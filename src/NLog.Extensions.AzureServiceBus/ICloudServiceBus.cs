using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace NLog.Extensions.AzureStorage
{
    internal interface ICloudServiceBus
    {
        string EntityPath { get; }
        TimeSpan? DefaultTimeToLive { get; }
        void Connect(string connectionString, string queueOrTopicName, string serviceUri, string tenantIdentity, string resourceIdentity, TimeSpan? timeToLive);
        Task SendAsync(IEnumerable<ServiceBusMessage> messages, CancellationToken cancellationToken);
        Task CloseAsync();
    }
}
