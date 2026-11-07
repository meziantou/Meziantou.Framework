---
title: Writing extensions
description: Change every parsed document, change how an element is rendered, or add your own syntax with an inline parser.
---

Choose the simplest way that does what you need:

| You want to | Use |
| --- | --- |
| Change the elements of every document, such as adding a class to the images | The [DocumentProcessed](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder.DocumentProcessed) event |
| Change how an element is written to HTML | An extension that replaces an object renderer |
| Add a syntax that Markdown does not have | An extension with a parser |

## Process the document after parsing

[DocumentProcessed](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder.DocumentProcessed) runs after each parse, before the document is returned or rendered. Change the tree there, for
every document that the pipeline parses:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownPipelineBuilder_DocumentProcessed;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = new MarkdownPipelineBuilder();
builder.DocumentProcessed += document =>
{
    // Runs after each parse, before the document is returned or rendered
    foreach (var link in document.Descendants<LinkInline>())
    {
        if (link.IsImage)
        {
            link.GetAttributes().AddProperty("loading", "lazy");
        }
    }
};
var pipeline = builder.Build();

var html = MarkdownConverter.ToHtml("![Logo](logo.png)", pipeline);
Assert.Equal("<p><img src=\"logo.png\" loading=\"lazy\" alt=\"Logo\" /></p>\n", html);
```
<!-- endref -->

Subscribe to the event before [Build](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder.Build). The handler runs for each parse: it must be thread-safe when the pipeline is
shared between threads.

## Write an extension

An extension implements [IMarkdownExtension](cref:Meziantou.Framework.Markdown.IMarkdownExtension), which has two [Setup](cref:Meziantou.Framework.Markdown.IMarkdownExtension.Setup) methods:

- `Setup(MarkdownPipelineBuilder)` runs once, in [Build](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder.Build). Add or change parsers there ([BlockParsers](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder.BlockParsers), [InlineParsers](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder.InlineParsers)),
  or subscribe to [DocumentProcessed](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder.DocumentProcessed).
- `Setup(MarkdownPipeline, IMarkdownRenderer)` runs for each renderer that the pipeline sets up: the renderers of
  [ToHtml](cref:Meziantou.Framework.Markdown.MarkdownConverter.ToHtml), [ToPlainText](cref:Meziantou.Framework.Markdown.MarkdownConverter.ToPlainText) and [Normalize](cref:Meziantou.Framework.Markdown.MarkdownConverter.Normalize), and the renderers you pass to `pipeline.Setup`. Add or replace object
  renderers there, after checking the type of the renderer.

Enable it with [Use\<TExtension\>()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Use), or `Use(extension)` for an instance with options.

## Change how an element is rendered

Each element type has an object renderer in [ObjectRenderers](cref:Meziantou.Framework.Markdown.Renderers.IMarkdownRenderer.ObjectRenderers). Replace it to write the element differently. For
instance, to start the headings at `<h2>` in a page that already has a `<h1>`:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MiscTests.cs;region=HtmlObjectRenderer_ShiftHeadingsExtension;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class ShiftHeadingsExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // Called for each renderer: only change the HTML one
        if (renderer is HtmlRenderer)
        {
            renderer.ObjectRenderers.Replace<HeadingRenderer>(new ShiftedHeadingRenderer());
        }
    }
}

internal sealed class ShiftedHeadingRenderer : HtmlObjectRenderer<HeadingBlock>
{
    private static readonly string[] Tags = ["h2", "h3", "h4", "h5", "h6", "h6"];

    protected override void Write(HtmlRenderer renderer, HeadingBlock obj)
    {
        var tag = Tags[Math.Clamp(obj.Level, 1, 6) - 1];

        // ToPlainText uses the HTML renderer without the tags
        if (renderer.EnableHtmlForBlock)
        {
            renderer.Write('<').Write(tag).WriteAttributes(obj).Write('>');
        }

        renderer.WriteLeafInline(obj);

        if (renderer.EnableHtmlForBlock)
        {
            renderer.Write("</").Write(tag).Write('>');
        }

        renderer.EnsureLine();
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MiscTests.cs;region=HtmlObjectRenderer_ReplaceRenderer;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .Use<ShiftHeadingsExtension>()
    .Build();

// The page already has a <h1>: the headings of the content start at <h2>
var html = MarkdownConverter.ToHtml("# Title\n\n## Section", pipeline);
Assert.Equal("<h2>Title</h2>\n<h3>Section</h3>\n", html);
Assert.Equal("Title\nSection\n", MarkdownConverter.ToPlainText("# Title\n\n## Section", pipeline));
```
<!-- endref -->

- [HtmlObjectRenderer\<T\>](cref:Meziantou.Framework.Markdown.Renderers.Html.HtmlObjectRenderer{TObject}) (namespace [Meziantou.Framework.Markdown.Renderers.Html](cref:Meziantou.Framework.Markdown.Renderers.Html)) is the base class of the HTML object
  renderers. [WriteAttributes](cref:Meziantou.Framework.Markdown.Renderers.HtmlRenderer.WriteAttributes) writes the [attributes](04-Extensions/06-GenericAttributes.md) of the element, and
  [WriteLeafInline](cref:Meziantou.Framework.Markdown.Renderers.TextRendererBase{T}.WriteLeafInline) or [WriteChildren](cref:Meziantou.Framework.Markdown.Renderers.RendererBase.WriteChildren) write its content.
- [Replace\<T\>](cref:Meziantou.Framework.Markdown.Helpers.OrderedList{T}.Replace), [InsertBefore\<T\>](cref:Meziantou.Framework.Markdown.Helpers.OrderedList{T}.InsertBefore) and [Find\<T\>](cref:Meziantou.Framework.Markdown.Helpers.OrderedList{T}.Find) of [ObjectRenderers](cref:Meziantou.Framework.Markdown.Renderers.RendererBase.ObjectRenderers) work on the existing renderers by type. Some
  renderers have settings that are enough without replacing them, such as [Rel](cref:Meziantou.Framework.Markdown.Renderers.Html.Inlines.LinkInlineRenderer.Rel) on [LinkInlineRenderer](cref:Meziantou.Framework.Markdown.Renderers.Html.Inlines.LinkInlineRenderer) or [GetTag](cref:Meziantou.Framework.Markdown.Renderers.Html.Inlines.EmphasisInlineRenderer.GetTag) on
  [EmphasisInlineRenderer](cref:Meziantou.Framework.Markdown.Renderers.Html.Inlines.EmphasisInlineRenderer).

## Add a syntax

A parser reads your syntax and creates elements in the tree. An inline parser declares the characters that can start
its syntax in [OpeningCharacters](cref:Meziantou.Framework.Markdown.Parsers.ParserBase{TProcessor}.OpeningCharacters); [Match](cref:Meziantou.Framework.Markdown.Parsers.InlineParser.Match) is called at each of them, reads the text from the [StringSlice](cref:Meziantou.Framework.Markdown.Helpers.StringSlice), and
returns `true` after creating an inline. This extension turns `@name` into a link to a GitHub profile:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MiscTests.cs;region=IMarkdownExtension_MentionExtension;format=md-fenced-code-block;dedent=true -->
```csharp
// Turns "@name" into a link to the GitHub profile of the user
internal sealed class MentionExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.InlineParsers.AddIfNotAlready(new MentionParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // Nothing to do: a mention is a LinkInline, and every renderer already writes links
    }
}

internal sealed class MentionParser : InlineParser
{
    public MentionParser()
    {
        OpeningCharacters = ['@'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // "user@example.com" is not a mention: the @ must start a word
        if (slice.PeekCharExtra(-1).IsAlphaNumeric())
            return false;

        var start = slice.Start;
        var current = slice.NextChar(); // Skips the @
        while (current.IsAlphaNumeric() || current == '-')
        {
            current = slice.NextChar();
        }

        var name = slice.Text[(start + 1)..slice.Start];
        if (name.Length == 0)
            return false;

        // The position of the inline in the source, when the pipeline computes them
        var position = processor.GetSourcePosition(start, out var line, out var column);
        var link = new LinkInline("https://github.com/" + name, title: "")
        {
            Span = new SourceSpan(position, position + name.Length),
            Line = line,
            Column = column,
            IsClosed = true, // The parser must not add the next inlines into the link
        };
        link.AppendChild(new LiteralInline("@" + name));
        processor.Inline = link;
        return true;
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MiscTests.cs;region=IMarkdownExtension_UseCustomExtension;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .Use<MentionExtension>()
    .Build();

var html = MarkdownConverter.ToHtml("Thanks @meziantou! Write to user@example.com.", pipeline);
Assert.Equal("<p>Thanks <a href=\"https://github.com/meziantou\">@meziantou</a>! Write to user@example.com.</p>\n", html);
```
<!-- endref -->

- The mention is a [LinkInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LinkInline): every renderer already writes links. A new element type needs an object renderer for
  each renderer you use.
- `processor.Inline` receives the new inline. Set [IsClosed](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline.IsClosed) on a container inline so that the next inlines are not
  added into it.
- Set the position of the inline from `processor.GetSourcePosition`; it is only computed when the pipeline uses
  [precise source locations](06-SourcePositions.md#positions-of-inlines).
- Parsers run in the order of the list: [InsertBefore\<T\>](cref:Meziantou.Framework.Markdown.Helpers.OrderedList{T}.InsertBefore) gives yours priority over an existing parser that uses the same
  opening character.

Block parsers derive from [BlockParser](cref:Meziantou.Framework.Markdown.Parsers.BlockParser), and fenced blocks from [FencedBlockParserBase\<T\>](cref:Meziantou.Framework.Markdown.Parsers.FencedBlockParserBase{T}). The extensions of the
library, in the `Meziantou.Framework.Markdown.Extensions` namespaces, are complete examples of both.
