using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    class CloudTableServiceMock : ICloudTableService
    {
        public Dictionary<string, IEnumerable<TableTransactionAction>> BatchExecuted { get; } = new Dictionary<string, IEnumerable<TableTransactionAction>>();
        public string ConnectionString { get; private set; }

        public void Connect(string connectionString, string serviceUri, string tenantIdentity, string resourceIdentifier, string clientIdentity, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey)
        {
            ConnectionString = connectionString;
        }

        public Task SubmitTransactionAsync(string tableName, IEnumerable<TableTransactionAction> tableTransaction, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("CloudTableService not connected");

            return Task.Delay(10).ContinueWith(t =>
            {
                lock (BatchExecuted)
                    BatchExecuted[tableName] = tableTransaction;
            });
        }

        public IEnumerable<ITableEntity> PeekLastAdded(string tableName)
        {
            lock (BatchExecuted)
            {
                if (BatchExecuted.TryGetValue(tableName, out var tableOperation))
                {
                    return tableOperation.Select(t => t.Entity);
                }
            }

            return null;
        }
    }
}
