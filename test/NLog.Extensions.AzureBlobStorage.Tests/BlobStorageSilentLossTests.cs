using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureBlobStorage.Tests
{
    // Regression tests for S1: the cache-miss write path used
    //     InitializeAndCacheBlobAsync(...).ContinueWith(async (t, s) => await t.Result.AppendBlockAsync(...))
    // which returns an un-unwrapped Task<Task>. The returned task completes as soon
    // as the inner append is *created*, and any failure lands on the orphaned inner
    // task -> exceptions were swallowed and logs silently lost.
    public class BlobStorageSilentLossTests
    {
        private static void Connect(BlobStorageTarget.CloudBlobService service, string connectionString)
        {
            service.Connect(connectionString, null, null, null, null, null, null, null, null, null, null, null, null);
        }

        private static async Task<bool> AzuriteAvailableAsync(int port = 10000)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await client.ConnectAsync("127.0.0.1", port, cts.Token).ConfigureAwait(false);
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        [Fact]
        public async Task FirstWriteFailure_FaultsReturnedTask_NotSilentlySwallowed()
        {
            // Point at a closed port so the first (cache-miss) append fails deterministically.
            const string deadEndpoint =
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
                "BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;";

            var service = new BlobStorageTarget.CloudBlobService();
            Connect(service, deadEndpoint);

            // Before the fix this completes successfully (the failure is swallowed on the
            // orphaned inner task); after the fix the returned task faults.
            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.AppendFromByteArrayAsync("nlogcontainer", "test.log", "text/plain",
                    Encoding.UTF8.GetBytes("hello"), null, CancellationToken.None));
        }

        [SkippableFact]
        public async Task HappyPath_AppendWritesBlobContent()
        {
            // integration test: requires Azurite on 127.0.0.1:10000 -> report as skipped, not passed, when absent
            Skip.IfNot(await AzuriteAvailableAsync(), "Azurite not reachable on 127.0.0.1:10000");

            const string devStorage = "UseDevelopmentStorage=true";
            var container = "nlog" + Guid.NewGuid().ToString("n"); // unique -> no cross-run accumulation

            var service = new BlobStorageTarget.CloudBlobService();
            Connect(service, devStorage);

            await service.AppendFromByteArrayAsync(container, "test.log", "text/plain",
                Encoding.UTF8.GetBytes("Hello S1"), null, CancellationToken.None);

            var verify = new BlobServiceClient(devStorage)
                .GetBlobContainerClient(container)
                .GetBlobClient("test.log");
            var downloaded = await verify.DownloadContentAsync();
            Assert.Equal("Hello S1", downloaded.Value.Content.ToString());

            // cleanup
            await new BlobServiceClient(devStorage).GetBlobContainerClient(container).DeleteIfExistsAsync();
        }

        // Metadata/tags are rendered from the first event that creates the blob. A rendered value
        // Azure rejects (tag chars outside its allowed set, non-ASCII metadata header) used to fail
        // CreateIfNotExists and lose the whole batch, clean events included.
        [SkippableFact]
        public async Task InvalidRenderedTagAndMetadataValues_DoNotLoseTheBatch()
        {
            // integration test: requires Azurite on 127.0.0.1:10000 -> report as skipped, not passed, when absent
            Skip.IfNot(await AzuriteAvailableAsync(), "Azurite not reachable on 127.0.0.1:10000");

            const string devStorage = "UseDevelopmentStorage=true";
            var container = "nlog" + Guid.NewGuid().ToString("n"); // unique -> no cross-run accumulation

            var logFactory = new LogFactory();
            var logConfig = new NLog.Config.LoggingConfiguration(logFactory);
            var target = new BlobStorageTarget { ConnectionString = devStorage, Container = container, BlobName = "app.log", Layout = "${message}" };
            target.BlobTags.Add(new TargetPropertyWithContext("Source", "${logger}"));
            target.BlobMetadata.Add(new TargetPropertyWithContext("User", "${event-properties:item=User}"));
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("MyApp.Repo<Order>").WithProperty("User", "José").Info("bad-first");
            logFactory.GetLogger("MyApp.Good").WithProperty("User", "jose").Info("good");
            logFactory.Flush();
            logFactory.Shutdown();

            var blob = new BlobServiceClient(devStorage).GetBlobContainerClient(container).GetBlobClient("app.log");
            var downloaded = await blob.DownloadContentAsync();
            Assert.Equal("bad-first" + Environment.NewLine + "good" + Environment.NewLine, downloaded.Value.Content.ToString());
            Assert.Equal("MyApp.Repo_Order_", (await blob.GetTagsAsync()).Value.Tags["Source"]);
            Assert.Equal("Jos_", (await blob.GetPropertiesAsync()).Value.Metadata["User"]);

            // cleanup
            await new BlobServiceClient(devStorage).GetBlobContainerClient(container).DeleteIfExistsAsync();
        }
    }
}
