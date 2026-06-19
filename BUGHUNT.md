# Bug-Hunt Report — NLog.Extensions.AzureStorage

Audit of the repository for correctness bugs, concurrency/async hazards, resource
leaks, and performance issues. **No code has been changed** — this document is an
inventory of areas that warrant reproduction, evaluation, and (where confirmed)
fixing.

**Scope:** the 8 active packages (~4,400 LOC).
`NLog.Extensions.AzureCosmosTable` is deprecated (README only) and was not audited.

## Fix progress (branch `bughunt/verify-and-fix`)

Each fix below was reproduced with a failing test, fixed, and re-verified.

- ✅ **S1 silent log loss** (Blob/Queue/Tables) — un-unwrapped `Task<Task>` rewritten as
  proper async; dropped `CancellationToken`s restored. Integration tests via Azurite +
  dead-endpoint fault. Commit `3a0360d`.
- ✅ **Table-name repair** (`CheckAndRepairTableNamingRules`) — returns the cleaned name,
  strips non-alphanumerics, null-guarded. Commit `3120cef`.
- ✅ **Truncation off-by-one** (3 sites) — caps Azure Table strings at exactly 32768.
  Commit `96f02d2`.
- ✅ **AccessToken refresh storm** — no-expiry → 55-min fallback (`a48cf0b`); failed
  acquisition → 30s backoff (`37afb88`).
- ✅ **S2 name-cache race** — `ConcurrentDictionary`; the .NET runtime's own corruption
  exception was reproduced under concurrent load. Commit `905d82e`.
- ✅ **Batch-size int-overflow (S3, minimal)** — ServiceBus/EventHub size estimate uses
  `long` + cap so it can't overflow to a negative size (which sent oversized batches).
  Commit `c3ca8d0`.
- ✅ **Table key sanitization** — strip `/ \ # ?`/control chars from PartitionKey/RowKey;
  whole-transaction loss from a forbidden key was proven against Azurite. Commit `3511c87`.

Each Azure-rule assumption (#1 name validity, #3 size limit, key forbidden-chars) is
backed by an Azurite characterization test, so the fixes rest on observed real behavior.

Test-environment notes: test projects target `net8.0` (most) / `net6.0` (AccessToken) but
only net9/net10 runtimes are installed → run with `DOTNET_ROLL_FORWARD=Major`. The
AccessToken project additionally fails to build under `TreatWarningsAsErrors` due to
pre-existing missing XML docs (CS1591) — unrelated to these fixes; verified with
`-p:TreatWarningsAsErrors=false`.

Still open: S3 over-/under-splitting + `MessageSizeExceeded` swallow (the full
`MessageBatch.TryAdd` redesign), S4 encoding-lock contention, S5 disposal leaks,
EventGrid no-batching, and the pre-existing AccessToken doc-build (CS1591) blocker.

## Legend

**Status**
- ✅ **Verified** — confirmed by reading the source directly.
- ⚠️ **Plausible** — identified during audit; logic is internally consistent but
  not yet reproduced. Worth confirming.

**Reproducibility / testability** (the key axis for the next phase)
- 🟢 **Unit-testable now** — exercisable with the existing test harness
  (`*ServiceMock` + `LogFactory`) or by directly calling an `internal` helper
  (every `src` project has `InternalsVisibleTo` its `.Tests` project).
- 🟡 **Needs integration/refactor** — the bug lives inside the real
  `CloudBlobService` / `CloudTableService` / `CloudQueueService` (the production
  `ICloudXService` implementation). The unit tests inject a **mock** of that
  interface and therefore **bypass** this code. Faithful reproduction needs the
  Azurite emulator (or the Azure SDK), or the caching/await layer must be
  refactored into a testable unit.
- 🔵 **Timing/concurrency** — non-deterministic; reproduction needs stress/race
  harnesses, `volatile`-stripping, or fault injection rather than a simple assert.

---

## 🔴 Systemic issues (the same pattern repeats across packages)

These are the highest-leverage findings: one fix-class resolves several call sites.

### S1 — `ContinueWith(async …)` returns an un-awaited `Task<Task>` → silent log loss
- **Where:** `BlobStorageTarget.cs:537`, `QueueStorageTarget.cs:389`, `DataTablesTarget.cs:538`
- **Status:** ✅ Verified (Blob + Queue read directly; DataTables identical pattern)
- **Testability:** 🟡 Needs integration/refactor (lives in the real service classes)

On the **cache-miss** path (first write to a given blob/queue/table):
```csharp
return InitializeAndCacheXAsync(...)
    .ContinueWith(async (t, s) => await t.Result.SendAsync(...).ConfigureAwait(false), state, ct);
```
The `async` continuation makes `ContinueWith` produce a `Task<Task>`, and `.Unwrap()`
is missing. The returned outer task completes when the inner operation is **created**,
not when it **completes**. Consequences:
- NLog's `AsyncTaskTarget` treats the batch as flushed before the network write
  finishes — the real send is effectively fire-and-forget.
- Exceptions from the real send (throttling, auth, size limits) land on the orphaned
  inner task and are **never observed** → logs silently dropped, NLog retry never fires.
- `t.Result` re-throws init failures as `AggregateException` *inside* the swallowed
  inner task, so initialization errors vanish too.

The `else` (cache-hit) branches are correct because they return the task directly.
Blob/DataTables additionally **drop the `CancellationToken`** on the inner call
(Queue correctly captures it in the closure — relevant when writing a regression test).

**Reproduction sketch:** because the mocks replace `ICloudXService`, this needs either
(a) an Azurite-backed integration test on the real `CloudBlobService.AppendFromByteArrayAsync`
where the underlying append throws, asserting the exception surfaces to NLog's
internal error handler / a registered error callback; or (b) extract the
"init-then-send" step into an injectable seam and unit-test that a faulting send
faults the returned task.

### S2 — Single "last-used" client cache + unsynchronized shared field
- **Where:** `_appendBlob`/`_container` (Blob `:533-567`), `_table` (DataTables `:535-564`), `_queue` (Queue `:386-412`); plus shared `AzureStorageNameCache._storageNameCache` (`AzureStorageNameCache.cs:9-20`)
- **Status:** ✅ Pattern verified
- **Testability:** 🟡 (client cache, service-layer) / 🟢 (name-cache concurrency, pure helper)

Each service caches exactly one client in a plain mutable field, read/written without
synchronization. Within one flush, `WriteAsyncTask` fans out buckets via `Task.WhenAll`,
so these read-modify-writes run **concurrently** whenever a batch targets multiple
names/partitions. Two effects:
- **Performance:** any config whose name is a `Layout` (e.g. `blobName="${logger}/…"`,
  `tableName`, `queueName="${level}"`) thrashes the single slot — every differing
  bucket takes the miss path and re-issues `ExistsAsync` / `CreateIfNotExistsAsync`
  network round-trips **every flush**.
- **Concurrency:** the field can be torn/overwritten between the name-check and the
  send. Worth evaluating a small `ConcurrentDictionary` keyed by name.

`AzureStorageNameCache._storageNameCache` is a plain `Dictionary` whose `Add`/`Clear`
run from those same concurrent bucket tasks — concurrent `Dictionary` writes are
undefined behavior (can throw or corrupt internal state).

**Reproduction sketch:** the name-cache race (🟢) is reachable directly — hammer
`AzureStorageNameCache.LookupStorageName` from N parallel tasks with >1000 distinct
keys (to trigger the `Clear()` at line 17) and assert no exception / no corruption.
The client-cache thrash (🟡) is observable via an Azurite integration test counting
`ExistsAsync` calls across flushes for a multi-name config.

### S3 — Hand-rolled batch-size estimation instead of the SDK's native batch API
- **Where:** EventHub `EventHubTarget.cs:437-495`, ServiceBus `ServiceBusTarget.cs:500-555`
- **Status:** ⚠️ Plausible
- **Testability:** 🟢 for the math (`CalculateBatchSize`/`EstimateEventDataSize` are internal); 🟡 for the drop-on-oversize behavior

Neither target uses `EventDataBatch` / `ServiceBusMessageBatch` + `TryAdd` (the
authoritative overflow API). Instead they estimate bytes as `(len+128)*3+128 * count`
and chunk by count. Problems:
- `*3` triples an already-byte length; `numberOfBatches = Math.Max(total/max, 10)`
  forces ≥10 chunks even for tiny overflow; the `-1` shrink compounds it →
  **systematic over-splitting** (far more network calls than needed).
- `int` accumulation can **overflow** on large flushes → one oversized send.
- On `MessageSizeExceeded`/`QuotaExceeded` the exception is **swallowed**
  (`ServiceBusTarget.cs:470-487`, `EventHubTarget.cs:416-422`), dropping the whole chunk.
- Behavior is inconsistent: single-event/single-batch paths throw on oversize; the
  multi-batch path drops.

**Reproduction sketch:** `CalculateBatchSize`/`EstimateEventDataSize` are pure and
`internal` — unit-test them directly with crafted sizes/counts and assert the number
of chunks is sane and that large inputs don't overflow to a negative size.

### S4 — `EncodeToUTF8` shares one buffer under a global `lock`
- **Where:** Blob `:371`, ServiceBus `:664`, EventHub `:584`, EventGrid `:371`
- **Status:** ⚠️ Plausible (performance, not a data race)
- **Testability:** 🔵 (throughput/contention — needs a benchmark, not an assert)

Each target shares one `char[32K]` guarded by `lock`. Encoding runs inside the
concurrent bucket fan-out (S2), so this lock **serializes all message encoding**,
negating the parallelism. The buffer-copy-then-`GetBytes(char[])` is also barely
cheaper than `Encoding.UTF8.GetBytes(string)` while adding contention. Consider
`ArrayPool`/`[ThreadStatic]` or the direct string overload.

### S5 — Shutdown & disposal gaps
- **Where:** ServiceBus `:393`, EventHub `:320`; EventGrid & Queue have no `CloseTarget`; `ProxyHelpers.cs:72`
- **Status:** ⚠️ Plausible
- **Testability:** 🟡 (most paths are in service classes / `CloseTarget`)

- ServiceBus and EventHub do `Task.Run(async …).Wait(500ms)` and **ignore the return**
  — on slow shutdown the flush is abandoned (shutdown log loss) and the task's
  exception is unobserved.
- EventGrid and QueueStorage have **no `CloseTarget`** override at all.
- `ProxyHelpers.CreateHttpClientTransport()` news an `HttpClient` that is **never
  disposed** → socket/handler leak across every NLog reconfiguration, for all
  proxy-enabled targets.

---

## 🟠 Notable package-specific findings

### Shared helper — `AzureStorageNameCache`
- **`CheckAndRepairTableNamingRules` returns the original, un-repaired name**
  — `AzureStorageNameCache.cs:131` returns `tableName`, not `cleanedTableName`.
  - **Status:** ✅ Verified · **Testability:** 🟢 (pure static, `InternalsVisibleTo`)
  - The whole repair branch is a no-op except the `"Logs"` default. A name like
    `"123logs"` (leading digit) or `"my-logs!"` is handed to Azure unchanged. The
    table path also lacks the null-guard the container path has (`:124` NRE on null).
  - **Repro:** `Assert.Equal("logs", CheckAndRepairTableNamingRules("123logs"))` —
    fails today (returns `"123logs"`). Easiest single bug to pin with a unit test.

### AccessTokenLayoutRenderer  (all ✅ Verified)
- **Non-`volatile` `_accessToken`** (`:182`) read with plain reads on the hot path
  (`:191,207,211`) while written via `Interlocked.Exchange` (`:224`). On ARM, readers
  can observe a stale/`null` value. (The author already uses the interlocked-read
  trick at `:196` — but only there.) · **Testability:** 🔵
- **500 ms refresh storm** — if the provider returns `default(DateTimeOffset)` expiry
  (`:308`, when `result?.ExpiresOn` is null) or acquisition throws (`:235`),
  `nextRefresh` is ≤0 → clamped to 500 ms (`:243`) → re-fetches **every 500 ms forever
  with no backoff**, hammering the identity endpoint during an outage. · **Testability:** 🟢
  - **Repro:** the test project has `AzureServiceTokenProviderMock`; have it return a
    `default` expiry (or throw) and assert the mock is *not* called many times within,
    say, 2 seconds.
- **`(int)nextRefresh.TotalMilliseconds`** (`:245`) overflows for tokens valid
  > ~24.8 days → wrong/negative `Timer.Change` argument. · **Testability:** 🟢 (math)
- **First-render hot-path block** up to ~100 ms polling (`:193-199`) and can latch
  `string.Empty` as the cached token (`:200`). · **Testability:** 🔵
- **`AccessTokenRefresher.Dispose()`** exists (`:250`, disposes `Timer`+`CTS`) but no
  caller was found — the event `remove` (`:281`) only stops them. Worth confirming the
  renderer lifecycle ever disposes the refresher. · **Testability:** 🟡

### DataTables / NLogEntity
- **Truncation off-by-one** — `length >= ColumnStringValueMaxSize` + `Substring(0, …-1)`
  (`DataTablesTarget.cs:418/421` and `:432/434`) truncates a legal 32768-char value to
  32767. · **Status:** ✅ · **Testability:** 🟢 (mock captures entity; log a 32768-char
  property and assert length preserved).
- **PartitionKey/RowKey not sanitized** for `/ \ # ?` / control chars (`:300,305`) —
  illegal keys make Azure reject the whole transaction, silently (due to S1).
  · **Status:** ⚠️ · **Testability:** 🟡 (Azure rejection) / 🟢 (assert a sanitizer once added).
- **`Skip/Take` over an `IList`** in the >100-entity batch path (`:370-374`) is O(n²).
  · **Status:** ✅ · **Testability:** 🟢 (log >100 events with one PartitionKey; assert
  batches and, with a stopwatch/large N, the scaling).
- **`GenerateBatch` returns a lazy `Select`** enumerated *inside* `SubmitTransactionAsync`,
  so render exceptions escape the local try/catch (`:329-356`). · **Status:** ⚠️ ·
  **Testability:** 🟢 (a property layout that throws should be caught locally, not surface
  from the send).
- **`LogTimeStamp` collision** — duplicate-detection only checks `ContextProperties[0]`
  (`:399-413`); a user property named `LogTimeStamp` elsewhere throws on `entity.Add`.
  · **Status:** ⚠️ · **Testability:** 🟢

### EventGrid
- **No batching** — overrides only the single-event `WriteAsyncTask`; sends one HTTP
  request per event despite `SendEventsAsync` existing. · **Status:** ⚠️ ·
  **Testability:** 🟢 (the `EventGridServiceMock` can count send calls).
- **Brittle `DataFormat` inference** from `ContentType` `Layout.ToString()` (`:49-71`)
  — returns the template, not the rendered value; order-dependent; can send JSON as
  Binary. · **Status:** ⚠️ · **Testability:** 🟢

### ServiceBus / Queue
- **`options` not passed** in the `ClientSecretCredential` branch
  (`ServiceBusTarget.cs:722`, `QueueStorageTarget.cs:362`) → WebSockets/proxy silently
  ignored under client-secret auth. · **Status:** ⚠️ · **Testability:** 🟡 (service-layer).
- **Queue `ContinueWith` uses default `TaskScheduler.Current`** (`:389`) and does a
  redundant `ExistsAsync` before `CreateIfNotExistsAsync` (`:406-410`).
  · **Status:** ✅ · **Testability:** 🟡.

---

## ✅ Reviewed and found sound
`SortHelpers.BucketSort` (efficient single-bucket fast path, no double key-eval),
the `logEvents.Count == 1` fast paths, `AzureCredentialHelpers` credential selection,
and `WriteAsyncTask(LogEventInfo)` throwing `NotImplementedException` (the `IList`
overload is the one used).

---

## Reproduction strategy (for the next phase)

The repo's test pattern is: each service interface has a `*ServiceMock`, and tests
build the target with the mock, configure `Layout` properties, log, `Flush()`, and
assert against the mock's captured payloads. Every `src` project exposes internals to
its `.Tests` project via `InternalsVisibleTo`.

**Start with the 🟢 unit-testable items — fastest path to a failing test:**

| Finding | One-line repro |
| --- | --- |
| Table-name repair (S/helper) | `CheckAndRepairTableNamingRules("123logs")` should be valid; today returns `"123logs"`. |
| DataTables truncation | Log a property of exactly 32768 chars; assert the captured value length is 32768, not 32767. |
| AccessToken refresh storm | Mock returns `default` expiry; assert the provider isn't called repeatedly within ~2 s. |
| Batch-size math (S3) | Unit-test `CalculateBatchSize`/`EstimateEventDataSize` with large inputs; assert no negative/overflowed size and a sane chunk count. |
| Name-cache concurrency (S2) | Parallel `LookupStorageName` with >1000 keys; assert no exception. |
| EventGrid no-batching | Log N events in one flush; assert the mock recorded 1 batched send, not N. |

**The 🟡 service-layer items (S1, S2 client cache, S5) need a faithful host.** Options:
1. **Azurite emulator** integration tests against the real `CloudBlobService` /
   `CloudQueueService` / `CloudTableService` (there is an existing
   `NLog.Extensions.AzureStorage.IntegrationTest` console project to build on).
2. **Refactor for testability** — extract the "initialize-then-send" step behind a
   seam so a unit test can inject a faulting send and assert the returned task faults
   (this is the cleanest way to lock in S1 as a regression test before fixing it).

**The 🔵 timing items (S4 contention, token volatility/first-render block)** are best
captured with micro-benchmarks or targeted stress harnesses rather than assertions.
