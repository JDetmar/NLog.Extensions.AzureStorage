using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureEventHub.Test
{
    class EventHubServiceMock : IEventHubService
    {
        private readonly Random _random = new Random();

        public Dictionary<string, List<EventData>> EventDataSent { get; } = new Dictionary<string, List<EventData>>();
        public string ConnectionString { get; private set; }
        public string EntityPath { get; private set; }

        public void Close()
        {
            lock (EventDataSent)
                EventDataSent.Clear();
        }

        public void Connect(string connectionString, string entityPath)
        {
            ConnectionString = connectionString;
            EntityPath = entityPath;
        }

        public Task SendAsync(IList<EventData> eventDataList, string partitionKey)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("EventHubService not connected");

            return Task.Delay(_random.Next(5, 10)).ContinueWith(t =>
            {
                lock (EventDataSent)
                {
                    if (EventDataSent.TryGetValue(partitionKey, out var existingBatch))
                        existingBatch.AddRange(eventDataList);
                    else
                        EventDataSent[partitionKey] = new List<EventData>(eventDataList);
                }
            });
        }

        public string PeekLastSent(string partitionKey)
        {
            lock (EventDataSent)
            {
                if (EventDataSent.TryGetValue(partitionKey, out var eventData))
                {
                    if (eventData.Count > 0)
                        return Encoding.UTF8.GetString(eventData[eventData.Count - 1].Body);
                }
            }

            return null;
        }
    }
}
