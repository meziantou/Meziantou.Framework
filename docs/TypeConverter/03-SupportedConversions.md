---
title: Supported conversions
description: What DefaultConverter accepts for numbers, booleans, enums, nullable types, dates, GUIDs, cultures, byte arrays and other types.
---

[ConvertUtilities](cref:Meziantou.Framework.ConvertUtilities) uses [ConvertUtilities.DefaultConverter](cref:Meziantou.Framework.ConvertUtilities.DefaultConverter), an instance of [DefaultConverter](cref:Meziantou.Framework.DefaultConverter):

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/ConvertUtilitiesTests.cs;region=TypeConverter_SupportedConversions;format=md-fenced-code-block;dedent=true -->
```csharp
var culture = CultureInfo.InvariantCulture;

// Numbers, including hexadecimal integers
Assert.Equal(255, ConvertUtilities.ChangeType("0xFF", defaultValue: 0, culture));
Assert.Equal(1234.5m, ConvertUtilities.ChangeType("1,234.5", defaultValue: 0m, culture));

// Booleans accept numbers and words
Assert.True(ConvertUtilities.ChangeType("yes", defaultValue: false, culture));
Assert.True(ConvertUtilities.ChangeType("1", defaultValue: false, culture));
Assert.False(ConvertUtilities.ChangeType("N", defaultValue: true, culture));

// Enums, by name (ignoring case), by value, or as a list of flags
Assert.Equal(DayOfWeek.Friday, ConvertUtilities.ChangeType("friday", defaultValue: DayOfWeek.Sunday, culture));
Assert.Equal(DayOfWeek.Friday, ConvertUtilities.ChangeType(5, defaultValue: DayOfWeek.Sunday, culture));
Assert.Equal(FileAccess.ReadWrite, ConvertUtilities.ChangeType("Read, Write", defaultValue: FileAccess.Read, culture));

// Nullable types: an empty string is null
Assert.Null(ConvertUtilities.ChangeType<int?>("", defaultValue: 0, culture));
Assert.Equal(12, ConvertUtilities.ChangeType<int?>("12", defaultValue: null, culture));

// Guid, TimeSpan, DateTimeOffset
Assert.Equal(new Guid("1c7a7a4e-0f43-4a3f-9d5b-6a7cb4b1f2d3"), ConvertUtilities.ChangeType("1c7a7a4e-0f43-4a3f-9d5b-6a7cb4b1f2d3", defaultValue: Guid.Empty, culture));
Assert.Equal(TimeSpan.FromMinutes(90), ConvertUtilities.ChangeType("01:30:00", defaultValue: TimeSpan.Zero, culture));
Assert.Equal(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero), ConvertUtilities.ChangeType("2024-06-15T12:00:00Z", defaultValue: DateTimeOffset.MinValue, culture));

// Types that have a TypeConverter, such as Uri and Version
Assert.Equal(new Uri("https://www.meziantou.net"), ConvertUtilities.ChangeType<Uri?>("https://www.meziantou.net", defaultValue: null, culture));
Assert.Equal(new Version(1, 2, 3), ConvertUtilities.ChangeType<Version?>("1.2.3", defaultValue: null, culture));

// Any value to string
Assert.Equal("3.5", ConvertUtilities.ChangeType<string?>(3.5, defaultValue: null, culture));
```
<!-- endref -->

## Conversion rules

[DefaultConverter](cref:Meziantou.Framework.DefaultConverter) tries these conversions in order, and stops at the first that succeeds:

1. A value that is already of the target type, or of a derived type, is returned as is. Any value converts to `object`.
2. `null` and `DBNull.Value` give `null` for reference and nullable types. For a nullable type, an empty or white-space
   string gives `null`, and other values are converted to the underlying type.
3. Enums are parsed from their name, ignoring case, or from their numeric value; a comma-separated list combines flags.
4. The types below have their own rules.
5. The `TypeConverter` of the target type (`TypeDescriptor.GetConverter`) converts from the type of the value, then the
   `TypeConverter` of the value converts to the target type, then the `TypeConverter` of the target type converts from
   the value written as a string. This handles `Uri`, `Version`, and your types that have a `[TypeConverter]`
   attribute.
6. An implicit conversion operator declared by the type of the value, to the target type.

| Target type | Accepted values |
| --- | --- |
| Integers (`int`, `long`, `short`, `byte`, `sbyte` and the unsigned types) | Numbers, numeric strings in the format provider's culture, and hexadecimal strings with a `0x` prefix. `short`, `int` and `long` also accept a byte array of their size |
| `float`, `double`, `decimal` | Numbers and numeric strings, with thousands separators. Hexadecimal strings are rejected |
| `bool` | Numbers (`0` is `false`), `true`/`false`, `yes`/`no`, `y`/`n`, `t`/`f`, ignoring case, a byte array of one byte |
| `char` | A one-character string |
| `DateTime`, `DateTimeOffset`, `TimeSpan` | Strings parsed with the format provider |
| `Guid` | A GUID string, or a 16-byte array |
| `CultureInfo` | A culture name such as `fr-FR`. A number is a culture identifier (LCID), supported on Windows only |
| `string` | Any value, formatted with the format provider. A `CultureInfo` gives its name, and a byte array is encoded as Base64 by default |
| `byte[]` | A Base64 or hexadecimal (`0x…`) string, or the binary representation of a number, `bool`, `char`, `DateTime`, `decimal`, `Guid`, `TimeSpan` or `DateTimeOffset` |

An empty or white-space string never converts to a number. When every rule fails, [TryChangeType](cref:Meziantou.Framework.DefaultConverter.TryChangeType) returns `false`.

## Byte arrays

[DefaultConverter.ByteArrayToStringFormat](cref:Meziantou.Framework.DefaultConverter.ByteArrayToStringFormat) sets how a byte array is written as a string: [Base64](cref:Meziantou.Framework.ByteArrayToStringFormat.Base64) (the default),
[Base16](cref:Meziantou.Framework.ByteArrayToStringFormat.Base16) (hexadecimal), or [Base16Prefixed](cref:Meziantou.Framework.ByteArrayToStringFormat.Base16Prefixed) (hexadecimal with a `0x` prefix). Strings are read back from both formats:
a string with a `0x` prefix is hexadecimal, other strings are read as Base64 first, then as hexadecimal.

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/DefaultConverterTests_ByteArrayTo.cs;region=TypeConverter_ByteArrayConversions;format=md-fenced-code-block;dedent=true -->
```csharp
var bytes = new byte[] { 1, 2, 3, 4 };

// Default: Base64
var converter1 = new DefaultConverter();
converter1.TryChangeType(bytes, null, out string? base64);
Assert.Equal("AQIDBA==", base64);

// Hexadecimal without prefix
var converter2 = new DefaultConverter
{
    ByteArrayToStringFormat = ByteArrayToStringFormat.Base16,
};
converter2.TryChangeType(bytes, null, out string? hex);
Assert.Equal("01020304", hex);

// Hexadecimal with 0x prefix
var converter3 = new DefaultConverter
{
    ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed,
};
converter3.TryChangeType(bytes, null, out string? hexPrefixed);
Assert.Equal("0x01020304", hexPrefixed);

// Convert from hexadecimal string to byte array
ConvertUtilities.TryChangeType("0x01020304", CultureInfo.InvariantCulture, out byte[]? result);
Assert.Equal([1, 2, 3, 4], result);

// Convert from Base64 string to byte array
ConvertUtilities.TryChangeType("AQIDBA==", CultureInfo.InvariantCulture, out byte[]? result2);
Assert.Equal([1, 2, 3, 4], result2);
```
<!-- endref -->

## Implicit conversion operators

When no other rule applies, [DefaultConverter](cref:Meziantou.Framework.DefaultConverter) looks for an implicit operator declared by the type of the value:

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/ConvertUtilitiesTests.cs;region=TypeConverter_ImplicitOperatorType;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class Percentage(double value)
{
    public double Value { get; } = value;

    // DefaultConverter uses the implicit operators declared by the type of the value to convert
    public static implicit operator double(Percentage percentage) => percentage.Value / 100;
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/ConvertUtilitiesTests.cs;region=TypeConverter_ImplicitOperatorUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var value = ConvertUtilities.ChangeType<double>(new Percentage(25), defaultValue: 0, CultureInfo.InvariantCulture);
Assert.Equal(0.25, value);
```
<!-- endref -->

Explicit operators, and implicit operators declared by the target type, are not used.

## Trimming

The package is not compatible with trimming: the `TypeConverter` and implicit operator rules discover types and methods
with reflection.
