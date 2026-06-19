using System;
using System.Collections.Concurrent;
using System.Linq;
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

            var work = Task.Run(() =>
                Parallel.For(0, 300_000, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
                {
                    var name = "n" + (i % 4000);   // > 1000 distinct keys -> exercises the Count>1000 Clear()
                    var result = cache.LookupStorageName(name, repair);
                    if (result != "ok-" + name)
                        corrupt.Enqueue($"{name} -> {result ?? "<null>"}");
                }));

            bool completed;
            try
            {
                completed = work.Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException("LookupStorageName threw under concurrent access: " + ex.GetBaseException());
            }

            Assert.True(completed, "LookupStorageName hung under concurrent access (corrupted Dictionary internal state)");
            Assert.True(corrupt.IsEmpty, $"LookupStorageName returned corrupt values under concurrency, e.g. {corrupt.FirstOrDefault()}");
        }
    }
}
