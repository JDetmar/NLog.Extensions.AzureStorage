using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureQueueStorage.Tests
{
    class CloudQueueServiceMock : ICloudQueueService
    {
        public Dictionary<string, string> MessagesAdded { get; } = new Dictionary<string, string>();
        
        public string ConnectionString { get; private set; }

        public TimeSpan? TimeToLive { get; private set; }

        public IDictionary<string, string> QueueMetadata { get; private set; }

        public Task AddMessageAsync(string queueName, string queueMessage, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("CloudQueueService not connected");

            return Task.Delay(10, cancellationToken).ContinueWith(t =>
            {
                lock (MessagesAdded)
                    MessagesAdded[queueName] = queueMessage;
            });
        }

        public void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, TimeSpan? timeToLive, IDictionary<string, string> queueMetadata)
        {
            ConnectionString = connectionString;
            QueueMetadata = queueMetadata;
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
