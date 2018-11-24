using System;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace NLog.Extensions.AzureStorage
{
    /// <summary>
    /// Azure Table Storage NLog Target
    /// </summary>
    /// <seealso cref="NLog.Targets.TargetWithLayout" />
    [Target("AzureQueueStorage")]
    public sealed class QueueStorageTarget : TargetWithLayout
    {
        private CloudQueueClient _client;
        private CloudQueue _queue;
        private readonly AzureStorageNameCache _containerNameCache = new AzureStorageNameCache();
        private readonly Func<string, string> _checkAndRepairQueueNameDelegate;

        public string ConnectionString { get => (_connectionString as SimpleLayout)?.Text ?? null; set => _connectionString = value; }
        private Layout _connectionString;
        public string ConnectionStringKey { get; set; }

        [RequiredParameter]
        public Layout QueueName { get; set; }

        public QueueStorageTarget()
        {
            OptimizeBufferReuse = true;
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
                connectionString = ConnectionStringHelper.LookupConnectionString(_connectionString, ConnectionStringKey);
                _client = CloudStorageAccount.Parse(connectionString).CreateCloudQueueClient();
                InternalLogger.Trace("AzureQueueStorageTarget - Initialized");
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureQueueStorageTarget(Name={0}): Failed to create QueueClient with connectionString={1}.", Name, connectionString);
                throw;
            }
        }

        /// <summary>
        /// Writes logging event to the log target.
        /// classes.
        /// </summary>
        /// <param name="logEvent">Logging event to be written out.</param>
        protected override void Write(LogEventInfo logEvent)
        {
            if (String.IsNullOrEmpty(logEvent.Message))
                return;

            var queueName = RenderLogEvent(QueueName, logEvent);
            try
            {
                queueName = _containerNameCache.LookupStorageName(queueName, _checkAndRepairQueueNameDelegate);
                InitializeQueue(queueName);
                var layoutMessage = RenderLogEvent(Layout, logEvent);
                var queueMessage = new CloudQueueMessage(layoutMessage);
                QueueAddMessage(_queue, queueMessage);
            }
            catch (StorageException ex)
            {
                InternalLogger.Error(ex, "AzureQueueStorageTarget(Name={0}): failed writing to queue: {1}", Name, queueName);
                throw;
            }
        }

        private static void QueueAddMessage(CloudQueue cloudQueue, CloudQueueMessage queueMessage)
        {
#if NETSTANDARD
            cloudQueue.AddMessageAsync(queueMessage);
#else
            cloudQueue.AddMessage(queueMessage);
#endif
        }

        private void QueueCreateIfNotExists(CloudQueue cloudQueue)
        {
#if NETSTANDARD
            cloudQueue.CreateIfNotExistsAsync().GetAwaiter().GetResult();
#else
            cloudQueue.CreateIfNotExists();
#endif
        }

        /// <summary>
        /// Initializes the Azure storage queue and creates it if it doesn't exist.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        private void InitializeQueue(string queueName)
        {
            if (_queue == null || _queue.Name != queueName)
            {
                try
                {
                    _queue = _client.GetQueueReference(queueName);
                    QueueCreateIfNotExists(_queue);
                }
                catch (StorageException storageException)
                {
                    InternalLogger.Error(storageException, "AzureQueueStorageTarget(Name={0}): Failed to create reference to queue {1}", Name, queueName);
                    throw;
                }
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
    }
}
