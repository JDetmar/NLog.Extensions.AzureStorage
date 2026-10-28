using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog.Extensions.AzureBlobStorage;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureQueueStorage.Tests
{
    class CloudQueueServiceMock : ICloudQueueService, IDisposable
    {
        public Dictionary<string, string> MessagesAdded { get; } = new Dictionary<string, string>();

        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public string ConnectionString { get; private set; }

        public TimeSpan? TimeToLive { get; private set; }

        public Dictionary<string, IDictionary<string, string>> QueueMetadata { get; } = new Dictionary<string, IDictionary<string, string>>();

        private Func<LogEventInfo, IDictionary<string, string>> queueMetadataRenderer;

        public Task AddMessageAsync(string queueName, string queueMessage, LogEventInfo queueMetadataLogEvent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("CloudQueueService not connected");

            lock (QueueMetadata)
            {
                if (!QueueMetadata.ContainsKey(queueName))
                    QueueMetadata[queueName] = queueMetadataRenderer?.Invoke(queueMetadataLogEvent);   // rendered once, by the event that creates the queue
            }

            return Task.Delay(10, cancellationToken).ContinueWith(t =>
            {
                lock (MessagesAdded)
                    MessagesAdded[queueName] = queueMessage;
            });
        }

        public void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, TimeSpan? timeToLive, Func<LogEventInfo, IDictionary<string, string>> queueMetadataRenderer, ProxySettings proxySettings = null)
        {
            ConnectionString = connectionString;
            this.queueMetadataRenderer = queueMetadataRenderer;
            TimeToLive = timeToLive;
        }

        public string PeekLastAdded(string queueName)
        {
            lock (MessagesAdded)
            {
                if (MessagesAdded.TryGetValue(queueName, out var queueMessage))
                {
                    return queueMessage;
                }
            }

            return null;
        }
    }
}
