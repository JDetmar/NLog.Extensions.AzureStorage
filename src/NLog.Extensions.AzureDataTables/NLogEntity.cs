using System;
using Azure;
using Azure.Data.Tables;

namespace NLog.Extensions.AzureStorage
{
    /// <summary>
    /// Represents a log entry entity for Azure Table Storage.
    /// </summary>
    public class NLogEntity: ITableEntity
    {
        /// <summary>
        /// Gets or sets the partition key of the table entity.
        /// </summary>
        public string PartitionKey { get; set; }
        /// <summary>
        /// Gets or sets the row key of the table entity.
        /// </summary>
        public string RowKey { get; set; }
        /// <summary>
        /// Gets or sets the timestamp of the table entity.
        /// </summary>
        public DateTimeOffset? Timestamp { get; set; }
        /// <summary>
        /// Gets or sets the ETag of the table entity.
        /// </summary>
        public ETag ETag { get; set; }
        /// <summary>
        /// Gets or sets the formatted timestamp of the log event.
        /// </summary>
        public string LogTimeStamp { get; set; }
        /// <summary>
        /// Gets or sets the log level of the log event.
        /// </summary>
        public string Level { get; set; }
        /// <summary>
        /// Gets or sets the name of the logger that created the log event.
        /// </summary>
        public string LoggerName { get; set; }
        /// <summary>
        /// Gets or sets the log message.
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// Gets or sets the exception information.
        /// </summary>
        public string Exception { get; set; }
        /// <summary>
        /// Gets or sets the inner exception information.
        /// </summary>
        public string InnerException { get; set; }
        /// <summary>
        /// Gets or sets the stack trace information.
        /// </summary>
        public string StackTrace { get; set; }
        /// <summary>
        /// Gets or sets the full formatted log message.
        /// </summary>
        public string FullMessage { get; set; }
        /// <summary>
        /// Gets or sets the name of the machine where the log event occurred.
        /// </summary>
        public string MachineName { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NLogEntity"/> class with log event data.
        /// </summary>
        /// <param name="logEvent">The log event information.</param>
        /// <param name="layoutMessage">The formatted layout message.</param>
        /// <param name="machineName">The machine name.</param>
        /// <param name="partitionKey">The partition key.</param>
        /// <param name="rowKey">The row key.</param>
        /// <param name="logTimeStampFormat">The timestamp format string.</param>
        public NLogEntity(LogEventInfo logEvent, string layoutMessage, string machineName, string partitionKey, string rowKey, string logTimeStampFormat)
        {
            FullMessage = TruncateWhenTooBig(layoutMessage);
            Level = logEvent.Level.Name;
            LoggerName = logEvent.LoggerName;
            Message = TruncateWhenTooBig(logEvent.Message);
            LogTimeStamp = logEvent.TimeStamp.ToString(logTimeStampFormat);
            MachineName = machineName;
            if(logEvent.Exception != null)
            {
                var exception = logEvent.Exception;
                var innerException = exception.InnerException;
                if (exception is AggregateException aggregateException)
                {
                    if (aggregateException.InnerExceptions?.Count == 1 && !(aggregateException.InnerExceptions[0] is AggregateException))
                    {
                        exception = aggregateException.InnerExceptions[0];
                        innerException = exception.InnerException;
                    }
                    else
                    {
                        var flatten = aggregateException.Flatten();
                        if (flatten.InnerExceptions?.Count == 1)
                        {
                            exception = flatten.InnerExceptions[0];
                            innerException = exception.InnerException;
                        }
                        else
                        {
                            innerException = flatten;
                        }
                    }
                }

                Exception = string.Concat(exception.Message, " - ", exception.GetType().ToString());
                StackTrace = TruncateWhenTooBig(exception.StackTrace);
                if (innerException != null)
                {
                    var innerExceptionText = innerException.ToString();
                    InnerException = TruncateWhenTooBig(innerExceptionText);
                }
            }
            RowKey = rowKey;
            PartitionKey = partitionKey;
        }

        private static string TruncateWhenTooBig(string stringValue)
        {
             return stringValue?.Length >= Targets.DataTablesTarget.ColumnStringValueMaxSize ?
                stringValue.Substring(0, Targets.DataTablesTarget.ColumnStringValueMaxSize - 1) : stringValue;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NLogEntity"/> class.
        /// </summary>
        public NLogEntity() { }
    }
}
