---
title: Custom syntaxes
description: Add your own syntaxes, such as mentions, highlights, admonitions or transcripts, and write their HTML.
---

The extensions cover the syntaxes that other tools share. A site often has syntaxes of its own: `@mentions` of its users,
`[[links]]` to its pages, `!!! note` admonitions, or a glossary that the text refers to. A syntax extension adds such a
syntax to the parser, without forking it. Add it to
[MarkdownParseOptions.SyntaxExtensions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.SyntaxExtensions).

| Extension | Reads | Node |
| --- | --- | --- |
| [MarkdownInlineExtension](cref:Meziantou.Framework.Markdown.MarkdownInlineExtension) | An inline that starts at a trigger character, such as `@name` | [MarkdownExtensionInlineSyntax](cref:Meziantou.Framework.Markdown.MarkdownExtensionInlineSyntax) |
| [MarkdownDelimiterExtension](cref:Meziantou.Framework.Markdown.MarkdownDelimiterExtension) | Content between runs of a character, such as `%%text%%` | [MarkdownExtensionInlineSyntax](cref:Meziantou.Framework.Markdown.MarkdownExtensionInlineSyntax) |
| [MarkdownFencedBlockExtension](cref:Meziantou.Framework.Markdown.MarkdownFencedBlockExtension) | Lines between an opening and a closing fence, such as `!!!` | [MarkdownExtensionBlockSyntax](cref:Meziantou.Framework.Markdown.MarkdownExtensionBlockSyntax) |
| [MarkdownParagraphExtension](cref:Meziantou.Framework.Markdown.MarkdownParagraphExtension) | Lines of a paragraph, such as `Speaker: message` | [MarkdownExtensionBlockSyntax](cref:Meziantou.Framework.Markdown.MarkdownExtensionBlockSyntax) |

The name of a node is the name of its extension. Its markers, such as the `@` of a mention or the fences of a block, and
its text, lines, blocks and inlines are the parts that the extension found. The tree still has every character of the
document. By default, the renderer writes the content escaped, without the markers: a
[node renderer](../04-Rendering.md#write-some-nodes-yourself) writes other HTML.

## Inline extensions

A [MarkdownInlineExtension](cref:Meziantou.Framework.Markdown.MarkdownInlineExtension) reads a construct that starts at
one of its trigger characters. At each of them, the parser calls
[TryParse](cref:Meziantou.Framework.Markdown.MarkdownInlineExtension.TryParse(Meziantou.Framework.Markdown.MarkdownInlineParseContext@)),
which reads [Text](cref:Meziantou.Framework.Markdown.MarkdownInlineParseContext.Text) from the trigger character, reports
the parts of the construct, and ends it with
[Complete](cref:Meziantou.Framework.Markdown.MarkdownInlineParseContext.Complete(System.Int32)):

- [AddMarker](cref:Meziantou.Framework.Markdown.MarkdownInlineParseContext.AddMarker(System.Int32,System.Int32))
  reports characters of the syntax, which the HTML does not have by default.
- [AddText](cref:Meziantou.Framework.Markdown.MarkdownInlineParseContext.AddText(System.Int32,System.Int32)) reports
  text, which is not read as Markdown.
- [AddInlines](cref:Meziantou.Framework.Markdown.MarkdownInlineParseContext.AddInlines(System.Int32,System.Int32))
  reports content that the parser reads as Markdown, such as emphasis and links.

The characters of the construct that no part covers are text.
[PreviousCharacter](cref:Meziantou.Framework.Markdown.MarkdownInlineParseContext.PreviousCharacter) tells whether the
construct starts a word. This extension reads mentions, and the node renderer writes them as links:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownInlineExtension_Mention;format=md-fenced-code-block;dedent=true -->
```csharp
// Reads @name, where the name is letters, digits and dashes, unless the @ follows a letter or a digit, as in an email address
private sealed class MentionExtension(string name = "mention", string triggerCharacters = "@") : MarkdownInlineExtension(name, triggerCharacters)
{
    public override bool TryParse(ref MarkdownInlineParseContext context)
    {
        if (char.IsLetterOrDigit(context.PreviousCharacter))
        {
            return false;
        }

        var length = 1;
        while (length < context.Text.Length && (char.IsAsciiLetterOrDigit(context.Text[length]) || context.Text[length] == '-'))
        {
            length++;
        }

        if (length == 1)
        {
            return false;
        }

        context.AddMarker(0, 1);
        context.AddText(1, length - 1);
        context.Complete(length);
        return true;
    }
}

// Writes the mentions as links to the page of the user
private sealed class MentionRenderer : MarkdownHtmlNodeRenderer
{
    public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
    {
        if (node is not MarkdownExtensionInlineSyntax { Name: "mention" } mention)
        {
            return false;
        }

        writer.WriteRawHtml("<a class=\"mention\" href=\"");
        writer.WriteUrl("/users/" + mention.Text);
        writer.WriteRawHtml("\">");
        writer.WriteText("@" + mention.Text);
        writer.WriteRawHtml("</a>");
        return true;
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_InlineExtension;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { SyntaxExtensions = [new MentionExtension()] };
var tree = MarkdownSyntaxTree.ParseText("Thanks @octo-cat, and *@hubot*!", options);
var mention = tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>().First();

Assert.Equal("mention", mention.Name);
Assert.Equal(["@"], mention.Markers.Select(marker => marker.Text));
Assert.Equal("octo-cat", mention.Text);

// By default, the text of the construct is written without its markers
Assert.Equal("<p>Thanks octo-cat, and <em>hubot</em>!</p>\n", MarkdownHtmlRenderer.ToHtml(tree));

// A node renderer writes other HTML
var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new MentionRenderer()] };
Assert.Equal("<p>Thanks <a class=\"mention\" href=\"/users/octo-cat\">@octo-cat</a>, and <em><a class=\"mention\" href=\"/users/hubot\">@hubot</a></em>!</p>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));

// An email address is not a mention: the extension reads the character before the @
Assert.Equal("<p>Mail me@example.com</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("Mail me@example.com", options), rendererOptions));
```
<!-- endref -->

The extensions are tried in the order of the list, before the built-in syntaxes: an extension can read `[[` before the
links, and return `false` to let the links read `[`. A construct is atomic, as a code span: the emphasis and the links
around it do not match the delimiters and the brackets in it. The parser does not call extensions in code spans, in
autolinks, in raw HTML, or after a backslash.

## Delimiter extensions

A [MarkdownDelimiterExtension](cref:Meziantou.Framework.Markdown.MarkdownDelimiterExtension) wraps content between runs
of a character, such as `%%`. Its runs follow the rules of emphasis, as the `==` of
[marked text](09-EmphasisExtras.md) do, so that they nest with emphasis in both directions: an inline extension cannot do
that, because it reads its whole construct at once. The extension only needs the character, the length of the shortest
run, and the most characters of each run that one construct uses:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_DelimiterExtension;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { SyntaxExtensions = [new MarkdownDelimiterExtension("highlight", '%')] };
var tree = MarkdownSyntaxTree.ParseText("Some %%important *and* urgent%% text, *nested %%both* ways%%.", options);
var highlight = tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>().Single();

Assert.Equal("highlight", highlight.Name);
Assert.Equal(["%%", "%%"], highlight.Markers.Select(marker => marker.Text));

// The runs take part in the emphasis: the second highlight is text, as the second == would be
var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new HighlightRenderer()] };
Assert.Equal("<p>Some <mark>important <em>and</em> urgent</mark> text, <em>nested %%both</em> ways%%.</p>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownDelimiterExtension_Highlight;format=md-fenced-code-block;dedent=true -->
```csharp
// Writes the constructs of the delimiter extensions as <mark> elements
private sealed class HighlightRenderer : MarkdownHtmlNodeRenderer
{
    public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
    {
        if (node is not MarkdownExtensionInlineSyntax highlight || highlight.Markers is not [{ Text: ['%', ..] }, ..])
        {
            return false;
        }

        writer.WriteRawHtml("<mark>");
        writer.WriteInlines(highlight.Inlines);
        writer.WriteRawHtml("</mark>");
        return true;
    }
}
```
<!-- endref -->

The character is ASCII punctuation that CommonMark does not read, and that no enabled extension reads: `~` with
strikethrough, or `|` with tables, makes the parser throw an `ArgumentException`.

## Fenced block extensions

A [MarkdownFencedBlockExtension](cref:Meziantou.Framework.Markdown.MarkdownFencedBlockExtension) reads a block between
two fences, as a fenced code block or a custom container. On a line that starts with one of its trigger characters,
[TryOpen](cref:Meziantou.Framework.Markdown.MarkdownFencedBlockExtension.TryOpen(System.ReadOnlySpan{System.Char})) returns
the length of the opening fence, and the rest of the line is the
[Info](cref:Meziantou.Framework.Markdown.MarkdownExtensionBlockSyntax.Info).
[TryClose](cref:Meziantou.Framework.Markdown.MarkdownFencedBlockExtension.TryClose(System.ReadOnlySpan{System.Char},System.ReadOnlySpan{System.Char}))
finds the closing fence; without one, the block ends with its container. The content is
[Lines](cref:Meziantou.Framework.Markdown.MarkdownExtensionBlockSyntax.Lines) of text, as the lines of a code block, or
[Blocks](cref:Meziantou.Framework.Markdown.MarkdownExtensionBlockSyntax.Blocks) of Markdown, as the content of a custom
container:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownFencedBlockExtension_Admonition;format=md-fenced-code-block;dedent=true -->
```csharp
// !!! kind, the blocks of the admonition, then !!!
private sealed class AdmonitionExtension() : MarkdownFencedBlockExtension("admonition", "!", MarkdownFencedBlockContent.Blocks)
{
    public override int TryOpen(ReadOnlySpan<char> line) => line.StartsWith("!!!", StringComparison.Ordinal) ? 3 : 0;

    public override int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence) => line.StartsWith(openingFence, StringComparison.Ordinal) ? openingFence.Length : 0;
}

// Writes the admonitions as a <div> whose classes are the kind of the admonition
private sealed class AdmonitionRenderer : MarkdownHtmlNodeRenderer
{
    public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
    {
        if (node is not MarkdownExtensionBlockSyntax { Name: "admonition" } admonition)
        {
            return false;
        }

        writer.EnsureNewLine();
        writer.WriteRawHtml("<div");
        writer.WriteAttributes(admonition, "admonition " + admonition.Info.Replace("*", "", StringComparison.Ordinal));
        writer.WriteRawHtml(">\n");
        writer.WriteBlocks(admonition.Blocks);
        writer.EnsureNewLine();
        writer.WriteRawHtml("</div>\n");
        return true;
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_FencedBlockExtension;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { SyntaxExtensions = [new AdmonitionExtension()] };
var tree = MarkdownSyntaxTree.ParseText("!!! warning\nDo not *push* on Friday.\n\n- Really.\n!!!\n", options);
var admonition = Assert.IsType<MarkdownExtensionBlockSyntax>(tree.GetRoot().Blocks.Single());

Assert.Equal("admonition", admonition.Name);
Assert.Equal("warning", admonition.Info);
Assert.Equal(["!!!", "!!!"], admonition.Markers.Select(marker => marker.Text));

// By default, the blocks are written without the fences
Assert.Equal("<p>Do not <em>push</em> on Friday.</p>\n<ul>\n<li>Really.</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree));

var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new AdmonitionRenderer()] };
Assert.Equal("<div class=\"admonition warning\">\n<p>Do not <em>push</em> on Friday.</p>\n<ul>\n<li>Really.</li>\n</ul>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));
```
<!-- endref -->

The fenced block extensions are tried before the built-in blocks. A closing fence closes the blocks in the content too,
even a code block.
[CanInterruptParagraph](cref:Meziantou.Framework.Markdown.MarkdownFencedBlockExtension.CanInterruptParagraph) set to
`false` keeps a fence after a paragraph in the paragraph, and
[ParseInfoAsInlines](cref:Meziantou.Framework.Markdown.MarkdownFencedBlockExtension.ParseInfoAsInlines) reads the info as
Markdown, such as a title with emphasis.

## Paragraph extensions

A [MarkdownParagraphExtension](cref:Meziantou.Framework.Markdown.MarkdownParagraphExtension) turns lines of a paragraph
into a block. It suits the syntaxes that are lines of text, without fences, such as a transcript. When a paragraph is
closed,
[TryConvert](cref:Meziantou.Framework.Markdown.MarkdownParagraphExtension.TryConvert(Meziantou.Framework.Markdown.MarkdownParagraphConvertContext@))
reads its lines, reports markers, text and inlines on them, as an inline extension does, then takes some lines with
[Complete](cref:Meziantou.Framework.Markdown.MarkdownParagraphConvertContext.Complete(System.Int32,System.Int32)).

Markdown joins the lines that follow each other in one paragraph, so a syntax without blank lines around it comes with
other text. The lines before the ones that the extension takes stay a paragraph, and the lines after them are offered to
the extensions again:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownParagraphExtension_Transcript;format=md-fenced-code-block;dedent=true -->
```csharp
// The lines "Speaker: message" of a paragraph: the first run of them is a transcript, whose speakers are text and whose
// messages are inlines
private sealed class TranscriptExtension() : MarkdownParagraphExtension("transcript")
{
    public override bool TryConvert(ref MarkdownParagraphConvertContext context)
    {
        var first = -1;
        var count = 0;
        for (var i = 0; i < context.LineCount; i++)
        {
            var line = context.GetLine(i);
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            var speaker = colon > 0 ? line[..colon] : [];
            if (speaker.IsEmpty || speaker.ContainsAnyExceptInRange('A', 'z') || speaker.ContainsAnyInRange('[', '`'))
            {
                if (first >= 0)
                {
                    break;
                }

                continue;
            }

            if (first < 0)
            {
                first = i;
            }

            context.AddText(i, 0, colon);
            context.AddMarker(i, colon, 1);
            context.AddInlines(i, colon + 2, line.Length - colon - 2);
            count++;
        }

        // A transcript has two lines or more
        if (count < 2)
        {
            return false;
        }

        context.Complete(first, count);
        return true;
    }
}

// Writes a transcript as a <dl>: the speakers are the terms, and the messages the definitions
private sealed class TranscriptRenderer : MarkdownHtmlNodeRenderer
{
    public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
    {
        if (node is not MarkdownExtensionBlockSyntax { Name: "transcript" } transcript)
        {
            return false;
        }

        writer.EnsureNewLine();
        writer.WriteRawHtml("<dl class=\"transcript\">\n");
        var message = new List<SyntaxNodeOrToken>();
        foreach (var child in transcript.ChildNodesAndTokens())
        {
            if (child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.ExtensionTextToken)
            {
                WriteMessage(writer, message);
                writer.WriteRawHtml("<dt>");
                writer.WriteText(child.AsToken().Text);
                writer.WriteRawHtml("</dt>");
            }
            else if (!(child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.ExtensionMarkerToken))
            {
                message.Add(child);
            }
        }

        WriteMessage(writer, message);
        writer.WriteRawHtml("</dl>\n");
        return true;
    }

    private static void WriteMessage(MarkdownHtmlWriter writer, List<SyntaxNodeOrToken> message)
    {
        if (message.Count > 0)
        {
            writer.WriteRawHtml("<dd>");
            writer.WriteInlines(message);
            writer.WriteRawHtml("</dd>\n");
            message.Clear();
        }
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_ParagraphExtension;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { SyntaxExtensions = [new TranscriptExtension()] };
var tree = MarkdownSyntaxTree.ParseText("The meeting went like this:\nAlice: Can we ship on *Friday*?\nBob: Only if the tests pass.\nThen we went for lunch.\n", options);
var blocks = tree.GetRoot().Blocks;

// The converter took the lines of the transcript: the lines around them are paragraphs
Assert.HasCount(3, blocks);
var transcript = Assert.IsType<MarkdownExtensionBlockSyntax>(blocks[1]);
Assert.Equal("transcript", transcript.Name);
Assert.Equal(["Alice", "Bob"], transcript.Texts.Select(speaker => speaker.Text));

var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new TranscriptRenderer()] };
Assert.Equal("<p>The meeting went like this:</p>\n<dl class=\"transcript\">\n<dt>Alice</dt><dd>Can we ship on <em>Friday</em>?</dd>\n<dt>Bob</dt><dd>Only if the tests pass.</dd>\n</dl>\n<p>Then we went for lunch.</p>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));
```
<!-- endref -->

The extension sees the lines without the markers of their containers, such as the `>` of a block quote, and after the
link reference definitions of the paragraph. A part is on one line, and the inlines of a line are read on their own. A
paragraph that a setext underline makes a heading is not offered.

## State of a document

Some syntaxes refer to definitions written elsewhere in the document, sometimes after the reference, as footnotes do.
[CreateState](cref:Meziantou.Framework.Markdown.MarkdownSyntaxExtension.CreateState) creates the state of an extension
for one document, and the contexts give it with `State`. `GetState` gives the state of another extension of the options,
such as the block extension whose definitions an inline extension reads.

The parser reads every block of the document before the first inline: the fenced blocks and the paragraph extensions
record their definitions, then the inline extensions read them. This glossary is defined after the text that uses it:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxExtension_Glossary;format=md-fenced-code-block;dedent=true -->
```csharp
// %%% glossary, then lines "Term: definition": the definitions are recorded in the state of the document
private sealed class GlossaryExtension() : MarkdownFencedBlockExtension("glossary", "%", MarkdownFencedBlockContent.RawLines)
{
    public override object? CreateState() => new Dictionary<string, string>(StringComparer.Ordinal);

    public override int TryOpen(ReadOnlySpan<char> line) => line.StartsWith("%%% glossary", StringComparison.Ordinal) ? 3 : 0;

    public override int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence) => line.StartsWith("%%%", StringComparison.Ordinal) ? 3 : 0;

    public override void OnClosed(ref MarkdownFencedBlockContext context)
    {
        var definitions = (Dictionary<string, string>)context.State!;
        for (var i = 0; i < context.LineCount; i++)
        {
            var line = context.GetLine(i);
            if (line.IndexOf(": ", StringComparison.Ordinal) is > 0 and var colon)
            {
                definitions[line[..colon].ToString()] = line[(colon + 2)..].ToString();
            }
        }
    }
}

// [Term], when the glossary defines the term: other brackets are read as usual, such as the ones of a link
private sealed class GlossaryTermExtension(GlossaryExtension glossary) : MarkdownInlineExtension("glossary-term", "[")
{
    public override bool TryParse(ref MarkdownInlineParseContext context)
    {
        var end = context.Text.IndexOf(']');
        var definitions = (Dictionary<string, string>)context.GetState(glossary)!;
        if (end < 2 || (end + 1 < context.Text.Length && context.Text[end + 1] is '(' or '[') || !definitions.ContainsKey(context.Text[1..end].ToString()))
        {
            return false;
        }

        context.AddMarker(0, 1);
        context.AddText(1, end - 1);
        context.AddMarker(end, 1);
        context.Complete(end + 1);
        return true;
    }
}

// Writes the terms as <abbr> elements, and nothing for the glossary
private sealed class GlossaryRenderer : MarkdownHtmlNodeRenderer
{
    public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
    {
        switch (node)
        {
            case MarkdownExtensionBlockSyntax { Name: "glossary" }:
                return true;

            case MarkdownExtensionInlineSyntax { Name: "glossary-term" } term:
                var glossary = term.SyntaxTree!.GetRoot().DescendantNodes().OfType<MarkdownExtensionBlockSyntax>().Where(block => block.Name == "glossary");
                var definition = glossary.SelectMany(block => block.Content.Split('\n')).FirstOrDefault(line => line.StartsWith(term.Text + ": ", StringComparison.Ordinal));
                writer.WriteRawHtml("<abbr title=\"");
                writer.WriteAttributeValue(definition is null ? "" : definition[(term.Text.Length + 2)..]);
                writer.WriteRawHtml("\">");
                writer.WriteText(term.Text);
                writer.WriteRawHtml("</abbr>");
                return true;

            default:
                return false;
        }
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_ExtensionState;format=md-fenced-code-block;dedent=true -->
```csharp
var glossary = new GlossaryExtension();
var options = new MarkdownParseOptions { SyntaxExtensions = [glossary, new GlossaryTermExtension(glossary)] };
var tree = MarkdownSyntaxTree.ParseText("Call the [API] from a [link](https://example.com).\n\n%%% glossary\nAPI: Application programming interface\n%%%\n", options);

// The glossary is read before the inlines, so a term can be used before its definition
var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new GlossaryRenderer()] };
Assert.Equal("<p>Call the <abbr title=\"Application programming interface\">API</abbr> from a <a href=\"https://example.com\">link</a>.</p>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));
```
<!-- endref -->

The state is not in the tree: a renderer that needs it, as this one, reads the tree. The cells of a grid table are
documents of their own, with their own state.

## Rules

- The names of the extensions are unique, and a character has at most one delimiter extension.
- The content of inline constructs nested more than 32 deep is text, so that a document cannot exhaust the stack.
- The [formatter](../06-Formatting.md) writes the constructs and the blocks as they are in the document.
- One extension reads every document, possibly on several threads at once: keep no state between calls, and use
  `CreateState` for the state of a document.
