using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using NLog.Common;
using NLog.Config;
using NLog.Extensions.AzureStorage;
using NLog.Layouts;

namespace NLog.Targets
{
    /// <summary>
    /// Azure ServiceBus NLog Target
    /// </summary>
    [Target("AzureServiceBus")]
    public sealed class ServiceBusTarget : AsyncTaskTarget
    {
        private readonly ICloudServiceBus _cloudServiceBus;
        private SortHelpers.KeySelector<LogEventInfo, string> _getMessagePartitionKeyDelegate;
        private readonly char[] _reusableEncodingBuffer = new char[32 * 1024];  // Avoid large-object-heap

        /// <summary>
        /// Gets or sets the service bus connection string
        /// </summary>
        public Layout ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the EntityPath for service bus
        /// </summary>
        /// <remarks>
        /// Queues offer First In, First Out (FIFO) message delivery to one or more competing consumers
        /// </remarks>
        public Layout QueueName { get; set; }

        /// <summary>
        /// Gets or sets the EntityPath for service bus
        /// </summary>
        /// <remarks>
        /// In contrast to queues, in which each message is processed by a single consumer, topics provides a one-to-many form of communication
        /// </remarks>
        public Layout TopiceName { get; set; }

        /// <summary>
        /// Gets and sets type of the content for <see cref="ServiceBusMessage.ContentType"/>. Ex. application/json
        /// </summary>
        public Layout ContentType { get; set; }

        /// <summary>
        /// The partitionKey will be hashed to determine the partitionId to send the Message to
        /// </summary>
        public Layout PartitionKey { get; set; }

        /// <summary>
        /// Sessions enable Service Bus to guarantee message ordering as well as the consistency of session states.
        /// </summary>
        public Layout SessionId { get; set; }

        /// <summary>
        /// Gets and sets the label for <see cref="ServiceBusMessage.Subject"/>. Similar to an email subject line.
        /// </summary>
        [Obsolete("Replaced by Subject")]
        public Layout Label { get => Subject; set => Subject = value; }

        /// <summary>
        /// Gets and sets the label for <see cref="ServiceBusMessage.Subject"/>. Similar to an email subject line.
        /// </summary>
        public Layout Subject { get; set; }

        /// <summary>
        /// Gets and sets the <see cref="ServiceBusMessage.MessageId"/>.
        /// </summary>
        public Layout MessageId { get; set; }

        /// <summary>
        /// Gets and sets the correlationid for <see cref="ServiceBusMessage.CorrelationId"/>. For the purposes of correlation.
        /// </summary>
        public Layout CorrelationId { get; set; }

        /// <summary>
        /// Standard Tier = 256 KByte, Premium Tier = 1 MByte
        /// </summary>
        public int MaxBatchSizeBytes { get; set; } = 256 * 1024;

        /// <summary>
        /// The default time to live value for the message.
        /// </summary>
        /// <remarks>
        /// Messages older than their TimeToLive value will expire and no longer be retained
        /// in the message store. Subscribers will be unable to receive expired messages.
        /// </remarks>
        public Layout TimeToLiveSeconds { get; set; }

        /// <summary>
        /// The default time to live value for the message.
        /// </summary>
        /// <remarks>
        /// Messages older than their TimeToLive value will expire and no longer be retained
        /// in the message store. Subscribers will be unable to receive expired messages.
        /// </remarks>
        public Layout TimeToLiveDays { get; set; }

        /// <summary>
        /// Alternative to ConnectionString. Ex. {yournamespace}.servicebus.windows.net
        /// </summary>
        public Layout ServiceUri { get; set; }

        /// <summary>
        /// Obsolete instead use <see cref="ServiceUri"/>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use ServiceUri")]
        public Layout ServiceUrl { get => ServiceUri; set => ServiceUri = value; }

        /// <summary>
        /// TenantId for <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Requires <see cref="ServiceUri"/>.
        /// </summary>
        public Layout TenantIdentity { get; set; }

        /// <summary>
        /// Obsolete instead use <see cref="ManagedIdentityResourceId"/>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use ManagedIdentityResourceId")]
        public Layout ResourceIdentity { get => ManagedIdentityResourceId; set => ManagedIdentityResourceId = value; }

        /// <summary>
        /// ResourceId for <see cref="Azure.Identity.DefaultAzureCredentialOptions.ManagedIdentityResourceId"/> on <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Requires <see cref="ServiceUri"/> .
        /// </summary>
        /// <remarks>
        /// Do not configure this value together with <see cref="ManagedIdentityClientId"/>
        /// </remarks>
        public Layout ManagedIdentityResourceId { get; set; }

        /// <summary>
        /// Obsolete instead use <see cref="ManagedIdentityClientId"/>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use ManagedIdentityClientId")]
        public Layout ClientIdentity { get => ManagedIdentityClientId; set => ManagedIdentityClientId = value; }

        /// <summary>
        /// ManagedIdentityClientId for <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Requires <see cref="ServiceUri"/>.
        /// </summary>
        /// <remarks>
        /// If this value is configured, then <see cref="ManagedIdentityResourceId"/> should not be configured.
        /// </remarks>
        public Layout ManagedIdentityClientId { get; set; }

        /// <summary>
        /// Access signature for <see cref="Azure.AzureSasCredential"/> authentication. Requires <see cref="ServiceUri"/>.
        /// </summary>
        public Layout SharedAccessSignature { get; set; }

        /// <summary>
        /// AccountName for <see cref="Azure.AzureNamedKeyCredential"/> authentication. Requires <see cref="ServiceUri"/> and <see cref="AccessKey"/>.
        /// </summary>
        public Layout AccountName { get; set; }

        /// <summary>
        /// AccountKey for <see cref="Azure.AzureNamedKeyCredential"/> authentication. Requires <see cref="ServiceUri"/> and <see cref="AccountName"/>.
        /// </summary>
        public Layout AccessKey { get; set; }

        /// <summary>
        /// The connection uses the AMQP protocol over web sockets. See also <see cref="ServiceBusTransportType.AmqpWebSockets"/>
        /// </summary>
        public Layout UseWebSockets { get; set; }

        /// <summary>
        /// The proxy to use for communication over web sockets.
        /// </summary>
        public Layout WebSocketProxyAddress { get; set; }

        /// <summary>
        /// Custom endpoint address that can be used when establishing the connection.
        /// </summary>
        public Layout CustomEndpointAddress { get; set; }

        /// <summary>
        /// Gets a list of user properties (aka custom application properties) to add to the AMQP message
        /// </summary>
        [Obsolete("Replaced by MessageProperties")]
        [ArrayParameter(typeof(TargetPropertyWithContext), "userproperty")]
        public IList<TargetPropertyWithContext> UserProperties { get => MessageProperties; }

        /// <summary>
        /// Gets a list of application properties (aka custom user properties) to add to the AMQP message
        /// </summary>
        [Obsolete("Replaced by MessageProperties")]
        public IList<TargetPropertyWithContext> ApplicationProperties { get => MessageProperties; }

        /// <summary>
        /// Gets a list of message properties (aka custom user-application properties) to add to the AMQP message
        /// </summary>
        [ArrayParameter(typeof(TargetPropertyWithContext), "messageproperty")]
        public IList<TargetPropertyWithContext> MessageProperties { get => ContextProperties; }

        public ServiceBusTarget()
            :this(new CloudServiceBus())
        {
        }

        internal ServiceBusTarget(ICloudServiceBus cloudServiceBus)
        {
            TaskDelayMilliseconds = 200;
            BatchSize = 100;
            RetryDelayMilliseconds = 100;

            _cloudServiceBus = cloudServiceBus;
        }

        /// <inheritdoc />
        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            string connectionString = string.Empty;
            string serviceUri = string.Empty;
            string tenantIdentity = string.Empty;
            string managedIdentityResourceId = string.Empty;
            string managedIdentityClientId = string.Empty;
            string sharedAccessSignature = string.Empty;
            string storageAccountName = string.Empty;
            string storageAccountAccessKey = string.Empty;
            string queueOrTopicName = string.Empty;
            string useWebSockets = string.Empty;
            string webSocketProxyAddress = string.Empty;
            string customEndPointAddress = string.Empty;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                var queuePath = QueueName?.Render(defaultLogEvent)?.Trim();
                var topicPath = TopiceName?.Render(defaultLogEvent)?.Trim();
                queueOrTopicName = string.IsNullOrWhiteSpace(queuePath) ? topicPath : queuePath;
                if (string.IsNullOrWhiteSpace(queueOrTopicName))
                {
                    throw new NLogConfigurationException("QueuePath or TopicPath must be specified");
                }

                connectionString = ConnectionString?.Render(defaultLogEvent) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    serviceUri = ServiceUri?.Render(defaultLogEvent);
                    tenantIdentity = TenantIdentity?.Render(defaultLogEvent);
                    managedIdentityResourceId = ManagedIdentityResourceId?.Render(defaultLogEvent);
                    managedIdentityClientId = ManagedIdentityClientId?.Render(defaultLogEvent);
                    sharedAccessSignature = SharedAccessSignature?.Render(defaultLogEvent);
                    storageAccountName = AccountName?.Render(defaultLogEvent);
                    storageAccountAccessKey = AccessKey?.Render(defaultLogEvent);
                }

                useWebSockets = UseWebSockets?.Render(defaultLogEvent) ?? string.Empty;
                if (!string.IsNullOrEmpty(useWebSockets) && (string.Equals(useWebSockets.Trim(), bool.TrueString, StringComparison.OrdinalIgnoreCase) || string.Equals(useWebSockets.Trim(), "1", StringComparison.OrdinalIgnoreCase)))
                {
                    useWebSockets = bool.TrueString;
                }
                webSocketProxyAddress = WebSocketProxyAddress?.Render(defaultLogEvent) ?? string.Empty;
                customEndPointAddress = CustomEndpointAddress?.Render(defaultLogEvent) ?? string.Empty;

                var timeToLive = RenderDefaultTimeToLive();
                if (timeToLive <= TimeSpan.Zero)
                {
                    timeToLive = default(TimeSpan?);
                }

                _cloudServiceBus.Connect(connectionString, queueOrTopicName, serviceUri, tenantIdentity, managedIdentityResourceId, managedIdentityClientId, sharedAccessSignature, storageAccountName, storageAccountAccessKey, bool.TrueString == useWebSockets, webSocketProxyAddress, customEndPointAddress, timeToLive);
                InternalLogger.Debug("AzureServiceBusTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(serviceUri))
                    InternalLogger.Error(ex, "AzureServiceBusTarget(Name={0}): Failed to create ServiceBusClient with EntityPath={1} and ServiceUri={2}", Name, queueOrTopicName, serviceUri);
                else
                    InternalLogger.Error(ex, "AzureServiceBusTarget(Name={0}): Failed to create ServiceBusClient with EntityPath={1} and ConnectionString={2}", Name, queueOrTopicName, connectionString);
                throw;
            }
        }

        private TimeSpan? RenderDefaultTimeToLive()
        {
            string timeToLiveSeconds = null;
            string timeToLiveDays = null;

            try
            {
                timeToLiveSeconds = TimeToLiveSeconds?.Render(LogEventInfo.CreateNullEvent());
                if (!string.IsNullOrEmpty(timeToLiveSeconds))
                {
                    if (int.TryParse(timeToLiveSeconds, out var resultSeconds))
                    {
                        return TimeSpan.FromSeconds(resultSeconds);
                    }
                    else
                    {
                        InternalLogger.Error("AzureServiceBusTarget(Name={0}): Failed to parse TimeToLiveSeconds={1}", Name, timeToLiveSeconds);
                    }
                }
                else
                {
                    timeToLiveDays = TimeToLiveDays?.Render(LogEventInfo.CreateNullEvent());
                    if (!string.IsNullOrEmpty(timeToLiveDays))
                    {
                        if (int.TryParse(timeToLiveDays, out var resultDays))
                        {
                            return TimeSpan.FromDays(resultDays);
                        }
                        else
                        {
                            InternalLogger.Error("AzureServiceBusTarget(Name={0}): Failed to parse TimeToLiveDays={1}", Name, timeToLiveDays);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureServiceBusTarget(Name={0}): Failed to parse TimeToLive value. Seconds={1}, Days={2}", Name, timeToLiveSeconds, timeToLiveDays);
            }

            return default(TimeSpan?);
        }

        /// <inheritdoc />
        protected override void CloseTarget()
        {
            var task = Task.Run(async () => await _cloudServiceBus.CloseAsync().ConfigureAwait(false));
            task.Wait(TimeSpan.FromMilliseconds(500));
            base.CloseTarget();
        }

        /// <inheritdoc />
        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();    // Will never get here, because of IList override 
        }

        /// <inheritdoc />
        protected override Task WriteAsyncTask(IList<LogEventInfo> logEvents, CancellationToken cancellationToken)
        {
            if (_getMessagePartitionKeyDelegate == null)
                _getMessagePartitionKeyDelegate = l => RenderLogEvent(SessionId, l) ?? RenderLogEvent(PartitionKey, l) ?? string.Empty;

            if (logEvents.Count == 1)
            {
                var partitionKey = _getMessagePartitionKeyDelegate(logEvents[0]);

                try
                {
                    var messageBatch = CreateMessageBatch(logEvents, partitionKey, out var messageBatchSize);
                    return WriteSingleBatchAsync(messageBatch, cancellationToken);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureServiceBusTarget(Name={0}): Failed writing {1} logevents to EntityPath={2} with PartitionKey={3}", Name, 1, _cloudServiceBus?.EntityPath, partitionKey);
                    throw;
                }
            }

            var partitionBuckets = (SessionId != null || PartitionKey != null) ? SortHelpers.BucketSort(logEvents, _getMessagePartitionKeyDelegate) : new Dictionary<string, IList<LogEventInfo>>() { { string.Empty, logEvents } };
            IList<Task> multipleTasks = partitionBuckets.Count > 1 ? new List<Task>(partitionBuckets.Count) : null;
            foreach (var partitionBucket in partitionBuckets)
            {
                var partitionKey = partitionBucket.Key;
                var bucketCount = partitionBucket.Value.Count;

                try
                {
                    var messageBatch = CreateMessageBatch(partitionBucket.Value, partitionBucket.Key, out var messageBatchSize);

                    Task sendTask = WritePartitionBucketAsync(messageBatch, messageBatchSize, cancellationToken);
                    if (multipleTasks == null)
                        return sendTask;

                    multipleTasks.Add(sendTask);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureServiceBusTarget(Name={0}): Failed writing {1} logevents to EntityPath={2} with PartitionKey={3}", Name, bucketCount, _cloudServiceBus?.EntityPath, partitionKey);
                    if (multipleTasks == null)
                        throw;
                }
            }

            return multipleTasks?.Count > 0 ? Task.WhenAll(multipleTasks) : Task.CompletedTask;
        }

        private Task WritePartitionBucketAsync(IList<ServiceBusMessage> messageBatch, int messageBatchSize, CancellationToken cancellationToken)
        {
            int maxBatchSize = CalculateBatchSize(messageBatch, messageBatchSize);
            if (messageBatch.Count <= maxBatchSize)
            {
                return WriteSingleBatchAsync(messageBatch, cancellationToken);
            }
            else
            {
                var batchCollection = GenerateBatches(messageBatch, maxBatchSize);
                return WriteMultipleBatchesAsync(batchCollection, cancellationToken);
            }
        }

        private async Task WriteMultipleBatchesAsync(IEnumerable<IEnumerable<ServiceBusMessage>> batchCollection, CancellationToken cancellationToken)
        {
            // Must chain the tasks together so they don't run concurrently
            foreach (var batchItem in batchCollection)
            {
                try
                {
                    await WriteSingleBatchAsync(batchItem, cancellationToken).ConfigureAwait(false);
                }
                catch (ServiceBusException ex)
                {
                    if (ex.Reason != ServiceBusFailureReason.MessageSizeExceeded && ex.Reason != ServiceBusFailureReason.QuotaExceeded)
                        throw;

                    InternalLogger.Error(ex, "AzureServiceBusTarget(Name={0}): Skipping failing logevents for EntityPath={1}", Name, ex.EntityPath);
                }
            }
        }

        IEnumerable<IEnumerable<ServiceBusMessage>> GenerateBatches(IList<ServiceBusMessage> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
                yield return source.Skip(i).Take(batchSize);
        }

        private Task WriteSingleBatchAsync(IEnumerable<ServiceBusMessage> messageBatch, CancellationToken cancellationToken)
        {
            return _cloudServiceBus.SendAsync(messageBatch, cancellationToken);
        }

        private int CalculateBatchSize(IList<ServiceBusMessage> messageBatch, int messageBatchSize)
        {
            if (messageBatchSize < MaxBatchSizeBytes)
                return Math.Min(messageBatch.Count, 100);

            if (messageBatch.Count > 10)
            {
                int numberOfBatches = Math.Max(messageBatchSize / MaxBatchSizeBytes, 10);
                int batchSize = Math.Max(messageBatch.Count / numberOfBatches - 1, 1);
                return Math.Min(batchSize, 100);
            }

            return 1;
        }

        private IList<ServiceBusMessage> CreateMessageBatch(IList<LogEventInfo> logEventList, string partitionKey, out int messageBatchSize)
        {
            if (logEventList.Count == 0)
            {
                messageBatchSize = 0;
                return Array.Empty<ServiceBusMessage>();
            }

            if (logEventList.Count == 1)
            {
                var messageData = CreateMessageData(logEventList[0], partitionKey, true);
                if (messageData == null)
                {
                    messageBatchSize = 0;
                    return Array.Empty<ServiceBusMessage>();
                }
                messageBatchSize = EstimateEventDataSize(messageData.Body.ToMemory().Length);
                return new[] { messageData };
            }

            messageBatchSize = 0;
            List<ServiceBusMessage> messageBatch = new List<ServiceBusMessage>(logEventList.Count);
            for (int i = 0; i < logEventList.Count; ++i)
            {
                var messageData = CreateMessageData(logEventList[i], partitionKey, messageBatch.Count == 0 && i == logEventList.Count - 1);
                if (messageData != null)
                {
                    if (messageData.Body.ToMemory().Length > messageBatchSize)
                        messageBatchSize = messageData.Body.ToMemory().Length;
                    messageBatch.Add(messageData);
                }
            }

            messageBatchSize = EstimateEventDataSize(messageBatchSize) * logEventList.Count;
            return messageBatch;
        }

        private static int EstimateEventDataSize(int eventDataSize)
        {
            return (eventDataSize + 128) * 3 + 128;
        }

        private ServiceBusMessage CreateMessageData(LogEventInfo logEvent, string partitionKey, bool allowThrow)
        {
            try
            {
                var messageBody = RenderLogEvent(Layout, logEvent) ?? string.Empty;
                var messageData = new ServiceBusMessage(EncodeToUTF8(messageBody));

                if (!string.IsNullOrEmpty(partitionKey))
                {
                    if (SessionId != null)
                        messageData.SessionId = partitionKey;
                    messageData.PartitionKey = partitionKey;
                }

                var messageContentType = RenderLogEvent(ContentType, logEvent);
                if (!string.IsNullOrEmpty(messageContentType))
                {
                    messageData.ContentType = messageContentType;
                }

                var messageLabel = RenderLogEvent(Subject, logEvent);
                if (!string.IsNullOrEmpty(messageLabel))
                {
                    messageData.Subject = messageLabel;
                }

                var messageId = RenderLogEvent(MessageId, logEvent);
                if (!string.IsNullOrEmpty(messageId))
                {
                    messageData.MessageId = messageId;
                }

                var correlationId = RenderLogEvent(CorrelationId, logEvent);
                if (!string.IsNullOrEmpty(correlationId))
                {
                    messageData.CorrelationId = correlationId;
                }

                var timeToLive = _cloudServiceBus.DefaultTimeToLive;
                if (timeToLive.HasValue)
                {
                    messageData.TimeToLive = timeToLive.Value;
                }

                if (ShouldIncludeProperties(logEvent))
                {
                    var properties = GetAllProperties(logEvent);
                    foreach (var property in properties)
                    {
                        var propertyValue = FlattenObjectValue(property.Value);
                        if (propertyValue != null)
                            messageData.ApplicationProperties.Add(property.Key, propertyValue);
                    }
                }
                else if (ContextProperties.Count > 0)
                {
                    for (int i = 0; i < ContextProperties.Count; ++i)
                    {
                        var property = ContextProperties[i];
                        if (string.IsNullOrEmpty(property.Name))
                            continue;

                        var propertyValue = RenderLogEvent(property.Layout, logEvent);
                        if (!property.IncludeEmptyValue && string.IsNullOrEmpty(propertyValue))
                            continue;

                        messageData.ApplicationProperties.Add(property.Name, propertyValue ?? string.Empty);
                    }
                }

                return messageData;
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureServiceBusTarget(Name={0}): Failed to convert to Message.", Name);

                if (allowThrow || LogManager.ThrowExceptions)
                    throw;

                return null;
            }
        }

        private object FlattenObjectValue(object value)
        {
            try
            {
                if (value is IConvertible convertible)
                {
                    return value;
                }
                else if (value is IFormattable formattable)
                {
                    return formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    return value?.ToString();
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureServiceBusTarget(Name={0}): Failed converting {1} value to Message.", Name, value?.GetType());
                return null;
            }
        }

        private byte[] EncodeToUTF8(string messageBody)
        {
            if (messageBody.Length < _reusableEncodingBuffer.Length)
            {
                lock (_reusableEncodingBuffer)
                {
                    messageBody.CopyTo(0, _reusableEncodingBuffer, 0, messageBody.Length);
                    return Encoding.UTF8.GetBytes(_reusableEncodingBuffer, 0, messageBody.Length);
                }
            }
            else
            {
                return Encoding.UTF8.GetBytes(messageBody);   // Calls string.ToCharArray()
            }
        }

        private sealed class CloudServiceBus : ICloudServiceBus
        {
            private ServiceBusClient _client;
            private ServiceBusSender _sender;

            public TimeSpan? DefaultTimeToLive { get; private set; }

            public string EntityPath { get; private set; }

            public void Connect(string connectionString, string queueOrTopicName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, bool useWebSockets, string webSocketProxyAddress, string endPointAddress, TimeSpan? timeToLive)
            {
                EntityPath = queueOrTopicName;
                DefaultTimeToLive = timeToLive;

                Azure.Messaging.ServiceBus.ServiceBusClientOptions options = default;
                if (useWebSockets || !string.IsNullOrEmpty(webSocketProxyAddress) || !string.IsNullOrEmpty(endPointAddress))
                {
                    options = new Azure.Messaging.ServiceBus.ServiceBusClientOptions();
                    options.TransportType = useWebSockets ? ServiceBusTransportType.AmqpWebSockets : options.TransportType;
                    options.WebProxy = !string.IsNullOrEmpty(webSocketProxyAddress) ? new System.Net.WebProxy(webSocketProxyAddress, true) : options.WebProxy;
                    options.CustomEndpointAddress = !string.IsNullOrEmpty(endPointAddress) ? new Uri(endPointAddress) : options.CustomEndpointAddress;
                }

                if (string.IsNullOrEmpty(serviceUri))
                {
                    _client = new ServiceBusClient(connectionString, options);
                }
                else if (!string.IsNullOrWhiteSpace(sharedAccessSignature))
                {
                    _client = new ServiceBusClient(serviceUri, new Azure.AzureSasCredential(sharedAccessSignature), options);
                }
                else if (!string.IsNullOrWhiteSpace(storageAccountName))
                {
                    _client = new ServiceBusClient(serviceUri, new Azure.AzureNamedKeyCredential(storageAccountName, storageAccountAccessKey), options);
                }
                else
                {
                    var tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(managedIdentityClientId, tenantIdentity, managedIdentityResourceId);
                    _client = new ServiceBusClient(serviceUri, tokenCredentials, options);
                }

                _sender = _client.CreateSender(queueOrTopicName);
            }

            public async Task CloseAsync()
            {
                await (_sender?.CloseAsync() ?? Task.CompletedTask).ConfigureAwait(false);
                await (_client?.DisposeAsync() ?? new ValueTask(Task.CompletedTask));
            }

            public Task SendAsync(IEnumerable<ServiceBusMessage> messages, CancellationToken cancellationToken)
            {
                if (_client == null)
                    throw new InvalidOperationException("ServiceBusClient has not been initialized");

                return _sender.SendMessagesAsync(messages, cancellationToken);
            }
        }
    }
}
