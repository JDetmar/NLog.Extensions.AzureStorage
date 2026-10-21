using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureBlobStorage.Tests
{
    class CloudBlobServiceMock : ICloudBlobService, IDisposable
    {
        public ConcurrentDictionary<KeyValuePair<string,string>, byte[]> AppendBlob { get; } = new ConcurrentDictionary<KeyValuePair<string, string>, byte[]>();
        public ConcurrentDictionary<KeyValuePair<string, string>, IDictionary<string, string>> AppendBlobMetadata { get; } = new ConcurrentDictionary<KeyValuePair<string, string>, IDictionary<string, string>>();
        public ConcurrentDictionary<KeyValuePair<string, string>, IDictionary<string, string>> AppendBlobTags { get; } = new ConcurrentDictionary<KeyValuePair<string, string>, IDictionary<string, string>>();

        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public string ConnectionString { get; private set; }

        public IDictionary<string, string> BlobMetadata { get; private set; }

        public IDictionary<string, string> BlobTags { get; private set; }

        public void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, IDictionary<string, string> blobMetadata, IDictionary<string, string> blobTags, ProxySettings proxySettings = null)
        {
            ConnectionString = connectionString;
            BlobMetadata = blobMetadata;
            BlobTags = blobTags;
        }

        public Task AppendFromByteArrayAsync(string containerName, string blobName, string contentType, byte[] buffer, LogEventInfo blobPropertiesLogEvent, Func<LogEventInfo, IDictionary<string, string>> blobMetadataRenderer, Func<LogEventInfo, IDictionary<string, string>> blobTagsRenderer, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("CloudBlobService not connected");

            var blobKey = new KeyValuePair<string, string>(containerName, blobName);
            var isNewBlob = !AppendBlob.ContainsKey(blobKey);
            AppendBlob[blobKey] = buffer;
            if (isNewBlob)
            {
                var blobMetadata = blobMetadataRenderer?.Invoke(blobPropertiesLogEvent);
                var blobTags = blobTagsRenderer?.Invoke(blobPropertiesLogEvent);
                AppendBlobMetadata[blobKey] = blobMetadata != null ? new Dictionary<string, string>(blobMetadata) : null;
                AppendBlobTags[blobKey] = blobTags != null ? new Dictionary<string, string>(blobTags) : null;
            }

            return Task.Delay(10, cancellationToken);
        }

        public string PeekLastAppendBlob(string containerName, string blobName)
        {
            if (AppendBlob.TryGetValue(new KeyValuePair<string, string>(containerName, blobName), out var payload))
            {
                return Encoding.UTF8.GetString(payload);
            }

            return null;
        }

        public IDictionary<string, string> PeekLastBlobMetadata(string containerName, string blobName)
        {
            if (AppendBlobMetadata.TryGetValue(new KeyValuePair<string, string>(containerName, blobName), out var metadata))
            {
                return metadata;
            }

            return null;
        }

        public IDictionary<string, string> PeekLastBlobTags(string containerName, string blobName)
        {
            if (AppendBlobTags.TryGetValue(new KeyValuePair<string, string>(containerName, blobName), out var tags))
            {
                return tags;
            }

            return null;
        }
    }
}
