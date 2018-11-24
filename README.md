# NLog.Extensions.AzureStorage [![AppVeyor](https://img.shields.io/appveyor/ci/JDetmar/nlog-extensions-azurestorage.svg)](https://ci.appveyor.com/project/JDetmar/nlog-extensions-azurestorage) [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureStorage.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureStorage/)

![logo](logo64.png?raw=true)
[![FOSSA Status](https://app.fossa.io/api/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage.svg?type=shield)](https://app.fossa.io/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage?ref=badge_shield)

NLog Targets for Azure Blob, Table and Queue Storage 


## Blob Configuration

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureStorage" /> 
</extensions>

<targets>
  <target xsi:type="AzureBlobStorage"
          name="String"
          layout="Layout"
          blobName="Layout"
          connectionString="String"
          connectionStringKey="String"
          container="Layout" />
</targets>
```


### Parameters

_name_ - Name of the target.

_layout_ - Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_blobName_ - BlobName. [Layout](https://github.com/NLog/NLog/wiki/Layouts)  

_connectionString_ - Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_connectionStringKey_ - App key name of Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_container_ - Azure blob container name. [Layout](https://github.com/NLog/NLog/wiki/Layouts)


## Table Configuration

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureStorage" /> 
</extensions>

<targets>
  <target xsi:type="AzureTableStorage"
          name="String"
          layout="Layout"
          connectionString="String"
          connectionStringKey="String"
          tableName="Layout" 
          logTimeStampFormat="O"/>
</targets>
```
### Parameters

_name_ - Name of the target.

_layout_ - Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_connectionString_ - Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_connectionStringKey_ - App key name of Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_tableName_ - Azure table name. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_logTimeStampFormat_ - Default Log TimeStamp is set to 'O' for [Round-trip](https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#the-round-trip-o-o-format-specifier) format if not specified.

## Queue Configuration

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureStorage" /> 
</extensions>

<targets>
  <target xsi:type="AzureQueueStorage"
          name="String"
          layout="Layout"
          queueName="Layout"
          connectionString="String"
          connectionStringKey="String" />
</targets>
```

### Parameters

_name_ - Name of the target.

_layout_ - Queue Message Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_queueName_ - QueueName. [Layout](https://github.com/NLog/NLog/wiki/Layouts)  

_connectionString_ - Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_connectionStringKey_ - App key name of Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.


## Sample Configuration

```xml
<extensions>
  <add assembly="NLog.Extensions.AzureStorage" /> 
</extensions>

<targets async="true">
    <target type="AzureBlobStorage"
            name="AzureEmulator"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring:innerFormat=tostring:maxInnerExceptionLevel=1000}"
            connectionString="UseDevelopmentStorage=true;"
            container="${level}"
            blobName="${date:universalTime=true:format=yyyy-MM-dd}/${date:universalTime=true:format=HH}.log" />
    <target type="AzureBlobStorage"
            name="Azure"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring:innerFormat=tostring:maxInnerExceptionLevel=1000}"
            connectionStringKey="storageConnectionString"
            container="${machinename}"
            blobName="${logger}/${date:universalTime=true:format=yy-MM-dd}/${date:universalTime=true:format=mm}.log" />
    <target type="AzureTableStorage"
            name="AzureTable"
            connectionStringKey="storageConnectionString"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring:innerFormat=tostring:maxInnerExceptionLevel=1000}"
            tableName="NlogTable" />
</targets>
```


## License
[![FOSSA Status](https://app.fossa.io/api/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage.svg?type=large)](https://app.fossa.io/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage?ref=badge_large)