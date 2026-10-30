---
title: Getting started
description: Install the package, then create a fixed string from a literal or an interpolated string.
---

## Install the package

Add the `Meziantou.Framework.FixedStringBuilder` package to your project:

```bash
dotnet add package Meziantou.Framework.FixedStringBuilder
```

The types are in the [Meziantou.Framework.FixedStringBuilder](cref:Meziantou.Framework.FixedStringBuilder) namespace. The examples of this documentation omit the
`using` directives, and check their results with assertions such as `Assert.Equal`.

## Choose a capacity

| Type | Maximum number of characters |
| --- | --- |
| [FixedStringBuilder8](cref:Meziantou.Framework.FixedStringBuilder.FixedStringBuilder8) | 8 |
| [FixedStringBuilder16](cref:Meziantou.Framework.FixedStringBuilder.FixedStringBuilder16) | 16 |
| [FixedStringBuilder32](cref:Meziantou.Framework.FixedStringBuilder.FixedStringBuilder32) | 32 |
| [FixedStringBuilder64](cref:Meziantou.Framework.FixedStringBuilder.FixedStringBuilder64) | 64 |

Choose the smallest type that fits the longest value: every value has the size of its capacity, whatever its length.
For another capacity, [generate your own type](04-CustomCapacities.md).

## Create a fixed string

Assign a string, or an interpolated string, to a variable of the type. The interpolated string is written directly
into the struct, without creating a `string` first, and you can append more text afterwards:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_Interpolation;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder16 sb = $"Hello {"World"}";
sb.AppendLiteral("!");

string str = sb.ToString();
Assert.Equal("Hello World!", str);
```
<!-- endref -->

`ToString()` creates the `string`. Call it only when an API needs a `string`: comparisons, spans and formatting work on
the struct directly. See [Working with fixed strings](03-WorkingWithFixedStrings.md).

## Next steps

- [Working with fixed strings](03-WorkingWithFixedStrings.md): what happens when a value does not fit, and the members
  of the types.
- [Custom capacities](04-CustomCapacities.md): a type for 128, 256 or any number of characters.
