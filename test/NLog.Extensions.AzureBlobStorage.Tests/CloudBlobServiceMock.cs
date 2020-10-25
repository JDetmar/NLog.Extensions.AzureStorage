using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureBlobStorage.Tests
{
    class CloudBlobServiceMock : ICloudBlobService
    {
        public Dictionary<KeyValuePair<string,string>, byte[]> AppendBlob { get; } = new Dictionary<KeyValuePair<string, string>, byte[]>();

        public string ConnectionString { get; private set; }

        public IDictionary<string, string> BlobMetadata { get; private set; }

        public IDictionary<string, string> BlobTags { get; private set; }

        public void Connect(string connectionString, string serviceUri, string tenantIdentity, string resourceIdentity, IDictionary<string, string> blobMetadata, IDictionary<string, string> blobTags)
        {
            ConnectionString = connectionString;
            BlobMetadata = blobMetadata;
            BlobTags = blobTags;
        }

        public Task AppendFromByteArrayAsync(string containerName, string blobName, string contentType, byte[] buffer, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("CloudBlobService not connected");

            lock (AppendBlob)
                AppendBlob[new KeyValuePair<string, string>(containerName, blobName)] = buffer;
            return Task.Delay(10, cancellationToken);
        }

        public string PeekLastAppendBlob(string containerName, string blobName)
        {
            lock (AppendBlob)
            {
                if (AppendBlob.TryGetValue(new KeyValuePair<string, string>(containerName, blobName), out var payload))
                {
                    return Encoding.UTF8.GetString(payload);
                }
            }

            return null;
        }
    }
}
