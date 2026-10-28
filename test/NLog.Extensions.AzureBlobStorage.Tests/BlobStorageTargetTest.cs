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
            Assert.Equal("MyMetadataValue", cloudBlobService.PeekLastBlobMetadata("info", "Test")["MyMetadata"]);
            Assert.Equal("MyTagValue", cloudBlobService.PeekLastBlobTags("info", "Test")["MyTag"]);
            Assert.Single(cloudBlobService.AppendBlob);   // One partition
            Assert.Equal("Hello World" + System.Environment.NewLine, cloudBlobService.PeekLastAppendBlob("info", "Test"));
        }

        [Fact]
        public void BlobMetadataAndTagsAreRenderedPerNewBlob()
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
            blobStorageTarget.BlobMetadata.Add(new TargetPropertyWithContext() { Name = "MetaValue", Layout = "${logger}" });
            blobStorageTarget.BlobTags.Add(new TargetPropertyWithContext() { Name = "TagValue", Layout = "${logger}" });
            logConfig.AddRuleForAllLevels(blobStorageTarget);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("BlobA").Info("first payload");
            logFactory.GetLogger("BlobB").Info("second payload");
            logFactory.Flush();

            Assert.Equal("BlobA", cloudBlobService.PeekLastBlobMetadata("info", "BlobA")["MetaValue"]);
            Assert.Equal("BlobA", cloudBlobService.PeekLastBlobTags("info", "BlobA")["TagValue"]);
            Assert.Equal("BlobB", cloudBlobService.PeekLastBlobMetadata("info", "BlobB")["MetaValue"]);
            Assert.Equal("BlobB", cloudBlobService.PeekLastBlobTags("info", "BlobB")["TagValue"]);
        }

        [Fact]
        public void BlobMetadataAndTagsRespectIncludeEmptyValue()
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
            blobStorageTarget.BlobMetadata.Add(new TargetPropertyWithContext() { Name = "SkipMeta", Layout = "${event-properties:item=MissingMeta}" });
            blobStorageTarget.BlobMetadata.Add(new TargetPropertyWithContext() { Name = "KeepMeta", Layout = "${event-properties:item=MissingMeta}", IncludeEmptyValue = true });
            blobStorageTarget.BlobTags.Add(new TargetPropertyWithContext() { Name = "SkipTag", Layout = "${event-properties:item=MissingTag}" });
            blobStorageTarget.BlobTags.Add(new TargetPropertyWithContext() { Name = "KeepTag", Layout = "${event-properties:item=MissingTag}", IncludeEmptyValue = true });
            logConfig.AddRuleForAllLevels(blobStorageTarget);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("Test").Info("Hello World");
            logFactory.Flush();

            var metadata = cloudBlobService.PeekLastBlobMetadata("info", "Test");
            var tags = cloudBlobService.PeekLastBlobTags("info", "Test");

            Assert.False(metadata.ContainsKey("SkipMeta"));
            Assert.Equal(string.Empty, metadata["KeepMeta"]);
            Assert.False(tags.ContainsKey("SkipTag"));
            Assert.Equal(string.Empty, tags["KeepTag"]);
        }

        [Fact]
        public void BlobMetadataAndTagsAreSanitizedToAzureRules()
        {
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            logConfig.Variables["ConnectionString"] = nameof(BlobStorageTargetTest);
            var cloudBlobService = new CloudBlobServiceMock();
            var blobStorageTarget = new BlobStorageTarget(cloudBlobService);
            blobStorageTarget.ConnectionString = "${var:ConnectionString}";
            blobStorageTarget.Container = "${level}";
            blobStorageTarget.BlobName = "app.log";
            blobStorageTarget.Layout = "${message}";
            blobStorageTarget.BlobMetadata.Add(new TargetPropertyWithContext() { Name = "User", Layout = "${event-properties:item=User}" });
            blobStorageTarget.BlobTags.Add(new TargetPropertyWithContext() { Name = "Source", Layout = "${logger}" });
            blobStorageTarget.BlobTags.Add(new TargetPropertyWithContext() { Name = "Long", Layout = "${event-properties:item=Long}" });
            logConfig.AddRuleForAllLevels(blobStorageTarget);
            logFactory.Configuration = logConfig;

            logFactory.GetLogger("MyApp.Repo<Order>").WithProperty("User", "José\r\n").WithProperty("Long", new string('x', 300)).Info("Hello World");
            logFactory.Flush();

            Assert.Equal("Jos___", cloudBlobService.PeekLastBlobMetadata("info", "app.log")["User"]);
            var tags = cloudBlobService.PeekLastBlobTags("info", "app.log");
            Assert.Equal("MyApp.Repo_Order_", tags["Source"]);
            Assert.Equal(new string('x', 256), tags["Long"]);
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
