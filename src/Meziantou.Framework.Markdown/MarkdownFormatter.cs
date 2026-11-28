using System.Collections.Immutable;
using System.Text;
using Meziantou.Framework.Markdown.Syntax.InternalSyntax;
using GreenNode = Meziantou.Framework.Language.InternalSyntax.GreenNode;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// Writes a Markdown document in a consistent style, without changing its HTML: one blank line between the blocks, the
/// markers of the containers on every line, the same marker for the bullet lists, ATX headings, fenced code blocks and
/// aligned tables, as <see cref="MarkdownFormattingOptions"/> configures.
/// </summary>
/// <remarks>
/// The text of the paragraphs, the headings and the cells, the code, and the HTML are kept: only the blocks around them
/// change. The blank lines of the lists are kept, as they make the difference between a tight and a loose list, and the
/// indentation of a line of a paragraph is removed, unless the line would start a block without it. The formatter checks
/// that the formatted text renders as the same HTML, and returns the text as it is when it does not, which can happen
/// with rare combinations of the extensions of Markdig, such as lazy lines that change how a pipe table is read.
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs" ref:region="MarkdownFormatter_Usage" ref:dedent="true">
/// var tree = MarkdownSyntaxTree.ParseText("""
///     Title
///     =====
///     * one
///     *   two
///
///     3) a
///     7) b
///
///     Code:
///
///         var x = 1;
///
///     &gt; quote
///     continued
///
///     | a | b |
///     |-|:-:|
///     | long cell | x |
///     """, MarkdownParseOptions.GitHubFlavored);
///
/// var formatted = MarkdownFormatter.Format(tree);
///
/// Assert.Equal("""
///     # Title
///
///     - one
///     - two
///
///     3) a
///     4) b
///
///     Code:
///
///     ```
///     var x = 1;
///     ```
///
///     &gt; quote
///     &gt; continued
///
///     | a         | b   |
///     | --------- | :-: |
///     | long cell | x   |
///
///     """, formatted.GetRoot().ToFullString());
/// Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(formatted));
/// </code>
/// </example>
public static class MarkdownFormatter
{
    /// <summary>Formats a document with <see cref="MarkdownFormattingOptions.Default"/>.</summary>
    /// <returns>
    /// The tree of the formatted text, parsed with the options of <paramref name="tree"/>, or <paramref name="tree"/> when its
    /// text is already formatted or when the formatted text would not render as the same HTML.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static MarkdownSyntaxTree Format(MarkdownSyntaxTree tree) => Format(tree, options: null);

    /// <summary>Formats a document.</summary>
    /// <param name="tree">The document.</param>
    /// <param name="options">How to write the document, or <see langword="null"/> for <see cref="MarkdownFormattingOptions.Default"/>.</param>
    /// <returns>
    /// The tree of the formatted text, parsed with the options of <paramref name="tree"/>, or <paramref name="tree"/> when its
    /// text is already formatted or when the formatted text would not render as the same HTML.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A character or the line ending of <paramref name="options"/> is not one that it allows.</exception>
    public static MarkdownSyntaxTree Format(MarkdownSyntaxTree tree, MarkdownFormattingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var text = WriteText(tree, options, out var original);

        // A document that is already formatted is written as it is
        if (string.Equals(text, original, StringComparison.Ordinal))
        {
            return tree;
        }

        var formatted = tree.Reparse(SourceText.From(text));
        return RendersAsTheSameHtml(tree, formatted) ? formatted : tree;
    }

    /// <summary>Formats a document and returns its text.</summary>
    /// <param name="tree">The document.</param>
    /// <param name="options">How to write the document, or <see langword="null"/> for <see cref="MarkdownFormattingOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A character or the line ending of <paramref name="options"/> is not one that it allows.</exception>
    public static string FormatText(MarkdownSyntaxTree tree, MarkdownFormattingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var text = WriteText(tree, options, out var original);
        if (string.Equals(text, original, StringComparison.Ordinal))
        {
            return text;
        }

        return RendersAsTheSameHtml(tree, tree.Reparse(SourceText.From(text))) ? text : original;
    }

    // The line endings of the code change with the line ending of the document
    private static bool RendersAsTheSameHtml(MarkdownSyntaxTree tree, MarkdownSyntaxTree formatted) => MarkdownHtmlRenderer.RenderAsTheSameHtml(tree, formatted);

    /// <summary>Writes the formatted text, without checking that it renders as the same HTML.</summary>
    /// <param name="tree">The document.</param>
    /// <param name="options">How to write the document.</param>
    /// <param name="original">The text of the document.</param>
    internal static string WriteText(MarkdownSyntaxTree tree, MarkdownFormattingOptions? options, out string original)
    {
        ArgumentNullException.ThrowIfNull(tree);
        options ??= MarkdownFormattingOptions.Default;
        Validate(options);

        // The text of the tree is the text of its root: the tree does not need to write it again
        var text = tree.GetText().Text;
        original = text;
        var lines = new Writer(tree.Options, options, text).WriteBlocks(tree.GetRoot().Blocks, BlankLines.Always);
        if (lines.Count == 0)
        {
            return "";
        }

        var newLine = options.NewLine ?? GetFirstLineEnding(text);
        var length = (long)lines.Count * newLine.Length;
        foreach (var line in lines)
        {
            length += line.Length;
        }

        return string.Create(checked((int)length), (lines, newLine), (span, state) =>
        {
            foreach (var line in state.lines)
            {
                line.CopyTo(span);
                state.newLine.CopyTo(span[line.Length..]);
                span = span[(line.Length + state.newLine.Length)..];
            }
        });
    }

    private static void Validate(MarkdownFormattingOptions options)
    {
        if (options.NewLine is not (null or "\n" or "\r\n" or "\r"))
        {
            throw new ArgumentException("The line ending must be \\n, \\r\\n or \\r.", nameof(options));
        }

        if (options.BulletListMarker is not (null or '-' or '*' or '+'))
        {
            throw new ArgumentException("The marker of the bullet lists must be -, * or +.", nameof(options));
        }

        if (options.CodeFenceCharacter is not ('`' or '~'))
        {
            throw new ArgumentException("The character of the code fences must be ` or ~.", nameof(options));
        }

        if (options.ThematicBreakCharacter is not ('-' or '*' or '_'))
        {
            throw new ArgumentException("The character of the thematic breaks must be -, * or _.", nameof(options));
        }
    }

    private static string GetFirstLineEnding(string text)
    {
        var index = text.AsSpan().IndexOfAny('\r', '\n');
        if (index < 0 || text[index] == '\n')
        {
            return "\n";
        }

        return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
    }

    private enum BlankLines
    {
        // One blank line between two blocks
        Always,

        // No blank line, as in a tight list
        Never,

        // One blank line where the text has at least one
        Preserve,
    }

    private sealed record ContinuedBlock(string Text, SyntaxKind Kind);

    private sealed class Writer(MarkdownParseOptions parseOptions, MarkdownFormattingOptions options, string source)
    {
        // The inlines of Markdig that depend on the character before or after them, which a pipe or a space can be
        private const MarkdownExtensions ContextSensitiveInlines = MarkdownExtensions.JiraLinks | MarkdownExtensions.MarkdigAutolinks | MarkdownExtensions.SmartyPants | MarkdownExtensions.Emoji;

        // The results of the parses that check a bullet marker, as the items of a list often have the same content
        private readonly Dictionary<(string Text, SyntaxKind Kind, bool Shallow), bool> _bulletMarkerChecks = [];

        public List<string> WriteBlocks(ImmutableArray<MarkdownBlockSyntax> blocks, BlankLines blankLines) => WriteBlocks(blocks, blankLines, out _);

        // The lines of the blocks, and the number of lines of the first block, which start them
        private List<string> WriteBlocks(ImmutableArray<MarkdownBlockSyntax> blocks, BlankLines blankLines, out int firstBlockLineCount)
        {
            firstBlockLineCount = 0;
            var lines = new List<string>();
            char? previousBulletMarker = null;
            var previousStart = 0;
            for (var i = 0; i < blocks.Length; i++)
            {
                var block = blocks[i];
                var previous = i > 0 ? blocks[i - 1] : null;
                var blankLine = previous is not null && blankLines switch
                {
                    BlankLines.Always => !(IsDefinitionSequence(previous, block) || IsMarkdigTable(previous) || IsMarkdigTable(block) || block is MarkdownTableSyntax || IsAfterLinkReferenceDefinition(previous, block) || EndsWithDefinition(previous)) || HasBlankLineBefore(block),
                    BlankLines.Never => false,
                    _ => HasBlankLineBefore(block),
                };

                if (blankLine)
                {
                    AddBlankLine(lines);
                }
                else if (previous is MarkdownBlockQuoteSyntax or MarkdownAlertSyntax && GetLastBlock(previous) is MarkdownParagraphSyntax
                    && (block is MarkdownParagraphSyntax or MarkdownTableSyntax or MarkdownLinkReferenceDefinitionSyntax or MarkdownDefinitionListSyntax || (block is MarkdownHeadingSyntax heading && StaysSetext(heading, GetHeadingLines(heading)))))
                {
                    // A line of text after the paragraph that ends a block quote would be a lazy line of the paragraph: an empty
                    // line of the block quote ends the paragraph (not before a setext heading written as an ATX heading, which
                    // interrupts the paragraph: fuzz regression)
                    lines.Add(">");
                }

                if (block is MarkdownListSyntax list)
                {
                    // A list after a block without a blank line, such as a pipe table, is a list only if its first line starts one
                    var context = previous is not null && !blankLine ? string.Join('\n', lines.Skip(previousStart)) : null;
                    previousStart = lines.Count;
                    previousBulletMarker = WriteList(lines, list, previousBulletMarker, GetIndentationOfNextBlock(blocks, i), context);
                }
                else
                {
                    previousBulletMarker = null;
                    var start = lines.Count;
                    WriteBlock(lines, block, previous, i + 1 < blocks.Length ? blocks[i + 1] : null, blankLine, previousStart);
                    previousStart = start;
                }

                if (i == 0)
                {
                    firstBlockLineCount = lines.Count;
                }
            }

            return lines;
        }

        // The definitions that follow each other stay together, as a formatter of code keeps the using directives together
        private static bool IsDefinitionSequence(MarkdownBlockSyntax previous, MarkdownBlockSyntax block)
        {
            return (previous, block) is (MarkdownLinkReferenceDefinitionSyntax, MarkdownLinkReferenceDefinitionSyntax)
                or (MarkdownAbbreviationDefinitionSyntax, MarkdownAbbreviationDefinitionSyntax)
                or (MarkdownFootnoteDefinitionSyntax, MarkdownFootnoteDefinitionSyntax);
        }

        // Markdig reads a pipe table from the inlines of a paragraph: the lines before and after the table are in the same
        // paragraph, and a quote of SmartyPants can be paired with a quote of the table. The grid tables, the pipe tables and
        // the definition lists keep the blank lines around them, and a table keeps the one before it, as the header row of a table
        // of GFM can interrupt a paragraph.
        private static bool IsMarkdigTable(MarkdownBlockSyntax block) => block is MarkdownTableSyntax { IsPipeTable: true } or MarkdownGridTableSyntax or MarkdownDefinitionListSyntax;

        // The link reference definitions are read from the start of a paragraph: the paragraph after them is the rest of it,
        // whose first line would start a block after a blank line, such as "*", which is an empty list item
        private bool IsAfterLinkReferenceDefinition(MarkdownBlockSyntax previous, MarkdownBlockSyntax block)
        {
            return previous is MarkdownLinkReferenceDefinitionSyntax && (block is MarkdownParagraphSyntax || (block is MarkdownHeadingSyntax heading && StaysSetext(heading, GetHeadingLines(heading))));
        }

        // A definition can end with its blank line, which is the one before the next block
        private static void AddBlankLine(List<string> lines)
        {
            if (lines.Count == 0 || lines[^1].Length != 0)
            {
                lines.Add("");
            }
        }

        // Markdig counts a blank line after a definition as a blank line of the definition, which renders its paragraphs in
        // <p>, even when the definition is in a list item and the blank line after the list
        private static bool EndsWithDefinition(MarkdownBlockSyntax block) => GetLastBlock(block) is MarkdownDefinitionListSyntax;

        // The block that ends a block, in its last containers
        private static MarkdownBlockSyntax GetLastBlock(MarkdownBlockSyntax block)
        {
            while (true)
            {
                var children = block switch
                {
                    MarkdownListSyntax list => list.Items.Length > 0 ? list.Items[^1].Blocks : [],
                    MarkdownListItemSyntax item => item.Blocks,
                    MarkdownBlockQuoteSyntax quote => quote.Blocks,
                    MarkdownAlertSyntax alert => alert.Blocks,
                    MarkdownFootnoteDefinitionSyntax footnote => footnote.Blocks,
                    MarkdownFooterSyntax footer => footer.Blocks,
                    _ => ImmutableArray<MarkdownBlockSyntax>.Empty,
                };

                if (children is not [.., var last])
                {
                    return block;
                }

                block = last;
            }
        }

        // A line of the block quotes around the node that has nothing else: a line with more markers is an empty line of a
        // block quote before the node
        private static bool HasBlankLineBefore(SyntaxNode node)
        {
            // The ancestors are counted only for a line ending, as most blocks have no line before them
            var quoteDepth = -1;
            var markers = 0;
            foreach (var trivia in node.GetFirstToken().LeadingTrivia)
            {
                switch ((SyntaxKind)trivia.RawKind)
                {
                    case SyntaxKind.BlockQuoteMarkerTrivia:
                        markers++;
                        break;

                    case SyntaxKind.EndOfLineTrivia:
                        if (quoteDepth < 0)
                        {
                            quoteDepth = node.Ancestors().Count(ancestor => ancestor is MarkdownBlockQuoteSyntax or MarkdownAlertSyntax);
                        }

                        if (markers <= quoteDepth)
                        {
                            return true;
                        }

                        markers = 0;
                        break;
                }
            }

            return false;
        }

        // The whitespace other than spaces and tabs after the marker of a container, such as \f, is in the trailing trivia of the
        // marker when it is on its line: it is the start of the first line of the blocks, or a line of its own before them
        private static List<string> WithMarkerLineText(SyntaxToken marker, List<string> lines)
        {
            var text = new StringBuilder();
            var endsLine = false;
            foreach (var trivia in marker.TrailingTrivia)
            {
                if (trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                {
                    endsLine = true;
                    break;
                }

                text.Append(trivia.ToFullString());
            }

            var content = text.ToString().TrimStart(' ', '\t');
            if (content.Length == 0)
            {
                return lines;
            }

            if (endsLine || lines.Count == 0)
            {
                lines.Insert(0, content);
            }
            else
            {
                lines[0] = content + lines[0];
            }

            return lines;
        }

        private static bool HasLineEnding(SyntaxTriviaList trivia)
        {
            foreach (var item in trivia)
            {
                if (item.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                {
                    return true;
                }
            }

            return false;
        }

        private void WriteBlock(List<string> lines, MarkdownBlockSyntax block, MarkdownBlockSyntax? previous, MarkdownBlockSyntax? next, bool blankLine, int previousStart)
        {
            switch (block)
            {
                case MarkdownParagraphSyntax or MarkdownLinkReferenceDefinitionSyntax:
                    // A paragraph of a container can have lazy lines, which would be a block on their own, such as a setext
                    // underline, and a paragraph before a table can be the lines that Markdig reads before the table in the same
                    // paragraph
                    var sourceParagraph = block is MarkdownParagraphSyntax { Parent: MarkdownDocumentSyntax } paragraph && next is not (MarkdownTableSyntax or MarkdownGridTableSyntax) ? paragraph : null;
                    WriteParagraphLines(lines, GetLines(block), GetContinuedBlock(lines, previousStart, previous, blankLine), block.Kind(), sourceParagraph: sourceParagraph);
                    break;

                case MarkdownHeadingSyntax heading:
                    WriteHeading(lines, heading, GetContinuedBlock(lines, previousStart, previous, blankLine));
                    break;

                case MarkdownThematicBreakSyntax thematicBreak when HasOnlyTokens(thematicBreak, SyntaxKind.ThematicBreakToken):
                    // --- would make the lines before it a heading, such as a paragraph or the rows of a pipe table, or start the
                    // front matter at the start of the document, and the marker of a list item and the thematic break that starts it
                    // would be one thematic break, as in "- ---"
                    var character = previous is null && thematicBreak.Parent is MarkdownListItemSyntax ? '_'
                        : options.ThematicBreakCharacter == '-' && ((!blankLine && previous is not null) || IsStartOfFrontMatter(thematicBreak)) ? '*'
                        : options.ThematicBreakCharacter;
                    lines.Add(new string(character, 3));
                    break;

                case MarkdownCodeBlockSyntax code when HasOnlyTokens(code, SyntaxKind.CodeFenceToken, SyntaxKind.InfoStringToken, SyntaxKind.CodeLineToken):
                    // The items of a list written before an indented code block may be indented less than in the text, and would contain it
                    WriteCodeBlock(lines, code, fenced: code.IsFenced || options.UseFencedCodeBlocks || previous is MarkdownListSyntax);
                    break;

                case MarkdownBlockQuoteSyntax quote:
                    AddPrefixed(lines, WithMarkerLineText(quote.MarkerToken, WriteBlocks(quote.Blocks, BlankLines.Always)), "> ", "> ");
                    break;

                case MarkdownAlertSyntax alert:
                    AddPrefixed(lines, WriteBlocks(alert.Blocks, BlankLines.Always), "> ", "> ");
                    break;

                case MarkdownFootnoteDefinitionSyntax footnote:
                    var label = string.Concat(footnote.ChildTokens().TakeWhile(token => token.RawKind != (int)SyntaxKind.ColonToken).Select(token => token.Text));
                    // Markdig reads the columns of a list that starts on the line of the label differently: the blocks other
                    // than a paragraph start on the next line
                    if (footnote.Blocks is [MarkdownParagraphSyntax, ..] or [])
                    {
                        AddPrefixed(lines, WriteBlocks(footnote.Blocks, BlankLines.Preserve), label + ": ", "    ");
                    }
                    else
                    {
                        lines.Add(label + ":");
                        AddPrefixed(lines, WriteBlocks(footnote.Blocks, BlankLines.Preserve), "    ", "    ");
                    }
                    break;

                case MarkdownCustomContainerSyntax container:
                    WriteFencedContainer(lines, container, container.Blocks, container.ClosingFenceToken, caption: null);
                    break;

                case MarkdownFigureSyntax figure:
                    WriteFencedContainer(lines, figure, GetFigureBlocks(figure), figure.ClosingFenceToken, figure.ChildNodes().OfType<MarkdownFigureCaptionSyntax>().FirstOrDefault(caption => figure.ClosingFenceToken.RawKind != 0 && caption.SpanStart > figure.ClosingFenceToken.SpanStart));
                    break;

                case MarkdownFooterSyntax footer:
                    AddPrefixed(lines, WriteBlocks(footer.Blocks, BlankLines.Preserve), "^^ ", "^^ ");
                    break;

                case MarkdownDefinitionListSyntax definitionList:
                    WriteDefinitionList(lines, definitionList);
                    break;

                case MarkdownTableSyntax table:
                    var aligned = options.AlignTables && CanAlign(table);
                    var tableLines = aligned ? GetAlignedTableLines(table) : GetTableLines(table);
                    if (!blankLine && previous is not null && tableLines.Count > 0)
                    {
                        var continuedBlock = new ContinuedBlock(string.Join('\n', lines.Skip(previousStart)), previous.Kind());
                        var blocks = ParseWithTable(continuedBlock, tableLines);
                        if (!IsTableAfter(blocks, continuedBlock.Kind, table) && !IsTableAfterWrittenBlock(ref continuedBlock, blocks, table))
                        {
                            // The first row of a pipe table that follows a paragraph is a line of the paragraph for Markdig
                            var indented = false;
                            if (previous is MarkdownParagraphSyntax && table.IsPipeTable && !IsParagraphContinuation(lines[^1].TrimStart(' '), tableLines[0]))
                            {
                                tableLines[0] = "    " + tableLines[0];
                                indented = true;
                            }

                            // The rows of the table can be lazy lines of the paragraph that ends the block before it, such as a list,
                            // or make a grid table of a paragraph that starts with +: the rows of the text, which the parser read as
                            // a table after the block, are written instead, or a blank line between them
                            if (!indented || !IsTableAfter(continuedBlock, tableLines, table))
                            {
                                var sourceLines = aligned ? GetTableLines(table) : null;
                                if (sourceLines is not null && IsTableAfter(continuedBlock, sourceLines, table))
                                {
                                    tableLines = sourceLines;
                                }
                                else
                                {
                                    // The blank line ends the paragraph of a block quote before the table, as the empty line of
                                    // the block quote written for it does: only one of them is written, as formatting the text
                                    // again writes it (fuzz regression)
                                    if (previous is MarkdownBlockQuoteSyntax or MarkdownAlertSyntax && lines.Count > previousStart && lines[^1] == ">")
                                    {
                                        lines.RemoveAt(lines.Count - 1);
                                    }

                                    AddBlankLine(lines);
                                }
                            }
                        }
                    }

                    lines.AddRange(tableLines);
                    break;

                // A math block that the end of its container closes would contain the blank line after it
                case MarkdownMathBlockSyntax math when math.ClosingFenceToken.RawKind == 0:
                    lines.AddRange(GetLines(block));
                    lines.Add(math.OpeningFenceToken.Text);
                    break;

                default:
                    lines.AddRange(GetLines(block));
                    break;
            }
        }

        private bool IsStartOfFrontMatter(MarkdownBlockSyntax block)
        {
            return (parseOptions.Extensions & MarkdownExtensions.YamlFrontMatter) != MarkdownExtensions.None && block.Parent is MarkdownDocumentSyntax document && document.Blocks[0] == block;
        }

        private static bool HasOnlyTokens(SyntaxNode node, params ReadOnlySpan<SyntaxKind> kinds)
        {
            foreach (var child in node.ChildNodesAndTokens())
            {
                if (!child.IsToken || !kinds.Contains((SyntaxKind)child.RawKind))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddPrefixed(List<string> lines, List<string> content, string firstPrefix, string prefix)
        {
            if (content.Count == 0)
            {
                lines.Add(firstPrefix.TrimEnd());
                return;
            }

            for (var i = 0; i < content.Count; i++)
            {
                var linePrefix = i == 0 ? firstPrefix : prefix;
                lines.Add(content[i].Length == 0 ? linePrefix.TrimEnd() : linePrefix + content[i]);
            }
        }

        // The lines after the first one of a paragraph are written without their indentation, unless they would start a block
        // without it: 4 spaces make them lines of the paragraph, as an indented code block cannot interrupt a paragraph.
        /// <param name="lines">The lines of the document, which the lines of the paragraph are added to.</param>
        /// <param name="paragraph">The lines of the paragraph.</param>
        /// <param name="continuedBlock">The block whose paragraph the lines continue, or <see langword="null"/>.</param>
        /// <param name="kind">The kind of the block that the lines are.</param>
        /// <param name="underline">The underline of a setext heading, or <see langword="null"/>.</param>
        /// <param name="sourceParagraph">
        /// The paragraph of the document that the lines are, when they are a single paragraph if they are its text: lines that were
        /// a paragraph of the document, not of a container, are one on their own, unless a table follows them.
        /// </param>
        private void WriteParagraphLines(List<string> lines, List<string> paragraph, ContinuedBlock? continuedBlock = null, SyntaxKind kind = SyntaxKind.Paragraph, string? underline = null, MarkdownParagraphSyntax? sourceParagraph = null)
        {
            // The spaces that start a line of a paragraph are not part of it, even the ones that a token has
            for (var i = 0; i < paragraph.Count; i++)
            {
                paragraph[i] = paragraph[i].TrimStart(' ', '\t');
            }

            // Most paragraphs are a paragraph without the indentation of their lines, after the block whose paragraph they continue.
            // The lines are joined only to parse them.
            if (continuedBlock is null && (paragraph.Count < 2 || IsSourceText(sourceParagraph, paragraph)))
            {
                lines.AddRange(paragraph);
                return;
            }

            var text = string.Join('\n', paragraph);
            if (continuedBlock is null ? IsSingleParagraph(text) : IsContinuation(continuedBlock, text, kind, underline))
            {
                lines.AddRange(paragraph);
                return;
            }

            var previous = continuedBlock?.Text[(continuedBlock.Text.AsSpan().LastIndexOf('\n') + 1)..].TrimStart(' ');
            foreach (var line in paragraph)
            {
                var written = previous is not null && !IsParagraphContinuation(previous, line) ? "    " + line : line;
                lines.Add(written);
                previous = written;
            }
        }

        private bool IsParagraphContinuation(string previous, string line)
        {
            // A line that starts with a letter is text, unless the letters are the number of a list item of ListExtras, such as "a."
            var letters = line.AsSpan().IndexOfAnyExceptInRange('A', 'z');
            if (line.Length == 0 || line[0] >= 0x80 || (char.IsAsciiLetter(line[0]) && (letters < 0 || line[letters] is not ('.' or ')'))))
            {
                return true;
            }

            // A line can interrupt the paragraph, or make it a heading, or the line before it the header row of a table: the
            // line before it is written after another one, as it is a line of the paragraph too
            return IsSingleParagraph("a\n" + line) && IsSingleParagraph("a\n" + previous + "\n" + line);
        }

        private bool IsSingleParagraph(string text) => IsSingleParagraph(text, parseOptions);

        // Whether the lines of a paragraph are its text in the document, without indentation or the markers of a container between
        // them: the parser read them as a single paragraph already
        private bool IsSourceText(MarkdownParagraphSyntax? paragraph, List<string> lines)
        {
            if (paragraph is null)
            {
                return false;
            }

            var text = source.AsSpan(paragraph.Span.Start, paragraph.Span.Length);
            for (var i = 0; i < lines.Count; i++)
            {
                // The lines end with the line endings of the document: \r\n, \n or \r
                if (i > 0)
                {
                    if (text is ['\r', '\n', ..])
                    {
                        text = text[2..];
                    }
                    else if (text is ['\n' or '\r', ..])
                    {
                        text = text[1..];
                    }
                    else
                    {
                        return false;
                    }
                }

                if (!text.StartsWith(lines[i], StringComparison.Ordinal))
                {
                    return false;
                }

                text = text[lines[i].Length..];
            }

            return text.IsEmpty;
        }

        private bool IsContinuation(ContinuedBlock continuedBlock, string text, SyntaxKind kind, string? underline)
        {
            var blocks = MarkdownSyntaxTree.ParseText(continuedBlock.Text + "\n" + text + (underline is null ? "" : "\n" + underline), parseOptions).GetRoot().Blocks;
            return blocks.Length >= 2 && blocks[^2].Kind() == continuedBlock.Kind && blocks[^1].Kind() == kind;
        }

        private bool IsTableAfter(ContinuedBlock continuedBlock, List<string> tableLines, MarkdownTableSyntax table) => IsTableAfter(ParseWithTable(continuedBlock, tableLines), continuedBlock.Kind, table);

        private static bool IsTableAfter(ImmutableArray<MarkdownBlockSyntax> blocks, SyntaxKind kind, MarkdownTableSyntax table) => blocks.Length >= 2 && blocks[^2].Kind() == kind && blocks[^1].Kind() == table.Kind();

        private ImmutableArray<MarkdownBlockSyntax> ParseWithTable(ContinuedBlock continuedBlock, List<string> tableLines) => MarkdownSyntaxTree.ParseText(continuedBlock.Text + "\n" + string.Join('\n', tableLines), parseOptions).GetRoot().Blocks;

        // The block before the table can be written as another kind, such as a setext heading as an ATX heading, or an indented
        // code block as a fenced one: the blocks of the block and the table are checked with the kind of the block that the lines
        // are. A paragraph is written as one.
        private bool IsTableAfterWrittenBlock(ref ContinuedBlock continuedBlock, ImmutableArray<MarkdownBlockSyntax> blocks, MarkdownTableSyntax table)
        {
            if (continuedBlock.Kind == SyntaxKind.Paragraph || MarkdownSyntaxTree.ParseText(continuedBlock.Text, parseOptions).GetRoot().Blocks is not [.., var written] || written.Kind() == continuedBlock.Kind)
            {
                return false;
            }

            continuedBlock = continuedBlock with { Kind = written.Kind() };
            return IsTableAfter(blocks, continuedBlock.Kind, table);
        }

        // The blocks tell whether the text is a paragraph, without parsing its inlines and building its tree, except for the pipe
        // tables of Markdig
        private static bool IsSingleParagraph(string text, MarkdownParseOptions options) => IsText(text, options) || (Parsing.BlockParser.IsSingleParagraph(text, options) ?? MarkdownSyntaxTree.ParseText(text, options).GetRoot().Blocks is [MarkdownParagraphSyntax]);

        // Text whose lines all start with a letter is a paragraph, without parsing it: a block starts with punctuation, a digit
        // or whitespace, or with letters followed by . or ) for the list items of ListExtras. A table starts on a line with a pipe.
        private static bool IsText(string text, MarkdownParseOptions options)
        {
            if (text.Contains('\r', StringComparison.Ordinal)
                || ((options.Extensions & (MarkdownExtensions.Tables | MarkdownExtensions.PipeTables | MarkdownExtensions.GridTables)) != MarkdownExtensions.None && text.Contains('|', StringComparison.Ordinal)))
            {
                return false;
            }

            foreach (var range in text.AsSpan().Split('\n'))
            {
                var line = text.AsSpan(range);
                if (line.IsEmpty)
                {
                    return false;
                }

                if (char.IsAsciiLetter(line[0]))
                {
                    var letters = line.IndexOfAnyExceptInRange('A', 'z');
                    if (letters >= 0 && line[letters] is '.' or ')')
                    {
                        return false;
                    }
                }
                else if (line[0] < 0x80 || !char.IsLetter(line[0]))
                {
                    return false;
                }
            }

            return true;
        }

        private MarkdownParseOptions WithoutTables => field ??= parseOptions with { Extensions = parseOptions.Extensions & ~(MarkdownExtensions.Tables | MarkdownExtensions.PipeTables) };

        // The options that read the blocks only a few levels deep, enough for the kind of the first block of a list item: it is in
        // the item and the list, and an alert is a block quote whose first paragraph starts with its kind
        private MarkdownParseOptions ShallowOptions => field ??= parseOptions with { MaximumNestingDepth = Math.Min(parseOptions.MaximumNestingDepth, 8) };

        // The lines of the link reference definitions or of the pipe table whose paragraph the block continues
        private static ContinuedBlock? GetContinuedBlock(List<string> lines, int previousStart, MarkdownBlockSyntax? previous, bool blankLine)
        {
            return blankLine || previous is not (MarkdownLinkReferenceDefinitionSyntax or MarkdownTableSyntax { IsPipeTable: true })
                ? null
                : new ContinuedBlock(string.Join('\n', lines.Skip(previousStart)), previous.Kind());
        }

        private static List<string> GetHeadingLines(MarkdownHeadingSyntax heading) => GetLines(heading, SyntaxKind.AtxHeadingMarkerToken, SyntaxKind.AtxHeadingClosingSequenceToken, SyntaxKind.SetextHeadingUnderlineToken);

        // Markdig reads the attributes of a setext heading in its text, and the ones of an ATX heading at its end, and keeps the
        // whitespace at the end of the text of a setext heading
        private bool StaysSetext(MarkdownHeadingSyntax heading, List<string> content)
        {
            return heading.IsSetext && (content.Count != 1 || !options.UseAtxHeadings || content[0] is [.., ' ' or '\t'] || HasGenericAttributes(heading));
        }

        private void WriteHeading(List<string> lines, MarkdownHeadingSyntax heading, ContinuedBlock? continuedBlock)
        {
            var content = GetHeadingLines(heading);
            if (StaysSetext(heading, content))
            {
                var underline = heading.Level == 1 ? "===" : "---";
                WriteParagraphLines(lines, content, continuedBlock, SyntaxKind.SetextHeading, underline);
                lines.Add(underline);
                return;
            }

            // Markdig reads the attributes at the end of an ATX heading, before its closing sequence: the heading is kept
            if (!heading.IsSetext && HasGenericAttributes(heading))
            {
                lines.AddRange(GetLines(heading));
                return;
            }

            var text = content.Count == 0 ? "" : content[0].Trim(' ', '\t');

            // A heading that ends with # needs a closing sequence, which would be the end of the text otherwise
            if (text.EndsWith('#', StringComparison.Ordinal))
            {
                text += " #";
            }

            lines.Add(text.Length == 0 ? new string('#', heading.Level) : new string('#', heading.Level) + " " + text);
        }

        private void WriteCodeBlock(List<string> lines, MarkdownCodeBlockSyntax code, bool fenced)
        {
            // The lines are added where they go, and the opening fence is inserted before them once their content gives its length
            var start = lines.Count;
            code.AddCodeLines(lines);
            var count = lines.Count - start;
            if (!fenced)
            {
                for (var i = start; i < lines.Count; i++)
                {
                    if (lines[i].Length != 0)
                    {
                        lines[i] = "    " + lines[i];
                    }
                }

                return;
            }

            var info = code.IsFenced ? code.InfoToken.Text : "";
            var character = options.CodeFenceCharacter == '`' && info.Contains('`', StringComparison.Ordinal) ? '~' : options.CodeFenceCharacter;

            // A line of the code that starts with as many fence characters as the fence would close it
            var length = 3;
            for (var i = start; i < start + count; i++)
            {
                var line = lines[i].AsSpan().TrimStart(" \t");
                var run = line.Length - line.TrimStart(character).Length;
                length = Math.Max(length, run + 1);
            }

            var fence = new string(character, length);
            lines.Insert(start, info.Length == 0 ? fence : info[0] == character ? fence + " " + info : fence + info);
            lines.Add(fence);
        }

        // The spaces that start the first line of the block after a list, such as the ones of an HTML block, which are part of it:
        // the content of the items must be indented more, or the line would be in the last item
        private int GetIndentationOfNextBlock(ImmutableArray<MarkdownBlockSyntax> blocks, int index)
        {
            if (index + 1 >= blocks.Length || blocks[index + 1] is MarkdownListSyntax)
            {
                return 0;
            }

            // The first line of a container is its marker or its opening fence: the blocks it contains are not written, as writing
            // them for each container would write the containers nested in each other an exponential number of times (fuzz
            // regression: a list followed by a custom container that is not closed, which contains the next list, and so on)
            List<string> next;
            switch (blocks[index + 1])
            {
                case MarkdownBlockQuoteSyntax or MarkdownAlertSyntax or MarkdownFootnoteDefinitionSyntax or MarkdownFooterSyntax:
                    return 0;

                case MarkdownCustomContainerSyntax container:
                    next = GetFencedContainerOpening(container, container.Blocks, container.ClosingFenceToken);
                    break;

                case MarkdownFigureSyntax figure:
                    next = GetFencedContainerOpening(figure, GetFigureBlocks(figure), figure.ClosingFenceToken);
                    break;

                default:
                    next = [];
                    WriteBlock(next, blocks[index + 1], blocks[index], index + 2 < blocks.Length ? blocks[index + 2] : null, blankLine: true, previousStart: 0);
                    break;
            }

            return next.Count == 0 ? 0 : next[0].Length - next[0].TrimStart(' ').Length;
        }

        private char? WriteList(List<string> lines, MarkdownListSyntax list, char? previousBulletMarker, int indentationOfNextBlock, string? context)
        {
            var items = list.Items;
            var contents = new List<string>[items.Length];
            var firstBlockLineCounts = new int[items.Length];
            var taskMarkers = new string[items.Length];
            for (var i = 0; i < items.Length; i++)
            {
                contents[i] = WriteBlocks(items[i].Blocks, list.IsTight ? BlankLines.Never : BlankLines.Always, out firstBlockLineCounts[i]);
                taskMarkers[i] = items[i].IsTask ? items[i].TaskListItemMarkerToken.Text + " " : "";
            }

            char? bulletMarker = null;
            var keepSpacing = false;
            if (!list.IsOrdered)
            {
                bulletMarker = ChooseBulletMarker(options.BulletListMarker ?? list.MarkerCharacter, previousBulletMarker, items, contents, firstBlockLineCounts, taskMarkers, context);
                if (bulletMarker is null)
                {
                    // No marker is valid with one space after it, as a tab after the marker is not a space for Markdig
                    bulletMarker = list.MarkerCharacter;
                    keepSpacing = true;
                }
            }

            var renumber = options.RenumberOrderedLists && list.OrderedStyle is MarkdownOrderedListStyle.Numbers && list.StartNumber is { } start && (long)start + items.Length - 1 <= 999_999_999;
            for (var i = 0; i < items.Length; i++)
            {
                // The end of a list item closes an HTML block that ends on a marker, such as ?>, which would contain the blank line,
                // and Markdig counts the blank line after a definition as one of the definition
                if (i > 0 && !list.IsTight && (GetLastBlock(items[i - 1]) is not (MarkdownHtmlBlockSyntax or MarkdownDefinitionListSyntax) || HasBlankLineBefore(items[i])))
                {
                    lines.Add("");
                }

                var marker = bulletMarker is { } bullet ? bullet.ToString()
                    : renumber ? (list.StartNumber!.Value + i).ToString(System.Globalization.CultureInfo.InvariantCulture) + list.MarkerCharacter
                    : items[i].MarkerToken.Text;

                var width = Math.Max(marker.Length + 1, indentationOfNextBlock + 1);
                var content = contents[i];
                if (keepSpacing && GetSpacingAfterMarker(items[i]) is ({ } spacing, var contentWidth))
                {
                    AddPrefixed(lines, content, marker + spacing + taskMarkers[i], new string(' ', Math.Max(contentWidth, width)));
                    continue;
                }

                if (StartsOnNextLine(items[i], content, taskMarkers[i]))
                {
                    lines.Add(marker);
                    AddPrefixed(lines, content, new string(' ', width), new string(' ', width));
                    continue;
                }

                AddPrefixed(lines, content, marker.PadRight(width) + taskMarkers[i], new string(' ', width));

                // The content of an empty item starts after its marker and one space, whatever the width of the other items: the
                // next block would be its content when it is indented as much. A blank line ends an empty item.
                if (i == items.Length - 1 && content.Count == 0 && taskMarkers[i].Length == 0 && indentationOfNextBlock >= marker.Length + 1)
                {
                    AddBlankLine(lines);
                }
            }

            return bulletMarker;
        }

        // The spaces that start the content, such as the ones of an HTML block, would be read as the spaces after the marker (an
        // indented code block is after the marker and one space), a text that starts with [ ] would be a task, and Markdig reads
        // the columns of a definition list differently on the line of the marker: the content starts on the next line
        private bool StartsOnNextLine(MarkdownListItemSyntax item, List<string> content, string taskMarker)
        {
            if (taskMarker.Length > 0 || content.Count == 0)
            {
                return false;
            }

            var firstLine = content[0];
            if (firstLine is [' ', ..] && item.Blocks[0] is not MarkdownCodeBlockSyntax)
            {
                return true;
            }

            if (item.Blocks[0] is MarkdownDefinitionListSyntax definitions && HasLineEnding(definitions.GetFirstToken().LeadingTrivia))
            {
                return true;
            }

            return (parseOptions.Extensions & MarkdownExtensions.TaskLists) != MarkdownExtensions.None && firstLine is ['[', _, ']', ..]
                && MarkdownSyntaxTree.ParseText("- " + firstLine, parseOptions).GetRoot().Blocks is [MarkdownListSyntax { Items: [{ IsTask: true }, ..] }, ..];
        }

        // The marker must not be the one of the list before, or the lists would be one, and each item must stay a list item
        // that starts with the same block: "- ---" is a thematic break, "- -|" is a list in a list instead of a table, and
        // "+ ---" is a paragraph when Markdig reads grid tables. It is null when no marker is valid.
        // The formatter writes the code blocks and the headings in another style
        private static SyntaxKind GetFirstBlockKind(MarkdownListItemSyntax item) => item.Blocks.Length == 0 ? SyntaxKind.None : item.Blocks[0].Kind() switch
        {
            SyntaxKind.IndentedCodeBlock => SyntaxKind.FencedCodeBlock,
            SyntaxKind.SetextHeading => SyntaxKind.AtxHeading,
            var kind => kind,
        };

        private char? ChooseBulletMarker(char preferred, char? previousBulletMarker, ImmutableArray<MarkdownListItemSyntax> items, List<string>[] contents, int[] firstBlockLineCounts, string[] taskMarkers, string? context)
        {
            foreach (var candidate in (ReadOnlySpan<char>)[preferred, '-', '*', '+'])
            {
                // A line that starts with + can be a row of a grid table
                if (candidate == previousBulletMarker || (candidate == '+' && candidate != items[0].MarkerToken.Text[0] && (parseOptions.Extensions & MarkdownExtensions.GridTables) != MarkdownExtensions.None))
                {
                    continue;
                }

                var valid = true;
                for (var i = 0; i < contents.Length && valid; i++)
                {
                    var firstLine = contents[i].Count == 0 ? "" : contents[i][0];
                    var itemContext = i == 0 ? context : null;
                    if (itemContext is null && firstLine.Length > 0 && char.IsAsciiLetter(firstLine[0]))
                    {
                        continue;
                    }

                    // The item is parsed with the lines of its first block and the line after them, as they can change what the first
                    // line is, such as a line without pipe after a row of a pipe table without delimiter row. The marker changes only
                    // the first line, so the other blocks are read as they were.
                    var item = new StringBuilder();
                    if (itemContext is not null)
                    {
                        item.Append(itemContext).Append('\n');
                    }

                    item.Append(candidate).Append(' ').Append(taskMarkers[i]).Append(firstLine);
                    var lineCount = Math.Min(contents[i].Count, firstBlockLineCounts[i] + 1);
                    for (var j = 1; j < lineCount; j++)
                    {
                        item.Append("\n  ").Append(contents[i][j]);
                    }

                    // The marker of a list or of a block quote that starts the item makes it what it is, whatever the blocks it
                    // contains: they are read as text of a paragraph past a few levels, so that the items of nested lists do not
                    // parse their whole content again at each level
                    var kind = GetFirstBlockKind(items[i]);
                    var shallow = itemContext is null && items[i].Blocks is [MarkdownListSyntax or MarkdownBlockQuoteSyntax, ..];
                    var key = (item.ToString(), kind, shallow);
                    if (!_bulletMarkerChecks.TryGetValue(key, out valid))
                    {
                        valid = MarkdownSyntaxTree.ParseText(key.Item1, shallow ? ShallowOptions : parseOptions).GetRoot().Blocks is [.., MarkdownListSyntax { Items: [var parsedItem, ..] }]
                            && GetFirstBlockKind(parsedItem) == kind;
                        _bulletMarkerChecks.Add(key, valid);
                    }
                }

                if (valid)
                {
                    return candidate;
                }
            }

            return null;
        }

        // The whitespace after the marker of an item, and the column of its content from the marker
        private (string? Spacing, int Width) GetSpacingAfterMarker(MarkdownListItemSyntax item)
        {
            var marker = item.MarkerToken;
            if (marker.TrailingTrivia is not [{ RawKind: (int)SyntaxKind.WhitespaceTrivia } spacing])
            {
                return (null, 0);
            }

            var lineStart = source.AsSpan(0, marker.SpanStart).LastIndexOfAny('\r', '\n') + 1;
            return (spacing.ToString(), GetColumn(lineStart, marker.FullSpan.End) - GetColumn(lineStart, marker.SpanStart));
        }

        private int GetColumn(int lineStart, int position)
        {
            var column = 0;
            for (var i = lineStart; i < position; i++)
            {
                column = source[i] == '\t' ? column + 4 - (column % 4) : column + 1;
            }

            return column;
        }

        // The blocks of a figure, without its caption
        private static ImmutableArray<MarkdownBlockSyntax> GetFigureBlocks(MarkdownFigureSyntax figure) => [.. figure.Blocks.Where(block => block is not MarkdownFigureCaptionSyntax)];

        // The lines of the opening fence, with the arguments of a custom container or the caption of a figure
        private static List<string> GetFencedContainerOpening(MarkdownBlockSyntax container, ImmutableArray<MarkdownBlockSyntax> blocks, SyntaxToken closingFence)
        {
            var end = Math.Min(blocks.Length > 0 ? blocks[0].SpanStart : int.MaxValue, closingFence.RawKind != 0 ? closingFence.SpanStart : int.MaxValue);
            return GetLines(container.DescendantTokens().Where(token => token.SpanStart < end));
        }

        private void WriteFencedContainer(List<string> lines, MarkdownBlockSyntax container, ImmutableArray<MarkdownBlockSyntax> blocks, SyntaxToken closingFence, MarkdownFigureCaptionSyntax? caption)
        {
            lines.AddRange(GetFencedContainerOpening(container, blocks, closingFence));
            lines.AddRange(WriteBlocks(blocks, BlankLines.Preserve));
            if (closingFence.RawKind != 0)
            {
                var closing = closingFence.Text;
                if (caption is not null && GetLines(caption) is [var text, ..])
                {
                    closing += " " + text;
                }

                lines.Add(closing);
            }
        }

        private void WriteDefinitionList(List<string> lines, MarkdownDefinitionListSyntax definitionList)
        {
            var items = definitionList.Items;
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (i > 0 && HasBlankLineBefore(item))
                {
                    AddBlankLine(lines);
                }

                // The terms are the lines of a paragraph
                WriteParagraphLines(lines, [.. item.Terms.SelectMany(term => GetLines(term))]);

                if (item.MarkerToken.RawKind != 0)
                {
                    if (item.Terms.Length > 0 && HasLineEnding(item.MarkerToken.LeadingTrivia))
                    {
                        AddBlankLine(lines);
                    }

                    // A definition without content is its marker and the spaces after it, which make the marker one
                    if (item.Blocks.Length == 0)
                    {
                        lines.Add(item.MarkerToken.Text + "   ");
                    }
                    else
                    {
                        AddPrefixed(lines, WriteBlocks(item.Blocks, BlankLines.Preserve), item.MarkerToken.Text + "   ", "    ");
                    }

                    // Markdig renders the paragraph of a definition that has a blank line, even one at its end, in <p>: it is
                    // the only blank line a definition of one paragraph can have
                    if (item.BlankLineCount > 0 && item.Blocks is [] or [MarkdownParagraphSyntax])
                    {
                        lines.Add("");
                    }
                }
            }
        }

        private static List<string> GetAlignedTableLines(MarkdownTableSyntax table)
        {
            var lines = new List<string>();
            WriteTable(lines, table);
            return lines;
        }

        // The rows of the table as they are in the text
        private List<string> GetTableLines(MarkdownTableSyntax table)
        {
            var lines = GetLines(table);

            // A row of whitespace other than spaces, such as \v, has no token when it ends the document
            if (table.ChildNodes().LastOrDefault() is MarkdownTableRowSyntax { FullSpan.Length: 0 })
            {
                lines.Add("| |");
            }

            // The rows of a pipe table are lines of a paragraph: a row that would start a block is indented
            for (var i = 1; table.IsPipeTable && i < lines.Count; i++)
            {
                if (!IsSingleParagraph("a\n" + lines[i], WithoutTables))
                {
                    lines[i] = "    " + lines[i];
                }
            }

            return lines;
        }

        private bool CanAlign(MarkdownTableSyntax table)
        {
            if (table.HeaderRow is null || table.DelimiterRow is null || (table.IsPipeTable && parseOptions.PipeTableOptions.InferColumnWidthsFromSeparator))
            {
                return false;
            }

            // Markdig gives the attributes of a cell to the cell or to the table depending on the pipes around them, and a pipe
            // that is text, such as one in the text of a link that spans rows, is not in the cell it would be in another layout
            if (HasGenericAttributes(table) || HasToken(table.Green, token => token.RawKind == (int)SyntaxKind.TextToken && token.Text.Contains('|', StringComparison.Ordinal)))
            {
                return false;
            }

            // A cell whose text is on several lines, such as a link of Markdig that spans rows, is kept as it is, and so is a
            // cell next to a pipe when what is next to its text changes what it is
            // An inline extension reads the character before its trigger character too
            var contextSensitive = (parseOptions.Extensions & ContextSensitiveInlines) != MarkdownExtensions.None || parseOptions.SyntaxExtensions.Length > 0;
            foreach (var row in table.ChildNodes().OfType<MarkdownTableRowSyntax>())
            {
                foreach (var cell in row.Cells)
                {
                    if (HasToken(cell.Green, token => token.Text.AsSpan().ContainsAny('\r', '\n')))
                    {
                        return false;
                    }

                    // The whitespace other than spaces and tabs, such as \v, is text that the formatter would move
                    var text = GetLines(cell) is [var line] ? line.Trim(' ', '\t') : "";
                    if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
                    {
                        return false;
                    }

                    // The character before the first cell of a row can be the marker of a container, which the formatter writes
                    // with a space after it
                    var span = cell.Span;
                    if (contextSensitive && span.Length > 0 && ((span.Start > 0 && !IsWhitespace(source[span.Start - 1]) && (source[span.Start - 1] == '|' || cell != row.Cells[0])) || (span.End < source.Length && !IsWhitespace(source[span.End]))))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n';

        // Whether the node has generic attributes below it, which the green node records when it is made: the tree is not walked
        private static bool HasGenericAttributes(MarkdownSyntaxNode node) => node.Green is MarkdownNode { RareKinds: var kinds } && (kinds & MarkdownRareKinds.GenericAttributes) != MarkdownRareKinds.None;

        // Whether a token below the node matches. The tokens are read from the green tree, which creates no red node or token. The
        // depth of the tree is bounded by the maximum nesting depth of the parser.
        private static bool HasToken(GreenNode node, Func<GreenToken, bool> predicate)
        {
            if (node is GreenToken token)
            {
                return predicate(token);
            }

            for (var i = 0; i < node.SlotCount; i++)
            {
                if (node.GetSlot(i) is { } child && HasToken(child, predicate))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteTable(List<string> lines, MarkdownTableSyntax table)
        {
            var rows = new List<string[]>();
            var delimiterIndex = -1;
            foreach (var row in table.ChildNodes().OfType<MarkdownTableRowSyntax>())
            {
                if (row.IsDelimiter)
                {
                    delimiterIndex = rows.Count;
                    rows.Add(new string[table.Alignments.Length]);
                    continue;
                }

                rows.Add([.. row.Cells.Select(cell => GetLines(cell) is [var text] ? text.Trim(' ', '\t') : "")]);
            }

            var widths = new List<int>();
            foreach (var cells in rows)
            {
                for (var i = 0; i < cells.Length; i++)
                {
                    if (i == widths.Count)
                    {
                        widths.Add(3);
                    }

                    widths[i] = Math.Max(widths[i], cells[i] is null ? 0 : GetDisplayWidth(cells[i]));
                }
            }

            var alignments = table.Alignments;
            var delimiterCells = rows[delimiterIndex];
            for (var i = 0; i < delimiterCells.Length; i++)
            {
                var width = widths[i];
                delimiterCells[i] = (i < alignments.Length ? alignments[i] : MarkdownTableAlignment.None) switch
                {
                    MarkdownTableAlignment.Left => ":" + new string('-', width - 1),
                    MarkdownTableAlignment.Right => new string('-', width - 1) + ":",
                    MarkdownTableAlignment.Center => ":" + new string('-', width - 2) + ":",
                    _ => new string('-', width),
                };
            }

            var builder = new StringBuilder();
            foreach (var cells in rows)
            {
                builder.Clear();
                builder.Append('|');
                for (var i = 0; i < cells.Length; i++)
                {
                    builder.Append(' ').Append(cells[i]).Append(' ', widths[i] - GetDisplayWidth(cells[i])).Append(" |");
                }

                lines.Add(builder.ToString());
            }
        }

        // The number of columns of a monospace editor that the text uses, so that the pipes of the rows of a table are aligned
        private static int GetDisplayWidth(string text)
        {
            var width = 0;
            var remaining = text.AsSpan();
            while (!remaining.IsEmpty)
            {
                if (char.IsAscii(remaining[0]) && (remaining.Length == 1 || char.IsAscii(remaining[1])))
                {
                    width++;
                    remaining = remaining[1..];
                    continue;
                }

                var length = Unicode.GetGraphemeClusterLength(remaining);
                width += GetGraphemeClusterWidth(remaining[..length]);
                remaining = remaining[length..];
            }

            return width;
        }

        // A wide or fullwidth character, such as 漢 or 😀, uses 2 columns, and so does an emoji written with U+FE0F or a flag. A
        // combining mark, or a character that is not displayed, such as a zero-width space, uses none.
        private static int GetGraphemeClusterWidth(ReadOnlySpan<char> cluster)
        {
            var codePoint = char.IsSurrogatePair(cluster[0], cluster.Length > 1 ? cluster[1] : '\0') ? char.ConvertToUtf32(cluster[0], cluster[1]) : cluster[0];
            if (UnicodeProperties.IsDefaultIgnorable(codePoint) || UnicodeProperties.GetGeneralCategory(codePoint) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.EnclosingMark)
            {
                return 0;
            }

            if (UnicodeProperties.GetEastAsianWidth(codePoint) is UnicodeEastAsianWidth.Wide or UnicodeEastAsianWidth.Fullwidth
                || UnicodeProperties.GetGraphemeClusterBreak(codePoint) == UnicodeGraphemeClusterBreak.RegionalIndicator
                || (cluster.Contains('\uFE0F') && UnicodeEmoji.IsEmoji(codePoint)))
            {
                return 2;
            }

            return 1;
        }

        // The lines of the tokens, without the markers of their containers and the indentation, which the writer adds again
        private static List<string> GetLines(IEnumerable<SyntaxToken> tokens)
        {
            var builder = LineBuilder.Rent();
            foreach (var token in tokens)
            {
                builder.AddToken((GreenToken)token.Node!);
            }

            return builder.CompleteAndReturn();
        }

        /// <summary>
        /// Gets the lines of the tokens of a node, without the tokens of the kinds to skip. The tokens are read from the green tree,
        /// which creates no red node or token.
        /// </summary>
        private static List<string> GetLines(SyntaxNode node, SyntaxKind skippedKind = SyntaxKind.None, SyntaxKind otherSkippedKind = SyntaxKind.None, SyntaxKind lastSkippedKind = SyntaxKind.None)
        {
            var builder = LineBuilder.Rent();
            var stack = builder.Stack;
            stack.Push((node.Green, 0));
            while (stack.TryPop(out var item))
            {
                var (green, slot) = item;
                if (slot >= green.SlotCount)
                {
                    continue;
                }

                // The rest of the slots of the node are read after the current one, which can be a node or a list
                stack.Push((green, slot + 1));
                switch (green.GetSlot(slot))
                {
                    case GreenToken token:
                        if (skippedKind == SyntaxKind.None || (SyntaxKind)token.RawKind is var kind && kind != skippedKind && kind != otherSkippedKind && kind != lastSkippedKind)
                        {
                            builder.AddToken(token);
                        }

                        break;

                    case { } child:
                        stack.Push((child, 0));
                        break;
                }
            }

            return builder.CompleteAndReturn();
        }

        // The lines of tokens: their text and the trivia that is not the indentation or the markers of a container. The formatter gets
        // the lines of each block: a builder is reused, with its buffers, from one block to the next on a thread.
        private sealed class LineBuilder
        {
            // A buffer that a long line made larger is not kept
            private const int MaximumCachedLineCapacity = 4096;

            [ThreadStatic]
            private static LineBuilder? s_cached;

            private List<string> _lines = [];
            private readonly StringBuilder _line = new();
            private bool _atLineStart = true;

            // The nodes being walked, with the next slot of each to read
            public Stack<(GreenNode Node, int Slot)> Stack { get; } = new();

            // A caller takes the builder of the thread, so that another caller, if any, makes its own
            public static LineBuilder Rent()
            {
                var builder = s_cached ?? new LineBuilder();
                s_cached = null;
                return builder;
            }

            // Returns the lines, and gives the builder back to the thread: the caller does not use it after
            public List<string> CompleteAndReturn()
            {
                if (!_atLineStart)
                {
                    EndLine();
                }

                var lines = _lines;
                _lines = [];
                _line.Clear();
                _atLineStart = true;
                Stack.Clear();
                if (_line.Capacity <= MaximumCachedLineCapacity)
                {
                    s_cached = this;
                }

                return lines;
            }

            public void AddToken(GreenToken token)
            {
                AddTrivia(token.LeadingTrivia);
                if (token.RawKind is (int)SyntaxKind.CodeLineToken or (int)SyntaxKind.MathLineToken && token.GetValue() is int virtualSpaces)
                {
                    _line.Append(' ', virtualSpaces);
                }

                var text = token.Text.AsSpan();
                while (text.Length > 0)
                {
                    var index = text.IndexOfAny('\r', '\n');
                    if (index < 0)
                    {
                        _line.Append(text);
                        _atLineStart = false;
                        break;
                    }

                    _line.Append(text[..index]);
                    EndLine();
                    var length = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                    text = text[(index + length)..];
                }

                AddTrivia(token.TrailingTrivia);
            }

            private void EndLine()
            {
                _lines.Add(_line.ToString());
                _line.Clear();
                _atLineStart = true;
            }

            // A list of trivia, a single trivia, or none
            private void AddTrivia(GreenNode? trivia)
            {
                if (trivia is null)
                {
                    return;
                }

                if (!trivia.IsList)
                {
                    AddTrivia((GreenTrivia)trivia);
                    return;
                }

                for (var i = 0; i < trivia.SlotCount; i++)
                {
                    AddTrivia((GreenTrivia)trivia.GetSlot(i)!);
                }
            }

            private void AddTrivia(GreenTrivia trivia)
            {
                switch ((SyntaxKind)trivia.RawKind)
                {
                    case SyntaxKind.EndOfLineTrivia:
                        if (!_atLineStart)
                        {
                            EndLine();
                        }

                        break;

                    // The indentation is spaces and tabs: other whitespace, such as \v, is text, without the spaces and the tabs before it,
                    // which are indentation, or the space after the marker of a block quote
                    case SyntaxKind.BlockQuoteMarkerTrivia or SyntaxKind.ListItemIndentTrivia or SyntaxKind.FooterMarkerTrivia when _atLineStart:
                    case SyntaxKind.WhitespaceTrivia when _atLineStart && !trivia.Text.AsSpan().ContainsAnyExcept(' ', '\t'):
                        break;

                    case SyntaxKind.WhitespaceTrivia when _atLineStart:
                        _line.Append(trivia.Text.AsSpan().TrimStart(" \t"));
                        _atLineStart = false;
                        break;

                    default:
                        _line.Append(trivia.Text);
                        _atLineStart = false;
                        break;
                }
            }
        }
    }
}
