# Meziantou.Framework.AtlassianDataFormat

`Meziantou.Framework.AtlassianDataFormat` provides:

- A typed object model for the [Atlassian Document Format](https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/) (ADF), the JSON rich-text format used by Jira, Confluence, and Bitbucket (`AdfDocument`, `AdfNode`, `AdfMark`, ...)
- A Markdown converter with per-node options for the ADF constructs Markdown cannot express (`AdfToMarkdown`)

## Convert a document to Markdown

The types are in the `Meziantou.Framework.AtlassianDataFormat` namespace.

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_Convert;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """
    {
      "version": 1,
      "type": "doc",
      "content": [
        { "type": "heading", "attrs": { "level": 1 }, "content": [ { "type": "text", "text": "Title" } ] },
        { "type": "paragraph", "content": [ { "type": "text", "text": "Hello", "marks": [ { "type": "strong" } ] } ] }
      ]
    }
    """;

var markdown = AdfToMarkdown.Convert(json);
Assert.Equal("# Title\n\n**Hello**", markdown);
```
<!-- endref -->

`Convert` also accepts a `JsonElement` or a `JsonNode`, which avoids a round trip when the document
comes from an API response you already parsed.

## Configure the conversion

Defaults produce plain CommonMark/GitHub-Flavored Markdown; HTML output is opt-in. With the `json` of the previous example:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_Options;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = AdfToMarkdown.Convert(json, new AdfToMarkdownOptions
{
    PanelStyle = AdfPanelStyle.GitHubAlert,      // > [!WARNING]
    ExpandStyle = AdfExpandStyle.HtmlDetails,    // <details><summary>…</summary>
    TableStyle = AdfTableStyle.Auto,             // pipe table, or HTML when cells span rows/columns
    MediaRendering = AdfMediaRendering.Link,
    EmojiRendering = AdfEmojiRendering.ShortName,
    TaskListStyle = AdfTaskListStyle.Checkbox,
    DecisionListStyle = AdfDecisionListStyle.BulletList,
    UnknownNodeHandling = AdfUnknownNodeHandling.KeepContent,
    MentionFormat = "@{text}",
    StatusFormat = "**[{text}]**",
    DateFormat = "yyyy-MM-dd",
    HeadingStyle = AdfHeadingStyle.Atx,
    EmphasisMarker = AdfEmphasisMarker.Asterisk,
    CodeBlockStyle = AdfCodeBlockStyle.Fenced,
    LineBreakStyle = AdfLineBreakStyle.Backslash,
});
```
<!-- endref -->

### Resolving media and mentions

Only media of type `external` carries a URL. Files stored by Atlassian carry an identifier and a
collection, and turning those into a URL needs an authenticated call to the media API. Documents
returned by the APIs also often omit the display name of a mention. Both can be supplied by a
callback:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_Resolvers;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """
    {
      "version": 1,
      "type": "doc",
      "content": [
        { "type": "paragraph", "content": [ { "type": "mention", "attrs": { "id": "123" } } ] },
        {
          "type": "mediaSingle",
          "content": [ { "type": "media", "attrs": { "type": "file", "id": "abc", "collection": "files", "alt": "diagram.png" } } ]
        }
      ]
    }
    """;

var displayNames = new Dictionary<string, string> { ["123"] = "Alex" };
var markdown = AdfToMarkdown.Convert(json, new AdfToMarkdownOptions
{
    MediaUrlResolver = media => $"https://media.example.com/{media.Collection}/{media.Id}",
    MentionResolver = mention => displayNames.GetValueOrDefault(mention.Id),
});

Assert.Equal("@Alex\n\n![diagram.png](https://media.example.com/files/abc)", markdown);
```
<!-- endref -->

## Work with the object model

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfDocumentTests.cs;region=AdfDocument_ObjectModel;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """
    {
      "version": 1,
      "type": "doc",
      "content": [
        { "type": "heading", "attrs": { "level": 1 }, "content": [ { "type": "text", "text": "Title" } ] },
        { "type": "paragraph", "content": [ { "type": "text", "text": "Hello " }, { "type": "mention", "attrs": { "id": "123", "text": "@Alex" } } ] }
      ]
    }
    """;

var document = AdfDocument.Parse(json);

var mentionIds = document.Descendants().OfType<AdfMention>().Select(mention => mention.Id).ToList();
Assert.Equal(["123"], mentionIds);

var heading = (AdfHeading)document.Content[0];
Assert.Equal(1, heading.Level);

var markdown = document.ToMarkdown();
Assert.Equal("# Title\n\nHello @Alex", markdown);

var roundTripped = document.ToJsonString();
Assert.Equal(AdfDocument.Parse(roundTripped).ToMarkdown(), markdown);
```
<!-- endref -->

Documents can also be built from scratch:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfDocumentTests.cs;region=AdfDocument_Build;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new AdfDocument
{
    Content =
    [
        new AdfHeading { Level = 1, Content = [new AdfText { Text = "Title" }] },
        new AdfParagraph { Content = [new AdfText { Text = "Hello", Marks = [new AdfStrongMark()] }] },
    ],
};

var json = document.ToJsonString();
Assert.Equal("""{"version":1,"type":"doc","content":[{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Title"}]},{"type":"paragraph","content":[{"type":"text","text":"Hello","marks":[{"type":"strong"}]}]}]}""", json);
```
<!-- endref -->

## Unknown nodes

Atlassian adds node types regularly, and real documents contain nodes such as `unsupportedBlock`.
Parsing never fails on an unknown type: it produces an `AdfUnknownNode` that keeps the original JSON,
so the document round-trips unchanged. `AdfToMarkdownOptions.UnknownNodeHandling` controls whether
such a node is skipped, has its content converted, or throws.

## Supported nodes and marks

Nodes: `blockCard`, `blockquote`, `bodiedExtension`, `bulletList`, `caption`, `codeBlock`, `date`,
`decisionItem`, `decisionList`, `embedCard`, `emoji`, `expand`, `extension`, `hardBreak`, `heading`,
`inlineCard`, `inlineExtension`, `layoutColumn`, `layoutSection`, `listItem`, `media`, `mediaGroup`,
`mediaInline`, `mediaSingle`, `mention`, `nestedExpand`, `orderedList`, `panel`, `paragraph`,
`placeholder`, `rule`, `status`, `table`, `tableCell`, `tableHeader`, `tableRow`, `taskItem`,
`taskList`, `text`.

Marks: `alignment`, `annotation`, `backgroundColor`, `border`, `breakout`, `code`, `em`,
`indentation`, `link`, `strike`, `strong`, `subsup`, `textColor`, `underline`.

Marks with no Markdown equivalent — `underline`, `textColor`, `backgroundColor`, `annotation`,
`border`, `alignment`, `indentation`, `breakout` — are dropped, and the text they carry is kept.
`subsup` becomes `<sub>` or `<sup>`.
