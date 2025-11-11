# Meziantou.Framework.NuGetPackageValidation

A .NET library for validating NuGet packages to ensure they follow best practices and contain all required metadata and files.

## Usage

This library provides a comprehensive set of validation rules to check NuGet packages (.nupkg) and symbol packages (.snupkg) for common issues, missing metadata, and compliance with NuGet best practices.

### Basic Validation

```csharp
using Meziantou.Framework.NuGetPackageValidation;

// Validate a package with default rules
var result = await NuGetPackageValidator.ValidateAsync("MyPackage.1.0.0.nupkg");

if (result.IsValid)
{
    Console.WriteLine("Package is valid!");
}
else
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"{error.ErrorCode}: {error.Message}");
        if (error.HelpText != null)
        {
            Console.WriteLine($"  Help: {error.HelpText}");
        }
    }
}
```

### Custom Rules

```csharp
// Validate with specific rules only
var result = await NuGetPackageValidator.ValidateAsync(
    "MyPackage.1.0.0.nupkg",
    new[]
    {
        NuGetPackageValidationRules.AuthorMustBeSet,
        NuGetPackageValidationRules.LicenseMustBeSet,
        NuGetPackageValidationRules.IconMustBeSet
    });
```

### Custom Options

```csharp
var options = new NuGetPackageValidationOptions();

// Add default rules
foreach (var rule in NuGetPackageValidationRules.Default)
{
    options.Rules.Add(rule);
}

// Exclude specific error codes
options.ExcludedRuleIds.Add(ErrorCodes.IconNotSet);

// Configure symbol servers
options.SymbolServers.Clear();
options.SymbolServers.Add("https://msdl.microsoft.com/download/symbols/");
options.SymbolServers.Add("https://symbols.nuget.org/download/symbols/");

// Configure HTTP requests (e.g., add authentication)
options.ConfigureRequest = request =>
{
    request.Headers.Add("X-Custom-Header", "value");
};

var result = await NuGetPackageValidator.ValidateAsync("MyPackage.1.0.0.nupkg", options);
```

## Available Validation Rules

### Default Rules

The following rules are included in `NuGetPackageValidationRules.Default`:

- **AssembliesMustBeOptimized** - Ensures assemblies are compiled in Release mode with optimizations enabled
- **AuthorMustBeSet** - Verifies the author metadata is set and not using default values
- **DescriptionMustBeSet** - Checks for a meaningful description (not default placeholder text)
- **IconMustBeSet** - Validates that a package icon is included (not deprecated iconUrl)
- **LicenseMustBeSet** - Ensures license information is provided (expression or file, not deprecated licenseUrl)
- **ProjectUrlMustBeSet** - Verifies a project URL is specified and accessible
- **ReadmeMustBeSet** - Checks that a readme file is included in the package
- **RepositoryMustBeSet** - Validates repository information is present
- **Symbols** - Comprehensive validation of debug symbols (PDB files), including:
  - Symbol files are present (embedded, .pdb, or .snupkg)
  - Deterministic builds are enabled
  - Source Link is configured
  - Portable PDB format is used (not full PDB)
  - Compiler flags are present
  - Source files are accessible or embedded
- **TagsMustBeSet** - Ensures package tags are defined and within length limits
- **XmlDocumentationMustBePresent** - Verifies XML documentation files are included for public APIs

### Additional Rules

These rules are available but not included by default:

- **PackageIdAvailableOnNuGetOrg** - Checks if the package ID is already taken on nuget.org (useful for new packages)
- **RepositoryBranchMustBeSet** - Validates that repository branch information is specified

## Error Codes

Each validation error has a specific error code for easy identification and filtering:

### General Errors (1-10)
- `1` - FileNotFound: Package file not found

### Author Errors (11-20)
- `11` - AuthorNotSet: Author metadata is missing
- `12` - DefaultAuthorSet: Author is set to the default value (same as package ID)

### License Errors (21-30)
- `21` - LicenseNotSet: License information is missing
- `22` - UseDeprecatedLicenseUrl: Using deprecated licenseUrl instead of license expression/file
- `23` - LicenseFileNotFound: License file specified but not found in package

### Icon Errors (31-40)
- `31` - UseDeprecatedIconUrl: Using deprecated iconUrl instead of icon file
- `32` - IconNotSet: No icon specified
- `33` - IconNotFound: Icon file not found in package
- `34` - IconFileTooLarge: Icon file exceeds size limit
- `35` - IconFileFormatNotSupported: Icon file format is not PNG or JPEG
- `36` - IconFileInvalidExtension: Icon file extension doesn't match content

### Description Errors (41-50)
- `41` - UseDeprecatedSummary: Using deprecated summary field
- `42` - DescriptionNotSet: Description is missing
- `43` - PackageHasDefaultDescription: Description is using default placeholder text
- `44` - PackageDescriptionIsTooLong: Description exceeds maximum length

### Project URL Errors (51-60)
- `51` - ProjectUrlNotSet: Project URL is missing
- `52` - ProjectUrlNotAccessible: Project URL is not accessible

### Readme Errors (61-70)
- `61` - ReadmeNotSet: Readme file is not specified
- `62` - ReadmeFileNotFound: Readme file not found in package

### Repository Errors (71-80)
- `71` - RepositoryNotSet: Repository metadata is missing
- `72` - RepositoryTypeNotSet: Repository type not specified
- `73` - RepositoryUrlNotSet: Repository URL not specified
- `74` - RepositoryCommitNotSet: Repository commit hash not specified
- `75` - RepositoryBranchNotSet: Repository branch not specified

### Assembly Errors (81-90)
- `81` - AssemblyIsNotOptimized: Assembly compiled in Debug mode or without optimizations

### Package ID Errors (91-100)
- `91` - CannotCheckPackageIdExistsOnNuGetOrg: Unable to verify if package ID exists
- `92` - PackageIdExistsOnNuGetOrg: Package ID already exists on nuget.org

### XML Documentation Errors (101-110)
- `101` - XmlDocumentationNotFound: XML documentation file not found

### Symbol/PDB Errors (111-130)
- `111` - SymbolsNotFound: Debug symbols not found
- `112` - NonDeterministic: Build is not deterministic
- `113` - SourceFileNotAccessible: Source files not accessible
- `114` - CompilerFlagsNotPresent: Compiler flags not embedded in PDB
- `115` - InvalidCompilerVersion: Compiler version is invalid
- `116` - CompilerDoesNotSupportReproducibleBuilds: Compiler doesn't support reproducible builds
- `117` - FullPdb: Using full PDB format instead of portable PDB
- `118` - PdbDoesNotMatchAssembly: PDB file doesn't match assembly
- `119` - UrlIsNotAccessible: URL referenced in source link is not accessible
- `120` - FileHashIsNotValid: File hash validation failed
- `121` - FileHashIsNotProvided: File hash not provided
- `122` - NotSupportedHashAlgorithm: Hash algorithm not supported

### Tag Errors (131-140)
- `131` - TagsNotSet: Package tags are not set
- `132` - TagsTooLong: Tags exceed the 4000 character limit

## Additional Resources

- [NuGet Package Metadata](https://learn.microsoft.com/en-us/nuget/reference/nuspec?WT.mc_id=DT-MVP-5003978)
- [NuGet Icon Metadata](https://learn.microsoft.com/en-us/nuget/reference/nuspec?WT.mc_id=DT-MVP-5003978#icon)
- [Source Link](https://github.com/dotnet/sourcelink)
- [Deterministic Builds](https://github.com/dotnet/reproducible-builds)
- [Ensuring best practices for NuGet packages](https://www.meziantou.net/ensuring-best-practices-for-nuget-packages.htm)