---
title: Getting started
description: Install the package, convert an ADF document to Markdown, and convert the description of a Jira issue.
---

## Install the package

Add the `Meziantou.Framework.AtlassianDataFormat` package to a project:

```bash
dotnet add package Meziantou.Framework.AtlassianDataFormat
```

The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework.AtlassianDataFormat;`.

## Convert a document

[AdfToMarkdown.Convert](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdown.Convert) takes the JSON of a document and returns Markdown:

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

By default, the output is CommonMark with the GitHub extensions (tables, task lists, strikethrough). HTML is only
written for subscript and superscript text, for the line breaks of a table cell that holds several paragraphs, and for
the options that ask for it. Lines end with `\n`, and there is no line terminator at the end.

## Convert the description of a Jira issue

An API response holds the document as one of its properties. Pass that property as a `JsonElement`, or a `JsonNode`,
instead of serializing it back to a string:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_JsonElement;format=md-fenced-code-block;dedent=true -->
```csharp
// The body of a Jira issue, where the description is an ADF document
using var issue = JsonDocument.Parse("""
    {
      "key": "ABC-1",
      "fields": {
        "description": {
          "version": 1,
          "type": "doc",
          "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Steps to reproduce" } ] } ]
        }
      }
    }
    """);

var description = issue.RootElement.GetProperty("fields").GetProperty("description");
var markdown = AdfToMarkdown.Convert(description);
Assert.Equal("Steps to reproduce", markdown);
```
<!-- endref -->

[Convert](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdown.Convert) throws an [AdfException](cref:Meziantou.Framework.AtlassianDataFormat.AdfException) when the value is not an ADF document. See
[Invalid documents](04-ObjectModel.md#invalid-documents).

## Next steps

- [Converting to Markdown](03-ConvertingToMarkdown.md): the options, mentions, media and unknown nodes.
- [The object model](04-ObjectModel.md): read, build and write documents.
- [Command-line tool](05-CommandLineTool.md): convert files without writing code.
