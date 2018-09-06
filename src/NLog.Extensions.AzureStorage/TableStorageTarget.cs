using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
#if !NETSTANDARD
using System.Configuration;
using Microsoft.Azure;
#endif

namespace NLog.Extensions.AzureStorage
{
    /// <summary>
    /// Azure Table Storage NLog Target
    /// </summary>
    /// <seealso cref="NLog.Targets.TargetWithLayout" />
    [Target("AzureTableStorage")]
    public sealed class TableStorageTarget : TargetWithLayout
    {
        private CloudTableClient _client;
        private CloudTable _table;
        private string _machineName;

        //Delegates for bucket sorting
        private SortHelpers.KeySelector<AsyncLogEventInfo, string> _getTableNameDelegate;

        public string ConnectionString { get => (_connectionString as SimpleLayout)?.Text ?? null; set => _connectionString = value; }
        private Layout _connectionString;
        public string ConnectionStringKey { get; set; }

        [RequiredParameter]
        public Layout TableName { get; set; }

        public string LogTimeStampFormat { get; set; } = "O";

        public TableStorageTarget()
        {
            OptimizeBufferReuse = true;
        }

        /// <summary>
        /// Initializes the target. Can be used by inheriting classes
        /// to initialize logging.
        /// </summary>
        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            _machineName = GetMachineName();

            var connectionString = _connectionString != null ? RenderLogEvent(_connectionString, LogEventInfo.CreateNullEvent()) : string.Empty;
#if !NETSTANDARD
            if (!string.IsNullOrWhiteSpace(ConnectionStringKey))
            {
                connectionString = CloudConfigurationManager.GetSetting(ConnectionStringKey);
                if (String.IsNullOrWhiteSpace(connectionString))
                    connectionString = ConfigurationManager.ConnectionStrings[ConnectionStringKey]?.ConnectionString;
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    InternalLogger.Error($"AzureTableStorageTarget: No ConnectionString found with ConnectionStringKey: {ConnectionStringKey}.");
                    throw new Exception($"No ConnectionString found with ConnectionStringKey: {ConnectionStringKey}.");
                }
            }
#endif

            if (String.IsNullOrWhiteSpace(connectionString))
            {
                InternalLogger.Error("AzureTableStorageTarget: A ConnectionString or ConnectionStringKey is required.");
                throw new Exception("A ConnectionString or ConnectionStringKey is required");
            }

            _client = CloudStorageAccount.Parse(connectionString).CreateCloudTableClient();

            InternalLogger.Trace("AzureTableStorageWrapper - Initialized");
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

            var tempTableName = RenderLogEvent(TableName, logEvent);
            var tableNameFinal = CheckAndRepairTableNamingRules(tempTableName);

            InitializeTable(tableNameFinal);
            var layoutMessage = RenderLogEvent(Layout, logEvent);
            var entity = new NLogEntity(logEvent, layoutMessage, _machineName, LogTimeStampFormat);
            var insertOperation = TableOperation.Insert(entity);
            TableExecute(_table, insertOperation);
        }

        /// <summary>
        /// Writes an array of logging events to the log target. By default it iterates on all
        /// events and passes them to "Write" method. Inheriting classes can use this method to
        /// optimize batch writes.
        /// </summary>
        /// <param name="logEvents">Logging events to be written out.</param>
        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
            if (logEvents.Count <= 1)
            {
                base.Write(logEvents);
                return;
            }

            //must sort into containers and then into the blobs for the container
            if (_getTableNameDelegate == null)
                _getTableNameDelegate = c => TableName.Render(c.LogEvent);

            var tableBuckets = SortHelpers.BucketSort(logEvents, _getTableNameDelegate);

            //Iterate over all the tables being written to
            foreach (var tableBucket in tableBuckets)
            {
                var tableNameFinal = CheckAndRepairTableNamingRules(tableBucket.Key);
                InitializeTable(tableNameFinal);
                var batch = new TableBatchOperation();
                //add each message for the destination table limit batch to 100 elements
                foreach (var asyncLogEventInfo in tableBucket.Value)
                {
                    var layoutMessage = RenderLogEvent(Layout, asyncLogEventInfo.LogEvent);
                    var entity = new NLogEntity(asyncLogEventInfo.LogEvent, layoutMessage, _machineName, LogTimeStampFormat);
                    batch.Insert(entity);
                    if (batch.Count == 100)
                    {
                        TableExecuteBatch(_table, batch);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                    TableExecuteBatch(_table, batch);

                foreach (var asyncLogEventInfo in tableBucket.Value)
                    asyncLogEventInfo.Continuation(null);
            }
        }

        private static void TableExecute(CloudTable cloudTable, TableOperation insertOperation)
        {
#if NETSTANDARD
            cloudTable.ExecuteAsync(insertOperation).GetAwaiter().GetResult();
#else
            cloudTable.Execute(insertOperation);
#endif
        }

        private static void TableExecuteBatch(CloudTable cloudTable, TableBatchOperation batch)
        {
#if NETSTANDARD
            cloudTable.ExecuteBatchAsync(batch).GetAwaiter().GetResult();
#else
            cloudTable.ExecuteBatch(batch);
#endif
        }

        private void TableCreateIfNotExists(CloudTable cloudTable)
        {
#if NETSTANDARD
            cloudTable.CreateIfNotExistsAsync().GetAwaiter().GetResult();
#else
            cloudTable.CreateIfNotExists();
#endif
        }

        /// <summary>
        /// Initializes the Azure storage table and creates it if it doesn't exist.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        private void InitializeTable(string tableName)
        {
            if (_table == null || _table.Name != tableName)
            {
                _table = _client.GetTableReference(tableName);
                try
                {
                    TableCreateIfNotExists(_table);
                }
                catch (StorageException storageException)
                {
                    InternalLogger.Error(storageException, "AzureTableStorageTarget: failed to get a reference to storage table.");
                    throw;
                }
            }
        }

        //TODO: update rules
        /// <summary>
        /// Checks the and repairs table name acording to the Azure naming rules.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <returns></returns>
        private static string CheckAndRepairTableNamingRules(string tableName)
        {
            /*  
                table Names

                Table names must be unique within an account.
                Table names may contain only alphanumeric characters.
                Table names cannot begin with a numeric character.
                Table names are case-insensitive.
                Table names must be from 3 to 63 characters long.
                Some table names are reserved, including "tables". Attempting to create a table with a reserved table name returns error code 404 (Bad Request).
            */
            const string trimLeadingPattern = "^.*?(?=[a-zA-Z])";
            const string trimFobiddenCharactersPattern = "[^a-zA-Z0-9-]";

            var pass1 = Regex.Replace(tableName, trimFobiddenCharactersPattern, String.Empty, RegexOptions.None);
            var cleanedTableName = Regex.Replace(pass1, trimLeadingPattern, String.Empty, RegexOptions.None);
            if (String.IsNullOrWhiteSpace(cleanedTableName) || cleanedTableName.Length > 63 || cleanedTableName.Length < 3)
            {
                var tableDefault = String.Concat("Logs");
                InternalLogger.Error("AzureTableStorageTarget: Invalid table Name provided: {0} | Using default: {1}", tableName, tableDefault);
                return tableDefault;
            }
            InternalLogger.Trace("AzureTableStorageTarget: Using provided table name: {0}", tableName);
            return tableName;
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
                NLog.Common.InternalLogger.Warn(ex, "AzureTableStorageTarget: Failed to lookup {0}", lookupType);
                return null;
            }
        }
    }
}
