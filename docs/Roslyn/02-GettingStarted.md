---
title: Getting started
description: Install the package in an analyzer or source generator project, and import the helper namespace.
---

## Install the package

Add the package to an analyzer, code fix or source generator project that already references the Roslyn packages, with
`PrivateAssets="all"`:

```xml
<ItemGroup>
  <PackageReference Include="Meziantou.Framework.Roslyn" Version="x.y.z" PrivateAssets="all" />
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.6.0" PrivateAssets="all" />
</ItemGroup>
```

The package contains no assembly: its source files are compiled into your project. They compile with
`Microsoft.CodeAnalysis` 4.8 and later, and use the APIs of newer versions when they are available, thanks to
[compilation constants](07-BuildConfiguration.md#roslyn-and-c-version-constants).

## Import the namespace

The helpers are extension methods and types of the `Meziantou.Framework.Roslyn` namespace:

<!-- ref:../../tests/Meziantou.Framework.Roslyn.Tests/RoslynHelperTests.cs;region=Roslyn_Using;format=md-fenced-code-block;dedent=true -->
```csharp
using Meziantou.Framework.Roslyn;
```
<!-- endref -->

## Use the helpers

The types are `internal`: they belong to your assembly, and do not conflict with the copies compiled into other
assemblies. A test project that sees your internals through `[InternalsVisibleTo]` and also uses the package does not get
`CS0436` warnings either: see [Build configuration](07-BuildConfiguration.md#embedded-types).

<!-- ref:../../tests/Meziantou.Framework.Roslyn.Tests/RoslynHelperTests.cs;region=Roslyn_IsOrInheritsFrom;format=md-fenced-code-block;dedent=true -->
```csharp
var compilation = CSharpCompilation.Create(
    "Sample",
    [CSharpSyntaxTree.ParseText("public class Base { } public class Derived : Base { }")],
    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
var baseType = compilation.GetTypeByMetadataName("Base")!;
var type = compilation.GetTypeByMetadataName("Derived")!;

// true when the type is Base or derives from it
Assert.True(type.IsOrInheritsFrom(baseType));
Assert.True(baseType.IsOrInheritsFrom(baseType));
Assert.False(baseType.IsOrInheritsFrom(type));
```
<!-- endref -->

## Next steps

- [Symbols and types](03-SymbolsAndTypes.md): type relationships and attributes.
- [Reporting diagnostics](06-ReportingDiagnostics.md): the `ReportDiagnostic` extension methods.
