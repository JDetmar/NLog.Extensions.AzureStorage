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

_cloudEventSource_ - Only for [CloudEvent](https://learn.microsoft.com/en-us/azure/event-grid/cloud-event-schema)-format (Recommended event format) and specify context where event occurred. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_gridEventSubject_ - Only for [GridEvent](https://learn.microsoft.com/en-us/azure/event-grid/event-schema)-format and specify resource path relative to the topic path. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_eventType_ - Type of the event. Ex. "Contoso.Items.ItemReceived". [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_contentType_ - Content type of the data-payload. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_dataFormat_ - Format of the data-payload (Binary / Json). Default Binary. `String`

_dataSchema_ - Schema version of the data-payload. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_managedIdentityClientId_ - Sets `ManagedIdentityClientId` on `DefaultAzureCredentialOptions`. Requires `serviceUri`.

_managedIdentityResourceId_ - resourceId for `ManagedIdentityResourceId` on `DefaultAzureCredentialOptions`, do not use together with `ManagedIdentityClientId`. Requires `serviceUri`.

_tenantIdentity_ - tenantId for `DefaultAzureCredentialOptions`. Requires `serviceUri`.

_sharedAccessSignature_ - Access signature for `AzureSasCredential` authentication. Requires `serviceUri`.

_accessKey_ - Key for `AzureKeyCredential` authentication. Requires `serviceUri`.

_clientAuthId_ - clientId for `ClientSecretCredential` authentication. Requires `tenantIdentity` and `clientAuthSecret`.

_clientAuthSecret_ - clientSecret for `ClientSecretCredential` authentication. Requires `tenantIdentity` and `clientAuthId`.

_noProxy_ - Bypasses any system proxy and proxy in `ProxyAddress` when set to `true`.

_proxyAddress_ - Address of the proxy server to use (e.g. http://proxyserver:8080).

_proxyLogin_ - Login to use for the proxy server. Requires `proxyPassword`.

_proxyPassword_ - Password to use for the proxy server. Requires `proxyLogin`.

_useDefaultCredentialsForProxy_ - Uses the default credentials (`System.Net.CredentialCache.DefaultCredentials`) for the proxy server, overriding any values that may have been set in `proxyLogin` and `proxyPassword`.
Only applies if `noProxy` is not set to `true`.

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)

## Azure Identity Environment
When `DefaultAzureCredential` is used for Azure Identity, then it will recognize these environment variables:
- `AZURE_CLIENT_ID` - For ManagedIdentityClientId / WorkloadIdentityClientId
- `AZURE_TENANT_ID` - For TenantId

See also: [Set up Your Environment for Authentication](https://github.com/Azure/azure-sdk-for-go/wiki/Set-up-Your-Environment-for-Authentication)