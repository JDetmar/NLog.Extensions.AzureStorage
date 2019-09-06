using System;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Extensions.AzureStorage;
using System.Threading;
using System.Threading.Tasks;

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
        public string ConnectionStringKey { get; set; }

        [RequiredParameter]
        public Layout QueueName { get; set; }

        public QueueStorageTarget()
            :this(new CloudQueueService())
        {
        }

        internal QueueStorageTarget(ICloudQueueService cloudQueueService)
        {
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
            try
            {
                connectionString = ConnectionStringHelper.LookupConnectionString(ConnectionString, ConnectionStringKey);
                _cloudQueueService.Connect(connectionString);
                InternalLogger.Trace("AzureQueueStorageTarget - Initialized");
            }
            catch (Exception ex)
            {
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
                var queueMessage = new CloudQueueMessage(layoutMessage);

                return _cloudQueueService.AddMessageAsync(queueName, queueMessage, cancellationToken);
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
            private CloudQueueClient _client;
            private CloudQueue _queue;

            public void Connect(string connectionString)
            {
                _client = CloudStorageAccount.Parse(connectionString).CreateCloudQueueClient();
            }

            public Task AddMessageAsync(string queueName, CloudQueueMessage queueMessage, CancellationToken cancellationToken)
            {
                var queue = _queue;
                if (queueName == null || queue?.Name != queueName)
                {
                    return InitializeAndCacheQueueAsync(queueName, cancellationToken).ContinueWith(async (t, m) => await t.Result.AddMessageAsync((CloudQueueMessage)m).ConfigureAwait(false), queueMessage, cancellationToken);
                }
                else
                {
#if NETSTANDARD1_3
                    return queue.AddMessageAsync(queueMessage);
#else
                    return queue.AddMessageAsync(queueMessage, cancellationToken);
#endif
                }
            }

            private async Task<CloudQueue> InitializeAndCacheQueueAsync(string queueName, CancellationToken cancellationToken)
            {
                try
                {
                    if (_client == null)
                        throw new InvalidOperationException("CloudQueueClient has not been initialized");

                    var queue = _client.GetQueueReference(queueName);
#if NETSTANDARD1_3
                    bool queueExists = await queue.ExistsAsync().ConfigureAwait(false);
#else
                    bool queueExists = await queue.ExistsAsync(cancellationToken).ConfigureAwait(false);
#endif
                    if (!queueExists)
                    {
#if NETSTANDARD1_3
                        await queue.CreateIfNotExistsAsync().ConfigureAwait(false);
#else
                        await queue.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
#endif
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
