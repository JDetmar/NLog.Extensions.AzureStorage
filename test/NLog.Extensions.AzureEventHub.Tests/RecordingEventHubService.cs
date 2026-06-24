using Azure.Messaging.EventHubs;
using NLog.Extensions.AzureBlobStorage;
using NLog.Extensions.AzureStorage;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureEventHub.Test
{
    /// <summary>
    /// Instrumented <see cref="IEventHubService"/> that counts batch sends relative to
    /// <see cref="CloseAsync"/>, and can make CloseAsync slow and/or throw on demand. Used to
    /// reproduce and regression-test the S5 shutdown-teardown behaviour.
    /// </summary>
    sealed class RecordingEventHubService : IEventHubService
    {
        private readonly object _sync = new object();

        public int SendCount;
        public int SendAfterCloseCount;
        public int CloseCompletedCount;

        /// <summary>True once CloseAsync has fully completed (connection closed).</summary>
        public volatile bool ConnectionClosed;

        public TimeSpan CloseDelay { get; set; } = TimeSpan.Zero;
        public bool CloseThrows { get; set; }

        public string EventHubName => "recording";

        public void Connect(string connectionString, string eventHubName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, string eventProducerIdentifier, bool useWebSockets, string endPointAddress, ProxySettings proxySettings)
        {
        }

        public async Task CloseAsync()
        {
            if (CloseDelay > TimeSpan.Zero)
                await Task.Delay(CloseDelay).ConfigureAwait(false);

            if (CloseThrows)
                throw new InvalidOperationException("RecordingEventHubService.CloseAsync boom");

            ConnectionClosed = true;
            lock (_sync)
                CloseCompletedCount++;
        }

        public Task<IEventDataBatch> CreateBatchAsync(string partitionKey, int maxBatchSizeBytes, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEventDataBatch>(new RecordingBatch(this));
        }

        private void RecordSend(int count)
        {
            lock (_sync)
            {
                SendCount += count;
                if (ConnectionClosed)
                    SendAfterCloseCount += count;
            }
        }

        private sealed class RecordingBatch : IEventDataBatch
        {
            private readonly RecordingEventHubService _owner;
            private readonly List<EventData> _events = new List<EventData>();

            public RecordingBatch(RecordingEventHubService owner)
            {
                _owner = owner;
            }

            public int Count => _events.Count;
            public long MaximumSizeInBytes => long.MaxValue;

            public bool TryAddEvent(EventData eventData)
            {
                _events.Add(eventData);
                return true;
            }

            public Task SendAsync(CancellationToken cancellationToken)
            {
                _owner.RecordSend(_events.Count);
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }
}
