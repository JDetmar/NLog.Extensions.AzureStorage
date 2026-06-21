using Azure.Messaging.EventHubs;
using NLog.Extensions.AzureBlobStorage;
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
        // All events that were actually sent, aggregated per partition key (in send order).
        public Dictionary<string, List<EventData>> EventDataSent { get; } = new Dictionary<string, List<EventData>>();
        // One entry per batch that was actually sent (partition key + the events it carried).
        public List<KeyValuePair<string, List<EventData>>> BatchesSent { get; } = new List<KeyValuePair<string, List<EventData>>>();
        public string ConnectionString { get; private set; }
        public string EventHubName { get; private set; }

        public async Task CloseAsync()
        {
            await Task.Delay(1).ConfigureAwait(false);
            lock (EventDataSent)
            {
                EventDataSent.Clear();
                BatchesSent.Clear();
            }
        }

        public void Connect(string connectionString, string eventHubName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, string eventProducerIdentifier, bool useWebSockets, string endPointAddress, ProxySettings proxySettings)
        {
            ConnectionString = connectionString;
            EventHubName = eventHubName;
        }

        public Task<IEventDataBatch> CreateBatchAsync(string partitionKey, int maxBatchSizeBytes, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("EventHubService not connected");

            int cap = maxBatchSizeBytes > 0 ? maxBatchSizeBytes : int.MaxValue;
            return Task.FromResult<IEventDataBatch>(new FakeEventDataBatch(this, partitionKey ?? string.Empty, cap));
        }

        private void RecordBatch(string partitionKey, List<EventData> events)
        {
            lock (EventDataSent)
            {
                if (EventDataSent.TryGetValue(partitionKey, out var existingBatch))
                    existingBatch.AddRange(events);
                else
                    EventDataSent[partitionKey] = new List<EventData>(events);

                BatchesSent.Add(new KeyValuePair<string, List<EventData>>(partitionKey, events));
            }
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

        // Mimics EventDataBatch: TryAddEvent caps the batch at maxSizeBytes (measured by the event
        // body length), so a full batch must be sealed/sent before the next one starts, and a single
        // event larger than the cap can never be added to an empty batch.
        private sealed class FakeEventDataBatch : IEventDataBatch
        {
            private readonly EventHubServiceMock _owner;
            private readonly string _partitionKey;
            private readonly int _maxSizeBytes;
            private readonly List<EventData> _events = new List<EventData>();
            private long _sizeBytes;

            public FakeEventDataBatch(EventHubServiceMock owner, string partitionKey, int maxSizeBytes)
            {
                _owner = owner;
                _partitionKey = partitionKey;
                _maxSizeBytes = maxSizeBytes;
            }

            public int Count => _events.Count;

            public bool TryAddEvent(EventData eventData)
            {
                int eventSize = eventData.Body.Length;
                long newSize = _sizeBytes + eventSize;
                if (_events.Count > 0 && newSize > _maxSizeBytes)
                    return false;   // batch full -> caller seals and sends, then retries on a fresh batch
                if (_events.Count == 0 && eventSize > _maxSizeBytes)
                    return false;   // single event too large to ever fit
                _sizeBytes = newSize;
                _events.Add(eventData);
                return true;
            }

            public Task SendAsync(CancellationToken cancellationToken)
            {
                var snapshot = new List<EventData>(_events);
                return Task.Delay(3, cancellationToken).ContinueWith(t =>
                {
                    _owner.RecordBatch(_partitionKey, snapshot);
                }, cancellationToken);
            }

            public void Dispose()
            {
            }
        }
    }
}
