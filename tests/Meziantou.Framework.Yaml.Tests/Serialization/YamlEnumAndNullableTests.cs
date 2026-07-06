namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlEnumAndNullableTests
{
    private enum Color
    {
        Red = 1,
        Green = 2,
    }

    [Fact]
    public void Enum_ReadsFromNameOrNumber()
    {
        Assert.Equal(Color.Green, YamlSerializer.Deserialize<Color>("green"));
        Assert.Equal(Color.Green, YamlSerializer.Deserialize<Color>("2"));
    }

    [Fact]
    public void Enum_InvalidValue_ThrowsYamlExceptionWithContext()
    {
        var options = new YamlSerializerOptions { SourceName = "colors.yaml" };
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Color>("unknown", options));

        Assert.Equal("colors.yaml", ex.SourceName);
        Assert.True(ex.Start.Index >= 0);
        Assert.Contains("unknown", ex.Message);
    }

    [Fact]
    public void Nullable_Primitives_RoundTrip()
    {
        int? value = 123;
        var yaml = YamlSerializer.Serialize(value);
        Assert.Equal(123, YamlSerializer.Deserialize<int?>(yaml));

        int? nullValue = null;
        var nullYaml = YamlSerializer.Serialize(nullValue);
        Assert.Null(YamlSerializer.Deserialize<int?>(nullYaml));
    }

    [Fact]
    public void Nullable_Elements_RoundTripInSequence()
    {
        var yaml = "- 1\n- null\n- 3\n";
        var values = YamlSerializer.Deserialize<int?[]>(yaml);

        Assert.NotNull(values);
        Assert.Equal(new int?[] { 1, null, 3 }, values);
    }
}

