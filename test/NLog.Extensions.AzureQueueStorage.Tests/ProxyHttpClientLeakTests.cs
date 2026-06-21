using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog.Extensions.AzureBlobStorage;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureQueueStorage.Tests
{
    // Regression tests for S5 ("ProxyHelpers HttpClient leak") — see the BlobStorage variant for the
    // full description. The proxy HttpClientTransport (and the HttpClient/handler it owns) must be
    // disposed when the service is closed (target CloseTarget) or its client is replaced (reconnect).
    public class ProxyHttpClientLeakTests
    {
        private const string DevConnectionString =
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;";

        private sealed class TrackingHttpMessageHandler : HttpMessageHandler
        {
            public int DisposeCount;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    DisposeCount++;
                base.Dispose(disposing);
            }
        }

        private static ProxySettings ProxyWith(TrackingHttpMessageHandler handler) =>
            new ProxySettings { Address = "http://127.0.0.1:8888", ProxyHttpMessageHandler = handler };

        [Fact]
        public void CloudQueueService_DisposesProxyTransport_WhenClosed()
        {
            var handler = new TrackingHttpMessageHandler();
            var service = new QueueStorageTarget.CloudQueueService();
            service.Connect(DevConnectionString, null, null, null, null, null, null, null, null, null, null, null, ProxyWith(handler));

            Assert.Equal(0, handler.DisposeCount);

            service.Dispose();

            Assert.Equal(1, handler.DisposeCount);
        }

        [Fact]
        public void CloudQueueService_DisposesPreviousTransport_OnReconnect()
        {
            var first = new TrackingHttpMessageHandler();
            var second = new TrackingHttpMessageHandler();
            var service = new QueueStorageTarget.CloudQueueService();

            service.Connect(DevConnectionString, null, null, null, null, null, null, null, null, null, null, null, ProxyWith(first));
            service.Connect(DevConnectionString, null, null, null, null, null, null, null, null, null, null, null, ProxyWith(second));

            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(0, second.DisposeCount);

            service.Dispose();
            Assert.Equal(1, second.DisposeCount);
        }

        [Fact]
        public void QueueStorageTarget_DisposesService_OnReconfiguration()
        {
            var logFactory = new LogFactory();
            var service = new CloudQueueServiceMock();
            var target = new QueueStorageTarget(service)
            {
                ConnectionString = nameof(ProxyHttpClientLeakTests),
                QueueName = "${logger}",
                Layout = "${message}",
            };
            var config = new Config.LoggingConfiguration(logFactory);
            config.AddRuleForAllLevels(target);
            logFactory.Configuration = config;
            logFactory.GetLogger("Test").Info("Hello World");
            logFactory.Flush();

            Assert.Equal(0, service.DisposeCount);

            logFactory.Configuration = new Config.LoggingConfiguration(logFactory);

            Assert.True(service.DisposeCount >= 1);
        }
    }
}
