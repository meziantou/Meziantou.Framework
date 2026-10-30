---
title: The object model
description: Parse ADF documents, find nodes and marks, build documents in code, write them as JSON, and keep unknown nodes.
---

`AdfDocument` is a parsed ADF document: a tree of `AdfNode` objects, each with its `AdfMark` list. Use it to read a
document, to change what the conversion to Markdown sees, or to build a document for an Atlassian API.

## Read a document

`AdfDocument.Parse` accepts the JSON as a `string`, a `JsonElement` or a `JsonNode`. `Content` holds the top-level
nodes, and `Descendants` enumerates every node of the document, depth first:

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

Each node type has its class, such as `AdfParagraph`, `AdfHeading` or `AdfMention`, whose properties are the attributes
of the node: `Level` for a heading, `Id` and `Text` for a mention. Every node has:

| Member | Content |
| --- | --- |
| `Kind` | The type of the node, as an `AdfNodeKind`. |
| `Content` | The child nodes. Empty for a leaf node, such as `text` or `mention`. |
| `Marks` | The marks of the node. Only text nodes, and a few others such as `media`, carry marks. |
| `LocalId` | The identifier of the node in the document, when it has one. |
| `DescendantsAndSelf()` | The node and all its descendants, depth first. |
| `GetMark<T>()` | The first mark of type `T`, or `null`. |

## Find marks

Marks format the text: `AdfStrongMark`, `AdfLinkMark`, `AdfTextColorMark`… Find the links of a document from the marks
of its text nodes:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfDocumentTests.cs;region=AdfDocument_FindLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var document = AdfDocument.Parse("""
    {
      "version": 1,
      "type": "doc",
      "content": [
        {
          "type": "paragraph",
          "content": [
            { "type": "text", "text": "See the " },
            { "type": "text", "text": "design", "marks": [ { "type": "link", "attrs": { "href": "https://example.com/design" } } ] },
            { "type": "text", "text": " and the " },
            { "type": "text", "text": "plan", "marks": [ { "type": "strong" }, { "type": "link", "attrs": { "href": "https://example.com/plan" } } ] }
          ]
        }
      ]
    }
    """);

var links = document.Descendants()
    .OfType<AdfText>()
    .Select(text => text.GetMark<AdfLinkMark>()?.Href)
    .OfType<string>();

Assert.Equal(["https://example.com/design", "https://example.com/plan"], links);
```
<!-- endref -->

## Build a document

The properties of the nodes are `init` properties: build a document with object initializers, then write it with
`ToJsonString`:

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

- `ToJsonString(indented: true)` indents the JSON. `WriteTo` writes it to a `Utf8JsonWriter`, such as the body of an
  HTTP request.
- `Version` is `1` by default, the only version of the format defined today.
- The properties are init-only: to change a document, build a new tree from the nodes of the old one.

## Invalid documents

`Parse` throws an `AdfException` when the value is not JSON, or when its root is not an object whose `type` is `doc`.
`TryParse` returns `false` instead:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfDocumentTests.cs;region=AdfDocument_TryParse;format=md-fenced-code-block;dedent=true -->
```csharp
// Not JSON
Assert.False(AdfDocument.TryParse("Steps to reproduce", out _));

// JSON, but the root is not a "doc" node
Assert.False(AdfDocument.TryParse("""{ "type": "paragraph" }""", out _));

// Parse throws an AdfException instead
Assert.Throws<AdfException>(() => AdfDocument.Parse("""{ "type": "paragraph" }"""));
```
<!-- endref -->

Below the root, parsing does not validate the document against the schema: a missing attribute gets a default value,
such as level 1 for a heading, and a node of an unknown type is kept as an unknown node.

## Unknown nodes and marks

A node whose type the library does not know is parsed as an `AdfUnknownNode`, with the `TypeName` and the `RawJson` of
the node. A mark is parsed as an `AdfUnknownMark` the same way. They are written back unchanged, so a document
round-trips even when it uses types added after this version:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfDocumentTests.cs;region=AdfDocument_UnknownNodes;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """{"version":1,"type":"doc","content":[{"type":"somethingNew","attrs":{"answer":42}}]}""";
var document = AdfDocument.Parse(json);

var unknown = Assert.IsType<AdfUnknownNode>(document.Content[0]);
Assert.Equal("somethingNew", unknown.TypeName);
Assert.Equal(42, unknown.RawJson.GetProperty("attrs").GetProperty("answer").GetInt32());

// The node is written back unchanged
Assert.Equal(json, document.ToJsonString());
```
<!-- endref -->

The nodes of a known type are written from their properties: an attribute their class does not have is not written
back.

## Supported types

Nodes: `blockCard`, `blockquote`, `bodiedExtension`, `bulletList`, `caption`, `codeBlock`, `date`, `decisionItem`,
`decisionList`, `embedCard`, `emoji`, `expand`, `extension`, `hardBreak`, `heading`, `inlineCard`, `inlineExtension`,
`layoutColumn`, `layoutSection`, `listItem`, `media`, `mediaGroup`, `mediaInline`, `mediaSingle`, `mention`,
`nestedExpand`, `orderedList`, `panel`, `paragraph`, `placeholder`, `rule`, `status`, `table`, `tableCell`,
`tableHeader`, `tableRow`, `taskItem`, `taskList`, `text`.

Marks: `alignment`, `annotation`, `backgroundColor`, `border`, `breakout`, `code`, `em`, `indentation`, `link`,
`strike`, `strong`, `subsup`, `textColor`, `underline`.

The class of each type is its name in PascalCase with the `Adf` prefix, such as `AdfBulletList`, and `Mark` at the end
for a mark, such as `AdfStrongMark`. Two marks are named after what they do: `em` is `AdfEmphasisMark` and `subsup` is
`AdfSubSupMark`.
