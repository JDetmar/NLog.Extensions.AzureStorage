using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
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

        /// <summary>
        /// Alternative to ConnectionString
        /// </summary>
        public Layout ServiceUri { get; set; }

        /// <summary>
        /// Alternative to ConnectionString
        /// </summary>
        public Layout TenantIdentity { get; set; }

        /// <summary>
        /// Alternative to ConnectionString (Defaults to https://storage.azure.com when not set)
        /// </summary>
        public Layout ResourceIdentity { get; set; }

        [RequiredParameter]
        public Layout Container { get; set; }

        [RequiredParameter]
        public Layout BlobName { get; set; }

        public string ContentType { get; set; } = "text/plain";

        [ArrayParameter(typeof(TargetPropertyWithContext), "metadata")]
        public IList<TargetPropertyWithContext> BlobMetadata { get; private set; }

        [ArrayParameter(typeof(TargetPropertyWithContext), "tag")]
        public IList<TargetPropertyWithContext> BlobTags { get; private set; }

        private readonly LogEventInfo _defaultLogEvent = LogEventInfo.CreateNullEvent();

        public BlobStorageTarget()
            :this(new CloudBlobService())
        {
        }

        internal BlobStorageTarget(ICloudBlobService cloudBlobService)
        {
            TaskDelayMilliseconds = 200;
            BatchSize = 100;

            BlobMetadata = new List<TargetPropertyWithContext>();
            BlobTags = new List<TargetPropertyWithContext>();

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
            string serviceUri = string.Empty;
            string tenantIdentity = string.Empty;
            string resourceIdentity = string.Empty;

            Dictionary<string, string> blobMetadata = null;
            Dictionary<string, string> blobTags = null;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                connectionString = ConnectionString?.Render(defaultLogEvent);
                if (string.IsNullOrEmpty(connectionString))
                {
                    serviceUri = ServiceUri?.Render(defaultLogEvent);
                    tenantIdentity = TenantIdentity?.Render(defaultLogEvent);
                    resourceIdentity = ResourceIdentity?.Render(defaultLogEvent);
                }

                if (BlobMetadata?.Count > 0)
                {
                    blobMetadata = new Dictionary<string, string>();
                    foreach (var metadata in BlobMetadata)
                    {
                        if (string.IsNullOrWhiteSpace(metadata.Name))
                            continue;

                        var metadataValue = metadata.Layout?.Render(defaultLogEvent);
                        if (string.IsNullOrEmpty(metadataValue))
                            continue;

                        blobMetadata[metadata.Name.Trim()] = metadataValue;
                    }
                }

                if (BlobTags?.Count > 0)
                {
                    blobTags = new Dictionary<string, string>();
                    foreach (var tag in BlobTags)
                    {
                        if (string.IsNullOrWhiteSpace(tag.Name))
                            continue;

                        var metadataValue = tag.Layout?.Render(defaultLogEvent);
                        blobTags[tag.Name.Trim()] = metadataValue ?? string.Empty;
                    }
                }

                _cloudBlobService.Connect(connectionString, serviceUri, tenantIdentity, resourceIdentity, blobMetadata, blobTags);
                InternalLogger.Trace("AzureBlobStorageTarget - Initialized");
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(serviceUri))
                    InternalLogger.Error(ex, "AzureBlobStorageTarget(Name={0}): Failed to create BlobClient with ServiceUri={1}.", Name, serviceUri);
                else
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
                var containerName = RenderLogEvent(Container, logEvents[0]);
                var blobName = RenderLogEvent(BlobName, logEvents[0]);

                try
                {
                    var blobPayload = CreateBlobPayload(logEvents);
                    return WriteToBlobAsync(blobPayload, containerName, blobName, cancellationToken);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureBlobStorage(Name={0}): Failed writing {1} logevents to BlobName={2} in ContainerName={3}", Name, 1, blobName, containerName);
                    throw;
                }
            }

            var partitionBuckets = SortHelpers.BucketSort(logEvents, _getContainerBlobNameDelegate);
            IList<Task> multipleTasks = partitionBuckets.Count > 1 ? new List<Task>(partitionBuckets.Count) : null;
            foreach (var partitionBucket in partitionBuckets)
            {
                var containerName = partitionBucket.Key.ContainerName;
                var blobName = partitionBucket.Key.BlobName;
                var bucketSize = partitionBucket.Value.Count;

                try
                {
                    var blobPayload = CreateBlobPayload(partitionBucket.Value);
                    var sendTask = WriteToBlobAsync(blobPayload, containerName, blobName, cancellationToken);
                    if (multipleTasks == null)
                        return sendTask;

                    multipleTasks.Add(sendTask);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureBlobStorage(Name={0}): Failed writing {1} logevents to BlobName={2} in ContainerName={3}", Name, bucketSize, blobName, containerName);
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

        private Task WriteToBlobAsync(byte[] buffer, string containerName, string blobName, CancellationToken cancellationToken)
        {
            containerName = CheckAndRepairContainerName(containerName);
            blobName = CheckAndRepairBlobNamingRules(blobName);

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
                ContainerName = containerName ?? string.Empty;
                BlobName = blobName ?? string.Empty;
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
            private IDictionary<string, string> _blobMetadata;
            private IDictionary<string, string> _blobTags;

            private BlobServiceClient _client;
            private AppendBlobClient _appendBlob;
            private BlobContainerClient _container;

            private class AzureServiceTokenProviderCredentials : Azure.Core.TokenCredential
            {
                private readonly string _resourceIdentity;
                private readonly string _tenantIdentity;
                private readonly Microsoft.Azure.Services.AppAuthentication.AzureServiceTokenProvider _tokenProvider;

                public AzureServiceTokenProviderCredentials(string tenantIdentity, string resourceIdentity)
                {
                    if (string.IsNullOrWhiteSpace(_resourceIdentity))
                        _resourceIdentity = "https://storage.azure.com/";
                    else
                        _resourceIdentity = resourceIdentity;
                    if (!string.IsNullOrWhiteSpace(tenantIdentity))
                        _tenantIdentity = tenantIdentity;
                    _tokenProvider = new Microsoft.Azure.Services.AppAuthentication.AzureServiceTokenProvider();
                }

                public override async ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
                {
                    try
                    {
                        var result = await _tokenProvider.GetAuthenticationResultAsync(_resourceIdentity, _tenantIdentity, cancellationToken: cancellationToken).ConfigureAwait(false);
                        return new Azure.Core.AccessToken(result.AccessToken, result.ExpiresOn);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error(ex, "AzureBlobStorageTarget - Failed getting AccessToken from AzureServiceTokenProvider for resource {0}", _resourceIdentity);
                        throw;
                    }
                }

                public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
                {
                    return GetTokenAsync(requestContext, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }

            public void Connect(string connectionString, string serviceUri, string tenantIdentity, string resourceIdentity, IDictionary<string, string> blobMetadata, IDictionary<string, string> blobTags)
            {
                _blobMetadata = blobMetadata?.Count > 0 ? blobMetadata : null;
                _blobTags = blobTags?.Count > 0 ? blobTags : null;

                if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(serviceUri))
                {
                    var tokenCredential = new AzureServiceTokenProviderCredentials(tenantIdentity, resourceIdentity);
                    _client = new BlobServiceClient(new Uri(serviceUri), tokenCredential);
                }
                else
                {
                    _client = new BlobServiceClient(connectionString);
                }
            }

            public Task AppendFromByteArrayAsync(string containerName, string blobName, string contentType, byte[] buffer, CancellationToken cancellationToken)
            {
                var stream = new System.IO.MemoryStream(buffer);

                var blob = _appendBlob;
                var container = _container;
                if (containerName == null || container?.Name != containerName || blobName == null || blob?.Name != blobName)
                {
                    return InitializeAndCacheBlobAsync(containerName, blobName, contentType, cancellationToken).ContinueWith(async (t, s) => await t.Result.AppendBlockAsync((System.IO.Stream)s, null).ConfigureAwait(false), stream, cancellationToken);
                }
                else
                {
                    return blob.AppendBlockAsync(stream, cancellationToken: cancellationToken);
                }
            }

            async Task<AppendBlobClient> InitializeAndCacheBlobAsync(string containerName, string blobName, string contentType, CancellationToken cancellationToken)
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
            private async Task<AppendBlobClient> InitializeBlob(string blobName, BlobContainerClient blobContainer, string contentType, CancellationToken cancellationToken)
            {
                var appendBlob = blobContainer.GetAppendBlobClient(blobName);

                var blobExits = await appendBlob.ExistsAsync(cancellationToken).ConfigureAwait(false);
                if (blobExits)
                    return appendBlob;

                var blobCreateOptions = new Azure.Storage.Blobs.Models.AppendBlobCreateOptions();
                var httpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders()
                {
                    ContentType = contentType,
                    ContentEncoding = Encoding.UTF8.WebName,
                };
                blobCreateOptions.HttpHeaders = httpHeaders;
                blobCreateOptions.Metadata = _blobMetadata;     // Optional custom metadata to set for this append blob.
                blobCreateOptions.Tags = _blobTags;             // Options tags to set for this append blob.

                await appendBlob.CreateIfNotExistsAsync(blobCreateOptions, cancellationToken).ConfigureAwait(false);
                return appendBlob;
            }

            /// <summary>
            /// Initializes the Azure storage container and creates it if it doesn't exist.
            /// </summary>
            /// <param name="containerName">Name of the container.</param>
            private async Task<BlobContainerClient> InitializeContainer(string containerName, CancellationToken cancellationToken)
            {
                if (_client == null)
                    throw new InvalidOperationException("CloudBlobClient has not been initialized");

                var container = _client.GetBlobContainerClient(containerName);

                var containerExists = await container.ExistsAsync(cancellationToken).ConfigureAwait(false);
                if (containerExists)
                    return container;

                await container.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None, null, cancellationToken).ConfigureAwait(false);
                return container;
            }
        }
    }
}
