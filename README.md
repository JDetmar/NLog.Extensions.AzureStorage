# NLog.Extensions.AzureStorage [![AppVeyor](https://img.shields.io/appveyor/ci/JDetmar/nlog-extensions-azurestorage.svg)](https://ci.appveyor.com/project/JDetmar/nlog-extensions-azurestorage) [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureStorage.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureStorage/) 

![logo](logo64.png?raw=true)

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

_contentType_ - Azure blob ContentType (Default = text/plain)


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

### DynamicTableEntity
Instead of using the predefined NLogEntity-properties, then one can specify wanted properties:

```xml
<extensions>
  <add assembly="NLog.Extensions.AzureStorage" /> 
</extensions>

<targets>
  <target xsi:type="AzureTableStorage"
          name="String"
          connectionString="String"
          connectionStringKey="String"
          tableName="Layout">
    <contextproperty name="Level" layout="${level}" />
    <contextproperty name="LoggerName" layout="${logger}" />
    <contextproperty name="Message" layout="${message:raw=true}" />
    <contextproperty name="Exception" layout="${exception:format=tostring}" />
    <contextproperty name="FullMessage" layout="${message}" />
    <contextproperty name="MachineName" layout="${machinename}" />
  </target>
</targets>
```

It will by default always include the hardcoded property `LogTimeStamp` of type DateTime.

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

## EventHub Configuration
AzureEventHub-target has its own dedicated nuget-package, because of special Microsoft.Azure.EventHubs dependency:

[![AppVeyor](https://img.shields.io/appveyor/ci/JDetmar/nlog-extensions-azureeventhub.svg)](https://ci.appveyor.com/project/JDetmar/nlog-extensions-azurestorage) [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureEventHub.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureEventHub/) 

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureEventHub" /> 
</extensions>

<targets>
  <target xsi:type="AzureEventHub"
          name="String"
          layout="Layout"
          eventHubName="Layout"
          partitionKey="Layout"
          connectionString="String">
	<contextProperty name="level" layout="${level}" />
	<contextProperty name="exception" layout="${exception:format=shorttype}" includeEmptyValue="false" />
	<layout type="JsonLayout" includeAllProperties="true">
		<attribute name="time" layout="${longdate}" />
		<attribute name="message" layout="${message}" />
		<attribute name="threadid" layout="${threadid}" />
		<attribute name="exception" layout="${exception:format=tostring}" />
	</layout>
  </target>
</targets>
```

### Parameters

_name_ - Name of the target.

_connectionString_ - Azure storage connection string.  [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required.

_eventHubName_ - Overrides the EntityPath in the ConnectionString. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_partitionKey_ - Partition-Key which EventHub uses to generate PartitionId-hash. [Layout](https://github.com/NLog/NLog/wiki/Layouts) (Default='0')

_layout_ - EventData Body Text to be rendered and encoded as UTF8. [Layout](https://github.com/NLog/NLog/wiki/Layouts). 

_batchSize_ - Number of EventData items to send in a single batch (Default=100)

_taskDelayMilliseconds_ - Artificial delay before sending to optimize for batching (Default=200 ms)

_queueLimit_ - Number of pending LogEvents to have in memory queue, that are waiting to be sent (Default=10000)

_overflowAction_ - Action to take when reaching limit of in memory queue (Default=Discard)

## Sample Configuration

```xml
<extensions>
  <add assembly="NLog.Extensions.AzureStorage" /> 
  <add assembly="NLog.Extensions.AzureEventHub" /> 
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
	<target type="AzureQueueStorage"
            name="AzureQueue"
            connectionStringKey="storageConnectionString"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring:innerFormat=tostring:maxInnerExceptionLevel=1000}"
            queueName="NlogQueue" />
    <target type="AzureEventHub"
            name="AzureEventHub"
            connectionString="Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=NLog;SharedAccessKey=EventHub"
            layout="${longdate:universalTime=true} ${level:uppercase=true} - ${logger}: ${message} ${exception:format=tostring:innerFormat=tostring:maxInnerExceptionLevel=1000}"
            eventHubName="NlogHub"
            PartitionKey="0"/>
</targets>
```


## License
[![FOSSA Status](https://app.fossa.io/api/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage.svg?type=small)](https://app.fossa.io/projects/git%2Bgithub.com%2FJDetmar%2FNLog.Extensions.AzureStorage?ref=badge_small)