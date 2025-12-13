# Deprecation and Removal Playbook

Practical steps to deprecate and remove a package (while keeping history) without letting vulnerable code stay on the default branch or get republished.

## Steps

1. **Declare deprecation**
   - Open an issue/PR stating the reason (vulnerability/abandonment) and affected package name.
   - Add a loud banner to the package README and a short note in the root README pointing to the safer alternative.
   - Add/update `DEPRECATED.md` with the status and migration guidance.

2. **Stop distribution**
   - Remove the project from the solution and CI pack/test pipelines so it cannot be built or packed.
   - Unlist all NuGet versions, or publish a final version with release notes that say "deprecated, insecure, unsupported" and link to the alternative.
   - Verify no other packages in the repo reference it (remove references or add a compile-time `#error` guard if needed).

3. **Clean the default branch for scanners**
   - Delete the package source folder from `master` (or default branch) and replace it with a small placeholder README that states it was removed, why, and where to find an alternative.
   - Keep a brief note in the root README so users understand it was intentionally removed.

4. **Preserve history without branch sprawl**
   - Tag the last commit that still contained the code (e.g., `archive/<package>-YYYY-MM-DD`).

5. **Security and comms**
   - If the risk is security-related, add a short SECURITY/Advisory note: status = won't fix, remediation = use alternative, scope of impact.
   - Optionally pin the advisory in the repo and link it from the package README placeholder.

6. **Validate**
   - Run `dotnet build` and targeted tests to confirm removal did not break supported packages.
   - Confirm CI pack/test steps skip the removed package.

## Artifacts to touch (typical)

- Package README: banner + deprecation note or placeholder.
- Root `README.md`: short note and link to alternative.
- `DEPRECATED.md`: status and guidance.
- Solution file and CI config: remove project, pack, and test entries.
- Optional: SECURITY/advisory file with "won't fix" language.

## Templates

**Placeholder README snippet (in the removed package folder):**

```markdown
# <PackageName> (removed)

This package was removed from the default branch because it is deprecated and contains known vulnerabilities. It is unmaintained and should not be used. See <AlternativePackage> instead.

Last code version is preserved at tag: archive/<package>-YYYY-MM-DD.
```

**Release notes snippet for the final/last package version:**

```text
Deprecated and insecure. This package is unmaintained and contains known vulnerabilities. Do not use. Migrate to <AlternativePackage>.
```

## Quick command hints

- Tag the last commit before removal:

```sh
git tag archive/<package>-YYYY-MM-DD
```


- Remove a project from the solution (example):

```sh
dotnet sln src/NLog.Extensions.AzureStorage.sln remove src/<Project>/<Project>.csproj
```

## Checklist for each deprecation

- [ ] Banner in package README + root README note
- [ ] Solution/CI pack/test entries removed
- [ ] NuGet versions unlisted or final deprecated version published with clear notes
- [ ] Placeholder README in default branch, code removed
- [ ] Tag created for last code commit (and archive branch if required)
- [ ] Advisory/SECURITY note added when security-driven
- [ ] Build/tests rerun to verify unaffected packages
