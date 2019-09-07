using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Queue;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureStorageQueue.Tests
{
    class CloudQueueServiceMock : ICloudQueueService
    {
        public Dictionary<string, CloudQueueMessage> MessagesAdded { get; } = new Dictionary<string, CloudQueueMessage>();
        public string ConnectionString { get; private set; }

        public Task AddMessageAsync(string queueName, CloudQueueMessage queueMessage, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("CloudQueueService not connected");

            lock (MessagesAdded)
                MessagesAdded[queueName] = queueMessage;
            return Task.Delay(10, cancellationToken);
        }

        public void Connect(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public string PeekLastAdded(string queueName)
        {
            lock (MessagesAdded)
            {
                if (MessagesAdded.TryGetValue(queueName, out var queueMessage))
                {
                    return queueMessage.AsString;
                }
            }

            return null;
        }
    }
}
