using System.Collections.Immutable;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlSerializerCollectionSupportReflectionTests
{
    [Fact]
    public void Deserialize_IReadOnlyList_ShouldReturnList()
    {
        var yaml = "- 1\n- 2\n";

        var result = YamlSerializer.Deserialize<IReadOnlyList<int>>(yaml);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
    }

    [Fact]
    public void Deserialize_ISet_ShouldReturnHashSet()
    {
        var yaml = "- a\n- b\n- a\n";

        var result = YamlSerializer.Deserialize<ISet<string>>(yaml);

        Assert.NotNull(result);
        Assert.HasCount(2, result);
        Assert.Contains("a", result);
        Assert.Contains("b", result);
    }

    [Fact]
    public void Deserialize_DictionaryWithIntKeys_ShouldParseKeys()
    {
        var yaml = "1: a\n2: b\n";

        var result = YamlSerializer.Deserialize<Dictionary<int, string>>(yaml);

        Assert.NotNull(result);
        Assert.Equal("a", result[1]);
        Assert.Equal("b", result[2]);
    }

    [Fact]
    public void Deserialize_IReadOnlyDictionaryWithEnumKeys_ShouldParseKeys()
    {
        var yaml = "Red: 1\nGreen: 2\n";

        var result = YamlSerializer.Deserialize<IReadOnlyDictionary<TestColor, int>>(yaml);

        Assert.NotNull(result);
        Assert.Equal(1, result[TestColor.Red]);
        Assert.Equal(2, result[TestColor.Green]);
    }

    [Fact]
    public void RoundTrip_IDictionaryNonDictionaryImplementation_ShouldPreserveSharedReferences()
    {
        var shared = new SortedDictionary<int, string>
        {
            [2] = "two",
            [1] = "one",
        };

        var payload = new DictionaryInterfacePayload
        {
            Primary = shared,
            Secondary = shared,
        };

        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
        var yaml = YamlSerializer.Serialize(payload, options);

        var anchor = ExtractAnchor(yaml, "Primary: &");
        Assert.Contains($"Secondary: *{anchor}", yaml);

        var result = YamlSerializer.Deserialize<DictionaryInterfacePayload>(yaml, options);

        Assert.NotNull(result);
        Assert.NotNull(result.Primary);
        Assert.NotNull(result.Secondary);
        Assert.True(ReferenceEquals(result.Primary, result.Secondary));
        Assert.Equal("one", result.Primary[1]);
        Assert.Equal("two", result.Primary[2]);
    }

    [Fact]
    public void RoundTrip_IReadOnlyDictionaryNonDictionaryImplementation_ShouldPreserveSharedReferences()
    {
        var shared = new SortedDictionary<TestColor, int>
        {
            [TestColor.Green] = 2,
            [TestColor.Red] = 1,
        };

        var payload = new ReadOnlyDictionaryInterfacePayload
        {
            Primary = shared,
            Secondary = shared,
        };

        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
        var yaml = YamlSerializer.Serialize(payload, options);

        var anchor = ExtractAnchor(yaml, "Primary: &");
        Assert.Contains($"Secondary: *{anchor}", yaml);

        var result = YamlSerializer.Deserialize<ReadOnlyDictionaryInterfacePayload>(yaml, options);

        Assert.NotNull(result);
        Assert.NotNull(result.Primary);
        Assert.NotNull(result.Secondary);
        Assert.True(ReferenceEquals(result.Primary, result.Secondary));
        Assert.Equal(1, result.Primary[TestColor.Red]);
        Assert.Equal(2, result.Primary[TestColor.Green]);
    }

    [Fact]
    public void Serialize_DictionaryWithGuidKeys_ShouldUseInvariantFormat()
    {
        var id = Guid.Parse("6d0c86e2-1e37-4c33-9c2f-5304a33f2c5e");
        var yaml = YamlSerializer.Serialize(new Dictionary<Guid, int> { [id] = 1 });

        Assert.Contains("6d0c86e2-1e37-4c33-9c2f-5304a33f2c5e:", yaml);
        Assert.Contains("1", yaml);
    }

    [Fact]
    public void Deserialize_ImmutableArray_ShouldRoundTripValues()
    {
        var yaml = "- 10\n- 20\n";

        var result = YamlSerializer.Deserialize<ImmutableArray<int>>(yaml);

        Assert.Equal(2, result.Length);
        Assert.Equal(10, result[0]);
        Assert.Equal(20, result[1]);
    }

    [Fact]
    public void Deserialize_ImmutableList_ShouldRoundTripValues()
    {
        var yaml = "- a\n- b\n";

        var result = YamlSerializer.Deserialize<ImmutableList<string>>(yaml);

        Assert.NotNull(result);
        Assert.HasCount(2, result);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
    }

    [Fact]
    public void Deserialize_ImmutableHashSet_ShouldRoundTripValues()
    {
        var yaml = "- 1\n- 2\n- 1\n";

        var result = YamlSerializer.Deserialize<ImmutableHashSet<int>>(yaml);

        Assert.NotNull(result);
        Assert.HasCount(2, result);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void Deserialize_ImmutableCollections_WithAnchors_ShouldPreserveReferences()
    {
        var yaml =
            "Values: &a\n" +
            "  - 1\n" +
            "  - 2\n" +
            "Other: *a\n";

        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
        var result = YamlSerializer.Deserialize<ImmutableAnchorPayload>(yaml, options);

        Assert.NotNull(result);
        Assert.NotNull(result.Values);
        Assert.NotNull(result.Other);
        Assert.True(ReferenceEquals(result.Values, result.Other));
    }

    [Fact]
    public void Deserialize_ImmutableArray_WithAnchors_ShouldResolveAlias()
    {
        var yaml =
            "Values: &a\n" +
            "  - 10\n" +
            "  - 20\n" +
            "Other: *a\n";

        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
        var result = YamlSerializer.Deserialize<ImmutableArrayAnchorPayload>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Values.Length);
        Assert.Equal(2, result.Other.Length);
        Assert.Equal(result.Values[0], result.Other[0]);
        Assert.Equal(result.Values[1], result.Other[1]);
    }

    internal enum TestColor
    {
        Red = 1,
        Green = 2,
    }

    private sealed class ImmutableAnchorPayload
    {
        public ImmutableList<int>? Values { get; set; }

        public ImmutableList<int>? Other { get; set; }
    }

    private sealed class ImmutableArrayAnchorPayload
    {
        public ImmutableArray<int> Values { get; set; }

        public ImmutableArray<int> Other { get; set; }
    }

    private sealed class DictionaryInterfacePayload
    {
        [SuppressMessage("Performance", "CA1859: Use concrete types when possible for improved performance", Justification = "Test class")]
        public IDictionary<int, string>? Primary { get; set; }

        [SuppressMessage("Performance", "CA1859: Use concrete types when possible for improved performance", Justification = "Test class")]
        public IDictionary<int, string>? Secondary { get; set; }
    }

    private sealed class ReadOnlyDictionaryInterfacePayload
    {
        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "Test class")]
        public IReadOnlyDictionary<TestColor, int>? Primary { get; set; }

        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "Test class")]
        public IReadOnlyDictionary<TestColor, int>? Secondary { get; set; }
    }

    private static string ExtractAnchor(string yaml, string prefix)
    {
        var anchorStart = yaml.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(anchorStart >= 0, $"Expected '{prefix}' in YAML.");
        anchorStart += prefix.Length;

        var anchorEnd = yaml.IndexOf('\n', anchorStart, StringComparison.Ordinal);
        Assert.True(anchorEnd > anchorStart, $"Expected an anchor after '{prefix}'.");

        var anchor = yaml.Substring(anchorStart, anchorEnd - anchorStart).Trim();
        Assert.NotEqual(string.Empty, anchor);
        return anchor;
    }
}
