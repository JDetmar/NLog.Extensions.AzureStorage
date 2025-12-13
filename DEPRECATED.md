# Deprecated package: NLog.Extensions.AzureCosmosTable

`NLog.Extensions.AzureCosmosTable` is deprecated, unmaintained, and contains known vulnerabilities in its dependency chain. It is no longer supported and will not receive fixes or updates.

## Guidance

- Do not use this package in new or existing projects.
- Migrate to `NLog.Extensions.AzureDataTables`, which targets the supported Azure Data Tables APIs.
- Unlist or remove any internal feeds that still carry this package to prevent accidental consumption.

## Status

- Maintenance: stopped
- Security: known vulnerabilities, will not be fixed
- NuGet: marked deprecated; recommend unlisting any remaining versions
- Last code commit containing this package: f1c345b490a7353c5fd00d1dde42364d162173ce (2022-01-29 — see tag `archive/azure-cosmos-table-2022-01-29`)

## Deprecated package: NLog.Extensions.AzureStorage (bundle)

The legacy bundled package `NLog.Extensions.AzureStorage` was superseded when targets were split. It should not be used.

### Guidance (bundle)

- Do not use the bundled package; consume the individual packages (Blob, Queue, EventHub, EventGrid, DataTables, ServiceBus, AccessToken) instead.
- Unlist or remove any internal feeds that still carry the bundle to prevent accidental consumption.

### Status (bundle)

- Maintenance: stopped
- Security: inherits vulnerabilities from deprecated dependencies in the bundle; will not be fixed
- NuGet: should be marked deprecated/unlisted
- Last code commit containing this bundle: c8bfb7966d550221e1aeca859705f606c8559dd2 (tag `archive/azure-storage-bundle`)
