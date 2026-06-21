using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureBlobStorage.Tests
{
    // Regression tests for S5 ("ProxyHelpers HttpClient leak").
    //
    // ProxySettings.CreateHttpClientTransport() does `new HttpClientTransport(new HttpClient(handler))`
    // on every Connect, i.e. on every NLog (re)configuration of a proxy-enabled target.
    // HttpClientTransport owns that HttpClient/handler and is IDisposable, but the original code
    // discarded it (ConfigureClientOptions was static and kept no reference) so nothing ever disposed
    // it -> a socket/handler leak that accumulates across reconfigurations.
    //
    // The fix retains the transport on the service and disposes it when the service is closed
    // (the target's CloseTarget) or when the client is replaced (a subsequent Connect).
    public class ProxyHttpClientLeakTests
    {
        // Well-formed dev connection string; BlobServiceClient constructs lazily (no network I/O).
        private const string DevConnectionString =
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

        // Observes disposal of the HttpClient/handler that HttpClientTransport owns.
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

        // Address makes RequiresManualProxyConfiguration true so a transport is actually created;
        // ProxyHttpMessageHandler is the internal test-seam that lets us observe its disposal.
        private static ProxySettings ProxyWith(TrackingHttpMessageHandler handler) =>
            new ProxySettings { Address = "http://127.0.0.1:8888", ProxyHttpMessageHandler = handler };

        [Fact]
        public void CloudBlobService_DisposesProxyTransport_WhenClosed()
        {
            var handler = new TrackingHttpMessageHandler();
            var service = new BlobStorageTarget.CloudBlobService();
            service.Connect(DevConnectionString, null, null, null, null, null, null, null, null, null, null, null, ProxyWith(handler));

            Assert.Equal(0, handler.DisposeCount);  // leak: the proxy HttpClient is still alive after Connect

            service.Dispose();

            Assert.Equal(1, handler.DisposeCount);  // fix: closing the service disposes the transport's HttpClient/handler
        }

        [Fact]
        public void CloudBlobService_DisposesPreviousTransport_OnReconnect()
        {
            var first = new TrackingHttpMessageHandler();
            var second = new TrackingHttpMessageHandler();
            var service = new BlobStorageTarget.CloudBlobService();

            service.Connect(DevConnectionString, null, null, null, null, null, null, null, null, null, null, null, ProxyWith(first));
            service.Connect(DevConnectionString, null, null, null, null, null, null, null, null, null, null, null, ProxyWith(second));

            Assert.Equal(1, first.DisposeCount);   // the first transport was replaced on reconnect -> disposed
            Assert.Equal(0, second.DisposeCount);

            service.Dispose();
            Assert.Equal(1, second.DisposeCount);
        }

        [Fact]
        public void BlobStorageTarget_DisposesService_OnReconfiguration()
        {
            var logFactory = new LogFactory();
            var service = new CloudBlobServiceMock();
            var target = new BlobStorageTarget(service)
            {
                ConnectionString = nameof(ProxyHttpClientLeakTests),
                Container = "${level}",
                BlobName = "${logger}",
                Layout = "${message}",
            };
            var config = new Config.LoggingConfiguration(logFactory);
            config.AddRuleForAllLevels(target);
            logFactory.Configuration = config;
            logFactory.GetLogger("Test").Info("Hello World");
            logFactory.Flush();

            Assert.Equal(0, service.DisposeCount);

            // Reconfiguring closes the old target -> CloseTarget disposes the service (and its proxy transport).
            logFactory.Configuration = new Config.LoggingConfiguration(logFactory);

            Assert.True(service.DisposeCount >= 1);
        }
    }
}
