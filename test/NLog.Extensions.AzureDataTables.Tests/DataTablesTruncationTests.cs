using System.Linq;
using Azure.Data.Tables;
using NLog.Extensions.AzureStorage;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // Azure Table string properties may hold up to ColumnStringValueMaxSize (32768)
    // UTF-16 chars. A value of exactly that length is legal and must NOT be truncated;
    // a longer value must be capped at exactly the max (not max-1).
    public class DataTablesTruncationTests
    {
        private const int MaxSize = 32768; // DataTablesTarget.ColumnStringValueMaxSize

        [Fact]
        public void ContextProperty_ExactlyMaxSize_IsNotTruncated()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesTruncationTests);
            var cloudTableService = new CloudTableServiceMock();
            var target = new DataTablesTarget(cloudTableService);
            target.ConnectionString = "${var:ConnectionString}";
            target.TableName = "${logger}";
            target.Layout = "${message}";
            target.ContextProperties.Add(new TargetPropertyWithContext("Big", new string('x', MaxSize)));
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("Test").Info("hello");
            logFactory.Flush();

            var entity = cloudTableService.PeekLastAdded("Test").Cast<TableEntity>().First();
            Assert.Equal(MaxSize, entity["Big"].ToString().Length);
        }

        [Fact]
        public void ContextProperty_AboveMaxSize_IsCappedAtMax()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesTruncationTests);
            var cloudTableService = new CloudTableServiceMock();
            var target = new DataTablesTarget(cloudTableService);
            target.ConnectionString = "${var:ConnectionString}";
            target.TableName = "${logger}";
            target.Layout = "${message}";
            target.ContextProperties.Add(new TargetPropertyWithContext("Big", new string('x', MaxSize + 5000)));
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("Test").Info("hello");
            logFactory.Flush();

            var entity = cloudTableService.PeekLastAdded("Test").Cast<TableEntity>().First();
            Assert.Equal(MaxSize, entity["Big"].ToString().Length);
        }

        [Fact]
        public void Message_ExactlyMaxSize_IsNotTruncated()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesTruncationTests);
            var cloudTableService = new CloudTableServiceMock();
            var target = new DataTablesTarget(cloudTableService); // no context properties -> NLogEntity path
            target.ConnectionString = "${var:ConnectionString}";
            target.TableName = "${logger}";
            target.Layout = "${message}";
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("Test").Info(new string('y', MaxSize));
            logFactory.Flush();

            var entity = cloudTableService.PeekLastAdded("Test").Cast<NLogEntity>().First();
            Assert.Equal(MaxSize, entity.FullMessage.Length);
        }
    }
}
