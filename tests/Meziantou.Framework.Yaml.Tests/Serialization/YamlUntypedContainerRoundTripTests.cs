namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlUntypedContainerRoundTripTests
{
    [Fact]
    public void DictionaryStringObject_RoundTripsNestedUntypedContainers()
    {
        var value = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = new object?[]
            {
                "x",
                2,
                new Dictionary<string, object?>(StringComparer.Ordinal) {
                    ["c"] = true,
                },
            },
            ["c"] = new List<object?>
            {
                null,
                "y",
            },
        };

        var yaml = YamlSerializer.Serialize(value);
        var roundTripped = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml);

        Assert.NotNull(roundTripped);
        Assert.HasCount(3, roundTripped);
        Assert.Equal(1L, roundTripped["a"]);

        var b = (List<object?>)roundTripped["b"]!;
        Assert.HasCount(3, b);
        Assert.Equal("x", b[0]);
        Assert.Equal(2L, b[1]);

        var inner = (Dictionary<string, object?>)b[2]!;
        Assert.Equal(true, inner["c"]);

        var c = (List<object?>)roundTripped["c"]!;
        Assert.HasCount(2, c);
        Assert.Null(c[0]);
        Assert.Equal("y", c[1]);
    }

    [Fact]
    public void ObjectArray_RoundTrips()
    {
        var value = new object?[] { 1, "x", null, true };
        var yaml = YamlSerializer.Serialize(value);
        var roundTripped = YamlSerializer.Deserialize<object[]>(yaml);

        Assert.NotNull(roundTripped);
        Assert.HasCount(4, roundTripped);
        Assert.Equal(1L, roundTripped[0]);
        Assert.Equal("x", roundTripped[1]);
        Assert.Null(roundTripped[2]);
        Assert.Equal(true, roundTripped[3]);
    }
}
