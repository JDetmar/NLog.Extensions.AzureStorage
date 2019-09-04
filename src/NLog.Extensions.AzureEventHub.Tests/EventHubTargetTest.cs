using System;
using System.Linq;
using NLog;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureEventHub.Test
{
    public class EventHubTargetTest
    {
        [Fact]
        public void SingleLogEventTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(EventHubTargetTest);
            var eventHubService = new EventHubService();
            var eventHubTarget = new EventHubTarget(eventHubService);
            eventHubTarget.ConnectionString = "${var:ConnectionString}";
            eventHubTarget.EventHubName = "${shortdate}";
            eventHubTarget.PartitionKey = "${logger}";
            eventHubTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(eventHubTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("Test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(EventHubTargetTest), eventHubService.ConnectionString);
            Assert.Single( eventHubService.EventDataSent);   // One partition
            Assert.Equal("Hello World", eventHubService.PeekLastSent("Test"));
        }

        [Fact]
        public void MultiplePartitionKeysTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            var eventHubService = new EventHubService();
            var eventHubTarget = new EventHubTarget(eventHubService);
            eventHubTarget.ConnectionString = "LocalEventHub";
            eventHubTarget.PartitionKey = "${logger}";
            eventHubTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(eventHubTarget);
            logFactory.Configuration = logConfig;
            for (int i = 0; i < 50; ++i)
            {
                logFactory.GetLogger("Test1").Info("Hello");
                logFactory.GetLogger("Test2").Debug("Goodbye");
            }
            logFactory.Flush();
            Assert.Equal(2, eventHubService.EventDataSent.Count);   // Two partitions
            Assert.Equal(50, eventHubService.EventDataSent["Test1"].Count);
            Assert.Equal(50, eventHubService.EventDataSent["Test2"].Count);
        }

        [Fact]
        public void EventDataPropertiesTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            var eventHubService = new EventHubService();
            var eventHubTarget = new EventHubTarget(eventHubService);
            eventHubTarget.ConnectionString = "LocalEventHub";
            eventHubTarget.PartitionKey = "${logger}";
            eventHubTarget.Layout = "${message}";
            eventHubTarget.ContextProperties.Add(new Targets.TargetPropertyWithContext("Level", "${level}"));
            logConfig.AddRuleForAllLevels(eventHubTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("Test").Info("Hello");
            logFactory.Flush();
            Assert.Single(eventHubService.EventDataSent);
            Assert.Equal("Hello", eventHubService.PeekLastSent("Test"));
            Assert.Single(eventHubService.EventDataSent.First().Value.First().Properties);
            Assert.Equal(LogLevel.Info.ToString(), eventHubService.EventDataSent.First().Value.First().Properties["Level"]);
        }
    }
}
