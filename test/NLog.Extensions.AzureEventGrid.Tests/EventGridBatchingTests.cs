using System.Linq;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureEventGrid.Tests
{
    // A flush of N events used to send N separate HTTP requests, because the target overrode only the
    // single-event WriteAsyncTask and the base IList override chains one SendEventAsync per event. With the
    // IList override + batched SendEventsAsync, the whole flush is one request. Reverting the IList override
    // makes SendCallCount jump back to N and fails these tests.
    public class EventGridBatchingTests
    {
        private static (LogFactory factory, EventGridServiceMock svc, Logger logger) Build(bool cloudEvents)
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["TopicUrl"] = nameof(EventGridBatchingTests);
            var svc = new EventGridServiceMock();
            var target = new EventGridTarget(svc)
            {
                Topic = "${var:TopicUrl}",
                Layout = "${message}",
                BatchSize = 100000,          // deliver the whole flush in one WriteAsyncTask call
                TaskDelayMilliseconds = 1,
            };
            if (cloudEvents)
                target.CloudEventSource = "${logger}";
            else
                target.GridEventSubject = "${logger}";
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;
            return (logFactory, svc, logFactory.GetLogger(nameof(EventGridBatchingTests)));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(50)]
        [InlineData(250)]
        public void BatchedGridEvents_SendOneRequest(int count)
        {
            var (factory, svc, logger) = Build(cloudEvents: false);

            for (int i = 0; i < count; ++i)
                logger.Info("m-" + i);
            factory.Flush();

            Assert.Equal(1, svc.SendCallCount);              // one batched request, not N
            Assert.Equal(count, svc.GridEvents.Count);       // every event delivered
            Assert.Empty(svc.CloudEvents);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(50)]
        [InlineData(250)]
        public void BatchedCloudEvents_SendOneRequest(int count)
        {
            var (factory, svc, logger) = Build(cloudEvents: true);

            for (int i = 0; i < count; ++i)
                logger.Info("m-" + i);
            factory.Flush();

            Assert.Equal(1, svc.SendCallCount);              // one batched request, not N
            Assert.Equal(count, svc.CloudEvents.Count);      // every event delivered
            Assert.Empty(svc.GridEvents);
        }
    }
}
