using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
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
        [RequiredParameter]
        public Layout ConnectionString { get; set; }

        /// <summary>
        /// Override the EntityPath in the ConnectionString
        /// </summary>
        public Layout EventHubName { get; set; }

        /// <summary>
        /// The partitionKey will be hashed to determine the partitionId to send the EventData to
        /// </summary>
        public Layout PartitionKey { get; set; } = "0";

        /// <summary>
        /// Gets and sets type of the content for <see cref="EventData.ContentType"/>
        /// </summary>
        public Layout ContentType { get; set; }

        /// <summary>
        /// Basic Tier = 256 KByte, Standard Tier = 1 MByte
        /// </summary>
        public int MaxBatchSizeBytes { get; set; } = 1024 * 1024;

        /// <summary>
        /// Gets a list of user properties (aka custom properties) to add to the message
        /// <para>
        [ArrayParameter(typeof(TargetPropertyWithContext), "userproperty")]
        public IList<TargetPropertyWithContext> UserProperties { get => ContextProperties; }

        public EventHubTarget()
            :this(new EventHubService())
        {
        }

        internal EventHubTarget(IEventHubService eventHubService)
        {
            TaskDelayMilliseconds = 200;
            BatchSize = 100;
            _eventHubService = eventHubService;
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            var entityPath = EventHubName?.Render(LogEventInfo.CreateNullEvent())?.Trim() ?? string.Empty;
            var connectionString = ConnectionString?.Render(LogEventInfo.CreateNullEvent()) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("ConnectionString is required");
            _eventHubService.Connect(connectionString, entityPath);
        }

        protected override void CloseTarget()
        {
            _eventHubService.Close();
            base.CloseTarget();
        }

        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();    // Will never get here, because of IList override 
        }

        protected override Task WriteAsyncTask(IList<LogEventInfo> logEvents, CancellationToken cancellationToken)
        {
            if (_getEventHubPartitionKeyDelegate == null)
                _getEventHubPartitionKeyDelegate = l => RenderLogEvent(PartitionKey, l);

            if (logEvents.Count == 1)
            {
                var eventDataBatch = CreateEventDataBatch(logEvents, out var eventDataSize);
                return WriteSingleBatchAsync(eventDataBatch, _getEventHubPartitionKeyDelegate(logEvents[0]));
            }

            var partitionBuckets = SortHelpers.BucketSort(logEvents, _getEventHubPartitionKeyDelegate);
            IList<Task> multipleTasks = partitionBuckets.Count > 1 ? new List<Task>(partitionBuckets.Count) : null;
            foreach (var partitionBucket in partitionBuckets)
            {
                try
                {
                    var eventDataBatch = CreateEventDataBatch(partitionBucket.Value, out var eventDataSize);

                    Task sendTask = WritePartitionBucketAsync(eventDataBatch, partitionBucket.Key, eventDataSize);
                    if (multipleTasks == null)
                        return sendTask;

                    multipleTasks.Add(sendTask);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureEventHub(Name={0}): Failed to create EventData batch.", Name);
                    if (multipleTasks == null)
                        throw;
                }
            }

            return multipleTasks?.Count > 0 ? Task.WhenAll(multipleTasks) : Task.CompletedTask;
        }

        private Task WritePartitionBucketAsync(IList<EventData> eventDataList, string partitionKey, int eventDataSize)
        {
            int batchSize = CalculateBatchSize(eventDataList, eventDataSize);
            if (eventDataList.Count <= batchSize)
            {
                return WriteSingleBatchAsync(eventDataList, partitionKey);
            }
            else
            {
                var batchCollection = GenerateBatches(eventDataList, batchSize);
                return WriteMultipleBatchesAsync(batchCollection, partitionKey);
            }
        }

        private async Task WriteMultipleBatchesAsync(IEnumerable<List<EventData>> batchCollection, string partitionKey)
        {
            // Must chain the tasks together so they don't run concurrently
            foreach (var batchItem in batchCollection)
            {
                await WriteSingleBatchAsync(batchItem, partitionKey).ConfigureAwait(false);
            }
        }

        IEnumerable<List<EventData>> GenerateBatches(IList<EventData> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
                yield return new List<EventData>(source.Skip(i).Take(batchSize));
        }

        private Task WriteSingleBatchAsync(IList<EventData> eventDataBatch, string partitionKey)
        {
            return _eventHubService.SendAsync(eventDataBatch, partitionKey);
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

        private IList<EventData> CreateEventDataBatch(IList<LogEventInfo> logEventList, out int eventDataSize)
        {
            if (logEventList.Count == 0)
            {
                eventDataSize = 0;
                return Array.Empty<EventData>();
            }

            if (logEventList.Count == 1)
            {
                var eventData = CreateEventData(logEventList[0], true);
                if (eventData == null)
                {
                    eventDataSize = 0;
                    return Array.Empty<EventData>();
                }
                eventDataSize = EstimateEventDataSize(eventData.Body.Count);
                return new[] { eventData };
            }

            eventDataSize = 0;
            List<EventData> eventDataBatch = new List<EventData>(logEventList.Count);
            for (int i = 0; i < logEventList.Count; ++i)
            {
                var eventData = CreateEventData(logEventList[i], eventDataBatch.Count == 0 && i == logEventList.Count - 1);
                if (eventData != null)
                {
                    if (eventData.Body.Count > eventDataSize)
                        eventDataSize = eventData.Body.Count;
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

        private EventData CreateEventData(LogEventInfo logEvent, bool allowThrow)
        {
            try
            {
                var eventDataBody = RenderLogEvent(Layout, logEvent) ?? string.Empty;
                var eventData = new EventData(EncodeToUTF8(eventDataBody));

                var eventDataContentType = RenderLogEvent(ContentType, logEvent);
                if (!string.IsNullOrEmpty(eventDataContentType))
                    eventData.ContentType = eventDataContentType;

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
                InternalLogger.Error(ex, "AzureEventHub(Name={0}): Failed to convert to EventData.", Name);

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
                InternalLogger.Error(ex, "AzureEventHub(Name={0}): Failed converting {1} value to EventData.", Name, value?.GetType());
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

        private class EventHubService : IEventHubService
        {
            private EventHubClient _client;

            public void Connect(string connectionString, string entityPath)
            {
                var connectionstringBuilder = new EventHubsConnectionStringBuilder(connectionString);
                if (!string.IsNullOrEmpty(entityPath))
                {
                    connectionstringBuilder.EntityPath = entityPath;
                }
                _client = EventHubClient.CreateFromConnectionString(connectionstringBuilder.ToString());
            }

            public void Close()
            {
                _client?.Close();
            }

            public Task SendAsync(IList<EventData> eventDataList, string partitionKey)
            {
                if (_client == null)
                    throw new InvalidOperationException("EventHubClient has not been initialized");

                return _client.SendAsync(eventDataList, partitionKey);
            }
        }
    }
}
