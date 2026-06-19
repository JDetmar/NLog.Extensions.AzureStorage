using System;
using System.Net.Sockets;
using Azure.Data.Tables;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // Characterizes REAL Azure Table behavior (via Azurite) to confirm this is a genuine issue:
    // a 32768-char string property is genuinely accepted and round-trips, so truncating it to
    // 32767 really was unnecessary data loss (and the fix is not over-permissive).
    public class StringSizeRulesTests
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

        [Fact]
        public void Azure_Accepts32768CharStringProperty()
        {
            if (!AzuriteTablesAvailable()) return;
            var svc = new TableServiceClient(DevStorage);
            var tableName = "v" + Guid.NewGuid().ToString("n");
            svc.CreateTable(tableName);
            try
            {
                var table = svc.GetTableClient(tableName);
                var big = new string('x', 32768); // exactly the constant the code caps at
                table.UpsertEntity(new TableEntity("pk", "rk") { ["Big"] = big });

                var read = table.GetEntity<TableEntity>("pk", "rk").Value;
                Assert.Equal(32768, read.GetString("Big").Length); // round-trips intact
            }
            finally
            {
                svc.DeleteTable(tableName);
            }
        }
    }
}
