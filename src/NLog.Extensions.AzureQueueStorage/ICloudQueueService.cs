using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureStorage
{
    internal interface ICloudQueueService
    {
        void Connect(string connectionString, string serviceUri, string tenantIdentity, string resourceIdentity, TimeSpan? timeToLive, IDictionary<string, string> queueMetadata);
        Task AddMessageAsync(string queueName, string queueMessage, CancellationToken cancellationToken);
    }
}
