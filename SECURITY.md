# Security Policy

## Supported Versions

Security fixes are released for the latest published version of each package on
NuGet. The packages are versioned independently:

| Package                                  | Supported            |
| ---------------------------------------- | -------------------- |
| NLog.Extensions.AzureBlobStorage         | Latest release only  |
| NLog.Extensions.AzureDataTables          | Latest release only  |
| NLog.Extensions.AzureQueueStorage        | Latest release only  |
| NLog.Extensions.AzureEventGrid           | Latest release only  |
| NLog.Extensions.AzureEventHub            | Latest release only  |
| NLog.Extensions.AzureServiceBus          | Latest release only  |

Please upgrade to the newest release before reporting an issue, as the fix may
already be published.

## Reporting a Vulnerability

Please **do not report security vulnerabilities through public GitHub issues**,
discussions, or pull requests.

Instead, report them privately using GitHub's private vulnerability reporting:

- <https://github.com/JDetmar/NLog.Extensions.AzureStorage/security/advisories/new>

This opens a private advisory visible only to the maintainers. Include as much
of the following as you can:

- The affected package and version.
- A description of the vulnerability and its impact.
- Steps to reproduce, or a proof-of-concept.
- Any known mitigations or workarounds.

## What to Expect

- We aim to acknowledge your report within 5 business days.
- We will work with you to confirm the issue and determine its severity.
- Once a fix is ready, we will publish a new package version to NuGet and, where
  appropriate, a GitHub Security Advisory crediting the reporter.
- We ask that you give us a reasonable period (up to 90 days) to release a fix
  before any public disclosure.

Thank you for helping keep this project and its users safe.
