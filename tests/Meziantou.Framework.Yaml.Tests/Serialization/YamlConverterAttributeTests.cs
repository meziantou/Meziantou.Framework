using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlConverterAttributeTests
{
    [Fact]
    public void Deserialize_UsesPropertyLevelConverter()
    {
        var value = YamlSerializer.Deserialize<PropertyLevelModel>("A: 1\n")!;
        Assert.Equal(2, value.A);
    }

    [Fact]
    public void Serialize_UsesPropertyLevelConverter()
    {
        var yaml = YamlSerializer.Serialize(new PropertyLevelModel { A = 1 });
        Assert.Contains("A: 2", yaml);
    }

    [Fact]
    public void Serialize_UsesTypeLevelConverter()
    {
        var yaml = YamlSerializer.Serialize(new TypeLevelContainer { Value = new CustomScalar { Text = "hello" } });

        Assert.Contains("Value: hello", yaml);
        Assert.DoesNotContain("Text:", yaml);
    }

    [Fact]
    public void Deserialize_UsesTypeLevelConverter()
    {
        var value = YamlSerializer.Deserialize<TypeLevelContainer>("Value: hello\n")!;
        Assert.NotNull(value.Value);
        Assert.Equal("hello", value.Value!.Text);
    }

    private sealed class PropertyLevelModel
    {
        [YamlConverter(typeof(IncrementIntConverter))]
        public int A { get; set; }
    }

    private sealed class IncrementIntConverter : YamlConverter<int>
    {
        public override int Read(YamlReader reader)
        {
            var scalar = reader.GetScalarValue();
            reader.Read();
            return int.Parse(scalar, CultureInfo.InvariantCulture) + 1;
        }

        public override void Write(YamlWriter writer, int value)
        {
            writer.WriteScalar((value + 1).ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class TypeLevelContainer
    {
        public CustomScalar? Value { get; set; }
    }

    [YamlConverter(typeof(CustomScalarConverter))]
    private sealed class CustomScalar
    {
        public string? Text { get; set; }
    }

    private sealed class CustomScalarConverter : YamlConverter<CustomScalar?>
    {
        public override CustomScalar? Read(YamlReader reader)
        {
            if (reader.TokenType is YamlTokenType.Scalar && YamlScalar.IsNull(reader.ScalarValue.AsSpan()))
            {
                reader.Read();
                return null;
            }

            var scalar = reader.GetScalarValue();
            reader.Read();
            return new CustomScalar { Text = scalar };
        }

        public override void Write(YamlWriter writer, CustomScalar? value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteScalar(value.Text);
        }
    }
}
