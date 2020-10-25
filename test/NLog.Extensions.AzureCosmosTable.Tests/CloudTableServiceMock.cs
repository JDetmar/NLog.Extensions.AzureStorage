using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    class CloudTableServiceMock : ICloudTableService
    {
        public Dictionary<string, TableBatchOperation> BatchExecuted { get; } = new Dictionary<string, TableBatchOperation>();
        public string ConnectionString { get; private set; }

        public void Connect(string connectionString, int? defaultTimeToLiveSeconds)
        {
            ConnectionString = connectionString;
        }

        public Task ExecuteBatchAsync(string tableName, TableBatchOperation tableOperation, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("CloudTableService not connected");

            lock (BatchExecuted)
                BatchExecuted[tableName] = tableOperation;
            return Task.Delay(10, cancellationToken);
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
