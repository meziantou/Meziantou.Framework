---
title: Comparing target frameworks
description: Combine the builds of an assembly for several target frameworks, and find the symbols and declarations that differ between them.
---

## Aggregate the builds

`PublicApi.Aggregate` combines the models of the builds of an assembly for several target frameworks. Symbols are
matched by documentation ID: each aggregated symbol lists the target frameworks that declare it and its distinct
declarations (its variants).

<!-- ref:../../../samples/Meziantou.Framework.PublicApiGenerator.Samples/ReadmeSnippets.cs;region=PublicApi_Aggregate;format=md-fenced-code-block;dedent=true -->
```csharp
var aggregate = PublicApi.Aggregate(
[
    PublicApi.ReadAssembly("/path/to/netstandard2.0/MyLibrary.dll", new PublicApiReadOptions { TargetFramework = "netstandard2.0" }),
    PublicApi.ReadAssembly("/path/to/net10.0/MyLibrary.dll", new PublicApiReadOptions { TargetFramework = "net10.0" }),
]);

foreach (var type in aggregate.Types)
{
    foreach (var member in type.Members)
    {
        if (!member.IsAvailableInAllTargetFrameworks)
            Console.WriteLine($"{member.DocumentationId} is only available in {string.Join(", ", member.TargetFrameworks)}");

        if (member.Differences != PublicApiSymbolDifferences.None) // e.g. Signature, Nullability, Constraints, Attributes, Inheritance
        {
            foreach (var variant in member.Variants)
                Console.WriteLine($"{string.Join(", ", variant.TargetFrameworks)}: {PublicApiFormatter.Format(variant.Symbol)}");
        }
    }
}
```
<!-- endref -->

- Read each build with [`PublicApi.ReadAssembly`](03-ReadingTheApiModel.md#read-an-assembly), with its target framework.
- The assemblies must have the same name and distinct target frameworks; otherwise `Aggregate` throws an
  `ArgumentException`.
- `Types` lists the top-level types; each `PublicApiAggregatedSymbol` has its `Members` and `NestedTypes`.
  `FindSymbolByDocumentationId` finds a symbol, and `GetAssembly` returns the model of one target framework.

## What differs

- `IsAvailableInAllTargetFrameworks` is `false` for a symbol that some target frameworks do not declare, and
  `TargetFrameworks` lists the ones that do.
- `Differences` tells what differs between the declarations of the symbol: its kind, accessibility, modifiers,
  signature, nullability, constraints, attributes, inheritance, value or parameter names (`PublicApiSymbolDifferences`
  is a flags enum). It is `None` when all the target frameworks that declare the symbol share the same declaration.
- `Variants` lists the distinct declarations. Each variant has the target frameworks that share it and keeps the symbol
  of each of them, with its own metadata provenance; its `Symbol`, the symbol of its first target framework, is the one
  to format. `GetSymbol` on the aggregated symbol returns the symbol of one target framework.

Declarations are compared using the structured model: two symbols with the same documentation ID belong to different
variants when their return types, nullable annotations, constraints, modifiers, attributes, constant values or base
types differ.

To write the merged API as C# files, with `#if` blocks for these differences, use
[`PublicApi.Generate`](02-GeneratingFiles.md#multi-targeted-libraries) instead.
