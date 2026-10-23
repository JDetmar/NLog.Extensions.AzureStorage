using NLog.Extensions.AzureBlobStorage;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NLog.Extensions.AzureStorage
{
    interface ICloudBlobService
    {
        void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, ProxySettings proxySettings = null);
        Task AppendFromByteArrayAsync(string containerName, string blobName, string contentType, byte[] buffer, LogEventInfo blobPropertiesLogEvent, Func<LogEventInfo, IDictionary<string, string>> blobMetadataRenderer, Func<LogEventInfo, IDictionary<string, string>> blobTagsRenderer, CancellationToken cancellationToken);
    }
}
