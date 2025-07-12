using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using NLog.Common;
using NLog.Config;
using NLog.Extensions.AzureStorage;
using NLog.Layouts;

namespace NLog.Targets
{
    /// <summary>
    /// Azure Event Hubs NLog Target
    /// </summary>
    [Target("AzureEventHub")]
    public class EventHubTarget : AsyncTaskTarget
    {
        private readonly IEventHubService _eventHubService;
        private SortHelpers.KeySelector<LogEventInfo, string> _getEventHubPartitionKeyDelegate;
        private readonly char[] _reusableEncodingBuffer = new char[32 * 1024];  // Avoid large-object-heap

        /// <summary>
        /// Lookup the ConnectionString for the EventHub
        /// </summary>
        public Layout ConnectionString { get; set; }

        /// <summary>
        /// Override the EntityPath in the ConnectionString
        /// </summary>
        public Layout EventHubName { get; set; }

        /// <summary>
        /// The partitionKey will be hashed to determine the partitionId to send the EventData to
        /// </summary>
        public Layout PartitionKey { get; set; }

        /// <summary>
        /// Gets and sets type of the content for <see cref="EventData.ContentType"/>
        /// </summary>
        /// <remarks>
        ///  The MIME type of the Azure.Messaging.EventHubs.EventData.EventBody content; when
        ///  unknown, it is recommended that this value should not be set. When the body is
        ///  known to be truly opaque binary data, it is recommended that "application/octet-stream"
        ///  be used.
        /// </remarks>
        public Layout ContentType { get; set; }

        /// <summary>
        /// Gets and sets type of the content for <see cref="EventData.CorrelationId"/>
        /// </summary>
        /// <remarks>
        /// An application-defined value that represents the context to use for correlation
        /// across one or more operations. The identifier is a free-form value and may reflect
        /// a unique identity or a shared data element with significance to the application.
        /// 
        /// The Azure.Messaging.EventHubs.EventData.CorrelationId is intended to enable tracing
        /// of data within an application, such as an event's path from producer to consumer.
        /// It has no meaning to the Event Hubs service.
        /// </remarks>
        public Layout CorrelationId { get; set; }

        /// <summary>
        /// Gets and sets type of the content for <see cref="EventData.MessageId"/>
        /// </summary>
        /// <remarks>
        /// An application-defined value that uniquely identifies the event. The identifier
        /// is a free-form value and can reflect a GUID or an identifier derived from the
        /// application context.
        /// 
        /// The Azure.Messaging.EventHubs.EventData.MessageId is intended to allow coordination
        /// between event producers and consumers. It has no meaning to the Event Hubs service,
        /// and does not influence how Event Hubs identifies the event.
        /// </remarks>
        public Layout MessageId { get; set; }

        /// <summary>
        /// Basic Tier = 256 KByte, Standard Tier = 1 MByte
        /// </summary>
        public int MaxBatchSizeBytes { get; set; } = 1024 * 1024;

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
        /// The connection uses the AMQP protocol over web sockets. See also <see cref="EventHubsTransportType.AmqpWebSockets"/>
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
        /// Gets a list of user properties (aka custom properties) to add to the AMQP message
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

        public EventHubTarget()
            :this(new EventHubService())
        {
        }

        internal EventHubTarget(IEventHubService eventHubService)
        {
            TaskDelayMilliseconds = 200;
            BatchSize = 100;
            RetryDelayMilliseconds = 100;

            _eventHubService = eventHubService;
        }

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
            string eventHubName = string.Empty;
            string useWebSockets = string.Empty;
            string webSocketProxyAddress = string.Empty;
            string customEndPointAddress = string.Empty;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                eventHubName = EventHubName?.Render(defaultLogEvent)?.Trim() ?? string.Empty;
                connectionString = ConnectionString?.Render(defaultLogEvent) ?? string.Empty;
                if (string.IsNullOrEmpty(connectionString))
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
                customEndPointAddress = CustomEndpointAddress?.Render(defaultLogEvent) ?? string.Empty;
                webSocketProxyAddress = WebSocketProxyAddress?.Render(defaultLogEvent) ?? string.Empty;

                _eventHubService.Connect(connectionString, eventHubName, serviceUri, tenantIdentity, managedIdentityResourceId, managedIdentityClientId, sharedAccessSignature, storageAccountName, storageAccountAccessKey, bool.TrueString == useWebSockets, webSocketProxyAddress, customEndPointAddress);
                InternalLogger.Debug("AzureEventHubTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(serviceUri))
                    InternalLogger.Error(ex, "AzureEventHubTarget(Name={0}): Failed to create EventHubClient with EventHubName={1} and ServiceUri={2}.", Name, eventHubName, serviceUri);
                else
                    InternalLogger.Error(ex, "AzureEventHubTarget(Name={0}): Failed to create EventHubClient with EventHubName={1} and ConnectionString={2}", Name, eventHubName, connectionString);
                throw;
            }
        }

        protected override void CloseTarget()
        {
            var task = Task.Run(async () => await _eventHubService.CloseAsync().ConfigureAwait(false));
            task.Wait(TimeSpan.FromMilliseconds(500));
            base.CloseTarget();
        }

        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();    // Will never get here, because of IList override 
        }

        protected override Task WriteAsyncTask(IList<LogEventInfo> logEvents, CancellationToken cancellationToken)
        {
            if (_getEventHubPartitionKeyDelegate == null)
                _getEventHubPartitionKeyDelegate = l => RenderLogEvent(PartitionKey, l) ?? string.Empty;

            if (logEvents.Count == 1)
            {
                var partitionKey = _getEventHubPartitionKeyDelegate(logEvents[0]);

                try
                {
                    var eventDataBatch = CreateEventDataBatch(logEvents, partitionKey, out var eventDataSize);
                    return WriteSingleBatchAsync(eventDataBatch, partitionKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureEventHubTarget(Name={0}): Failed writing {1} logevents to EntityPath={2} with PartitionKey={3}", Name, 1, _eventHubService?.EventHubName, partitionKey);
                    throw;
                }
            }

            var partitionBuckets = PartitionKey != null ? SortHelpers.BucketSort(logEvents, _getEventHubPartitionKeyDelegate) : new Dictionary<string, IList<LogEventInfo>>() { { string.Empty, logEvents } };
            IList<Task> multipleTasks = partitionBuckets.Count > 1 ? new List<Task>(partitionBuckets.Count) : null;
            foreach (var partitionBucket in partitionBuckets)
            {
                var partitionKey = partitionBucket.Key;
                var bucketCount = partitionBucket.Value.Count;

                try
                {
                    var eventDataBatch = CreateEventDataBatch(partitionBucket.Value, partitionKey, out var eventDataSize);

                    Task sendTask = WritePartitionBucketAsync(eventDataBatch, eventDataSize, partitionKey, cancellationToken);
                    if (multipleTasks == null)
                        return sendTask;

                    multipleTasks.Add(sendTask);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureEventHubTarget(Name={0}): Failed writing {1} logevents to EntityPath={2} with PartitionKey={3}", Name, bucketCount, _eventHubService?.EventHubName, partitionKey);
                    if (multipleTasks == null)
                        throw;
                }
            }

            return multipleTasks?.Count > 0 ? Task.WhenAll(multipleTasks) : Task.CompletedTask;
        }

        private Task WritePartitionBucketAsync(IList<EventData> eventDataList, int eventDataSize, string partitionKey, CancellationToken cancellationToken)
        {
            int maxBatchSize = CalculateBatchSize(eventDataList, eventDataSize);
            if (eventDataList.Count <= maxBatchSize)
            {
                return WriteSingleBatchAsync(eventDataList, partitionKey, cancellationToken);
            }
            else
            {
                var batchCollection = GenerateBatches(eventDataList, maxBatchSize);
                return WriteMultipleBatchesAsync(batchCollection, partitionKey, cancellationToken);
            }
        }

        private async Task WriteMultipleBatchesAsync(IEnumerable<IEnumerable<EventData>> batchCollection, string partitionKey, CancellationToken cancellationToken)
        {
            // Must chain the tasks together so they don't run concurrently
            foreach (var batchItem in batchCollection)
            {
                try
                {
                    await WriteSingleBatchAsync(batchItem, partitionKey, cancellationToken).ConfigureAwait(false);
                }
                catch (EventHubsException ex)
                {
                    if (ex.Reason != EventHubsException.FailureReason.MessageSizeExceeded && ex.Reason != EventHubsException.FailureReason.QuotaExceeded)
                        throw;

                    InternalLogger.Error(ex, "AzureEventHubTarget(Name={0}): Skipping failing logevents for EntityPath={1} with PartitionKey={2}", Name, ex.EventHubName, partitionKey);
                }
            }
        }

        IEnumerable<IEnumerable<EventData>> GenerateBatches(IList<EventData> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
                yield return source.Skip(i).Take(batchSize);
        }

        private Task WriteSingleBatchAsync(IEnumerable<EventData> eventDataBatch, string partitionKey, CancellationToken cancellationToken)
        {
            return _eventHubService.SendAsync(eventDataBatch, partitionKey, cancellationToken);
        }

        private int CalculateBatchSize(IList<EventData> eventDataBatch, int eventDataSize)
        {
            if (eventDataSize < MaxBatchSizeBytes)
                return Math.Min(eventDataBatch.Count, 100);

            if (eventDataBatch.Count > 10)
            {
                int numberOfBatches = Math.Max(eventDataSize / MaxBatchSizeBytes, 10);
                int batchSize = Math.Max(eventDataBatch.Count / numberOfBatches - 1, 1);
                return Math.Min(batchSize, 100);
            }

            return 1;
        }

        private IList<EventData> CreateEventDataBatch(IList<LogEventInfo> logEventList, string partitionKey, out int eventDataSize)
        {
            if (logEventList.Count == 0)
            {
                eventDataSize = 0;
                return Array.Empty<EventData>();
            }

            if (string.IsNullOrEmpty(partitionKey))
                partitionKey = null;

            if (logEventList.Count == 1)
            {
                var eventData = CreateEventData(partitionKey, logEventList[0], true);
                if (eventData == null)
                {
                    eventDataSize = 0;
                    return Array.Empty<EventData>();
                }
                eventDataSize = EstimateEventDataSize(eventData.Body.Length);
                return new[] { eventData };
            }

            eventDataSize = 0;
            List<EventData> eventDataBatch = new List<EventData>(logEventList.Count);
            for (int i = 0; i < logEventList.Count; ++i)
            {
                var eventData = CreateEventData(partitionKey, logEventList[i], eventDataBatch.Count == 0 && i == logEventList.Count - 1);
                if (eventData != null)
                {
                    if (eventData.Body.Length > eventDataSize)
                        eventDataSize = eventData.Body.Length;
                    eventDataBatch.Add(eventData);
                }
            }

            eventDataSize = EstimateEventDataSize(eventDataSize) * logEventList.Count;
            return eventDataBatch;
        }

        private static int EstimateEventDataSize(int eventDataSize)
        {
            return (eventDataSize + 128) * 3 + 128;
        }

        private EventData CreateEventData(string partitionKey, LogEventInfo logEvent, bool allowThrow)
        {
            try
            {
                var eventDataBody = RenderLogEvent(Layout, logEvent) ?? string.Empty;
                var eventData = EventHubsModelFactory.EventData(new BinaryData(EncodeToUTF8(eventDataBody)), partitionKey: partitionKey, offsetString: null);

                var contentType = RenderLogEvent(ContentType, logEvent) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(contentType))
                {
                    eventData.ContentType = contentType;
                }

                var correlationId = RenderLogEvent(CorrelationId, logEvent) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(correlationId))
                {
                    eventData.CorrelationId = correlationId;
                }

                var messageId = RenderLogEvent(MessageId, logEvent) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(messageId))
                {
                    eventData.MessageId = messageId;
                }

                if (ShouldIncludeProperties(logEvent))
                {
                    var properties = GetAllProperties(logEvent);
                    foreach (var property in properties)
                    {
                        var propertyValue = FlattenObjectValue(property.Value);
                        if (propertyValue != null)
                            eventData.Properties.Add(property.Key, propertyValue);
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

                        eventData.Properties.Add(property.Name, propertyValue ?? string.Empty);
                    }
                }
                return eventData;
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureEventHubTarget(Name={0}): Failed to convert to EventData.", Name);

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
                InternalLogger.Error(ex, "AzureEventHubTarget(Name={0}): Failed converting {1} value to EventData.", Name, value?.GetType());
                return null;
            }
        }

        private byte[] EncodeToUTF8(string eventDataBody)
        {
            if (eventDataBody.Length < _reusableEncodingBuffer.Length)
            {
                lock (_reusableEncodingBuffer)
                {
                    eventDataBody.CopyTo(0, _reusableEncodingBuffer, 0, eventDataBody.Length);
                    return Encoding.UTF8.GetBytes(_reusableEncodingBuffer, 0, eventDataBody.Length);
                }
            }
            else
            {
                return Encoding.UTF8.GetBytes(eventDataBody);   // Calls string.ToCharArray()
            }
        }

        private sealed class EventHubService : IEventHubService
        {
            private Azure.Messaging.EventHubs.Producer.EventHubProducerClient _client;
            private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Azure.Messaging.EventHubs.Producer.SendEventOptions> _partitionKeys = new System.Collections.Concurrent.ConcurrentDictionary<string, Azure.Messaging.EventHubs.Producer.SendEventOptions>();

            public string EventHubName { get; private set; }

            public void Connect(string connectionString, string eventHubName, string serviceUri, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string storageAccountName, string storageAccountAccessKey, bool useWebSockets, string webSocketsProxyAddress, string endPointAddress)
            {
                EventHubName = eventHubName;

                Azure.Messaging.EventHubs.Producer.EventHubProducerClientOptions options = default;
                if (useWebSockets || !string.IsNullOrEmpty(webSocketsProxyAddress) || !string.IsNullOrEmpty(endPointAddress))
                {
                    options = new Azure.Messaging.EventHubs.Producer.EventHubProducerClientOptions();
                    options.ConnectionOptions.TransportType = useWebSockets ? EventHubsTransportType.AmqpWebSockets : options.ConnectionOptions.TransportType;
                    options.ConnectionOptions.Proxy = !string.IsNullOrEmpty(webSocketsProxyAddress) ? new System.Net.WebProxy(webSocketsProxyAddress, true) : options.ConnectionOptions.Proxy;
                    options.ConnectionOptions.CustomEndpointAddress = !string.IsNullOrEmpty(endPointAddress) ? new Uri(endPointAddress) : options.ConnectionOptions.CustomEndpointAddress;
                }

                if (string.IsNullOrWhiteSpace(serviceUri))
                {
                    if (string.IsNullOrWhiteSpace(eventHubName))
                    {
                        _client = new Azure.Messaging.EventHubs.Producer.EventHubProducerClient(connectionString, options);
                    }
                    else
                    {
                        _client = new Azure.Messaging.EventHubs.Producer.EventHubProducerClient(connectionString, eventHubName, options);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(sharedAccessSignature))
                {
                    _client = new Azure.Messaging.EventHubs.Producer.EventHubProducerClient(serviceUri, eventHubName, new Azure.AzureSasCredential(sharedAccessSignature), options);
                }
                else if (!string.IsNullOrWhiteSpace(storageAccountName))
                {
                    _client = new Azure.Messaging.EventHubs.Producer.EventHubProducerClient(serviceUri, eventHubName, new Azure.AzureNamedKeyCredential(storageAccountName, storageAccountAccessKey), options);
                }
                else
                {
                    var tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(managedIdentityClientId, tenantIdentity, managedIdentityResourceId);
                    _client = new Azure.Messaging.EventHubs.Producer.EventHubProducerClient(serviceUri, eventHubName, tokenCredentials, options);
                }
            }

            public Task CloseAsync()
            {
                return _client?.CloseAsync() ?? Task.CompletedTask;
            }

            public Task SendAsync(IEnumerable<EventData> eventDataBatch, string partitionKey, CancellationToken cancellationToken)
            {
                if (_client == null)
                    throw new InvalidOperationException("EventHubClient has not been initialized");

                Azure.Messaging.EventHubs.Producer.SendEventOptions sendEventOptions = null;
                if (!string.IsNullOrEmpty(partitionKey))
                {
                    if (!_partitionKeys.TryGetValue(partitionKey, out sendEventOptions))
                    {
                        sendEventOptions = new Azure.Messaging.EventHubs.Producer.SendEventOptions() { PartitionKey = partitionKey };
                        _partitionKeys.TryAdd(partitionKey, sendEventOptions);
                    }
                }

                if (sendEventOptions != null)
                    return _client.SendAsync(eventDataBatch, sendEventOptions, cancellationToken);
                else
                    return _client.SendAsync(eventDataBatch, cancellationToken);
            }
        }
    }
}
