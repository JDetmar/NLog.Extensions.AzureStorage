using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
#if !NETSTANDARD
using Microsoft.Azure;
using System.Configuration;
#endif

namespace NLog.Extensions.AzureStorage
{
    /// <summary>
    /// Azure Blob Storage NLog Target
    /// </summary>
    /// <seealso cref="NLog.Targets.TargetWithLayout" />
    [Target("AzureBlobStorage")]
    public sealed class BlobStorageTarget : TargetWithLayout
    {
        private CloudBlobClient _client;
        private CloudAppendBlob _appendBlob;
        private CloudBlobContainer _container;

        //Delegates for bucket sorting
        private SortHelpers.KeySelector<AsyncLogEventInfo, string> _getBlobNameDelegate;
        private SortHelpers.KeySelector<AsyncLogEventInfo, string> _getContainerNameDelegate;

        public string ConnectionString { get => (_connectionString as SimpleLayout)?.Text ?? null; set => _connectionString = value; }
        private Layout _connectionString;
        public string ConnectionStringKey { get; set; }

        [RequiredParameter]
        public Layout Container { get; set; }

        [RequiredParameter]
        public Layout BlobName { get; set; }

        public BlobStorageTarget()
        {
            OptimizeBufferReuse = true;
        }

        /// <summary>
        /// Initializes the target. Can be used by inheriting classes
        /// to initialize logging.
        /// </summary>
        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            var connectionString = _connectionString != null ? RenderLogEvent(_connectionString, LogEventInfo.CreateNullEvent()) : string.Empty;
#if !NETSTANDARD
            if (!string.IsNullOrWhiteSpace(ConnectionStringKey))
            {
                connectionString = CloudConfigurationManager.GetSetting(ConnectionStringKey);
                if (String.IsNullOrWhiteSpace(connectionString))
                    connectionString = ConfigurationManager.ConnectionStrings[ConnectionStringKey]?.ConnectionString;
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    InternalLogger.Error($"AzureBlobStorageTarget: No ConnectionString found with ConnectionStringKey: {ConnectionStringKey}.");
                    throw new Exception($"No ConnectionString found with ConnectionStringKey: {ConnectionStringKey}.");
                }
            }
#endif

            if (String.IsNullOrWhiteSpace(connectionString))
            {
                InternalLogger.Error("AzureBlobStorageTarget: A ConnectionString or ConnectionStringKey is required.");
                throw new Exception("A ConnectionString or ConnectionStringKey is required");
            }

            _client = CloudStorageAccount.Parse(connectionString).CreateCloudBlobClient();

            InternalLogger.Trace("AzureBlobStorageWrapper - Initialized");
        }

        /// <summary>
        /// Writes logging event to the log target.
        /// classes.
        /// </summary>
        /// <param name="logEvent">Logging event to be written out.</param>
        protected override void Write(LogEventInfo logEvent)
        {
            if (String.IsNullOrEmpty(logEvent.Message))
                return;

            string containerName = string.Empty;
            string blobName = string.Empty;

            try
            {
                containerName = RenderLogEvent(Container, logEvent);
                blobName = RenderLogEvent(BlobName, logEvent);
                var layoutMessage = RenderLogEvent(Layout, logEvent);
                var logMessage = string.Concat(layoutMessage, Environment.NewLine);

                containerName = CheckAndRepairContainerNamingRules(containerName);
                blobName = CheckAndRepairBlobNamingRules(blobName);
                InitializeContainer(containerName);
                AppendBlobText(blobName, logMessage);
            }
            catch (StorageException ex)
            {
                InternalLogger.Error(ex, "AzureBlobStorageTarget: failed writing to blob: {0} in container: {1}", blobName, containerName);
                throw;
            }
        }

        /// <summary>
        /// Writes an array of logging events to the log target. By default it iterates on all
        /// events and passes them to "Write" method. Inheriting classes can use this method to
        /// optimize batch writes.
        /// </summary>
        /// <param name="logEvents">Logging events to be written out.</param>
        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
            if (logEvents.Count <= 1)
            {
                base.Write(logEvents);
                return;
            }

            //must sort into containers and then into the blobs for the container
            if (_getContainerNameDelegate == null)
                _getContainerNameDelegate = c => RenderLogEvent(Container, c.LogEvent);

            var containerBuckets = SortHelpers.BucketSort(logEvents, _getContainerNameDelegate);

            //Iterate over all the containers being written to
            foreach (var containerBucket in containerBuckets)
            {
                string containerName = containerBucket.Key;
                string blobName = string.Empty;

                try
                {
                    containerName = CheckAndRepairContainerNamingRules(containerBucket.Key);
                    InitializeContainer(containerName);

                    if (_getBlobNameDelegate == null)
                        _getBlobNameDelegate = c => RenderLogEvent(BlobName, c.LogEvent);

                    var blobBuckets = SortHelpers.BucketSort(containerBucket.Value, _getBlobNameDelegate);

                    //Iterate over all the blobs in the container to be written to
                    foreach (var blobBucket in blobBuckets)
                    {
                        //Initilize StringBuilder size based on number of items to write. Default StringBuilder initialization size is 16 characters.
                        var logMessage = new StringBuilder(blobBucket.Value.Count * 128);

                        //add each message for the destination append blob
                        foreach (var asyncLogEventInfo in blobBucket.Value)
                        {
                            var layoutMessage = RenderLogEvent(Layout, asyncLogEventInfo.LogEvent);
                            logMessage.AppendLine(layoutMessage);
                        }

                        blobName = CheckAndRepairBlobNamingRules(blobBucket.Key);
                        AppendBlobText(blobName, logMessage.ToString());

                        foreach (var asyncLogEventInfo in blobBucket.Value)
                            asyncLogEventInfo.Continuation(null);
                    }
                }
                catch (StorageException ex)
                {
                    InternalLogger.Error(ex, "AzureBlobStorageTarget: failed writing to blob: {0} in container: {1}", blobName, containerName);
                    throw;
                }
            }
        }

        /// <summary>
        /// Initializes the BLOB.
        /// </summary>
        /// <param name="blobName">Name of the BLOB.</param>
        private void InitializeBlob(string blobName)
        {
            if (_appendBlob == null || _appendBlob.Name != blobName)
            {
                _appendBlob = _container.GetAppendBlobReference(blobName);

#if NETSTANDARD
                bool blobExists = _appendBlob.ExistsAsync().GetAwaiter().GetResult();
#else
                bool blobExists = _appendBlob.Exists();
#endif
                if (!blobExists)
                {
                    _appendBlob.Properties.ContentType = "text/plain";

#if NETSTANDARD
                    _appendBlob.CreateOrReplaceAsync().GetAwaiter().GetResult();
#else
                    _appendBlob.CreateOrReplace(AccessCondition.GenerateIfNotExistsCondition());
#endif
                }
            }
        }

        /// <summary>
        /// Initializes the Azure storage container and creates it if it doesn't exist.
        /// </summary>
        /// <param name="containerName">Name of the container.</param>
        private void InitializeContainer(string containerName)
        {
            if (_container == null || _container.Name != containerName)
            {
                _container = _client.GetContainerReference(containerName);
                try
                {
#if NETSTANDARD
                    _container.CreateIfNotExistsAsync().GetAwaiter().GetResult();
#else
                    _container.CreateIfNotExists();
#endif
                }
                catch (StorageException storageException)
                {
                    InternalLogger.Error(storageException, "NLog.Extensions.AzureStorage failed to get a reference to storage container.");
                    throw;
                }

                _appendBlob = null;
            }
        }

        private void AppendBlobText(string blobName, string logMessage)
        {
            InitializeBlob(blobName);

#if NETSTANDARD
            _appendBlob.AppendTextAsync(logMessage).GetAwaiter().GetResult();
#else
            _appendBlob.AppendText(logMessage);
#endif
        }

        /// <summary>
        /// Checks the and repairs container name acording to the Azure naming rules.
        /// </summary>
        /// <param name="requestedContainerName">Name of the requested container.</param>
        /// <returns></returns>
        private static string CheckAndRepairContainerNamingRules(string requestedContainerName)
        {
            /*  https://docs.microsoft.com/en-us/rest/api/storageservices/fileservices/naming-and-referencing-containers--blobs--and-metadata
                Container Names
                A container name must be a valid DNS name, conforming to the following naming rules:
                Container names must start with a letter or number, and can contain only letters, numbers, and the dash (-) character.
                Every dash (-) character must be immediately preceded and followed by a letter or number; 
                    consecutive dashes are not permitted in container names.
                All letters in a container name must be lowercase.
                Container names must be from 3 through 63 characters long.
            */
            InternalLogger.Trace("AzureTableStorageTarget: Requested Container Name: {0}", requestedContainerName);
            requestedContainerName = requestedContainerName?.Trim() ?? string.Empty;
            bool validContainerName = requestedContainerName.Length > 0;
            for (int i = 0; i < requestedContainerName.Length; ++i)
            {
                char chr = requestedContainerName[i];
                if (chr >= 'A' && chr <= 'Z')
                    continue;
                if (chr >= 'a' && chr <= 'z')
                    continue;
                if (chr >= '0' && chr <= '9')
                    continue;
                if (chr == '_' || chr == '-' || chr == '.')
                    continue;
                validContainerName = false;
                break;
            }
            if (validContainerName)
                return requestedContainerName;

            const string validContainerPattern = "^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$";
            var loweredRequestedContainerName = requestedContainerName.ToLower();
            if (Regex.Match(loweredRequestedContainerName, validContainerPattern).Success)
            {
                InternalLogger.Trace("AzureTableStorageTarget: Using Container Name: {0}", loweredRequestedContainerName);
                //valid name okay to lower and use
                return loweredRequestedContainerName;
            }
            InternalLogger.Trace("AzureTableStorageTarget: Requested Container Name violates Azure naming rules! Attempting to clean.");
            const string trimLeadingPattern = "^.*?(?=[a-zA-Z0-9])";
            const string trimTrailingPattern = "(?<=[a-zA-Z0-9]).*?";
            const string trimFobiddenCharactersPattern = "[^a-zA-Z0-9-]";
            const string trimExtraHyphensPattern = "-+";

            var pass1 = Regex.Replace(requestedContainerName, trimFobiddenCharactersPattern, String.Empty, RegexOptions.None);
            var pass2 = Regex.Replace(pass1, trimTrailingPattern, String.Empty, RegexOptions.RightToLeft);
            var pass3 = Regex.Replace(pass2, trimLeadingPattern, String.Empty, RegexOptions.None);
            var pass4 = Regex.Replace(pass3, trimExtraHyphensPattern, "-", RegexOptions.None);
            var loweredCleanedContainerName = pass4.ToLower();
            if (Regex.Match(loweredCleanedContainerName, validContainerPattern).Success)
            {
                InternalLogger.Trace("AzureTableStorageTarget: Using Cleaned Container name: {0}", loweredCleanedContainerName);
                return loweredCleanedContainerName;
            }
            return "defaultlog";
        }

        /// <summary>
        /// Checks the and repairs BLOB name acording to the Azure naming rules.
        /// </summary>
        /// <param name="blobName">Name of the BLOB.</param>
        /// <returns></returns>
        private static string CheckAndRepairBlobNamingRules(string blobName)
        {
            /*  https://docs.microsoft.com/en-us/rest/api/storageservices/fileservices/naming-and-referencing-containers--blobs--and-metadata
                Blob Names

                A blob name must conforming to the following naming rules:
                A blob name can contain any combination of characters.
                A blob name must be at least one character long and cannot be more than 1,024 characters long.
                Blob names are case-sensitive.
                Reserved URL characters must be properly escaped.

                The number of path segments comprising the blob name cannot exceed 254.
                A path segment is the string between consecutive delimiter characters (e.g., the forward slash '/') that corresponds to the name of a virtual directory.
            */
            if (String.IsNullOrWhiteSpace(blobName) || blobName.Length > 1024)
            {
                var blobDefault = String.Concat("Log-", DateTime.UtcNow.ToString("yy-MM-dd"), ".log");
                InternalLogger.Error("AzureTableStorageTarget: Invalid Blob Name provided: {0} | Using default: {1}", blobName, blobDefault);
                return blobDefault;
            }
            InternalLogger.Trace("AzureTableStorageTarget: Using provided blob name: {0}", blobName);
            return blobName;
        }
    }
}
