---
title: Running in CI
description: Fail a build when a package does not follow the rules, and plan for the network access the rules need.
---

Validate the packages after `dotnet pack` and before you publish them: the tool exits with a non-zero code when a
package is not valid, which fails the step.

## Validate the packages of a build

Pack in Release, so that the assemblies are optimized, then validate every package of the output folder:

```bash
dotnet pack --configuration Release --output artifacts
```

```bash
dotnet tool update Meziantou.Framework.NuGetPackageValidation.Tool --global
```

```bash
meziantou.validate-nuget-package artifacts/*.nupkg --only-report-errors
```

- Pass the `.nupkg` files only. A `.snupkg` file next to a package is found by the package's name.
- `--only-report-errors` keeps the log of a build with many packages short.
- On GitHub Actions, pass the token of the workflow with `--github-token "$GITHUB_TOKEN"` when the repository is
  private, so that the source files of the symbols can be downloaded. See
  [Authenticate the requests](07-CommandLineTool.md#authenticate-the-requests).

## Choose what the build enforces

Start with the default rules, then remove what does not apply to your packages rather than ignoring the whole report.
For instance, packages that do not ship a readme or tags, and whose project URL is not public:

```bash
meziantou.validate-nuget-package artifacts/*.nupkg --excluded-rules ReadmeMustBeSet,TagsMustBeSet --excluded-rule-ids 52 --only-report-errors
```

Use `--excluded-rules` to turn off a rule, and `--excluded-rule-ids` to ignore one of its errors and keep the others.
See [Rules](03-Rules.md) for what each rule checks.

## Network access

Some rules send HTTP requests:

| Rule | Requests |
| --- | --- |
| `ProjectUrlMustBeSet` | `GET` the project URL, and the repository URL when there is one |
| `Symbols` | `GET` the source files that Source Link maps to a URL, and query the symbol servers when the symbols are not in the package or its `.snupkg` file |
| `PackageIdAvailableOnNuGetOrg` | Query nuget.org |

The other rules only read the package. On a build agent without internet access, or to avoid failures caused by a
remote server:

- Exclude `52` to stop requiring the project and repository URLs to answer.
- Exclude `119` or `120` to skip the download of the source files.
- Embed the symbols, or publish a `.snupkg` file next to the package, so that the symbol servers are not queried. In
  code, you can also clear `SymbolServers`. See [Symbol servers](05-ConfiguringValidation.md#symbol-servers).

An error on a remote server is reported as an error of the package (`52`, `91`, `111` or `119`), not as a failure of
the tool, so a temporary outage fails the validation. Rerun the step, or exclude the codes of the checks your build
does not need.
