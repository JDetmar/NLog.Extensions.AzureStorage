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
        void Connect(string connectionString, string queueOrTopicName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, bool useWebSockets, string webSocketProxyAddress, string endPointAddress, TimeSpan? timeToLive);
        Task SendAsync(IEnumerable<ServiceBusMessage> messages, CancellationToken cancellationToken);
        Task CloseAsync();
    }
}
