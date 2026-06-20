using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog.Extensions.AzureStorage;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // S2: AzureStorageNameCache.LookupStorageName reads, Clears, and writes a plain
    // Dictionary with no synchronization. A single flush fans out bucket tasks via
    // Task.WhenAll, so the cache is hit concurrently for multiple names. This test
    // drives that real concurrency and asserts the cache neither throws, hangs, nor
    // returns a corrupt value.
    public class AzureStorageNameCacheConcurrencyTests
    {
        [Fact]
        public void LookupStorageName_UnderConcurrentLoad_DoesNotThrowHangOrCorrupt()
        {
            var cache = new AzureStorageNameCache();
            Func<string, string> repair = n => "ok-" + n; // deterministic: name -> "ok-"+name
            var corrupt = new ConcurrentQueue<string>();

            // Tie the timeout to the loop itself: when the token fires, Parallel.For stops
            // issuing iterations and throws OperationCanceledException, so a hung/corrupt
            // cache can't leave the 300k-iteration loop spinning after the test fails.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cts.Token
            };

            var work = Task.Run(() =>
                Parallel.For(0, 300_000, options, i =>
                {
                    var name = "n" + (i % 4000);   // > 1000 distinct keys -> exercises the Count>1000 Clear()
                    var result = cache.LookupStorageName(name, repair);
                    if (result != "ok-" + name)
                        corrupt.Enqueue($"{name} -> {result ?? "<null>"}");
                }));

            try
            {
                work.Wait();
            }
            catch (Exception ex) when (ex.GetBaseException() is OperationCanceledException)
            {
                throw new Xunit.Sdk.XunitException("LookupStorageName hung under concurrent access (corrupted Dictionary internal state)");
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException("LookupStorageName threw under concurrent access: " + ex.GetBaseException());
            }

            Assert.True(corrupt.IsEmpty, $"LookupStorageName returned corrupt values under concurrency, e.g. {corrupt.FirstOrDefault()}");
        }
    }
}
