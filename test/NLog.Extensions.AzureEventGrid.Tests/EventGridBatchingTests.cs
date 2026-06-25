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
        private static (LogFactory factory, EventGridServiceMock svc, Logger logger) Build(bool cloudEvents, int maxBatchSizeBytes = 0)
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
                TaskDelayMilliseconds = 1000,  // long enough that the timer never flushes mid-loop; Flush() forces the send

            };
            if (maxBatchSizeBytes > 0)
                target.MaxBatchSizeBytes = maxBatchSizeBytes;
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

        // A flush whose combined payload exceeds the ~1MB request limit must be split across requests so it
        // is never rejected wholesale - and nothing may be lost in the split. EventGrid has no SDK batch
        // builder, so the target estimates size conservatively; here large events force more than one request.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OversizedFlush_SplitsAcrossRequests_NoLoss(bool cloudEvents)
        {
            const int count = 10;
            var (factory, svc, logger) = Build(cloudEvents, maxBatchSizeBytes: 7000);

            var bigMessage = new string('x', 2000);
            for (int i = 0; i < count; ++i)
                logger.Info(bigMessage);
            factory.Flush();

            var delivered = cloudEvents ? svc.CloudEvents.Count : svc.GridEvents.Count;
            Assert.True(svc.SendCallCount > 1, $"expected a split into multiple requests, got {svc.SendCallCount}");
            Assert.Equal(count, delivered);                  // nothing lost in the split
        }

        // EventGrid caps events per request; a flush of many tiny events (under the byte limit) must still be
        // chunked by count so no single request exceeds the cap.
        [Fact]
        public void ManyTinyEvents_SplitByEventCount_NoLoss()
        {
            const int count = 2500;
            var (factory, svc, logger) = Build(cloudEvents: false, maxBatchSizeBytes: int.MaxValue);  // isolate the count cap

            for (int i = 0; i < count; ++i)
                logger.Info("m-" + i);
            factory.Flush();

            Assert.True(svc.SendCallCount > 1, "expected multiple requests once the per-request event cap is hit");
            Assert.All(svc.SendBatchSizes, n => Assert.InRange(n, 1, 1000));   // no batched request exceeds the cap
            Assert.Equal(count, svc.GridEvents.Count);                         // nothing lost
        }

        // A single event larger than the cap can't be made to fit - but it must still be sent (alone), and it
        // must not drag its in-flush siblings down with it (the regression #202 guards against elsewhere).
        [Fact]
        public void SingleOversizedEvent_SentAlone_SiblingsStillDelivered()
        {
            var (factory, svc, logger) = Build(cloudEvents: false, maxBatchSizeBytes: 4000);

            logger.Info(new string('x', 8000));   // alone exceeds the cap
            logger.Info("small-1");
            logger.Info("small-2");
            factory.Flush();

            Assert.Equal(3, svc.GridEvents.Count);          // oversized event not dropped, siblings delivered
            Assert.True(svc.SendCallCount >= 2, "the oversized event should be isolated into its own request");
        }
    }
}
