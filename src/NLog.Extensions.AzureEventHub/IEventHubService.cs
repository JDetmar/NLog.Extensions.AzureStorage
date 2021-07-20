using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;

namespace NLog.Extensions.AzureStorage
{
    internal interface IEventHubService
    {
        void Connect(string connectionString, string entityPath, string serviceUri, string tenantIdentity, string resourceIdentity);
        void Close();
        Task SendAsync(IList<EventData> eventDataList, string partitionKey, CancellationToken cancellationToken);
    }
}
