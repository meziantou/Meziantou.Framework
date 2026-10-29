---
title: Converters
description: Write a type your way with a HumanReadableConverter, registered in the options or with an attribute.
---

## Write a value as a single line

Derive from `HumanReadableConverter<T>` and write the value with the `HumanReadableTextWriter`:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_Converter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed record Temperature(double Celsius);

private sealed class TemperatureConverter : HumanReadableConverter<Temperature>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Temperature? value, HumanReadableSerializerOptions options)
    {
        // The serializer writes null values itself, so value is never null here
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteValue(value.Celsius.ToString("0.0", CultureInfo.InvariantCulture) + " °C");
    }
}
```
<!-- endref -->

Add it to `Converters`:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_ConverterUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions();
options.Converters.Add(new TemperatureConverter());

var forecast = new { City = "Paris", Min = new Temperature(12.3), Max = new Temperature(21) };

Assert.Equal("""
    City: Paris
    Min: 12.3 °C
    Max: 21.0 °C
    """, HumanReadableSerializer.Serialize(forecast, options));
```
<!-- endref -->

A converter of `T` also handles the types derived from `T`. The serializer writes `<null>` for a `null` value
without calling the converter.

## Register a converter

The serializer picks the converter of a value in this order:

1. The `[HumanReadableConverter]` attribute of the member being written.
2. The first converter of `Converters` that can convert the type.
3. The `[HumanReadableConverter]` attribute of the type, including one added with `AddAttribute(type, attribute)` or
   declared on an interface that the type implements.
4. The built-in converters.

`[HumanReadableConverter(typeof(MyConverter))]` creates the converter with its parameterless constructor.
`AddAttribute(type, new HumanReadableConverterAttribute(converterInstance))` uses an instance you create.

## Write an object or a list

`HumanReadableTextWriter` writes the structure of the text. Call `HumanReadableSerializer.Serialize(writer, value,
options)` to write a nested value with its own converter:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_ObjectConverter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed record Money(decimal Amount, string Currency);

private sealed class MoneyConverter : HumanReadableConverter<Money>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Money? value, HumanReadableSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.StartObject();
        writer.WritePropertyName("Amount");
        HumanReadableSerializer.Serialize(writer, value.Amount, options);
        writer.WritePropertyName("Currency");
        writer.WriteValue(value.Currency);
        writer.EndObject();
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_ObjectConverterUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions();
options.Converters.Add(new MoneyConverter());

Assert.Equal("""
    Total:
      Amount: 12.5
      Currency: EUR
    """, HumanReadableSerializer.Serialize(new { Total = new Money(12.5m, "EUR") }, options));
```
<!-- endref -->

| Method | Writes |
| --- | --- |
| `WriteValue(text)` | A value. A multi-line value is written below its property name |
| `WriteNullValue()` | `<null>` |
| `WriteFormattedValue(mediaType, text)` | A value through the formatter of a media type, such as `application/json`. See [JSON, XML and HTML values](06-JsonXmlAndHtml.md) |
| `StartObject()`, `WritePropertyName(name)`, `EndObject()` | An object, one property at a time |
| `WriteEmptyObject()` | `{}` |
| `StartArray()`, `StartArrayItem()`, `EndArrayItem()`, `EndArray()` | A list, one item at a time |
| `WriteEmptyArray()` | `[]` |

## Converter factories

To handle a family of types, such as every `Result<T>`, derive from `HumanReadableConverterFactory`: `CanConvert(type)`
tells whether the factory handles a type, and `CreateConverter(type, options)` returns the converter for it, or `null`
to let the next converter handle the type.

`HumanReadableSerializerOptions.GetOrSetSerializationData(name, factory)` stores data for the duration of one
`Serialize` call, for converters that number the values they write, for instance.
