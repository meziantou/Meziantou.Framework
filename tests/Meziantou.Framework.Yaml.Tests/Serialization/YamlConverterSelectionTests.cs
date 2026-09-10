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

    [Fact]
    public void Serialize_UsesCustomConverterForBuiltInRootType()
    {
        var options = new YamlSerializerOptions
        {
            Converters = [new Int42Converter()],
        };

        Assert.Equal("42\n", YamlSerializer.Serialize(5, options));
        Assert.Equal("42\n", YamlSerializer.Serialize(5, typeof(int), options));
    }

    [Fact]
    public void Deserialize_UsesCustomConverterForBuiltInRootType()
    {
        var options = new YamlSerializerOptions
        {
            Converters = [new Int42Converter()],
        };

        Assert.Equal(42, YamlSerializer.Deserialize<int>("5", options));
        Assert.Equal(42, (int)YamlSerializer.Deserialize("5", typeof(int), options)!);
    }

    [Fact]
    public void Serialize_UsesConverterFactoryForBuiltInRootType()
    {
        var options = new YamlSerializerOptions
        {
            Converters = [new Int42FactoryConverter()],
        };

        Assert.Equal("42\n", YamlSerializer.Serialize(5, options));
        Assert.Equal(42, YamlSerializer.Deserialize<int>("5", options));
    }

    [Fact]
    public void Serialize_UsesCustomConverterForStringRootType()
    {
        var options = new YamlSerializerOptions
        {
            Converters = [new UpperCaseStringConverter()],
        };

        Assert.Equal("HELLO\n", YamlSerializer.Serialize("hello", options));
        Assert.Equal("HELLO", YamlSerializer.Deserialize<string>("hello", options));
    }

    private sealed class Int42Converter : YamlConverter<int>
    {
        public override int Read(YamlReader reader)
        {
            reader.Skip();
            return 42;
        }

        public override void Write(YamlWriter writer, int value) => writer.WriteScalar(42);
    }

    private sealed class Int42FactoryConverter : YamlConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

        public override YamlConverter CreateConverter(Type typeToConvert, YamlSerializerOptions options) => new Int42Converter();
    }

    private sealed class UpperCaseStringConverter : YamlConverter<string?>
    {
        public override string? Read(YamlReader reader)
        {
            var value = reader.ScalarValue;
            reader.Skip();
            return value?.ToUpperInvariant();
        }

        public override void Write(YamlWriter writer, string? value) => writer.WriteString(value?.ToUpperInvariant());
    }

    [Fact]
    public void GetConverter_ReusesConvertersAcrossReadersAndWritersOfTheSameOptions()
    {
        var options = new YamlSerializerOptions();

        var fromWriter = new YamlWriter(new StringBuilder(), options).GetConverter(typeof(Payload));
        var fromOtherWriter = new YamlWriter(new StringBuilder(), options).GetConverter(typeof(Payload));
        var fromReader = YamlReader.Create("Name: n", options).GetConverter(typeof(Payload));

        Assert.Same(fromWriter, fromOtherWriter);
        Assert.Same(fromWriter, fromReader);
    }

    [Fact]
    public void GetConverter_DoesNotShareConvertersBetweenOptionsInstances()
    {
        var first = new YamlSerializerOptions();
        var second = first with { PropertyNamingPolicy = YamlNamingPolicy.CamelCase };

        var fromFirst = new YamlWriter(new StringBuilder(), first).GetConverter(typeof(Payload));
        var fromSecond = new YamlWriter(new StringBuilder(), second).GetConverter(typeof(Payload));

        Assert.NotSame(fromFirst, fromSecond);
    }

    [Fact]
    public void Serialize_SharedOptionsAreUsableFromSeveralThreads()
    {
        var options = new YamlSerializerOptions();
        var payload = new Payload { Name = "n", Count = 3 };
        const string ExpectedYaml = "Name: n\nCount: 3\n";

        Parallel.For(0, 200, _ =>
        {
            Assert.Equal(ExpectedYaml, YamlSerializer.Serialize(payload, options));

            var roundTrip = YamlSerializer.Deserialize<Payload>(ExpectedYaml, options);
            Assert.NotNull(roundTrip);
            Assert.Equal("n", roundTrip.Name);
            Assert.Equal(3, roundTrip.Count);
        });
    }

    private sealed class Payload
    {
        public string? Name { get; set; }

        public int Count { get; set; }
    }
}
