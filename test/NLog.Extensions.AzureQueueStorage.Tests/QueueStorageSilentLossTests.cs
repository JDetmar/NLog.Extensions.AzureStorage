using System;
using System.Threading;
using System.Threading.Tasks;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureQueueStorage.Tests
{
    // Regression test for S1: the cache-miss send path returned an un-unwrapped
    // Task<Task> (ContinueWith with an async lambda), so a first-write failure was
    // swallowed on the orphaned inner task and messages were silently lost.
    public class QueueStorageSilentLossTests
    {
        [Fact]
        public async Task FirstWriteFailure_FaultsReturnedTask_NotSilentlySwallowed()
        {
            // Point at a closed port so the first (cache-miss) send fails deterministically.
            const string deadEndpoint =
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
                "QueueEndpoint=http://127.0.0.1:1/devstoreaccount1;";

            var service = new QueueStorageTarget.CloudQueueService();
            service.Connect(deadEndpoint, null, null, null, null, null, null, null, null, null, null, null);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.AddMessageAsync("nlogqueue", "hello", CancellationToken.None));
        }
    }
}
