using System;
using System.Linq;
using System.Net.Sockets;
using Azure;
using Azure.Data.Tables;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // Characterizes REAL Azure Table behavior (via Azurite) to confirm this is a genuine issue:
    // a PartitionKey/RowKey is capped at the documented "1 KiB" -- which, because keys are measured
    // in UTF-16, is 512 chars. A 512-char key is accepted; a 513-char key makes Azure reject the
    // ENTIRE transaction, so a single over-long key loses every valid log entry in the same batch.
    // The target must therefore truncate rendered keys to 512 chars before building the entity
    // (mirrors KeyValueMaxSize in DataTablesTarget).
    public class KeyLengthRulesTests
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
        public void Azure_Accepts512CharKey_Rejects513()
        {
            Skip.IfNot(AzuriteTablesAvailable(), "Azurite Tables endpoint (127.0.0.1:10002) is not available.");
            var svc = new TableServiceClient(DevStorage);
            var tableName = "l" + Guid.NewGuid().ToString("n");
            svc.CreateTable(tableName);
            try
            {
                var table = svc.GetTableClient(tableName);

                // 512 chars is exactly the cap the code truncates to -- genuinely accepted and round-trips.
                var maxKey = new string('r', 512);
                table.UpsertEntity(new TableEntity("pk", maxKey));
                Assert.Equal(512, table.GetEntity<TableEntity>("pk", maxKey).Value.RowKey.Length);

                // 513 chars is rejected, and (in a batch) takes the whole transaction down with it.
                var tooLong = new string('r', 513);
                var actions = new[]
                {
                    new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "good-row")),
                    new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", tooLong)),
                };
                Assert.ThrowsAny<RequestFailedException>(() => table.SubmitTransaction(actions));

                // ...and the VALID entity in the same batch was lost too (never committed -> 404).
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
