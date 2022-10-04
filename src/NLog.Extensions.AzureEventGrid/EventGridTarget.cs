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
        public Layout ContentType { get; set; }

        /// <summary>
        /// The schema version of the data object.
        /// </summary>
        public Layout DataSchema { get; set; }

        /// <summary>
        /// Input for AzureKeyCredential for EventGridPublisherClient constructor
        /// </summary>
        public Layout AccessKey { get; set; }

        /// <summary>
        /// Alternative to AccessKey. Input for <see cref="Azure.Identity.DefaultAzureCredential"/>
        /// </summary>
        public Layout TenantIdentity { get; set; }

        /// <summary>
        /// Alternative to AccessKey. Input for <see cref="Azure.Identity.DefaultAzureCredential"/>
        /// </summary>
        public Layout ResourceIdentity { get; set; }

        /// <summary>
        /// Alternative to AccessKey. Input for <see cref="Azure.Identity.DefaultAzureCredential"/>
        /// </summary>
        public Layout ClientIdentity { get; set; }

        /// <summary>
        /// Gets a list of message properties aka. custom CloudEvent Extension Attributes
        /// </summary>
        [ArrayParameter(typeof(TargetPropertyWithContext), "messageproperty")]
        public IList<TargetPropertyWithContext> MessageProperties { get => ContextProperties; }

        public EventGridTarget()
            : this(new EventGridService())
        {
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
            string resourceIdentity = string.Empty;
            string clientIdentity = string.Empty;
            string accessKey = string.Empty;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                topic = Topic?.Render(defaultLogEvent);
                tenantIdentity = TenantIdentity?.Render(defaultLogEvent);
                resourceIdentity = ResourceIdentity?.Render(defaultLogEvent);
                clientIdentity = ClientIdentity?.Render(defaultLogEvent);
                accessKey = AccessKey?.Render(defaultLogEvent);

                _eventGridService.Connect(topic, tenantIdentity, resourceIdentity, clientIdentity, accessKey);
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

            public void Connect(string topic, string tenantIdentity, string resourceIdentifier, string clientIdentity, string accessKey)
            {
                Topic = topic;

                if (!string.IsNullOrWhiteSpace(accessKey))
                {
                    _client = new EventGridPublisherClient(new Uri(topic), new AzureKeyCredential(accessKey));
                }
                else
                {
                    var tokenCredentials = AzureCredentialHelpers.CreateTokenCredentials(clientIdentity, tenantIdentity, resourceIdentifier);
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
            var cloudEvent =  new CloudEvent(eventSource, eventType, new BinaryData(EncodeToUTF8(eventDataBody)), eventContentType, CloudEventDataFormat.Binary);
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
