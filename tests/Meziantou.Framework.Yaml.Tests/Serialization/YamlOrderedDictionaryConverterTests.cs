using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

public sealed class YamlOrderedDictionaryConverterTests
{
    [Fact]
    public void Reflection_Deserialize_StringKeyOrderedDictionary()
    {
        const string Yaml = """
            alice: 100
            bob: 200
            charlie: 300
            """;

        var result = YamlSerializer.Deserialize<OrderedDictionary<string, int>>(Yaml);

        Assert.NotNull(result);
        Assert.HasCount(3, result);
        Assert.Equal(100, result["alice"]);
        Assert.Equal(200, result["bob"]);
        Assert.Equal(300, result["charlie"]);

        // Verify order is preserved
        var keys = new List<string>(result.Keys);
        Assert.Equal(new[] { "alice", "bob", "charlie" }, keys);
    }

    [Fact]
    public void Reflection_Deserialize_GenericKeyOrderedDictionary()
    {
        const string Yaml = """
            1: one
            2: two
            3: three
            """;

        var result = YamlSerializer.Deserialize<OrderedDictionary<int, string>>(Yaml);

        Assert.NotNull(result);
        Assert.HasCount(3, result);
        Assert.Equal("one", result[1]);
        Assert.Equal("two", result[2]);
        Assert.Equal("three", result[3]);
    }

    [Fact]
    public void Reflection_Serialize_StringKeyOrderedDictionary()
    {
        var dict = new OrderedDictionary<string, int>(StringComparer.Ordinal)
        {
            { "alice", 100 },
            { "bob", 200 },
            { "charlie", 300 },
        };

        var yaml = YamlSerializer.Serialize(dict);

        Assert.NotNull(yaml);
        Assert.Contains("alice: 100", yaml);
        Assert.Contains("bob: 200", yaml);
        Assert.Contains("charlie: 300", yaml);
    }

    [Fact]
    public void Reflection_Serialize_GenericKeyOrderedDictionary()
    {
        var dict = new OrderedDictionary<int, string>
        {
            { 1, "one" },
            { 2, "two" },
            { 3, "three" },
        };

        var yaml = YamlSerializer.Serialize(dict);

        Assert.NotNull(yaml);
        Assert.Contains("1: one", yaml);
        Assert.Contains("2: two", yaml);
        Assert.Contains("3: three", yaml);
    }

    [Fact]
    public void Reflection_RoundTrip_PreservesOrder()
    {
        var original = new OrderedDictionary<string, int>(StringComparer.Ordinal)
        {
            { "zebra", 1 },
            { "apple", 2 },
            { "mango", 3 },
        };

        var yaml = YamlSerializer.Serialize(original);
        var result = YamlSerializer.Deserialize<OrderedDictionary<string, int>>(yaml);

        Assert.NotNull(result);
        Assert.HasCount(3, result);

        var originalKeys = new List<string>(original.Keys);
        var resultKeys = new List<string>(result.Keys);
        Assert.Equal(originalKeys, resultKeys);
    }

    [Fact]
    public void Reflection_RoundTrip_StringKeyOrderedDictionary_ShouldPreserveSharedReferences()
    {
        var shared = new OrderedDictionary<string, int>(StringComparer.Ordinal)
        {
            { "zebra", 1 },
            { "apple", 2 },
        };

        var payload = new OrderedDictionaryReferenceModel
        {
            Primary = shared,
            Secondary = shared,
        };

        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
        var yaml = YamlSerializer.Serialize(payload, options);

        var anchor = ExtractAnchor(yaml, "Primary: &");
        Assert.Contains($"Secondary: *{anchor}", yaml);

        var result = YamlSerializer.Deserialize<OrderedDictionaryReferenceModel>(yaml, options);

        Assert.NotNull(result);
        Assert.NotNull(result.Primary);
        Assert.NotNull(result.Secondary);
        Assert.True(ReferenceEquals(result.Primary, result.Secondary));

        var keys = new List<string>(result.Primary.Keys);
        Assert.Equal(new[] { "zebra", "apple" }, keys);
    }

    [Fact]
    public void Reflection_RoundTrip_GenericKeyOrderedDictionary_ShouldPreserveSharedReferences()
    {
        var shared = new OrderedDictionary<int, string>
        {
            { 3, "three" },
            { 1, "one" },
        };

        var payload = new OrderedDictionaryGenericReferenceModel
        {
            Primary = shared,
            Secondary = shared,
        };

        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
        var yaml = YamlSerializer.Serialize(payload, options);

        var anchor = ExtractAnchor(yaml, "Primary: &");
        Assert.Contains($"Secondary: *{anchor}", yaml);

        var result = YamlSerializer.Deserialize<OrderedDictionaryGenericReferenceModel>(yaml, options);

        Assert.NotNull(result);
        Assert.NotNull(result.Primary);
        Assert.NotNull(result.Secondary);
        Assert.True(ReferenceEquals(result.Primary, result.Secondary));

        var keys = new List<int>(result.Primary.Keys);
        Assert.Equal(new[] { 3, 1 }, keys);
    }

    [Fact]
    public void Reflection_Deserialize_NullValue()
    {
        const string Yaml = "~";

        var result = YamlSerializer.Deserialize<OrderedDictionary<string, int>>(Yaml);

        Assert.Null(result);
    }

    [Fact]
    public void Reflection_Serialize_NullValue()
    {
        OrderedDictionary<string, int>? dict = null;

        var yaml = YamlSerializer.Serialize(dict);

        Assert.NotNull(yaml);
    }

    [Fact]
    public void Reflection_Deserialize_AsObjectProperty()
    {
        const string Yaml = """
            Scores:
              alice: 100
              bob: 200
            """;

        var result = YamlSerializer.Deserialize<OrderedDictionaryModel>(Yaml);

        Assert.NotNull(result);
        Assert.HasCount(2, result.Scores);
        Assert.Equal(100, result.Scores["alice"]);
        Assert.Equal(200, result.Scores["bob"]);
    }

    [Fact]
    public void SourceGen_Deserialize_StringKeyOrderedDictionary()
    {
        const string Yaml = """
            alice: 100
            bob: 200
            charlie: 300
            """;

        var context = OrderedDictionaryTestContext.Default;
        var result = YamlSerializer.Deserialize<OrderedDictionary<string, int>>(Yaml, context);

        Assert.NotNull(result);
        Assert.HasCount(3, result);
        Assert.Equal(100, result["alice"]);
        Assert.Equal(200, result["bob"]);
        Assert.Equal(300, result["charlie"]);

        // Verify order is preserved
        var keys = new List<string>(result.Keys);
        Assert.Equal(new[] { "alice", "bob", "charlie" }, keys);
    }

    [Fact]
    public void SourceGen_Serialize_StringKeyOrderedDictionary()
    {
        var dict = new OrderedDictionary<string, int>(StringComparer.Ordinal)
        {
            { "alice", 100 },
            { "bob", 200 },
            { "charlie", 300 },
        };

        var context = OrderedDictionaryTestContext.Default;
        var yaml = YamlSerializer.Serialize(dict, context);

        Assert.NotNull(yaml);
        Assert.Contains("alice: 100", yaml);
        Assert.Contains("bob: 200", yaml);
        Assert.Contains("charlie: 300", yaml);
    }

    [Fact]
    public void SourceGen_Deserialize_GenericKeyOrderedDictionary()
    {
        const string Yaml = """
            1: one
            2: two
            3: three
            """;

        var context = OrderedDictionaryTestContext.Default;
        var result = YamlSerializer.Deserialize<OrderedDictionary<int, string>>(Yaml, context);

        Assert.NotNull(result);
        Assert.HasCount(3, result);
        Assert.Equal("one", result[1]);
        Assert.Equal("two", result[2]);
        Assert.Equal("three", result[3]);
    }

    [Fact]
    public void SourceGen_RoundTrip_PreservesOrder()
    {
        var original = new OrderedDictionary<string, string>(StringComparer.Ordinal)
        {
            { "zebra", "first" },
            { "apple", "second" },
            { "mango", "third" },
        };

        var context = OrderedDictionaryTestContext.Default;
        var yaml = YamlSerializer.Serialize(original, context);
        var result = YamlSerializer.Deserialize<OrderedDictionary<string, string>>(yaml, context);

        Assert.NotNull(result);
        Assert.HasCount(3, result);

        var originalKeys = new List<string>(original.Keys);
        var resultKeys = new List<string>(result.Keys);
        Assert.Equal(originalKeys, resultKeys);

        var originalValues = new List<string>(original.Values);
        var resultValues = new List<string>(result.Values);
        Assert.Equal(originalValues, resultValues);
    }

    [Fact]
    public void SourceGen_Deserialize_AsObjectProperty()
    {
        const string Yaml = """
            scores:
              alice: 100
              bob: 200
            """;

        var context = OrderedDictionaryTestContext.Default;
        var result = YamlSerializer.Deserialize<OrderedDictionaryModel>(Yaml, context);

        Assert.NotNull(result);
        Assert.HasCount(2, result.Scores);
        Assert.Equal(100, result.Scores["alice"]);
        Assert.Equal(200, result.Scores["bob"]);
    }

    [Fact]
    public void SourceGen_Deserialize_WithCamelCaseNaming()
    {
        const string Yaml = """
            alice: 100
            bob: 200
            """;

        var context = OrderedDictionaryTestContext.Default;
        var result = YamlSerializer.Deserialize<OrderedDictionary<string, int>>(Yaml, context);

        Assert.NotNull(result);
        Assert.HasCount(2, result);
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

#pragma warning disable MA0048 // File name must match type name
internal sealed class OrderedDictionaryModel
{
    public OrderedDictionary<string, int> Scores { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class OrderedDictionaryReferenceModel
{
    public OrderedDictionary<string, int>? Primary { get; set; }

    public OrderedDictionary<string, int>? Secondary { get; set; }
}

internal sealed class OrderedDictionaryGenericReferenceModel
{
    public OrderedDictionary<int, string>? Primary { get; set; }

    public OrderedDictionary<int, string>? Secondary { get; set; }
}

[YamlSourceGenerationOptions(PropertyNamingPolicy = YamlKnownNamingPolicy.CamelCase)]
[YamlSerializable(typeof(OrderedDictionary<string, int>))]
[YamlSerializable(typeof(OrderedDictionary<string, string>))]
[YamlSerializable(typeof(OrderedDictionary<int, string>))]
[YamlSerializable(typeof(OrderedDictionaryModel))]
internal sealed partial class OrderedDictionaryTestContext : YamlSerializerContext
{
}
