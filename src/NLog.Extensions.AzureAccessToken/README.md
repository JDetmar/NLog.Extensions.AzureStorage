# Azure AccessToken

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureAccessToken** | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureAccessToken.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureAccessToken/) | Azure App Authentication Access Token |

## Managed Identity Configuration with DatabaseTarget

Remember to setup the DbProvider for the DatabaseTarget to use Microsoft SqlConnection, and also remember to add the matching nuget-package.

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureAccessToken" /> 
</extensions>

<targets>
  <target xsi:type="Database" connectionString="...">
    <dbProvider>Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient</dbProvider>
    <connectionProperty name="AccessToken" layout="${AzureAccessToken:ResourceName=${gdc:DatabaseHostSuffix}}"  />
  </target>
</targets>
```

```c#
NLog.GlobalDiagnosticsContext.Set("DatabaseHostSuffix", $"https://{DatabaseHostSuffix}/");
NLog.LogManager.LoadConfiguration("nlog.config");
```

### Parameters

_ResourceName_ - AzureServiceTokenProvider Resource (Ex. https://management.azure.com or https://database.windows.net/ or https://storage.azure.com/). [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required.

_TenantId_ - AzureServiceTokenProvider TenantId (or directory Id) of your Azure Active Directory. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Optional.

_ConnectionString_ - AzureServiceTokenProvider ConnectionString [Layout](https://github.com/NLog/NLog/wiki/Layouts) Optional.

_AzureAdInstance_ - AzureServiceTokenProvider AzureAdInstance [Layout](https://github.com/NLog/NLog/wiki/Layouts) Optional.