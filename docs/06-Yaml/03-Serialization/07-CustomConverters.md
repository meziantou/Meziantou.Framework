---
title: Custom converters
description: Read and write a type your own way with a converter, register it, and create converters for families of types.
---

A converter reads and writes the YAML of one type. Write one when a type must have another representation than the
mapping of its members, such as a value written as a single scalar, or when the serializer does not support the type.

## Write a converter

Derive from `YamlConverter<T>` and implement `Read` and `Write`. This converter writes a temperature as `21.5C`:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlConverterAttributeTests.cs;region=YamlConverter_Scalar;format=md-fenced-code-block;dedent=true -->
```csharp
[YamlConverter(typeof(TemperatureConverter))]
internal readonly record struct Temperature(double Celsius);

internal sealed class TemperatureConverter : YamlConverter<Temperature>
{
    public override Temperature Read(YamlReader reader)
    {
        var text = reader.GetScalarValue(); // Throws when the value is not a scalar
        if (!text.EndsWith('C', StringComparison.Ordinal) || !double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var celsius))
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"'{text}' is not a temperature.");

        reader.Read(); // Move past the value
        return new Temperature(celsius);
    }

    public override void Write(YamlWriter writer, Temperature value)
    {
        writer.WriteString(value.Celsius.ToString(CultureInfo.InvariantCulture) + "C");
    }
}

internal sealed class Room
{
    public string? Name { get; set; }

    public Temperature Target { get; set; }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlConverterAttributeTests.cs;region=YamlConverter_ScalarUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var room = YamlSerializer.Deserialize<Room>("Name: Office\nTarget: 21.5C\n");
Assert.Equal(21.5, room!.Target.Celsius);

Assert.Equal("Name: Office\nTarget: 21.5C\n", YamlSerializer.Serialize(room));
Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Room>("Target: warm"));
```
<!-- endref -->

- `Read` starts on the first token of the value and must move past the whole value: call `reader.Read()` after a
  scalar, or `reader.Skip()` to skip a value and its children. A converter that stays on its value breaks the reading
  of the next key.
- `GetScalarValue` returns the text of the current scalar and throws when the current token is not a scalar.
  `TokenType` tells which token the reader is on (`Scalar`, `StartMapping`, `StartSequence`, …), and `Tag`, `Anchor`
  and `Alias` give its properties.
- Report invalid content with a `YamlException`, built with `reader.SourceName`, `reader.Start` and `reader.End`, so
  that the message gives the [position of the error](../08-ErrorHandling.md).
- `Write` uses the `YamlWriter`: `WriteString`, `WriteScalar` (with an overload for each built-in type),
  `WriteNullValue`, `WriteStartMapping`, `WritePropertyName`, `WriteEndMapping`, `WriteStartSequence`,
  `WriteEndSequence`, `WriteTag`, `WriteAnchor` and `WriteAlias`.

The static class `YamlScalar` exposes the parsers of the built-in converters, such as `TryParseInt64`,
`TryParseDouble` and `IsNull`, to read scalars the same way.

A converter also receives the null scalars (`~`, `null`) of its type: when `null` is valid for the type, check
`YamlScalar.IsNull` in `Read`, as in the [generic example below](#generic-types).

## Register a converter

A converter applies where it is registered:

- `[YamlConverter(typeof(TemperatureConverter))]` on the type, as above: everywhere the type is used.
- `[YamlConverter]` on a property or a field: that member only.
- `YamlSerializerOptions.Converters`, for instance `Converters = [new TemperatureConverter()]`: every serialization
  that uses these options. The converters are tried in order, before the built-in ones, so they can also replace how
  a built-in type is written.
- `YamlSourceGenerationOptionsAttribute.Converters` for a [generated context](../04-SourceGeneration.md).

A converter registered for a type that is also used as a dictionary key converts the values of the dictionary, not its
keys: keys of the [supported key types](03-SupportedTypes.md#collections-and-dictionaries) are always written as
scalars.

## Generic types

`[YamlConverter]` accepts an open generic converter on a generic type with the same number of type parameters. The
converter is closed with the type arguments of each use:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlConverterAttributeTests.cs;region=YamlConverterAttribute_OpenGeneric;format=md-fenced-code-block;dedent=true -->
```csharp
[YamlConverter(typeof(BoxConverter<>))]
private sealed class Box<T>
{
    public Box(T? value) => Value = value;

    public T? Value { get; }
}

private sealed class BoxConverter<T> : YamlConverter<Box<T>?> // used for Box<int>, Box<string>, ...
{
    public override Box<T>? Read(YamlReader reader)
    {
        if (reader.TokenType is YamlTokenType.Scalar && YamlScalar.IsNull(reader.ScalarValue.AsSpan()))
        {
            reader.Read();
            return null;
        }

        var scalar = reader.GetScalarValue();
        reader.Read();
        return new Box<T>((T)Convert.ChangeType(scalar, typeof(T), CultureInfo.InvariantCulture));
    }

    public override void Write(YamlWriter writer, Box<T>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteScalar(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
    }
}
```
<!-- endref -->

## Converter factories

To handle a family of types with a converter created for each type, derive from `YamlConverterFactory`: `CanConvert`
tells whether the factory handles a type, and `CreateConverter` returns the converter for it. Add the factory to
`YamlSerializerOptions.Converters`.

## Populate existing values

Override `CanPopulate` and `Populate` to support
[`YamlObjectCreationHandling.Populate`](06-ReadingBehavior.md#fill-existing-collections): `Populate` receives the value
that the member already holds and reads the YAML into it.
