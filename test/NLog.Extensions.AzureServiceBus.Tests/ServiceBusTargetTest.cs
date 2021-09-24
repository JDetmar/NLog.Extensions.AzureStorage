using System;
using System.Linq;
using NLog;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureServiceBus.Test
{
    public class ServiceBusTargetTest
    {
        [Fact]
        public void SingleLogEventTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(ServiceBusTargetTest);
            var eventHubService = new ServiceBusMock();
            var serviceBusTarget = new ServiceBusTarget(eventHubService);
            serviceBusTarget.ConnectionString = "${var:ConnectionString}";
            serviceBusTarget.QueueName = "${shortdate}";
            serviceBusTarget.PartitionKey = "${logger}";
            serviceBusTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(serviceBusTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("Test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(ServiceBusTargetTest), eventHubService.ConnectionString);
            Assert.Single( eventHubService.MessageDataSent);   // One partition
            Assert.Equal("Hello World", eventHubService.PeekLastMessageBody());
        }

        [Fact]
        public void MultiplePartitionKeysTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            var serviceBusMock = new ServiceBusMock();
            var serviceBusTarget = new ServiceBusTarget(serviceBusMock);
            serviceBusTarget.ConnectionString = "LocalEventHub";
            serviceBusTarget.QueueName = "${shortdate}";
            serviceBusTarget.PartitionKey = "${logger}";
            serviceBusTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(serviceBusTarget);
            logFactory.Configuration = logConfig;
            for (int i = 0; i < 50; ++i)
            {
                logFactory.GetLogger("Test1").Info("Hello");
                logFactory.GetLogger("Test2").Debug("Goodbye");
            }
            logFactory.Flush();
            Assert.Equal(2, serviceBusMock.MessageDataSent.Count);   // Two partitions
            Assert.Equal(50, serviceBusMock.MessageDataSent[0].Count);
            Assert.Equal(50, serviceBusMock.MessageDataSent[1].Count);
        }

        [Fact]
        public void ServiceBusUserPropertiesTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            var serviceBusMock = new ServiceBusMock();
            var serviceBusTarget = new ServiceBusTarget(serviceBusMock);
            serviceBusTarget.ConnectionString = "LocalEventHub";
            serviceBusTarget.QueueName = "${shortdate}";
            serviceBusTarget.PartitionKey = "${logger}";
            serviceBusTarget.Layout = "${message}";
            serviceBusTarget.ContextProperties.Add(new Targets.TargetPropertyWithContext("Level", "${level}"));
            logConfig.AddRuleForAllLevels(serviceBusTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("Test").Info("Hello");
            logFactory.Flush();
            Assert.Single(serviceBusMock.MessageDataSent);
            Assert.Equal("Hello", serviceBusMock.PeekLastMessageBody());
            Assert.Single(serviceBusMock.MessageDataSent.First().First().ApplicationProperties);
            Assert.Equal(LogLevel.Info.ToString(), serviceBusMock.MessageDataSent.First().First().ApplicationProperties["Level"]);
        }
    }
}
