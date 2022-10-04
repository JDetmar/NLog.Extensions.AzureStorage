using System;
using System.Collections.Generic;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureEventGrid.Tests
{
    public class EventGridTargetTest
    {
        [Fact]
        public void SingleGridEventTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["TopicUrl"] = nameof(EventGridTargetTest);
            var eventGridService = new EventGridServiceMock();
            var eventGridTarget = new EventGridTarget(eventGridService);
            eventGridTarget.Topic = "${var:TopicUrl}";
            eventGridTarget.Layout = "${message}";
            eventGridTarget.GridEventSubject = "${logger}";
            eventGridTarget.MessageProperties.Add(new TargetPropertyWithContext() { Name = "MyMeta", Layout = "MyMetaValue" });
            logConfig.AddRuleForAllLevels(eventGridTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(EventGridTargetTest), eventGridService.Topic);
            Assert.Single(eventGridService.GridEvents);   // One queue
            Assert.Equal("Hello World", eventGridService.PeekLastGridEvent());
        }

        [Fact]
        public void SingleCloudEventTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["TopicUrl"] = nameof(EventGridTargetTest);
            var eventGridService = new EventGridServiceMock();
            var eventGridTarget = new EventGridTarget(eventGridService);
            eventGridTarget.Topic = "${var:TopicUrl}";
            eventGridTarget.Layout = "${message}";
            eventGridTarget.CloudEventSource = "${logger}";
            eventGridTarget.MessageProperties.Add(new TargetPropertyWithContext() { Name = "MyMeta", Layout = "MyMetaValue" });
            logConfig.AddRuleForAllLevels(eventGridTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(EventGridTargetTest), eventGridService.Topic);
            Assert.Single(eventGridService.CloudEvents);   // One queue
            Assert.Equal("Hello World", eventGridService.PeekLastCloudEvent());
        }
    }
}
