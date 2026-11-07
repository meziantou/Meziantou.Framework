---
title: Introduction
description: A source generator that writes specialized helpers for the enums you choose, and an analyzer that points to them.
---

`Meziantou.Framework.FastEnumGenerator` is a C# source generator. You list the enums to optimize with an assembly
attribute, and the generator writes, at compile time, methods that convert them to strings, parse them and enumerate
their values with code specialized for each enum, instead of the reflection that `System.Enum` uses:

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

<!-- ref:../../samples/Meziantou.Framework.FastEnumGenerator.Samples/Program.cs;region=FastEnumGenerator_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using Sample.Extensions; // only needed when ExtensionMethodNamespace is used

var name = Color.Blue.ToStringFast();
```
<!-- endref -->

Use it for the enums that hot paths format or parse: log fields, serialized values, HTTP headers, metrics tags...

## What you can do with it

- **Format values** with `ToStringFast` and `GetName`, and test flags with `HasFlagFast`. See
  [Generated members](03-GeneratedMembers.md).
- **Parse values and list the members** with `Parse`, `TryParse`, `IsDefinedFast`, `GetNames` and `GetValues`, called
  on the enum type like the `System.Enum` methods (C# 14).
- **Use display names** from `[Display]` or `[EnumMember]` to format and parse values.
- **Speed up existing code without changing it**, by letting the generator intercept the `System.Enum` calls. See
  [Interceptors](04-Interceptors.md).
- **Find the calls to replace**: the analyzer reports the `System.Enum` calls that have a generated equivalent, with a
  code fix. See [Diagnostics](05-Diagnostics.md).

## Principles

- **Same results as `System.Enum`.** The generated methods return what the `System.Enum` method they replace returns,
  for declared values, undeclared values and `[Flags]` combinations. Only the way they compute it changes.
- **You choose the enums.** Nothing is generated for an enum that is not listed in a `FastEnumAttribute`.
- **Everything happens at compile time.** The generated code is plain C# in your assembly, and the attribute is removed
  from the compiled assembly: there is no runtime dependency on the package.
- **Opt-in rewrites.** Interceptors change what an unchanged line of code does, so they are disabled until you enable
  them.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and generate the methods of an enum.
- [Generated members](03-GeneratedMembers.md): every generated method and how it behaves.
