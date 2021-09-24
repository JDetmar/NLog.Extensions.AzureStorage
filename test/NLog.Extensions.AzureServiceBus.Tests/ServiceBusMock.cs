using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureServiceBus.Test
{
    class ServiceBusMock : ICloudServiceBus
    {
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

        public void Connect(string connectionString, string queueOrTopicName, string serviceUri, string tenantIdentity, string resourceIdentity, TimeSpan? timeToLive)
        {
            ConnectionString = connectionString;
            EntityPath = queueOrTopicName;
            DefaultTimeToLive = timeToLive;
        }

        public Task SendAsync(IEnumerable<ServiceBusMessage> messages, System.Threading.CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("ServiceBusService not connected");

            return Task.Delay(10, cancellationToken).ContinueWith(t =>
            {
                lock (MessageDataSent)
                    MessageDataSent.Add(new List<ServiceBusMessage>(messages));
            }, cancellationToken);
        }

        public async Task CloseAsync()
        {
            await Task.Delay(1).ConfigureAwait(false);
            lock (MessageDataSent)
                MessageDataSent.Clear();
        }
    }
}
