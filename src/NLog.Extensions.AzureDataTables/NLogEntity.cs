using System;
using Azure;
using Azure.Data.Tables;

namespace NLog.Extensions.AzureStorage
{
    public class NLogEntity: ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
        public string LogTimeStamp { get; set; }
        public string Level { get; set; }
        public string LoggerName { get; set; }
        public string Message { get; set; }
        public string Exception { get; set; }
        public string InnerException { get; set; }
        public string StackTrace { get; set; }
        public string FullMessage { get; set; }
        public string MachineName { get; set; }

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

        public NLogEntity() { }
    }
}
