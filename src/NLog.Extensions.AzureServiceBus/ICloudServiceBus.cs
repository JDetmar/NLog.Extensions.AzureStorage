using Microsoft.Azure.ServiceBus;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureStorage
{
    internal interface ICloudServiceBus
    {
        TimeSpan? DefaultTimeToLive { get; }
        void Connect(string connectionString, string queuePath, string topicPath, TimeSpan? timeToLive);
        Task SendAsync(IList<Message> messages);
        Task CloseAsync();
    }
}
