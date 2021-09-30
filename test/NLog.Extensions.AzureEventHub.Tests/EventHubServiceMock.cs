using Azure.Messaging.EventHubs;
using NLog.Extensions.AzureStorage;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureEventHub.Test
{
    class EventHubServiceMock : IEventHubService
    {
        private readonly Random _random = new Random();

        public Dictionary<string, List<EventData>> EventDataSent { get; } = new Dictionary<string, List<EventData>>();
        public string ConnectionString { get; private set; }
        public string EventHubName { get; private set; }

        public async Task CloseAsync()
        {
            await Task.Delay(1).ConfigureAwait(false);
            lock (EventDataSent)
                EventDataSent.Clear();
        }

        public void Connect(string connectionString, string eventHubName, string serviceUri, string tenantIdentity, string resourceIdentity)
        {
            ConnectionString = connectionString;
            EventHubName = eventHubName;
        }

        public Task SendAsync(IEnumerable<EventData> eventDataBatch, string partitionKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("EventHubService not connected");

            return Task.Delay(_random.Next(5, 10), cancellationToken).ContinueWith(t =>
            {
                lock (EventDataSent)
                {
                    if (EventDataSent.TryGetValue(partitionKey, out var existingBatch))
                        existingBatch.AddRange(eventDataBatch);
                    else
                        EventDataSent[partitionKey] = new List<EventData>(eventDataBatch);
                }
            }, cancellationToken);
        }

        public string PeekLastSent(string partitionKey)
        {
            lock (EventDataSent)
            {
                if (EventDataSent.TryGetValue(partitionKey, out var eventData))
                {
                    if (eventData.Count > 0)
                    {
                        return Encoding.UTF8.GetString(eventData[eventData.Count - 1].Body.ToArray());
                    }
                }
            }

            return null;
        }
    }
}
