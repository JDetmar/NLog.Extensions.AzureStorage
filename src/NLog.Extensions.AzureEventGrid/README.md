# Azure Event Grid

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureEventGrid** | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureEventGrid.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureEventGrid/) | Azure Event Grid |

## Queue Configuration

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureEventGrid" /> 
</extensions>

<targets>
  <target xsi:type="AzureEventGrid"
          name="String"
          layout="Layout"
          topic="Layout"
          cloudEventSource="Layout"
          gridEventSubject="Layout"
          eventType="Layout"
          contentType="Layout"
          dataSchema="Layout"
          accessKey="Layout">
            <metadata name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
  </target>
</targets>
```

### Parameters

_name_ - Name of the target.

_layout_ - Event data-payload. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_topic_ - Topic EndPoint Uri. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_cloudEventSource_ - Only for CloudEvent and specify context where event occurred. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_gridEventSubject_ - Only for EventGridEvent and specify resource path relative to the topic path. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_eventType_ - Type of the event. Ex. "Contoso.Items.ItemReceived". [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_contentType_ - Content type of the data-payload. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_dataSchema_ - Schema version of the data-payload. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_tenantIdentity_ - Input for DefaultAzureCredential. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_resourceIdentity_ - Input for DefaultAzureCredential as ManagedIdentityResourceId. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_clientIdentity_ - Input for DefaultAzureCredential as ManagedIdentityClientId. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_accessKey_ - Alternative to DefaultAzureCredential. Input for AzureKeyCredential. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_sharedAccessSignature_ - Alternative to DefaultAzureCredential. Input for AzureSasCredential. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)