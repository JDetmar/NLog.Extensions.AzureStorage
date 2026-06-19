using Azure.Messaging.EventHubs;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureEventHub.Test
{
    // #5: CreateEventDataBatch estimates the batch byte-size as EstimateEventDataSize(maxBody) * count
    // using int arithmetic. For a large flush this overflows to a NEGATIVE value, which makes
    // CalculateBatchSize take the "small total" branch (up to 100 events/batch) instead of splitting,
    // producing multi-MB batches that exceed the Event Hub limit and are rejected.
    public class EventHubBatchSizeTests
    {
        private const int MaxBatch = 1024 * 1024; // EventHubTarget.MaxBatchSizeBytes default

        [Fact]
        public void LargeFlush_SizeEstimate_DoesNotOverflowToNegative()
        {
            const int maxBodyBytes = 200_000;
            const int count = 20_000;

            long trueTotal = (long)EventHubTarget.EstimateEventDataSize(maxBodyBytes) * count;
            Assert.True(trueTotal > MaxBatch, "sanity: this flush genuinely needs many batches");

            int estimate = EventHubTarget.EstimateBatchSizeBytes(maxBodyBytes, count);
            Assert.True(estimate > 0, $"size estimate overflowed to {estimate} (negative) for a large flush");
        }

        [Fact]
        public void LargeFlush_IsSplitIntoSmallBatches_NotPackedIntoOversizedOnes()
        {
            const int maxBodyBytes = 200_000;
            const int count = 20_000;

            var target = new EventHubTarget { MaxBatchSizeBytes = MaxBatch };
            var events = new EventData[count]; // CalculateBatchSize only reads .Count
            int estimate = EventHubTarget.EstimateBatchSizeBytes(maxBodyBytes, count);

            int perBatch = target.CalculateBatchSize(events, estimate);

            Assert.True(perBatch < 100, $"a {count}-event flush was placed into batches of {perBatch} (the overflow took the 'small total' path -> oversized batches)");
        }
    }
}
