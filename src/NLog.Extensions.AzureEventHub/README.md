# Azure EventHubs

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureEventHub**     | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureEventHub.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureEventHub/) | Azure EventHubs |

## EventHub Configuration

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
          contentType="Layout"
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

_contentType_ - EventData ContentType to be assigned. [Layout](https://github.com/NLog/NLog/wiki/Layouts). 

### Batching Policy

_batchSize_ - Number of EventData items to send in a single batch (Default=1)

_taskDelayMilliseconds_ - Artificial delay before sending to optimize for batching (Default=1 ms)

_queueLimit_ - Number of pending LogEvents to have in memory queue, that are waiting to be sent (Default=10000)

_overflowAction_ - Action to take when reaching limit of in memory queue (Default=Discard)

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)