---
title: Rules
description: What each validation rule checks, the errors it reports, and how to fix them.
---

A rule checks one aspect of a package and reports one or more [error codes](04-ErrorCodes.md). The rules are the
properties of [NuGetPackageValidationRules](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationRules), and the command-line tool takes the same names.

## Default rules

[NuGetPackageValidationRules.Default](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationRules.Default) holds the rules that apply to most packages. [ValidateAsync](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidator.ValidateAsync) and the command-line
tool use them when you do not choose the rules.

### AssembliesMustBeOptimized

Checks that the `.dll` and `.exe` files of the package are built with optimizations, as in the Release configuration.
Files that are not .NET assemblies are skipped.

- `81` AssemblyIsNotOptimized: pack with `dotnet pack --configuration Release`, or set `Optimize` to `true` in the
  project.

### AuthorMustBeSet

Checks the `authors` metadata.

- `11` AuthorNotSet: set the `Authors` property of the project.
- `12` DefaultAuthorSet: the authors are the package id, a sign that `Authors` was not set explicitly.

### DescriptionMustBeSet

Checks the `description` metadata.

- `42` DescriptionNotSet: set the `Description` property of the project.
- `43` PackageHasDefaultDescription: the description is the `Package Description` placeholder.
- `44` PackageDescriptionIsTooLong: the description is longer than 4,000 characters.
- `41` UseDeprecatedSummary: the package uses the deprecated `summary` metadata.

### IconMustBeSet

Checks that the package embeds its icon.

- `32` IconNotSet: the package has no icon. Add an image to the package and set the `PackageIcon` property.
- `31` UseDeprecatedIconUrl: the package only has the deprecated `iconUrl` metadata. A package can have both `icon` and
  `iconUrl`.
- `33` IconNotFound: the icon file is not in the package.
- `35` IconFileFormatNotSupported: the icon is neither a `.png` nor a `.jpg` or `.jpeg` file.
- `36` IconFileInvalidExtension: the content of the file does not match its extension, for instance a PNG image named
  `icon.jpg`.
- `34` IconFileTooLarge: the icon is larger than 1 MB.

### LicenseMustBeSet

Checks that the package declares its license with a license expression or a license file.

- `21` LicenseNotSet: set the `PackageLicenseExpression` property, or add a file and set `PackageLicenseFile`.
- `22` UseDeprecatedLicenseUrl: the package only has the deprecated `licenseUrl` metadata.
- `23` LicenseFileNotFound: the license file is not in the package.

### ProjectUrlMustBeSet

Checks that the package has a project URL and that it answers. It sends a `GET` request to the project URL, and to the
repository URL when the package has one.

- `51` ProjectUrlNotSet: set the `PackageProjectUrl` property.
- `52` ProjectUrlNotAccessible: the project URL is not an absolute `http` or `https` URL, or the project or repository
  URL does not answer with a success status code. A private repository does not answer anonymous requests: exclude this
  code for such packages.

### ReadmeMustBeSet

Checks that the package embeds a readme.

- `61` ReadmeNotSet: add a Markdown file to the package and set the `PackageReadmeFile` property.
- `62` ReadmeFileNotFound: the readme file is not in the package.

### RepositoryMustBeSet

Checks the `repository` metadata, which links the package to the source code it was built from.

- `71` RepositoryNotSet: the package has no repository metadata.
- `72` RepositoryTypeNotSet, `73` RepositoryUrlNotSet, `74` RepositoryCommitNotSet: the repository metadata misses its
  type, its URL or its commit.

### Symbols

Checks that the debugger can find the symbols of each .NET assembly in `lib` and `analyzers`, and the source files they
point to. Satellite assemblies are skipped. The rule looks for the symbols, in this order:

1. a portable PDB embedded in the assembly;
2. a `.pdb` file next to the assembly in the package;
3. a `.pdb` file at the same path in the `.snupkg` file next to the package;
4. the [symbol servers](05-ConfiguringValidation.md#symbol-servers).

It then reports:

- `111` SymbolsNotFound: no symbols were found. The message lists the symbol servers that could not be queried.
- `117` FullPdb: the symbols are a Windows PDB. Set `DebugType` to `portable` or `embedded`.
- `118` PdbDoesNotMatchAssembly: the symbols are not the ones of this build of the assembly.
- `114` CompilerFlagsNotPresent: the symbols do not record the compiler flags. Build with a .NET SDK 5.0.300 or later.
- `115` InvalidCompilerVersion and `116` CompilerDoesNotSupportReproducibleBuilds: the compiler that built the assembly
  is not recorded correctly, or is too old for reproducible builds.
- `112` NonDeterministic: a source path is a path of the build machine instead of a deterministic `/_/` path. Set
  `ContinuousIntegrationBuild` to `true` on the build server.
- `113` SourceFileNotAccessible: a source file is neither embedded in the symbols nor mapped to a URL by Source Link.
- `119` UrlIsNotAccessible: the URL that Source Link gives for a source file is invalid or does not answer.
- `120` FileHashIsNotValid, `121` FileHashIsNotProvided, `122` NotSupportedHashAlgorithm: the downloaded source file
  is not the one the assembly was compiled from, or its hash cannot be checked.

The rule downloads every source file that Source Link maps to a URL. Exclude `119` or `120` to skip these downloads.

### TagsMustBeSet

Checks the `tags` metadata.

- `131` TagsNotSet: set the `PackageTags` property.
- `132` TagsTooLong: the tags are longer than 4,000 characters.

### XmlDocumentationMustBePresent

Checks that each .NET library in `lib` has its XML documentation file (`MyLibrary.xml` next to `MyLibrary.dll`), so
that IDEs show the documentation of its API. Satellite assemblies and executables are skipped.

- `101` XmlDocumentationNotFound: set `GenerateDocumentationFile` to `true` in the project.

## Additional rules

These rules are not in [Default](cref:Meziantou.Framework.NuGetPackageValidation.NuGetPackageValidationRules.Default): add them explicitly.

### PackageIdAvailableOnNuGetOrg

Checks that no package with the same id exists on nuget.org. Use it before the first release of a package.

- `92` PackageIdExistsOnNuGetOrg: the id is taken.
- `91` CannotCheckPackageIdExistsOnNuGetOrg: nuget.org could not be queried.

### RepositoryBranchMustBeSet

Checks that the repository metadata has a branch.

- `75` RepositoryBranchNotSet: the repository metadata has no branch.
