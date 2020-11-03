using Microsoft.Azure.ServiceBus;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureStorage
{
    internal interface ICloudServiceBus
    {
        void Connect(string connectionString, string queuePath, string topicPath);
        Task SendAsync(IList<Message> messages);
        Task CloseAsync();
    }
}
