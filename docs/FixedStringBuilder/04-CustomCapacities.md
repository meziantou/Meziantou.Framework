---
title: Custom capacities
description: Generate a fixed string type for any capacity with the source generator package.
---

The built-in types stop at 64 characters. The `Meziantou.Framework.FixedStringBuilder.Generator` package generates a
type for any capacity, from 1 to 32,767 characters, with the same members as the built-in types.

## Install the generator

```bash
dotnet add package Meziantou.Framework.FixedStringBuilder.Generator
```

Also reference the `Meziantou.Framework.FixedStringBuilder` package if you want the generated types to implement the
[`IFixedString` interfaces](03-WorkingWithFixedStrings.md#write-generic-code): they are added only when the project
can use them.

The generated types store their characters in an inline array (`[InlineArray]`), which requires .NET 8 or later.

## Declare a type

Add `[FixedStringBuilderAttribute(capacity)]` to a `partial struct`. The generator adds the attribute to the project, in
the global namespace:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_CustomType;format=md-fenced-code-block;dedent=true -->
```csharp
[FixedStringBuilderAttribute(128)]
public partial struct FixedStringBuilder128;
```
<!-- endref -->

The struct must be `partial`, and must not be `readonly` or a `ref struct`: the generated members change its value. A
struct nested in other types requires these types to be `partial` too. The generated type keeps the accessibility of
your declaration.

## Use it

The generated type works like the built-in ones:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_CustomTypeUsage;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder128 value = $"{new string('a', 100)}";
Assert.Equal(100, value.Length);
Assert.Equal(128, FixedStringBuilder128.MaxLength);

// 129 characters do not fit
Assert.Throws<ArgumentException>(() => value.AppendLiteral(new string('b', 29)));
```
<!-- endref -->

## Generated members

| Member | Description |
| --- | --- |
| `MaxLength` | The capacity given to the attribute (`static`). |
| `Length` | The number of characters of the value. |
| Constructor `(string value)` and implicit conversion from `string` | Create a value. Throw an `ArgumentException` when the string is too long. |
| Interpolated string handler members (`AppendLiteral`, `AppendFormatted`) | Build the value from an interpolated string, and append to it. |
| `AsSpan()` | The characters as a `ReadOnlySpan<char>`. |
| `Clear()`, `Clear(bool zeroBuffer)` | Reset the value. See [Clear](03-WorkingWithFixedStrings.md#clear). |
| `Equals`, `GetHashCode`, `==`, `!=` | Ordinal equality. `Equals(T, StringComparison)` uses another comparison. |
| `ToString()`, `TryFormat` | Create a `string`, or copy the characters into a span. |

The type implements `IEquatable<T>` and `ISpanFormattable`, and `IFixedString<T>` when the project references the
`Meziantou.Framework.FixedStringBuilder` package. The generator reports its errors through an analyzer: see
[Diagnostics](05-Diagnostics.md).
