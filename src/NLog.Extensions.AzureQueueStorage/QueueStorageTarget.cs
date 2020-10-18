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
        /// Alternative to ConnectionString
        /// </summary>
        public Layout TenantIdentity { get; set; }

        /// <summary>
        /// Alternative to ConnectionString (Defaults to https://storage.azure.com when not set)
        /// </summary>
        public Layout ResourceIdentity { get; set; }

        [ArrayParameter(typeof(TargetPropertyWithContext), "metadata")]
        public IList<TargetPropertyWithContext> QueueMetadata { get; private set; }

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
            string resourceIdentity = string.Empty;

            Dictionary<string, string> queueMetadata = null;

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

                _cloudQueueService.Connect(connectionString, serviceUri, tenantIdentity, resourceIdentity, queueMetadata);
                InternalLogger.Trace("AzureQueueStorageTarget - Initialized");
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
        
        /// <inheritdoc/>
        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            var queueName = RenderLogEvent(QueueName, logEvent);

            try
            {
                queueName = _containerNameCache.LookupStorageName(queueName, _checkAndRepairQueueNameDelegate);

                var layoutMessage = RenderLogEvent(Layout, logEvent);

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

        private class CloudQueueService : ICloudQueueService
        {
            private QueueServiceClient _client;
            private QueueClient _queue;
            private IDictionary<string, string> _queueMetadata;

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

            public void Connect(string connectionString, string serviceUri, string tenantIdentity, string resourceIdentity, IDictionary<string, string> queueMetadata)
            {
                _queueMetadata = queueMetadata;

                if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(serviceUri))
                {
                    var tokenCredential = new AzureServiceTokenProviderCredentials(tenantIdentity, resourceIdentity);
                    _client = new QueueServiceClient(new Uri(serviceUri), tokenCredential);
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
                    return InitializeAndCacheQueueAsync(queueName, cancellationToken).ContinueWith(async (t, m) => await t.Result.SendMessageAsync((string)m, cancellationToken).ConfigureAwait(false), queueMessage, cancellationToken);
                }
                else
                {
                    return queue.SendMessageAsync(queueMessage, cancellationToken);
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
