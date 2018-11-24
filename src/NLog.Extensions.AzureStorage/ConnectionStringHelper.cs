using System;
#if !NETSTANDARD
using System.Configuration;
using Microsoft.Azure;
#endif
using NLog.Layouts;

namespace NLog.Extensions.AzureStorage
{
    static class ConnectionStringHelper
    {
        public static string LookupConnectionString(Layout connectionStringLayout, string connectionStringKey)
        {
            var connectionString = connectionStringLayout != null ? connectionStringLayout.Render(LogEventInfo.CreateNullEvent()) : string.Empty;

#if !NETSTANDARD
            if (!string.IsNullOrWhiteSpace(connectionStringKey))
            {
                connectionString = CloudConfigurationManager.GetSetting(connectionStringKey);
                if (string.IsNullOrWhiteSpace(connectionString))
                    connectionString = ConfigurationManager.ConnectionStrings[connectionStringKey]?.ConnectionString;
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new ArgumentException($"No ConnectionString found with ConnectionStringKey: {connectionStringKey}.");
                }
            }
#endif

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("A ConnectionString or ConnectionStringKey is required");
            }

            return connectionString;
        }
    }
}
