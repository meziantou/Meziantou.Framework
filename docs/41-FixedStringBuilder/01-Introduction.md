---
title: Introduction
description: Fixed-capacity string value types that store their characters inline, and a generator to create your own.
---

`Meziantou.Framework.FixedStringBuilder` provides value types that hold a string of up to a fixed number of
characters: `FixedStringBuilder8`, `FixedStringBuilder16`, `FixedStringBuilder32` and `FixedStringBuilder64`. The
characters are stored in the struct itself, so building, comparing and formatting the text allocates nothing on the
heap until you call `ToString()`:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_Interpolation;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder16 sb = $"Hello {"World"}";
sb.AppendLiteral("!");

string str = sb.ToString();
Assert.Equal("Hello World!", str);
```
<!-- endref -->

Use them for short texts whose maximum length you know: identifiers, codes, keys, labels, log fields. A value that does
not fit throws an `ArgumentException`: the types never truncate.

## Packages

| Package | Contains |
| --- | --- |
| `Meziantou.Framework.FixedStringBuilder` | The built-in types and the `IFixedString` interfaces, in the `Meziantou.Framework.FixedStringBuilder` namespace. |
| `Meziantou.Framework.FixedStringBuilder.Generator` | A source generator that creates a type of any capacity from a `partial struct`, and its analyzer. |

## Next steps

- [Getting started](02-GettingStarted.md): create a fixed string with a literal or an interpolated string.
- [Working with fixed strings](03-WorkingWithFixedStrings.md): capacity, formatting, comparison, spans and generic code.
- [Custom capacities](04-CustomCapacities.md): generate a type for another capacity.
- [Diagnostics](05-Diagnostics.md): the errors reported by the analyzer of the generator.
