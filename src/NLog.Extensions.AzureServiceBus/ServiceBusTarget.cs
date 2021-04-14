using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
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
        [RequiredParameter]
        public Layout ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the EntityPath for service bus <see cref="QueueClient"/>
        /// </summary>
        /// <remarks>
        /// Queues offer First In, First Out (FIFO) message delivery to one or more competing consumers
        /// </remarks>
        public Layout QueueName { get; set; }

        /// <summary>
        /// Gets or sets the EntityPath for service bus <see cref="TopicClient"/>
        /// </summary>
        /// <remarks>
        /// In contrast to queues, in which each message is processed by a single consumer, topics provides a one-to-many form of communication
        /// </remarks>
        public Layout TopiceName { get; set; }

        /// <summary>
        /// Gets and sets type of the content for <see cref="Message.ContentType"/>. Ex. application/json
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
        /// Gets and sets the label for <see cref="Message.Label"/>. Similar to an email subject line.
        /// </summary>
        public Layout Label { get; set; }

        /// <summary>
        /// Gets and sets the <see cref="Message.MessageId"/>.
        /// </summary>
        public Layout MessageId { get; set; }

        /// <summary>
        /// Gets and sets the correlationid for <see cref="Message.CorrelationId"/>. For the purposes of correlation.
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
        /// Gets a list of user properties (aka custom properties) to add to the message
        /// <para>
        [ArrayParameter(typeof(TargetPropertyWithContext), "userproperty")]
        public IList<TargetPropertyWithContext> UserProperties { get => ContextProperties; }

        public ServiceBusTarget()
            :this(new CloudServiceBus())
        {
        }

        internal ServiceBusTarget(ICloudServiceBus cloudServiceBus)
        {
            TaskDelayMilliseconds = 200;
            BatchSize = 100;

            _cloudServiceBus = cloudServiceBus;
        }

        /// <inheritdoc />
        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            string connectionString = string.Empty;

            var defaultLogEvent = LogEventInfo.CreateNullEvent();

            try
            {
                connectionString = ConnectionString?.Render(defaultLogEvent);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new ArgumentException("ConnectionString is required");
                }

                var queuePath = QueueName?.Render(defaultLogEvent)?.Trim();
                var topicPath = TopiceName?.Render(defaultLogEvent)?.Trim();
                if (string.IsNullOrWhiteSpace(queuePath) && string.IsNullOrWhiteSpace(topicPath))
                {
                    throw new ArgumentException("QueuePath or TopicPath must be specified");
                }

                var timeToLive = RenderDefaultTimeToLive();
                if (timeToLive <= TimeSpan.Zero)
                {
                    timeToLive = default(TimeSpan?);
                }

                _cloudServiceBus.Connect(connectionString, queuePath, topicPath, timeToLive);
                InternalLogger.Trace("AzureServiceBus - Initialized");
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureServiceBus(Name={0}): Failed to create ServiceBusClient with connectionString={1}.", Name, connectionString);
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
                        InternalLogger.Error("AzureServiceBus(Name={0}): Failed to parse TimeToLiveSeconds={1}", Name, timeToLiveSeconds);
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
                            InternalLogger.Error("AzureServiceBus(Name={0}): Failed to parse TimeToLiveDays={1}", Name, timeToLiveDays);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureServiceBus(Name={0}): Failed to parse TimeToLive value. Seconds={1}, Days={2}", Name, timeToLiveSeconds, timeToLiveDays);
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
                var messageBatch = CreateMessageBatch(logEvents, partitionKey, out var messageBatchSize);
                return WriteSingleBatchAsync(messageBatch);
            }

            var partitionBuckets = SortHelpers.BucketSort(logEvents, _getMessagePartitionKeyDelegate);
            IList<Task> multipleTasks = partitionBuckets.Count > 1 ? new List<Task>(partitionBuckets.Count) : null;
            foreach (var partitionBucket in partitionBuckets)
            {
                try
                {
                    var messageBatch = CreateMessageBatch(partitionBucket.Value, partitionBucket.Key, out var messageBatchSize);

                    Task sendTask = WritePartitionBucketAsync(messageBatch, messageBatchSize);
                    if (multipleTasks == null)
                        return sendTask;

                    multipleTasks.Add(sendTask);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "AzureServiceBus(Name={0}): Failed to create Message batch.", Name);
                    if (multipleTasks == null)
                        throw;
                }
            }

            return multipleTasks?.Count > 0 ? Task.WhenAll(multipleTasks) : Task.CompletedTask;
        }

        private Task WritePartitionBucketAsync(IList<Message> messageList, int messageBatchSize)
        {
            int batchSize = CalculateBatchSize(messageList, messageBatchSize);
            if (messageList.Count <= batchSize)
            {
                return WriteSingleBatchAsync(messageList);
            }
            else
            {
                var batchCollection = GenerateBatches(messageList, batchSize);
                return WriteMultipleBatchesAsync(batchCollection);
            }
        }

        private async Task WriteMultipleBatchesAsync(IEnumerable<List<Message>> batchCollection)
        {
            // Must chain the tasks together so they don't run concurrently
            foreach (var batchItem in batchCollection)
            {
                await WriteSingleBatchAsync(batchItem).ConfigureAwait(false);
            }
        }

        IEnumerable<List<Message>> GenerateBatches(IList<Message> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
                yield return new List<Message>(source.Skip(i).Take(batchSize));
        }

        private Task WriteSingleBatchAsync(IList<Message> messageDataList)
        {
            return _cloudServiceBus.SendAsync(messageDataList);
        }

        private int CalculateBatchSize(IList<Message> messageDataList, int messageBatchSize)
        {
            if (messageBatchSize < MaxBatchSizeBytes)
                return Math.Min(messageDataList.Count, 100);

            if (messageDataList.Count > 10)
            {
                int numberOfBatches = Math.Max(messageBatchSize / MaxBatchSizeBytes, 10);
                int batchSize = Math.Max(messageDataList.Count / numberOfBatches - 1, 1);
                return Math.Min(batchSize, 100);
            }

            return 1;
        }

        private IList<Message> CreateMessageBatch(IList<LogEventInfo> logEventList, string partitionKey, out int messageBatchSize)
        {
            if (logEventList.Count == 0)
            {
                messageBatchSize = 0;
                return Array.Empty<Message>();
            }

            if (logEventList.Count == 1)
            {
                var messageData = CreateMessageData(logEventList[0], partitionKey, true);
                if (messageData == null)
                {
                    messageBatchSize = 0;
                    return Array.Empty<Message>();
                }
                messageBatchSize = EstimateEventDataSize(messageData.Body.Length);
                return new[] { messageData };
            }

            messageBatchSize = 0;
            List<Message> messageBatch = new List<Message>(logEventList.Count);
            for (int i = 0; i < logEventList.Count; ++i)
            {
                var messageData = CreateMessageData(logEventList[i], partitionKey, messageBatch.Count == 0 && i == logEventList.Count - 1);
                if (messageData != null)
                {
                    if (messageData.Body.Length > messageBatchSize)
                        messageBatchSize = messageData.Body.Length;
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

        private Message CreateMessageData(LogEventInfo logEvent, string partitionKey, bool allowThrow)
        {
            try
            {
                var messageBody = RenderLogEvent(Layout, logEvent) ?? string.Empty;
                var messageData = new Message(EncodeToUTF8(messageBody));

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

                var messageLabel = RenderLogEvent(Label, logEvent);
                if (!string.IsNullOrEmpty(messageLabel))
                {
                    messageData.Label = messageLabel;
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
                            messageData.UserProperties.Add(property.Key, propertyValue);
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

                        messageData.UserProperties.Add(property.Name, propertyValue ?? string.Empty);
                    }
                }

                return messageData;
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "AzureServiceBus(Name={0}): Failed to convert to Message.", Name);

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
                InternalLogger.Error(ex, "AzureServiceBus(Name={0}): Failed converting {1} value to Message.", Name, value?.GetType());
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

        private class CloudServiceBus : ICloudServiceBus
        {
            private ISenderClient _client;

            public TimeSpan? DefaultTimeToLive { get; private set; }

            public void Connect(string connectionString, string queuePath, string topicPath, TimeSpan? timeToLive)
            {
                DefaultTimeToLive = timeToLive;

                if (!string.IsNullOrEmpty(queuePath) || topicPath == null)
                    _client = new QueueClient(connectionString, queuePath);
                else
                    _client = new TopicClient(connectionString, topicPath);
            }

            public Task SendAsync(IList<Message> messages)
            {
                return _client.SendAsync(messages);
            }

            public Task CloseAsync()
            {
                return _client.CloseAsync();
            }
        }
    }
}
