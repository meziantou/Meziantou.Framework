using System;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiOptionsConverterFactoryValidationTests
{
    private sealed class NullFactory : TomlConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

        public override TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options)
            => null!;
    }

    private sealed class SelfFactory : TomlConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

        public override TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options) => this;
    }

    private sealed class WrongTypeConverterFactory : TomlConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

        public override TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options) => new TomlStringConverter();
    }

    private sealed class TomlStringConverter : TomlConverter<string>
    {
        public override string Read(TomlReader reader) => reader.GetString();

        public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value);
    }

    [Fact]
    public void ConverterFactory_ReturnsNull_ThrowsTomlException()
    {
        var options = TomlSerializerOptions.Default with
        {
            Converters = [new NullFactory()],
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(123, options));
    }

    [Fact]
    public void ConverterFactory_ReturnsFactory_ThrowsTomlException()
    {
        var options = TomlSerializerOptions.Default with
        {
            Converters = [new SelfFactory()],
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(123, options));
    }

    [Fact]
    public void ConverterFactory_ReturnsWrongTypeConverter_ThrowsTomlException()
    {
        var options = TomlSerializerOptions.Default with
        {
            Converters = [new WrongTypeConverterFactory()],
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(123, options));
    }
}

