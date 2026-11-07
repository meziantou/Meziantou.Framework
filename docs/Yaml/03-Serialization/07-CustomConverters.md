---
title: Custom converters
description: Read and write a type your own way with a converter, register it, and create converters for families of types.
---

A converter reads and writes the YAML of one type. Write one when a type must have another representation than the
mapping of its members, such as a value written as a single scalar, or when the serializer does not support the type.

## Write a converter

Derive from [YamlConverter\<T\>](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter{T}) and implement [Read](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter{T}.Read) and [Write](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter{T}.Write). This converter writes a temperature as `21.5C`:

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

- [Read](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter{T}.Read) starts on the first token of the value and must move past the whole value: call `reader.Read()` after a
  scalar, or `reader.Skip()` to skip a value and its children. A converter that stays on its value breaks the reading
  of the next key.
- [GetScalarValue](cref:Meziantou.Framework.Yaml.Serialization.YamlReader.GetScalarValue) returns the text of the current scalar and throws when the current token is not a scalar.
  `TokenType` tells which token the reader is on ([Scalar](cref:Meziantou.Framework.Yaml.Serialization.YamlTokenType.Scalar), [StartMapping](cref:Meziantou.Framework.Yaml.Serialization.YamlTokenType.StartMapping), [StartSequence](cref:Meziantou.Framework.Yaml.Serialization.YamlTokenType.StartSequence), …), and [Tag](cref:Meziantou.Framework.Yaml.Serialization.YamlReader.Tag), [Anchor](cref:Meziantou.Framework.Yaml.Serialization.YamlReader.Anchor)
  and [Alias](cref:Meziantou.Framework.Yaml.Serialization.YamlReader.Alias) give its properties.
- Report invalid content with a [YamlException](cref:Meziantou.Framework.Yaml.YamlException), built with `reader.SourceName`, `reader.Start` and `reader.End`, so
  that the message gives the [position of the error](../08-ErrorHandling.md).
- [Write](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter{T}.Write) uses the [YamlWriter](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter): [WriteString](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteString), [WriteScalar](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteScalar) (with an overload for each built-in type),
  [WriteNullValue](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteNullValue), [WriteStartMapping](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteStartMapping), [WritePropertyName](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WritePropertyName), [WriteEndMapping](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteEndMapping), [WriteStartSequence](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteStartSequence),
  [WriteEndSequence](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteEndSequence), [WriteTag](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteTag), [WriteAnchor](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteAnchor) and [WriteAlias](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.WriteAlias).

The static class [YamlScalar](cref:Meziantou.Framework.Yaml.Serialization.YamlScalar) exposes the parsers of the built-in converters, such as [TryParseInt64](cref:Meziantou.Framework.Yaml.Serialization.YamlScalar.TryParseInt64),
[TryParseDouble](cref:Meziantou.Framework.Yaml.Serialization.YamlScalar.TryParseDouble) and [IsNull](cref:Meziantou.Framework.Yaml.Serialization.YamlScalar.IsNull), to read scalars the same way.

A converter also receives the null scalars (`~`, `null`) of its type: when `null` is valid for the type, check
[YamlScalar.IsNull](cref:Meziantou.Framework.Yaml.Serialization.YamlScalar.IsNull) in [Read](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter{T}.Read), as in the [generic example below](#generic-types).

## Register a converter

A converter applies where it is registered:

- `[YamlConverter(typeof(TemperatureConverter))]` on the type, as above: everywhere the type is used.
- [\[YamlConverter\]](cref:Meziantou.Framework.Yaml.Serialization.YamlConverterAttribute) on a property or a field: that member only.
- [YamlSerializerOptions.Converters](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.Converters), for instance `Converters = [new TemperatureConverter()]`: every serialization
  that uses these options. The converters are tried in order, before the built-in ones, so they can also replace how
  a built-in type is written.
- [YamlSourceGenerationOptionsAttribute.Converters](cref:Meziantou.Framework.Yaml.Serialization.YamlSourceGenerationOptionsAttribute.Converters) for a [generated context](../04-SourceGeneration.md).

A converter registered for a type that is also used as a dictionary key converts the values of the dictionary, not its
keys: keys of the [supported key types](03-SupportedTypes.md#collections-and-dictionaries) are always written as
scalars.

## Generic types

[\[YamlConverter\]](cref:Meziantou.Framework.Yaml.Serialization.YamlConverterAttribute) accepts an open generic converter on a generic type with the same number of type parameters. The
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

To handle a family of types with a converter created for each type, derive from [YamlConverterFactory](cref:Meziantou.Framework.Yaml.Serialization.YamlConverterFactory): [CanConvert](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter.CanConvert)
tells whether the factory handles a type, and [CreateConverter](cref:Meziantou.Framework.Yaml.Serialization.YamlConverterFactory.CreateConverter) returns the converter for it. Add the factory to
[YamlSerializerOptions.Converters](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.Converters).

## Populate existing values

Override [CanPopulate](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter.CanPopulate) and [Populate](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter.Populate) to support
[`YamlObjectCreationHandling.Populate`](06-ReadingBehavior.md#fill-existing-collections): [Populate](cref:Meziantou.Framework.Yaml.Serialization.YamlConverter.Populate) receives the value
that the member already holds and reads the YAML into it.
