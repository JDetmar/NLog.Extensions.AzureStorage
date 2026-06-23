using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using NLog.Extensions.AzureBlobStorage;
using NLog.Extensions.AzureStorage;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    class CloudTableServiceMock : ICloudTableService, IDisposable
    {
        private readonly object _sync = new object();

        // Last transaction submitted per table (kept for the existing single-batch tests/PeekLastAdded).
        public Dictionary<string, IEnumerable<TableTransactionAction>> BatchExecuted { get; } = new Dictionary<string, IEnumerable<TableTransactionAction>>();

        // Every transaction submitted, in submission order. Azure commits each transaction atomically,
        // so a transaction that throws while being enumerated commits NOTHING and is not recorded here.
        public List<KeyValuePair<string, List<TableTransactionAction>>> Transactions { get; } = new List<KeyValuePair<string, List<TableTransactionAction>>>();

        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public string ConnectionString { get; private set; }

        public void Connect(string connectionString, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, string clientAuthId, string clientAuthSecret, ProxySettings proxySettings = null)
        {
            ConnectionString = connectionString;
        }

        public Task SubmitTransactionAsync(string tableName, IEnumerable<TableTransactionAction> tableTransaction, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new InvalidOperationException("CloudTableService not connected");

            return Task.Delay(10).ContinueWith(t =>
            {
                // Azure ENUMERATES the transaction (rendering each entity) and commits it all-or-nothing.
                // Materialize here so a deferred render exception faults the whole transaction and records
                // nothing -- exactly the silent whole-batch loss the eager-build fix is meant to prevent --
                // rather than being quietly deferred past the target's guard.
                var materialized = tableTransaction.ToList();
                lock (_sync)
                {
                    BatchExecuted[tableName] = materialized;
                    Transactions.Add(new KeyValuePair<string, List<TableTransactionAction>>(tableName, materialized));
                }
            });
        }

        public IEnumerable<ITableEntity> PeekLastAdded(string tableName)
        {
            lock (_sync)
            {
                if (BatchExecuted.TryGetValue(tableName, out var tableOperation))
                {
                    return tableOperation.Select(t => t.Entity).ToList();
                }
            }

            return null;
        }

        // Each atomic transaction committed for a table, in submission order (one inner list per batch).
        public IReadOnlyList<IReadOnlyList<ITableEntity>> GetBatches(string tableName)
        {
            lock (_sync)
            {
                return Transactions
                    .Where(x => x.Key == tableName)
                    .Select(x => (IReadOnlyList<ITableEntity>)x.Value.Select(a => a.Entity).ToList())
                    .ToList();
            }
        }

        // Every entity committed for a table across all of its batches.
        public IEnumerable<ITableEntity> PeekAllAdded(string tableName)
        {
            return GetBatches(tableName).SelectMany(b => b).ToList();
        }
    }
}
