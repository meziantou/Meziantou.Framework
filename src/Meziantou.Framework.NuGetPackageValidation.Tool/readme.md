# Meziantou.Framework.NuGetPackageValidation.Tool

`Meziantou.Framework.NuGetPackageValidation.Tool` is a tool to validate local `nupkg` file before pushing them to a server such as nuget.org.
It helps you producing valuable the NuGet package. Best practices for NuGet packages are explained [in this post](https://www.meziantou.net/ensuring-best-practices-for-nuget-packages.htm).

# How to use it

1. Install the tool

    ````bash
    dotnet tool update Meziantou.Framework.NuGetPackageValidation.Tool --global
    ````

2. Run the tool

    ````bash
    meziantou.validate-nuget-package "example.nupkg"
    ````

    If the package is not valid, the program exit with a non-zero value. All errors are written to the standard output in a JSON format.

You can show available options using:

````bash
meziantou.validate-nuget-package --help
````

<!-- help -->
```
Description:
  Validate a NuGet package

Usage:
  meziantou.validate-nuget-package <package-path>... [options]

Arguments:
  <package-path>  Paths to the NuGet packages to validate

Options:
  --rules <rules>                          Available rules: AssembliesMustBeOptimized, AuthorMustBeSet, DescriptionMustBeSet, IconMustBeSet, LicenseMustBeSet, PackageIdAvailableOnNuGetOrg, ProjectUrlMustBeSet, ReadmeMustBeSet, RepositoryMustBeSet, RepositoryBranchMustBeSet, Symbols, TagsMustBeSet, XmlDocumentationMustBePresent
  --excluded-rules <excluded-rules>        Available rules: AssembliesMustBeOptimized, AuthorMustBeSet, DescriptionMustBeSet, IconMustBeSet, LicenseMustBeSet, PackageIdAvailableOnNuGetOrg, ProjectUrlMustBeSet, ReadmeMustBeSet, RepositoryMustBeSet, RepositoryBranchMustBeSet, Symbols, TagsMustBeSet, XmlDocumentationMustBePresent
  --excluded-rule-ids <excluded-rule-ids>  List of rule ids to exclude from analysis
  --github-token <github-token>            GitHub token to authenticate requests
  --only-report-errors                     Only report errors on the output
  -?, -h, --help                           Show help and usage information
  --version                                Show version information
```
<!-- help -->