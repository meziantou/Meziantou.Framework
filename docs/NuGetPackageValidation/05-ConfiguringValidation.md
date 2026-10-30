---
title: Configuring the validation
description: Choose the rules, ignore some error codes, change the symbol servers and configure the HTTP requests.
---

`NuGetPackageValidator.ValidateAsync` has three overloads: the default rules, a list of rules, or a
`NuGetPackageValidationOptions` for everything else.

## Choose the rules

Pass the rules to apply. The other rules do not run:

<!-- ref:../../tests/Meziantou.Framework.NuGetPackageValidation.Tests/NuGetPackageValidatorTests.cs;region=NuGetPackageValidator_CustomRules;format=md-fenced-code-block;dedent=true -->
```csharp
var packagePath = FullPath.FromPath(AppContext.BaseDirectory) / "Packages" / "Release_Author.1.0.0.nupkg";

// Validate with specific rules only
var result = await NuGetPackageValidator.ValidateAsync(
    packagePath,
    [
        NuGetPackageValidationRules.AuthorMustBeSet,
        NuGetPackageValidationRules.LicenseMustBeSet,
        NuGetPackageValidationRules.IconMustBeSet,
    ]);

Assert.False(result.IsValid);
Assert.Equal([21, 32], result.Errors.Select(error => error.ErrorCode).Order()); // LicenseNotSet, IconNotSet
```
<!-- endref -->

To add a rule to the default ones, such as `PackageIdAvailableOnNuGetOrg` before the first release of a package, add
`NuGetPackageValidationRules.Default` and the rule to `NuGetPackageValidationOptions.Rules`. A new
`NuGetPackageValidationOptions` has no rules: `NuGetPackageValidationOptions.Default` returns new options that have the
default rules. `Rules` is a set, so adding a rule twice runs it once.

## Ignore some errors

A rule can report several errors. To keep a rule but ignore one of its errors, add the
[error code](04-ErrorCodes.md) to `ExcludedRuleIds`:

<!-- ref:../../tests/Meziantou.Framework.NuGetPackageValidation.Tests/NuGetPackageValidatorTests.cs;region=NuGetPackageValidation_ExcludeErrorCodes;format=md-fenced-code-block;dedent=true -->
```csharp
var packagePath = FullPath.FromPath(AppContext.BaseDirectory) / "Packages" / "Release_Author.1.0.0.nupkg";

var options = new NuGetPackageValidationOptions();
options.Rules.Add(NuGetPackageValidationRules.LicenseMustBeSet);
options.Rules.Add(NuGetPackageValidationRules.IconMustBeSet);

// The rule still runs, but it does not report this error
options.ExcludedRuleIds.Add(32); // IconNotSet

var result = await NuGetPackageValidator.ValidateAsync(packagePath, options);
Assert.Equal(21, Assert.Single(result.Errors).ErrorCode); // LicenseNotSet
```
<!-- endref -->

- An excluded code is never reported, including codes `1` and `2` for a missing or unreadable package.
- The rule still runs. To skip its work, remove the rule instead. The exception is the `Symbols` rule: excluding `119`
  (UrlIsNotAccessible) or `120` (FileHashIsNotValid) skips the download of the source files.

## Symbol servers

When the symbols of an assembly are neither in the package nor in the `.snupkg` file next to it, the `Symbols` rule asks
the symbol servers of `SymbolServers`, in order. The default servers are the Microsoft and the nuget.org symbol servers.
Clear the list to only accept the symbols that the packages contain, or add the server where you publish your symbols:

<!-- ref:../../samples/Meziantou.Framework.NuGetPackageValidation.Samples/ReadmeExamples.cs;region=NuGetPackageValidator_CustomOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new NuGetPackageValidationOptions();

// Add default rules
foreach (var rule in NuGetPackageValidationRules.Default)
{
    options.Rules.Add(rule);
}

// Exclude specific error codes
options.ExcludedRuleIds.Add(32); // IconNotSet

// Configure symbol servers
options.SymbolServers.Clear();
options.SymbolServers.Add("https://msdl.microsoft.com/download/symbols/");
options.SymbolServers.Add("https://symbols.nuget.org/download/symbols/");

// Configure HTTP requests (e.g., add authentication)
options.ConfigureRequest = request =>
{
    request.Headers.Add("X-Custom-Header", "value");
};

var result = await NuGetPackageValidator.ValidateAsync(FullPath.FromPath("MyPackage.1.0.0.nupkg"), options);
```
<!-- endref -->

A trailing `/` is added to a server URL that has none, and empty entries are ignored.

## Configure the HTTP requests

`ConfigureRequest` is called for every HTTP request that the rules send: the project and repository URLs, the source
files of the symbols, the symbol servers, and nuget.org. Use it to authenticate the requests, as in the example above.
The callback receives every request, so check `RequestUri` before adding a credential, so that a token is only sent to
the host it is for.

The command-line tool does this with `--github-token`: it adds the token to the requests to `raw.githubusercontent.com`
only. See [Command-line tool](07-CommandLineTool.md#authenticate-the-requests).

## Cancel a validation

Each `ValidateAsync` overload takes a `CancellationToken`, which the rules pass to their HTTP requests. Use it to stop a
validation that waits on a slow server: a canceled validation throws an `OperationCanceledException` rather than
reporting errors.
