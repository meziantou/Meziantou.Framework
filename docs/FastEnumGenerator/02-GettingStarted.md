---
title: Getting started
description: Install the package, list an enum in FastEnumAttribute, and call the generated methods.
---

## Install the package

Add the `Meziantou.Framework.FastEnumGenerator` package to the project that declares the attribute:

<InstallNuGetPackage Id="Meziantou.Framework.FastEnumGenerator" />

The package contains the source generator, the analyzer and its code fixes. The generator also adds
`FastEnumAttribute` to the project, in the `Meziantou.Framework.Annotations` namespace. When the project enables implicit
usings (`<ImplicitUsings>enable</ImplicitUsings>`), the package adds `global using Meziantou.Framework.Annotations;`;
otherwise, add the `using` directive yourself.

## Choose the enums

Add one `FastEnumAttribute` per enum, at the assembly level. It can be in any file of the project:

<!-- ref:../../samples/Meziantou.Framework.FastEnumGenerator.Samples/Color.cs;region=FastEnumGenerator_Declaration;format=md-fenced-code-block;dedent=true -->
```csharp
[assembly: FastEnumAttribute(typeof(Sample.Color), IsPublic = true, ExtensionMethodNamespace = "Sample.Extensions")]

namespace Sample;

public enum Color
{
    Blue,
    Red,
    Green,
}
```
<!-- endref -->

The attribute takes the type of the enum and two optional properties:

| Property | Default | Effect |
| --- | --- | --- |
| `ExtensionMethodNamespace` | The namespace of the enum | Namespace of the generated class. |
| `IsPublic` | `true` | Makes the generated methods `public` when the enum is visible outside the assembly. `false` keeps them `internal`. |

The generated methods of a `public` enum are `public` by default, so other assemblies can call them. They are always
`internal` when the enum is `internal`, whatever `IsPublic` says.

## Call the generated methods

The generated methods are extension methods of a class in the namespace of the enum, or in `ExtensionMethodNamespace`.
Call sites need that namespace in scope:

<!-- ref:../../samples/Meziantou.Framework.FastEnumGenerator.Samples/Program.cs;region=FastEnumGenerator_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using Sample.Extensions; // only needed when ExtensionMethodNamespace is used

var name = Color.Blue.ToStringFast();
```
<!-- endref -->

The class has a generated name that starts with `FastEnumExtensions_` and is hidden from IntelliSense: you never use it
directly.

## Find the calls to update

Once an enum is listed, the analyzer reports the `System.Enum` calls on it that have a generated equivalent, such as
`value.ToString()` ([MFEG0008](05-Diagnostics.md)), and a code fix rewrites them. To keep your code unchanged, enable
[interceptors](04-Interceptors.md) instead.

## Next steps

- [Generated members](03-GeneratedMembers.md): what each method does, and the members that need C# 14.
- [Interceptors](04-Interceptors.md): use the generated code without changing the call sites.
