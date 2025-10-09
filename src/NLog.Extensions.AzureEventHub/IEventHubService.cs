using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using NLog.Extensions.AzureBlobStorage;

namespace NLog.Extensions.AzureStorage
{
    internal interface IEventHubService
    {
        string EventHubName { get; }
        void Connect(string connectionString, string eventHubName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, string eventProducerIdentifier, bool useWebSockets, string endPointAddress, ProxySettings proxySettings);
        Task CloseAsync();
        Task SendAsync(IEnumerable<EventData> eventDataBatch, string partitionKey, CancellationToken cancellationToken);
    }
}
