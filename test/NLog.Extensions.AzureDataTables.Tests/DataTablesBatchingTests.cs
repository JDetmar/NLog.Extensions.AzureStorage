using System.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // A partition with more than 100 events is split into multiple <=100-entity transactions. The old
    // split indexed the IList with Skip(i).Take(n) (O(n^2) on frameworks whose Enumerable.Skip is not
    // IList-optimized, e.g. netstandard2.0); the fix slices by index (O(n) everywhere). The split must
    // remain correct: every event delivered, each batch a valid <=100-entity transaction.
    public class DataTablesBatchingTests
    {
        private const string Table = "BatchTbl";

        private static (LogFactory factory, CloudTableServiceMock svc, Logger logger) Build()
        {
            var logFactory = new LogFactory();
            var logConfig = new LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesBatchingTests);
            var svc = new CloudTableServiceMock();
            var target = new DataTablesTarget(svc)
            {
                ConnectionString = "${var:ConnectionString}",
                TableName = "${logger}",
                Layout = "${message}",
                BatchSize = 100000,          // deliver the whole flush in one WriteAsyncTask call (>100 path)
                TaskDelayMilliseconds = 1,
            };
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;
            return (logFactory, svc, logFactory.GetLogger(Table));
        }

        [Theory]
        [InlineData(101)]
        [InlineData(150)]
        [InlineData(250)]
        [InlineData(1001)]
        public void PartitionOver100_SplitsIntoValidSubBatches_WithNoLoss(int count)
        {
            var (factory, svc, logger) = Build();

            for (int i = 0; i < count; ++i)
                logger.Info("m-" + i);
            factory.Flush();

            var batches = svc.GetBatches(Table);
            int expectedBatches = (count + 99) / 100;

            Assert.Equal(count, batches.Sum(b => b.Count));                  // nothing lost
            Assert.Equal(count, svc.PeekAllAdded(Table).Count());            // same total via the flattened view
            Assert.Equal(expectedBatches, batches.Count);                    // chunked into <=100 batches
            Assert.All(batches, b => Assert.InRange(b.Count, 1, 100));       // each a valid atomic transaction
        }
    }
}
