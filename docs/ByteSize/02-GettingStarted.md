---
title: Getting started
description: Install the package, create sizes, and compare and add them.
---

## Install the package

Add the `Meziantou.Framework.ByteSize` package to your project:

```bash
dotnet add package Meziantou.Framework.ByteSize
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;` and
`using System.Globalization;`. They check their results with assertions such as `Assert.Equal`, which show what each
call returns.

## Create a size

A `ByteSize` is a number of bytes, stored as a `long` in its `Value` property. Create it from bytes, or from a value in
a unit:

<!-- ref:../../tests/Meziantou.Framework.ByteSize.Tests/ByteSizeTests.cs;region=ByteSize_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var a = new ByteSize(1_500);                       // 1,500 bytes
var b = ByteSize.FromKiloBytes(1.5);               // 1.5 × 1,000 bytes
var c = ByteSize.FromKibiBytes(1);                 // 1 × 1,024 bytes
var d = ByteSize.From(2, ByteSizeUnit.MebiByte);   // 2 × 1,048,576 bytes
ByteSize e = 4_096;                                // implicit conversion from long

Assert.Equal(a, b);
Assert.Equal(1_024, c.Value);
Assert.Equal(2_097_152, d.Value);
Assert.Equal(4_096, e.Value);

// Read the value in another unit
Assert.Equal(1.5, a.GetValue(ByteSizeUnit.KiloByte));
Assert.Equal(2, d.GetValue(ByteSizeUnit.MebiByte));
```
<!-- endref -->

- `FromBytes`, `FromKiloBytes`, `FromMegaBytes`, … `FromExaBytes` and `FromKibiBytes`, `FromMebiBytes`, …
  `FromExbiBytes` accept integers and floating-point values. A fractional value is truncated to a whole number of bytes.
- `From(value, unit)` takes the unit as a `ByteSizeUnit`.
- `FromFileLength` takes a file path or a `FileInfo` and returns the length of the file.
- `GetValue(unit)` returns the size in a unit, as a `double`.
- `ByteSize.Zero`, `ByteSize.MinValue` and `ByteSize.MaxValue` are the zero size and the limits of a `long`.

## Display a size

`ToString` picks the best decimal unit: the largest unit in which the value is at least 1. See
[Formatting and parsing](03-FormattingAndParsing.md) to use binary units, a given unit, or a number of decimals.

## Compare and compute

`ByteSize` supports the comparison operators, `+`, `-`, and `*` and `/` by a `long`:

<!-- ref:../../tests/Meziantou.Framework.ByteSize.Tests/ByteSizeTests.cs;region=ByteSize_Arithmetic;format=md-fenced-code-block;dedent=true -->
```csharp
var total = ByteSize.FromMegaBytes(1) + ByteSize.FromKiloBytes(500);
Assert.Equal("1.5MB", total.ToString(CultureInfo.InvariantCulture));

var remaining = ByteSize.FromGigaBytes(1) - total;
Assert.Equal(998_500_000, remaining.Value);

Assert.Equal(ByteSize.FromMegaBytes(3), total * 2);
Assert.Equal(ByteSize.FromKiloBytes(750), total / 2);

var files = new[] { ByteSize.FromKiloBytes(3), ByteSize.FromMegaBytes(1), new ByteSize(10) };
Assert.Equal(ByteSize.FromMegaBytes(1), files.Max());
Assert.True(files[0] < files[1]);

// Additions and multiplications are checked
Assert.Throws<OverflowException>(() => _ = ByteSize.MaxValue + ByteSize.FromBytes(1));
```
<!-- endref -->

- `Add`, `Subtract`, `Multiply` and `Divide` are the method forms of the operators.
- `+`, `-` and `*` throw an `OverflowException` when the result does not fit in a `long`. `/` is an integer division.
- `ByteSize` implements `IComparable<ByteSize>` and `IEquatable<ByteSize>`, so `Order`, `Max` and sorted collections
  work with it. Two sizes are equal when they have the same number of bytes: `FromKiloBytes(1)` equals `new ByteSize(1000)`.

## Next steps

- [Formatting and parsing](03-FormattingAndParsing.md): format strings, cultures, and the text `Parse` accepts.
