using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using NLog.Extensions.AzureBlobStorage;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureServiceBus.Test
{
    class ServiceBusMock : ICloudServiceBus
    {
        // One inner list per batch that was actually sent (in send order, per partition).
        public List<List<ServiceBusMessage>> MessageDataSent { get; } = new List<List<ServiceBusMessage>>();
        public string ConnectionString { get; private set; }
        public string EntityPath { get; private set; }
        public TimeSpan? DefaultTimeToLive { get; private set; }

        public string PeekLastMessageBody()
        {
            lock (MessageDataSent)
            {
                if (MessageDataSent.Count > 0)
                {
                    var messages = MessageDataSent[MessageDataSent.Count - 1];
                    if (messages.Count > 0)
                    {
                        return Encoding.UTF8.GetString(messages[messages.Count - 1].Body);
                    }
                }
            }

            return null;
        }

        public void Connect(string connectionString, string queueOrTopicName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, string eventProducerIdentifier, bool useWebSockets, string endPointAddress, TimeSpan? timeToLive, ProxySettings proxySettings)
        {
            ConnectionString = connectionString;
            EntityPath = queueOrTopicName;
            DefaultTimeToLive = timeToLive;
        }

        public Task<IServiceBusMessageBatch> CreateMessageBatchAsync(int maxBatchSizeBytes, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("ServiceBusService not connected");

            int cap = maxBatchSizeBytes > 0 ? maxBatchSizeBytes : int.MaxValue;
            return Task.FromResult<IServiceBusMessageBatch>(new FakeMessageBatch(this, cap));
        }

        public async Task CloseAsync()
        {
            await Task.Delay(1).ConfigureAwait(false);
            lock (MessageDataSent)
                MessageDataSent.Clear();
        }

        // Mimics ServiceBusMessageBatch: TryAddMessage caps the batch at maxSizeBytes (measured by
        // the message body length), so a full batch must be sealed/sent before the next one starts,
        // and a single message larger than the cap can never be added to an empty batch.
        private sealed class FakeMessageBatch : IServiceBusMessageBatch
        {
            private readonly ServiceBusMock _owner;
            private readonly int _maxSizeBytes;
            private readonly List<ServiceBusMessage> _messages = new List<ServiceBusMessage>();
            private long _sizeBytes;

            public FakeMessageBatch(ServiceBusMock owner, int maxSizeBytes)
            {
                _owner = owner;
                _maxSizeBytes = maxSizeBytes;
            }

            public int Count => _messages.Count;

            public bool TryAddMessage(ServiceBusMessage message)
            {
                int messageSize = message.Body.ToMemory().Length;
                long newSize = _sizeBytes + messageSize;
                if (_messages.Count > 0 && newSize > _maxSizeBytes)
                    return false;   // batch full -> caller seals and sends, then retries on a fresh batch
                if (_messages.Count == 0 && messageSize > _maxSizeBytes)
                    return false;   // single message too large to ever fit
                _sizeBytes = newSize;
                _messages.Add(message);
                return true;
            }

            public Task SendAsync(CancellationToken cancellationToken)
            {
                var snapshot = new List<ServiceBusMessage>(_messages);
                return Task.Delay(5, cancellationToken).ContinueWith(t =>
                {
                    lock (_owner.MessageDataSent)
                        _owner.MessageDataSent.Add(snapshot);
                }, cancellationToken);
            }

            public void Dispose()
            {
            }
        }
    }
}
