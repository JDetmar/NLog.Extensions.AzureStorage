# Azure Table Storage and Cosmos Table

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureCosmosTable**  | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureCosmosTable.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureCosmosTable/) | Azure Table Storage or Azure CosmosDb Tables |

## Table Configuration
Supports both Azure Storage Tables and CosmosDB Tables.

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureCosmosTable" /> 
</extensions>

<targets>
  <target xsi:type="AzureCosmosTable"
          name="String"
          layout="Layout"
          connectionString="String"
          connectionStringKey="String"
          tableName="Layout"
          logTimeStampFormat="O"
          timeToLiveDays="0" />
</targets>
```
### Parameters

_name_ - Name of the target.

_layout_ - Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_connectionString_ - Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_connectionStringKey_ - App key name of Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_tableName_ - Azure table name. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_rowKey_ - Azure Table RowKey. [Layout](https://github.com/NLog/NLog/wiki/Layouts). Default = "InverseTicks_${guid}"

_partitionKey_ - Azure PartitionKey. [Layout](https://github.com/NLog/NLog/wiki/Layouts). Default = `${logger}`

_logTimeStampFormat_ - Default Log TimeStamp is set to 'O' for [Round-trip](https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#the-round-trip-o-o-format-specifier) format if not specified.

_timeToLiveSeconds_ - Default [Time-to-live](https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live) (TTL) for CosmosDb rows in seconds. Default = 0 (Off - Forever)

_timeToLiveDays_ - Default [Time-to-live](https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live) (TTL) for CosmosDb rows in days. Default = 0 (Off - Forever)

### DynamicTableEntity
Instead of using the predefined NLogEntity-properties, then one can specify wanted properties:

```xml
<extensions>
  <add assembly="NLog.Extensions.AzureCosmosTable" /> 
</extensions>

<targets>
  <target xsi:type="AzureCosmosTable"
          name="String"
          connectionString="Layout"
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

### Batching Policy

_batchSize_ - Number of EventData items to send in a single batch (Default=100)

_taskDelayMilliseconds_ - Artificial delay before sending to optimize for batching (Default=200 ms)

_queueLimit_ - Number of pending LogEvents to have in memory queue, that are waiting to be sent (Default=10000)

_overflowAction_ - Action to take when reaching limit of in memory queue (Default=Discard)

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)

## Azure ConnectionString

NLog Layout makes it possible to retrieve settings from [many locations](https://nlog-project.org/config/?tab=layout-renderers).

#### Lookup ConnectionString from appsettings.json

  > `connectionString="${configsetting:ConnectionStrings.AzureTable}"`

* Example appsettings.json on .NetCore:

```json
  {
    "ConnectionStrings": {
      "AzureTable": "Server=tcp:server.database.windows.net;"
    }
  }
```

#### Lookup ConnectionString from app.config

  > `connectionString="${appsetting:ConnectionStrings.AzureTable}"`

* Example app.config on .NetFramework:

```xml
  <configuration>
    <connectionStrings>
      <add name="AzureTable" connectionString="Server=tcp:server.database.windows.net;"/>
    </connectionStrings>
  </configuration>
```

#### Lookup ConnectionString from environment-variable

  > `connectionString="${environment:AZURESQLCONNSTR_CONNECTION_STRING}"`

#### Lookup ConnectionString from NLog GlobalDiagnosticsContext (GDC)

  > `connectionString="${gdc:AzureTableConnectionString}"`

* Example code for setting GDC-value:

```c#
  NLog.GlobalDiagnosticsContext.Set("AzureTableConnectionString", "Server=tcp:server.database.windows.net;");
```