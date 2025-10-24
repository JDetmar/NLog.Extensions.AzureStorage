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
          connectionString="Layout"
          eventHubName="Layout"
          partitionKey="Layout"
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

### General Options

_name_ - Name of the target.

_connectionString_ - Azure storage connection string.  [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required.

_eventHubName_ - Overrides the EntityPath in the ConnectionString. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_partitionKey_ - Partition-Key which EventHub uses to generate PartitionId-hash. [Layout](https://github.com/NLog/NLog/wiki/Layouts) (Default='0')

_layout_ - EventData Body Text to be rendered and encoded as UTF8. [Layout](https://github.com/NLog/NLog/wiki/Layouts). 

_contentType_ - EventData ContentType. [Layout](https://github.com/NLog/NLog/wiki/Layouts). Ex. application/json

_messageId_ - EventData MessageId. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_correlationId_ - EventData Correlationid. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_useWebSockets_ - Enable AmqpWebSockets. Ex. true/false (optional)

_customEndpointAddress_ - Custom endpoint address that can be used when establishing the connection (optional)

_serviceUri_ - Alternative to ConnectionString, where Managed Identiy is applied from DefaultAzureCredential.

_eventProducerIdentifier_ - A unique name used to identify the event producer. If null or empty, a GUID will be used as the identifier (optional)

### Authentication Options

_managedIdentityClientId_ - Sets `ManagedIdentityClientId` on `DefaultAzureCredentialOptions`. Requires `serviceUri`

_managedIdentityResourceId_ - resourceId for `ManagedIdentityResourceId` on `DefaultAzureCredentialOptions`, do not use together with `ManagedIdentityClientId`. Requires `serviceUri`.

_tenantIdentity_ - tenantId for `DefaultAzureCredentialOptions`. Requires `serviceUri`.

_sharedAccessSignature_ - Access signature for `AzureSasCredential` authentication. Requires `serviceUri`.

_accountName_ - accountName for `AzureNamedKeyCredential` authentication. Requires `serviceUri` and `accessKey`.

_accessKey_ - accountKey for `AzureNamedKeyCredential` authentication. Requires `serviceUri` and `accountName`.

_clientAuthId_ - clientId for `ClientSecretCredential` authentication. Requires `serviceUri`, `tenantIdentity` and `clientAuthSecret`.

_clientAuthSecret_ - clientSecret for `ClientSecretCredential` authentication. Requires `serviceUri`,`tenantIdentity` and `clientAuthId`.

### Proxy Options

_noProxy_ - Bypasses any system proxy and proxy in `ProxyAddress` when set to `true`. Requires `useWebSockets = true`.

_proxyAddress_ - Address of the proxy server to use (e.g. http://proxyserver:8080). Requires `useWebSockets = true`.

_proxyLogin_ - Login to use for the proxy server. Requires `proxyPassword`. Requires `useWebSockets = true`.

_proxyPassword_ - Password to use for the proxy server. Requires `proxyLogin`. Requires `useWebSockets = true`.

_useDefaultCredentialsForProxy_ - Uses the default credentials (`System.Net.CredentialCache.DefaultCredentials`) for the proxy server. Take precedence over `proxyLogin` and `proxyPassword` when set to `true`. Requires `useWebSockets = true`.

### Batching Policy

_maxBatchSizeBytes_ - Max size of a single batch in bytes [Integer](https://github.com/NLog/NLog/wiki/Data-types) (Default=1024*1024)

_batchSize_ - Number of EventData items to send in a single batch (Default=100)

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

  > `connectionString="${configsetting:ConnectionStrings.AzureEventHub}"`

* Example appsettings.json on .NetCore:

```json
  {
    "ConnectionStrings": {
      "AzureEventHub": "UseDevelopmentStorage=true;"
    }
  }
```

#### Lookup ConnectionString from app.config

  > `connectionString="${appsetting:ConnectionStrings.AzureEventHub}"`

* Example app.config on .NetFramework:

```xml
  <configuration>
    <connectionStrings>
      <add name="AzureEventHub" connectionString="UseDevelopmentStorage=true;"/>
    </connectionStrings>
  </configuration>
```

#### Lookup ConnectionString from environment-variable

  > `connectionString="${environment:AZURE_STORAGE_CONNECTION_STRING}"`

#### Lookup ConnectionString from NLog GlobalDiagnosticsContext (GDC)

  > `connectionString="${gdc:AzureEventHubConnectionString}"`

  * Example code for setting GDC-value:

```c#
  NLog.GlobalDiagnosticsContext.Set("AzureEventHubConnectionString", "UseDevelopmentStorage=true;");
```