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

namespace NLog.Targets
{
    /// <summary>
    /// Azure Event Grid NLog Target
    /// </summary>
    [Target("AzureEventGrid")]
    public class EventGridTarget : AsyncTaskTarget
    {
        private readonly IEventGridService _eventGridService;
        private readonly char[] _reusableEncodingBuffer = new char[32 * 1024];  // Avoid large-object-heap

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
        /// AccessKey for <see cref="Azure.AzureKeyCredential"/> authentication. Requires <see cref="ServiceUri"/>.
        /// </summary>
        public Layout AccessKey { get; set; }

        /// <summary>
        /// Access signature for <see cref="Azure.AzureSasCredential"/> authentication. Requires <see cref="ServiceUri"/>.
        /// </summary>
        public Layout SharedAccessSignature { get; set; }

        /// <summary>
        /// Gets a list of message properties aka. custom CloudEvent Extension Attributes
        /// </summary>
        [ArrayParameter(typeof(TargetPropertyWithContext), "messageproperty")]
        public IList<TargetPropertyWithContext> MessageProperties { get => ContextProperties; }

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

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                topic = Topic?.Render(defaultLogEvent);
                tenantIdentity = TenantIdentity?.Render(defaultLogEvent);
                managedIdentityResourceId = ManagedIdentityResourceId?.Render(defaultLogEvent);
                managedIdentityClientId = ManagedIdentityClientId?.Render(defaultLogEvent);
                sharedAccessSignature = SharedAccessSignature?.Render(defaultLogEvent);
                accessKey = AccessKey?.Render(defaultLogEvent);

                _eventGridService.Connect(topic, tenantIdentity, managedIdentityResourceId, managedIdentityClientId, sharedAccessSignature, accessKey);
                InternalLogger.Debug("AzureEventGridTarget(Name={0}): Initialized", Name);
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureEventGridTarget(Name={0}): Failed to create EventGridPublisherClient with Topic={1}.", Name, topic);
                throw;
            }
        }

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

        private sealed class EventGridService : IEventGridService
        {
            EventGridPublisherClient _client;

            public string Topic { get; private set; }

            public void Connect(string topic, string tenantIdentity, string managedIdentityResourceId, string managedIdentityClientId, string sharedAccessSignature, string accessKey)
            {
                Topic = topic;

                if (!string.IsNullOrWhiteSpace(sharedAccessSignature))
                {
                    _client = new EventGridPublisherClient(new Uri(topic), new AzureSasCredential(sharedAccessSignature));
                }
                else if (!string.IsNullOrWhiteSpace(accessKey))
                {
                    _client = new EventGridPublisherClient(new Uri(topic), new AzureKeyCredential(accessKey));
                }
                else
                {
                    var tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(managedIdentityClientId, tenantIdentity, managedIdentityResourceId);
                    _client = new EventGridPublisherClient(new Uri(topic), tokenCredentials);
                }
            }

            public Task SendEventAsync(EventGridEvent gridEvent, CancellationToken cancellationToken)
            {
                return _client.SendEventAsync(gridEvent, cancellationToken);
            }

            public Task SendEventAsync(CloudEvent cloudEvent, CancellationToken cancellationToken)
            {
                return _client.SendEventAsync(cloudEvent, cancellationToken);
            }
        }

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
