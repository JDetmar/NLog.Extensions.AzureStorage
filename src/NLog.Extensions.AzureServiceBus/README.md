# Azure ServiceBus

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureServiceBus** | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureServiceBus.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureServiceBus/) | Azure Service Bus |

## ServiceBus Configuration

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureServiceBus" /> 
</extensions>

<targets>
  <target xsi:type="AzureServiceBus"
          name="String"
          layout="Layout"
          connectionString="Layout"
          queueName="Layout"
          topicName="Layout"
          sessionId="Layout"
          partitionKey="Layout"
          subject="Layout"
          contentType="Layout"
          messageId="Layout"
          correlationId="Layout">
	    <messageProperty name="level" layout="${level}" />
	    <messageProperty name="exception" layout="${exception:format=shorttype}" includeEmptyValue="false" />
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

_queueName_ - QueueName for multiple producers single consumer. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_topicName_ - TopicName for multiple producers and multiple subscribers. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_sessionId_ - SessionId-Key which Service Bus uses to generate PartitionId-hash. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_partitionKey_ - Partition-Key which Service Bus uses to generate PartitionId-hash. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_subject_ - Service Bus Message Subject to be used as label for the message. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_layout_ - Service Bus Message Body to be rendered and encoded as UTF8. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_contentType_ - Service Bus Message Body ContentType. [Layout](https://github.com/NLog/NLog/wiki/Layouts). Ex. application/json

_messageId_ - Service Bus Message MessageId. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_correlationId_ - Service Bus Message Correlationid. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_timeToLiveSeconds_ - Default Time-To-Live (TTL) for ServiceBus messages in seconds (Optional)

_timeToLiveDays_ - Default Time-To-Live (TTL) for ServiceBus messages in days (Optional)

_useWebSockets_ - Enable AmqpWebSockets. Ex. true/false (optional)

_webSocketProxyAddress_ - Custom WebProxy address for WebSockets (optional)

_customEndpointAddress_ - Custom endpoint address that can be used when establishing the connection (optional)

_serviceUri_ - Alternative to ConnectionString, where Managed Identiy is applied from DefaultAzureCredential.

_managedIdentityClientId_ - Sets `ManagedIdentityClientId` on `DefaultAzureCredentialOptions`. Requires `serviceUri`.

_managedIdentityResourceId_ - resourceId for `ManagedIdentityResourceId` on `DefaultAzureCredentialOptions`, do not use together with `ManagedIdentityClientId`. Requires `serviceUri`.

_tenantIdentity_ - tenantId for `DefaultAzureCredentialOptions`. Requires `serviceUri`.

_sharedAccessSignature_ - Access signature for `AzureSasCredential` authentication. Requires `serviceUri`.

_accountName_ - accountName for `AzureNamedKeyCredential` authentication. Requires `serviceUri` and `accessKey`.

_accessKey_ - accountKey for `AzureNamedKeyCredential` authentication. Requires `serviceUri` and `accountName`.

### Batching Policy

_maxBatchSizeBytes_ - Max size of a single batch in bytes [Integer](https://github.com/NLog/NLog/wiki/Data-types) (Default=256*1024)

_batchSize_ - Number of LogEvents to send in a single batch (Default=100)

_taskDelayMilliseconds_ - Artificial delay before sending to optimize for batching (Default=200 ms)

_queueLimit_ - Number of pending LogEvents to have in memory queue, that are waiting to be sent (Default=10000)

_overflowAction_ - Action to take when reaching limit of in memory queue (Default=Discard)

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)

## Azure Identity Environment
When using `ServiceUri` (Instead of ConnectionString), then `DefaultAzureCredential` is used for Azure Identity which supports environment variables:
- `AZURE_CLIENT_ID` - For ManagedIdentityClientId / WorkloadIdentityClientId
- `AZURE_TENANT_ID` - For TenantId

See also: [Set up Your Environment for Authentication](https://github.com/Azure/azure-sdk-for-go/wiki/Set-up-Your-Environment-for-Authentication)

## Azure ConnectionString

NLog Layout makes it possible to retrieve settings from [many locations](https://nlog-project.org/config/?tab=layout-renderers).

#### Lookup ConnectionString from appsettings.json

  > `connectionString="${configsetting:ConnectionStrings.AzureBus}"`

* Example appsettings.json on .NetCore:
```json
  {
    "ConnectionStrings": {
      "AzureBus": "UseDevelopmentStorage=true;"
    }
  }
```

#### Lookup ConnectionString from app.config

   > `connectionString="${appsetting:ConnectionStrings.AzureBus}"`

* Example app.config on .NetFramework:
```xml
   <configuration>
      <connectionStrings>
        <add name="AzureBus" connectionString="UseDevelopmentStorage=true;"/>
      </connectionStrings>
   </configuration>
```

#### Lookup ConnectionString from environment-variable

   > `connectionString="${environment:AZURE_STORAGE_CONNECTION_STRING}"`

#### Lookup ConnectionString from NLog GlobalDiagnosticsContext (GDC)
 
  > `connectionString="${gdc:AzureBusConnectionString}"`
  
* Example code for setting GDC-value:
```c#
  NLog.GlobalDiagnosticsContext.Set("AzureBusConnectionString", "UseDevelopmentStorage=true;");
```