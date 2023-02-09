using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureEventGrid.Tests
{
    public class EventGridServiceMock : IEventGridService
    {
        public string Topic { get; set; }

        public List<EventGridEvent> GridEvents { get; } = new List<EventGridEvent>();

        public List<CloudEvent> CloudEvents { get; } = new List<CloudEvent>();

        public void Connect(string topic, string tenantIdentity, string resourceIdentifier, string clientIdentity, string accessKey)
        {
            Topic = topic;
        }

        public Task SendEventAsync(EventGridEvent gridEvent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Topic))
                throw new InvalidOperationException("TopicUri not connected");

            return Task.Delay(10, cancellationToken).ContinueWith(t =>
            {
                lock (GridEvents)
                    GridEvents.Add(gridEvent);
            });
        }

        public Task SendEventAsync(CloudEvent cloudEvent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Topic))
                throw new InvalidOperationException("TopicUri not connected");

            return Task.Delay(10, cancellationToken).ContinueWith(t =>
            {
                lock (CloudEvents)
                    CloudEvents.Add(cloudEvent);
            });
        }

        public string PeekLastGridEvent()
        {
            lock (GridEvents)
            {
                var gridEvent = GridEvents.LastOrDefault();
                if (gridEvent != null)
                    return Encoding.UTF8.GetString(gridEvent.Data.ToArray());
            }

            return null;
        }

        public string PeekLastCloudEvent()
        {
            lock (CloudEvents)
            {
                var cloudEvent = CloudEvents.LastOrDefault();
                if (cloudEvent != null)
                    return Encoding.UTF8.GetString(cloudEvent.Data.ToArray());
            }

            return null;
        }
    }
}
