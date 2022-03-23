using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;

namespace NLog.Extensions.AzureStorage
{
    internal interface IEventHubService
    {
        string EventHubName { get; }
        void Connect(string connectionString, string eventHubName, string serviceUri, string tenantIdentity, string clientIdentity, string resourceIdentity);
        Task CloseAsync();
        Task SendAsync(IEnumerable<EventData> eventDataBatch, string partitionKey, CancellationToken cancellationToken);
    }
}
