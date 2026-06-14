namespace Meziantou.Framework.Yamlish.Tests;

public sealed class YamlishDocumentTests
{
    [Fact]
    public void Parse_SampleDocument()
    {
        var document = YamlishDocument.Parse("""
            id: abc
            name: sample product
            price: 12.5
            is_available: true
            description: |
                This is a sample product.
                Line 2
                Line 3
                    Line 4 starts with 4 spaces
            """);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal("abc", Assert.IsType<YamlishScalar>(mapping["id"]).Value);
        Assert.Equal("12.5", Assert.IsType<YamlishScalar>(mapping["price"]).Value);
        Assert.Equal("""
            This is a sample product.
            Line 2
            Line 3
                Line 4 starts with 4 spaces
            """, Assert.IsType<YamlishScalar>(mapping["description"]).Value);
    }

    [Fact]
    public void Parse_NestedMappingAndSequences()
    {
        var document = YamlishDocument.Parse("""
            product:
              name: sample
              tags: [new, "featured", 'sale']
              variants:
                - name: small
                - name: large
            """);

        var root = Assert.IsType<YamlishMapping>(document.Root);
        var product = Assert.IsType<YamlishMapping>(root["product"]);
        var tags = Assert.IsType<YamlishSequence>(product["tags"]);
        Assert.Equal(["new", "featured", "sale"], tags.Cast<YamlishScalar>().Select(item => item.Value));
        var variants = Assert.IsType<YamlishSequence>(product["variants"]);
        Assert.Equal("large", Assert.IsType<YamlishScalar>(Assert.IsType<YamlishMapping>(variants[1])["name"]).Value);
    }

    [Fact]
    public void Parse_QuotedStrings()
    {
        var document = YamlishDocument.Parse("""
            double: "line\nvalue"
            single: 'it''s literal'
            """);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal("line\nvalue", Assert.IsType<YamlishScalar>(mapping["double"]).Value);
        Assert.Equal("it's literal", Assert.IsType<YamlishScalar>(mapping["single"]).Value);
    }

    [Theory]
    [InlineData("value: plain value", "plain value")]
    [InlineData("value: \"\"", "")]
    [InlineData("value: \" leading and trailing \"", " leading and trailing ")]
    [InlineData("value: \"quote: \\\"; slash: \\\\; tab: \\t\"", "quote: \"; slash: \\; tab: \t")]
    [InlineData("value: 'it''s literal: \\n'", "it's literal: \\n")]
    public void Parse_StringValues(string content, string expected)
    {
        var document = YamlishDocument.Parse(content);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal(expected, Assert.IsType<YamlishScalar>(mapping["value"]).Value);
    }

    [Fact]
    public void Parse_LiteralString_DoesNotAddTrailingNewLine()
    {
        var document = YamlishDocument.Parse("""
            value: |
              first
              second
            """);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal("first\nsecond", Assert.IsType<YamlishScalar>(mapping["value"]).Value);
    }

    [Fact]
    public void Parse_LiteralString_PreservesBlankLinesAndRelativeIndentation()
    {
        var document = YamlishDocument.Parse("""
            value: |
              first

                indented
              last
            next: value
            """);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal("first\n\n  indented\nlast", Assert.IsType<YamlishScalar>(mapping["value"]).Value);
    }

    [Fact]
    public void Parse_DuplicateKey_ThrowsWithLine()
    {
        var exception = Assert.Throws<FormatException>(() => YamlishDocument.Parse("""
            name: first
            name: second
            """));

        Assert.Contains("line 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NestedInlineSequence_Throws()
    {
        Assert.Throws<FormatException>(() => YamlishDocument.Parse("values: [one, [two]]"));
    }

    [Fact]
    public void Parse_Comment_Throws()
    {
        Assert.Throws<FormatException>(() => YamlishDocument.Parse("name: value # comment"));
    }

    [Fact]
    public void Document_RoundTrips()
    {
        var root = new YamlishMapping
        {
            { "name", new YamlishScalar("sample") },
            { "description", new YamlishScalar("line 1\n  line 2") },
            { "trailingNewLine", new YamlishScalar("line 1\n") },
            { "commentLike", new YamlishScalar("value # not a comment") },
            { "tags", new YamlishSequence([new YamlishScalar("one"), new YamlishScalar("two")]) },
        };

        var content = new YamlishDocument(root).ToString();
        var parsed = YamlishDocument.Parse(content);

        var mapping = Assert.IsType<YamlishMapping>(parsed.Root);
        Assert.False(content.EndsWith("\n", StringComparison.Ordinal));
        Assert.Equal("line 1\n  line 2", Assert.IsType<YamlishScalar>(mapping["description"]).Value);
        Assert.Equal("line 1\n", Assert.IsType<YamlishScalar>(mapping["trailingNewLine"]).Value);
        Assert.Equal("value # not a comment", Assert.IsType<YamlishScalar>(mapping["commentLike"]).Value);
        Assert.Equal(["one", "two"], Assert.IsType<YamlishSequence>(mapping["tags"]).Cast<YamlishScalar>().Select(item => item.Value));
    }

    [Fact]
    public void Document_ToString_NeverAddsTrailingNewLine()
    {
        YamlishNode[] nodes =
        [
            new YamlishScalar("value"),
            new YamlishMapping { { "name", new YamlishScalar("value") } },
            new YamlishSequence([new YamlishScalar("one"), new YamlishScalar("two")]),
            new YamlishMapping { { "description", new YamlishScalar("first\nsecond") } },
        ];

        foreach (var node in nodes)
        {
            Assert.False(new YamlishDocument(node).ToString().EndsWith("\n", StringComparison.Ordinal));
        }
    }
}
