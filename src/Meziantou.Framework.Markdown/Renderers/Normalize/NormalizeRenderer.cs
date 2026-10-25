// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;
using System.Text;
using Meziantou.Framework.Markdown.Extensions.Tables;
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

        ObjectWriteBefore += (_, obj) =>
        {
            if (obj is MarkdownDocument document)
            {
                Definitions = GetDefinitions(document);
                _childIndexes.Clear();
                BulletCharacters.Clear();
                LiteralDelimiterCache = null;
                WrittenDefinitions.Clear();
                ContinuesParagraph = false;
                FencedCodeBlocks.Clear();
            }
        };
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

    // The pipes of the literals are escaped, so that a paragraph is not parsed back as a table
    internal bool EscapeLiteralPipes { get; set; }

    // The pipeline set up with this renderer, which decides the lines that can start a block
    internal MarkdownPipeline? Pipeline { get; set; }

    // Renders the inlines of a leaf block without indents, so that each line can be checked before it is written
    private NormalizeRenderer? _lineRenderer;

    // The index of the last block looked up in each container. The blocks are written in order, so the next lookup is
    // the same block, the one before it, or a block after it: searching forward from there is linear over the container.
    private readonly Dictionary<ContainerBlock, int> _childIndexes = new(ReferenceEqualityComparer.Instance);

    // The indented code blocks written fenced, as the list before them cannot be indented further
    internal HashSet<CodeBlock> FencedCodeBlocks { get; } = new(ReferenceEqualityComparer.Instance);

    // The bullet, or the delimiter, written for each list of the document
    internal Dictionary<ListBlock, char> BulletCharacters { get; } = new(ReferenceEqualityComparer.Instance);

    // The link reference definitions already written, before a paragraph that must continue them
    internal HashSet<LinkReferenceDefinition> WrittenDefinitions { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Writes the link reference definitions that the parser took from the block, whose source is in it, and that are not
    /// written yet.
    /// </summary>
    internal bool WriteDefinitionsOf(Block block)
    {
        var written = false;
        if (Definitions is not null)
        {
            foreach (var child in Definitions)
            {
                if (child is LinkReferenceDefinition definition && definition.Span.Start >= block.Span.Start && definition.Span.End <= block.Span.End && WrittenDefinitions.Add(definition))
                {
                    Write(definition);
                    EnsureLine();
                    written = true;
                }
            }
        }

        return written;
    }

    // Whether the group has definitions that are not written yet
    internal bool HasDefinitionsToWrite(LinkReferenceDefinitionGroup group)
    {
        foreach (var child in group)
        {
            if (child is LinkReferenceDefinition definition && !WrittenDefinitions.Contains(definition))
            {
                return true;
            }
        }

        return false;
    }

    // Whether the next paragraph is written right after link reference definitions or a pipe table, the paragraph it continues
    internal bool ContinuesParagraph { get; set; }

    // The link reference definitions of the document being written
    internal LinkReferenceDefinitionGroup? Definitions { get; private set; }

    private static LinkReferenceDefinitionGroup? GetDefinitions(MarkdownDocument document)
    {
        foreach (var block in document)
        {
            if (block is LinkReferenceDefinitionGroup group)
            {
                return group;
            }
        }

        return null;
    }

    // Whether a literal of an inline tree contains a code span delimiter, which all the code spans of the tree need
    internal (ContainerInline Root, char Delimiter, bool Result)? LiteralDelimiterCache { get; set; }

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

    // The blank line after a block of an item of a loose list can be the one that makes the list loose, so the options
    // that remove the blank line after a block do not apply there
    internal static bool IsInLooseListItem(Block block) => block.Parent is ListItemBlock { Parent: ListBlock { IsLoose: true } };

    /// <summary>
    /// Writes the inlines of a paragraph line by line. A continuation line that would start a block, or turn the paragraph
    /// into a setext heading, is indented by 4 spaces: the indentation is not part of the content, and cannot start a block.
    /// The first line is escaped when it would start a block, for example with the list item markers written before it.
    /// </summary>
    internal void WriteParagraphInline(LeafBlock leafBlock)
    {
        var text = RenderLeafInline(leafBlock);
        if (!EscapeTablePipes && !EscapeLiteralPipes && text.Contains('|', StringComparison.Ordinal) && IsGfmTable(text))
        {
            // The lines that continue a paragraph lazily are written indented, which makes them part of a GFM table
            EscapeLiteralPipes = true;
            try
            {
                text = RenderLeafInline(leafBlock);
            }
            finally
            {
                EscapeLiteralPipes = false;
            }
        }

        WriteParagraphInline(leafBlock, text);
    }

    private bool IsGfmTable(string text)
    {
        var parsers = (Pipeline ?? MarkdownConverter.DefaultPipeline).BlockParsers;
        if (!parsers.Any(parser => parser is GfmPipeTableParser))
        {
            return false;
        }

        foreach (var block in ParseBlocks(text))
        {
            if (block is Table)
            {
                return true;
            }
        }

        return false;
    }

    internal void WriteParagraphInline(LeafBlock leafBlock, string text)
    {
        var remaining = text.AsSpan();
        var isFirstLine = true;
        var previousLine = ReadOnlySpan<char>.Empty;
        var newLine = NewLine.LineFeed;
        while (true)
        {
            // The text of a setext heading keeps the carriage returns of the source, which end its lines too
            var end = remaining.IndexOfAny('\r', '\n');
            var line = end < 0 ? remaining : remaining[..end];
            var contentLine = line;
            if (isFirstLine && ContinuesParagraph)
            {
                // The line continues the paragraph of the link reference definitions or the pipe table written before it, so it
                // must not interrupt it
                ContinuesParagraph = false;
                if (CanInterruptParagraph(line))
                {
                    Write("    ");
                }
            }
            else if (isFirstLine)
            {
                // A character that the literal already writes escaped, such as a '<', needs no other backslash
                var escapeIndex = GetFirstLineEscapeIndex(leafBlock, line);
                if (escapeIndex >= 0 && line[escapeIndex] == '`')
                {
                    // A run of backticks is written with entities: the rest of an escaped run would still open a code span, and
                    // a backslash does not escape the backtick that closes a code span
                    Write(line[..escapeIndex]);
                    line = line[escapeIndex..];
                    while (line is ['`', ..])
                    {
                        Write("&#96;");
                        line = line[1..];
                    }
                }
                else if (escapeIndex >= 0 && line[escapeIndex] != '\\')
                {
                    Write(line[..escapeIndex]);
                    Write('\\');
                    line = line[escapeIndex..];
                }
            }
            else
            {
                if (newLine == NewLine.LineFeed)
                {
                    WriteLine();
                }
                else
                {
                    WriteLine(newLine);
                }

                if (CanInterruptParagraph(previousLine, line))
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

            newLine = remaining[end] == '\n' ? NewLine.LineFeed : end + 1 < remaining.Length && remaining[end + 1] == '\n' ? NewLine.CarriageReturnLineFeed : NewLine.CarriageReturn;
            remaining = remaining[(end + (newLine == NewLine.CarriageReturnLineFeed ? 2 : 1))..];
            isFirstLine = false;
            previousLine = contentLine;
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
            renderer.EscapeLiteralPipes = EscapeLiteralPipes;
            renderer.Pipeline = Pipeline;
            renderer.LiteralDelimiterCache = null;
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
            markers = ListRenderer.GetMarker(this, list, GetChildIndex(item)) + markers;
            block = list;
        }

        return markers;
    }

    /// <summary>
    /// Gets the index of the block in its parent, or -1.
    /// </summary>
    internal int GetChildIndex(Block block)
    {
        if (block.Parent is not { } parent)
        {
            return -1;
        }

        if (_childIndexes.TryGetValue(parent, out var previous))
        {
            for (var index = Math.Max(0, previous - 1); index < parent.Count; index++)
            {
                if (parent[index] == block)
                {
                    _childIndexes[parent] = index;
                    return index;
                }
            }
        }

        var result = parent.IndexOf(block);
        _childIndexes[parent] = result;
        return result;
    }

    internal Block? GetPreviousSibling(Block block)
    {
        var index = GetChildIndex(block);
        return index > 0 ? block.Parent![index - 1] : null;
    }

    internal Block? GetNextSibling(Block block)
    {
        var index = GetChildIndex(block);
        return index >= 0 && index + 1 < block.Parent!.Count ? block.Parent[index + 1] : null;
    }

    private bool CanInterruptParagraph(ReadOnlySpan<char> line)
    {
        var content = line.TrimStart(' ');
        if (content.IsEmpty || line.Length - content.Length >= 4 || !CanOpenBlock(content[0]))
        {
            return false;
        }

        // Let the block parsers decide, as the rules are subtle (setext underlines, ordered lists starting at 1, HTML block kinds, extensions...).
        // Some parsers consume the line without creating a block, such as an abbreviation definition.
        return ParseBlocks(string.Concat("a\n", line)) is not [ParagraphBlock { Lines.Count: 2 }];
    }

    // Whether the line would interrupt the paragraph after the previous line. A line can depend on the line before it, such as
    // the delimiter row of a GFM table, whose number of cells must be the one of the header row before it.
    private bool CanInterruptParagraph(ReadOnlySpan<char> previousLine, ReadOnlySpan<char> line)
    {
        var content = line.TrimStart(' ');
        if (previousLine.IsEmpty || content.IsEmpty || line.Length - content.Length >= 4 || !CanOpenBlock(content[0]))
        {
            return CanInterruptParagraph(line);
        }

        // The previous line is a continuation line, as when it is written
        return ParseBlocks(string.Concat("a\n    ", previousLine, "\n", line)) is not [ParagraphBlock { Lines.Count: 3 }];
    }

    // Returns the index before which the first line must be escaped, or -1
    private int GetFirstLineEscapeIndex(LeafBlock leafBlock, ReadOnlySpan<char> line)
    {
        if (StartsParagraph(leafBlock, line))
        {
            return -1;
        }

        // The "[" of a link that is not closed is written as is, and can start a link reference definition
        if (leafBlock.Inline?.FirstChild is LinkDelimiterInline { IsImage: false } && line is ['[', ..])
        {
            return 0;
        }

        // Only a literal can be escaped. It is compared to the line before the characters that are written escaped.
        if (leafBlock.Inline?.FirstChild is not LiteralInline { IsFirstCharacterEscaped: false } literal || !line.StartsWith(GetWrittenAsIs(literal.Content.AsSpan(), line.Length)))
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

    private static ReadOnlySpan<char> GetWrittenAsIs(ReadOnlySpan<char> content, int maxLength)
    {
        var index = content.IndexOfAny('\\', '|', '<');
        var text = index < 0 ? content : content[..index];
        return text[..Math.Min(text.Length, maxLength)];
    }

    /// <summary>
    /// Escapes the text of a link, so that it is parsed back as the same text.
    /// </summary>
    internal string EscapeLinkText(string text, string destination)
    {
        // Code spans, autolinks and raw HTML take precedence over links, even outside the link text. Other inlines, such as
        // emphasis, depend on the pipeline: escape more characters until the text is parsed back.
        foreach (var characters in (ReadOnlySpan<string>)["`<[]&", "`<[]&*_~"])
        {
            var escaped = Escape(text, characters);
            if (IsLinkWithText(string.Concat("[", escaped, "](", destination, ")"), text))
            {
                return escaped;
            }
        }

        return Escape(text, characters: null);

        static string Escape(string text, string? characters)
        {
            var builder = new StringBuilder(text.Length + 8);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '`')
                {
                    // An escaped backtick still pairs with a backtick before the link
                    builder.Append("&#96;");
                    continue;
                }

                var escape = characters is null
                    ? c.IsAsciiPunctuation()
                    : (characters.Contains(c, StringComparison.Ordinal) && (c != '&' || LinkInlineRenderer.IsEntityStart(text.AsSpan(i + 1))))
                        || (c == '\\' && (i + 1 == text.Length || text[i + 1].IsAsciiPunctuation()));
                if (escape)
                {
                    builder.Append('\\');
                }

                builder.Append(c);
            }

            return builder.ToString();
        }
    }

    private bool IsLinkWithText(string markdown, string text)
    {
        if (ParseBlocks(markdown) is not [ParagraphBlock paragraph])
        {
            return false;
        }

        var processor = InlineProcessor.Rent(new MarkdownDocument(), (Pipeline ?? MarkdownConverter.DefaultPipeline).InlineParsers, preciseSourcelocation: false, context: null, trackTrivia: false);
        try
        {
            processor.ProcessInlineLeaf(paragraph);
        }
        finally
        {
            InlineProcessor.Release(processor);
        }

        if (paragraph.Inline?.FirstChild is not LinkInline { NextSibling: null } link)
        {
            return false;
        }

        var builder = new StringBuilder(text.Length);
        for (var inline = link.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.AsSpan());
                    break;
                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded.AsSpan());
                    break;
                default:
                    return false;
            }
        }

        return builder.Equals(text);
    }

    // Whether the first line of the paragraph, after the list item markers written on the same line, starts a paragraph
    private bool StartsParagraph(LeafBlock leafBlock, ReadOnlySpan<char> line)
    {
        // A link reference definition is parsed from a paragraph, so no block parser opens on its "["
        if (line.IsEmpty || (!CanOpenBlock(line[0]) && line[0] != '['))
        {
            return true;
        }

        var markers = leafBlock.Parent is ListItemBlock ? GetListMarkersBefore(leafBlock) : string.Empty;
        Block? block = ParseBlocks(string.Concat(markers, line)) is [var first] ? first : null;
        // A marker can be followed by several spaces, so the levels are the lists the block starts on the line of
        var depth = 0;
        if (markers.Length > 0)
        {
            for (Block current = leafBlock; current.Parent is ListItemBlock { Count: > 0 } parentItem && parentItem[0] == current && parentItem.Parent is ListBlock parentList; current = parentList)
            {
                depth++;
            }
        }

        for (; depth > 0; depth--)
        {
            // Each marker opens a list containing a single item
            block = block is ListBlock { Count: 1 } list && list[0] is ListItemBlock { Count: > 0 } item ? item[0] : null;
        }

        return block is ParagraphBlock { Lines.Count: 1 };
    }

    /// <summary>
    /// Gets a value indicating whether the first line of the paragraph starts another block, and cannot be escaped, such as
    /// an HTML tag alone on its line. Such a paragraph can only continue the lines before it.
    /// </summary>
    internal bool IsParagraphContinuationOnly(LeafBlock paragraph)
    {
        var text = RenderLeafInline(paragraph).AsSpan();
        var end = text.IndexOfAny('\r', '\n');
        var line = end < 0 ? text : text[..end];
        return GetFirstLineEscapeIndex(paragraph, line) < 0 && !StartsParagraph(paragraph, line);
    }

    // Whether the block is a paragraph, or a setext heading, whose first line can only continue the lines before it
    internal bool IsParagraphContinuationOnly(Block? block)
    {
        // A heading on a single line is written as an ATX heading
        return (block is ParagraphBlock || (block is HeadingBlock { IsSetext: true, Level: 1 or 2 } heading && RenderLeafInline(heading).AsSpan().ContainsAny('\r', '\n')))
            && IsParagraphContinuationOnly((LeafBlock)block);
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
        ArgumentNullException.ThrowIfNull(leafBlock);
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
