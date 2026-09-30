// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip.Inlines;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

/// <summary>
/// Markdown renderer honoring trivia for a  <see cref="MarkdownDocument"/> object.
/// </summary>
/// Ensure to call the <see cref="MarkdownExtensions.EnableTrackTrivia"/> extension method when
/// parsing markdown to have trivia available for rendering.
public class RoundtripRenderer : TextRendererBase<RoundtripRenderer>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoundtripRenderer"/> class.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public RoundtripRenderer(TextWriter writer) : base(writer)
    {
        // Default block renderers
        ObjectRenderers.Add(new CodeBlockRenderer());
        ObjectRenderers.Add(new ListRenderer());
        ObjectRenderers.Add(new HeadingRenderer());
        ObjectRenderers.Add(new HtmlBlockRenderer());
        ObjectRenderers.Add(new ParagraphRenderer());
        ObjectRenderers.Add(new QuoteBlockRenderer());
        ObjectRenderers.Add(new ThematicBreakRenderer());
        ObjectRenderers.Add(new LinkReferenceDefinitionGroupRenderer());
        ObjectRenderers.Add(new LinkReferenceDefinitionRenderer());
        ObjectRenderers.Add(new EmptyBlockRenderer());

        // Default inline renderers
        ObjectRenderers.Add(new AutolinkInlineRenderer());
        ObjectRenderers.Add(new CodeInlineRenderer());
        ObjectRenderers.Add(new DelimiterInlineRenderer());
        ObjectRenderers.Add(new EmphasisInlineRenderer());
        ObjectRenderers.Add(new LineBreakInlineRenderer());
        ObjectRenderers.Add(new RoundtripHtmlInlineRenderer());
        ObjectRenderers.Add(new RoundtripHtmlEntityInlineRenderer());
        ObjectRenderers.Add(new LinkInlineRenderer());
        ObjectRenderers.Add(new LiteralInlineRenderer());
    }

    /// <summary>
    /// Writes the lines of a <see cref="LeafBlock"/>
    /// </summary>
    /// <param name="leafBlock">The leaf block.</param>
    public void WriteLeafRawLines(LeafBlock leafBlock)
    {
        ArgumentNullException.ThrowIfNull(leafBlock);
        if (leafBlock.Lines.Lines != null)
        {
            var lines = leafBlock.Lines;
            var slices = lines.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                var slice = slices[i].Slice;
                Write(ref slice);
                WriteLine(slice.NewLine);
            }
        }
    }

    /// <summary>
    /// Performs the render lines before operation.
    /// </summary>
    public void RenderLinesBefore(Block block)
    {
        if (block.LinesBefore is null)
        {
            return;
        }
        foreach (var line in block.LinesBefore)
        {
            Write(line);
            WriteLine(line.NewLine);
        }
    }

    /// <summary>
    /// Writes text that can span lines, starting each line with the indent of the containers, as the lines of a leaf block.
    /// </summary>
    internal void WriteLines(StringSlice slice) => WriteLines(slice.AsSpan());

    /// <summary>
    /// Writes text that can span lines, starting each line with the indent of the containers, as the lines of a leaf block.
    /// </summary>
    internal void WriteLines(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            var index = text.IndexOfAny('\r', '\n');
            if (index < 0)
            {
                Write(text);
                return;
            }

            var length = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? index + 2 : index + 1;
            Write(text[..length]);
            PreviousWasLine = true;
            text = text[length..];
        }
    }

    /// <summary>
    /// Performs the render lines after operation.
    /// </summary>
    public void RenderLinesAfter(Block block) => RenderLinesAfter(block, start: 0);

    // Writes the blank lines after a block, from the one at the specified index
    internal void RenderLinesAfter(Block block, int start)
    {
        PreviousWasLine = true;
        if (block.LinesAfter is null)
        {
            return;
        }

        for (var i = start; i < block.LinesAfter.Count; i++)
        {
            var line = block.LinesAfter[i];
            Write(line);
            WriteLine(line.NewLine);
        }
    }
}
