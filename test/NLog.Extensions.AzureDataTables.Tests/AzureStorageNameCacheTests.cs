using NLog.Extensions.AzureStorage;
using Xunit;

namespace NLog.Extensions.AzureTableStorage.Tests
{
    public class AzureStorageNameCacheTests
    {
        // Azure table naming rules: alphanumeric only, may not begin with a digit,
        // 3-63 chars, case-insensitive. The repair routine must RETURN a name that
        // satisfies these rules, not the original invalid input.

        [Theory]
        [InlineData("MyValidTable", "MyValidTable")]   // already valid -> unchanged (fast path)
        [InlineData("logs", "logs")]                    // already valid -> unchanged
        public void CheckAndRepairTableNamingRules_ValidName_ReturnedUnchanged(string input, string expected)
        {
            Assert.Equal(expected, AzureStorageNameCache.CheckAndRepairTableNamingRules(input));
        }

        [Fact]
        public void CheckAndRepairTableNamingRules_LeadingDigits_StripsLeadingDigits()
        {
            // "123logs" is invalid (begins with a digit). Repair should drop the
            // leading digits and return "logs" -- NOT the original "123logs".
            var result = AzureStorageNameCache.CheckAndRepairTableNamingRules("123logs");

            Assert.Equal("logs", result);
            Assert.False(char.IsDigit(result[0]), "repaired table name must not start with a digit");
        }

        [Fact]
        public void CheckAndRepairTableNamingRules_ForbiddenCharacters_AreRemoved()
        {
            // Dashes/dots/etc are not allowed in table names (alphanumeric only).
            var result = AzureStorageNameCache.CheckAndRepairTableNamingRules("my-table.name");

            Assert.Equal("mytablename", result);
            Assert.DoesNotContain('-', result);
            Assert.DoesNotContain('.', result);
        }

        [Fact]
        public void CheckAndRepairTableNamingRules_Unrepairable_FallsBackToDefault()
        {
            // Nothing usable -> documented "Logs" default.
            Assert.Equal("Logs", AzureStorageNameCache.CheckAndRepairTableNamingRules("--"));
        }

        [Fact]
        public void CheckAndRepairTableNamingRules_Null_DoesNotThrow()
        {
            // Defensive: a null rendered name must not throw (container path already guards this).
            var result = AzureStorageNameCache.CheckAndRepairTableNamingRules(null);
            Assert.Equal("Logs", result);
        }
    }
}
