using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // Regression test for S1: the cache-miss submit path returned an un-unwrapped
    // Task<Task> (ContinueWith with an async lambda), so a first-write failure was
    // swallowed on the orphaned inner task and table rows were silently lost.
    public class DataTablesSilentLossTests
    {
        [Fact]
        public async Task FirstWriteFailure_FaultsReturnedTask_NotSilentlySwallowed()
        {
            // Point at a closed port so the first (cache-miss) transaction fails deterministically.
            const string deadEndpoint =
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
                "TableEndpoint=http://127.0.0.1:1/devstoreaccount1;";

            var service = new DataTablesTarget.CloudTableService();
            service.Connect(deadEndpoint, null, null, null, null, null, null, null, null, null);

            var actions = new[]
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "rk")),
            };

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.SubmitTransactionAsync("nlogtable", actions, CancellationToken.None));
        }
    }
}
