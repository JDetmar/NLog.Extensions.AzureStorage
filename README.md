# NLog Targets for Azure Storage [![AppVeyor](https://img.shields.io/appveyor/ci/JDetmar/nlog-extensions-azurestorage.svg)](https://ci.appveyor.com/project/JDetmar/nlog-extensions-azurestorage)

![logo](logo64.png?raw=true)

| Package Name                          | NuGet                 | Description | Documentation |
| ------------------------------------- | :-------------------: | ----------- | ------------- |
| **NLog.Extensions.AzureBlobStorage**  | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureBlobStorage.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureBlobStorage/) | Azure Blob Storage | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureBlobStorage/README.md) | 
| **NLog.Extensions.AzureCosmosTable**  | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureCosmosTable.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureCosmosTable/) | Azure Table Storage or Azure CosmosDb Tables | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureCosmosTable/README.md) | 
| **NLog.Extensions.AzureEventHub**     | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureEventHub.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureEventHub/) | Azure EventHubs | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureEventHub/README.md) | 
| **NLog.Extensions.AzureQueueStorage** | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureQueueStorage.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureQueueStorage/) | Azure Queue Storage | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureQueueStorage/README.md) | 
| **NLog.Extensions.AzureAccessToken**  | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureAccessToken.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureAccessToken/) | Azure App Authentication Access Token | [![](https://img.shields.io/badge/Readme-Docs-blue)](src/NLog.Extensions.AzureAccessToken/README.md) | 

Before all NLog targets was bundled into a single nuget-package called [NLog.Extensions.AzureStorage](https://www.nuget.org/packages/NLog.Extensions.AzureStorage/).
But Microsoft decided to discontinue [WindowsAzure.Storage](https://www.nuget.org/packages/WindowsAzure.Storage/) and split into multiple parts.

## Sample Configuration

```xml
<extensions>
  <add assembly="NLog.Extensions.AzureBlobStorage" /> 
  <add assembly="NLog.Extensions.AzureCosmosTable" /> 
  <add assembly="NLog.Extensions.AzureEventHub" /> 
  <add assembly="NLog.Extensions.AzureQueueStorage" /> 
  <add assembly="NLog.Extensions.AzureAccessToken" /> 
</extensions>

<targets async="true">
    <target type="AzureBlobStorage"
            name="AzureEmulator"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring}"
            connectionString="UseDevelopmentStorage=true;"
            container="${level}"
            blobName="${date:universalTime=true:format=yyyy-MM-dd}/${date:universalTime=true:format=HH}.log" />
    <target type="AzureBlobStorage"
            name="Azure"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring}"
            connectionString="DefaultEndpointsProtocol=https;AccountName=##accountName##;AccountKey=##accountKey##;EndpointSuffix=core.windows.net"
            container="${machinename}"
            blobName="${logger}/${date:universalTime=true:format=yy-MM-dd}/${date:universalTime=true:format=HH}.log">
                <metadata name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
                <tag name="mytag" layout="mytagvalue" /> <!-- Multiple allowed -->
    </target>
    <target type="AzureCosmosTable"
            name="AzureTable"
            connectionStringKey="storageConnectionString"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring}"
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
            PartitionKey="0"/>
</targets>
```

## License
[![FOSSA Status](https://app.fossa.io/api/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage.svg?type=small)](https://app.fossa.io/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage?ref=badge_small)