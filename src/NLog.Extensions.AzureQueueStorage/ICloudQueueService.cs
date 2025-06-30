using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureStorage
{
    internal interface ICloudQueueService
    {
        void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, TimeSpan? timeToLive, IDictionary<string, string> queueMetadata);
        Task AddMessageAsync(string queueName, string queueMessage, CancellationToken cancellationToken);
    }
}
