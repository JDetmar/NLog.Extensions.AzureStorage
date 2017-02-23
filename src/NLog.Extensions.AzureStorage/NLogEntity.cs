using Microsoft.WindowsAzure.Storage.Table;
using NLog.Layouts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureStorage
{
    public class NLogEntity: TableEntity
    {
        public string Exception { get; set; }
        public string InnerException { get; set; }
        public string FormattedMessage { get; set; }
        public string Level { get; set; }
        public string LoggerName { get; set; }
        public string Message { get; set; }
        public int SequenceID { get; set; }
        public string LogTimeStamp { get; set; }
        public string MachineName { get; set; }

        public NLogEntity(LogEventInfo logEvent, Layout layout)
        {
            Exception = GetExceptionAsString(logEvent.Exception);
            FormattedMessage = layout.Render(logEvent);
            Level = logEvent.Level.Name;
            LoggerName = logEvent.LoggerName;
            Message = logEvent.Message;
            SequenceID = logEvent.SequenceID;
            LogTimeStamp = logEvent.TimeStamp.ToString();
            MachineName = Environment.MachineName;
            if(logEvent.Exception != null && logEvent.Exception.InnerException != null)
            {
                InnerException = logEvent.Exception.InnerException.ToString();
            }
            RowKey = String.Format("{0}__{1}", (DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks).ToString("d19"), Guid.NewGuid());
            PartitionKey = LoggerName;
        }
        public NLogEntity() { }
        
        

        private static string GetExceptionAsString(Exception ex)
        {

            var exceptionString = new StringBuilder();
            if (ex != null)
            {
                foreach (DictionaryEntry item in ex.Data)
                {
                    exceptionString.AppendLine($"{item.Key} = {item.Value}");
                }
            }
            return exceptionString.ToString();
        }
    }
}
