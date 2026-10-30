---
title: Working with fixed strings
description: Handle the capacity, format values, compare, clear, read the characters as a span and write generic code.
---

The built-in types and the [types you generate](04-CustomCapacities.md) have the same members.

## Capacity

`MaxLength` is the capacity of the type, and `Length` the number of characters of the value. An operation that would
exceed the capacity throws an `ArgumentException`, whether it is a conversion from a string, an interpolated string or
`AppendLiteral`:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_Capacity;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder8 code = "ABC";
Assert.Equal(3, code.Length);
Assert.Equal(8, FixedStringBuilder8.MaxLength);

// 9 characters do not fit in a FixedStringBuilder8
Assert.Throws<ArgumentException>(() => new FixedStringBuilder8("ABCDEFGHI"));
```
<!-- endref -->

## Format values

Interpolated strings support format strings and alignment. A positive alignment pads on the left, a negative one on the
right:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_Formatting;format=md-fenced-code-block;dedent=true -->
```csharp
var quantity = 42;
FixedStringBuilder32 line = $"[{quantity:D5}] [{"A",3}] [{"B",-3}]";
Assert.Equal("[00042] [  A] [B  ]", line.ToString());
```
<!-- endref -->

A value that implements `ISpanFormattable`, such as a number or a date, is formatted directly into the remaining
characters, without an intermediate string. The values are formatted with the current culture: there is no parameter to
pass an `IFormatProvider`, so format culture-sensitive values (dates, decimal numbers) yourself when the result must not
depend on the culture.

## Compare

`==`, `!=` and `Equals(T)` compare the characters with an ordinal comparison, and `GetHashCode()` is consistent with
them. `Equals(T, StringComparison)` uses the comparison you choose:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_Equality;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder16 first = "Hello";
FixedStringBuilder16 second = "HELLO";

Assert.False(first == second);
Assert.True(first.Equals(second, StringComparison.OrdinalIgnoreCase));
Assert.True(first == "Hello"); // The string is converted to a FixedStringBuilder16
```
<!-- endref -->

## Read the characters

[AsSpan()](cref:Meziantou.Framework.FixedStringBuilder.FixedStringBuilder16.AsSpan) returns the characters of the value as a `ReadOnlySpan<char>`, without copying them:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_AsSpan;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder16 value = "Hello World";
ReadOnlySpan<char> span = value.AsSpan();

Assert.HasCount(11, span);
Assert.True(span.StartsWith("Hello", StringComparison.Ordinal));
```
<!-- endref -->

The span points into the struct, so the compiler prevents it from outliving the variable. The types also implement
`ISpanFormattable`: `TryFormat` copies the characters into a span you provide, and a fixed string inside another
interpolated string is copied without creating a `string`.

## Clear

[Clear()](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.Clear) resets the length to 0. The characters stay in the struct, where [IFixedString.GetUnsafeFullSpan()](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.GetUnsafeFullSpan) can still
read them. For sensitive data, use `Clear(zeroBuffer: true)`, which also overwrites every character with `'\0'`:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_ClearZeroBuffer;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder16 sb = $"secret";
sb.Clear(zeroBuffer: true);
Assert.Equal(0, sb.Length);
```
<!-- endref -->

## Write generic code

All the types implement [IFixedString](cref:Meziantou.Framework.FixedStringBuilder.IFixedString), which exposes [MaxLength](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.MaxLength) (a static member), [Length](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.Length), [Clear](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.Clear) and
[GetUnsafeFullSpan](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.GetUnsafeFullSpan), and [IFixedString\<T\>](cref:Meziantou.Framework.FixedStringBuilder.IFixedString{T}), which adds `IEquatable<T>` and the conversion from `string`. Use them as
generic constraints to write code that works with any capacity:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_GenericCode;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(8, GetMaxLength<FixedStringBuilder8>());
Assert.Equal(64, GetMaxLength<FixedStringBuilder64>());

var value = Create<FixedStringBuilder16>("Hello");
Assert.Equal(5, value.Length);

static int GetMaxLength<T>() where T : IFixedString => T.MaxLength;
static T Create<T>(string value) where T : IFixedString<T> => value;
```
<!-- endref -->

[GetUnsafeFullSpan()](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.GetUnsafeFullSpan) returns all the characters up to [MaxLength](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.MaxLength), including those after [Length](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.Length). It is only available
through the interface, and requires an `unsafe` context when the code is compiled with the updated memory safety rules:
the span is not tied to the struct, so the caller must not use it after the struct goes out of scope. Prefer [AsSpan()](cref:Meziantou.Framework.FixedStringBuilder.FixedStringBuilder16.AsSpan).

## Pass fixed strings around

A fixed string is a struct that contains all its characters: assigning it or passing it by value copies them all. Pass
large types by `in` or `ref` in hot paths. The members that do not change the value ([Length](cref:Meziantou.Framework.FixedStringBuilder.IFixedString.Length), `AsSpan`, `Equals`,
`ToString`, `TryFormat`...) are `readonly`, so calling them on an `in` parameter or a `readonly` field does not create a
defensive copy.
