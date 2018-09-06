using System;
using Microsoft.WindowsAzure.Storage.Table;
using NLog.Layouts;

namespace NLog.Extensions.AzureStorage
{
    public class NLogEntity: TableEntity
    {
        public string LogTimeStamp { get; set; }
        public string Level { get; set; }
        public string LoggerName { get; set; }
        public string Message { get; set; }
        public string Exception { get; set; }
        public string InnerException { get; set; }
        public string StackTrace { get; set; }
        public string FullMessage { get; set; }
        public string MachineName { get; set; }

        public NLogEntity(LogEventInfo logEvent, string layoutMessage, string machineName, string logTimeStampFormat)
        {
            FullMessage = layoutMessage;
            Level = logEvent.Level.Name;
            LoggerName = logEvent.LoggerName;
            Message = logEvent.Message;
            LogTimeStamp = logEvent.TimeStamp.ToString(logTimeStampFormat);
            MachineName = machineName;
            if(logEvent.Exception != null)
            {
                Exception = logEvent.Exception.Message;
                StackTrace = logEvent.Exception.StackTrace;
                if (logEvent.Exception.InnerException != null)
                {
                    InnerException = logEvent.Exception.InnerException.ToString();
                }
            }
            RowKey = string.Concat((DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks).ToString("d19"), "__", Guid.NewGuid().ToString());
            PartitionKey = LoggerName;
        }
        public NLogEntity() { }
    }
}
