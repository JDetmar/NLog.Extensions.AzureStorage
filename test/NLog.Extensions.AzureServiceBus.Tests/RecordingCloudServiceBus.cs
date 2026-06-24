using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using NLog.Extensions.AzureBlobStorage;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureServiceBus.Test
{
    /// <summary>
    /// Instrumented <see cref="ICloudServiceBus"/> that counts batch sends relative to
    /// <see cref="CloseAsync"/>, and can make CloseAsync slow and/or throw on demand. Models the
    /// real CloseAsync that closes the sender and then disposes the client, so a test can assert the
    /// client dispose is awaited (not abandoned). Used to reproduce/regression-test the S5 teardown.
    /// </summary>
    sealed class RecordingCloudServiceBus : ICloudServiceBus
    {
        private readonly object _sync = new object();

        public int SendCount;
        public int SendAfterCloseCount;
        public int CloseCompletedCount;

        /// <summary>True once the sender has been closed.</summary>
        public volatile bool ConnectionClosed;
        /// <summary>True once the client has been disposed (the step that leaks if abandoned).</summary>
        public volatile bool DisposeCompleted;

        public TimeSpan CloseDelay { get; set; } = TimeSpan.Zero;
        public bool CloseThrows { get; set; }

        public string EntityPath => "recording";
        public TimeSpan? DefaultTimeToLive => null;

        public void Connect(string connectionString, string queueOrTopicName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, string eventProducerIdentifier, bool useWebSockets, string endPointAddress, TimeSpan? timeToLive, ProxySettings proxySettings)
        {
        }

        public async Task CloseAsync()
        {
            if (CloseDelay > TimeSpan.Zero)
                await Task.Delay(CloseDelay).ConfigureAwait(false);

            if (CloseThrows)
                throw new InvalidOperationException("RecordingCloudServiceBus.CloseAsync boom");

            ConnectionClosed = true;    // _sender.CloseAsync()
            DisposeCompleted = true;    // _client.DisposeAsync()
            lock (_sync)
                CloseCompletedCount++;
        }

        public Task<IServiceBusMessageBatch> CreateMessageBatchAsync(int maxBatchSizeBytes, CancellationToken cancellationToken)
        {
            return Task.FromResult<IServiceBusMessageBatch>(new RecordingBatch(this));
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

        private sealed class RecordingBatch : IServiceBusMessageBatch
        {
            private readonly RecordingCloudServiceBus _owner;
            private readonly List<ServiceBusMessage> _messages = new List<ServiceBusMessage>();

            public RecordingBatch(RecordingCloudServiceBus owner)
            {
                _owner = owner;
            }

            public int Count => _messages.Count;
            public long MaximumSizeInBytes => long.MaxValue;

            public bool TryAddMessage(ServiceBusMessage message)
            {
                _messages.Add(message);
                return true;
            }

            public Task SendAsync(CancellationToken cancellationToken)
            {
                _owner.RecordSend(_messages.Count);
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }
}
