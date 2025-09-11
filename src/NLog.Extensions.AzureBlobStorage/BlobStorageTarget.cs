using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using NLog.Common;
using NLog.Config;
using NLog.Extensions.AzureBlobStorage;
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

        /// <summary>
        /// Gets or sets the Azure Storage connection string. Alternative to <see cref="ServiceUri"/>.
        /// </summary>
        public Layout ConnectionString { get; set; }

        /// <summary>
        /// Uri to reference the blob service (e.g. https://{account_name}.blob.core.windows.net). 
        /// Input for <see cref="BlobServiceClient"/>. Required, when <see cref="ConnectionString"/> is not configured. Overrides <see cref="ConnectionString"/> when both are set.
        /// </summary>
        public Layout ServiceUri { get; set; }

        /// <summary>
        /// Obsolete instead use <see cref="ServiceUri"/>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use ServiceUri")]
        public Layout ServiceUrl { get => ServiceUri; set => ServiceUri = value; }

        /// <summary>
        /// TenantId for <see cref="Azure.Identity.DefaultAzureCredentialOptions"/> and <see cref="Azure.Identity.ClientSecretCredential"/>. Requires <see cref="ServiceUri"/>.
        /// </summary>
        public Layout TenantIdentity { get; set; }

        /// <summary>
        /// Obsolete instead use <see cref="ManagedIdentityResourceId"/>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use ManagedIdentityResourceId")]
        public Layout ResourceIdentity { get => ManagedIdentityResourceId; set => ManagedIdentityResourceId = value; }

        /// <summary>
        /// ResourceId for <see cref="Azure.Identity.DefaultAzureCredentialOptions.ManagedIdentityResourceId"/> on <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Requires <see cref="ServiceUri"/> .
        /// </summary>
        /// <remarks>
        /// Do not configure this value together with <see cref="ManagedIdentityClientId"/>
        /// </remarks>
        public Layout ManagedIdentityResourceId { get; set; }

        /// <summary>
        /// Obsolete instead use <see cref="ManagedIdentityClientId"/>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use ManagedIdentityClientId")]
        public Layout ClientIdentity { get => ManagedIdentityClientId; set => ManagedIdentityClientId = value; }

        /// <summary>
        /// ManagedIdentityClientId for <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Requires <see cref="ServiceUri"/>.
        /// </summary>
        /// <remarks>
        /// If this value is configured, then <see cref="ManagedIdentityResourceId"/> should not be configured.
        /// </remarks>
        public Layout ManagedIdentityClientId { get; set; }

        /// <summary>
        /// Access signature for <see cref="Azure.AzureSasCredential"/> authentication. Requires <see cref="ServiceUri"/>.
        /// </summary>
        public Layout SharedAccessSignature { get; set; }

        /// <summary>
        /// AccountName for <see cref="Azure.Storage.StorageSharedKeyCredential"/> authentication. Requires <see cref="ServiceUri"/> and <see cref="AccessKey"/>.
        /// </summary>
        public Layout AccountName { get; set; }

        /// <summary>
        /// AccountKey for <see cref="Azure.Storage.StorageSharedKeyCredential"/> authentication. Requires <see cref="ServiceUri"/> and <see cref="AccountName"/>.
        /// </summary>
        public Layout AccessKey { get; set; }

        /// <summary>
        /// clientId for <see cref="Azure.Identity.ClientSecretCredential"/> authentication. Requires <see cref="ServiceUri"/>, <see cref="TenantIdentity"/> and <see cref="ClientAuthSecret"/>.
        /// </summary>
        public Layout ClientAuthId { get; set; }

        /// <summary>
        /// clientSecret for <see cref="Azure.Identity.ClientSecretCredential"/> authentication. Requires <see cref="ServiceUri"/>, <see cref="TenantIdentity"/> and <see cref="ClientAuthId"/>.
        /// </summary>
        public Layout ClientAuthSecret { get; set; }

        /// <summary>
        /// Enables connection through a proxy server. If no <see cref="ProxyAddress"/> has been defined, the default proxy of the system will be used.
        /// </summary>
        public bool UseProxy { get; set; }

        /// <summary>
        /// Address of the proxy server to use (e.g. http://proxyserver:8080). Requires <see cref="UseProxy"/>
        /// </summary>
        public Layout ProxyAddress { get; set; }

        /// <summary>
        /// Login to use for the proxy server. Requires <see cref="ProxyPassword"/> and <see cref="UseProxy"/>
        /// </summary>
        public Layout ProxyLogin { get; set; }

        /// <summary>
        /// Password to use for the proxy server. Requires <see cref="ProxyLogin"/> and <see cref="UseProxy"/>
        /// </summary>
        public Layout ProxyPassword { get; set; }

        /// <summary>
        /// Uses the default credentials for the proxy server, overriding any values that may have been set in <see cref="ProxyLogin"/> and <see cref="ProxyPassword"/>. Requires <see cref="UseProxy"/>
        /// </summary>
        public bool UseDefaultCredentialsForProxy { get; set; }

        /// <summary>
        /// Name of the Blob storage Container
        /// </summary>
        [RequiredParameter]
        public Layout Container { get; set; }

        /// <summary>
        /// Gets or sets the Azure Blob Storage container name.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use Container")]
        public Layout ContainerName { get => Container; set => Container = value; }

        /// <summary>
        /// name of the Blob Storage Blob
        /// </summary>
        [RequiredParameter]
        public Layout BlobName { get; set; }

        /// <summary>
        /// Gets or sets the MIME content type for blob storage.
        /// </summary>
        /// <remarks>Default: "text/plain"</remarks>
        public string ContentType { get; set; } = "text/plain";

        /// <summary>
        /// Gets the collection of custom metadata key-value pairs to attach to the blob.
        /// </summary>
        [ArrayParameter(typeof(TargetPropertyWithContext), "metadata")]
        public IList<TargetPropertyWithContext> BlobMetadata { get; private set; }

        /// <summary>
        /// Gets the collection of blob tags for categorization and filtering.
        /// </summary>
        [ArrayParameter(typeof(TargetPropertyWithContext), "tag")]
        public IList<TargetPropertyWithContext> BlobTags { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobStorageTarget"/> class.
        /// </summary>
        public BlobStorageTarget()
            :this(new CloudBlobService())
        {
        }

        internal BlobStorageTarget(ICloudBlobService cloudBlobService)
        {
            TaskDelayMilliseconds = 200;
            BatchSize = 100;
            RetryDelayMilliseconds = 100;

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
            string managedIdentityResourceId = string.Empty;
            string managedIdentityClientId = string.Empty;
            string sharedAccessSignature = string.Empty;
            string storageAccountName = string.Empty;
            string storageAccountAccessKey = string.Empty;
            string clientAuthId = string.Empty;
            string clientAuthSecret = string.Empty;

            Dictionary<string, string> blobMetadata = null;
            Dictionary<string, string> blobTags = null;
            ProxySettings proxySettings = null;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                connectionString = ConnectionString?.Render(defaultLogEvent);
                if (string.IsNullOrEmpty(connectionString))
                {
                    serviceUri = ServiceUri?.Render(defaultLogEvent);
                    tenantIdentity = TenantIdentity?.Render(defaultLogEvent);
                    managedIdentityResourceId = ManagedIdentityResourceId?.Render(defaultLogEvent);
                    managedIdentityClientId = ManagedIdentityClientId?.Render(defaultLogEvent);
                    sharedAccessSignature = SharedAccessSignature?.Render(defaultLogEvent);
                    storageAccountName = AccountName?.Render(defaultLogEvent);
                    storageAccountAccessKey = AccessKey?.Render(defaultLogEvent);
                    clientAuthId = ClientAuthId?.Render(defaultLogEvent);
                    clientAuthSecret = ClientAuthSecret?.Render(defaultLogEvent);
                }
                if (UseProxy)
                {
                    proxySettings = new ProxySettings();
                    proxySettings.UseDefaultCredentials = UseDefaultCredentialsForProxy;
                    proxySettings.Address = ProxyAddress?.Render(defaultLogEvent);
                    proxySettings.Login = ProxyLogin?.Render(defaultLogEvent);
                    proxySettings.Password = ProxyPassword?.Render(defaultLogEvent);
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

                _cloudBlobService.Connect(connectionString, serviceUri, tenantIdentity, managedIdentityResourceId, managedIdentityClientId, sharedAccessSignature, storageAccountName, storageAccountAccessKey, clientAuthId, clientAuthSecret, blobMetadata, blobTags, proxySettings);
                InternalLogger.Debug("AzureBlobStorageTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(serviceUri))
                    InternalLogger.Error(ex, "AzureBlobStorageTarget(Name={0}): Failed to create BlobClient with ServiceUri={1}.", Name, serviceUri);
                else
                    InternalLogger.Error(ex, "AzureBlobStorageTarget(Name={0}): Failed to create BlobClient with connectionString={1}.", Name, connectionString);
                throw;
            }
        }

        /// <summary>
        /// Override this to provide async task for writing a single logevent.
        /// </summary>
        /// <param name="logEvent">The log event.</param>
        /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
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
        /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
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
                InternalLogger.Trace("AzureBlobStorageTarget(Name={0}): Using Container Name: {1}", Name, validContainerName);
            }
            else
            {
                InternalLogger.Trace("AzureBlobStorageTarget(Name={0}): Using Cleaned Container name: {1}", Name, validContainerName);
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

            public void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, IDictionary<string, string> blobMetadata, IDictionary<string, string> blobTags, ProxySettings proxySettings = null)
            {
                _blobMetadata = blobMetadata?.Count > 0 ? blobMetadata : null;
                _blobTags = blobTags?.Count > 0 ? blobTags : null;
                BlobClientOptions options = CreateBlobClientOptions(proxySettings);
                if (string.IsNullOrWhiteSpace(serviceUri))
                {
                    _client = new BlobServiceClient(connectionString, options);
                }
                else if (!string.IsNullOrEmpty(sharedAccessSignature))
                {
                    _client = new BlobServiceClient(new Uri(serviceUri), new Azure.AzureSasCredential(sharedAccessSignature), options);
                }
                else if (!string.IsNullOrWhiteSpace(storageAccountName))
                {
                    _client = new BlobServiceClient(new Uri(serviceUri), new Azure.Storage.StorageSharedKeyCredential(storageAccountName, storageAccountAccessKey), options);
                }
                else if (!string.IsNullOrEmpty(clientAuthId) && !string.IsNullOrEmpty(clientAuthSecret) && !string.IsNullOrEmpty(tenantIdentity))
                {
                    var tokenCredentials = new Azure.Identity.ClientSecretCredential(tenantIdentity, clientAuthId, clientAuthSecret);
                    _client = new BlobServiceClient(new Uri(serviceUri), tokenCredentials, options);
                }
                else
                {
                    var tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(managedIdentityClientId, tenantIdentity, managedIdentityResourceId);
                    _client = new BlobServiceClient(new Uri(serviceUri), tokenCredentials, options);
                }
            }

            private static BlobClientOptions CreateBlobClientOptions(ProxySettings proxySettings)
            {
                if (proxySettings == null)
                    return null;
                return new BlobClientOptions()
                {
                    Transport = new HttpClientTransport(new HttpClient(new HttpClientHandler
                    {
                        UseProxy = true,
                        Proxy = ProxyHelper.CreateProxy(proxySettings),
                        UseDefaultCredentials = proxySettings?.UseDefaultCredentials ?? false
                    }))
                };
            }

            public Task AppendFromByteArrayAsync(string containerName, string blobName, string contentType, byte[] buffer, CancellationToken cancellationToken)
            {
                var stream = new System.IO.MemoryStream(buffer);

                var blob = _appendBlob;
                var container = _container;
                if (string.IsNullOrEmpty(containerName) || container?.Name != containerName || string.IsNullOrEmpty(blobName) || blob?.Name != blobName)
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
                    if (string.IsNullOrEmpty(containerName) || container?.Name != containerName)
                    {
                        container = await InitializeContainer(containerName, cancellationToken).ConfigureAwait(false);
                        if (container != null)
                        {
                            _appendBlob = null;
                            _container = container;
                        }
                    }

                    var blob = _appendBlob;
                    if (string.IsNullOrEmpty(blobName) || blob?.Name != blobName)
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
            /// <param name="blobContainer">The blob container client.</param>
            /// <param name="contentType">MIME content type for the blob.</param>
            /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
            private async Task<AppendBlobClient> InitializeBlob(string blobName, BlobContainerClient blobContainer, string contentType, CancellationToken cancellationToken)
            {
                InternalLogger.Debug("AzureBlobStorageTarget: Initializing blob: {0}", blobName);
                var appendBlob = blobContainer.GetAppendBlobClient(blobName);

                var blobExits = await appendBlob.ExistsAsync(cancellationToken).ConfigureAwait(false);
                if (blobExits)
                    return appendBlob;

                InternalLogger.Debug("AzureBlobStorageTarget: Creating new blob: {0}", blobName);
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
            /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
            private async Task<BlobContainerClient> InitializeContainer(string containerName, CancellationToken cancellationToken)
            {
                if (_client == null)
                    throw new InvalidOperationException("BlobServiceClient has not been initialized");

                InternalLogger.Debug("AzureBlobStorageTarget: Initializing container: {0}", containerName);
                var container = _client.GetBlobContainerClient(containerName);

                var containerExists = await container.ExistsAsync(cancellationToken).ConfigureAwait(false);
                if (containerExists)
                    return container;

                InternalLogger.Debug("AzureBlobStorageTarget: Creating new container: {0}", containerName);
                await container.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None, null, cancellationToken).ConfigureAwait(false);
                return container;
            }
        }
    }
}
