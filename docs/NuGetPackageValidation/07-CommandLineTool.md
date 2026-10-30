---
title: Command-line tool
description: Validate packages from a terminal or a script, choose the rules, ignore errors and read the JSON output.
---

`Meziantou.Framework.NuGetPackageValidation.Tool` runs the [rules](03-Rules.md) of the library on the packages you give
it.

## Install the tool

Install or update it as a global tool:

```bash
dotnet tool update Meziantou.Framework.NuGetPackageValidation.Tool --global
```

The command is `meziantou.validate-nuget-package`. To pin the version in a repository, install it as a local tool
(`dotnet new tool-manifest`, then `dotnet tool install Meziantou.Framework.NuGetPackageValidation.Tool`) and run it with
`dotnet tool run meziantou.validate-nuget-package`.

## Validate packages

Pass one or more package paths:

```bash
meziantou.validate-nuget-package artifacts/MyLibrary.1.0.0.nupkg artifacts/MyLibrary.Tool.1.0.0.nupkg
```

- The packages are validated in parallel, up to the number of processors. `--max-parallelism 1` validates one package
  at a time.
- A path given twice is validated once.
- The tool does not expand wildcards: let your shell do it, for instance `artifacts/*.nupkg`.
- A `.snupkg` file next to a package is used to check its symbols.

## Read the output

The result is written to the standard output as JSON. The packages are keyed by their full path, in the order of the
arguments:

```json
{
  "IsValid": false,
  "Packages": {
    "/home/user/src/artifacts/Debug.1.0.0.nupkg": {
      "Errors": [
        {
          "ErrorCode": 81,
          "Message": "Assembly is not optimized",
          "HelpText": "Build the package using the Release configuration: 'dotnet pack --configuration Release'. ...",
          "FileName": "lib/net6.0/samplelib.dll"
        }
      ],
      "IsValid": false
    }
  }
}
```

- The exit code is `0` when every package is valid, and `1` otherwise. It is also `1` when the arguments are invalid,
  such as an unknown rule.
- `--only-report-errors` leaves the valid packages out of the output, which keeps the log of a large build short.
- [HelpText](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.HelpText) and [FileName](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.FileName) are `null` when the error has none.

## Choose the rules

Without options, the tool applies the [default rules](03-Rules.md#default-rules). `--rules` replaces them, and
`--excluded-rules` removes rules. Both take the rule names of [NuGetPackageValidationRules](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationRules), separated by `,` or `;`, and
`default` stands for the default rules:

```bash
meziantou.validate-nuget-package MyLibrary.1.0.0.nupkg --rules default,PackageIdAvailableOnNuGetOrg
```

```bash
meziantou.validate-nuget-package MyLibrary.1.0.0.nupkg --excluded-rules ReadmeMustBeSet,TagsMustBeSet
```

The names are case-sensitive, as written in `--help`.

## Ignore some errors

`--excluded-rule-ids` ignores [error codes](04-ErrorCodes.md) without removing their rule, like [ExcludedRuleIds](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationOptions.ExcludedRuleIds) in
code:

```bash
meziantou.validate-nuget-package MyLibrary.1.0.0.nupkg --excluded-rule-ids 52,119
```

Excluding `119` or `120` also skips the download of the source files of the symbols. See
[Ignore some errors](05-ConfiguringValidation.md#ignore-some-errors).

## Authenticate the requests

The [Symbols](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationRules.Symbols) rule downloads the source files that Source Link points to. For a private GitHub repository, these
downloads need a token:

```bash
meziantou.validate-nuget-package MyLibrary.1.0.0.nupkg --github-token "$GITHUB_TOKEN"
```

The token is only sent to `raw.githubusercontent.com` and its subdomains, as a bearer token. Requests to other hosts
are sent without it.

## Options

| Option | Description |
| --- | --- |
| `<package-path>...` | The packages to validate (required) |
| `--rules <rules>` | The rules to apply instead of the default rules |
| `--excluded-rules <rules>` | The rules to remove |
| `--excluded-rule-ids <ids>` | The error codes to ignore, separated by `,` or `;` |
| `--github-token <token>` | The token to send to `raw.githubusercontent.com` |
| `--only-report-errors` | Only write the packages that have errors |
| `--max-parallelism <n>` | The maximum number of packages validated at the same time. Must be greater than 0 |

`meziantou.validate-nuget-package --help` shows the same list with the names of all the rules.
