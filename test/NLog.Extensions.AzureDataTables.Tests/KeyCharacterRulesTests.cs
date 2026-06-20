using System;
using System.Net.Sockets;
using Azure;
using Azure.Data.Tables;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // Characterizes REAL Azure Table behavior (via Azurite) to confirm this is a genuine issue:
    // a forbidden key character ('/') makes Azure reject the ENTIRE transaction, so a single bad
    // key loses every valid log entry in the same batch -> the target must sanitize keys.
    public class KeyCharacterRulesTests
    {
        private const string DevStorage = "UseDevelopmentStorage=true";

        private static bool AzuriteTablesAvailable()
        {
            try
            {
                using var c = new TcpClient();
                var r = c.BeginConnect("127.0.0.1", 10002, null, null);
                return r.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500)) && c.Connected;
            }
            catch { return false; }
        }

        [SkippableFact]
        public void Azure_ForbiddenKeyCharInBatch_RejectsWholeTransaction_LosingGoodEntitiesToo()
        {
            Skip.IfNot(AzuriteTablesAvailable(), "Azurite Tables endpoint (127.0.0.1:10002) is not available.");
            var svc = new TableServiceClient(DevStorage);
            var tableName = "k" + Guid.NewGuid().ToString("n");
            svc.CreateTable(tableName);
            try
            {
                var table = svc.GetTableClient(tableName);
                var actions = new[]
                {
                    new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "good-row")),
                    new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "bad/row")), // '/' is forbidden in keys
                };

                // The whole transaction is rejected because of the one forbidden RowKey.
                // (TableTransactionFailedException derives from RequestFailedException.)
                Assert.ThrowsAny<RequestFailedException>(() => table.SubmitTransaction(actions));

                // ...and the VALID log entry in the same batch was lost too (never committed):
                // reading it back yields a 404, proving it was never persisted.
                var readBack = Assert.ThrowsAny<RequestFailedException>(() => table.GetEntity<TableEntity>("pk", "good-row"));
                Assert.Equal(404, readBack.Status);
            }
            finally
            {
                svc.DeleteTable(tableName);
            }
        }
    }
}
