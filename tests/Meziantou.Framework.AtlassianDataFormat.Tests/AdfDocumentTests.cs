using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.AtlassianDataFormat.Tests;

public sealed class AdfDocumentTests
{
    private const string SampleJson = """
        {
          "version": 1,
          "type": "doc",
          "content": [
            {
              "type": "heading",
              "attrs": { "level": 2 },
              "content": [ { "type": "text", "text": "Title" } ]
            },
            {
              "type": "paragraph",
              "content": [
                { "type": "text", "text": "Hello " },
                { "type": "text", "text": "world", "marks": [ { "type": "strong" } ] }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void ParseReadsTheDocumentStructure()
    {
        var document = AdfDocument.Parse(SampleJson);

        Assert.Equal(1, document.Version);
        Assert.Equal(2, document.Content.Count);

        var heading = Assert.IsType<AdfHeading>(document.Content[0]);
        Assert.Equal(2, heading.Level);
        Assert.Equal("Title", Assert.IsType<AdfText>(heading.Content[0]).Text);

        var paragraph = Assert.IsType<AdfParagraph>(document.Content[1]);
        Assert.Equal(2, paragraph.Content.Count);

        var strong = Assert.IsType<AdfText>(paragraph.Content[1]);
        Assert.Equal("world", strong.Text);
        Assert.IsType<AdfStrongMark>(strong.Marks[0]);
    }

    [Fact]
    public void ParseFromJsonElement()
    {
        using var json = JsonDocument.Parse(SampleJson);
        var document = AdfDocument.Parse(json.RootElement);
        Assert.Equal(2, document.Content.Count);
    }

    [Fact]
    public void ParseFromJsonNode()
    {
        var node = JsonNode.Parse(SampleJson)!;
        var document = AdfDocument.Parse(node);
        Assert.Equal(2, document.Content.Count);
    }

    [Fact]
    public void ParseRejectsANonDocumentRoot()
    {
        Assert.Throws<AdfException>(() => AdfDocument.Parse("""{"type":"paragraph"}"""));
    }

    [Fact]
    public void ParseRejectsInvalidJson()
    {
        Assert.Throws<AdfException>(() => AdfDocument.Parse("not json"));
    }

    [Fact]
    public void TryParseReturnsFalseForInvalidInput()
    {
        Assert.False(AdfDocument.TryParse("not json", out var document));
        Assert.Null(document);
    }

    [Fact]
    public void TryParseReturnsTrueForAValidDocument()
    {
        Assert.True(AdfDocument.TryParse(SampleJson, out var document));
        Assert.NotNull(document);
    }

    [Fact]
    public void GetMarkFindsAMarkByType()
    {
        var document = AdfDocument.Parse(SampleJson);
        var paragraph = document.Content[1];

        Assert.NotNull(paragraph.Content[1].GetMark<AdfStrongMark>());
        Assert.Null(paragraph.Content[0].GetMark<AdfStrongMark>());
    }

    [Fact]
    public void DescendantsEnumeratesEveryNode()
    {
        var document = AdfDocument.Parse(SampleJson);
        var kinds = document.Descendants().Select(n => n.Kind).ToList();

        Assert.Equal(
            [AdfNodeKind.Heading, AdfNodeKind.Text, AdfNodeKind.Paragraph, AdfNodeKind.Text, AdfNodeKind.Text],
            kinds);
    }

    [Fact]
    public void ToJsonStringRoundTrips()
    {
        var document = AdfDocument.Parse(SampleJson);
        var roundTripped = AdfDocument.Parse(document.ToJsonString());

        Assert.Equal(document.ToJsonString(), roundTripped.ToJsonString());
        Assert.Equal(document.ToMarkdown(), roundTripped.ToMarkdown());
    }

    [Fact]
    public void ToJsonStringWritesTheExpectedShape()
    {
        var document = new AdfDocument
        {
            Content =
            [
                new AdfParagraph
                {
                    Content = [new AdfText { Text = "Hello", Marks = [new AdfStrongMark()] }],
                },
            ],
        };

        Assert.Equal(
            """{"version":1,"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello","marks":[{"type":"strong"}]}]}]}""",
            document.ToJsonString());
    }

    [Fact]
    public void NodesWithoutAttributesDoNotWriteAnEmptyAttrsObject()
    {
        var document = new AdfDocument { Content = [new AdfRule()] };
        Assert.Equal("""{"version":1,"type":"doc","content":[{"type":"rule"}]}""", document.ToJsonString());
    }

    [Fact]
    public void UnknownNodesArePreservedInsteadOfThrowing()
    {
        const string Json = """
            {"version":1,"type":"doc","content":[{"type":"unsupportedBlock","attrs":{"originalValue":{"type":"whatever"}}}]}
            """;

        var document = AdfDocument.Parse(Json);
        var unknown = Assert.IsType<AdfUnknownNode>(document.Content[0]);
        Assert.Equal("unsupportedBlock", unknown.TypeName);

        // The raw JSON is kept, so the node round-trips unchanged.
        Assert.Equal(Json.Trim(), document.ToJsonString());
    }

    [Fact]
    public void UnknownMarksArePreserved()
    {
        const string Json = """
            {"version":1,"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"somethingNew","attrs":{"value":1}}]}]}]}
            """;

        var document = AdfDocument.Parse(Json);
        var text = (AdfText)document.Content[0].Content[0];
        var mark = Assert.IsType<AdfUnknownMark>(text.Marks[0]);
        Assert.Equal("somethingNew", mark.TypeName);
        Assert.Equal(Json.Trim(), document.ToJsonString());
    }

    [Fact]
    public void ExtensionParametersArePreserved()
    {
        const string Json = """
            {"version":1,"type":"doc","content":[{"type":"extension","attrs":{"extensionKey":"k","extensionType":"t","parameters":{"a":[1,2]}}}]}
            """;

        var document = AdfDocument.Parse(Json);
        var extension = Assert.IsType<AdfExtension>(document.Content[0]);
        Assert.Equal("k", extension.ExtensionKey);
        Assert.Equal(Json.Trim(), document.ToJsonString());
    }

    [Fact]
    public void MissingVersionDefaultsToOne()
    {
        var document = AdfDocument.Parse("""{"type":"doc","content":[]}""");
        Assert.Equal(1, document.Version);
    }

    [Theory]
    [InlineData("1704067200000", "2024-01-01T00:00:00+00:00")]
    [InlineData("1704067200", "2024-01-01T00:00:00+00:00")]
    public void DateTimestampsAreReadAsMillisecondsWithASecondsFallback(string timestamp, string expected)
    {
        var date = new AdfDate { Timestamp = timestamp };
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), date.GetDateTimeOffset());
    }

    [Fact]
    public void ANonNumericDateTimestampHasNoValue()
    {
        Assert.Null(new AdfDate { Timestamp = "not a date" }.GetDateTimeOffset());
    }

    [Fact]
    public void DocumentsCanBeBuiltAndConvertedWithoutParsing()
    {
        var document = new AdfDocument
        {
            Content =
            [
                new AdfHeading { Level = 1, Content = [new AdfText { Text = "Title" }] },
                new AdfBulletList
                {
                    Content =
                    [
                        new AdfListItem { Content = [new AdfParagraph { Content = [new AdfText { Text = "one" }] }] },
                        new AdfListItem { Content = [new AdfParagraph { Content = [new AdfText { Text = "two" }] }] },
                    ],
                },
            ],
        };

        Assert.Equal("# Title\n\n- one\n- two", document.ToMarkdown());
    }
}
