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

        // Fake queue service (no Azurite, so it runs in CI): the queue doesn't exist yet, a create carrying
        // metadata gets the configured rejection, a create without metadata succeeds, sends succeed.
        private sealed class FakeQueueServiceHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _createWithMetadataStatus;
            private readonly string _createWithMetadataErrorCode;
            public readonly List<string> Requests = new List<string>();

            public FakeQueueServiceHandler(HttpStatusCode createWithMetadataStatus, string createWithMetadataErrorCode)
            {
                _createWithMetadataStatus = createWithMetadataStatus;
                _createWithMetadataErrorCode = createWithMetadataErrorCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var hasMetadata = request.Headers.Any(h => h.Key.StartsWith("x-ms-meta-", StringComparison.OrdinalIgnoreCase));
                lock (Requests)
                    Requests.Add(request.Method + (hasMetadata ? " +metadata" : ""));

                if (request.Method == HttpMethod.Get)   // ExistsAsync -> GetProperties
                    return Respond(request, HttpStatusCode.NotFound, "QueueNotFound");
                if (request.Method == HttpMethod.Put)   // CreateIfNotExistsAsync
                    return hasMetadata ? Respond(request, _createWithMetadataStatus, _createWithMetadataErrorCode) : Respond(request, HttpStatusCode.Created, null);

                var response = new HttpResponseMessage(HttpStatusCode.Created) { RequestMessage = request };   // SendMessageAsync
                response.Content = new StringContent("<?xml version=\"1.0\" encoding=\"utf-8\"?><QueueMessagesList><QueueMessage><MessageId>1</MessageId><InsertionTime>Mon, 01 Jan 2024 00:00:00 GMT</InsertionTime><ExpirationTime>Mon, 08 Jan 2024 00:00:00 GMT</ExpirationTime><PopReceipt>p</PopReceipt><TimeNextVisible>Mon, 01 Jan 2024 00:00:00 GMT</TimeNextVisible></QueueMessage></QueueMessagesList>", System.Text.Encoding.UTF8, "application/xml");
                return Task.FromResult(response);
            }

            private static Task<HttpResponseMessage> Respond(HttpRequestMessage request, HttpStatusCode status, string errorCode)
            {
                var response = new HttpResponseMessage(status) { RequestMessage = request };
                if (errorCode != null)
                    response.Headers.Add("x-ms-error-code", errorCode);
                return Task.FromResult(response);
            }
        }

        private static QueueStorageTarget.CloudQueueService ConnectWithMetadata(FakeQueueServiceHandler handler)
        {
            const string devStorage = "UseDevelopmentStorage=true";
            var service = new QueueStorageTarget.CloudQueueService();
            service.Connect(devStorage, null, null, null, null, null, null, null, null, null, null,
                _ => new Dictionary<string, string> { ["app"] = "x" },
                new ProxySettings { Address = "http://127.0.0.1:8888", ProxyHttpMessageHandler = handler });
            return service;
        }

        // Metadata Azure rejects for reasons value cleanup can't fix (invalid name, size limit; live Azure
        // answers 400 InvalidMetadata) must not lose the message: the queue is created without it.
        [Fact]
        public async Task RejectedMetadata_QueueCreatedWithoutIt_MessageNotLost()
        {
            var handler = new FakeQueueServiceHandler(HttpStatusCode.BadRequest, "InvalidMetadata");
            using (var service = ConnectWithMetadata(handler))
            {
                await service.AddMessageAsync("nlogqueue", "hello", null, CancellationToken.None);
            }

            Assert.Equal(new[] { "GET", "PUT +metadata", "PUT", "POST" }, handler.Requests);
        }

        // A 403 is a real auth/permission failure (queue metadata needs no extra permission), so it must
        // surface instead of a second, doomed create.
        [Fact]
        public async Task ForbiddenCreate_IsNotRetriedWithoutMetadata()
        {
            var handler = new FakeQueueServiceHandler(HttpStatusCode.Forbidden, "AuthorizationPermissionMismatch");
            using (var service = ConnectWithMetadata(handler))
            {
                await Assert.ThrowsAsync<Azure.RequestFailedException>(() => service.AddMessageAsync("nlogqueue", "hello", null, CancellationToken.None));
            }

            Assert.Equal(new[] { "GET", "PUT +metadata" }, handler.Requests);
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
