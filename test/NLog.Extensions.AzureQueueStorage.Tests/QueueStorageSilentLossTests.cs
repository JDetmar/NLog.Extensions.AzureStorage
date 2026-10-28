using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using NLog.Extensions.AzureBlobStorage;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureQueueStorage.Tests
{
    // Regression test for S1: the cache-miss send path returned an un-unwrapped
    // Task<Task> (ContinueWith with an async lambda), so a first-write failure was
    // swallowed on the orphaned inner task and messages were silently lost.
    public class QueueStorageSilentLossTests
    {
        private static async Task<bool> AzuriteAvailableAsync(int port = 10001)
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
            // Point at a closed port so the first (cache-miss) send fails deterministically.
            const string deadEndpoint =
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
                "QueueEndpoint=http://127.0.0.1:1/devstoreaccount1;";

            var service = new QueueStorageTarget.CloudQueueService();
            service.Connect(deadEndpoint, null, null, null, null, null, null, null, null, null, null, null);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.AddMessageAsync("nlogqueue", "hello", null, CancellationToken.None));
        }

        // Queue metadata is rendered from the event that creates the queue, so a per-event QueueName gets
        // per-queue metadata (it used to be rendered once against an empty event, so it was never set).
        // A value Azure rejects (non-ASCII, trailing whitespace) is repaired instead of losing the message.
        [SkippableFact]
        public async Task QueueMetadata_RenderedPerCreatedQueue()
        {
            // integration test: requires Azurite on 127.0.0.1:10001 -> report as skipped, not passed, when absent
            Skip.IfNot(await AzuriteAvailableAsync(), "Azurite not reachable on 127.0.0.1:10001");

            await WriteToAzuriteQueuesAsync(
                target => target.QueueMetadata.Add(new TargetPropertyWithContext("app", "${event-properties:App}")),
                async queues =>
                {
                    Assert.Equal("billing", (await queues["billing"].GetPropertiesAsync()).Value.Metadata["app"]);
                    Assert.Equal("Jos_", (await queues["jos"].GetPropertiesAsync()).Value.Metadata["app"]);
                });
        }

        // Forwards to Azurite, but rejects a queue create carrying metadata the way Azure does for an invalid
        // metadata name or size (Azurite's queue service doesn't enforce either).
        private sealed class RejectMetadataOnCreateHandler : DelegatingHandler
        {
            public int RejectedCount;

            public RejectMetadataOnCreateHandler() : base(new HttpClientHandler { UseProxy = false }) { }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Put && request.Headers.Any(h => h.Key.StartsWith("x-ms-meta-", StringComparison.OrdinalIgnoreCase)))
                {
                    Interlocked.Increment(ref RejectedCount);
                    var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { RequestMessage = request };
                    response.Headers.Add("x-ms-error-code", "InvalidMetadata");
                    return Task.FromResult(response);
                }
                return base.SendAsync(request, cancellationToken);
            }
        }

        // Metadata Azure rejects for reasons value cleanup can't fix (invalid name, size limit)
        // must not lose the message: the queue is created without it.
        [SkippableFact]
        public async Task RejectedMetadata_QueueCreatedWithoutIt_MessageNotLost()
        {
            // integration test: requires Azurite on 127.0.0.1:10001 -> report as skipped, not passed, when absent
            Skip.IfNot(await AzuriteAvailableAsync(), "Azurite not reachable on 127.0.0.1:10001");

            const string devStorage = "UseDevelopmentStorage=true";
            var queue = new QueueServiceClient(devStorage).GetQueueClient("nlog" + Guid.NewGuid().ToString("n")); // unique -> no cross-run accumulation
            var handler = new RejectMetadataOnCreateHandler();
            var service = new QueueStorageTarget.CloudQueueService();
            try
            {
                service.Connect(devStorage, null, null, null, null, null, null, null, null, null, null,
                    _ => new Dictionary<string, string> { ["app"] = "x" },
                    new ProxySettings { Address = "http://127.0.0.1:8888", ProxyHttpMessageHandler = handler });

                await service.AddMessageAsync(queue.Name, "hello", null, CancellationToken.None);

                Assert.Equal(1, handler.RejectedCount);
                Assert.Empty((await queue.GetPropertiesAsync()).Value.Metadata);
                Assert.Equal("hello", (await queue.ReceiveMessageAsync()).Value?.MessageText);
            }
            finally
            {
                service.Dispose();
                await queue.DeleteIfExistsAsync();
            }
        }

        private static async Task WriteToAzuriteQueuesAsync(Action<QueueStorageTarget> configure, Func<IDictionary<string, QueueClient>, Task> assertQueues)
        {
            const string devStorage = "UseDevelopmentStorage=true";
            var prefix = "nlog" + Guid.NewGuid().ToString("n").Substring(0, 12); // unique -> no cross-run accumulation
            var service = new QueueServiceClient(devStorage);
            var queues = new Dictionary<string, QueueClient>
            {
                ["billing"] = service.GetQueueClient(prefix + "-billing"),
                ["jos"] = service.GetQueueClient(prefix + "-jos"),
            };
            try
            {
                var logFactory = new LogFactory();
                var logConfig = new NLog.Config.LoggingConfiguration(logFactory);
                var target = new QueueStorageTarget { ConnectionString = devStorage, QueueName = prefix + "-${event-properties:Queue}", Layout = "${message}" };
                configure(target);
                logConfig.AddRuleForAllLevels(target);
                logFactory.Configuration = logConfig;

                var logger = logFactory.GetLogger("Test");
                logger.WithProperty("Queue", "billing").WithProperty("App", "billing").Info("from billing");
                logger.WithProperty("Queue", "jos").WithProperty("App", "José ").Info("from jose");
                logFactory.Flush();
                logFactory.Shutdown();

                Assert.Equal("from billing", (await queues["billing"].ReceiveMessageAsync()).Value?.MessageText);
                Assert.Equal("from jose", (await queues["jos"].ReceiveMessageAsync()).Value?.MessageText);
                await assertQueues(queues);
            }
            finally
            {
                foreach (var queue in queues.Values)
                    await queue.DeleteIfExistsAsync();
            }
        }
    }
}
