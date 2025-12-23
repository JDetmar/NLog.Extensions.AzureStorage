# NLog Targets for Azure Storage [![AppVeyor](https://img.shields.io/appveyor/ci/JDetmar/nlog-extensions-azurestorage.svg)](https://ci.appveyor.com/project/JDetmar/nlog-extensions-azurestorage)

![logo](logo64.png?raw=true)

| Package Name                          | NuGet                 | Description | Documentation |
| ------------------------------------- | :-------------------: | ----------- | ------------- |
| **NLog.Extensions.AzureBlobStorage**  | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureBlobStorage.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureBlobStorage/) | Azure Blob Storage | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureBlobStorage/README.md) | 
| **NLog.Extensions.AzureDataTables**   | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureDataTables.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureDataTables/) | Azure Table Storage or Azure CosmosDb Tables | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureDataTables/README.md) | 
| **NLog.Extensions.AzureEventHub**     | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureEventHub.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureEventHub/) | Azure EventHubs | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureEventHub/README.md) | 
| **NLog.Extensions.AzureEventGrid**    | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureEventGrid.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureEventGrid/) | Azure Event Grid | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureEventGrid/README.md) | 
| **NLog.Extensions.AzureQueueStorage** | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureQueueStorage.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureQueueStorage/) | Azure Queue Storage | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureQueueStorage/README.md) | 
| **NLog.Extensions.AzureServiceBus**   | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureServiceBus.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureServiceBus/) | Azure Service Bus | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureServiceBus/README.md) | 
| **NLog.Extensions.AzureAccessToken**  | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureAccessToken.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureAccessToken/) | Azure App Authentication Access Token for Managed Identity | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureAccessToken/README.md) | 

Initially all NLog targets was bundled into a single nuget-package called [NLog.Extensions.AzureStorage](https://www.nuget.org/packages/NLog.Extensions.AzureStorage/).
But Microsoft decided to discontinue [WindowsAzure.Storage](https://www.nuget.org/packages/WindowsAzure.Storage/) and split into multiple parts.
Later Microsoft also decided to discontinue [Microsoft.Azure.DocumentDB](https://www.nuget.org/packages/Microsoft.Azure.DocumentDB.Core/)
and so [NLog.Extensions.AzureCosmosTable](https://www.nuget.org/packages/NLog.Extensions.AzureCosmosTable/) also became deprecated.

## Sample Configuration

```xml
<extensions>
  <add assembly="NLog.Extensions.AzureBlobStorage" /> 
  <add assembly="NLog.Extensions.AzureDataTables" /> 
  <add assembly="NLog.Extensions.AzureQueueStorage" /> 
  <add assembly="NLog.Extensions.AzureEventHub" /> 
  <add assembly="NLog.Extensions.AzureEventGrid" /> 
  <add assembly="NLog.Extensions.AzureServiceBus" /> 
</extensions>

<targets
    <target type="AzureBlobStorage"
            name="Azure"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring}"
            connectionString="DefaultEndpointsProtocol=https;AccountName=##accountName##;AccountKey=##accountKey##;EndpointSuffix=core.windows.net"
            container="${machinename}"
            blobName="${logger}/${date:universalTime=true:format=yy-MM-dd}/${date:universalTime=true:format=HH}.log">
                <metadata name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
    </target>
    <target type="AzureDataTables"
            name="AzureTable"
            connectionString="DefaultEndpointsProtocol=http;AccountName=##accountName##;AccountKey=##accountKey##;"
            layout="${message} ${exception:format=tostring}"
            tableName="NlogTable" />
    <target type="AzureQueueStorage"
            name="AzureQueue"
            connectionString="UseDevelopmentStorage=true;"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring}"
            queueName="NlogQueue">
                <metadata name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
    </target>
    <target type="AzureEventHub"
            name="AzureEventHub"
            connectionString="Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=NLog;SharedAccessKey=EventHub"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring}"
            eventHubName="NlogHub"
            PartitionKey="0">
                <messageProperty name="exceptiontype" layout="${exception:format=type}" />   <!-- Multiple allowed -->
    </target>
    <target type="AzureServiceBus"
            name="AzureServiceBus"
            connectionString="Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=NLog;SharedAccessKey=ServiceBus"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring}"
            label="Message from NLog"
            topiceName="NLogTopic">
                <messageProperty name="exceptiontype" layout="${exception:format=type}" />   <!-- Multiple allowed -->
    </target>
</targets>
```

## License
[![FOSSA Status](https://app.fossa.io/api/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage.svg?type=small)](https://app.fossa.io/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage?ref=badge_small)
