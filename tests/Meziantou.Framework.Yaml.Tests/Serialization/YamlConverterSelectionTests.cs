using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlConverterSelectionTests
{
    [Fact]
    public void GetConverter_UsesFirstMatchingConverter()
    {
        var options = new YamlSerializerOptions
        {
            Converters =
            [
                new AlwaysInt32Converter("first"),
                new AlwaysInt32Converter("second"),
            ],
        };

        var writer = new YamlWriter(new StringBuilder(), options);
        var converter = writer.GetConverter(typeof(int));

        Assert.IsType<AlwaysInt32Converter>(converter);
        Assert.Equal("first", ((AlwaysInt32Converter)converter).Id);
    }

    [Fact]
    public void GetConverter_ExpandsFactoryConverters()
    {
        var options = new YamlSerializerOptions
        {
            Converters =
            [
                new Int32FactoryConverter(),
            ],
        };

        var writer = new YamlWriter(new StringBuilder(), options);
        var converter = writer.GetConverter(typeof(int));

        Assert.IsType<AlwaysInt32Converter>(converter);
        Assert.Equal("factory", ((AlwaysInt32Converter)converter).Id);
    }

    [Fact]
    public void TryGetCustomConverter_ReturnsFalseWhenNoConverterFound()
    {
        var options = new YamlSerializerOptions();

        var writer = new YamlWriter(new StringBuilder(), options);
        Assert.False(writer.TryGetCustomConverter(typeof(int), out var converter));
        Assert.Null(converter);
    }

    private sealed class AlwaysInt32Converter : YamlConverter<int>
    {
        public AlwaysInt32Converter(string id) => Id = id;

        public string Id { get; }

        public override int Read(YamlReader reader) => 42;

        public override void Write(YamlWriter writer, int value)
        {
        }
    }

    private sealed class Int32FactoryConverter : YamlConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

        public override YamlConverter CreateConverter(Type typeToConvert, YamlSerializerOptions options)
            => new AlwaysInt32Converter("factory");
    }
}
