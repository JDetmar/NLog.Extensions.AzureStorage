using System;
using System.Collections.Generic;
using System.Linq;
using NLog.Targets;
using Xunit;

namespace NLog.Extensions.AzureBlobStorage.Tests
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
            blobStorageTarget.BlobTags.Add(new TargetPropertyWithContext() { Name = "MyTag", Layout = "MyTagValue" });
            blobStorageTarget.BlobMetadata.Add(new TargetPropertyWithContext() { Name = "MyMetadata", Layout = "MyMetadataValue" });
            logConfig.AddRuleForAllLevels(blobStorageTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("Test").Info("Hello World");
            logFactory.Flush();
            Assert.Equal(nameof(BlobStorageTargetTest), cloudBlobService.ConnectionString);
            Assert.Single(cloudBlobService.BlobMetadata);
            Assert.Contains(new KeyValuePair<string, string>("MyMetadata", "MyMetadataValue"), cloudBlobService.BlobMetadata);
            Assert.Single(cloudBlobService.BlobTags);
            Assert.Contains(new KeyValuePair<string, string>("MyTag", "MyTagValue"), cloudBlobService.BlobTags);
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
            Assert.NotNull(cloudBlobService.PeekLastAppendBlob("info", "Test1"));
            Assert.NotNull(cloudBlobService.PeekLastAppendBlob("debug", "Test2"));
        }

        [Fact]
        public void RollOnBlockCountExceedsLimit()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(BlobStorageTargetTest);
            var cloudBlobService = new CloudBlobServiceMock();
            var logEventInfo = LogEventInfo.CreateNullEvent();
            var blobStorageTarget = new BlobStorageTarget(cloudBlobService);
            blobStorageTarget.ConnectionString = "${var:ConnectionString}";
            blobStorageTarget.Container = "${level}";
            blobStorageTarget.BlobName = "${logger}_${sequenceid:cached=true}";
            blobStorageTarget.Layout = "${message}";
            logConfig.AddRuleForAllLevels(blobStorageTarget);
            logFactory.Configuration = logConfig;

            var logEvent1 = LogEventInfo.Create(LogLevel.Info, null, "Hello");
            logFactory.GetLogger("Test1").Log(logEvent1);
            logFactory.Flush();
            Assert.Contains("Hello", cloudBlobService.PeekLastAppendBlob("info", "Test1_" + logEvent1.SequenceID.ToString()));
            var logEvent2 = LogEventInfo.Create(LogLevel.Info, null, "Goodbye");
            logFactory.GetLogger("Test1").Log(logEvent2);
            logFactory.Flush();
            Assert.Contains("Goodbye", cloudBlobService.PeekLastAppendBlob("info", "Test1_" + logEvent1.SequenceID.ToString()));

            cloudBlobService.FirstBlobNameHasBlockCountExceeded = true;

            var logEvent3 = LogEventInfo.Create(LogLevel.Info, null, "Hello Roll");
            logFactory.GetLogger("Test1").Log(logEvent3);
            logFactory.Flush();
            Assert.Contains("Hello Roll", cloudBlobService.PeekLastAppendBlob("info", "Test1_" + logEvent3.SequenceID.ToString()));
            var logEvent4 = LogEventInfo.Create(LogLevel.Info, null, "Goodbye Roll");
            logFactory.GetLogger("Test1").Log(logEvent4);
            logFactory.Flush();
            Assert.Contains("Goodbye Roll", cloudBlobService.PeekLastAppendBlob("info", "Test1_" + logEvent3.SequenceID.ToString()));
        }
    }
}
