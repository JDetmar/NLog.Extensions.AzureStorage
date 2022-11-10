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
        /// Alternative to ConnectionString, when using <see cref="ServiceUri"/>
        /// </summary>
        public Layout TenantIdentity { get; set; }

        /// <summary>
        /// Alternative to ConnectionString, when using <see cref="ServiceUri"/> (Defaults to https://storage.azure.com when not set)
        /// </summary>
        public Layout ResourceIdentity { get; set; } = @"https://storage.azure.com/";

        /// <summary>
        /// Alternative to ConnectionString, when using <see cref="ServiceUri"/>
        /// </summary>
        public Layout ClientIdentity { get; set; }

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
            string resourceIdentifier = string.Empty;
            string clientIdentity = string.Empty;

            Dictionary<string, string> queueMetadata = null;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                connectionString = ConnectionString?.Render(defaultLogEvent);
                if (string.IsNullOrEmpty(connectionString))
                {
                    serviceUri = ServiceUri?.Render(defaultLogEvent);
                    tenantIdentity = TenantIdentity?.Render(defaultLogEvent);
                    resourceIdentifier = ResourceIdentity?.Render(defaultLogEvent);
                    clientIdentity = ClientIdentity?.Render(defaultLogEvent);
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

                _cloudQueueService.Connect(connectionString, serviceUri, tenantIdentity, resourceIdentifier, clientIdentity, timeToLive, queueMetadata);
                InternalLogger.Debug("AzureQueueStorageTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(serviceUri))
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
                InternalLogger.Trace("AzureQueueStorageTarget(Name={0}): Using Queue Name: {0}", Name, validQueueName);
            }
            else
            {
                InternalLogger.Trace("AzureQueueStorageTarget(Name={0}): Using Cleaned Queue name: {0}", Name, validQueueName);
            }
            return validQueueName;
        }

        private sealed class CloudQueueService : ICloudQueueService
        {
            private QueueServiceClient _client;
            private QueueClient _queue;
            private IDictionary<string, string> _queueMetadata;
            private TimeSpan? _timeToLive;

            public void Connect(string connectionString, string serviceUri, string tenantIdentity, string resourceIdentifier, string clientIdentity, TimeSpan? timeToLive, IDictionary<string, string> queueMetadata)
            {
                _timeToLive = timeToLive;
                _queueMetadata = queueMetadata;

                if (!string.IsNullOrWhiteSpace(serviceUri))
                {
                    Azure.Core.TokenCredential tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(clientIdentity, tenantIdentity, resourceIdentifier);
                    _client = new QueueServiceClient(new Uri(serviceUri), tokenCredentials);
                }
                else
                {
                    _client = new QueueServiceClient(connectionString);
                }
            }

            public Task AddMessageAsync(string queueName, string queueMessage, CancellationToken cancellationToken)
            {
                var queue = _queue;
                if (queueName == null || queue?.Name != queueName)
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
                        throw new InvalidOperationException("CloudQueueClient has not been initialized");

                    var queue = _client.GetQueueClient(queueName);
                    bool queueExists = await queue.ExistsAsync(cancellationToken).ConfigureAwait(false);
                    if (!queueExists)
                    {
                        await queue.CreateIfNotExistsAsync(_queueMetadata, cancellationToken).ConfigureAwait(false);
                    }
                    _queue = queue;
                    return queue;
                }
                catch (Exception exception)
                {
                    InternalLogger.Error(exception, "AzureQueueStorageTarget: Failed to initialize queue {1}", queueName);
                    throw;
                }
            }
        }
    }
}
