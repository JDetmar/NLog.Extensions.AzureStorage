using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using NLog.Common;
using NLog.Config;
using NLog.Extensions.AzureStorage;
using NLog.Layouts;

namespace NLog.Targets
{
    /// <summary>
    /// Azure Table Storage NLog Target
    /// </summary>
    [Target("AzureDataTables")]
    public sealed class DataTablesTarget : AsyncTaskTarget
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
            public readonly string PartitionKey;

            public TablePartitionKey(string tableName, string partitionKey)
            {
                TableName = tableName;
                PartitionKey = partitionKey;
            }

            public bool Equals(TablePartitionKey other)
            {
                return TableName == other.TableName &&
                       PartitionKey == other.PartitionKey;
            }

            public override bool Equals(object obj)
            {
                return (obj is TablePartitionKey) && Equals((TablePartitionKey)obj);
            }

            public override int GetHashCode()
            {
                return TableName.GetHashCode() ^ PartitionKey.GetHashCode();
            }
        }

        public Layout ConnectionString { get; set; }
        public string ConnectionStringKey { get; set; }

        /// <summary>
        /// Alternative to ConnectionString. A System.Uri referencing the table service account
        /// </summary>
        /// <remarks>
        /// Ex. "https://{account_name}.table.core.windows.net/" or "https://{account_name}.table.cosmos.azure.com/".
        /// </remarks>
        public Layout ServiceUri { get; set; }

        /// <summary>
        /// Alternative to ConnectionString when using ServiceUri. For <see cref="Microsoft.Azure.Services.AppAuthentication.AzureServiceTokenProvider"/>
        /// </summary>
        public Layout TenantIdentity { get; set; }

        /// <summary>
        /// Alternative to ConnectionString when using ServiceUri. For <see cref="Microsoft.Azure.Services.AppAuthentication.AzureServiceTokenProvider"/>
        /// </summary>
        /// <remarks>
        /// Defaults to https://database.windows.net/ when not set
        /// </remarks>
        public Layout ResourceIdentity { get; set; }

        /// <summary>
        /// Alternative to ConnectionString when using ServiceUri. For <see cref="TableSharedKeyCredential"/> storage account name.
        /// </summary>
        /// <remarks>
        /// You'll need a Storage or Cosmos DB account name, primary key, and endpoint Uri. 
        /// You can obtain both from the Azure Portal by clicking Access Keys under Settings
        /// in the Portal Storage account blade or Connection String under Settings in the Portal Cosmos DB account blade.
        /// </remarks>
        public Layout AccountName { get; set; }

        /// <summary>
        /// Alternative to ConnectionString when using ServiceUri. For <see cref="TableSharedKeyCredential"/> access-key or <see cref="Azure.AzureSasCredential"/>  signature.
        /// </summary>
        public Layout AccessKey { get; set; }

        [RequiredParameter]
        public Layout TableName { get; set; }

        [RequiredParameter]
        public Layout PartitionKey { get; set; } = "${logger}";

        [RequiredParameter]
        public Layout RowKey { get; set; }

        public string LogTimeStampFormat { get; set; } = "O";

        public DataTablesTarget()
            :this(new CloudTableService())
        {
        }

        internal DataTablesTarget(ICloudTableService cloudTableService)
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
            string serviceUri = string.Empty;
            string tenantIdentity = string.Empty;
            string resourceIdentity = string.Empty;
            string accountName = string.Empty;
            string accessKey = string.Empty;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                connectionString = ConnectionString?.Render(defaultLogEvent);
                if (string.IsNullOrEmpty(connectionString))
                {
                    serviceUri = ServiceUri?.Render(defaultLogEvent);
                    tenantIdentity = TenantIdentity?.Render(defaultLogEvent);
                    resourceIdentity = ResourceIdentity?.Render(defaultLogEvent);
                    accountName = AccountName?.Render(defaultLogEvent);
                    accessKey = AccessKey?.Render(defaultLogEvent);
                }

                _cloudTableService.Connect(connectionString, serviceUri, tenantIdentity, resourceIdentity, accountName, accessKey);
                InternalLogger.Trace("AzureDataTablesTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureDataTablesTarget(Name={0}): Failed to create TableClient with connectionString={1}.", Name, connectionString);
                throw;
            }
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
                var tableName = RenderLogEvent(TableName, logEvents[0]);
                var partitionKey = RenderLogEvent(PartitionKey, logEvents[0]);

                try
                {
                    var batchItem = GenerateTableTransactionAction(partitionKey, logEvents[0]);
                    return WriteToTableAsync(tableName, new[] { batchItem }, cancellationToken);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureDataTablesTarget(Name={0}): Failed writing {1} logevents to Table={2} with PartitionKey={3}", Name, 1, tableName, partitionKey);
                    throw;
                }
            }

            const int BatchMaxSize = 100;

            var partitionBuckets = SortHelpers.BucketSort(logEvents, _getTablePartitionNameDelegate);
            IList<Task> multipleTasks = partitionBuckets.Count > 1 ? new List<Task>(partitionBuckets.Count) : null;
            foreach (var partitionBucket in partitionBuckets)
            {
                var tableName = partitionBucket.Key.TableName;
                var partitionKey = partitionBucket.Key.PartitionKey;
                var bucketSize = partitionBucket.Value.Count;

                try
                {
                    if (partitionBucket.Value.Count <= BatchMaxSize)
                    {
                        var batchItem = GenerateBatch(partitionBucket.Value, partitionKey);
                        var writeTask = WriteToTableAsync(tableName, batchItem, cancellationToken);
                        if (multipleTasks == null)
                            return writeTask;

                        multipleTasks.Add(writeTask);
                    }
                    else
                    {
                        // Must chain the tasks together so they don't run concurrently
                        var batchCollection = GenerateBatches(partitionBucket.Value, partitionKey, BatchMaxSize);
                        Task writeTask = WriteMultipleBatchesAsync(batchCollection, tableName, cancellationToken);
                        if (multipleTasks == null)
                            return writeTask;

                        multipleTasks.Add(writeTask);
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureDataTablesTarget(Name={0}): Failed writing {1} logevents to Table={2} with PartitionKey={3}", Name, bucketSize, tableName, partitionKey);
                    if (multipleTasks == null)
                        throw;
                }
            }

            return Task.WhenAll(multipleTasks ?? new Task[0]);
        }

        private async Task WriteMultipleBatchesAsync(IEnumerable<IEnumerable<TableTransactionAction>> batchCollection, string tableName, CancellationToken cancellationToken)
        {
            foreach (var batchItem in batchCollection)
            {
                await WriteToTableAsync(tableName, batchItem, cancellationToken).ConfigureAwait(false);
            }
        }

        IEnumerable<IEnumerable<TableTransactionAction>> GenerateBatches(IList<LogEventInfo> source, string partitionKey, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
                yield return GenerateBatch(source.Skip(i).Take(batchSize), partitionKey);
        }

        private IEnumerable<TableTransactionAction> GenerateBatch(IEnumerable<LogEventInfo> logEvents, string partitionKey)
        {
            return logEvents.Select(evt => GenerateTableTransactionAction(partitionKey, evt));
        }

        private TableTransactionAction GenerateTableTransactionAction(string partitionKey, LogEventInfo evt)
        {
            return new TableTransactionAction(TableTransactionActionType.Add, CreateTableEntity(evt, partitionKey));
        }

        private Task WriteToTableAsync(string tableName, IEnumerable<TableTransactionAction> tableTransaction, CancellationToken cancellationToken)
        {
            tableName = CheckAndRepairTableName(tableName);
            return _cloudTableService.SubmitTransactionAsync(tableName, tableTransaction, cancellationToken);
        }

        private ITableEntity CreateTableEntity(LogEventInfo logEvent, string partitionKey)
        {
            var rowKey = RenderLogEvent(RowKey, logEvent);

            if (ContextProperties.Count > 0)
            {
                var entity = new TableEntity(partitionKey, rowKey);
                bool logTimeStampOverridden = "LogTimeStamp".Equals(ContextProperties[0].Name, StringComparison.OrdinalIgnoreCase);
                if (!logTimeStampOverridden)
                {
                    entity.Add("LogTimeStamp", logEvent.TimeStamp.ToUniversalTime());
                }

                for (int i = 0; i < ContextProperties.Count; ++i)
                {
                    var contextproperty = ContextProperties[i];
                    if (string.IsNullOrEmpty(contextproperty.Name))
                        continue;

                    var propertyValue = contextproperty.Layout != null ? RenderLogEvent(contextproperty.Layout, logEvent) : string.Empty;
                    if (logTimeStampOverridden && i == 0 && string.IsNullOrEmpty(propertyValue))
                        continue;

                    entity.Add(contextproperty.Name, propertyValue);
                }

                return entity;
            }
            else
            {
                var layoutMessage = RenderLogEvent(Layout, logEvent);
                return new NLogEntity(logEvent, layoutMessage, _machineName, partitionKey, rowKey, LogTimeStampFormat);
            }
        }

        private string CheckAndRepairTableName(string tableName)
        {
            return _containerNameCache.LookupStorageName(tableName, _checkAndRepairTableNameDelegate);
        }

        private string CheckAndRepairTableNamingRules(string tableName)
        {
            InternalLogger.Trace("AzureDataTablesTarget(Name={0}): Requested Table Name: {1}", Name, tableName);
            string validTableName = AzureStorageNameCache.CheckAndRepairTableNamingRules(tableName);
            if (validTableName == tableName)
            {
                InternalLogger.Trace("AzureDataTablesTarget(Name={0}): Using Table Name: {1}", Name, validTableName);
            }
            else
            {
                InternalLogger.Trace("AzureDataTablesTarget(Name={0}): Using Cleaned Table name: {1}", Name, validTableName);
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
                InternalLogger.Warn(ex, "AzureDataTablesTarget: Failed to lookup {0}", lookupType);
                return null;
            }
        }

        class CloudTableService : ICloudTableService
        {
            private TableServiceClient _client;
            private TableClient _table;

            private class AzureServiceTokenProviderCredentials : Azure.Core.TokenCredential
            {
                private readonly string _resourceIdentity;
                private readonly string _tenantIdentity;
                private readonly Microsoft.Azure.Services.AppAuthentication.AzureServiceTokenProvider _tokenProvider;

                public AzureServiceTokenProviderCredentials(string tenantIdentity, string resourceIdentity)
                {
                    if (string.IsNullOrWhiteSpace(_resourceIdentity))
                        _resourceIdentity = "https://database.windows.net/";
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
                        InternalLogger.Error(ex, "AzureDataTablesTarget - Failed getting AccessToken from AzureServiceTokenProvider for resource {0}", _resourceIdentity);
                        throw;
                    }
                }

                public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
                {
                    return GetTokenAsync(requestContext, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }

            public void Connect(string connectionString, string serviceUri, string tenantIdentity, string resourceIdentity, string storageAccountName, string accessKey)
            {
                if (string.IsNullOrWhiteSpace(serviceUri))
                {
                    _client = new TableServiceClient(connectionString);
                }
                else if (string.IsNullOrWhiteSpace(accessKey))
                {
                    _client = new TableServiceClient(new Uri(serviceUri), new AzureServiceTokenProviderCredentials(tenantIdentity, resourceIdentity));
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(storageAccountName))
                        _client = new TableServiceClient(new Uri(serviceUri), new Azure.AzureSasCredential(accessKey));
                    else
                        _client = new TableServiceClient(new Uri(serviceUri), new TableSharedKeyCredential(storageAccountName, accessKey));
                }
            }

            public Task SubmitTransactionAsync(string tableName, IEnumerable<TableTransactionAction> tableTransaction, CancellationToken cancellationToken)
            {
                var table = _table;
                if (tableName == null || table?.Name != tableName)
                {
                    return InitializeAndCacheTableAsync(tableName, cancellationToken).ContinueWith(async (t, operation) => await t.Result.SubmitTransactionAsync((IEnumerable<TableTransactionAction>)operation).ConfigureAwait(false), tableTransaction, cancellationToken);
                }
                else
                {
                    return table.SubmitTransactionAsync(tableTransaction, cancellationToken);
                }
            }

            async Task<TableClient> InitializeAndCacheTableAsync(string tableName, CancellationToken cancellationToken)
            {
                try
                {
                    if (_client == null)
                        throw new InvalidOperationException("CloudTableClient has not been initialized");

                    InternalLogger.Debug("AzureDataTablesTarget: Initializing table: {0}", tableName);

                    var tableExists = await _client.CreateTableIfNotExistsAsync(tableName, cancellationToken).ConfigureAwait(false);
                    
                    var table = _client.GetTableClient(tableName);
                    
                    if (tableExists != null)
                        InternalLogger.Debug("AzureDataTablesTarget: Created new table: {0}", tableName);
                    else
                        InternalLogger.Debug("AzureDataTablesTarget: Opened existing table: {0}", tableName);

                    _table = table;
                    return table;
                }
                catch (Exception exception)
                {
                    InternalLogger.Error(exception, "AzureDataTablesTarget: Failed to initialize table={0}", tableName);
                    throw;
                }
            }
        }
    }
}

