---
title: Build configuration
description: Use the Roslyn and C# version constants, and configure the embedded attribute and the warnings of the helpers.
---

## Roslyn and C# version constants

Before the compilation, the package reads the version of the `Microsoft.CodeAnalysis` package that the project references
and defines constants for it, so the same code can target several versions of Roslyn:

| Constant | Defined when the Roslyn version is at least |
| --- | --- |
| `ROSLYN_<version>_OR_GREATER`, such as `ROSLYN_4_8_OR_GREATER` or `ROSLYN_4_9_2_OR_GREATER` | That version, for the Roslyn releases from 1.0 to 5.9. |
| `CSHARP9_OR_GREATER` | 4.2 |
| `CSHARP10_OR_GREATER` | 4.4 |
| `CSHARP11_OR_GREATER` | 4.6 |
| `CSHARP12_OR_GREATER` | 4.8 |
| `CSHARP13_OR_GREATER` | 4.14 |
| `CSHARP14_OR_GREATER` | 5.0 |
| `CSHARP15_OR_GREATER` | 5.6 |
| `ROSLYN_WORKSPACES` | Any version, when the project references a workspaces package. |

Use them in `#if` directives around the code that calls newer APIs. The `CSHARP*` constants say which C# versions the
referenced compiler APIs know, such as `LanguageVersion.CSharp12`, not the C# version of your project.

The version is read from the `PackageReference` of `Microsoft.CodeAnalysis.CSharp`, then `Microsoft.CodeAnalysis.Common`,
`Microsoft.CodeAnalysis.Workspaces.Common` and `Microsoft.CodeAnalysis.CSharp.Workspaces`, then from the `PackageVersion`
items of central package management. `ROSLYN_WORKSPACES` is defined when the project references
`Microsoft.CodeAnalysis.Workspaces.Common`, `Microsoft.CodeAnalysis.CSharp.Workspaces`,
`Microsoft.CodeAnalysis.VisualBasic.Workspaces` or `Microsoft.CodeAnalysis.Workspaces.MSBuild`.

To define the constants yourself, disable this feature:

```xml
<PropertyGroup>
  <MeziantouFrameworkRoslynDefineConstants>false</MeziantouFrameworkRoslynDefineConstants>
</PropertyGroup>
```

## Embedded types

All the types of the package are marked with `[Microsoft.CodeAnalysis.Embedded]`, so the compiler hides them from the
other assemblies. Two assemblies that use the package and see each other's internals through `[InternalsVisibleTo]` do
not get `CS0436` warnings about duplicate types.

The package declares `Microsoft.CodeAnalysis.EmbeddedAttribute` as an `internal sealed partial class` without any
attribute, so it merges with the declarations that other source generators or packages emit. If your project declares
the attribute in a way that conflicts, define one of these constants:

| Constant | Effect |
| --- | --- |
| `MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE_DECLARATION` | The types are still marked as embedded, but the project must declare the attribute. |
| `MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE` | Neither declares the attribute nor marks the types with it. |

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE_DECLARATION</DefineConstants>
</PropertyGroup>
```

## Compiler warnings

The helper files suppress all the compiler warnings, so they do not add warnings to your build, whatever its rules.
Define `MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS` to report them:

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS</DefineConstants>
</PropertyGroup>
```
