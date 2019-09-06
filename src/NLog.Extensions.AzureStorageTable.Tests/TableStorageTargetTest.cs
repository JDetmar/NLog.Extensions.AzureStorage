using System;
using System.Linq;
using Microsoft.Azure.Cosmos.Table;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureStorageTable.Tests
{
    public class TableStorageTargetTest
    {
        [Fact]
        public void SingleLogEventTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(TableStorageTargetTest);
            var cloudTableService = new CloudTableServiceMock();
            var queueStorageTarget = new TableStorageTarget(cloudTableService);
            queueStorageTarget.ConnectionString = "${var:ConnectionString}";
            queueStorageTarget.TableName = "${logger}";
            queueStorageTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(queueStorageTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(TableStorageTargetTest), cloudTableService.ConnectionString);
            Assert.Single(cloudTableService.BatchExecuted);   // One queue
            Assert.Equal("test", cloudTableService.PeekLastAdded("test").First().PartitionKey);
        }

        [Fact]
        public void MultiplePartitionKeysTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(TableStorageTargetTest);
            var cloudTableService = new CloudTableServiceMock();
            var queueStorageTarget = new TableStorageTarget(cloudTableService);
            queueStorageTarget.ConnectionString = "${var:ConnectionString}";
            queueStorageTarget.TableName = "${logger}";
            queueStorageTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(queueStorageTarget);
            logFactory.Configuration = logConfig;
            for (int i = 0; i < 50; ++i)
            {
                logFactory.GetLogger("Test1").Info("Hello");
                logFactory.GetLogger("Test2").Debug("Goodbye");
            }
            logFactory.Flush();
            Assert.Equal(2, cloudTableService.BatchExecuted.Count);   // Two partitions
            Assert.Equal(50, cloudTableService.PeekLastAdded("Test1").Count());
            Assert.Equal(50, cloudTableService.PeekLastAdded("Test2").Count());
        }

        [Fact]
        public void DynamicTableEntityTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(TableStorageTargetTest);
            var cloudTableService = new CloudTableServiceMock();
            var queueStorageTarget = new TableStorageTarget(cloudTableService);
            queueStorageTarget.ContextProperties.Add(new TargetPropertyWithContext("ThreadId", "${threadid}"));
            queueStorageTarget.ConnectionString = "${var:ConnectionString}";
            queueStorageTarget.TableName = "${logger}";
            queueStorageTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(queueStorageTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("Test").Info("Hello");
            logFactory.Flush();
            Assert.Single(cloudTableService.BatchExecuted);
            Assert.Equal(System.Threading.Thread.CurrentThread.ManagedThreadId.ToString(), cloudTableService.PeekLastAdded("Test").Cast<DynamicTableEntity>().First()["ThreadId"].ToString());
        }
    }
}
