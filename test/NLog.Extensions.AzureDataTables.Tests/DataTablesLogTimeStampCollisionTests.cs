using System;
using System.Linq;
using Azure.Data.Tables;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    // A user can override the library's default "LogTimeStamp" column by adding a context property of
    // that name. The override was only detected at ContextProperties[0], so a "LogTimeStamp" at index >= 1
    // was NOT recognized as the override: the library default was still added, and the override's
    // skip-when-empty semantics did not apply. (Azure.Data.Tables 12.11.0 TableEntity.Add OVERWRITES a
    // duplicate key rather than throwing, so a non-empty user value still wins -- but an EMPTY user value
    // leaks the library default. The fix detects the override at ANY index so behavior is position-independent.)
    public class DataTablesLogTimeStampCollisionTests
    {
        private static TableEntity LogOnce(Action<DataTablesTarget> configure)
        {
            var logFactory = new LogFactory();
            var logConfig = new LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(DataTablesLogTimeStampCollisionTests);
            var svc = new CloudTableServiceMock();
            var target = new DataTablesTarget(svc)
            {
                ConnectionString = "${var:ConnectionString}",
                TableName = "${logger}",
                Layout = "${message}",
            };
            configure(target);
            logConfig.AddRuleForAllLevels(target);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("Test").Info("hi");
            logFactory.Flush();
            return svc.PeekLastAdded("Test").Cast<TableEntity>().Single();
        }

        [Fact]
        public void UserLogTimeStampAtIndex1_NonEmpty_WinsWithSingleValue_NoThrow()
        {
            var entity = LogOnce(t =>
            {
                t.ContextProperties.Add(new TargetPropertyWithContext("Other", "x"));
                t.ContextProperties.Add(new TargetPropertyWithContext("LogTimeStamp", "2026-06-22"));
            });

            Assert.Equal("x", entity["Other"].ToString());
            Assert.True(entity.ContainsKey("LogTimeStamp"));
            Assert.Equal("2026-06-22", entity["LogTimeStamp"]);   // the user's value, stored as their string
        }

        [Fact]
        public void UserLogTimeStampAtIndex1_Empty_DoesNotLeakLibraryDefault()
        {
            // Regression: with only ContextProperties[0] checked, a LogTimeStamp override at index 1 left
            // the library default in place, so an empty user value leaked the default timestamp. A
            // LogTimeStamp override at index 0 produces NO column for an empty value; index >= 1 must match.
            var entity = LogOnce(t =>
            {
                t.ContextProperties.Add(new TargetPropertyWithContext("Other", "x"));
                t.ContextProperties.Add(new TargetPropertyWithContext("LogTimeStamp", Layout.FromMethod(_ => string.Empty)));
            });

            Assert.False(entity.ContainsKey("LogTimeStamp"),
                "an empty LogTimeStamp override at index >= 1 must not leak the library default (parity with index 0)");
        }

        [Fact]
        public void UserLogTimeStampAtIndex0_NonEmpty_StillWins()
        {
            var entity = LogOnce(t =>
            {
                t.ContextProperties.Add(new TargetPropertyWithContext("LogTimeStamp", "2026-06-22"));
                t.ContextProperties.Add(new TargetPropertyWithContext("Other", "x"));
            });

            Assert.Equal("2026-06-22", entity["LogTimeStamp"]);
        }

        [Fact]
        public void NoUserLogTimeStamp_LibraryDefaultIsAdded()
        {
            var entity = LogOnce(t => t.ContextProperties.Add(new TargetPropertyWithContext("ThreadId", "${threadid}")));

            Assert.True(entity.ContainsKey("LogTimeStamp"));
            Assert.IsType<DateTime>(entity["LogTimeStamp"]);      // the library default is a DateTime, not a string
        }
    }
}
