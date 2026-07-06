using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlOptionsValidationTests
{
    private sealed class StringOnlyConverter : YamlConverter<string>
    {
        public override string Read(YamlReader reader) => throw new NotSupportedException();

        public override void Write(YamlWriter writer, string value) => throw new NotSupportedException();
    }

    private sealed class BadFactoryReturnsNull : YamlConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

        public override YamlConverter CreateConverter(Type typeToConvert, YamlSerializerOptions options) => null!;
    }

    private sealed class BadFactoryWrongConverter : YamlConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

        public override YamlConverter CreateConverter(Type typeToConvert, YamlSerializerOptions options) => new StringOnlyConverter();
    }

    [Fact]
    public void Options_IndentSize_MustBeAtLeastOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlSerializerOptions { IndentSize = 0 });
    }

    [Fact]
    public void Options_MaxDepth_CannotBeNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlSerializerOptions { MaxDepth = -1 });
    }

    [Fact]
    public void Options_BlockSequenceItemStyles_MustBeKnownValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlSerializerOptions { BlockSequenceMappingStyle = (YamlSequenceItemStyle)123 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlSerializerOptions { BlockSequenceSequenceStyle = (YamlSequenceItemStyle)123 });
    }

    [Fact]
    public void Options_Converters_CannotBeNullOrContainNull()
    {
        Assert.Throws<ArgumentNullException>(() => new YamlSerializerOptions { Converters = null! });
        Assert.Throws<ArgumentException>(() => new YamlSerializerOptions { Converters = new List<YamlConverter> { null! } });
    }

    [Fact]
    public void ConverterFactory_MustReturnValidConverter()
    {
        var options1 = new YamlSerializerOptions
        {
            Converters = new YamlConverter[] { new BadFactoryReturnsNull() },
        };
        Assert.Throws<InvalidOperationException>(() => new YamlWriter(new StringBuilder(), options1).GetConverter(typeof(int)));

        var options2 = new YamlSerializerOptions
        {
            Converters = new YamlConverter[] { new BadFactoryWrongConverter() },
        };
        Assert.Throws<InvalidOperationException>(() => new YamlWriter(new StringBuilder(), options2).GetConverter(typeof(int)));
    }

    [Fact]
    public void NamingPolicy_CamelCase_ConvertsOnlyWhenLeadingUppercase()
    {
        Assert.Equal(string.Empty, YamlNamingPolicy.CamelCase.ConvertName(string.Empty));
        Assert.Equal("already", YamlNamingPolicy.CamelCase.ConvertName("already"));
        Assert.Equal("hello", YamlNamingPolicy.CamelCase.ConvertName("Hello"));
        Assert.Equal("urlValue", YamlNamingPolicy.CamelCase.ConvertName("URLValue"));
    }

    [Fact]
    public void PolymorphismOptions_ValidateDiscriminatorStyle()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlPolymorphismOptions
        {
            DiscriminatorStyle = YamlTypeDiscriminatorStyle.Unspecified,
        });
    }

    [Fact]
    public void PolymorphismOptions_ValidatePropertyName()
    {
        Assert.Throws<ArgumentException>(() => new YamlPolymorphismOptions { TypeDiscriminatorPropertyName = string.Empty });
        Assert.Throws<ArgumentException>(() => new YamlPolymorphismOptions { TypeDiscriminatorPropertyName = null! });
    }
}
