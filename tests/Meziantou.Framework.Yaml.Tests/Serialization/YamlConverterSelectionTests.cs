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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildTimeConverterForUnderlyingType_HandlesNullableValues(bool useSourceGeneration)
    {
        var options = useSourceGeneration
            ? NullableBuildTimeConverterYamlContext.Default.Options
            : new YamlSerializerOptions { Converters = [new Plus1000Int32Converter()] };

        AssertNullableValuesUsePlus1000Converter(options);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeConverterForUnderlyingType_HandlesNullableValues(bool useSourceGeneration)
    {
        var options = useSourceGeneration
            ? NullableRuntimeConverterYamlContext.Default.CreateOptions(static options => options with { Converters = [new Plus1000Int32Converter()] })
            : new YamlSerializerOptions { Converters = [new Plus1000Int32Converter()] };

        AssertNullableValuesUsePlus1000Converter(options);
    }

    private static void AssertNullableValuesUsePlus1000Converter(YamlSerializerOptions options)
    {
        Assert.Equal("null\n", YamlSerializer.Serialize<int?>(null, options));
        Assert.Equal("1003\n", YamlSerializer.Serialize<int?>(3, options));
        Assert.Null(YamlSerializer.Deserialize<int?>("~\n", options));
        Assert.Equal(1042, YamlSerializer.Deserialize<int?>("42\n", options));

        Assert.Equal("Value: null\n", YamlSerializer.Serialize(new NullableInt32Holder(), options));
        Assert.Equal("Value: 1003\n", YamlSerializer.Serialize(new NullableInt32Holder { Value = 3 }, options));
        Assert.Null(YamlSerializer.Deserialize<NullableInt32Holder>("Value: ~\n", options)!.Value);
        Assert.Equal(1042, YamlSerializer.Deserialize<NullableInt32Holder>("Value: 42\n", options)!.Value);

        Assert.Equal("- 1001\n- null\n", YamlSerializer.Serialize(new List<int?> { 1, null }, options));
        Assert.Equal(new int?[] { 1001, null }, YamlSerializer.Deserialize<List<int?>>("- 1\n- ~\n", options)!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dictionary_KeysDoNotUseCustomConverters(bool useSourceGeneration)
    {
        var options = useSourceGeneration
            ? ConverterSelectionYamlContext.Default.CreateOptions(o => o with { Converters = [new OffsetInt32Converter()] })
            : new YamlSerializerOptions { Converters = [new OffsetInt32Converter()] };

        var yaml = YamlSerializer.Serialize(new IntKeyDictionaryModel { Values = new Dictionary<int, int> { [1] = 2 } }, options);
        Assert.Equal("Values:\n  1: 102\n", yaml);
        Assert.Equal("1: 102\n", YamlSerializer.Serialize(new Dictionary<int, int> { [1] = 2 }, options));

        var model = YamlSerializer.Deserialize<IntKeyDictionaryModel>(yaml, options);
        Assert.NotNull(model);
        Assert.Equal(new KeyValuePair<int, int>(1, 2), Assert.Single(model.Values!));

        var dictionary = YamlSerializer.Deserialize<Dictionary<int, int>>("1: 102\n", options);
        Assert.NotNull(dictionary);
        Assert.Equal(2, dictionary[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dictionary_KeysDoNotUseSourceGenerationOptionsConverters(bool useSourceGeneration)
    {
        var options = useSourceGeneration
            ? ConverterSelectionWithConvertersYamlContext.Default.Options
            : new YamlSerializerOptions { Converters = [new OffsetInt32Converter()] };

        var yaml = YamlSerializer.Serialize(new IntKeyDictionaryModel { Values = new Dictionary<int, int> { [1] = 2 } }, options);
        Assert.Equal("Values:\n  1: 102\n", yaml);

        var model = YamlSerializer.Deserialize<IntKeyDictionaryModel>(yaml, options);
        Assert.NotNull(model);
        Assert.Equal(new KeyValuePair<int, int>(1, 2), Assert.Single(model.Values!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dictionary_KeysDoNotUseTypeLevelConverter(bool useSourceGeneration)
    {
        var value = new EnumKeyDictionaryModel { Values = new Dictionary<PrefixedColor, int> { [PrefixedColor.Green] = 1 } };
        var yaml = useSourceGeneration
            ? YamlSerializer.Serialize(value, ConverterSelectionYamlContext.Default)
            : YamlSerializer.Serialize(value);

        Assert.Equal("Values:\n  Green: 1\n", yaml);

        var model = useSourceGeneration
            ? YamlSerializer.Deserialize<EnumKeyDictionaryModel>(yaml, ConverterSelectionYamlContext.Default)
            : YamlSerializer.Deserialize<EnumKeyDictionaryModel>(yaml);

        Assert.NotNull(model);
        Assert.Equal(new KeyValuePair<PrefixedColor, int>(PrefixedColor.Green, 1), Assert.Single(model.Values!));
    }

    internal sealed class IntKeyDictionaryModel
    {
        public Dictionary<int, int>? Values { get; set; }
    }

    internal sealed class EnumKeyDictionaryModel
    {
        public Dictionary<PrefixedColor, int>? Values { get; set; }
    }

    [YamlConverter(typeof(PrefixedColorConverter))]
    internal enum PrefixedColor
    {
        Red,
        Green,
    }

    internal sealed class PrefixedColorConverter : YamlConverter<PrefixedColor>
    {
        private const string Prefix = "color-";

        public override PrefixedColor Read(YamlReader reader)
        {
            var scalar = reader.GetScalarValue();
            reader.Read();
            return Enum.Parse<PrefixedColor>(scalar[Prefix.Length..]);
        }

        public override void Write(YamlWriter writer, PrefixedColor value) => writer.WriteScalar(Prefix + value.ToString());
    }

    internal sealed class OffsetInt32Converter : YamlConverter<int>
    {
        public override int Read(YamlReader reader)
        {
            var scalar = reader.GetScalarValue();
            reader.Read();
            return int.Parse(scalar, CultureInfo.InvariantCulture) - 100;
        }

        public override void Write(YamlWriter writer, int value) => writer.WriteScalar(value + 100);
    }
}

#pragma warning disable MA0048 // File name must match type name
internal sealed class Plus1000Int32Converter : YamlConverter<int>
{
    public override int Read(YamlReader reader)
    {
        var value = int.Parse(reader.ScalarValue!, CultureInfo.InvariantCulture);
        reader.Skip();
        return value + 1000;
    }

    public override void Write(YamlWriter writer, int value) => writer.WriteScalar(value + 1000);
}

internal sealed class NullableInt32Holder
{
    public int? Value { get; set; }
}

[YamlSourceGenerationOptions(Converters = [typeof(Plus1000Int32Converter)])]
[YamlSerializable(typeof(int?))]
[YamlSerializable(typeof(NullableInt32Holder))]
[YamlSerializable(typeof(List<int?>))]
internal sealed partial class NullableBuildTimeConverterYamlContext : YamlSerializerContext
{
}

[YamlSerializable(typeof(int?))]
[YamlSerializable(typeof(NullableInt32Holder))]
[YamlSerializable(typeof(List<int?>))]
internal sealed partial class NullableRuntimeConverterYamlContext : YamlSerializerContext
{
}

[YamlSerializable(typeof(YamlConverterSelectionTests.IntKeyDictionaryModel))]
[YamlSerializable(typeof(YamlConverterSelectionTests.EnumKeyDictionaryModel))]
[YamlSerializable(typeof(Dictionary<int, int>))]
internal sealed partial class ConverterSelectionYamlContext : YamlSerializerContext
{
}

[YamlSourceGenerationOptions(Converters = [typeof(YamlConverterSelectionTests.OffsetInt32Converter)])]
[YamlSerializable(typeof(YamlConverterSelectionTests.IntKeyDictionaryModel))]
internal sealed partial class ConverterSelectionWithConvertersYamlContext : YamlSerializerContext
{
}
