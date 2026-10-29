---
title: Error codes
description: The list of the error codes, with the rule that reports each of them.
---

Each error has a numeric code. Use it to [ignore an error](05-ConfiguringValidation.md#ignore-some-errors) with
`ExcludedRuleIds` or the `--excluded-rule-ids` option of the tool. The codes are grouped by tens, one group per topic.
[Rules](03-Rules.md) explains how to fix each of them.

| Code | Name | Reported by |
| --- | --- | --- |
| 1 | FileNotFound | The validator: the package file does not exist |
| 2 | InvalidPackage | The validator: the file cannot be read as a NuGet package |
| 11 | AuthorNotSet | AuthorMustBeSet |
| 12 | DefaultAuthorSet | AuthorMustBeSet |
| 21 | LicenseNotSet | LicenseMustBeSet |
| 22 | UseDeprecatedLicenseUrl | LicenseMustBeSet |
| 23 | LicenseFileNotFound | LicenseMustBeSet |
| 31 | UseDeprecatedIconUrl | IconMustBeSet |
| 32 | IconNotSet | IconMustBeSet |
| 33 | IconNotFound | IconMustBeSet |
| 34 | IconFileTooLarge | IconMustBeSet |
| 35 | IconFileFormatNotSupported | IconMustBeSet |
| 36 | IconFileInvalidExtension | IconMustBeSet |
| 41 | UseDeprecatedSummary | DescriptionMustBeSet |
| 42 | DescriptionNotSet | DescriptionMustBeSet |
| 43 | PackageHasDefaultDescription | DescriptionMustBeSet |
| 44 | PackageDescriptionIsTooLong | DescriptionMustBeSet |
| 51 | ProjectUrlNotSet | ProjectUrlMustBeSet |
| 52 | ProjectUrlNotAccessible | ProjectUrlMustBeSet, for the project URL and the repository URL |
| 61 | ReadmeNotSet | ReadmeMustBeSet |
| 62 | ReadmeFileNotFound | ReadmeMustBeSet |
| 71 | RepositoryNotSet | RepositoryMustBeSet |
| 72 | RepositoryTypeNotSet | RepositoryMustBeSet |
| 73 | RepositoryUrlNotSet | RepositoryMustBeSet |
| 74 | RepositoryCommitNotSet | RepositoryMustBeSet |
| 75 | RepositoryBranchNotSet | RepositoryBranchMustBeSet |
| 81 | AssemblyIsNotOptimized | AssembliesMustBeOptimized |
| 91 | CannotCheckPackageIdExistsOnNuGetOrg | PackageIdAvailableOnNuGetOrg |
| 92 | PackageIdExistsOnNuGetOrg | PackageIdAvailableOnNuGetOrg |
| 101 | XmlDocumentationNotFound | XmlDocumentationMustBePresent |
| 111 | SymbolsNotFound | Symbols |
| 112 | NonDeterministic | Symbols |
| 113 | SourceFileNotAccessible | Symbols |
| 114 | CompilerFlagsNotPresent | Symbols |
| 115 | InvalidCompilerVersion | Symbols |
| 116 | CompilerDoesNotSupportReproducibleBuilds | Symbols |
| 117 | FullPdb | Symbols |
| 118 | PdbDoesNotMatchAssembly | Symbols |
| 119 | UrlIsNotAccessible | Symbols |
| 120 | FileHashIsNotValid | Symbols |
| 121 | FileHashIsNotProvided | Symbols |
| 122 | NotSupportedHashAlgorithm | Symbols |
| 131 | TagsNotSet | TagsMustBeSet |
| 132 | TagsTooLong | TagsMustBeSet |

The names identify the errors in this documentation. The library exposes the codes only as numbers: in code, write the
number with the name in a comment, as the examples do.
