using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Azure;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Extensions.AzureStorage;
using NLog.Extensions.AzureBlobStorage;

namespace NLog.Targets
{
    /// <summary>
    /// Azure Event Grid NLog Target
    /// </summary>
    [Target("AzureEventGrid")]
    public sealed class EventGridTarget : AsyncTaskTarget
    {
        private readonly IEventGridService _eventGridService;
        private readonly char[] _reusableEncodingBuffer = new char[32 * 1024];  // Avoid large-object-heap

        private const int DefaultMaxBatchSizeBytes = 1000 * 1000;   // ~1MB EventGrid request limit, with headroom under the hard cap
        private const int MaxEventsPerRequest = 1000;               // EventGrid namespace cap (custom topics allow 5000); 1000 is safe everywhere
        private const int EventEnvelopeOverheadBytes = 512;         // id (GUID) + timestamp + JSON field names/punctuation per event

        /// <summary>
        /// The topic endpoint. For example, "https://TOPIC-NAME.REGION-NAME-1.eventgrid.azure.net/api/events"
        /// </summary>
        public Layout Topic { get; set; }

        /// <summary>
        /// A resource path relative to the topic path.
        /// </summary>
        public Layout GridEventSubject { get; set; }

        /// <summary>
        /// Identifies the context in which an event happened. The combination of id and source must be unique for each distinct event.
        /// </summary>
        public Layout CloudEventSource { get; set; }

        /// <summary>
        /// The type of the event that occurred. For example, "Contoso.Items.ItemReceived".
        /// </summary>
        public Layout EventType { get; set; }

        /// <summary>
        /// Content type of the payload. A content type different from "application/json" should be specified if payload is not JSON.
        /// </summary>
        public Layout ContentType
        {
            get => _contentType;
            set
            {
                _contentType = value;
                if (!_dataFormatJson.HasValue && value?.ToString()?.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _dataFormatJson = true;
                }
            }
        }
        private Layout _contentType;

        /// <summary>
        /// The serialize format of the data object. Json / Binary
        /// </summary>
        public string DataFormat
        {
            get => _dataFormatJson == true ? "Json" : "Binary";
            set => _dataFormatJson = value?.IndexOf("Json", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private bool? _dataFormatJson;

        /// <summary>
        /// The schema version of the data object.
        /// </summary>
        public Layout DataSchema { get; set; }

        /// <summary>
        /// Maximum estimated size (in bytes) of a single batched publish request. When a flush (bounded by
        /// <see cref="AsyncTaskTarget.BatchSize"/>) would exceed this, it is split across multiple requests so
        /// each stays under Azure Event Grid's ~1 MB per-request limit. Default 1,000,000.
        /// </summary>
        public int MaxBatchSizeBytes { get; set; } = DefaultMaxBatchSizeBytes;

        /// <summary>
        /// TenantId for <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Used with DefaultAzureCredential authentication when connecting to the Event Grid topic endpoint.
        /// </summary>
        public Layout TenantIdentity { get; set; }

        /// <summary>
        /// Obsolete instead use <see cref="ManagedIdentityResourceId"/>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Instead use ManagedIdentityResourceId")]
        public Layout ResourceIdentity { get => ManagedIdentityResourceId; set => ManagedIdentityResourceId = value; }

        /// <summary>
        /// ResourceId for <see cref="Azure.Identity.DefaultAzureCredentialOptions.ManagedIdentityResourceId"/> on <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Used with managed identity authentication when connecting to the Event Grid topic endpoint.
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
        /// ManagedIdentityClientId for <see cref="Azure.Identity.DefaultAzureCredentialOptions"/>. Used with managed identity authentication when connecting to the Event Grid topic endpoint.
        /// </summary>
        /// <remarks>
        /// If this value is configured, then <see cref="ManagedIdentityResourceId"/> should not be configured.
        /// </remarks>
        public Layout ManagedIdentityClientId { get; set; }

        /// <summary>
        /// AccessKey for <see cref="Azure.AzureKeyCredential"/> authentication. Used with key-based authentication when connecting to the Event Grid topic endpoint.
        /// </summary>
        public Layout AccessKey { get; set; }

        /// <summary>
        /// Access signature for <see cref="Azure.AzureSasCredential"/> authentication. Used with SAS token authentication when connecting to the Event Grid topic endpoint.
        /// </summary>
        public Layout SharedAccessSignature { get; set; }

        /// <summary>
        /// clientId for <see cref="Azure.Identity.ClientSecretCredential"/> OAuth2 authentication. Requires <see cref="TenantIdentity"/> and <see cref="ClientAuthSecret"/>.
        /// </summary>
        public Layout ClientAuthId { get; set; }

        /// <summary>
        /// clientSecret for <see cref="Azure.Identity.ClientSecretCredential"/> OAuth2 authentication. Requires <see cref="TenantIdentity"/> and <see cref="ClientAuthId"/>.
        /// </summary>
        public Layout ClientAuthSecret { get; set; }

        /// <summary>
        /// Bypasses any system proxy and proxy in <see cref="ProxyAddress"/> when set to <see langword="true"/>.
        /// Overrides <see cref="ProxyAddress"/>.
        /// </summary>
        public bool NoProxy { get; set; }

        /// <summary>
        /// Address of the proxy server to use (e.g. http://proxyserver:8080).
        /// </summary>
        public Layout ProxyAddress { get; set; }

        /// <summary>
        /// Login to use for the proxy server. Requires <see cref="ProxyPassword"/>.
        /// </summary>
        public Layout ProxyLogin { get; set; }

        /// <summary>
        /// Password to use for the proxy server. Requires <see cref="ProxyLogin"/>.
        /// </summary>
        public Layout ProxyPassword { get; set; }

        /// <summary>
        /// Uses the default credentials (<see cref="System.Net.CredentialCache.DefaultCredentials"/>) for the proxy server.
        /// </summary>
        /// <remarks>Take precedence over <see cref = "ProxyLogin" /> and <see cref="ProxyPassword"/> when set to <see langword="true"/>.</remarks>
        public bool UseDefaultCredentialsForProxy { get; set; }

        /// <summary>
        /// Gets a list of message properties aka. custom CloudEvent Extension Attributes
        /// </summary>
        [ArrayParameter(typeof(TargetPropertyWithContext), "messageproperty")]
        public IList<TargetPropertyWithContext> MessageProperties { get => ContextProperties; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EventGridTarget"/> class.
        /// </summary>
        public EventGridTarget()
            : this(new EventGridService())
        {
            RetryDelayMilliseconds = 100;
        }

        internal EventGridTarget(IEventGridService eventGridService)
        {
            _eventGridService = eventGridService;
        }

        /// <inheritdoc />
        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            for (int i = 0; i < MessageProperties.Count; ++i)
            {
                var messageProperty = MessageProperties[i];
                if (!IsValidExtensionAttribue(messageProperty.Name))
                {
                    var messagePropertyName = (messageProperty.Name ?? string.Empty).Trim().ToLowerInvariant();
                    if (!IsValidExtensionAttribue(messagePropertyName))
                    {
                        messagePropertyName = string.Join(string.Empty, System.Linq.Enumerable.Where(messagePropertyName, chr => !IsInvalidExtensionAttribueChar(chr)));
                    }

                    InternalLogger.Debug("AzureEventGridTarget(Name={0}): Fixing MessageProperty-Name from '{1}' to '{2}'", Name, messageProperty.Name, messagePropertyName);
                    messageProperty.Name = messagePropertyName;
                }
            }

            string topic = string.Empty;
            string tenantIdentity = string.Empty;
            string managedIdentityResourceId = string.Empty;
            string managedIdentityClientId = string.Empty;
            string sharedAccessSignature = string.Empty;
            string accessKey = string.Empty;
            string clientAuthId = string.Empty;
            string clientAuthSecret = string.Empty;

            ProxySettings proxySettings = null;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                topic = Topic?.Render(defaultLogEvent);
                tenantIdentity = TenantIdentity?.Render(defaultLogEvent);
                managedIdentityResourceId = ManagedIdentityResourceId?.Render(defaultLogEvent);
                managedIdentityClientId = ManagedIdentityClientId?.Render(defaultLogEvent);
                sharedAccessSignature = SharedAccessSignature?.Render(defaultLogEvent);
                accessKey = AccessKey?.Render(defaultLogEvent);
                clientAuthId = ClientAuthId?.Render(defaultLogEvent);
                clientAuthSecret = ClientAuthSecret?.Render(defaultLogEvent);

                proxySettings = new ProxySettings
                {
                    NoProxy = NoProxy,
                    UseDefaultCredentials = UseDefaultCredentialsForProxy,
                    Address = ProxyAddress?.Render(defaultLogEvent),
                    Login = ProxyLogin?.Render(defaultLogEvent),
                    Password = ProxyPassword?.Render(defaultLogEvent)
                };

                _eventGridService.Connect(topic, tenantIdentity, managedIdentityResourceId, managedIdentityClientId, sharedAccessSignature, accessKey, clientAuthId, clientAuthSecret, proxySettings);
                InternalLogger.Debug("AzureEventGridTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureEventGridTarget(Name={0}): Failed to create EventGridPublisherClient with Topic={1}.", Name, topic);
                throw;
            }
        }

        /// <summary>
        /// Closes the target, disposing the proxy <see cref="System.Net.Http.HttpClient"/>/transport (if any
        /// was created) so it does not leak across NLog reconfigurations.
        /// </summary>
        protected override void CloseTarget()
        {
            (_eventGridService as IDisposable)?.Dispose();
            base.CloseTarget();
        }

        /// <summary>
        /// Override this to provide async task for writing a single logevent.
        /// </summary>
        /// <param name="logEvent">The log event.</param>
        /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            try
            {
                if (CloudEventSource is null)
                {
                    var gridEvent = CreateGridEvent(logEvent);
                    return _eventGridService.SendEventAsync(gridEvent, cancellationToken);
                }
                else
                {
                    var cloudEvent = CreateCloudEvent(logEvent);
                    return _eventGridService.SendEventAsync(cloudEvent, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureEventGridTarget(Name={0}): Failed sending logevent to Topic={1}", Name, _eventGridService?.Topic);
                throw;
            }
        }

        /// <summary>
        /// Override this to provide async task for writing a batch of logevents in a single request.
        /// </summary>
        /// <param name="logEvents">The log events.</param>
        /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
        protected override Task WriteAsyncTask(IList<LogEventInfo> logEvents, CancellationToken cancellationToken)
        {
            if (logEvents.Count == 1)
                return WriteAsyncTask(logEvents[0], cancellationToken);  // single-event path, NLog uses it for count==1

            try
            {
                if (CloudEventSource is null)
                {
                    var gridEvents = new List<EventGridEvent>(logEvents.Count);
                    for (int i = 0; i < logEvents.Count; ++i)
                        gridEvents.Add(CreateGridEvent(logEvents[i]));
                    return SendInBatches(gridEvents, EstimateGridEventBytes, (batch, ct) => _eventGridService.SendEventsAsync(batch, ct), cancellationToken);
                }
                else
                {
                    var cloudEvents = new List<CloudEvent>(logEvents.Count);
                    for (int i = 0; i < logEvents.Count; ++i)
                        cloudEvents.Add(CreateCloudEvent(logEvents[i]));
                    return SendInBatches(cloudEvents, EstimateCloudEventBytes, (batch, ct) => _eventGridService.SendEventsAsync(batch, ct), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureEventGridTarget(Name={0}): Failed sending {1} logevents to Topic={2}", Name, logEvents.Count, _eventGridService?.Topic);
                throw;
            }
        }

        internal sealed class EventGridService : IEventGridService, IDisposable
        {
            EventGridPublisherClient _client;
            private IDisposable _proxyTransport;

            public string Topic { get; private set; }

            public void Connect(string topic, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string accessKey, string clientAuthId, string clientAuthSecret, ProxySettings proxySettings = null)
            {
                Topic = topic;
                EventGridPublisherClientOptions options = ConfigureClientOptions(proxySettings);
                if (!string.IsNullOrWhiteSpace(sharedAccessSignature))
                {
                    _client = new EventGridPublisherClient(new Uri(topic), new AzureSasCredential(sharedAccessSignature), options);
                }
                else if (!string.IsNullOrWhiteSpace(accessKey))
                {
                    _client = new EventGridPublisherClient(new Uri(topic), new AzureKeyCredential(accessKey), options);
                }
                else if (!string.IsNullOrEmpty(clientAuthId) && !string.IsNullOrEmpty(clientAuthSecret) && !string.IsNullOrEmpty(tenantIdentity))
                {
                    var tokenCredentials = new Azure.Identity.ClientSecretCredential(tenantIdentity, clientAuthId, clientAuthSecret);
                    _client = new EventGridPublisherClient(new Uri(topic), tokenCredentials, options);
                }
                else
                {
                    var tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(managedIdentityClientId, tenantIdentity, managedIdentityResourceId);
                    _client = new EventGridPublisherClient(new Uri(topic), tokenCredentials, options);
                }
            }

            private EventGridPublisherClientOptions ConfigureClientOptions(ProxySettings proxySettings)
            {
                var transport = proxySettings?.CreateHttpClientTransport();
                _proxyTransport?.Dispose();   // dispose any transport from a previous Connect (client replaced)
                _proxyTransport = transport;
                if (transport != null)
                {
                    return new EventGridPublisherClientOptions
                    {
                        Transport = transport
                    };
                }
                return null;
            }

            public void Dispose()
            {
                _proxyTransport?.Dispose();   // dispose the HttpClient/handler owned by the proxy transport
                _proxyTransport = null;
            }

            public Task SendEventAsync(EventGridEvent gridEvent, CancellationToken cancellationToken)
            {
                return _client.SendEventAsync(gridEvent, cancellationToken);
            }

            public Task SendEventAsync(CloudEvent cloudEvent, CancellationToken cancellationToken)
            {
                return _client.SendEventAsync(cloudEvent, cancellationToken);
            }

            public Task SendEventsAsync(IEnumerable<EventGridEvent> gridEvents, CancellationToken cancellationToken)
            {
                return _client.SendEventsAsync(gridEvents, cancellationToken);
            }

            public Task SendEventsAsync(IEnumerable<CloudEvent> cloudEvents, CancellationToken cancellationToken)
            {
                return _client.SendEventsAsync(cloudEvents, cancellationToken);
            }
        }

#if NET6_0_OR_GREATER
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming - Allow converting option-values from config", "IL2026")]
#endif
        private CloudEvent CreateCloudEvent(LogEventInfo logEvent)
        {
            var eventDataBody = RenderLogEvent(Layout, logEvent) ?? string.Empty;
            var eventSource = RenderLogEvent(CloudEventSource, logEvent) ?? string.Empty;
            var eventType = RenderLogEvent(EventType, logEvent) ?? string.Empty;
            var eventDataSchema = RenderLogEvent(DataSchema, logEvent) ?? string.Empty;
            var eventContentType = RenderLogEvent(ContentType, logEvent) ?? string.Empty;
            var cloudEventFormat = _dataFormatJson == true ? CloudEventDataFormat.Json : CloudEventDataFormat.Binary;
            var cloudEvent =  new CloudEvent(eventSource, eventType, new BinaryData(EncodeToUTF8(eventDataBody)), eventContentType, cloudEventFormat);
            cloudEvent.Time = logEvent.TimeStamp.ToUniversalTime();

            if (!string.IsNullOrEmpty(eventDataSchema))
            {
                cloudEvent.DataSchema = eventDataSchema;
            }

            for (int i = 0; i < MessageProperties.Count; ++i)
            {
                var messageProperty = MessageProperties[i];
                if (string.IsNullOrEmpty(messageProperty.Name))
                    continue;

                var propertyValue = RenderLogEvent(messageProperty.Layout, logEvent);
                if (string.IsNullOrEmpty(propertyValue) && !messageProperty.IncludeEmptyValue)
                    continue;

                cloudEvent.ExtensionAttributes.Add(messageProperty.Name, propertyValue);
            }

            return cloudEvent;
        }

        private EventGridEvent CreateGridEvent(LogEventInfo logEvent)
        {
            var eventDataBody = RenderLogEvent(Layout, logEvent) ?? string.Empty;
            var eventSubject = RenderLogEvent(GridEventSubject, logEvent) ?? string.Empty;
            var eventType = RenderLogEvent(EventType, logEvent) ?? string.Empty;
            var eventDataSchema = RenderLogEvent(DataSchema, logEvent) ?? string.Empty;
            var gridEvent = new EventGridEvent(eventSubject, eventType, eventDataSchema, new BinaryData(EncodeToUTF8(eventDataBody)));
            gridEvent.EventTime = logEvent.TimeStamp.ToUniversalTime();
            return gridEvent;
        }

        // ponytail: EventGrid's SDK has no TryAdd batch-builder (unlike the ServiceBus/EventHub SDKs), so the
        // request size is a conservative estimate that over-counts (per-event envelope allowance + base64
        // inflation) to stay safely under the limit and never under-count into a 413. The cap default leaves
        // headroom under EventGrid's ~1MB hard limit. Swap in exact serialization accounting only if the
        // estimate ever proves too coarse in practice.
        private Task SendInBatches<T>(IList<T> events, Func<T, long> estimateBytes, Func<IList<T>, CancellationToken, Task> sendBatch, CancellationToken cancellationToken)
        {
            long sizeLimit = MaxBatchSizeBytes > 0 ? MaxBatchSizeBytes : DefaultMaxBatchSizeBytes;

            List<Task> sendTasks = null;
            var batch = new List<T>();
            long batchBytes = 0;

            for (int i = 0; i < events.Count; ++i)
            {
                long eventBytes = estimateBytes(events[i]);
                if (batch.Count > 0 && (batch.Count >= MaxEventsPerRequest || batchBytes + eventBytes > sizeLimit))
                {
                    if (sendTasks == null)
                        sendTasks = new List<Task>();
                    sendTasks.Add(sendBatch(batch, cancellationToken));
                    batch = new List<T>();
                    batchBytes = 0;
                }

                batch.Add(events[i]);   // a single event over the limit is still sent alone - the service is the authority, not our estimate
                batchBytes += eventBytes;
            }

            if (sendTasks == null)
                return sendBatch(batch, cancellationToken);   // common case: the whole flush fits one request

            sendTasks.Add(sendBatch(batch, cancellationToken));
            return Task.WhenAll(sendTasks);
        }

        private static long EstimateGridEventBytes(EventGridEvent gridEvent)
        {
            long bytes = EventEnvelopeOverheadBytes;
            if (gridEvent.Data != null)
                bytes += EstimateDataBytes(gridEvent.Data.ToMemory().Length, false);   // EventGridEvent has no binary data format
            bytes += (gridEvent.Subject?.Length ?? 0) + (gridEvent.EventType?.Length ?? 0) + (gridEvent.DataVersion?.Length ?? 0);
            return bytes;
        }

        private long EstimateCloudEventBytes(CloudEvent cloudEvent)
        {
            long bytes = EventEnvelopeOverheadBytes;
            if (cloudEvent.Data != null)
                bytes += EstimateDataBytes(cloudEvent.Data.ToMemory().Length, _dataFormatJson != true);   // binary data is base64-encoded in the envelope
            bytes += (cloudEvent.Source?.Length ?? 0) + (cloudEvent.Type?.Length ?? 0) + (cloudEvent.DataSchema?.Length ?? 0);
            foreach (var attribute in cloudEvent.ExtensionAttributes)
                bytes += attribute.Key.Length + (attribute.Value?.ToString()?.Length ?? 0) + 8;   // "key":"value",
            return bytes;
        }

        private static long EstimateDataBytes(int rawDataLength, bool binary)
        {
            // Conservative: binary is base64-encoded (~4/3), JSON/text gets a margin for string escaping.
            return binary ? (rawDataLength * 4L / 3 + 4) : (rawDataLength + rawDataLength / 8 + 16);
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

        private static bool IsValidExtensionAttribue(string propertyValue)
        {
            for (int i = 0; i < propertyValue.Length; ++i)
            {
                char chr = propertyValue[i];
                if (IsInvalidExtensionAttribueChar(chr))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsInvalidExtensionAttribueChar(char chr)
        {
            return (chr < 'a' || chr > 'z') && (chr < '0' || chr > '9');
        }
    }
}
