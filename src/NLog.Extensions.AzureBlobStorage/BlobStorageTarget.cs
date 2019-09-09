using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using NLog.Common;
using NLog.Config;
using NLog.Extensions.AzureStorage;
using NLog.Layouts;

namespace NLog.Targets
{
    /// <summary>
    /// Azure Blob Storage NLog Target
    /// </summary>
    [Target("AzureBlobStorage")]
    public sealed class BlobStorageTarget : AsyncTaskTarget
    {
        private readonly ICloudBlobService _cloudBlobService;
        private readonly AzureStorageNameCache _containerNameCache = new AzureStorageNameCache();
        private readonly Func<string, string> _checkAndRepairContainerNameDelegate;
        private readonly char[] _reusableEncodingBuffer = new char[32 * 1024];  // Avoid large-object-heap
        private readonly StringBuilder _reusableEncodingBuilder = new StringBuilder(1024);

        //Delegates for bucket sorting
        private SortHelpers.KeySelector<LogEventInfo, ContainerBlobKey> _getContainerBlobNameDelegate;

        public Layout ConnectionString { get; set; }
        public string ConnectionStringKey { get; set; }

        [RequiredParameter]
        public Layout Container { get; set; }

        [RequiredParameter]
        public Layout BlobName { get; set; }

        public string ContentType { get; set; } = "text/plain";

        public BlobStorageTarget()
            :this(new CloudBlobService())
        {
        }

        internal BlobStorageTarget(ICloudBlobService cloudBlobService)
        {
            _checkAndRepairContainerNameDelegate = CheckAndRepairContainerNamingRules;
            _cloudBlobService = cloudBlobService;
        }

        /// <summary>
        /// Initializes the target. Can be used by inheriting classes
        /// to initialize logging.
        /// </summary>
        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            string connectionString = string.Empty;
            try
            {
                connectionString = ConnectionStringHelper.LookupConnectionString(ConnectionString, ConnectionStringKey);
                _cloudBlobService.Connect(connectionString);
                InternalLogger.Trace("AzureBlobStorageTarget - Initialized");
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureBlobStorageTarget(Name={0}): Failed to create BlobClient with connectionString={1}.", Name, connectionString);
                throw;
            }
        }

        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Writes an array of logging events to the log target. By default it iterates on all
        /// events and passes them to "Write" method. Inheriting classes can use this method to
        /// optimize batch writes.
        /// </summary>
        /// <param name="logEvents">Logging events to be written out.</param>
        protected override Task WriteAsyncTask(IList<LogEventInfo> logEvents, CancellationToken cancellationToken)
        {
            //must sort into containers and then into the blobs for the container
            if (_getContainerBlobNameDelegate == null)
                _getContainerBlobNameDelegate = logEvent => new ContainerBlobKey(RenderLogEvent(Container, logEvent), RenderLogEvent(BlobName, logEvent));

            if (logEvents.Count == 1)
            {
                return WriteToBlobAsync(logEvents, RenderLogEvent(Container, logEvents[0]), RenderLogEvent(BlobName, logEvents[0]), cancellationToken);
            }

            var partitionBuckets = SortHelpers.BucketSort(logEvents, _getContainerBlobNameDelegate);
            IList<Task> multipleTasks = partitionBuckets.Count > 1 ? new List<Task>(partitionBuckets.Count) : null;
            foreach (var partitionBucket in partitionBuckets)
            {
                try
                {
                    var sendTask = WriteToBlobAsync(partitionBucket.Value, partitionBucket.Key.ContainerName, partitionBucket.Key.BlobName, cancellationToken);
                    if (multipleTasks == null)
                        return sendTask;
                    else
                        multipleTasks.Add(sendTask);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureBlobStorage(Name={0}): Failed to write {1} logevents to blob. ContainerName={2}, BlobName={3}", Name, partitionBucket.Value.Count, partitionBucket.Key.ContainerName, partitionBucket.Key.BlobName);
                    if (multipleTasks == null)
                        throw;
                }
            }

            return Task.WhenAll(multipleTasks ?? new Task[0]);
        }

        private byte[] CreateBlobPayload(IList<LogEventInfo> logEvents)
        {
            lock (_reusableEncodingBuilder)
            {
                _reusableEncodingBuilder.Length = 0;

                try
                {
                    //add each message for the destination append blob
                    for (int i = 0; i < logEvents.Count; ++i)
                    {
                        var logEvent = logEvents[i];
                        var layoutMessage = RenderLogEvent(Layout, logEvent);
                        _reusableEncodingBuilder.AppendLine(layoutMessage);
                    }

                    int totalLength = _reusableEncodingBuilder.Length;
                    if (totalLength < _reusableEncodingBuffer.Length)
                    {
                        _reusableEncodingBuilder.CopyTo(0, _reusableEncodingBuffer, 0, _reusableEncodingBuilder.Length);
                        return Encoding.UTF8.GetBytes(_reusableEncodingBuffer, 0, totalLength);
                    }
                    else
                    {
                        return Encoding.UTF8.GetBytes(_reusableEncodingBuilder.ToString());
                    }
                }
                finally
                {
                    const int maxSize = 512 * 1024;
                    if (_reusableEncodingBuilder.Length > maxSize)
                    {
                        _reusableEncodingBuilder.Remove(maxSize, _reusableEncodingBuilder.Length - maxSize);   // Releases all buffers
                    }
                }
            }
        }

        private Task WriteToBlobAsync(IList<LogEventInfo> logEvents, string containerName, string blobName, CancellationToken cancellationToken)
        {
            containerName = CheckAndRepairContainerName(containerName);
            blobName = CheckAndRepairBlobNamingRules(blobName);

            var buffer = CreateBlobPayload(logEvents);
            return _cloudBlobService.AppendFromByteArrayAsync(containerName, blobName, ContentType, buffer, cancellationToken);
        }

        private string CheckAndRepairContainerName(string containerName)
        {
            return _containerNameCache.LookupStorageName(containerName, _checkAndRepairContainerNameDelegate);
        }

        private string CheckAndRepairContainerNamingRules(string containerName)
        {
            InternalLogger.Trace("AzureBlobStorageTarget(Name={0}): Requested Container Name: {1}", Name, containerName);
            string validContainerName = AzureStorageNameCache.CheckAndRepairContainerNamingRules(containerName);
            if (validContainerName == containerName.ToLowerInvariant())
            {
                InternalLogger.Trace("AzureBlobStorageTarget(Name={0}): Using Container Name: {0}", Name, validContainerName);
            }
            else
            {
                InternalLogger.Trace("AzureBlobStorageTarget(Name={0}): Using Cleaned Container name: {0}", Name, validContainerName);
            }
            return validContainerName;
        }

        /// <summary>
        /// Checks the and repairs BLOB name acording to the Azure naming rules.
        /// </summary>
        /// <param name="blobName">Name of the BLOB.</param>
        /// <returns></returns>
        private static string CheckAndRepairBlobNamingRules(string blobName)
        {
            /*  https://docs.microsoft.com/en-us/rest/api/storageservices/fileservices/naming-and-referencing-containers--blobs--and-metadata
                Blob Names

                A blob name must conforming to the following naming rules:
                A blob name can contain any combination of characters.
                A blob name must be at least one character long and cannot be more than 1,024 characters long.
                Blob names are case-sensitive.
                Reserved URL characters must be properly escaped.

                The number of path segments comprising the blob name cannot exceed 254.
                A path segment is the string between consecutive delimiter characters (e.g., the forward slash '/') that corresponds to the name of a virtual directory.
            */
            if (String.IsNullOrWhiteSpace(blobName) || blobName.Length > 1024)
            {
                var blobDefault = String.Concat("Log-", DateTime.UtcNow.ToString("yy-MM-dd"), ".log");
                InternalLogger.Error("AzureBlobStorageTarget: Invalid Blob Name provided: {0} | Using default: {1}", blobName, blobDefault);
                return blobDefault;
            }
            InternalLogger.Trace("AzureBlobStorageTarget: Using provided blob name: {0}", blobName);
            return blobName;
        }

        struct ContainerBlobKey : IEquatable<ContainerBlobKey>
        {
            public readonly string ContainerName;
            public readonly string BlobName;

            public ContainerBlobKey(string containerName, string blobName)
            {
                ContainerName = containerName;
                BlobName = blobName;
            }

            public bool Equals(ContainerBlobKey other)
            {
                return ContainerName == other.ContainerName &&
                       BlobName == other.BlobName;
            }

            public override bool Equals(object obj)
            {
                return (obj is ContainerBlobKey) && Equals((ContainerBlobKey)obj);
            }

            public override int GetHashCode()
            {
                return ContainerName.GetHashCode() ^ BlobName.GetHashCode();
            }
        }

        class CloudBlobService : ICloudBlobService
        {
            private CloudBlobClient _client;
            private CloudAppendBlob _appendBlob;
            private CloudBlobContainer _container;

            public void Connect(string connectionString)
            {
                _client = CloudStorageAccount.Parse(connectionString).CreateCloudBlobClient();
            }

            public Task AppendFromByteArrayAsync(string containerName, string blobName, string contentType, byte[] buffer, CancellationToken cancellationToken)
            {
                var blob = _appendBlob;
                var container = _container;
                if (containerName == null || container?.Name != containerName || blobName == null || blob?.Name != blobName)
                {
                    return InitializeAndCacheBlobAsync(containerName, blobName, contentType, cancellationToken).ContinueWith(async (t, b) => await t.Result.AppendFromByteArrayAsync((byte[])b, 0, ((byte[])b).Length).ConfigureAwait(false), buffer, cancellationToken);
                }
                else
                {
#if NETSTANDARD1_3
                    return blob.AppendFromByteArrayAsync(buffer, 0, buffer.Length);
#else
                    return blob.AppendFromByteArrayAsync(buffer, 0, buffer.Length, cancellationToken);
#endif
                }
            }

            async Task<CloudAppendBlob> InitializeAndCacheBlobAsync(string containerName, string blobName, string contentType, CancellationToken cancellationToken)
            {
                try
                {
                    var container = _container;
                    if (containerName == null || container?.Name != containerName)
                    {
                        container = await InitializeContainer(containerName, cancellationToken).ConfigureAwait(false);
                        if (container != null)
                        {
                            _appendBlob = null;
                            _container = container;
                        }
                    }

                    var blob = _appendBlob;
                    if (blobName == null || blob?.Name != blobName)
                    {
                        blob = await InitializeBlob(blobName, container, contentType, cancellationToken).ConfigureAwait(false);
                        if (blob != null && ReferenceEquals(container, _container))
                        {
                            _appendBlob = blob;
                        }
                    }
                    return blob;
                }
                catch (Exception exception)
                {
                    InternalLogger.Error(exception, "AzureBlobStorageTarget: Failed to initialize blob={0} in container={1}", blobName, containerName);
                    throw;
                }
            }

            /// <summary>
            /// Initializes the BLOB.
            /// </summary>
            /// <param name="blobName">Name of the BLOB.</param>
            private async Task<CloudAppendBlob> InitializeBlob(string blobName, CloudBlobContainer blobContainer, string contentType, CancellationToken cancellationToken)
            {
                var appendBlob = blobContainer.GetAppendBlobReference(blobName);

#if NETSTANDARD1_3
                var blobExits = await appendBlob.ExistsAsync().ConfigureAwait(false);
#else
                var blobExits = await appendBlob.ExistsAsync(cancellationToken).ConfigureAwait(false);
#endif
                if (blobExits)
                    return appendBlob;

                appendBlob.Properties.ContentType = contentType;
#if NETSTANDARD1_3
                await appendBlob.CreateOrReplaceAsync().ConfigureAwait(false);
#else
                await appendBlob.CreateOrReplaceAsync(cancellationToken).ConfigureAwait(false);
#endif
                return appendBlob;
            }

            /// <summary>
            /// Initializes the Azure storage container and creates it if it doesn't exist.
            /// </summary>
            /// <param name="containerName">Name of the container.</param>
            private async Task<CloudBlobContainer> InitializeContainer(string containerName, CancellationToken cancellationToken)
            {
                if (_client == null)
                    throw new InvalidOperationException("CloudBlobClient has not been initialized");

                var container = _client.GetContainerReference(containerName);

#if NETSTANDARD1_3
                var containerExists = await _container.ExistsAsync().ConfigureAwait(false);
#else
                var containerExists = await _container.ExistsAsync(cancellationToken).ConfigureAwait(false);
#endif
                if (containerExists)
                    return container;

#if NETSTANDARD1_3
                await container.CreateIfNotExistsAsync().ConfigureAwait(false);
#else
                await container.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
#endif
                return container;
            }
        }
    }
}
