using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using NLog.Extensions.AzureBlobStorage;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureEventGrid.Tests
{
    public class EventGridServiceMock : IEventGridService, IDisposable
    {
        public string Topic { get; set; }

        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public List<EventGridEvent> GridEvents { get; } = new List<EventGridEvent>();

        public List<CloudEvent> CloudEvents { get; } = new List<CloudEvent>();

        /// <summary>Number of service send calls (one per HTTP request). One batched flush => 1.</summary>
        public int SendCallCount { get; private set; }

        /// <summary>Event count of each batched send call, in order. One entry per request issued.</summary>
        public List<int> SendBatchSizes { get; } = new List<int>();

        public void Connect(string topic, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string accessKey, string clientAuthId, string clientAuthSecret, ProxySettings proxySettings = null)
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
                {
                    SendCallCount++;
                    GridEvents.Add(gridEvent);
                }
            });
        }

        public Task SendEventAsync(CloudEvent cloudEvent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Topic))
                throw new InvalidOperationException("TopicUri not connected");

            return Task.Delay(10, cancellationToken).ContinueWith(t =>
            {
                lock (CloudEvents)
                {
                    SendCallCount++;
                    CloudEvents.Add(cloudEvent);
                }
            });
        }

        public Task SendEventsAsync(IEnumerable<EventGridEvent> gridEvents, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Topic))
                throw new InvalidOperationException("TopicUri not connected");

            var batch = gridEvents.ToList();
            return Task.Delay(10, cancellationToken).ContinueWith(t =>
            {
                lock (GridEvents)
                {
                    SendCallCount++;   // one batched send => one request
                    SendBatchSizes.Add(batch.Count);
                    GridEvents.AddRange(batch);
                }
            });
        }

        public Task SendEventsAsync(IEnumerable<CloudEvent> cloudEvents, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Topic))
                throw new InvalidOperationException("TopicUri not connected");

            var batch = cloudEvents.ToList();
            return Task.Delay(10, cancellationToken).ContinueWith(t =>
            {
                lock (CloudEvents)
                {
                    SendCallCount++;   // one batched send => one request
                    SendBatchSizes.Add(batch.Count);
                    CloudEvents.AddRange(batch);
                }
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
