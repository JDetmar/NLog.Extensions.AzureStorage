﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureServiceBus.Test
{
    class ServiceBusMock : ICloudServiceBus
    {
        public List<IList<Message>> MessageDataSent { get; } = new List<IList<Message>>();
        public string ConnectionString { get; private set; }
        public string QueuePath { get; private set; }
        public string TopicPath { get; private set; }

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

        public void Connect(string connectionString, string queuePath, string topicPath, TimeSpan? timeToLive)
        {
            ConnectionString = connectionString;
            QueuePath = queuePath;
            TopicPath = topicPath;
            DefaultTimeToLive = timeToLive;
    }

        public Task SendAsync(IList<Message> messages)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("EventHubService not connected");

            return Task.Delay(10).ContinueWith(t =>
            {
                lock (MessageDataSent)
                    MessageDataSent.Add(messages);
            });
        }

        public Task CloseAsync()
        {
            lock (MessageDataSent)
                MessageDataSent.Clear();
            return Task.FromResult<object>(null);
        }
    }
}
