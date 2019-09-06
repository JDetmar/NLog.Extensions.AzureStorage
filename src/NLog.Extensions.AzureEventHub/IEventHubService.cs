using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;

namespace NLog.Extensions.AzureStorage
{
    internal interface IEventHubService
    {
        void Connect(string connectionString, string entityPath);
        void Close();
        Task SendAsync(IList<EventData> eventDataList, string partitionKey);
    }
}
