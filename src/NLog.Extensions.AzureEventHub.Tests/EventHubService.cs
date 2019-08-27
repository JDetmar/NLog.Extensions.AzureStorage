using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;

namespace NLog.Extensions.AzureEventHub.Test
{
    class EventHubService : IEventHubService
    {
        public Dictionary<string, IList<EventData>> EventDataSent { get; } = new Dictionary<string, IList<EventData>>();
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
            lock (EventDataSent)
                EventDataSent[partitionKey] = eventDataList;
            return Task.Delay(10);
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
