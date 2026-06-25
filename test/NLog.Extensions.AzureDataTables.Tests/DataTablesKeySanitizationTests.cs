using System.Linq;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // Azure rejects an entire table transaction if any PartitionKey/RowKey contains a
    // forbidden character (/ \ # ?) or a control char -- losing every (valid) log entry
    // in that batch (proven in KeyCharacterRulesTests against Azurite). The target must
    // therefore sanitize rendered keys before building the entity.
    public class DataTablesKeySanitizationTests
    {
        [Fact]
        public void ForbiddenCharactersInKeys_AreSanitized()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesKeySanitizationTests);
            var svc = new CloudTableServiceMock();
            var target = new DataTablesTarget(svc);
            target.ConnectionString = "${var:ConnectionString}";
            target.TableName = "${logger}";
            target.PartitionKey = "pk/with#bad";   // '/' and '#' are forbidden in keys
            target.RowKey = "row?with\\bad";        // '?' and '\' are forbidden in keys
            target.Layout = "${message}";
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("Test").Info("hi");
            logFactory.Flush();

            var entity = svc.PeekLastAdded("Test").First();
            foreach (var bad in new[] { "/", "\\", "#", "?" })
            {
                Assert.DoesNotContain(bad, entity.PartitionKey);
                Assert.DoesNotContain(bad, entity.RowKey);
            }
        }

        [Fact]
        public void OverLongKeys_AreTruncatedTo512()
        {
            // Azure caps PartitionKey/RowKey at 512 chars (the "1 KiB" UTF-16 limit); an over-long key
            // makes Azure reject the whole transaction, losing every entry in the batch (proven against
            // Azurite in KeyLengthRulesTests). The target must truncate rendered keys so they fit.
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesKeySanitizationTests);
            var svc = new CloudTableServiceMock();
            var target = new DataTablesTarget(svc);
            target.ConnectionString = "${var:ConnectionString}";
            target.TableName = "${logger}";
            target.PartitionKey = new string('p', 600);   // longer than the 512 cap
            target.RowKey = new string('r', 600);          // longer than the 512 cap
            target.Layout = "${message}";
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("Test").Info("hi");
            logFactory.Flush();

            var entity = svc.PeekLastAdded("Test").First();
            Assert.Equal(512, entity.PartitionKey.Length);
            Assert.Equal(512, entity.RowKey.Length);
        }

        [Fact]
        public void MaxLengthKeys_ArePreserved()
        {
            // A key of exactly 512 chars is valid and must NOT be truncated (no off-by-one).
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesKeySanitizationTests);
            var svc = new CloudTableServiceMock();
            var target = new DataTablesTarget(svc);
            target.ConnectionString = "${var:ConnectionString}";
            target.TableName = "${logger}";
            target.PartitionKey = new string('p', 512);
            target.RowKey = new string('r', 512);
            target.Layout = "${message}";
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("Test").Info("hi");
            logFactory.Flush();

            var entity = svc.PeekLastAdded("Test").First();
            Assert.Equal(512, entity.PartitionKey.Length);
            Assert.Equal(512, entity.RowKey.Length);
        }
    }
}
