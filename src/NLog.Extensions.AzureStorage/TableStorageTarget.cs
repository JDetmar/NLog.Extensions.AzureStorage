using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using System.Text.RegularExpressions;

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

        //Delegates for bucket sorting
        private SortHelpers.KeySelector<AsyncLogEventInfo, string> _getTableNameDelegate;

        public string ConnectionString { get; set; }
        public string ConnectionStringKey { get; set; }

        [RequiredParameter]
        public Layout TableName { get; set; }

        /// <summary>
        /// Initializes the target. Can be used by inheriting classes
        /// to initialize logging.
        /// </summary>
        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            if (String.IsNullOrWhiteSpace(ConnectionString) && !String.IsNullOrWhiteSpace(ConnectionStringKey))
            {
                ConnectionString = CloudConfigurationManager.GetSetting(ConnectionStringKey);
            }
            if (String.IsNullOrWhiteSpace(ConnectionString))
            {
                InternalLogger.Error("AzureTableStorageWrapper: A ConnectionString or ConnectionStringKey is required.");
                throw new Exception("A ConnectionString or ConnectionStringKey is required");
            }
            _client = CloudStorageAccount.Parse(ConnectionString).CreateCloudTableClient();

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

            var tempTableName = TableName.Render(logEvent);
            var tableNameFinal = CheckAndRepairTableNamingRules(tempTableName);
            var logMessage = string.Concat(Layout.Render(logEvent), Environment.NewLine);

            InitializeTable(tableNameFinal);
            var entity = new NLogEntity(logEvent, Layout);
            var insertOperation = TableOperation.Insert(entity);
            _table.Execute(insertOperation);
        }

        /// <summary>
        /// Writes an array of logging events to the log target. By default it iterates on all
        /// events and passes them to "Write" method. Inheriting classes can use this method to
        /// optimize batch writes.
        /// </summary>
        /// <param name="logEvents">Logging events to be written out.</param>
        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
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
                    var entity = new NLogEntity(asyncLogEventInfo.LogEvent, Layout);
                    batch.Insert(entity);
                    if (batch.Count == 100)
                    {
                        _table.ExecuteBatch(batch);
                        batch.Clear();
                    }
                }
                if(batch.Count > 0)
                    _table.ExecuteBatch(batch);
            }
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
                    _table.CreateIfNotExists();
                }
                catch (StorageException storageException)
                {
                    InternalLogger.Error(storageException, "NLog.Extensions.AzureStorage failed to get a reference to storage table.");
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
                InternalLogger.Error("Invalid table Name provided: {0} | Using default: {1}", tableName, tableDefault);
                return tableDefault;
            }
            InternalLogger.Trace("Using provided table name: {0}", tableName);
            return tableName;
        }

    }
}
