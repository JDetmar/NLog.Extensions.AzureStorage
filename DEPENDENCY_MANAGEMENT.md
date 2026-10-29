# Dependency Management

## TL;DR

**Dependabot handles security updates automatically.** Just review PRs labeled `security` and merge if tests pass.

## How It Works

1. Dependabot scans weekly for security vulnerabilities
2. Creates PRs automatically for security patches (ignores major version bumps)
3. You review the PR (~5 min)
4. Tests run in CI
5. Merge if green
6. Update version number in `.csproj` and add release note

## When to Update

✅ **Security fixes** - Always
⚠️ **New features** - Only if needed
❌ **Major versions** - Avoid unless necessary

## Quick Reference

### Approve a Dependabot PR

```bash
# Tests run automatically in CI, but you can run locally:
dotnet test test\NLog.Extensions.AzureBlobStorage.Tests /p:Configuration=Release
dotnet test test\NLog.Extensions.AzureDataTables.Tests /p:Configuration=Release
dotnet test test\NLog.Extensions.AzureQueueStorage.Tests /p:Configuration=Release
dotnet test test\NLog.Extensions.AzureEventGrid.Tests /p:Configuration=Release
dotnet test test\NLog.Extensions.AzureEventHub.Tests /p:Configuration=Release
dotnet test test\NLog.Extensions.AzureServiceBus.Tests /p:Configuration=Release
```

After merge: Bump version in affected `.csproj` files and update `<PackageReleaseNotes>`.

### SDK and Lock Files

Builds are reproducible, so the .NET SDK is pinned in `global.json` and every project has a committed `packages.lock.json`.
CI restores in locked mode and fails if a lock file is out of date.

The lock files include SDK-bundled packages such as `Microsoft.NET.ILLink.Tasks`, so a Dependabot PR that bumps `global.json` also needs fresh lock files.
Install the new SDK, run this, and push the result to the PR:

```bash
dotnet restore src/NLog.Extensions.AzureStorage.sln --force-evaluate
dotnet restore src/NLog.Extensions.AzureAccessToken --force-evaluate
```

### Check for Vulnerabilities Manually

```bash
dotnet list package --vulnerable
```

### GitHub Settings

Enable these notifications:

- Dependabot alerts (Settings → Security)
- PRs labeled `security`

## Versioning

- **Patch** (x.y.Z): Security fixes, dependency updates
- **Minor** (x.Y.0): New features
- **Major** (X.0.0): Breaking changes (rare)

## Time Commitment

- **Most months**: 0 minutes (no security issues)
- **Security update**: 10-15 minutes (review + merge PR)
- **Quarterly check**: Optional
