using System;
using System.Net.Sockets;
using Azure.Data.Tables;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // Characterizes REAL Azure Table behavior (via Azurite) to confirm this is a genuine issue:
    // a table name starting with a digit is genuinely rejected, so the repair returning the
    // un-repaired "123logs" really would break table use.
    public class TableNameRulesTests
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
        public void Azure_RejectsTableNameStartingWithDigit_ButAcceptsRepairedName()
        {
            if (!AzuriteTablesAvailable()) return;
            var svc = new TableServiceClient(DevStorage);

            // "123logs" is exactly what the buggy CheckAndRepairTableNamingRules returned.
            var ex = Record.Exception(() => svc.CreateTableIfNotExists("123logs"));
            Assert.NotNull(ex); // real Azure rejects a digit-leading table name

            // "logs" is the repaired name -> must be usable.
            var valid = "logs" + Guid.NewGuid().ToString("n").Substring(0, 8);
            svc.CreateTableIfNotExists(valid);
            svc.DeleteTable(valid);
        }
    }
}
