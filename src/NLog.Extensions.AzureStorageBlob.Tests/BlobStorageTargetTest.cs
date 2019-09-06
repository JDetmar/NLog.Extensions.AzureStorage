using System;
using System.Linq;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureStorageBlob.Tests
{
    public class BlobStorageTargetTest
    {
        [Fact]
        public void SingleLogEventTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(BlobStorageTargetTest);
            var cloudBlobService = new CloudBlobServiceMock();
            var blobStorageTarget = new BlobStorageTarget(cloudBlobService);
            blobStorageTarget.ConnectionString = "${var:ConnectionString}";
            blobStorageTarget.Container = "${level}";
            blobStorageTarget.BlobName = "${logger}";
            blobStorageTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(blobStorageTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("Test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(BlobStorageTargetTest), cloudBlobService.ConnectionString);
            Assert.Single(cloudBlobService.AppendBlob);   // One partition
            Assert.Equal("Hello World" + System.Environment.NewLine, cloudBlobService.PeekLastAppendBlob("info", "Test"));
        }

        [Fact]
        public void MultiplePartitionKeysTest()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(BlobStorageTargetTest);
            var cloudBlobService = new CloudBlobServiceMock();
            var blobStorageTarget = new BlobStorageTarget(cloudBlobService);
            blobStorageTarget.ConnectionString = "${var:ConnectionString}";
            blobStorageTarget.Container = "${level}";
            blobStorageTarget.BlobName = "${logger}";
            blobStorageTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(blobStorageTarget);
            logFactory.Configuration = logConfig;
            for (int i = 0; i < 50; ++i)
            {
                logFactory.GetLogger("Test1").Info("Hello");
                logFactory.GetLogger("Test2").Debug("Goodbye");
            }
            logFactory.Flush();
            Assert.Equal(2, cloudBlobService.AppendBlob.Count);   // Two partitions
            Assert.NotEqual(string.Empty, cloudBlobService.PeekLastAppendBlob("info", "Test1"));
            Assert.NotEqual(string.Empty, cloudBlobService.PeekLastAppendBlob("debug", "Test2"));
        }
    }
}
