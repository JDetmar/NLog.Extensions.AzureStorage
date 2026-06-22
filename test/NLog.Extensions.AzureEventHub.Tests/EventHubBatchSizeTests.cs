using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Azure.Messaging.EventHubs;
using NLog.Common;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureEventHub.Test
{
    // S3 (full): the target now splits a flush into batches with the SDK's native EventDataBatch +
    // TryAdd (driven here by EventHubServiceMock.FakeEventDataBatch, which caps a batch at
    // MaxBatchSizeBytes). These tests prove:
    //   1. a payload just over the limit splits into ~2 batches, not the >=10 the old count-based
    //      heuristic produced;
    //   2. a large flush is delivered in full, with every batch within the size cap and a sane
    //      batch count (no int-overflow, no oversized batch);
    //   3. an oversize event is NOT silently swallowed as a whole chunk: its siblings are still
    //      sent, only the single undeliverable event is dropped, and that drop is logged.
    [CollectionDefinition("InternalLogger isolation", DisableParallelization = true)]
    public class InternalLoggerIsolationCollection { }

    [Collection("InternalLogger isolation")]
    public class EventHubBatchSizeTests
    {
        private static EventHubServiceMock CreateTarget(int maxBatchSizeBytes, out LogFactory logFactory, out Logger logger)
        {
            logFactory = new LogFactory();
            var logConfig = new LoggingConfiguration(logFactory);
            var eventHubServiceMock = new EventHubServiceMock();
            var target = new EventHubTarget(eventHubServiceMock)
            {
                ConnectionString = "LocalEventHub",
                EventHubName = "${shortdate}",
                Layout = "${message}",
                MaxBatchSizeBytes = maxBatchSizeBytes,
                BatchSize = 10000,                                              // deliver the flush in one WriteAsyncTask call
                OverflowAction = Targets.Wrappers.AsyncTargetWrapperOverflowAction.Grow,
                TaskDelayMilliseconds = 1,
            };
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;
            logger = logFactory.GetLogger(nameof(EventHubBatchSizeTests));
            return eventHubServiceMock;
        }

        private static List<List<EventData>> SnapshotBatches(EventHubServiceMock mock)
        {
            lock (mock.EventDataSent)
                return mock.BatchesSent.Select(b => new List<EventData>(b.Value)).ToList();
        }

        [Fact]
        public void PayloadJustOverLimit_SplitsIntoFewBatches_NotMany()
        {
            const int cap = 1000;
            var mock = CreateTarget(cap, out var logFactory, out var logger);

            // 5 x 300 bytes = 1500 bytes, just over a 1000-byte cap -> needs 2 batches (3 + 2).
            for (int i = 0; i < 5; ++i)
                logger.Info(new string('a', 300));
            logFactory.Flush();

            var batches = SnapshotBatches(mock);
            int totalSent = batches.Sum(b => b.Count);

            Assert.Equal(5, totalSent);                                        // nothing lost
            Assert.True(batches.Count >= 2 && batches.Count <= 3,
                $"expected ~2 batches for a payload just over the limit, got {batches.Count} (old count-based math produced >=10)");
            Assert.All(batches, b => Assert.True(BodyBytes(b) <= cap, $"batch of {BodyBytes(b)} bytes exceeds the {cap}-byte cap"));
        }

        [Fact]
        public void LargeFlush_DeliveredInFull_WithSaneBatchCountAndNoOversizedBatch()
        {
            const int cap = 4096;
            const int count = 1000;
            const int bodyBytes = 500;                                         // 8 events per 4096-byte batch
            var mock = CreateTarget(cap, out var logFactory, out var logger);

            for (int i = 0; i < count; ++i)
                logger.Info(new string('x', bodyBytes));
            logFactory.Flush();

            var batches = SnapshotBatches(mock);
            int totalSent = batches.Sum(b => b.Count);

            Assert.Equal(count, totalSent);                                    // no overflow, no whole-chunk drop
            Assert.All(batches, b => Assert.True(BodyBytes(b) <= cap, $"batch of {BodyBytes(b)} bytes exceeds the {cap}-byte cap"));
            // ~125 batches expected. Old math over-split to one-event-per-batch (~1000).
            Assert.True(batches.Count > 1 && batches.Count <= count / 4,
                $"expected a sane batch count near {count * bodyBytes / cap}, got {batches.Count}");
        }

        [Fact]
        public void OversizeEvent_IsDroppedAndLogged_SiblingsStillSent()
        {
            const int cap = 1000;
            var mock = CreateTarget(cap, out var logFactory, out var logger);

            var originalWriter = InternalLogger.LogWriter;
            var originalLevel = InternalLogger.LogLevel;
            var capture = new StringWriter();
            InternalLogger.LogWriter = capture;
            InternalLogger.LogLevel = LogLevel.Error;
            try
            {
                logger.Info(new string('a', 300));
                logger.Info(new string('b', 300));
                logger.Info(new string('g', 5000));     // larger than the 1000-byte cap -> undeliverable on its own
                logger.Info(new string('c', 300));
                logger.Info(new string('d', 300));
                logFactory.Flush();
            }
            finally
            {
                InternalLogger.LogWriter = originalWriter;
                InternalLogger.LogLevel = originalLevel;
            }

            var batches = SnapshotBatches(mock);
            var delivered = batches.SelectMany(b => b).Select(e => Encoding.UTF8.GetString(e.Body.ToArray())).ToList();

            Assert.Equal(4, delivered.Count);                                  // the 4 siblings still send
            Assert.DoesNotContain(delivered, body => body.Length == 5000);     // the oversize one is gone
            Assert.All(delivered, body => Assert.Equal(300, body.Length));
            Assert.All(batches, b => Assert.True(BodyBytes(b) <= cap));
            // Dropped distinctly, not lost silently and not swallowed as a whole chunk.
            Assert.Contains("Dropped 1 logevents", capture.ToString());
        }

        private static int BodyBytes(IEnumerable<EventData> batch)
        {
            return batch.Sum(e => e.Body.Length);
        }
    }
}
