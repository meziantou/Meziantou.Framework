---
title: Converters
description: The types the serializer converts without help, and how to write a converter for your own types.
---

A converter turns the text of a value into a .NET value, and back. The serializer has converters for the common types,
and uses the converters you add for the others.

## Built-in types

| Types | Text |
| --- | --- |
| `string`, `char` | As they are. An empty value is an empty string. |
| `bool` | `true` and `false`; `true`, `false`, `1` and `0` ignoring case when reading. |
| Numbers: `byte` to `ulong`, `Int128`, `UInt128`, `nint`, `nuint`, `Half`, `float`, `double`, `decimal`, `BigInteger` | Formatted and parsed with the format provider. |
| `DateTime`, `DateTimeOffset` | ISO 8601 (`O`). Read with any format the format provider accepts. |
| `DateOnly`, `TimeOnly` | ISO 8601 (`O`). |
| `TimeSpan` | The constant format (`c`). |
| `Guid`, `Uri`, `Version` | Their usual text. |
| `byte[]` | Base64. |
| Enumerations | The names of the members; names and numbers ignoring case when reading. |
| `Nullable<T>` | An empty value is `null`. |
| Other types that implement `ISpanParsable<T>` and `ISpanFormattable` | Their own format. |

[CsvFormatAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvFormatAttribute) sets the format string of a member: the dates and times are read with this exact format, and the other
types are written with it.

## Write a converter

Derive from [CsvConverter\<T\>](cref:Meziantou.Framework.Csv.Serialization.CsvConverter{T}) and override [Read](cref:Meziantou.Framework.Csv.Serialization.CsvConverter{T}.Read) and [TryFormat](cref:Meziantou.Framework.Csv.Serialization.CsvConverter{T}.TryFormat). [TryFormat](cref:Meziantou.Framework.Csv.Serialization.CsvConverter{T}.TryFormat) writes into a span: return
`false` when the span is too small, and the serializer calls it again with a larger one. Throw a `FormatException` for
an invalid value; the serializer reports it as a [CsvException](cref:Meziantou.Framework.Csv.CsvException) with the position of the value:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_ConverterModel;format=md-fenced-code-block;dedent=true -->
```csharp
public readonly record struct Point(int X, int Y);

public sealed class PointConverter : CsvConverter<Point>
{
    public override Point Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        var separator = value.IndexOf(';');
        if (separator < 0)
            throw new FormatException("A point is written 'x;y'.");

        return new Point(int.Parse(value[..separator], context.FormatProvider), int.Parse(value[(separator + 1)..], context.FormatProvider));
    }

    public override bool TryFormat(Point value, Span<char> destination, out int charsWritten, CsvConverterContext context)
        => destination.TryWrite(context.FormatProvider, $"{value.X};{value.Y}", out charsWritten);
}

public sealed class UpperCaseConverter : CsvConverter<string?>
{
    public override string? Read(ReadOnlySpan<char> value, CsvConverterContext context) => value.ToString().ToLowerInvariant();

    public override bool TryFormat(string? value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        charsWritten = 0;
        if (value is null)
            return true;

        if (destination.Length < value.Length)
            return false;

        charsWritten = value.AsSpan().ToUpperInvariant(destination);
        return true;
    }
}

public sealed class Shape
{
    [CsvConverter(typeof(UpperCaseConverter))]
    public string? Name { get; set; }

    public Point Start { get; set; }
}
```
<!-- endref -->

Add the converter to [Converters](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.Converters) to use it for every member of its type, or set it on a member with [CsvConverterAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvConverterAttribute):

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_Converters;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvSerializerOptions { Converters = [new PointConverter()] };

var csv = CsvSerializer.Serialize([new Shape { Name = "line", Start = new Point(1, 2) }], options);
Assert.Equal("Name,Start\r\nLINE,1;2\r\n", csv); // UpperCaseConverter is set with CsvConverterAttribute

var shape = CsvSerializer.Deserialize<Shape>(csv, options)[0];
Assert.Equal(new Point(1, 2), shape.Start);
```
<!-- endref -->

- A converter of a member takes precedence over the converters of the options, which take precedence over the
  built-in converters.
- [CsvConverterContext](cref:Meziantou.Framework.Csv.Serialization.CsvConverterContext) gives the format provider, the format string of the member and the options.
- When [HandleNull](cref:Meziantou.Framework.Csv.Serialization.CsvConverter{T}.HandleNull) is `false`, the default, and the type can be `null`, the serializer reads an empty value as `null`
  and writes `null` as an empty value without calling the converter.
- The serializer reads and writes UTF-8 data with the overloads of [Read](cref:Meziantou.Framework.Csv.Serialization.CsvConverter{T}.Read) and [TryFormat](cref:Meziantou.Framework.Csv.Serialization.CsvConverter{T}.TryFormat) that take bytes. By default they
  convert the value to UTF-16 and call the overloads that take characters: override them to avoid the conversion.

## Converter factories

A [CsvConverterFactory](cref:Meziantou.Framework.Csv.Serialization.CsvConverterFactory) creates converters for a family of types, such as the instances of a generic type. Override
[CanConvert](cref:Meziantou.Framework.Csv.Serialization.CsvConverter.CanConvert) to select the types, and [CreateConverter](cref:Meziantou.Framework.Csv.Serialization.CsvConverterFactory.CreateConverter) to return a [CsvConverter\<T\>](cref:Meziantou.Framework.Csv.Serialization.CsvConverter{T}) for each of them.
