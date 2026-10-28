using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        // Azure rejects (tag chars outside its allowed set, non-ASCII metadata header, metadata with
        // trailing whitespace -> 403) used to fail CreateIfNotExists and lose the whole batch.
        [SkippableFact]
        public async Task InvalidRenderedTagAndMetadataValues_DoNotLoseTheBatch()
        {
            // integration test: requires Azurite on 127.0.0.1:10000 -> report as skipped, not passed, when absent
            Skip.IfNot(await AzuriteAvailableAsync(), "Azurite not reachable on 127.0.0.1:10000");

            await WriteMixedBatchToAzuriteAsync(
                target =>
                {
                    target.BlobTags.Add(new TargetPropertyWithContext("Source", "${logger}"));
                    target.BlobMetadata.Add(new TargetPropertyWithContext("User", "${event-properties:item=User}"));
                },
                async blob =>
                {
                    Assert.Equal("MyApp.Repo_Order_", (await blob.GetTagsAsync()).Value.Tags["Source"]);
                    Assert.Equal("Jos_", (await blob.GetPropertiesAsync()).Value.Metadata["User"]);
                });
        }

        // Metadata/tags Azure rejects for reasons value cleanup can't fix (here an invalid metadata name)
        // must not lose the batch: the blob is created without them.
        [SkippableFact]
        public async Task RejectedMetadataName_BlobCreatedWithoutProperties()
        {
            // integration test: requires Azurite on 127.0.0.1:10000 -> report as skipped, not passed, when absent
            Skip.IfNot(await AzuriteAvailableAsync(), "Azurite not reachable on 127.0.0.1:10000");

            await WriteMixedBatchToAzuriteAsync(
                target => target.BlobMetadata.Add(new TargetPropertyWithContext("app-name", "x")),
                async blob => Assert.Empty((await blob.GetPropertiesAsync()).Value.Metadata));
        }

        // Fake blob service (no Azurite, so it runs in CI): the container exists, the blob doesn't yet, a create
        // carrying metadata/tags gets the configured rejection, a create without them succeeds, appends succeed.
        private sealed class FakeBlobServiceHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _createWithPropertiesStatus;
            private readonly string _createWithPropertiesErrorCode;
            public readonly List<string> Requests = new List<string>();

            public FakeBlobServiceHandler(HttpStatusCode createWithPropertiesStatus, string createWithPropertiesErrorCode)
            {
                _createWithPropertiesStatus = createWithPropertiesStatus;
                _createWithPropertiesErrorCode = createWithPropertiesErrorCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var query = request.RequestUri.Query;
                var hasProperties = request.Headers.Any(h => h.Key.StartsWith("x-ms-meta-", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("x-ms-tags", StringComparison.OrdinalIgnoreCase));
                string kind = query.Contains("restype=container") ? "container-exists" : request.Method == HttpMethod.Head ? "blob-exists" : query.Contains("comp=appendblock") ? "append" : "create" + (hasProperties ? " +properties" : "");
                lock (Requests)
                    Requests.Add(kind);

                switch (kind)
                {
                    case "container-exists": return Respond(request, HttpStatusCode.OK, null);
                    case "blob-exists": return Respond(request, HttpStatusCode.NotFound, "BlobNotFound");
                    case "create +properties": return Respond(request, _createWithPropertiesStatus, _createWithPropertiesErrorCode);
                    default: return Respond(request, HttpStatusCode.Created, null);
                }
            }

            private static Task<HttpResponseMessage> Respond(HttpRequestMessage request, HttpStatusCode status, string errorCode)
            {
                var response = new HttpResponseMessage(status) { RequestMessage = request };
                if (errorCode != null)
                    response.Headers.Add("x-ms-error-code", errorCode);
                return Task.FromResult(response);
            }
        }

        // Metadata/tags Azure rejects for reasons value cleanup can't fix must not lose the batch: the blob is
        // created without them. Live Azure answers 400 MetadataTooLarge/InvalidMetadata/InvalidTag, and 403
        // AuthorizationPermissionMismatch for tags with a SAS lacking the 't' permission.
        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "MetadataTooLarge")]
        [InlineData(HttpStatusCode.Forbidden, "AuthorizationPermissionMismatch")]
        public async Task RejectedMetadataOrTags_BlobCreatedWithoutThem_BatchNotLost(HttpStatusCode status, string errorCode)
        {
            var handler = new FakeBlobServiceHandler(status, errorCode);
            using (var service = new BlobStorageTarget.CloudBlobService())
            {
                service.Connect("UseDevelopmentStorage=true", null, null, null, null, null, null, null, null, null,
                    _ => new Dictionary<string, string> { ["app"] = "x" },
                    _ => new Dictionary<string, string> { ["env"] = "prod" },
                    new ProxySettings { Address = "http://127.0.0.1:8888", ProxyHttpMessageHandler = handler });

                await service.AppendFromByteArrayAsync("nlogcontainer", "app.log", "text/plain", Encoding.UTF8.GetBytes("hello"), null, CancellationToken.None);
            }

            Assert.Equal(new[] { "container-exists", "blob-exists", "create +properties", "create", "append" }, handler.Requests);
        }

        private static async Task WriteMixedBatchToAzuriteAsync(Action<BlobStorageTarget> configure, Func<BlobClient, Task> assertBlob)
        {
            const string devStorage = "UseDevelopmentStorage=true";
            var container = new BlobServiceClient(devStorage).GetBlobContainerClient("nlog" + Guid.NewGuid().ToString("n")); // unique -> no cross-run accumulation
            try
            {
                var logFactory = new LogFactory();
                var logConfig = new NLog.Config.LoggingConfiguration(logFactory);
                var target = new BlobStorageTarget { ConnectionString = devStorage, Container = container.Name, BlobName = "app.log", Layout = "${message}" };
                configure(target);
                logConfig.AddRuleForAllLevels(target);
                logFactory.Configuration = logConfig;

                logFactory.GetLogger("MyApp.Repo<Order>").WithProperty("User", "José ").Info("bad-first");
                logFactory.GetLogger("MyApp.Good").WithProperty("User", "jose").Info("good");
                logFactory.Flush();
                logFactory.Shutdown();

                var blob = container.GetBlobClient("app.log");
                var downloaded = await blob.DownloadContentAsync();
                Assert.Equal("bad-first" + Environment.NewLine + "good" + Environment.NewLine, downloaded.Value.Content.ToString());
                await assertBlob(blob);
            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }
    }
}
