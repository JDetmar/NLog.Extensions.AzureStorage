using System;
using System.IO;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureServiceBus.Test
{
    /// <summary>
    /// Regression tests for the S5 shutdown-teardown behaviour of <see cref="ServiceBusTarget"/>:
    /// the connection close (sender close + client dispose) must be observed (never a silent
    /// unobserved task exception), must not propagate out of shutdown, and a legitimately slow
    /// close must be awaited rather than abandoned by an over-tight timeout (which would also leak
    /// the undisposed client). Events must be flushed before the connection is closed.
    /// </summary>
    [Collection("InternalLogger isolation")]
    public class ServiceBusShutdownTests
    {
        private static (LogFactory, RecordingCloudServiceBus) BuildTarget(int taskDelayMs = 1)
        {
            var svc = new RecordingCloudServiceBus();
            var logFactory = new LogFactory();
            var logConfig = new LoggingConfiguration(logFactory);
            var target = new ServiceBusTarget(svc)
            {
                ConnectionString = "LocalServiceBus",
                QueueName = "q",
                PartitionKey = "${logger}",
                Layout = "${message}",
                TaskDelayMilliseconds = taskDelayMs,
                BatchSize = 1000,
            };
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;
            return (logFactory, svc);
        }

        [Fact]
        public void Shutdown_FlushesQueuedEventsBeforeClosingConnection()
        {
            var (logFactory, svc) = BuildTarget(taskDelayMs: 10000);
            var logger = logFactory.GetLogger("P");
            for (int i = 0; i < 5; ++i)
                logger.Info("msg" + i);

            logFactory.Shutdown();

            Assert.Equal(5, svc.SendCount);             // nothing dropped on shutdown
            Assert.Equal(0, svc.SendAfterCloseCount);   // nothing sent over a closed connection
            Assert.Equal(1, svc.CloseCompletedCount);
        }

        [Fact]
        public void Shutdown_WhenCloseAsyncThrows_IsLoggedAndDoesNotPropagate()
        {
            var buffer = new StringWriter();
            var prevWriter = InternalLogger.LogWriter;
            var prevLevel = InternalLogger.LogLevel;
            InternalLogger.LogWriter = TextWriter.Synchronized(buffer);
            InternalLogger.LogLevel = LogLevel.Trace;
            try
            {
                var (logFactory, svc) = BuildTarget();
                svc.CloseDelay = TimeSpan.FromMilliseconds(900); // faults well after the old 500ms cap
                svc.CloseThrows = true;
                logFactory.GetLogger("P").Info("hello");
                logFactory.Flush();

                var ex = Record.Exception(() => logFactory.Shutdown());

                Assert.Null(ex);   // shutdown must never throw because the connection close failed
            }
            finally
            {
                InternalLogger.LogWriter = prevWriter;
                InternalLogger.LogLevel = prevLevel;
            }

            string internalLog;
            lock (buffer)
                internalLog = buffer.ToString();
            Assert.Contains("ServiceBusClient connection during shutdown", internalLog, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Shutdown_SlowCloseIsAwaitedNotAbandoned()
        {
            var (logFactory, svc) = BuildTarget();
            svc.CloseDelay = TimeSpan.FromMilliseconds(800); // slower than the old 500ms cap, well within the new bound
            logFactory.GetLogger("P").Info("hello");
            logFactory.Flush();

            logFactory.Shutdown();

            // Both the sender close and the client dispose must run to completion during shutdown
            // rather than being abandoned mid-flight (an abandoned DisposeAsync leaks the client).
            Assert.True(svc.ConnectionClosed, "CloseAsync was abandoned before it completed");
            Assert.True(svc.DisposeCompleted, "ServiceBusClient was not disposed (dispose abandoned)");
            Assert.Equal(1, svc.CloseCompletedCount);
        }
    }
}
