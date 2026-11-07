---
title: Getting started
description: Validate a package with the command-line tool or with the library, and read the errors.
---

## Validate from the command line

Install the tool, pack your project, and validate the package it produces:

```bash
dotnet tool update Meziantou.Framework.NuGetPackageValidation.Tool --global
```

```bash
dotnet pack --configuration Release --output artifacts
```

```bash
meziantou.validate-nuget-package artifacts/MyLibrary.1.0.0.nupkg
```

The tool applies the [default rules](03-Rules.md), writes the errors to the standard output as JSON, and exits with a
non-zero code when a package is not valid. See [Command-line tool](07-CommandLineTool.md) for its options.

## Validate from code

Add the `Meziantou.Framework.NuGetPackageValidation` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.NuGetPackageValidation" />

The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework;` and `using Meziantou.Framework.NuGetPackageValidation;`.

Pass the path of the package to [NuGetPackageValidator.ValidateAsync](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidator.ValidateAsync). Without other arguments, it applies the default
rules:

<!-- ref:../../samples/Meziantou.Framework.NuGetPackageValidation.Samples/ReadmeExamples.cs;region=NuGetPackageValidator_BasicValidation;format=md-fenced-code-block;dedent=true -->
```csharp
// Validate a package with default rules
var result = await NuGetPackageValidator.ValidateAsync(FullPath.FromPath("MyPackage.1.0.0.nupkg"));

if (result.IsValid)
{
    Console.WriteLine("Package is valid!");
}
else
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"{error.ErrorCode}: {error.Message}");
        if (error.HelpText is not null)
        {
            Console.WriteLine($"  Help: {error.HelpText}");
        }
    }
}
```
<!-- endref -->

- [IsValid](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationResult.IsValid) is `true` when the package has no error.
- [Errors](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationResult.Errors) lists the problems found. A missing file or a file that is not a NuGet package is reported as an error too
  (error codes `1` and `2`), not as an exception.
- When a `.snupkg` file with the same name is next to the package, the symbol rule reads the symbols from it.

## Read an error

Each error has a code, a message, and when available the file of the package it is about and how to fix it:

<!-- ref:../../tests/Meziantou.Framework.NuGetPackageValidation.Tests/NuGetPackageValidatorTests.cs;region=NuGetPackageValidation_ReadErrors;format=md-fenced-code-block;dedent=true -->
```csharp
var packagePath = FullPath.FromPath(AppContext.BaseDirectory) / "Packages" / "Debug.1.0.0.nupkg";
var result = await NuGetPackageValidator.ValidateAsync(packagePath, [NuGetPackageValidationRules.AssembliesMustBeOptimized]);

Assert.False(result.IsValid);
NuGetPackageValidationError error = Assert.Single(result.Errors);
Assert.Equal(81, error.ErrorCode); // AssemblyIsNotOptimized
Assert.Equal("Assembly is not optimized", error.Message);

// The file of the package the error is about, and how to fix the error
Assert.Equal("lib/net6.0/samplelib.dll", error.FileName);
Assert.StartsWith("Build the package using the Release configuration", error.HelpText, StringComparison.Ordinal);

Assert.Equal("81 - lib/net6.0/samplelib.dll: Assembly is not optimized", error.ToString());
```
<!-- endref -->

[ErrorCode](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.ErrorCode) identifies the problem: [Error codes](04-ErrorCodes.md) lists them all. [HelpText](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.HelpText) and [FileName](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationError.FileName) are `null`
when the rule has nothing to add.

## Next steps

- [Rules](03-Rules.md): what each rule checks and how to fix its errors.
- [Configuring the validation](05-ConfiguringValidation.md): choose the rules and ignore errors.
