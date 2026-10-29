---
title: Custom rules
description: Write a rule that enforces your own conventions, and run it with the built-in rules.
---

A rule derives from `NuGetPackageValidationRule` and overrides `ExecuteAsync`. This one checks that the id of the
package starts with a prefix:

<!-- ref:../../tests/Meziantou.Framework.NuGetPackageValidation.Tests/NuGetPackageValidatorTests.cs;region=NuGetPackageValidation_CustomRuleDefinition;format=md-fenced-code-block;dedent=true -->
```csharp
// Reports an error when the id of the package does not start with the prefix of the company
private sealed class PackageIdPrefixRule(string prefix) : NuGetPackageValidationRule
{
    public const int ErrorCode = 1001; // Outside the ranges of the built-in error codes

    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var id = context.Package.NuspecReader.GetId();
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
        {
            context.ReportError(ErrorCode, $"The package id '{id}' does not start with '{prefix}'", helpText: $"Rename the package to '{prefix}{id}'");
        }

        return Task.CompletedTask;
    }
}
```
<!-- endref -->

Add an instance to the options, with the built-in rules you want:

<!-- ref:../../tests/Meziantou.Framework.NuGetPackageValidation.Tests/NuGetPackageValidatorTests.cs;region=NuGetPackageValidation_CustomRuleUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var packagePath = FullPath.FromPath(AppContext.BaseDirectory) / "Packages" / "Release_Author.1.0.0.nupkg";

var options = new NuGetPackageValidationOptions();
options.Rules.Add(NuGetPackageValidationRules.AuthorMustBeSet);
options.Rules.Add(new PackageIdPrefixRule("Contoso."));

var result = await NuGetPackageValidator.ValidateAsync(packagePath, options);
NuGetPackageValidationError error = Assert.Single(result.Errors);
Assert.Equal(PackageIdPrefixRule.ErrorCode, error.ErrorCode);
Assert.Equal("The package id 'Release_Author' does not start with 'Contoso.'", error.Message);
```
<!-- endref -->

## The validation context

`ExecuteAsync` receives a `NuGetPackageValidationContext`:

| Member | Use |
| --- | --- |
| `Package` | The reader of the package: its metadata (`NuspecReader`), its files and their content |
| `SymbolPackage` | The reader of the `.snupkg` file next to the package, or `null` when there is none |
| `PackagePath` | The path of the package |
| `SymbolServers` | The symbol servers of the options, as URLs that end with `/` |
| `CancellationToken` | The token passed to `ValidateAsync` |
| `ReportError` | Reports an error, with an optional help text and file name |
| `IsRuleExcluded` | Whether an error code is excluded, to skip expensive work whose errors would be ignored |

- `ReportError` ignores the excluded codes, so a rule does not have to check them before reporting.
- Use codes that the built-in rules do not use. The built-in codes are below 200: see [Error codes](04-ErrorCodes.md).
- The rules run one after the other, in the order of the `Rules` set, which is not guaranteed. Do not make a rule depend
  on another one.
- The package and its symbol package are disposed when the validation ends: do not keep them.

The command-line tool only runs the built-in rules. To run a custom rule, call the library from your own program.
