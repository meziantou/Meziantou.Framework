---
title: Getting started
description: Install the package, convert an HTML fragment, set the options and convert a complete document.
---

## Install the package

Add the `Meziantou.Framework.HtmlToMarkdown` package to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.HtmlToMarkdown
```

The examples of this documentation need `using Meziantou.Framework;`.

## Convert a fragment

`HtmlToMarkdown.Convert` converts an HTML fragment and returns the Markdown:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_Convert;format=md-fenced-code-block;dedent=true -->
```csharp
var html = "<h1>Title</h1><p>Hello <strong>world</strong></p>";
var markdown = HtmlToMarkdown.Convert(html);
Assert.Equal("""
    # Title

    Hello **world**
    """, markdown);
```
<!-- endref -->

The blocks are separated by an empty line, and the result has no leading or trailing line break. An empty fragment, or
one that holds only whitespace, gives an empty string.

## Set the options

Pass an `HtmlToMarkdownOptions` to choose the Markdown syntax of each element:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_Options;format=md-fenced-code-block;dedent=true -->
```csharp
var html = "<h1>Title</h1><p>Line 1<br>Line 2</p>";

var markdown = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions
{
    HeadingStyle = HeadingStyle.Setext,
    EmphasisMarker = EmphasisMarker.Underscore,
    CodeBlockStyle = CodeBlockStyle.Fenced,
    CodeBlockFenceCharacter = '~',
    UnorderedListMarker = '*',
    ThematicBreak = "***",
    LineBreakStyle = LineBreakStyle.Backslash,
    UseSimplePunctuation = true,
    EmojiShortcodeMode = EmojiShortcodeMode.GitHub,
    UnknownElementHandling = UnknownElementHandling.StripKeepContent,
});
Assert.Equal("""
    Title
    =====

    Line 1\
    Line 2
    """, markdown);
```
<!-- endref -->

The defaults are the most common syntax: ATX headings (`#`), `*` emphasis, fenced code blocks with backticks, `-`
list items and `---` horizontal rules. [Formatting options](04-FormattingOptions.md) describes each option.

## Convert a complete document

`Convert` takes a fragment: the content of a `body` element. Given a complete page, it writes the elements of the
`head`, such as `title` and `meta`, as raw HTML. To convert a complete page, parse it with
[Meziantou.Framework.WebPlatform](../WebPlatform/03-HtmlAndDom/02-ParsingDocuments.md) (a dependency of the package)
and convert the content of its body, so that the `head` of the page is ignored:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_CompleteDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var html = """
    <!DOCTYPE html>
    <html>
    <head><title>Page title</title></head>
    <body><h1>Title</h1><p>Content</p></body>
    </html>
    """;

// Convert the content of the body, not the whole document
var document = HtmlDocumentParser.ParseDocument(html);
var markdown = HtmlToMarkdown.Convert(document.Body!.InnerHtml);
Assert.Equal("""
    # Title

    Content
    """, markdown);
```
<!-- endref -->

`HtmlDocumentParser` is in the [Meziantou.Framework.WebPlatform](cref:Meziantou.Framework.WebPlatform) namespace. The
[command-line tool](07-CommandLineTool.md) does this for you.

## Next steps

- [Converted elements](03-ConvertedElements.md): the Markdown each element produces.
- [Unknown elements and untrusted HTML](05-UnknownElementsAndUntrustedHtml.md): read it before you convert HTML that
  your users wrote.
