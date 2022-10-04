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
          gridEventSubject="Layout"
          cloudEventSource="Layout"
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

_gridEventSubject_ - Resource path relative to the topic path. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_cloudEventSource_ - Conext where the event happened. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_eventType_ - Type of the event. Ex. "Contoso.Items.ItemReceived". [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_contentType_ - Content type of the data-payload. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_dataSchema_ - Schema version of the data-payload. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_accessKey_ - Input for AzureKeyCredential. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_tenantIdentity_ - Alternative to AccessKey. Input for DefaultAzureCredential.

_resourceIdentity_ - Alternative to AccessKey. Input for DefaultAzureCredential as ManagedIdentityResourceId.

_clientIdentity_ - Alternative to AccessKey. Input for DefaultAzureCredential as ManagedIdentityClientId.

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)