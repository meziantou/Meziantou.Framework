namespace Meziantou.Framework.Yamlish.Tests;

public sealed class YamlishSerializerTests
{
    [Fact]
    public void Serialize_UsesCSharpNamesByDefault()
    {
        var result = YamlishSerializer.Serialize(new DefaultNamesProduct { Id = "abc", IsAvailable = true, Price = 12.5m });

        Assert.Equal("""
            Id: abc
            IsAvailable: true
            Price: 12.5
            """, result);
    }

    [Fact]
    public void Serialize_UsesSnakeCasePolicyAndAttributes()
    {
        var options = new YamlishSerializerOptions { PropertyNamingPolicy = YamlishNamingPolicy.SnakeCaseLower };

        var result = YamlishSerializer.Serialize(new Product { Id = "abc", IsAvailable = true, Ignored = "secret" }, options);

        Assert.Contains("product_id: abc", result, StringComparison.Ordinal);
        Assert.Contains("is_available: true", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignored", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_ConvertsScalarsAndNestedValues()
    {
        var result = YamlishSerializer.Deserialize<Product>("""
            product_id: abc
            IsAvailable: true
            Price: 12.5
            Tags: [new, sale]
            Dimensions:
              Width: 10
              Height: 20
            """);

        Assert.NotNull(result);
        Assert.Equal("abc", result.Id);
        Assert.True(result.IsAvailable);
        Assert.Equal(12.5m, result.Price);
        Assert.Equal(["new", "sale"], result.Tags);
        Assert.Equal(10, result.Dimensions?.Width);
        Assert.Equal(20, result.Dimensions?.Height);
    }

    [Fact]
    public void SerializeAndDeserialize_Dictionary()
    {
        var value = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["one"] = 1,
            ["two"] = 2,
        };

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<Dictionary<string, int>>(content);

        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain value")]
    [InlineData(" leading and trailing ")]
    [InlineData("quote: \"; slash: \\; tab: \t")]
    [InlineData("first\nsecond")]
    [InlineData("first\n")]
    public void SerializeAndDeserialize_String_RoundTripsWithoutDocumentTrailingNewLine(string value)
    {
        var content = YamlishSerializer.Serialize(new StringValue { Value = value });
        var result = YamlishSerializer.Deserialize<StringValue>(content);

        Assert.False(content.EndsWith("\n", StringComparison.Ordinal));
        Assert.Equal(value, result?.Value);
    }

    [Theory]
    [InlineData("Value: plain value", "plain value")]
    [InlineData("Value: \" leading and trailing \"", " leading and trailing ")]
    [InlineData("Value: 'it''s literal'", "it's literal")]
    [InlineData("Value: |\n  first\n  second", "first\nsecond")]
    public void Deserialize_StringValues(string content, string expected)
    {
        var result = YamlishSerializer.Deserialize<StringValue>(content);

        Assert.Equal(expected, result?.Value);
    }

    [Fact]
    public void Deserialize_InvalidScalar_Throws()
    {
        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<Product>("Price: invalid"));
    }

    private sealed class Product
    {
        [YamlishPropertyName("product_id")]
        public string? Id { get; set; }

        public bool IsAvailable { get; set; }

        public decimal Price { get; set; }

        public List<string>? Tags { get; set; }

        public Dimensions? Dimensions { get; set; }

        [YamlishIgnore]
        public string? Ignored { get; set; }
    }

    private sealed class DefaultNamesProduct
    {
        public string? Id { get; set; }

        public bool IsAvailable { get; set; }

        public decimal Price { get; set; }
    }

    private sealed class Dimensions
    {
        public int Width { get; set; }

        public int Height { get; set; }
    }

    private sealed class StringValue
    {
        public string? Value { get; set; }
    }
}
