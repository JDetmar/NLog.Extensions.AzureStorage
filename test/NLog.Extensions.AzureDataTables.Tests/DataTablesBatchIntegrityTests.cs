using System;
using System.IO;
using System.Linq;
using Azure.Data.Tables;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // Azure table transactions are ATOMIC (<=100 entities, one partition, all-or-nothing). GenerateBatch
    // returned a lazy `Select`, so the per-event entity render (layouts/properties) was deferred OUT of
    // WriteAsyncTask's try/catch and INTO the SDK's enumeration during SubmitTransactionAsync. A single
    // event whose layout throws there faulted the WHOLE transaction -> every good sibling in the partition
    // was lost (and, since exceptions now propagate to AsyncTaskTarget, the retried batch failed again and
    // was ultimately dropped). The fix builds entities EAGERLY inside the guard and drops only the poison
    // event so the remaining events still form a valid transaction.
    //
    // Note on the trigger: NLog precalculates VOLATILE layouts on the logging thread (a throw there drops
    // just that one event upstream). Only [ThreadAgnostic] layouts -- and the always-deferred RowKey -- are
    // rendered later, during the batch build. So a [ThreadAgnostic] throwing layout is the realistic poison.
    [ThreadAgnostic]
    sealed class PoisonLayout : Layout
    {
        private readonly string _trigger;
        public PoisonLayout(string trigger) { _trigger = trigger; }

        protected override string GetFormattedMessage(LogEventInfo logEvent)
            => logEvent.Message == _trigger ? throw new InvalidOperationException("render boom") : logEvent.Message;

        protected override void RenderFormattedMessage(LogEventInfo logEvent, System.Text.StringBuilder target)
        {
            if (logEvent.Message == _trigger) throw new InvalidOperationException("render boom");
            target.Append(logEvent.Message);
        }
    }

    // OversizeMessage-style tests mutate the global InternalLogger.LogWriter/LogLevel; isolate them.
    [CollectionDefinition("DataTables InternalLogger isolation", DisableParallelization = true)]
    public class DataTablesInternalLoggerIsolationCollection { }

    [Collection("DataTables InternalLogger isolation")]
    public class DataTablesBatchIntegrityTests
    {
        private const string Table = "PoisonTbl";

        private static (LogFactory factory, CloudTableServiceMock svc, Logger logger) Build(int batchSize)
        {
            var logFactory = new LogFactory();
            var logConfig = new LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesBatchIntegrityTests);
            var svc = new CloudTableServiceMock();
            var target = new DataTablesTarget(svc)
            {
                ConnectionString = "${var:ConnectionString}",
                TableName = "${logger}",
                Layout = "${message}",
                BatchSize = batchSize,           // deliver the flush in a single WriteAsyncTask call
                TaskDelayMilliseconds = 1,
            };
            target.ContextProperties.Add(new TargetPropertyWithContext("Payload", new PoisonLayout("POISON")));
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;
            return (logFactory, svc, logFactory.GetLogger(Table));
        }

        private static T CaptureInternalLog<T>(out string log, Func<T> action)
        {
            var originalWriter = InternalLogger.LogWriter;
            var originalLevel = InternalLogger.LogLevel;
            var capture = new StringWriter();
            InternalLogger.LogWriter = capture;
            InternalLogger.LogLevel = LogLevel.Error;
            try { var r = action(); log = capture.ToString(); return r; }
            finally
            {
                InternalLogger.LogWriter = originalWriter;
                InternalLogger.LogLevel = originalLevel;
            }
        }

        [Fact]
        public void PoisonEvent_InSmallBatch_IsDroppedAndLogged_GoodSiblingsStillCommit()
        {
            var (factory, svc, logger) = Build(batchSize: 100);   // <=100 path

            var committed = CaptureInternalLog(out var log, () =>
            {
                logger.Info("good-0");
                logger.Info("good-1");
                logger.Info("POISON");        // its layout throws at build time
                logger.Info("good-3");
                logger.Info("good-4");
                factory.Flush();
                return svc.PeekAllAdded(Table).Cast<TableEntity>().ToList();
            });

            var payloads = committed.Select(e => e["Payload"].ToString()).OrderBy(x => x).ToList();

            // The 4 good events still commit (the whole batch is NOT lost); the poison is excluded.
            Assert.Equal(new[] { "good-0", "good-1", "good-3", "good-4" }, payloads);
            Assert.Single(svc.GetBatches(Table));                         // as one valid atomic transaction
            Assert.Contains("Dropped 1 logevents", log);                  // distinctly logged, not silently lost
        }

        [Fact]
        public void PoisonEvent_InLargeMultiBatchPartition_IsDroppedAndLogged_GoodSiblingsStillCommit()
        {
            var (factory, svc, logger) = Build(batchSize: 10000);  // all events in one call -> >100 path

            var committed = CaptureInternalLog(out var log, () =>
            {
                for (int i = 0; i < 150; ++i)
                    logger.Info(i == 50 ? "POISON" : "good-" + i);
                factory.Flush();
                return svc.PeekAllAdded(Table).Cast<TableEntity>().ToList();
            });

            var batches = svc.GetBatches(Table);

            Assert.Equal(149, committed.Count);                           // 149 good events still commit
            Assert.DoesNotContain(committed, e => e["Payload"].ToString() == "POISON");
            Assert.True(batches.Count >= 2, $"expected the >100 partition to split, got {batches.Count} batch(es)");
            Assert.All(batches, b => Assert.InRange(b.Count, 1, 100));    // each a valid <=100 transaction
            Assert.Contains("Dropped 1 logevents", log);
        }
    }
}
