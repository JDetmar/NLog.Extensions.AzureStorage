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
    }
}
