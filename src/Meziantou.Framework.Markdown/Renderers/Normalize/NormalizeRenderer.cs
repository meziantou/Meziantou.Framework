// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// Default HTML renderer for a Markdown <see cref="MarkdownDocument"/> object.
/// </summary>
/// <seealso cref="TextRendererBase{NormalizeRenderer}" />
public class NormalizeRenderer : TextRendererBase<NormalizeRenderer>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NormalizeRenderer"/> class.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="options">The normalize options</param>
    public NormalizeRenderer(TextWriter writer, NormalizeOptions? options = null) : base(writer)
    {
        Options = options ?? new NormalizeOptions();
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

        // Default inline renderers
        ObjectRenderers.Add(new AutolinkInlineRenderer());
        ObjectRenderers.Add(new CodeInlineRenderer());
        ObjectRenderers.Add(new DelimiterInlineRenderer());
        ObjectRenderers.Add(new EmphasisInlineRenderer());
        ObjectRenderers.Add(new LineBreakInlineRenderer());
        ObjectRenderers.Add(new NormalizeHtmlInlineRenderer());
        ObjectRenderers.Add(new NormalizeHtmlEntityInlineRenderer());
        ObjectRenderers.Add(new LinkInlineRenderer());
        ObjectRenderers.Add(new LiteralInlineRenderer());
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public NormalizeOptions Options { get; }

    /// <summary>
    /// Gets or sets the compact paragraph.
    /// </summary>
    public bool CompactParagraph { get; set; }

    // Raw inline content must escape pipes when emitted inside a GFM table.
    internal bool EscapeTablePipes { get; set; }

    // The pipeline set up with this renderer, which decides the lines that can start a block
    internal MarkdownPipeline? Pipeline { get; set; }

    // Renders the inlines of a leaf block without indents, so that each line can be checked before it is written
    private NormalizeRenderer? _lineRenderer;

    /// <summary>
    /// Performs the finish block operation.
    /// </summary>
    public void FinishBlock(bool emptyLine)
    {
        if (!IsLastInContainer)
        {
            if (CompactParagraph)
            {
                // A blank line between the blocks of a tight list item would make the list loose
                EnsureLine();
            }
            else
            {
                WriteLine();
                if (emptyLine)
                {
                    WriteLine();
                }
            }
        }
    }

    /// <summary>
    /// Writes the inlines of a paragraph line by line. A continuation line that would start a block, or turn the paragraph
    /// into a setext heading, is indented by 4 spaces: the indentation is not part of the content, and cannot start a block.
    /// The first line is escaped when it would start a block, for example with the list item markers written before it.
    /// </summary>
    internal void WriteParagraphInline(LeafBlock leafBlock) => WriteParagraphInline(leafBlock, RenderLeafInline(leafBlock));

    internal void WriteParagraphInline(LeafBlock leafBlock, string text)
    {
        var remaining = text.AsSpan();
        var isFirstLine = true;
        while (true)
        {
            var end = remaining.IndexOf('\n');
            var line = end < 0 ? remaining : remaining[..end];
            if (isFirstLine)
            {
                var escapeIndex = GetFirstLineEscapeIndex(leafBlock, line);
                if (escapeIndex >= 0)
                {
                    Write(line[..escapeIndex]);
                    Write('\\');
                    line = line[escapeIndex..];
                }
            }
            else
            {
                WriteLine();
                if (CanInterruptParagraph(line))
                {
                    Write("    ");
                }
            }

            if (line.IsEmpty)
            {
                // Still writes the pending indents, such as a list item marker
                Write(string.Empty);
            }
            else
            {
                Write(line);
            }

            if (end < 0)
            {
                break;
            }

            remaining = remaining[(end + 1)..];
            isFirstLine = false;
        }
    }

    internal string RenderLeafInline(LeafBlock leafBlock)
    {
        var renderer = _lineRenderer;
        if (renderer is null)
        {
            renderer = new NormalizeRenderer(new StringWriter(), Options)
            {
                MaximumNestingDepth = MaximumNestingDepth,
            };
            renderer.ObjectRenderers.Clear();
            renderer.ObjectRenderers.AddRange(ObjectRenderers);
        }

        // An inline renderer could render a nested leaf block
        _lineRenderer = null;
        try
        {
            renderer.EscapeTablePipes = EscapeTablePipes;
            renderer.WriteLeafInline(leafBlock);
            var builder = ((StringWriter)renderer.Writer).GetStringBuilder();
            var text = builder.ToString();
            builder.Clear();
            return text;
        }
        finally
        {
            _lineRenderer = renderer;
        }
    }

    /// <summary>
    /// Gets the markers of the list items that start on the same line as the specified block.
    /// </summary>
    internal string GetListMarkersBefore(Block block)
    {
        var markers = string.Empty;
        while (block.Parent is ListItemBlock item && item.Count > 0 && item[0] == block && item.Parent is ListBlock list)
        {
            markers = ListRenderer.GetMarker(this, list, list.IndexOf(item)) + markers;
            block = list;
        }

        return markers;
    }

    private bool CanInterruptParagraph(ReadOnlySpan<char> line)
    {
        var content = line.TrimStart(' ');
        if (content.IsEmpty || line.Length - content.Length >= 4 || !CanOpenBlock(content[0]))
        {
            return false;
        }

        // Let the block parsers decide, as the rules are subtle (setext underlines, ordered lists starting at 1, HTML block kinds, extensions...)
        return ParseBlocks(string.Concat("a\n", line)) is not [ParagraphBlock];
    }

    // Returns the index before which the first line must be escaped, or -1
    private int GetFirstLineEscapeIndex(LeafBlock leafBlock, ReadOnlySpan<char> line)
    {
        if (line.IsEmpty || !CanOpenBlock(line[0]))
        {
            return -1;
        }

        var markers = leafBlock.Parent is ListItemBlock ? GetListMarkersBefore(leafBlock) : string.Empty;
        Block? block = ParseBlocks(string.Concat(markers, line)) is [var first] ? first : null;
        for (var depth = markers.AsSpan().Count(' '); depth > 0; depth--)
        {
            // Each marker opens a list containing a single item
            block = block is ListBlock { Count: 1 } list && list[0] is ListItemBlock { Count: > 0 } item ? item[0] : null;
        }

        if (block is ParagraphBlock)
        {
            return -1;
        }

        // Only a literal can be escaped
        if (leafBlock.Inline?.FirstChild is not LiteralInline { IsFirstCharacterEscaped: false } literal || !line.StartsWith(literal.Content.AsSpan()[..Math.Min(literal.Content.Length, line.Length)]))
        {
            return -1;
        }

        var index = 0;
        if (!line[0].IsAsciiPunctuation())
        {
            // An ordered list item, whose delimiter can be escaped
            while (index < literal.Content.Length && char.IsAsciiLetterOrDigit(line[index]))
            {
                index++;
            }

            if (index == 0 || index >= literal.Content.Length || line[index] is not ('.' or ')'))
            {
                return -1;
            }
        }

        return index;
    }

    private bool CanOpenBlock(char c)
    {
        var parsers = (Pipeline ?? MarkdownConverter.DefaultPipeline).BlockParsers;
        return parsers.GetParsersForOpeningCharacter(c) is not null
            || (parsers.GlobalParsers is not null && !Array.TrueForAll(parsers.GlobalParsers, parser => parser is ParagraphBlockParser or IndentedCodeBlockParser));
    }

    private MarkdownDocument ParseBlocks(string text)
    {
        var document = new MarkdownDocument { IsOpen = true };
        var processor = BlockProcessor.Rent(document, (Pipeline ?? MarkdownConverter.DefaultPipeline).BlockParsers, context: null, trackTrivia: false);
        try
        {
            processor.Open(document);
            var lineReader = new LineReader(text);
            while (lineReader.ReadLine() is { Text: not null } slice)
            {
                processor.ProcessLine(slice);
            }

            processor.CloseAll(true);
        }
        finally
        {
            BlockProcessor.Release(processor);
        }

        return document;
    }

    ///// <summary>
    ///// Writes the attached <see cref="HtmlAttributes"/> on the specified <see cref="MarkdownObject"/>.
    ///// </summary>
    ///// <param name="obj">The object.</param>
    ///// <returns></returns>
    //public NormalizeRenderer WriteAttributes(MarkdownObject obj)
    //{
    //    if (obj is null) throw new ArgumentNullException(nameof(obj));
    //    return WriteAttributes(obj.TryGetAttributes());
    //}

    ///// <summary>
    ///// Writes the specified <see cref="HtmlAttributes"/>.
    ///// </summary>
    ///// <param name="attributes">The attributes to render.</param>
    ///// <returns>This instance</returns>
    //public NormalizeRenderer WriteAttributes(HtmlAttributes attributes)
    //{
    //    if (attributes is null)
    //    {
    //        return this;
    //    }

    //    if (attributes.Id != null)
    //    {
    //        Write(" id=\"").WriteEscape(attributes.Id).Write('"');
    //    }

    //    if (attributes.Classes != null && attributes.Classes.Count > 0)
    //    {
    //        Write(" class=\"");
    //        for (int i = 0; i < attributes.Classes.Count; i++)
    //        {
    //            var cssClass = attributes.Classes[i];
    //            if (i > 0)
    //            {
    //                Write(" ");
    //            }
    //            WriteEscape(cssClass);
    //        }
    //        Write('"');
    //    }

    //    if (attributes.Properties != null && attributes.Properties.Count > 0)
    //    {
    //        foreach (var property in attributes.Properties)
    //        {
    //            Write(' ').Write(property.Key);
    //            if (property.Value != null)
    //            {
    //                Write('=').Write('"');
    //                WriteEscape(property.Value);
    //                Write('"');
    //            }
    //        }
    //    }

    //    return this;
    //}

    /// <summary>
    /// Writes the lines of a <see cref="LeafBlock"/>
    /// </summary>
    /// <param name="leafBlock">The leaf block.</param>
    /// <param name="writeEndOfLines">if set to <c>true</c> write end of lines.</param>
    /// <param name="indent">Whether to write indents.</param>
    /// <returns>This instance</returns>
    public NormalizeRenderer WriteLeafRawLines(LeafBlock leafBlock, bool writeEndOfLines, bool indent = false)
    {
        if (leafBlock is null) ThrowHelper.ArgumentNullException_leafBlock();
        if (leafBlock.Lines.Lines != null)
        {
            var lines = leafBlock.Lines;
            var slices = lines.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!writeEndOfLines && i > 0)
                {
                    WriteLine();
                }

                if (indent)
                {
                    Write("    ");
                }

                Write(ref slices[i].Slice);

                if (writeEndOfLines)
                {
                    WriteLine();
                }
            }
        }
        return this;
    }
}
