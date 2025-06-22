using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Extensions.AzureStorage;

namespace NLog.Targets
{
    /// <summary>
    /// Azure Queue Storage NLog Target
    /// </summary>
    [Target("AzureQueueStorage")]
    public sealed class QueueStorageTarget : AsyncTaskTarget
    {
        private readonly ICloudQueueService _cloudQueueService;
        private readonly AzureStorageNameCache _containerNameCache = new AzureStorageNameCache();
        private readonly Func<string, string> _checkAndRepairQueueNameDelegate;

        public Layout ConnectionString { get; set; }

        [RequiredParameter]
        public Layout QueueName { get; set; }

        /// <summary>
        /// Alternative to ConnectionString
        /// </summary>
        public Layout ServiceUri { get; set; }

        /// <summary>
        /// Obsolete instead use <see cref="ServiceUri"/>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use ServiceUri")]
        public Layout ServiceUrl { get => ServiceUri; set => ServiceUri = value; }

        /// <summary>
        /// TenantId for <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Requires <see cref="ServiceUri"/>.
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

        [ArrayParameter(typeof(TargetPropertyWithContext), "metadata")]
        public IList<TargetPropertyWithContext> QueueMetadata { get; private set; }

        /// <summary>
        /// The default time to live value for the message.
        /// </summary>
        /// <remarks>
        /// Messages older than their TimeToLive value will expire and no longer be retained
        /// in the message store. Subscribers will be unable to receive expired messages.
        /// </remarks>
        public Layout TimeToLiveSeconds { get; set; }

        /// <summary>
        /// The default time to live value for the message.
        /// </summary>
        /// <remarks>
        /// Messages older than their TimeToLive value will expire and no longer be retained
        /// in the message store. Subscribers will be unable to receive expired messages.
        /// </remarks>
        public Layout TimeToLiveDays { get; set; }

        public QueueStorageTarget()
            :this(new CloudQueueService())
        {
        }

        internal QueueStorageTarget(ICloudQueueService cloudQueueService)
        {
            RetryDelayMilliseconds = 100;

            QueueMetadata = new List<TargetPropertyWithContext>();
            _cloudQueueService = cloudQueueService;
            _checkAndRepairQueueNameDelegate = CheckAndRepairQueueNamingRules;
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

            Dictionary<string, string> queueMetadata = null;

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
                }

                if (QueueMetadata?.Count > 0)
                {
                    queueMetadata = new Dictionary<string, string>();
                    foreach (var metadata in QueueMetadata)
                    {
                        if (string.IsNullOrWhiteSpace(metadata.Name))
                            continue;

                        var metadataValue = metadata.Layout?.Render(defaultLogEvent);
                        if (string.IsNullOrEmpty(metadataValue))
                            continue;

                        queueMetadata[metadata.Name.Trim()] = metadataValue;
                    }
                }

                var timeToLive = RenderDefaultTimeToLive();
                if (timeToLive <= TimeSpan.Zero)
                {
                    timeToLive = default(TimeSpan?);
                }

                _cloudQueueService.Connect(connectionString, serviceUri, tenantIdentity, managedIdentityResourceId, managedIdentityClientId, sharedAccessSignature, storageAccountName, storageAccountAccessKey, timeToLive, queueMetadata);
                InternalLogger.Debug("AzureQueueStorageTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(serviceUri))
                    InternalLogger.Error(ex, "AzureQueueStorageTarget(Name={0}): Failed to create QueueClient with ServiceUri={1}.", Name, serviceUri);
                else
                    InternalLogger.Error(ex, "AzureQueueStorageTarget(Name={0}): Failed to create QueueClient with connectionString={1}.", Name, connectionString);
                throw;
            }
        }

        private TimeSpan? RenderDefaultTimeToLive()
        {
            string timeToLiveSeconds = null;
            string timeToLiveDays = null;

            try
            {
                timeToLiveSeconds = TimeToLiveSeconds?.Render(LogEventInfo.CreateNullEvent());
                if (!string.IsNullOrEmpty(timeToLiveSeconds))
                {
                    if (int.TryParse(timeToLiveSeconds, out var resultSeconds))
                    {
                        return TimeSpan.FromSeconds(resultSeconds);
                    }
                    else
                    {
                        InternalLogger.Error("AzureQueueStorageTarget(Name={0}): Failed to parse TimeToLiveSeconds={1}", Name, timeToLiveSeconds);
                    }
                }
                else
                {
                    timeToLiveDays = TimeToLiveDays?.Render(LogEventInfo.CreateNullEvent());
                    if (!string.IsNullOrEmpty(timeToLiveDays))
                    {
                        if (int.TryParse(timeToLiveDays, out var resultDays))
                        {
                            return TimeSpan.FromDays(resultDays);
                        }
                        else
                        {
                            InternalLogger.Error("AzureQueueStorageTarget(Name={0}): Failed to parse TimeToLiveDays={1}", Name, timeToLiveDays);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureQueueStorageTarget(Name={0}): Failed to parse TimeToLive value. Seconds={1}, Days={2}", Name, timeToLiveSeconds, timeToLiveDays);
            }

            return default(TimeSpan?);
        }

        /// <inheritdoc/>
        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            var queueName = RenderLogEvent(QueueName, logEvent);
            var layoutMessage = RenderLogEvent(Layout, logEvent);

            try
            {
                queueName = _containerNameCache.LookupStorageName(queueName, _checkAndRepairQueueNameDelegate);

                return _cloudQueueService.AddMessageAsync(queueName, layoutMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureQueueStorageTarget(Name={0}): failed writing to queue: {1}", Name, queueName);
                throw;
            }
        }

        private string CheckAndRepairQueueNamingRules(string queueName)
        {
            InternalLogger.Trace("AzureQueueStorageTarget(Name={0}): Requested Queue Name: {1}", Name, queueName);
            string validQueueName = AzureStorageNameCache.CheckAndRepairContainerNamingRules(queueName);
            if (validQueueName == queueName.ToLowerInvariant())
            {
                InternalLogger.Trace("AzureQueueStorageTarget(Name={0}): Using Queue Name: {1}", Name, validQueueName);
            }
            else
            {
                InternalLogger.Trace("AzureQueueStorageTarget(Name={0}): Using Cleaned Queue name: {1}", Name, validQueueName);
            }
            return validQueueName;
        }

        private sealed class CloudQueueService : ICloudQueueService
        {
            private QueueServiceClient _client;
            private QueueClient _queue;
            private IDictionary<string, string> _queueMetadata;
            private TimeSpan? _timeToLive;

            public void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, TimeSpan? timeToLive, IDictionary<string, string> queueMetadata)
            {
                _timeToLive = timeToLive;
                _queueMetadata = queueMetadata;

                if (string.IsNullOrWhiteSpace(serviceUri))
                {
                    _client = new QueueServiceClient(connectionString);
                }
                else if (!string.IsNullOrWhiteSpace(sharedAccessSignature))
                {
                    _client = new QueueServiceClient(new Uri(serviceUri), new Azure.AzureSasCredential(sharedAccessSignature));
                }
                else if (!string.IsNullOrWhiteSpace(storageAccountName))
                {
                    _client = new QueueServiceClient(new Uri(serviceUri), new Azure.Storage.StorageSharedKeyCredential(storageAccountName, storageAccountAccessKey));
                }
                else
                {
                    Azure.Core.TokenCredential tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(managedIdentityClientId, tenantIdentity, managedIdentityResourceId);
                    _client = new QueueServiceClient(new Uri(serviceUri), tokenCredentials);
                }
            }

            public Task AddMessageAsync(string queueName, string queueMessage, CancellationToken cancellationToken)
            {
                var queue = _queue;
                if (string.IsNullOrEmpty(queueName) || queue?.Name != queueName)
                {
                    return InitializeAndCacheQueueAsync(queueName, cancellationToken).ContinueWith(async (t, m) => await t.Result.SendMessageAsync((string)m, null, _timeToLive, cancellationToken).ConfigureAwait(false), queueMessage, cancellationToken);
                }
                else
                {
                    return queue.SendMessageAsync(queueMessage, null, _timeToLive, cancellationToken);
                }
            }

            private async Task<QueueClient> InitializeAndCacheQueueAsync(string queueName, CancellationToken cancellationToken)
            {
                try
                {
                    if (_client == null)
                        throw new InvalidOperationException("QueueServiceClient has not been initialized");

                    InternalLogger.Debug("AzureQueueStorageTarget: Initializing queue: {0}", queueName);
                    var queue = _client.GetQueueClient(queueName);
                    bool queueExists = await queue.ExistsAsync(cancellationToken).ConfigureAwait(false);
                    if (!queueExists)
                    {
                        InternalLogger.Debug("AzureQueueStorageTarget: Creating new queue: {0}", queueName);
                        await queue.CreateIfNotExistsAsync(_queueMetadata, cancellationToken).ConfigureAwait(false);
                    }
                    _queue = queue;
                    return queue;
                }
                catch (Exception exception)
                {
                    InternalLogger.Error(exception, "AzureQueueStorageTarget: Failed to initialize queue {0}", queueName);
                    throw;
                }
            }
        }
    }
}
