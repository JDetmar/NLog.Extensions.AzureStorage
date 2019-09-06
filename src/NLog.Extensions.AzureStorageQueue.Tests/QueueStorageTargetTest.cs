using System;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureStorageQueue.Tests
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
            logConfig.AddRuleForAllLevels(queueStorageTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(QueueStorageTargetTest), cloudQueueService.ConnectionString);
            Assert.Single(cloudQueueService.MessagesAdded);   // One queue
            Assert.Equal("Hello World", cloudQueueService.PeekLastAdded("test"));
        }
    }
}
