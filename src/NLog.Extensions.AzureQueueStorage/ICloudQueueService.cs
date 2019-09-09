using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Queue;

namespace NLog.Extensions.AzureStorage
{
    internal interface ICloudQueueService
    {
        void Connect(string connectionString);
        Task AddMessageAsync(string queueName, CloudQueueMessage queueMessage, CancellationToken cancellationToken);
    }
}
