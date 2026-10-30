---
title: Library
description: Generate API files from your own code, read a structured model of a public API, and format its declarations.
---

The `Meziantou.Framework.PublicApiGenerator` package is the library the MSBuild package and the tool are built on. Use it
directly to generate the API files from your own code, or to inspect a public API without generating files: list the
types and members of an assembly, find a symbol by its documentation ID, compare the builds of several target
frameworks, or format declarations for a documentation site.

```bash
dotnet add package Meziantou.Framework.PublicApiGenerator
```

The package targets .NET 10 and later. All the types are in the [Meziantou.Framework.PublicApiGenerator](cref:Meziantou.Framework.PublicApiGenerator) namespace; the
examples of these pages omit the `using` directive.

The entry point is the static `PublicApi` class:

| Method | Returns |
| --- | --- |
| `PublicApi.Generate` | The API files, in memory. |
| `PublicApi.GenerateToDirectory` | Nothing: it writes the API files to a directory. |
| `PublicApi.ReadAssembly` | A `PublicApiAssembly`: the model of the public API of one assembly. |
| `PublicApi.Aggregate` | A `PublicApiAggregatedAssembly`: the models of the builds of an assembly for several target frameworks, combined. |

`PublicApiFormatter` formats the symbols of the model as C# declarations.

## Pages of this group

- [Generating API files](02-GeneratingFiles.md): generate the files of one assembly or of a multi-targeted library, in
  memory or to a directory.
- [Reading the API model](03-ReadingTheApiModel.md): list the types, members, parameters and attributes of an assembly.
- [Identifying symbols](04-IdentifyingSymbols.md): documentation IDs, identities and metadata tokens, and how to find a
  symbol.
- [Formatting declarations](05-FormattingDeclarations.md): write a symbol as a C# declaration, with the type references
  it contains.
- [Comparing target frameworks](06-ComparingTargetFrameworks.md): find the symbols that only exist in some target
  frameworks, and the declarations that differ.
