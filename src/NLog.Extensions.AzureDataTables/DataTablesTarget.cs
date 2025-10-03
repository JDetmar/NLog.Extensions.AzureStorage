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
        internal const int ColumnStringValueMaxSize = 32768;

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

        /// <summary>
        /// Gets or sets the connection string for Azure Table Storage or Cosmos DB Table API.
        /// </summary>
        public Layout ConnectionString { get; set; }
        /// <summary>
        /// Gets or sets the name of the connection string stored in ConnectionStrings section of the config.
        /// </summary>
        public string ConnectionStringKey { get; set; }

        /// <summary>
        /// Alternative to ConnectionString. A System.Uri referencing the table service account
        /// </summary>
        /// <remarks>
        /// Ex. "https://{account_name}.table.core.windows.net/" or "https://{account_name}.table.cosmos.azure.com/".
        /// </remarks>
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
        /// AccountName for <see cref="TableSharedKeyCredential"/> authentication. Requires <see cref="ServiceUri"/> and <see cref="AccessKey"/>.
        /// </summary>
        /// <remarks>
        /// You'll need a Storage or Cosmos DB account name, primary key, and endpoint Uri. 
        /// You can obtain both from the Azure Portal by clicking Access Keys under Settings
        /// in the Portal Storage account blade or Connection String under Settings in the Portal Cosmos DB account blade.
        /// </remarks>
        public Layout AccountName { get; set; }

        /// <summary>
        /// AccountKey for <see cref="TableSharedKeyCredential"/> authentication. Requires <see cref="ServiceUri"/> and <see cref="AccountName"/>.
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
        /// Gets or sets the name of the Azure table where log entries will be stored.
        /// </summary>
        [RequiredParameter]
        public Layout TableName { get; set; }

        /// <summary>
        /// Gets or sets the partition key for table entries. Defaults to "${logger}".
        /// </summary>
        [RequiredParameter]
        public Layout PartitionKey { get; set; } = "${logger}";

        /// <summary>
        /// Gets or sets the row key for table entries.
        /// </summary>
        [RequiredParameter]
        public Layout RowKey { get; set; }

        /// <summary>
        /// Gets or sets the format string for log timestamps. Defaults to "O" (ISO 8601).
        /// </summary>
        public string LogTimeStampFormat { get; set; } = "O";

        /// <summary>
        /// Initializes a new instance of the <see cref="DataTablesTarget"/> class.
        /// </summary>
        public DataTablesTarget()
            :this(new CloudTableService())
        {
            Layout = "${message}";  // Override default Layout, since splitting into columns
        }

        internal DataTablesTarget(ICloudTableService cloudTableService)
        {
            TaskDelayMilliseconds = 200;
            BatchSize = 100;
            RetryDelayMilliseconds = 100;

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
            string managedIdentityResourceId = string.Empty;
            string managedIdentityClientId = string.Empty;
            string sharedAccessSignature = string.Empty;
            string accountName = string.Empty;
            string accessKey = string.Empty;
            string clientAuthId = string.Empty;
            string clientAuthSecret = string.Empty;

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
                    accountName = AccountName?.Render(defaultLogEvent);
                    accessKey = AccessKey?.Render(defaultLogEvent);
                    clientAuthId = ClientAuthId?.Render(defaultLogEvent);
                    clientAuthSecret = ClientAuthSecret?.Render(defaultLogEvent);
                }

                _cloudTableService.Connect(connectionString, serviceUri, tenantIdentity, managedIdentityResourceId, managedIdentityClientId, sharedAccessSignature, accountName, accessKey, clientAuthId, clientAuthSecret);
                InternalLogger.Debug("AzureDataTablesTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(serviceUri))
                    InternalLogger.Error(ex, "AzureDataTablesTarget(Name={0}): Failed to create TableClient with ServiceUri={1}.", Name, serviceUri);
                else
                    InternalLogger.Error(ex, "AzureDataTablesTarget(Name={0}): Failed to create TableClient with ConnectionString={1}.", Name, connectionString);
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
        /// Override this to provide async task for writing multiple logevents in optimized batch operation.
        /// </summary>
        /// <param name="logEvents">The log events.</param>
        /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
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

                    var propertyValue = RenderLogEvent(contextproperty.Layout, logEvent) ?? string.Empty;
                    if (logTimeStampOverridden && i == 0 && string.IsNullOrEmpty(propertyValue))
                        continue;

                    if (!contextproperty.IncludeEmptyValue && string.IsNullOrEmpty(propertyValue))
                        continue;

                    if (propertyValue.Length >= ColumnStringValueMaxSize)
                    {
                        InternalLogger.Debug("AzureDataTablesTarget(Name={0}): Truncating value from column '{1}', because string-length above 32K", Name, contextproperty.Name);
                        propertyValue = propertyValue.Substring(0, ColumnStringValueMaxSize - 1);
                    }

                    entity.Add(contextproperty.Name, propertyValue);
                }

                return entity;
            }
            else
            {
                var layoutMessage = RenderLogEvent(Layout, logEvent) ?? string.Empty;
                if (layoutMessage.Length >= ColumnStringValueMaxSize)
                {
                    layoutMessage = layoutMessage.Substring(0, ColumnStringValueMaxSize - 1);
                    InternalLogger.Debug("AzureDataTablesTarget(Name={0}): Truncating value from Layout, because string-length above 32K", Name);
                }
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

            public void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret)
            {
                if (string.IsNullOrWhiteSpace(serviceUri))
                {
                    _client = new TableServiceClient(connectionString);
                }
                else if (!string.IsNullOrWhiteSpace(sharedAccessSignature))
                {
                    _client = new TableServiceClient(new Uri(serviceUri), new Azure.AzureSasCredential(sharedAccessSignature));
                }
                else if (!string.IsNullOrWhiteSpace(storageAccountName))
                {
                    _client = new TableServiceClient(new Uri(serviceUri), new TableSharedKeyCredential(storageAccountName, storageAccountAccessKey));
                }
                else if (!string.IsNullOrEmpty(clientAuthId) && !string.IsNullOrEmpty(clientAuthSecret) && !string.IsNullOrEmpty(tenantIdentity))
                {
                    var tokenCredentials = new Azure.Identity.ClientSecretCredential(tenantIdentity, clientAuthId, clientAuthSecret);
                    _client = new TableServiceClient(new Uri(serviceUri), tokenCredentials);
                }
                else
                {
                    var tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(managedIdentityClientId, tenantIdentity, managedIdentityResourceId);
                    _client = new TableServiceClient(new Uri(serviceUri), tokenCredentials);
                }
            }

            public Task SubmitTransactionAsync(string tableName, IEnumerable<TableTransactionAction> tableTransaction, CancellationToken cancellationToken)
            {
                var table = _table;
                if (string.IsNullOrEmpty(tableName) || table?.Name != tableName)
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

