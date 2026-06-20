using Azure.Messaging.ServiceBus;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureServiceBus.Test
{
    // #5: CreateMessageBatch estimates the batch byte-size as EstimateEventDataSize(maxBody) * count
    // using int arithmetic. For a large flush this overflows to a NEGATIVE value, which makes
    // CalculateBatchSize take the "small total" branch (up to 100 messages/batch) instead of
    // splitting. The result is multi-MB batches that exceed the Service Bus limit and are rejected.
    public class ServiceBusBatchSizeTests
    {
        private const int MaxBatch = 256 * 1024; // ServiceBusTarget.MaxBatchSizeBytes default

        [Fact]
        public void LargeFlushSizeEstimateDoesNotOverflow()
        {
            const int maxBodyBytes = 200_000; // ~200KB messages (each individually under the 256KB limit)
            const int count = 20_000;         // a large flush

            // The true byte estimate (~12 GB) is far beyond a single 256KB batch -> the flush must split.
            long trueTotal = (long)ServiceBusTarget.EstimateEventDataSize(maxBodyBytes) * count;
            Assert.True(trueTotal > MaxBatch, "sanity: this flush genuinely needs many batches");

            long estimate = ServiceBusTarget.EstimateBatchSizeBytes(maxBodyBytes, count);
            Assert.True(estimate > 0, $"size estimate overflowed to {estimate} (negative) for a large flush");
            Assert.Equal(trueTotal, estimate); // long arithmetic must keep the full magnitude (no overflow, no cap)
        }

        [Fact]
        public void LargeFlushIsSplitIntoSmallBatches()
        {
            const int maxBodyBytes = 200_000;
            const int count = 20_000;

            var target = new ServiceBusTarget { MaxBatchSizeBytes = MaxBatch };
            var messages = new ServiceBusMessage[count]; // CalculateBatchSize only reads .Count
            long estimate = ServiceBusTarget.EstimateBatchSizeBytes(maxBodyBytes, count);

            int perBatch = target.CalculateBatchSize(messages, estimate);

            // Overflow -> negative estimate -> CalculateBatchSize returns Math.Min(count, 100) = 100,
            // so each send packs ~100 x 200KB = ~20MB >> 256KB and is rejected. The fix must split small.
            Assert.True(perBatch < 100, $"a {count}-message flush was placed into batches of {perBatch} (the overflow took the 'small total' path -> oversized batches)");
        }
    }
}
