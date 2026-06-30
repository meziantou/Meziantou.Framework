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

    [Fact]
    public void Parse_ScalarKinds()
    {
        var document = YamlishDocument.Parse("""
            nullValue: null
            quotedNull: "null"
            plainText: value
            """);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal(YamlishScalarKind.Null, Assert.IsType<YamlishScalar>(mapping["nullValue"]).ScalarKind);
        Assert.Equal(YamlishScalarKind.String, Assert.IsType<YamlishScalar>(mapping["quotedNull"]).ScalarKind);
        Assert.Equal(YamlishScalarKind.String, Assert.IsType<YamlishScalar>(mapping["plainText"]).ScalarKind);
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
    public void Parse_LiteralString_AtEndOfInputWithoutLineBreakDoesNotAddTrailingNewLine()
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
        Assert.Equal("first\n\n  indented\nlast\n", Assert.IsType<YamlishScalar>(mapping["value"]).Value);
    }

    [Theory]
    [InlineData("|-", "first\nsecond")]
    [InlineData("|", "first\nsecond\n")]
    [InlineData("|+", "first\nsecond\n\n")]
    [InlineData(">-", "first second")]
    [InlineData(">", "first second\n")]
    [InlineData(">+", "first second\n\n")]
    public void Parse_BlockScalarChomping(string header, string expected)
    {
        var document = YamlishDocument.Parse($"value: {header}\n  first\n  second\n\nnext: value");

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal(expected, Assert.IsType<YamlishScalar>(mapping["value"]).Value);
    }

    [Fact]
    public void Parse_FoldedString_PreservesBlankAndMoreIndentedLines()
    {
        var document = YamlishDocument.Parse("""
            value: >-
              folded
              text

              next
              line
                * bullet

                * list
                * lines

              last
              line
            """);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal("folded text\nnext line\n  * bullet\n\n  * list\n  * lines\n\nlast line", Assert.IsType<YamlishScalar>(mapping["value"]).Value);
    }

    [Theory]
    [InlineData("value: |-\n", "")]
    [InlineData("value: |\n", "")]
    [InlineData("value: |+\n\n", "\n")]
    [InlineData("value: >-\n", "")]
    [InlineData("value: >\n", "")]
    [InlineData("value: >+\n\n", "\n")]
    public void Parse_EmptyBlockScalarChomping(string content, string expected)
    {
        var document = YamlishDocument.Parse(content);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal(expected, Assert.IsType<YamlishScalar>(mapping["value"]).Value);
    }

    [Fact]
    public void Parse_BlockScalarsInSequencesAndCompactMappings()
    {
        var document = YamlishDocument.Parse("""
            values:
              - >-
                folded
                value
              - value: |-
                  literal
                  value
            """);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        var sequence = Assert.IsType<YamlishSequence>(mapping["values"]);
        Assert.Equal("folded value", Assert.IsType<YamlishScalar>(sequence[0]).Value);
        Assert.Equal("literal\nvalue", Assert.IsType<YamlishScalar>(Assert.IsType<YamlishMapping>(sequence[1])["value"]).Value);
    }

    [Fact]
    public void Parse_BlockScalar_AllowsTabAsContent()
    {
        var document = YamlishDocument.Parse("value: |-\n  \tcontent");

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal("\tcontent", Assert.IsType<YamlishScalar>(mapping["value"]).Value);
    }

    [Fact]
    public void Parse_DuplicateKey_ThrowsWithLine()
    {
        var exception = Assert.Throws<FormatException>(() => YamlishDocument.Parse("""
            name: first
            name: second
            """));

        Assert.Contains("line 2", exception.Message);
    }

    [Fact]
    public void Parse_NestedInlineSequence_Throws()
    {
        Assert.Throws<FormatException>(() => YamlishDocument.Parse("values: [one, [two]]"));
    }

    [Fact]
    public void Parse_Comments()
    {
        var document = YamlishDocument.Parse("""
            # this is a comment
            plain: value # comment
            double: "value" # comment
            single: 'value' # comment
            quotedComment: "value # not comment" # comment
            literal: |
                value # not a comment
            literalWithComment: | # comment
              value
            # comment between entries
            nested: # comment
              # nested comment
              value: content # comment
            inlineSequence: [one, "two # not comment"] # comment
            sequence:
              - one # comment
              # comment between items
              - two
              - | # comment
                three # not a comment
            """);

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal("value", Assert.IsType<YamlishScalar>(mapping["plain"]).Value);
        Assert.Equal("value", Assert.IsType<YamlishScalar>(mapping["double"]).Value);
        Assert.Equal("value", Assert.IsType<YamlishScalar>(mapping["single"]).Value);
        Assert.Equal("value # not comment", Assert.IsType<YamlishScalar>(mapping["quotedComment"]).Value);
        Assert.Equal("value # not a comment\n", Assert.IsType<YamlishScalar>(mapping["literal"]).Value);
        Assert.Equal("value\n", Assert.IsType<YamlishScalar>(mapping["literalWithComment"]).Value);
        Assert.Equal("content", Assert.IsType<YamlishScalar>(Assert.IsType<YamlishMapping>(mapping["nested"])["value"]).Value);
        Assert.Equal(["one", "two # not comment"], Assert.IsType<YamlishSequence>(mapping["inlineSequence"]).Cast<YamlishScalar>().Select(item => item.Value));
        Assert.Equal(["one", "two", "three # not a comment"], Assert.IsType<YamlishSequence>(mapping["sequence"]).Cast<YamlishScalar>().Select(item => item.Value));
    }

    [Fact]
    public void Parse_CommentMarkerWithoutPrecedingWhitespaceIsPartOfPlainScalar()
    {
        var document = YamlishDocument.Parse("value: content#not-comment");

        var mapping = Assert.IsType<YamlishMapping>(document.Root);
        Assert.Equal("content#not-comment", Assert.IsType<YamlishScalar>(mapping["value"]).Value);
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
        Assert.False(content.EndsWith('\n', StringComparison.Ordinal));
        Assert.Equal("line 1\n  line 2", Assert.IsType<YamlishScalar>(mapping["description"]).Value);
        Assert.Equal("line 1\n", Assert.IsType<YamlishScalar>(mapping["trailingNewLine"]).Value);
        Assert.Equal("value # not a comment", Assert.IsType<YamlishScalar>(mapping["commentLike"]).Value);
        Assert.Equal(["one", "two"], Assert.IsType<YamlishSequence>(mapping["tags"]).Cast<YamlishScalar>().Select(item => item.Value));
        Assert.Contains("description: |-", content);
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
            Assert.False(new YamlishDocument(node).ToString().EndsWith('\n', StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Document_ToString_UsesNodeStyles()
    {
        var document = new YamlishDocument(new YamlishMapping
        {
            {
                "Values",
                new YamlishSequence([new YamlishScalar("item1"), new YamlishScalar("item2")])
                {
                    Style = YamlishSequenceStyle.Block,
                }
            },
            {
                "Value",
                new YamlishScalar("first\nsecond")
                {
                    Style = YamlishScalarStyle.Literal,
                    Chomping = YamlishScalarChomping.Strip,
                }
            },
        });

        Assert.Equal("""
            Values:
              - item1
              - item2
            Value: |-
              first
              second
            """, document.ToString(), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Document_MultilineRootScalar_RoundTrips()
    {
        var content = new YamlishDocument(new YamlishScalar("first\nsecond")).ToString();
        var document = YamlishDocument.Parse(content);

        Assert.StartsWith("|-" + Environment.NewLine, content);
        Assert.Equal("first\nsecond", Assert.IsType<YamlishScalar>(document.Root).Value);
    }
}
