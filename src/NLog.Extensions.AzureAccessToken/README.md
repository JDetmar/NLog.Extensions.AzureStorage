# Azure AccessToken

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureAccessToken** | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureAccessToken.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureAccessToken/) | Azure App Authentication Access Token |


## Using Active Directory Default authentication

Microsoft.Data.SqlClient 2.0.0 (and newer) supports `Authentication` option in the ConnectionString,
that enables builtin AD authentication. This removes the need for using `NLog.Extensions.AzureAccessToken`.

Example with `Authentication` assigned to `Active Directory Default`:

```charp
string ConnectionString = @"Server=demo.database.windows.net; Authentication=Active Directory Default; Database=testdb;";
```

`Active Directory Default` uses `DefaultAzureCredential` from Azure.Identity-package that supports the following identity-providers:

- EnvironmentCredential - Authentication to Azure Active Directory based on environment variables.
- ManagedIdentityCredential - Authentication to Azure Active Directory using identity assigned to deployment environment.
- SharedTokenCacheCredential - Authentication using tokens in the local cache shared between Microsoft applications.
- VisualStudioCredential - Authentication to Azure Active Directory using data from Visual Studio
- VisualStudioCodeCredential - Authentication to Azure Active Directory using data from Visual Studio Code
- AzureCliCredential - Authentication to Azure Active Directory using Azure CLI to obtain an access token

See also: [Using Active Directory Default authentication](https://docs.microsoft.com/en-us/sql/connect/ado-net/sql/azure-active-directory-authentication?view=sql-server-ver15#using-active-directory-default-authentication)

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