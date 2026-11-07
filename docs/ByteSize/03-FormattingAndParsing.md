---
title: Formatting and parsing
description: Format a size with the best unit or a given unit and a number of decimals, and parse sizes from text.
---

## Format a size

`ToString(format, formatProvider)` writes the number followed by the unit, without a space:

<!-- ref:../../tests/Meziantou.Framework.ByteSize.Tests/ByteSizeTests.cs;region=ByteSize_Format;format=md-fenced-code-block;dedent=true -->
```csharp
var size = new ByteSize(1_536_000);

// Best decimal unit (kB, MB, GB, ...)
Assert.Equal("1.536MB", size.ToString(CultureInfo.InvariantCulture));
Assert.Equal("1.54MB", size.ToString("G2", CultureInfo.InvariantCulture));

// Best binary unit (kiB, MiB, GiB, ...)
Assert.Equal("1.46484375MiB", size.ToString("Gi", CultureInfo.InvariantCulture));
Assert.Equal("1.46MiB", size.ToString("Gi2", CultureInfo.InvariantCulture));

// A given unit, with or without a number of decimals
Assert.Equal("1536kB", size.ToString("kB", CultureInfo.InvariantCulture));
Assert.Equal("1500.000kiB", size.ToString("kiB3", CultureInfo.InvariantCulture));
Assert.Equal("1536kB", size.ToString(ByteSizeUnit.KiloByte, CultureInfo.InvariantCulture));

// Format strings work in interpolated strings and string.Format
Assert.Equal("Downloaded 1.46MiB", string.Create(CultureInfo.InvariantCulture, $"Downloaded {size:Gi2}"));
```
<!-- endref -->

The format strings are:

| Format | Unit | Number |
| --- | --- | --- |
| `G` (the default, also `null` or empty) | Best decimal unit: `B`, `kB`, `MB`, … | All the significant digits |
| `G2`, `G3`, … | Best decimal unit | 2, 3, … decimals |
| `Gi` | Best binary unit: `B`, `kiB`, `MiB`, … | All the significant digits |
| `Gi2`, `Gi3`, … | Best binary unit | 2, 3, … decimals |
| `B`, `kB`, `MB`, `GB`, `TB`, `PB`, `EB`, `kiB`, `MiB`, `GiB`, `TiB`, `PiB`, `EiB` | This unit | All the significant digits |
| `kB2`, `MiB3`, … | This unit | 2, 3, … decimals |

`F`, `F2`, `Fi` and `Fi2` are synonyms of `G`, `G2`, `Gi` and `Gi2`. The best unit is the largest unit in which the
value is at least 1; negative sizes use the same unit as their absolute value. An unknown format throws a
`FormatException`.

`ToString(ByteSizeUnit unit, IFormatProvider? formatProvider)` writes the value in the given unit, like the `kB`
format.

[ByteSize](cref:Meziantou.Framework.ByteSize) implements `ISpanFormattable` and `IUtf8SpanFormattable`, so the format strings also work in interpolated
strings, `string.Format`, and [TryFormat](cref:Meziantou.Framework.ByteSize.TryFormat) into a `Span<char>` or a UTF-8 `Span<byte>`.

### Cultures

The number is formatted with the format provider, so the decimal separator depends on the culture: `1.54MB` in English,
`1,54MB` in French. The overloads without a provider use the current culture. Pass
`CultureInfo.InvariantCulture` for text that a program reads back, such as a configuration file or a log that is
parsed later.

## Parse a size

[Parse](cref:Meziantou.Framework.ByteSize.Parse) and [TryParse](cref:Meziantou.Framework.ByteSize.TryParse) read a number followed by an optional unit:

<!-- ref:../../tests/Meziantou.Framework.ByteSize.Tests/ByteSizeTests.cs;region=ByteSize_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(1_500, ByteSize.Parse("1.5kB", CultureInfo.InvariantCulture).Value);
Assert.Equal(1_536, ByteSize.Parse("1.5 kiB", CultureInfo.InvariantCulture).Value);
Assert.Equal(42, ByteSize.Parse("42", CultureInfo.InvariantCulture).Value); // No unit: bytes

// Units are case-insensitive: "KB" is a kilobyte (1,000 bytes), "KIB" a kibibyte (1,024 bytes)
Assert.Equal(1_000, ByteSize.Parse("1KB", CultureInfo.InvariantCulture).Value);
Assert.Equal(1_024, ByteSize.Parse("1KIB", CultureInfo.InvariantCulture).Value);

// UTF-8 text
Assert.Equal(1_500, ByteSize.Parse("1.5kB"u8).Value);

// Invalid text, and sizes that do not fit in a long, are rejected
Assert.False(ByteSize.TryParse("ten MB", CultureInfo.InvariantCulture, out _));
Assert.False(ByteSize.TryParse("100EB", CultureInfo.InvariantCulture, out _));
```
<!-- endref -->

- The number can be an integer or a decimal number, and spaces are allowed around it and before the unit.
- Without a unit, the number is a number of bytes.
- The unit is case-insensitive. `KB`, `kb` and `kB` are all kilobytes (1,000 bytes); only the `i` makes a binary unit.
- A size that does not fit in a `long` is rejected: [TryParse](cref:Meziantou.Framework.ByteSize.TryParse) returns `false` and [Parse](cref:Meziantou.Framework.ByteSize.Parse) throws a `FormatException`.
- The number is parsed with the format provider: `1,5kB` is 1,500 bytes with the French culture. `TryParse(string,
  out ByteSize)` uses the current culture.
- The `ReadOnlySpan<char>` overloads read a span, and the `ReadOnlySpan<byte>` overloads read UTF-8 text with the
  invariant number format.

[ByteSize](cref:Meziantou.Framework.ByteSize) implements `ISpanParsable<ByteSize>` and `IUtf8SpanParsable<ByteSize>`, so generic code that parses values
through these interfaces accepts it.

### Rounding

A format with a number of decimals rounds the value: `1.54MB`, written with `G2`, parses back as 1,540,000 bytes and
not 1,536,000. Use the `B` format to write the exact number of bytes.
