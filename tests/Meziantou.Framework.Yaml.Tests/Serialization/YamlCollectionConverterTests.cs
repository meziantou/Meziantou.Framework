namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlCollectionConverterTests
{
    private sealed class EnumerableModel
    {
        public IEnumerable<int> Values { get; set; } = Array.Empty<int>();
    }

    [Fact]
    public void Array_RoundTrip()
    {
        var data = new[] { 1, 2, 3 };
        var yaml = YamlSerializer.Serialize(data);
        var roundTrip = YamlSerializer.Deserialize<int[]>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal(data, roundTrip);
    }

    [Fact]
    public void IEnumerable_RoundTripThroughObjectProperty()
    {
        var model = new EnumerableModel { Values = new[] { 1, 2, 3 } };
        var yaml = YamlSerializer.Serialize(model);

        var deserialized = YamlSerializer.Deserialize<EnumerableModel>(yaml);
        Assert.NotNull(deserialized);

        var values = deserialized.Values.ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, values);
    }

    [Fact]
    public void DictionaryKeyPolicy_AppliesToSerializedKeys()
    {
        var options = new YamlSerializerOptions { DictionaryKeyPolicy = YamlNamingPolicy.CamelCase };
        var yaml = YamlSerializer.Serialize(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["MyKey"] = 1,
                ["OtherKey"] = 2,
            },
            options);

        Assert.Contains("myKey: 1", yaml);
        Assert.Contains("otherKey: 2", yaml);
        Assert.DoesNotContain("MyKey:", yaml);
        Assert.DoesNotContain("OtherKey:", yaml);
    }

    [Fact]
    public void Indentation_RespectsIndentSize()
    {
        var yaml = YamlSerializer.Serialize(
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal)
            {
                ["outer"] = new Dictionary<string, int>(StringComparer.Ordinal) { ["inner"] = 1 },
            },
            new YamlSerializerOptions { IndentSize = 4 });

        Assert.Contains("outer:\n    inner: 1\n", yaml);
    }
}
