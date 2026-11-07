using System;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable MA0048 // File name must match type name
internal sealed class GeneratedUppercaseConverter : TomlConverter<string>
{
    public override string? Read(TomlReader reader)
    {
        var value = reader.GetString();
        reader.Read();
        return value.ToUpperInvariant();
    }

    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value.ToUpperInvariant());
}

[TomlConverter(typeof(GeneratedHexConverter))]
internal sealed class GeneratedHexValue
{
    public int Value { get; init; }
}

internal sealed class GeneratedHexConverter : TomlConverter<GeneratedHexValue>
{
    public override GeneratedHexValue? Read(TomlReader reader)
    {
        var value = int.Parse(reader.GetString(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        reader.Read();
        return new GeneratedHexValue { Value = value };
    }

    public override void Write(TomlWriter writer, GeneratedHexValue value) => writer.WriteStringValue(value.Value.ToString("x", System.Globalization.CultureInfo.InvariantCulture));
}

internal enum GeneratedColor
{
    Red,
    Green,
}

internal sealed class GeneratedColorNameConverter : TomlConverter<GeneratedColor>
{
    public override GeneratedColor Read(TomlReader reader)
    {
        var value = Enum.Parse<GeneratedColor>(reader.GetString());
        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, GeneratedColor value) => writer.WriteStringValue(value.ToString());
}

internal sealed class GeneratedColorConverterFactory : TomlConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(GeneratedColor);

    public override TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options) => new GeneratedColorNameConverter();
}

internal sealed class GeneratedConverterModel
{
    [TomlConverter(typeof(GeneratedUppercaseConverter))]
    public string Name { get; set; } = "";

    public GeneratedHexValue Hex { get; set; } = new();

    [TomlConverter(typeof(TomlStringEnumConverter))]
    public GeneratedColor Color { get; set; }
}

internal sealed record GeneratedConverterRecord([property: TomlConverter(typeof(GeneratedUppercaseConverter))] string Name);

internal sealed class GeneratedFactoryModel
{
    [TomlConverter(typeof(GeneratedColorConverterFactory))]
    public GeneratedColor Color { get; set; }
}

internal sealed class GeneratedNullableConverterModel
{
    [TomlConverter(typeof(TomlStringEnumConverter))]
    public GeneratedColor? StringEnum { get; set; }

    [TomlConverter(typeof(GeneratedColorNameConverter))]
    public GeneratedColor? Converter { get; set; }

    [TomlConverter(typeof(GeneratedColorConverterFactory))]
    public GeneratedColor? Factory { get; set; }
}

[TomlSerializable(typeof(GeneratedConverterModel))]
[TomlSerializable(typeof(GeneratedNullableConverterModel))]
[TomlSerializable(typeof(GeneratedConverterRecord))]
[TomlSerializable(typeof(GeneratedFactoryModel))]
internal sealed partial class GeneratedConverterContext : TomlSerializerContext;
#pragma warning restore MA0048

public sealed class NewApiConverterAttributeTests
{
    [Fact]
    public void SourceGenerated_ConverterAttributes_MatchReflection()
    {
        var model = new GeneratedConverterModel { Name = "abc", Hex = new GeneratedHexValue { Value = 0x5c }, Color = GeneratedColor.Green };

        var reflection = TomlSerializer.Serialize(model);
        var generated = TomlSerializer.Serialize(model, GeneratedConverterContext.Default.GeneratedConverterModel);

        Assert.Equal("Name = \"ABC\"\nHex = \"5c\"\nColor = \"Green\"\n", reflection);
        Assert.Equal(reflection, generated);

        var roundtrip = TomlSerializer.Deserialize("Name = \"xyz\"\nHex = \"7c\"\nColor = \"Green\"\n", GeneratedConverterContext.Default.GeneratedConverterModel)!;
        Assert.Equal("XYZ", roundtrip.Name);
        Assert.Equal(0x7c, roundtrip.Hex.Value);
        Assert.Equal(GeneratedColor.Green, roundtrip.Color);
    }

    [Fact]
    public void ConverterForT_IsUsedOnANullableMember()
    {
        var model = new GeneratedNullableConverterModel { StringEnum = GeneratedColor.Green, Converter = GeneratedColor.Red, Factory = GeneratedColor.Green };
        var typeInfo = GeneratedConverterContext.Default.GeneratedNullableConverterModel;

        var reflection = TomlSerializer.Serialize(model);
        var generated = TomlSerializer.Serialize(model, typeInfo);

        Assert.Equal("StringEnum = \"Green\"\nConverter = \"Red\"\nFactory = \"Green\"\n", reflection);
        Assert.Equal(reflection, generated);
        Assert.Equal("", TomlSerializer.Serialize(new GeneratedNullableConverterModel(), typeInfo));
        foreach (var roundtrip in new[] { TomlSerializer.Deserialize<GeneratedNullableConverterModel>(reflection)!, TomlSerializer.Deserialize(reflection, typeInfo)! })
        {
            Assert.Equal(GeneratedColor.Green, roundtrip.StringEnum);
            Assert.Equal(GeneratedColor.Red, roundtrip.Converter);
            Assert.Equal(GeneratedColor.Green, roundtrip.Factory);
        }
    }

    [Fact]
    public void SourceGenerated_ConstructorParameter_UsesLinkedMemberConverter()
    {
        var value = TomlSerializer.Deserialize("Name = \"abc\"\n", GeneratedConverterContext.Default.GeneratedConverterRecord);

        Assert.Equal("ABC", value?.Name);
        Assert.Equal(value, TomlSerializer.Deserialize<GeneratedConverterRecord>("Name = \"abc\"\n"));
    }

    [Fact]
    public void SourceGenerated_MemberConverterFactory_IsUsed()
    {
        var typeInfo = GeneratedConverterContext.Default.GeneratedFactoryModel;

        var toml = TomlSerializer.Serialize(new GeneratedFactoryModel { Color = GeneratedColor.Green }, typeInfo);

        Assert.Equal("Color = \"Green\"\n", toml);
        Assert.Equal(toml, TomlSerializer.Serialize(new GeneratedFactoryModel { Color = GeneratedColor.Green }));
        Assert.Equal(GeneratedColor.Green, TomlSerializer.Deserialize(toml, typeInfo)?.Color);
    }

    private sealed class UppercaseStringConverter : TomlConverter<string>
    {
        public override string? Read(TomlReader reader)
        {
            var value = reader.GetString();
            reader.Read();
            return value.ToUpperInvariant();
        }

        public override void Write(TomlWriter writer, string value)
        {
            writer.WriteStringValue(value.ToUpperInvariant());
        }
    }

    private sealed class UppercaseStringConverterWithoutAdvance : TomlConverter<string>
    {
        public override string? Read(TomlReader reader) => reader.GetString().ToUpperInvariant();

        public override void Write(TomlWriter writer, string value)
        {
            writer.WriteStringValue(value.ToLowerInvariant());
        }
    }

    private sealed class MemberLevelConverterModel
    {
        [TomlConverter(typeof(UppercaseStringConverter))]
        public string Name { get; set; } = "";
    }

    private sealed class MemberLevelTomlConverterWithoutAdvanceModel
    {
        [TomlConverter(typeof(UppercaseStringConverterWithoutAdvance))]
        public string Name { get; set; } = "";

        public string Title { get; set; } = "";
    }

    [TomlConverter(typeof(SpecialTypeConverter))]
    private sealed class SpecialType
    {
        public SpecialType(string value) => Value = value;

        public string Value { get; }
    }

    [TomlConverter(typeof(SpecialTypeConverterWithoutAdvance))]
    private sealed class SpecialTypeWithoutAdvance
    {
        public SpecialTypeWithoutAdvance(string value) => Value = value;

        public string Value { get; }
    }

    private sealed class SpecialTypeConverterWithoutAdvance : TomlConverter<SpecialTypeWithoutAdvance>
    {
        public override SpecialTypeWithoutAdvance? Read(TomlReader reader)
        {
            var value = reader.GetString();
            return new SpecialTypeWithoutAdvance(value);
        }

        public override void Write(TomlWriter writer, SpecialTypeWithoutAdvance value)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class SpecialTypeConverter : TomlConverter<SpecialType>
    {
        public override SpecialType? Read(TomlReader reader)
        {
            var value = reader.GetString();
            reader.Read();
            return new SpecialType(value);
        }

        public override void Write(TomlWriter writer, SpecialType value)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class ThrowingValue
    {
        public ThrowingValue(string value) => Value = value;

        public string Value { get; }
    }

    private sealed class ThrowingValueConverter : TomlConverter<ThrowingValue>
    {
        public override ThrowingValue? Read(TomlReader reader)
        {
            throw new FormatException($"Invalid value '{reader.GetString()}'.");
        }

        public override void Write(TomlWriter writer, ThrowingValue value)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class TypeLevelConverterModel
    {
        public SpecialType Item { get; set; } = new SpecialType("x");
    }

    private sealed class TypeLevelConverterWithoutAdvanceModel
    {
        public SpecialTypeWithoutAdvance Item { get; set; } = new SpecialTypeWithoutAdvance("x");

        public string Title { get; set; } = "";
    }

    private sealed class ThrowingValueModel
    {
        public ThrowingValue First { get; set; } = new ThrowingValue("first");

        public ThrowingValue Second { get; set; } = new ThrowingValue("second");
    }

    [Fact]
    public void MemberLevelTomlConverter_IsUsed()
    {
        var model = new MemberLevelConverterModel { Name = "ada" };
        var toml = TomlSerializer.Serialize(model);
        var roundtrip = TomlSerializer.Deserialize<MemberLevelConverterModel>(toml);

        Assert.NotNull(roundtrip);
        Assert.Equal("ADA", roundtrip!.Name);
    }

    [Fact]
    public void MemberLevelTomlConverter_ReadNeedNotAdvanceReader()
    {
        var roundtrip = TomlSerializer.Deserialize<MemberLevelTomlConverterWithoutAdvanceModel>("Name = 'Mr Poop'\nTitle = 'Sir'");

        Assert.NotNull(roundtrip);
        Assert.Equal("MR POOP", roundtrip!.Name);
        Assert.Equal("Sir", roundtrip.Title);
    }

    [Fact]
    public void TypeLevelTomlConverter_IsUsed()
    {
        var model = new TypeLevelConverterModel { Item = new SpecialType("hello") };
        var toml = TomlSerializer.Serialize(model);
        var roundtrip = TomlSerializer.Deserialize<TypeLevelConverterModel>(toml);

        Assert.NotNull(roundtrip);
        Assert.Equal("hello", roundtrip!.Item.Value);
    }

    [Fact]
    public void TypeLevelTomlConverter_ReadNeedNotAdvanceReader()
    {
        var roundtrip = TomlSerializer.Deserialize<TypeLevelConverterWithoutAdvanceModel>("Item = 'hello'\nTitle = 'Sir'");

        Assert.NotNull(roundtrip);
        Assert.Equal("hello", roundtrip!.Item.Value);
        Assert.Equal("Sir", roundtrip.Title);
    }

    [Fact]
    public void OptionsConverter_ExceptionsAreAggregatedWithLocations()
    {
        var options = new TomlSerializerOptions
        {
            SourceName = "config.toml",
            Converters = [new ThrowingValueConverter()],
        };

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<ThrowingValueModel>("First = 'bad1'\nSecond = 'bad2'\n", options));

        Assert.NotNull(ex);
        Assert.Equal(2, ex!.Diagnostics.Count);
        Assert.Equal(0, ex.Diagnostics[0].Span.Start.Line);
        Assert.Equal(8, ex.Diagnostics[0].Span.Start.Column);
        Assert.Contains("Exception while trying to convert TOML value", ex.Diagnostics[0].Message);
        Assert.Contains("Invalid value 'bad1'.", ex.Diagnostics[0].Message);
        Assert.Equal(1, ex.Diagnostics[1].Span.Start.Line);
        Assert.Equal(9, ex.Diagnostics[1].Span.Start.Column);
        Assert.Contains("Invalid value 'bad2'.", ex.Diagnostics[1].Message);
        Assert.Contains("config.toml(1,9)", ex.Message);
        Assert.Contains("config.toml(2,10)", ex.Message);
    }
}

