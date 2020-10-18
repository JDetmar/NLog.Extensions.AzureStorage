using System;
using System.Collections.Generic;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureQueueStorage.Tests
{
    public class QueueStorageTargetTest
    {
        [Fact]
        public void SingleLogEventTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(QueueStorageTargetTest);
            var cloudQueueService = new CloudQueueServiceMock();
            var queueStorageTarget = new QueueStorageTarget(cloudQueueService);
            queueStorageTarget.ConnectionString = "${var:ConnectionString}";
            queueStorageTarget.QueueName = "${logger}";
            queueStorageTarget.Layout = "${message}";
            queueStorageTarget.QueueMetadata.Add(new TargetPropertyWithContext() { Name = "MyMeta", Layout = "MyMetaValue" });
            logConfig.AddRuleForAllLevels(queueStorageTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(QueueStorageTargetTest), cloudQueueService.ConnectionString);
            Assert.Single(cloudQueueService.QueueMetadata);
            Assert.Contains(new KeyValuePair<string, string>("MyMeta", "MyMetaValue"), cloudQueueService.QueueMetadata);
            Assert.Single(cloudQueueService.MessagesAdded);   // One queue
            Assert.Equal("Hello World", cloudQueueService.PeekLastAdded("test"));
        }
    }
}
