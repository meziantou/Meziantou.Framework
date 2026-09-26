using System;
using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace Tomlyn.Tests;

public sealed class NewApiConverterAttributeTests
{
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
        [JsonConverter(typeof(UppercaseStringConverter))]
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
    public void MemberLevelJsonConverter_IsUsed()
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

