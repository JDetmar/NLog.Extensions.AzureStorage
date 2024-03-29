﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#if NETSTANDARD2_0 || NET461
using Microsoft.Azure.Cosmos.Table;
#else
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
#endif
using NLog.Common;
using NLog.Config;
using NLog.Extensions.AzureStorage;
using NLog.Layouts;

namespace NLog.Targets
{
    /// <summary>
    /// Azure Table Storage NLog Target
    /// </summary>
    [Target("AzureCosmosTable")]
    public sealed class TableStorageTarget : AsyncTaskTarget
    {
        private readonly ICloudTableService _cloudTableService;
        private string _machineName;
        private readonly AzureStorageNameCache _containerNameCache = new AzureStorageNameCache();
        private readonly Func<string, string> _checkAndRepairTableNameDelegate;

        //Delegates for bucket sorting
        private SortHelpers.KeySelector<LogEventInfo, TablePartitionKey> _getTablePartitionNameDelegate;

        struct TablePartitionKey : IEquatable<TablePartitionKey>
        {
            public readonly string TableName;
            public readonly string PartitionId;

            public TablePartitionKey(string tableName, string partitionId)
            {
                TableName = tableName ?? string.Empty;
                PartitionId = partitionId ?? string.Empty;
            }

            public bool Equals(TablePartitionKey other)
            {
                return TableName == other.TableName &&
                       PartitionId == other.PartitionId;
            }

            public override bool Equals(object obj)
            {
                return (obj is TablePartitionKey) && Equals((TablePartitionKey)obj);
            }

            public override int GetHashCode()
            {
                return TableName.GetHashCode() ^ PartitionId.GetHashCode();
            }
        }

        public Layout ConnectionString { get; set; }
        public string ConnectionStringKey { get; set; }

        [RequiredParameter]
        public Layout TableName { get; set; }

        [RequiredParameter]
        public Layout PartitionKey { get; set; } = "${logger}";

        [RequiredParameter]
        public Layout RowKey { get; set; }

        public string LogTimeStampFormat { get; set; } = "O";

        public Layout TimeToLiveSeconds { get; set; }

        public Layout TimeToLiveDays { get; set; }

        public TableStorageTarget()
            :this(new CloudTableService())
        {
        }

        internal TableStorageTarget(ICloudTableService cloudTableService)
        {
            TaskDelayMilliseconds = 200;
            BatchSize = 100;

            RowKey = Layout.FromMethod(l => string.Concat((DateTime.MaxValue.Ticks - l.TimeStamp.Ticks).ToString("d19"), "__", Guid.NewGuid().ToString()), LayoutRenderOptions.ThreadAgnostic);

            _cloudTableService = cloudTableService;
            _checkAndRepairTableNameDelegate = CheckAndRepairTableNamingRules;
        }

        /// <summary>
        /// Initializes the target. Can be used by inheriting classes
        /// to initialize logging.
        /// </summary>
        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            _machineName = GetMachineName();

            string connectionString = string.Empty;
            try
            {
                connectionString = ConnectionStringHelper.LookupConnectionString(ConnectionString, ConnectionStringKey);
                TimeSpan defaultTimeToLive = RenderDefaultTimeToLive();
                _cloudTableService.Connect(connectionString, (int)defaultTimeToLive.TotalSeconds);
                InternalLogger.Trace("AzureTableStorageTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureTableStorageTarget(Name={0}): Failed to create TableClient with connectionString={1}.", Name, connectionString);
                throw;
            }
        }

        private TimeSpan RenderDefaultTimeToLive()
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
                        InternalLogger.Error("AzureTableStorageTarget(Name={0}): Failed to parse TimeToLiveSeconds={1}", Name, timeToLiveSeconds);
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
                            InternalLogger.Error("AzureTableStorageTarget(Name={0}): Failed to parse TimeToLiveDays={1}", Name, timeToLiveDays);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureTableStorageTarget(Name={0}): Failed to parse TimeToLive value. Seconds={1}, Days={2}", Name, timeToLiveSeconds, timeToLiveDays);
            }

            return TimeSpan.Zero;
        }

        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteAsyncTask(IList<LogEventInfo> logEvents, CancellationToken cancellationToken)
        {
            //must sort into containers and then into the blobs for the container
            if (_getTablePartitionNameDelegate == null)
                _getTablePartitionNameDelegate = logEvent => new TablePartitionKey(RenderLogEvent(TableName, logEvent), RenderLogEvent(PartitionKey, logEvent));

            if (logEvents.Count == 1)
            {
                var batchItem = GenerateBatch(logEvents, RenderLogEvent(PartitionKey, logEvents[0]));
                return WriteToTableAsync(RenderLogEvent(TableName, logEvents[0]), batchItem, cancellationToken);
            }

            const int BatchMaxSize = 100;

            var partitionBuckets = SortHelpers.BucketSort(logEvents, _getTablePartitionNameDelegate);
            IList<Task> multipleTasks = partitionBuckets.Count > 1 ? new List<Task>(partitionBuckets.Count) : null;
            foreach (var partitionBucket in partitionBuckets)
            {
                string tableName = partitionBucket.Key.TableName;

                try
                {
                    if (partitionBucket.Value.Count <= BatchMaxSize)
                    {
                        var batchItem = GenerateBatch(partitionBucket.Value, partitionBucket.Key.PartitionId);
                        var writeTask = WriteToTableAsync(partitionBucket.Key.TableName, batchItem, cancellationToken);
                        if (multipleTasks == null)
                            return writeTask;

                        multipleTasks.Add(writeTask);
                    }
                    else
                    {
                        // Must chain the tasks together so they don't run concurrently
                        var batchCollection = GenerateBatches(partitionBucket.Value, partitionBucket.Key.PartitionId, BatchMaxSize);
                        Task writeTask = WriteMultipleBatchesAsync(batchCollection, tableName, cancellationToken);
                        if (multipleTasks == null)
                            return writeTask;

                        multipleTasks.Add(writeTask);
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureTableStorageTarget(Name={0}): Failed to write table={1}", Name, tableName);
                    if (multipleTasks == null)
                        throw;
                }
            }

            return Task.WhenAll(multipleTasks ?? new Task[0]);
        }

        private async Task WriteMultipleBatchesAsync(IEnumerable<TableBatchOperation> batchCollection, string tableName, CancellationToken cancellationToken)
        {
            foreach (var batchItem in batchCollection)
            {
                await WriteToTableAsync(tableName, batchItem, cancellationToken);
            }
        }

        IEnumerable<TableBatchOperation> GenerateBatches(IList<LogEventInfo> source, string partitionId, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
                yield return GenerateBatch(source.Skip(i).Take(batchSize), partitionId);
        }

        private TableBatchOperation GenerateBatch(IEnumerable<LogEventInfo> logEvents, string partitionId)
        {
            var batch = new TableBatchOperation();
            foreach (var logEvent in logEvents)
            {
                var tableEntity = CreateTableEntity(logEvent, partitionId);
                batch.Insert(tableEntity);
            }
            return batch;
        }

        private Task WriteToTableAsync(string tableName, TableBatchOperation tableOperation, CancellationToken cancellationToken)
        {
            try
            {
                tableName = CheckAndRepairTableName(tableName);
                return _cloudTableService.ExecuteBatchAsync(tableName, tableOperation, cancellationToken);
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureTableStorageTarget(Name={0}): Failed to write table={1}", Name, tableName);
                throw;
            }
        }

        private ITableEntity CreateTableEntity(LogEventInfo logEvent, string partitionKey)
        {
            var rowKey = RenderLogEvent(RowKey, logEvent);

            if (ContextProperties.Count > 0)
            {
                DynamicTableEntity entity = new DynamicTableEntity();
                entity.PartitionKey = partitionKey;
                entity.RowKey = rowKey;

                bool logTimeStampOverridden = "LogTimeStamp".Equals(ContextProperties[0].Name, StringComparison.OrdinalIgnoreCase);
                if (!logTimeStampOverridden)
                {
                    entity.Properties.Add("LogTimeStamp", new EntityProperty(logEvent.TimeStamp.ToUniversalTime()));
                }

                for (int i = 0; i < ContextProperties.Count; ++i)
                {
                    var contextproperty = ContextProperties[i];
                    if (string.IsNullOrEmpty(contextproperty.Name))
                        continue;

                    var propertyValue = contextproperty.Layout != null ? RenderLogEvent(contextproperty.Layout, logEvent) : string.Empty;
                    if (logTimeStampOverridden && i == 0 && string.IsNullOrEmpty(propertyValue))
                        continue;

                    entity.Properties.Add(contextproperty.Name, new EntityProperty(propertyValue));
                }

                return entity;
            }
            else
            {
                var layoutMessage = RenderLogEvent(Layout, logEvent);
                var entity = new NLogEntity(logEvent, layoutMessage, _machineName, partitionKey, rowKey, LogTimeStampFormat);
                return entity;
            }
        }

        private string CheckAndRepairTableName(string tableName)
        {
            return _containerNameCache.LookupStorageName(tableName, _checkAndRepairTableNameDelegate);
        }

        private string CheckAndRepairTableNamingRules(string tableName)
        {
            InternalLogger.Trace("AzureTableStorageTarget(Name={0}): Requested Table Name: {1}", Name, tableName);
            string validTableName = AzureStorageNameCache.CheckAndRepairTableNamingRules(tableName);
            if (validTableName == tableName)
            {
                InternalLogger.Trace("AzureTableStorageTarget(Name={0}): Using Table Name: {0}", Name, validTableName);
            }
            else
            {
                InternalLogger.Trace("AzureTableStorageTarget(Name={0}): Using Cleaned Table name: {0}", Name, validTableName);
            }
            return validTableName;
        }

        /// <summary>
        /// Gets the machine name
        /// </summary>
        private static string GetMachineName()
        {
            return TryLookupValue(() => Environment.GetEnvironmentVariable("COMPUTERNAME"), "COMPUTERNAME")
                ?? TryLookupValue(() => Environment.GetEnvironmentVariable("HOSTNAME"), "HOSTNAME")
#if !NETSTANDARD1_3
                ?? TryLookupValue(() => Environment.MachineName, "MachineName")
#endif
                ?? TryLookupValue(() => System.Net.Dns.GetHostName(), "DnsHostName");
        }

        private static string TryLookupValue(Func<string> lookupFunc, string lookupType)
        {
            try
            {
                string lookupValue = lookupFunc()?.Trim();
                return string.IsNullOrEmpty(lookupValue) ? null : lookupValue;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn(ex, "AzureTableStorageTarget: Failed to lookup {0}", lookupType);
                return null;
            }
        }

        class CloudTableService : ICloudTableService
        {
            private CloudTableClient _client;
            private CloudTable _table;
            private int? _defaultTimeToLiveSeconds;

            public void Connect(string connectionString, int? defaultTimeToLiveSeconds)
            {
                _client = CloudStorageAccount.Parse(connectionString).CreateCloudTableClient();
                _defaultTimeToLiveSeconds = defaultTimeToLiveSeconds == 0 ? null : defaultTimeToLiveSeconds;
            }

            public Task ExecuteBatchAsync(string tableName, TableBatchOperation tableOperation, CancellationToken cancellationToken)
            {
                var table = _table;
                if (tableName == null || table?.Name != tableName)
                {
                    return InitializeAndCacheTableAsync(tableName, cancellationToken).ContinueWith(async (t, operation) => await t.Result.ExecuteBatchAsync((TableBatchOperation)operation).ConfigureAwait(false), tableOperation, cancellationToken);
                }
                else
                {
#if NETSTANDARD1_3
                    return table.ExecuteBatchAsync(tableOperation);
#else
                    return table.ExecuteBatchAsync(tableOperation, cancellationToken);
#endif
                }
            }

            async Task<CloudTable> InitializeAndCacheTableAsync(string tableName, CancellationToken cancellationToken)
            {
                try
                {
                    if (_client == null)
                        throw new InvalidOperationException("CloudTableClient has not been initialized");

                    var table = _client.GetTableReference(tableName);

#if NETSTANDARD1_3
                    var tableExists = await table.ExistsAsync().ConfigureAwait(false);
#else
                    var tableExists = await table.ExistsAsync(cancellationToken).ConfigureAwait(false);
#endif
                    if (!tableExists)
                    {
#if NETSTANDARD1_3
                        await table.CreateIfNotExistsAsync().ConfigureAwait(false);
#elif NET45
                        await table.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
#else
                        if (_defaultTimeToLiveSeconds.HasValue)
                            await table.CreateIfNotExistsAsync(Microsoft.Azure.Cosmos.IndexingMode.Consistent, null, _defaultTimeToLiveSeconds, cancellationToken).ConfigureAwait(false);
                        else
                            await table.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
#endif
                    }

                    _table = table;
                    return table;
                }
                catch (Exception exception)
                {
                    InternalLogger.Error(exception, "AzureTableStorageTarget: Failed to initialize table={1}", tableName);
                    throw;
                }
            }
        }
    }
}

