using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;

namespace NLog.Extensions.AzureStorage
{
    internal interface IEventHubService
    {
        void Connect(string connectionString, string entityPath, string serviceUri, string tenantIdentity, string resourceIdentity);
        Task CloseAsync();
        Task SendAsync(IEnumerable<EventData> eventDataList, string partitionKey, CancellationToken cancellationToken);
    }
}
