using System.Buffers;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// Parses the blocks of a document line by line, as the CommonMark reference implementation (commonmark.js) does. The
/// marker that opens a container (a <c>&gt;</c>, a list marker) is a token of the container; the markers and the indentation
/// that continue it on the next lines are trivia, whose kind is recorded here. A leaf block keeps the ranges of its lines.
/// </summary>
internal sealed class BlockParser
{
    private const int CodeIndent = 4;

    // The kind of the block of a fenced block extension of raw lines while the blocks are read: a value that SyntaxKind does not
    // define, above the kinds of the nodes, so that it is not taken for a token. Its own kind lets the checks of the kinds that
    // run for each line, such as AcceptsLines, cover it without another comparison. Convert writes an ExtensionBlock.
    private const SyntaxKind RawExtensionBlock = (SyntaxKind)int.MaxValue;

    // The empty cells that complete the short rows of a GFM table, as cmark-gfm limits them: once a table has more, its next
    // line is not a row. Without the limit, a header of many columns followed by many short rows makes the HTML grow
    // quadratically with the text.
    private const long MaximumAutocompletedCells = 0x80000;

    private readonly string _text;
    private readonly MarkdownExtensions _extensions;
    private readonly bool _disableHtml;
    private readonly bool _allowNestedAlerts;
    private readonly int _maximumNestingDepth;

    // The blocks that the document is in: for the content of a cell, the grid tables and their containers
    private int _baseDepth;

    // The alert being converted, which a quote in it cannot be, and the paragraph that starts with its kind
    private bool _inAlert;
    private Block? _alertParagraph;
    private (int KindStart, int KindEnd, int ContentStart) _alertKind;
    private readonly List<TriviaSpan> _triviaSpans = [];

    // The lines of all the leaf blocks: a block has a range of them. Only the last open leaf block gets lines, so its lines
    // are always the last ones.
    // The lines of the leaf blocks, in a rented array that Parse returns
    private Line[] _lines;
    private int _lineCount;
    private readonly Dictionary<string, bool> _references = new(StringComparer.Ordinal);

    // Whether the text is the content of a cell of a grid table whose grid table at the end is kept, and whether the end closes
    // the blocks
    private bool _keepsGridTable;
    private bool _atEnd;
    private readonly InlineParser _inlineParser;

    // The syntax extensions of the options, their states for the document, and the parts that a paragraph extension reports
    private readonly MarkdownParseOptions _options;
    private readonly SyntaxExtensionSet? _syntaxExtensions;
    private readonly SyntaxExtensionStates? _states;
    private readonly List<ParagraphExtensionRange> _paragraphRanges = [];

    // The nodes and tokens of the tree, which the blocks are converted to once they are all read
    private readonly TreeBuffer _tree;

    // The definition that the line closes after it has been read
    private Block? _closingDefinition;
    private readonly Block _document = new(SyntaxKind.Document, parent: null);
    private Block _tip;
    private Block _oldTip;
    private Block _lastMatchedContainer;
    private bool _allClosed = true;
    private bool _skipAddLine;

    // The list item that the current line opened, which a task list item marker can follow
    private Block? _itemOpenedOnLine;

    // The state of the current line: positions are indexes in the text
    private int _lineEnd;
    private int _eolEnd;
    private int _offset;
    private int _column;
    private bool _partiallyConsumedTab;
    private int _nextNonspace;
    private int _nextNonspaceColumn;
    private int _indent;
    private bool _indented;
    private bool _blank;

    private BlockParser(string text, MarkdownParseOptions options, TreeBuffer tree)
    {
        _text = text;
        _tree = tree;
        _extensions = options.Extensions;
        _disableHtml = options.DisableHtml;
        _allowNestedAlerts = options.AllowNestedAlerts;
        _maximumNestingDepth = options.MaximumNestingDepth;

        // A block has at most every line of the text
        _lines = ArrayPool<Line>.Shared.Rent(text.AsSpan().Count('\n') + 1);
        _tip = _document;
        _oldTip = _document;
        _lastMatchedContainer = _document;
        _options = options;
        _syntaxExtensions = options.SyntaxExtensionSet;
        _states = _syntaxExtensions is null ? null : new SyntaxExtensionStates(_syntaxExtensions);
        _inlineParser = new InlineParser(_references, options) { MaximumAbbreviationExpansion = Math.Max(100_000, text.Length), States = _states };
    }

    /// <param name="cell">For the content of a cell of a grid table, the context of the table.</param>
    /// <summary>Adds the nodes and tokens of the text to <paramref name="tree"/>, the document first. Returns the trivia that is not whitespace.</summary>
    public static List<TriviaSpan> Parse(string text, MarkdownParseOptions options, TreeBuffer tree, CellContext? cell = null)
    {
        var parser = new BlockParser(text, options, tree) { _keepsGridTable = cell?.KeepsGridTable ?? false, _baseDepth = cell?.Depth ?? 0 };
        foreach (var label in cell?.References ?? [])
        {
            parser._references.TryAdd(label, value: true);
        }

        parser.ParseBlocks();
        if (parser.HasExtension(MarkdownExtensions.AutoIdentifiers) && options.AutoIdentifierOptions.HasFlag(MarkdownAutoIdentifierOptions.AutoLink))
        {
            parser.AddHeadingLabels(parser._document);
        }

        parser.Convert(parser._document);
        ArrayPool<Line>.Shared.Return(parser._lines);
        parser._lines = [];
        return parser._triviaSpans;
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Parse"/> reads the text as a single paragraph, found from the blocks: the
    /// inlines are not parsed and no tree is built. Returns <see langword="null"/> when only the inlines tell, which is the case
    /// of a paragraph with a pipe when <see cref="MarkdownExtensions.PipeTables"/> is enabled.
    /// </summary>
    public static bool? IsSingleParagraph(string text, MarkdownParseOptions options)
    {
        using var tree = new TreeBuffer(1);
        var parser = new BlockParser(text, options, tree);
        try
        {
            parser.ParseBlocks();

            // The definitions of a paragraph are blocks before it, and a paragraph of definitions only is not one
            if (parser._document.Children is not [{ Kind: SyntaxKind.Paragraph, IsEmpty: false, DefinitionsOrNull: null or [], ExtensionSegments: null } paragraph])
            {
                return false;
            }

            // The pipe tables of Markdig are found in the inlines of a paragraph
            return parser.HasExtension(MarkdownExtensions.PipeTables) && paragraph.Subject!.From(paragraph.ContentStart).Contains('|') ? null : true;
        }
        finally
        {
            ArrayPool<Line>.Shared.Return(parser._lines);
            parser._lines = [];
        }
    }

    // Reads the blocks of the text: the blocks are closed, and the content of their paragraphs is read
    private void ParseBlocks()
    {
        var text = _text;
        var position = HasExtension(MarkdownExtensions.YamlFrontMatter) ? ParseFrontMatter() : 0;
        while (position < text.Length)
        {
            var lineEnd = position;
            while (lineEnd < text.Length && text[lineEnd] is not ('\r' or '\n'))
            {
                lineEnd++;
            }

            var eolEnd = lineEnd;
            if (eolEnd < text.Length)
            {
                eolEnd += text[eolEnd] == '\r' && eolEnd + 1 < text.Length && text[eolEnd + 1] == '\n' ? 2 : 1;
            }

            IncorporateLine(position, lineEnd, eolEnd);
            position = eolEnd;
        }

        _atEnd = true;
        while (_tip is { } tip && tip != _document)
        {
            Finalize(tip);
        }

        Finalize(_document);
    }

    private char Peek(int index) => index < _lineEnd ? _text[index] : '\0';

    // A YAML front matter: the first line of the text is ---, and a later line is --- or ..., each followed by whitespace
    // only. Returns where the lines of the document start.
    private int ParseFrontMatter()
    {
        var (lineEnd, eolEnd) = GetLine(0);
        if (!IsFrontMatterFence(0, lineEnd, '-') || eolEnd == lineEnd)
        {
            return 0;
        }

        var lines = new List<Line>();
        var position = eolEnd;
        while (position < _text.Length)
        {
            var (end, nextLine) = GetLine(position);
            if (IsFrontMatterFence(position, end, '-') || IsFrontMatterFence(position, end, '.'))
            {
                var block = new Block(SyntaxKind.FrontMatter, _document) { Open = false };
                block.Leading.Add(BuildElement.Token(SyntaxKind.FrontMatterFenceToken, 0, 3));
                foreach (var line in lines)
                {
                    AddLine(block, line);
                }

                block.Trailing.Add(BuildElement.Token(SyntaxKind.FrontMatterFenceToken, position, position + 3));
                _document.AddChild(block);
                return nextLine;
            }

            lines.Add(new Line(position, end, nextLine, 0));
            position = nextLine;
        }

        return 0;
    }

    private bool IsFrontMatterFence(int start, int end, char c) => end - start >= 3 && _text[start] == c && _text[start + 1] == c && _text[start + 2] == c && _text.AsSpan(start + 3, end - start - 3).Trim(" \t\v\f").IsEmpty;

    private (int LineEnd, int EolEnd) GetLine(int start)
    {
        var lineEnd = start;
        while (lineEnd < _text.Length && _text[lineEnd] is not ('\r' or '\n'))
        {
            lineEnd++;
        }

        var eolEnd = lineEnd;
        if (eolEnd < _text.Length)
        {
            eolEnd += _text[eolEnd] == '\r' && eolEnd + 1 < _text.Length && _text[eolEnd + 1] == '\n' ? 2 : 1;
        }

        return (lineEnd, eolEnd);
    }

    private void IncorporateLine(int lineStart, int lineEnd, int eolEnd)
    {
        _closingDefinition = null;
        var container = _document;
        _oldTip = _tip;
        _offset = lineStart;
        _column = 0;
        _blank = false;
        _partiallyConsumedTab = false;
        _lineEnd = lineEnd;
        _eolEnd = eolEnd;
        _itemOpenedOnLine = null;

        // The open blocks that the line continues
        var allMatched = true;
        while (container.LastChild is { Open: true } lastChild)
        {
            container = lastChild;
            FindNextNonspace();
            switch (Continue(container))
            {
                case 1:
                    allMatched = false;
                    break;
                case 2:
                    return;
            }

            if (!allMatched)
            {
                container = container.Parent!;
                break;
            }
        }

        _allClosed = container == _oldTip;
        _lastMatchedContainer = container;

        // The blocks that the line starts
        var matchedLeaf = container.Kind != SyntaxKind.Paragraph && AcceptsLines(container.Kind);
        while (!matchedLeaf)
        {
            FindNextNonspace();

            // A block nested in more blocks than the limit is text, so that the tree stays shallow enough to walk
            if (container.Depth + _baseDepth > _maximumNestingDepth)
            {
                AdvanceNextNonspace();
                break;
            }

            if (!_indented && !IsMaybeSpecial(Peek(_nextNonspace)) && !IsMaybeExtension(container, Peek(_nextNonspace)) && _syntaxExtensions?.GetFencedBlockExtensions(Peek(_nextNonspace)) is null)
            {
                AdvanceNextNonspace();
                break;
            }

            // The fenced block extensions are tried before the built-in blocks
            var result = _syntaxExtensions is not null && !_indented && _syntaxExtensions.GetFencedBlockExtensions(Peek(_nextNonspace)) is { } fencedExtensions && TryStartFencedExtension(container, fencedExtensions) ? 2 : TryStart(container);
            if (result == 0)
            {
                AdvanceNextNonspace();
                break;
            }

            container = _tip;
            if (result == 2)
            {
                matchedLeaf = true;
            }
        }

        if (!_allClosed && !_blank && _tip.Kind == SyntaxKind.Paragraph)
        {
            // A lazy continuation line of a paragraph, which keeps the blocks that the line does not continue open
            AddLine();
        }
        else
        {
            CloseUnmatchedBlocks();
            if (AcceptsLines(container.Kind))
            {
                if (!_skipAddLine)
                {
                    AddLine();
                }

                if (container.Kind == SyntaxKind.HtmlBlock && Scanners.IsHtmlBlockEnd(container.HtmlBlockType, _text[_offset.._lineEnd]))
                {
                    Finalize(container);
                }
            }
            else if (_offset < _lineEnd && !_blank)
            {
                AddChild(SyntaxKind.Paragraph);
                AdvanceNextNonspace();
                AddLine();
            }
        }

        _skipAddLine = false;
    }


    private static bool AcceptsLines(SyntaxKind kind) => kind is SyntaxKind.Paragraph or SyntaxKind.IndentedCodeBlock or SyntaxKind.FencedCodeBlock or SyntaxKind.MathBlock or SyntaxKind.HtmlBlock or SyntaxKind.GridTable or RawExtensionBlock;

    private static bool CanContain(SyntaxKind parent, SyntaxKind child) => parent switch
    {
        SyntaxKind.Document or SyntaxKind.BlockQuote or SyntaxKind.ListItem or SyntaxKind.FootnoteDefinition or SyntaxKind.CustomContainer or SyntaxKind.DefinitionItem or SyntaxKind.Figure or SyntaxKind.Footer or SyntaxKind.ExtensionBlock => child is not (SyntaxKind.ListItem or SyntaxKind.DefinitionItem),
        SyntaxKind.List => child == SyntaxKind.ListItem,
        SyntaxKind.DefinitionList => child == SyntaxKind.DefinitionItem,
        _ => false,
    };

    private static bool IsMaybeSpecial(char c) => c is '#' or '`' or '~' or '*' or '+' or '_' or '=' or '<' or '>' or '-' or (>= '0' and <= '9');

    // Whether the line can start a block of an extension: a delimiter row under a paragraph, a row of a table, or the task
    // list item marker after the marker of a list item
    private bool IsMaybeExtension(Block container, char c) => (c == '$' && HasExtension(MarkdownExtensions.Mathematics)) || (c == ':' && HasExtension(MarkdownExtensions.CustomContainers)) || (c is ':' or '~' && HasExtension(MarkdownExtensions.DefinitionLists)) || (char.IsAsciiLetter(c) && HasExtension(MarkdownExtensions.ListExtras)) || (c == '^' && (HasExtension(MarkdownExtensions.Figures) || HasExtension(MarkdownExtensions.Footers))) || (c == '[' && IsFootnoteContainer(container)) || container.Kind switch
    {
        SyntaxKind.Paragraph => HasGfmTables && c is '|' or ':',
        SyntaxKind.Table => true,
        SyntaxKind.ListItem => HasExtension(MarkdownExtensions.TaskLists) && c == '[' && container == _itemOpenedOnLine,
        _ => false,
    };

    private bool HasExtension(MarkdownExtensions extension) => (_extensions & extension) == extension;

    // The pipe tables of Markdig replace the tables of GFM
    private bool HasGfmTables => HasExtension(MarkdownExtensions.Tables) && !HasExtension(MarkdownExtensions.PipeTables);

    // 0: the line continues the block, 1: it does not, 2: the block takes the whole line
    private int Continue(Block block)
    {
        switch (block.Kind)
        {
            case SyntaxKind.Document or SyntaxKind.List or SyntaxKind.DefinitionList:
                return 0;

            case SyntaxKind.BlockQuote:
                if (!_indented && Peek(_nextNonspace) == '>')
                {
                    AdvanceNextNonspace();
                    _triviaSpans.Add(new TriviaSpan(_offset, _offset + 1, SyntaxKind.BlockQuoteMarkerTrivia));
                    AdvanceOffset(1, columns: false);
                    if (Scanners.IsSpaceOrTab(Peek(_offset)))
                    {
                        AdvanceOffset(1, columns: true);
                    }

                    return 0;
                }

                return 1;

            case SyntaxKind.ListItem:
                var data = block.ListData!;
                if (_blank)
                {
                    if (block.Children.Count == 0)
                    {
                        // A list item can start with one blank line only
                        return 1;
                    }

                    AdvanceNextNonspace();
                }
                else if (_indent >= data.MarkerOffset + data.Padding)
                {
                    var start = _offset;
                    AdvanceOffset(data.MarkerOffset + data.Padding, columns: true);
                    if (_offset > start)
                    {
                        _triviaSpans.Add(new TriviaSpan(start, _offset, SyntaxKind.ListItemIndentTrivia));
                    }
                }
                else
                {
                    return 1;
                }

                return 0;

            case SyntaxKind.GridTable:
                // The lines of a grid table start with + or |, after their indentation
                if (Peek(_nextNonspace) is '+' or '|')
                {
                    AdvanceNextNonspace();
                    return 0;
                }

                // Markdig reads the rest of the line after its indentation, which is then not code
                if (IsGridTable(block))
                {
                    AdvanceNextNonspace();
                    return 1;
                }

                // As in Markdig, the lines are the paragraph that the line continues, which keeps the whitespace at their end
                block.Kind = SyntaxKind.Paragraph;
                block.GridLineCount = block.LineCount;
                return Continue(block);

            case SyntaxKind.FencedCodeBlock or SyntaxKind.MathBlock:
                if (_indent <= 3 && Peek(_nextNonspace) == block.FenceChar)
                {
                    var end = _nextNonspace;
                    while (end < _lineEnd && _text[end] == block.FenceChar)
                    {
                        end++;
                    }

                    var length = end - _nextNonspace;
                    var rest = end;
                    while (rest < _lineEnd && Scanners.IsSpaceOrTab(_text[rest]))
                    {
                        rest++;
                    }

                    if (length >= block.FenceLength && rest == _lineEnd)
                    {
                        block.Trailing.Add(BuildElement.Token(block.Kind == SyntaxKind.MathBlock ? SyntaxKind.MathFenceToken : SyntaxKind.CodeFenceToken, _nextNonspace, end));
                        Finalize(block);
                        return 2;
                    }
                }

                // The indentation of the opening fence is removed from the content
                for (var i = block.FenceOffset; i > 0 && Scanners.IsSpaceOrTab(Peek(_offset)); i--)
                {
                    AdvanceOffset(1, columns: true);
                }

                return 0;

            case SyntaxKind.DefinitionItem:
            {
                // As in Markdig: an indented line continues the definition, a marker starts the next one, a blank line leaves a
                // blank line in the definition, and another line continues it after a paragraph only
                if (_indent >= CodeIndent)
                {
                    AdvanceOffset(CodeIndent, columns: true);
                    return 0;
                }

                if (Peek(_nextNonspace) is ':' or '~')
                {
                    if (GetDefinitionMarkerWidth() < 4 && block.LastIsBlankLine)
                    {
                        block.BlankLineCount--;
                        block.LastIsBlankLine = false;
                    }

                    return 1;
                }

                if (_blank)
                {
                    if (!block.LastIsBlankLine && block.LastChild is not { Kind: SyntaxKind.FencedCodeBlock or SyntaxKind.MathBlock or RawExtensionBlock })
                    {
                        block.BlankLineCount++;
                        block.LastIsBlankLine = true;
                    }

                    return 0;
                }

                if (!block.LastIsBlankLine && block.LastChild is { Kind: SyntaxKind.Paragraph })
                {
                    return 0;
                }

                if (block.LastIsBlankLine)
                {
                    block.BlankLineCount--;
                    block.LastIsBlankLine = false;
                }

                // Markdig closes the definition, but its open blocks still read the line, and the next lines: the blocks that the line
                // starts are after the definition, except the lists that its lists start. The line is read after its indentation.
                _closingDefinition = block;
                AdvanceNextNonspace();
                return 0;
            }

            case SyntaxKind.Figure:
                // As in Markdig: a fence of carets as long as the opening one closes the figure, and the text after it is a caption
                if (_indent <= 3 && Peek(_nextNonspace) == '^')
                {
                    var end = _nextNonspace;
                    while (end < _lineEnd && _text[end] == '^')
                    {
                        end++;
                    }

                    if (end - _nextNonspace >= block.FenceLength)
                    {
                        while (_tip != block)
                        {
                            Finalize(_tip);
                        }

                        block.Trailing.Add(BuildElement.Token(SyntaxKind.FigureFenceToken, _nextNonspace, end));
                        block.ClosingCaption = GetCaption(end);
                        Finalize(block);
                        return 2;
                    }
                }

                return 0;

            case SyntaxKind.Footer:
                // As a block quote, with ^^ and a space: a blank line ends the footer, and takes the line
                if (!_indented && Peek(_nextNonspace) == '^' && Peek(_nextNonspace + 1) == '^')
                {
                    AdvanceNextNonspace();
                    var marker = _offset;
                    AdvanceOffset(2, columns: false);
                    _triviaSpans.Add(new TriviaSpan(marker, marker + 2, SyntaxKind.FooterMarkerTrivia));
                    if (Scanners.IsSpaceOrTab(Peek(_offset)))
                    {
                        AdvanceOffset(1, columns: true);
                    }

                    return 0;
                }

                if (_blank && !_indented)
                {
                    while (_tip != block)
                    {
                        Finalize(_tip);
                    }

                    Finalize(block);
                    return 2;
                }

                return 1;

            case SyntaxKind.CustomContainer:
                // As in Markdig: a fence of colons as long as the opening one closes the container and its blocks, even in a code
                // block. The indentation of the other lines is kept.
                if (_indent <= 3 && Peek(_nextNonspace) == ':')
                {
                    var end = _nextNonspace;
                    while (end < _lineEnd && _text[end] == ':')
                    {
                        end++;
                    }

                    var rest = end;
                    while (rest < _lineEnd && Scanners.IsSpaceOrTab(_text[rest]))
                    {
                        rest++;
                    }

                    if (end - _nextNonspace >= block.FenceLength && rest == _lineEnd)
                    {
                        while (_tip != block)
                        {
                            Finalize(_tip);
                        }

                        block.Trailing.Add(BuildElement.Token(SyntaxKind.CustomContainerFenceToken, _nextNonspace, end));
                        Finalize(block);
                        return 2;
                    }
                }

                return 0;

            case SyntaxKind.IndentedCodeBlock:
                if (_indent >= CodeIndent)
                {
                    AdvanceOffset(CodeIndent, columns: true);
                }
                else if (_blank)
                {
                    AdvanceNextNonspace();
                }
                else
                {
                    return 1;
                }

                return 0;

            case SyntaxKind.HtmlBlock:
                return _blank && block.HtmlBlockType is 6 or 7 ? 1 : 0;

            case SyntaxKind.FootnoteDefinition:
            {
                // As in Markdig: a blank line continues the footnote, and a line that is not indented ends it after a blank line or
                // when it starts another footnote. The lines of a fenced block in the footnote are its own.
                var leaf = block;
                while (leaf.LastChild is { Open: true } open)
                {
                    leaf = open;
                }

                if (leaf.Kind is not (SyntaxKind.FencedCodeBlock or SyntaxKind.MathBlock or RawExtensionBlock))
                {
                    // A blank line closes the blocks of the footnote, and the footnote takes it
                    if (_blank)
                    {
                        block.FootnoteLastLineBlank = true;
                        while (_tip != block)
                        {
                            Finalize(_tip);
                        }

                        return 2;
                    }

                    if (_indent == 0 && (block.FootnoteLastLineBlank || ScanFootnoteLabel(_nextNonspace) > 0))
                    {
                        return 1;
                    }
                }

                block.FootnoteLastLineBlank = false;
                if (_indented)
                {
                    AdvanceOffset(CodeIndent, columns: true);
                }

                return 0;
            }

            case SyntaxKind.Paragraph:
                return _blank ? 1 : 0;

            case SyntaxKind.ExtensionBlock or RawExtensionBlock:
                return ContinueFencedExtension(block);

            case SyntaxKind.Table:
                // A row has at least one cell: a line with only a pipe ends the table
                return _blank || block.TableAutocompletedCells > MaximumAutocompletedCells || TableScanner.ScanRow(_text, _nextNonspace, _lineEnd, cells: null, pipes: null) == 0 ? 1 : 0;

            default:
                return 1;
        }
    }

    // 0: no block starts, 1: a container starts, 2: a leaf block starts
    private int TryStart(Block container)
    {
        var c = Peek(_nextNonspace);

        // Grid table, a line of columns, +---+---+, which Markdig tries first, unless the line is an item of the list of + that it continues
        if (!_indented && c == '+' && HasExtension(MarkdownExtensions.GridTables) && !(container.Kind == SyntaxKind.List && container.ListData is { Ordered: false, BulletChar: '+' } && (_nextNonspace + 1 >= _lineEnd || Scanners.IsSpaceOrTab(_text[_nextNonspace + 1]))) && GridTableLayout.ReadColumns(_text.AsSpan(_nextNonspace, _lineEnd - _nextNonspace)) is not null)
        {
            CloseUnmatchedBlocks();
            AddChild(SyntaxKind.GridTable);
            AdvanceNextNonspace();
            return 2;
        }

        // A line of dashes, pipes, colons and spaces that has a pipe continues a paragraph, as Markdig reads a delimiter row of a pipe
        // table, which is then not a list item, the underline of a setext heading or a thematic break. It is a lazy line, unless
        // it starts an item of a list that it does not continue.
        if (!_indented && c == '-' && HasExtension(MarkdownExtensions.PipeTables) && (container.Kind == SyntaxKind.Paragraph || (!_allClosed && _tip.Kind == SyntaxKind.Paragraph && !(Scanners.IsSpaceOrTab(Peek(_nextNonspace + 1)) && HasUnmatchedListItem()))) && IsPipeTableDelimiterLine())
        {
            return 0;
        }

        // Definition, which Markdig tries first: a marker after a paragraph, whose lines are the terms, or after a definition. In
        // a list item, a blank line after the paragraph is a block of the item for Markdig.
        if (!_indented && c is ':' or '~' && HasExtension(MarkdownExtensions.DefinitionLists) && TryStartDefinition(container))
        {
            return 1;
        }

        // Figure and footer, which Markdig tries first
        if (!_indented && c == '^' && (HasExtension(MarkdownExtensions.Figures) || HasExtension(MarkdownExtensions.Footers)))
        {
            var carets = _nextNonspace;
            while (carets < _lineEnd && _text[carets] == '^')
            {
                carets++;
            }

            if (carets - _nextNonspace >= 3 && HasExtension(MarkdownExtensions.Figures))
            {
                CloseUnmatchedBlocks();
                var figure = AddChild(SyntaxKind.Figure);
                figure.FenceLength = carets - _nextNonspace;
                figure.Leading.Add(BuildElement.Token(SyntaxKind.FigureFenceToken, _nextNonspace, carets));
                figure.OpeningCaption = GetCaption(carets);
                AdvanceNextNonspace();
                AdvanceOffset(_lineEnd - _offset, columns: false);
                return 2;
            }

            if (carets - _nextNonspace >= 2 && HasExtension(MarkdownExtensions.Footers))
            {
                AdvanceNextNonspace();
                var marker = _offset;
                AdvanceOffset(2, columns: false);
                if (Scanners.IsSpaceOrTab(Peek(_offset)))
                {
                    AdvanceOffset(1, columns: true);
                }

                CloseUnmatchedBlocks();
                AddChild(SyntaxKind.Footer).Leading.Add(BuildElement.Token(SyntaxKind.FooterMarkerToken, marker, marker + 2));
                return 1;
            }
        }

        // Custom container, which Markdig tries first: its opening fence is the whole line
        if (!_indented && c == ':' && HasExtension(MarkdownExtensions.CustomContainers) && TryStartCustomContainer())
        {
            return 2;
        }

        // Footnote definition: [^label]: at the level of the document, which the rest of the line is the content of
        if (!_indented && c == '[' && IsFootnoteContainer(container) && ScanFootnoteLabel(_nextNonspace) is > 0 and var labelEnd)
        {
            // The label is without the whitespace around it, which is trivia
            var labelStart = _nextNonspace + 1;
            var labelTextEnd = labelEnd - 1;
            while (_text[labelStart] is ' ' or '\t')
            {
                labelStart++;
            }

            while (_text[labelTextEnd - 1] is ' ' or '\t')
            {
                labelTextEnd--;
            }

            CloseUnmatchedBlocks();
            var footnote = AddChild(SyntaxKind.FootnoteDefinition);
            footnote.Leading.Add(BuildElement.Token(SyntaxKind.OpenBracketToken, _nextNonspace, _nextNonspace + 1));
            footnote.Leading.Add(BuildElement.Token(SyntaxKind.FootnoteLabelToken, labelStart, labelTextEnd));
            footnote.Leading.Add(BuildElement.Token(SyntaxKind.CloseBracketToken, labelEnd - 1, labelEnd));
            footnote.Leading.Add(BuildElement.Token(SyntaxKind.ColonToken, labelEnd, labelEnd + 1));
            _inlineParser.Footnotes.Add(Scanners.NormalizeLabel(_text[labelStart..labelTextEnd]));
            AdvanceNextNonspace();
            AdvanceOffset(labelEnd + 1 - _offset, columns: false);
            return 1;
        }

        // Block quote
        if (!_indented && c == '>')
        {
            AdvanceNextNonspace();
            var marker = _offset;
            AdvanceOffset(1, columns: false);
            if (Scanners.IsSpaceOrTab(Peek(_offset)))
            {
                AdvanceOffset(1, columns: true);
            }

            CloseUnmatchedBlocks();
            AddChild(SyntaxKind.BlockQuote).Leading.Add(BuildElement.Token(SyntaxKind.BlockQuoteMarkerToken, marker, marker + 1));
            return 1;
        }

        // ATX heading
        if (!_indented && c == '#' && TryStartAtxHeading())
        {
            return 2;
        }

        // Fenced code block
        if (!_indented && (c is '`' or '~' || (c == '$' && HasExtension(MarkdownExtensions.Mathematics))) && TryStartFencedCodeBlock(c))
        {
            return 2;
        }

        // HTML block
        if (!_indented && c == '<' && !_disableHtml)
        {
            var interruptsParagraph = container.Kind == SyntaxKind.Paragraph || (!_allClosed && !_blank && _tip.Kind == SyntaxKind.Paragraph);
            var type = Scanners.GetHtmlBlockStart(_text[_nextNonspace.._lineEnd], interruptsParagraph);
            if (type > 0)
            {
                CloseUnmatchedBlocks();
                AddChild(SyntaxKind.HtmlBlock).HtmlBlockType = type;
                return 2;
            }
        }

        // Setext heading underline
        if (!_indented && container.Kind == SyntaxKind.Paragraph && c is '=' or '-' && IsSetextUnderline(out var underlineEnd))
        {
            CloseUnmatchedBlocks();
            ParseDefinitions(container);
            if (!container.IsEmpty)
            {
                container.Kind = SyntaxKind.SetextHeading;
                container.Trailing.Add(BuildElement.Token(SyntaxKind.SetextHeadingUnderlineToken, _nextNonspace, underlineEnd));
                _offset = _lineEnd;
                return 2;
            }
        }

        // Thematic break
        if (!_indented && c is '*' or '_' or '-' && IsThematicBreak(out var breakEnd))
        {
            CloseUnmatchedBlocks();
            var block = AddChild(SyntaxKind.ThematicBreak);
            block.Leading.Add(BuildElement.Token(SyntaxKind.ThematicBreakToken, _nextNonspace, breakEnd));
            _offset = _lineEnd;
            return 2;
        }

        // List item
        if ((!_indented || container.Kind == SyntaxKind.List) && ParseListMarker(container) is { } data)
        {
            CloseUnmatchedBlocks();
            if (_tip.Kind != SyntaxKind.List || !ListsMatch(container.ListData, data))
            {
                AddChild(SyntaxKind.List).ListData = data;
            }

            var item = AddChild(SyntaxKind.ListItem);
            item.ListData = data;
            item.Leading.Add(BuildElement.Token(SyntaxKind.ListMarkerToken, data.MarkerStart, data.MarkerEnd, value: (int)data.Style));
            _itemOpenedOnLine = item;
            return 1;
        }

        // Abbreviation definition: *[label]: text
        if (!_indented && c == '*' && HasExtension(MarkdownExtensions.Abbreviations) && TryStartAbbreviationDefinition())
        {
            return 2;
        }

        // Indented code block
        if (_indented && _tip.Kind != SyntaxKind.Paragraph && !_blank)
        {
            AdvanceOffset(CodeIndent, columns: true);
            CloseUnmatchedBlocks();
            AddChild(SyntaxKind.IndentedCodeBlock);
            return 2;
        }

        if (_indented)
        {
            return 0;
        }

        // Table: a delimiter row under the last line of a paragraph, which is the header row
        if (HasGfmTables && container.Kind == SyntaxKind.Paragraph && !container.TableVisited && TryStartTable(container))
        {
            return 2;
        }

        // A row of a table
        if (container.Kind == SyntaxKind.Table && !_blank)
        {
            container.TableAutocompletedCells += Math.Max(0, container.TableColumnCount - TableScanner.ScanRow(_text, _nextNonspace, _lineEnd, cells: null, pipes: null));
            AddLine(container, new Line(_nextNonspace, _lineEnd, _eolEnd, 0));
            _offset = _lineEnd;
            return 2;
        }

        // Task list item: [ ], [x] or [X] at the start of the first paragraph of a list item, on the line of its marker
        if (HasExtension(MarkdownExtensions.TaskLists) && container == _itemOpenedOnLine && IsTaskListItemMarker())
        {
            container.Leading.Add(BuildElement.Token(SyntaxKind.TaskListItemMarkerToken, _nextNonspace, _nextNonspace + 3));
            AdvanceNextNonspace();
            AdvanceOffset(3, columns: false);
            FindNextNonspace();
            _itemOpenedOnLine = null;
            return 0;
        }

        return 0;
    }

    // The label is between brackets, at most 999 characters with escaped brackets, and the text is the rest of the line. A
    // later definition of a label replaces the earlier one, as in Markdig.
    private bool TryStartAbbreviationDefinition()
    {
        var position = _nextNonspace + 1;
        if (Peek(position) != '[')
        {
            return false;
        }

        var labelStart = position + 1;
        var labelEnd = labelStart;
        while (labelEnd < _lineEnd && _text[labelEnd] != ']' && labelEnd - labelStart < 1000)
        {
            if (_text[labelEnd] == '[')
            {
                return false;
            }

            labelEnd += _text[labelEnd] == '\\' && labelEnd + 1 < _lineEnd ? 2 : 1;
        }

        if (labelEnd >= _lineEnd || _text[labelEnd] != ']' || Peek(labelEnd + 1) != ':' || _text.AsSpan(labelStart, labelEnd - labelStart).IsWhiteSpace())
        {
            return false;
        }

        var textStart = labelEnd + 2;
        var textEnd = _lineEnd;
        // The text is trimmed of ASCII whitespace, as Markdig trims it
        while (textStart < textEnd && _text[textStart] is ' ' or '\t' or '\v' or '\f')
        {
            textStart++;
        }

        while (textEnd > textStart && _text[textEnd - 1] is ' ' or '\t' or '\v' or '\f')
        {
            textEnd--;
        }

        CloseUnmatchedBlocks();
        var block = AddChild(SyntaxKind.AbbreviationDefinition);
        block.Leading.Add(BuildElement.Token(SyntaxKind.AsteriskToken, _nextNonspace, _nextNonspace + 1));
        block.Leading.Add(BuildElement.Token(SyntaxKind.OpenBracketToken, labelStart - 1, labelStart));
        block.Leading.Add(BuildElement.Token(SyntaxKind.AbbreviationLabelToken, labelStart, labelEnd));
        block.Leading.Add(BuildElement.Token(SyntaxKind.CloseBracketToken, labelEnd, labelEnd + 1));
        block.Leading.Add(BuildElement.Token(SyntaxKind.ColonToken, labelEnd + 1, labelEnd + 2));
        if (textEnd > textStart)
        {
            block.Leading.Add(BuildElement.Token(SyntaxKind.AbbreviationTextToken, textStart, textEnd));
        }

        _inlineParser.Abbreviations[Scanners.NormalizeAbbreviationLabel(_text.AsSpan(labelStart, labelEnd - labelStart))] = textEnd - textStart;
        _offset = _lineEnd;
        return true;
    }

    // A footnote is a child of the document, which can interrupt a paragraph, and end a list whose item the line does not continue
    private bool IsFootnoteContainer(Block container) => HasExtension(MarkdownExtensions.Footnotes) && (container == _document || (container.Kind is SyntaxKind.Paragraph or SyntaxKind.List && container.Parent == _document));

    // [^label]: where the label is a link label that starts with ^ once trimmed. Returns where the ] ends, or 0.
    private int ScanFootnoteLabel(int start)
    {
        var position = start + 1;
        while (Peek(position) is ' ' or '\t')
        {
            position++;
        }

        if (Peek(start) != '[' || Peek(position) != '^')
        {
            return 0;
        }

        while (position < _lineEnd && _text[position] != ']' && position - start <= 1000)
        {
            if (_text[position] == '[')
            {
                return 0;
            }

            position += _text[position] == '\\' && position + 1 < _lineEnd ? 2 : 1;
        }

        return position < _lineEnd && _text[position] == ']' && Peek(position + 1) == ':' ? position + 1 : 0;
    }

    private bool IsTaskListItemMarker()
    {
        var position = _nextNonspace;
        if (position + 3 >= _lineEnd || _text[position] != '[' || _text[position + 1] is not (' ' or 'x' or 'X') || _text[position + 2] != ']' || !Scanners.IsSpaceOrTab(_text[position + 3]))
        {
            return false;
        }

        // The marker is followed by the content of the paragraph
        position += 3;
        while (position < _lineEnd && Scanners.IsSpaceOrTab(_text[position]))
        {
            position++;
        }

        return position < _lineEnd;
    }

    private bool HasUnmatchedListItem()
    {
        for (var block = _oldTip; block != _lastMatchedContainer && block is not null; block = block.Parent)
        {
            if (block.Kind == SyntaxKind.ListItem)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPipeTableDelimiterLine()
    {
        var hasPipe = false;
        for (var i = _nextNonspace; i < _lineEnd; i++)
        {
            switch (_text[i])
            {
                case '|':
                    hasPipe = true;
                    break;
                case not (' ' or '-' or ':'):
                    return false;
            }
        }

        return hasPipe;
    }

    private bool TryStartTable(Block paragraph)
    {
        var columns = TableScanner.ScanDelimiterRow(_text, _nextNonspace, _lineEnd, cells: null, pipes: null);
        if (columns == 0)
        {
            return false;
        }

        // A setext underline that found only link reference definitions read the paragraph already: a line of a definition is
        // not a header row
        if (paragraph.LineCount <= paragraph.DefinitionLineCount)
        {
            return false;
        }

        // The header row must have as many cells as the delimiter row. A paragraph that fails is never a table: its next lines
        // are not tried again.
        var header = _lines[paragraph.LineStart + paragraph.LineCount - 1];
        if (TableScanner.ScanRow(_text, header.Start, header.End, cells: null, pipes: null) != columns)
        {
            paragraph.TableVisited = true;
            return false;
        }

        CloseUnmatchedBlocks();

        // The lines before the header row are a paragraph
        if (paragraph.LineCount > 1)
        {
            var parent = paragraph.Parent!;
            var before = new Block(SyntaxKind.Paragraph, parent) { Open = false, LineStart = paragraph.LineStart, LineCount = paragraph.LineCount - 1 };
            parent.InsertChild(parent.Children.Count - 1, before);
            ParseDefinitions(before);
        }

        // The header row is the last line of the paragraph, so the delimiter row is next to it
        paragraph.Kind = SyntaxKind.Table;
        paragraph.TableColumnCount = columns;
        paragraph.LineStart += paragraph.LineCount - 1;
        paragraph.LineCount = 1;
        AddLine(paragraph, new Line(_nextNonspace, _lineEnd, _eolEnd, 0));
        _offset = _lineEnd;
        return true;
    }

    private bool TryStartAtxHeading()
    {
        var position = _nextNonspace;
        while (position < _lineEnd && _text[position] == '#')
        {
            position++;
        }

        var level = position - _nextNonspace;
        if (level > 6 || (position < _lineEnd && !Scanners.IsSpaceOrTab(_text[position])))
        {
            return false;
        }

        AdvanceNextNonspace();
        var markerStart = _offset;
        var afterSpaces = position;
        while (afterSpaces < _lineEnd && Scanners.IsSpaceOrTab(_text[afterSpaces]))
        {
            afterSpaces++;
        }

        AdvanceOffset(afterSpaces - _offset, columns: false);
        CloseUnmatchedBlocks();
        var block = AddChild(SyntaxKind.AtxHeading);
        block.Leading.Add(BuildElement.Token(SyntaxKind.AtxHeadingMarkerToken, markerStart, position));

        // The attributes are read before the closing sequence, which is before them
        var lineEnd = _lineEnd;
        var attributes = ParseBlockAttributes(_offset, ref lineEnd);

        // The closing sequence is a run of '#' at the end, after a space, or alone
        var end = lineEnd;
        while (end > _offset && Scanners.IsSpaceOrTab(_text[end - 1]))
        {
            end--;
        }

        var hashStart = end;
        while (hashStart > _offset && _text[hashStart - 1] == '#')
        {
            hashStart--;
        }

        var contentEnd = end;
        if (hashStart < end && (hashStart == _offset || Scanners.IsSpaceOrTab(_text[hashStart - 1])))
        {
            block.Trailing.Add(BuildElement.Token(SyntaxKind.AtxHeadingClosingSequenceToken, hashStart, end));
            contentEnd = hashStart;
            while (contentEnd > _offset && Scanners.IsSpaceOrTab(_text[contentEnd - 1]))
            {
                contentEnd--;
            }
        }

        if (attributes is not null)
        {
            block.Trailing.AddRange(attributes);
        }

        AddLine(block, new Line(_offset, contentEnd, contentEnd, 0));
        _offset = _lineEnd;
        return true;
    }

    // The generic attributes of a block, from the first { of its line: the text after them is dropped, as Markdig drops it
    private BuildElement[]? ParseBlockAttributes(int start, ref int lineEnd)
    {
        if (!HasExtension(MarkdownExtensions.GenericAttributes))
        {
            return null;
        }

        var brace = _text.AsSpan(start, lineEnd - start).IndexOf('{');
        if (brace < 0 || AttributesScanner.TryParse(_text, start + brace, lineEnd, out _) is not (>= 0 and var end))
        {
            return null;
        }

        brace += start;
        var rest = end;
        var restEnd = lineEnd;
        while (rest < restEnd && TableScanner.IsSpace(_text[rest]))
        {
            rest++;
        }

        while (restEnd > rest && TableScanner.IsSpace(_text[restEnd - 1]))
        {
            restEnd--;
        }

        lineEnd = brace;
        var attributes = BuildElement.Token(SyntaxKind.AttributesToken, brace, end, value: 3);
        return restEnd > rest
            ? [BuildElement.Node(SyntaxKind.GenericAttributes, 2), attributes, BuildElement.Token(SyntaxKind.AttributesTrailingTextToken, rest, restEnd)]
            : [BuildElement.Node(SyntaxKind.GenericAttributes, 1), attributes];
    }

    private bool TryStartFencedCodeBlock(char c)
    {
        var position = _nextNonspace;
        while (position < _lineEnd && _text[position] == c)
        {
            position++;
        }

        // The attributes are read first, and the info string is before them. A math block opens with exactly $$, and nothing
        // else on the line.
        var lineEnd = _lineEnd;
        var attributes = ParseBlockAttributes(position, ref lineEnd);
        var length = position - _nextNonspace;
        var math = c == '$';
        if (math ? length != 2 || !_text.AsSpan(position, lineEnd - position).Trim(" \t").IsEmpty : length < 3 || (c == '`' && _text.AsSpan(position, lineEnd - position).Contains('`')))
        {
            return false;
        }

        CloseUnmatchedBlocks();
        var block = AddChild(math ? SyntaxKind.MathBlock : SyntaxKind.FencedCodeBlock);
        block.FenceChar = c;
        block.FenceLength = length;
        // Markdig keeps the indentation of the opening fence in the lines of a math block, which a code block removes
        block.FenceOffset = math ? 0 : _indent;
        AdvanceNextNonspace();
        block.Leading.Add(BuildElement.Token(math ? SyntaxKind.MathFenceToken : SyntaxKind.CodeFenceToken, _offset, position));
        AdvanceOffset(length, columns: false);

        // The info string is the rest of the line, without the whitespace around it
        var infoStart = _offset;
        var infoEnd = lineEnd;
        while (infoStart < infoEnd && Scanners.IsSpaceOrTab(_text[infoStart]))
        {
            infoStart++;
        }

        while (infoEnd > infoStart && Scanners.IsSpaceOrTab(_text[infoEnd - 1]))
        {
            infoEnd--;
        }

        if (infoEnd > infoStart)
        {
            block.Leading.Add(BuildElement.Token(SyntaxKind.InfoStringToken, infoStart, infoEnd));
        }

        if (attributes is not null)
        {
            block.Leading.AddRange(attributes);
        }

        _skipAddLine = true;
        return true;
    }

    // The text after a fence of a figure, without the whitespace before it, which is its caption: Markdig keeps the whitespace
    // after it
    private (int Start, int End) GetCaption(int fenceEnd)
    {
        var start = fenceEnd;
        while (start < _lineEnd && Scanners.IsSpaceOrTab(_text[start]))
        {
            start++;
        }

        return (start, _lineEnd);
    }

    // The columns from the start of the line, after the containers, to the content after the marker of a definition: the marker,
    // and the whitespace after it
    private int GetDefinitionMarkerWidth()
    {
        var column = _nextNonspaceColumn + 1;
        for (var i = _nextNonspace + 1; i < _lineEnd && _text[i] is ' ' or '\t'; i++)
        {
            column += _text[i] == '\t' ? 4 - (column % 4) : 1;
        }

        return column - _column;
    }

    private bool TryStartDefinition(Block container)
    {
        if (GetDefinitionMarkerWidth() < 4)
        {
            return false;
        }

        // The terms are the lines of the paragraph: Markdig does not read the link reference definitions of a paragraph that
        // is still open, which are terms then
        Block? paragraph = null;
        var lazy = false;
        if (container.Kind == SyntaxKind.Paragraph)
        {
            // As in Markdig, a paragraph that has link reference definitions has no terms, such as the ones read for a setext
            // underline
            if (container.DefinitionsOrNull is not null)
            {
                return false;
            }

            paragraph = container;
        }
        else if (container.Kind == SyntaxKind.DefinitionList)
        {
            // The marker of the next definition, which the open one did not continue
        }
        else if (!_allClosed && _tip is { Kind: SyntaxKind.Paragraph, Open: true })
        {
            // A lazy line: Markdig reads the marker after the paragraph of the containers that the line does not continue, which
            // stay open
            paragraph = _tip;
            lazy = true;
        }
        else if (container.LastChild is { Kind: SyntaxKind.Paragraph, Open: false, IsEmpty: false, DefinitionsOrNull: null, ExtensionSegments: null } closed && container.Kind is not (SyntaxKind.ListItem or SyntaxKind.DefinitionItem or SyntaxKind.FootnoteDefinition))
        {
            paragraph = closed;
        }
        else
        {
            return false;
        }

        // The list goes in the block of the paragraph, which is deeper than the container for a lazy line: a block nested in more
        // blocks than the limit is text
        if (paragraph is not null && paragraph.Parent!.Depth + _baseDepth > _maximumNestingDepth)
        {
            return false;
        }

        if (lazy)
        {
            _allClosed = true;
        }

        var termLineStart = 0;
        var termLineCount = 0;
        var termsTrimmed = false;
        if (paragraph is not null)
        {
            var parent = paragraph.Parent!;
            termLineStart = paragraph.LineStart;
            termLineCount = paragraph.LineCount;
            termsTrimmed = !paragraph.Open;
            if (paragraph.Open)
            {
                // The paragraph is not finalized: it has no link reference definitions
                paragraph.Open = false;
                _tip = parent;
                _oldTip = parent;
                _lastMatchedContainer = parent;
            }
            else
            {
                CloseUnmatchedBlocks();
            }

            parent.RemoveChild(paragraph);

            _tip = parent;
            if (parent.LastChild is { Kind: SyntaxKind.DefinitionList } list)
            {
                // The terms after a definition list add a definition to it
                list.Open = true;
                _tip = list;
            }
            else
            {
                AddChild(SyntaxKind.DefinitionList);
            }
        }
        else
        {
            CloseUnmatchedBlocks();
        }

        var item = AddChild(SyntaxKind.DefinitionItem);
        item.TermLineStart = termLineStart;
        item.TermLineCount = termLineCount;
        item.TermsTrimmed = termsTrimmed;
        item.MarkerPosition = _nextNonspace;

        // The content starts four columns after the start of the line
        var contentColumn = _column + 4;
        AdvanceNextNonspace();
        AdvanceOffset(1, columns: false);
        if (contentColumn > _column)
        {
            AdvanceOffset(contentColumn - _column, columns: true);
        }

        return true;
    }

    // A fence of three colons or more, then a name, which is the class of the container, and arguments
    private bool TryStartCustomContainer()
    {
        var position = _nextNonspace;
        while (position < _lineEnd && _text[position] == ':')
        {
            position++;
        }

        var length = position - _nextNonspace;
        if (length < 3)
        {
            return false;
        }

        var lineEnd = _lineEnd;
        var attributes = ParseBlockAttributes(position, ref lineEnd);
        CloseUnmatchedBlocks();
        var block = AddChild(SyntaxKind.CustomContainer);
        block.FenceLength = length;
        AdvanceNextNonspace();
        block.Leading.Add(BuildElement.Token(SyntaxKind.CustomContainerFenceToken, _offset, position));

        // The name is the first word, and the arguments are the rest of the line, without the whitespace around them
        var nameStart = position;
        while (nameStart < lineEnd && Scanners.IsSpaceOrTab(_text[nameStart]))
        {
            nameStart++;
        }

        var nameEnd = nameStart;
        while (nameEnd < lineEnd && !Scanners.IsSpaceOrTab(_text[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd > nameStart)
        {
            block.Leading.Add(BuildElement.Token(SyntaxKind.InfoStringToken, nameStart, nameEnd));
        }

        var argumentsStart = nameEnd;
        var argumentsEnd = lineEnd;
        while (argumentsStart < argumentsEnd && Scanners.IsSpaceOrTab(_text[argumentsStart]))
        {
            argumentsStart++;
        }

        while (argumentsEnd > argumentsStart && Scanners.IsSpaceOrTab(_text[argumentsEnd - 1]))
        {
            argumentsEnd--;
        }

        if (argumentsEnd > argumentsStart)
        {
            block.Leading.Add(BuildElement.Token(SyntaxKind.CustomContainerArgumentsToken, argumentsStart, argumentsEnd));
        }

        if (attributes is not null)
        {
            block.Leading.AddRange(attributes);
        }

        // The rest of the line is the fence: no block starts after it
        AdvanceOffset(_lineEnd - _offset, columns: false);
        return true;
    }

    private bool IsSetextUnderline(out int end)
    {
        var c = _text[_nextNonspace];
        end = _nextNonspace;
        while (end < _lineEnd && _text[end] == c)
        {
            end++;
        }

        var rest = end;
        while (rest < _lineEnd && Scanners.IsSpaceOrTab(_text[rest]))
        {
            rest++;
        }

        return rest == _lineEnd;
    }

    private bool IsThematicBreak(out int end)
    {
        var c = _text[_nextNonspace];
        var count = 0;
        end = _nextNonspace;
        for (var i = _nextNonspace; i < _lineEnd; i++)
        {
            if (_text[i] == c)
            {
                count++;
                end = i + 1;
            }
            else if (!Scanners.IsSpaceOrTab(_text[i]))
            {
                return false;
            }
        }

        return count >= 3;
    }

    private ListData? ParseListMarker(Block container)
    {
        if (_indent >= CodeIndent)
        {
            return null;
        }

        var markerStart = _nextNonspace;
        var c = Peek(markerStart);
        bool ordered;
        var start = 0;
        var delimiter = '\0';
        var style = MarkdownOrderedListStyle.Numbers;
        int markerEnd;
        if (c is '*' or '+' or '-')
        {
            ordered = false;
            markerEnd = markerStart + 1;
        }
        else if (char.IsAsciiLetter(c) && HasExtension(MarkdownExtensions.ListExtras))
        {
            // As in Markdig: roman numbers, without validation, unless the line continues a list that is not of roman numbers, or a
            // letter. When this fails, Markdig reads the line again as the start of a list, where roman numbers are allowed.
            var romanAllowed = container.Kind != SyntaxKind.List || container.ListData!.Style is MarkdownOrderedListStyle.LowerRoman or MarkdownOrderedListStyle.UpperRoman;
            if (!TryParseLetterMarker(markerStart, romanAllowed, out var end, out style, out start) && (romanAllowed || !TryParseLetterMarker(markerStart, romanAllowed: true, out end, out style, out start)))
            {
                return null;
            }

            ordered = true;
            delimiter = _text[end];
            markerEnd = end + 1;
        }
        else
        {
            var digitsEnd = markerStart;
            while (digitsEnd < _lineEnd && digitsEnd - markerStart < 9 && char.IsAsciiDigit(_text[digitsEnd]))
            {
                digitsEnd++;
            }

            if (digitsEnd == markerStart || Peek(digitsEnd) is not ('.' or ')'))
            {
                return null;
            }

            start = int.Parse(_text.AsSpan(markerStart, digitsEnd - markerStart), System.Globalization.CultureInfo.InvariantCulture);

            // An ordered list can interrupt a paragraph only when it starts with 1
            if (container.Kind == SyntaxKind.Paragraph && start != 1)
            {
                return null;
            }

            ordered = true;
            delimiter = _text[digitsEnd];
            markerEnd = digitsEnd + 1;
        }

        var next = Peek(markerEnd);
        if (next is not ('\0' or ' ' or '\t'))
        {
            return null;
        }

        // A list item can interrupt a paragraph only when it is not empty
        if (container.Kind == SyntaxKind.Paragraph)
        {
            var rest = markerEnd;
            while (rest < _lineEnd && Scanners.IsSpaceOrTab(_text[rest]))
            {
                rest++;
            }

            if (rest == _lineEnd)
            {
                return null;
            }
        }

        var markerOffset = _indent;
        var markerLength = markerEnd - markerStart;
        AdvanceNextNonspace();
        AdvanceOffset(markerLength, columns: true);
        var spacesStartColumn = _column;
        var spacesStartOffset = _offset;
        do
        {
            AdvanceOffset(1, columns: true);
            next = Peek(_offset);
        }
        while (_column - spacesStartColumn < 5 && Scanners.IsSpaceOrTab(next));

        var blankItem = _offset >= _lineEnd;
        var spacesAfterMarker = _column - spacesStartColumn;
        int padding;
        if (spacesAfterMarker >= 5 || spacesAfterMarker < 1 || blankItem)
        {
            // The content starts after one space: the other spaces are the indentation of an indented code block
            padding = markerLength + 1;
            _column = spacesStartColumn;
            _offset = spacesStartOffset;
            if (Scanners.IsSpaceOrTab(Peek(_offset)))
            {
                AdvanceOffset(1, columns: true);
            }
        }
        else
        {
            padding = markerLength + spacesAfterMarker;
        }

        return new ListData(ordered, ordered ? '\0' : c, start, delimiter, padding, markerOffset, markerStart, markerEnd) { Style = style };
    }

    private bool TryParseLetterMarker(int markerStart, bool romanAllowed, out int end, out MarkdownOrderedListStyle style, out int number)
    {
        var c = _text[markerStart];
        end = markerStart;
        if (IsRomanLetter(c) && romanAllowed)
        {
            var lower = char.IsAsciiLetterLower(c);
            while (end < _lineEnd && IsRomanLetter(_text[end]) && char.IsAsciiLetterLower(_text[end]) == lower)
            {
                end++;
            }

            style = lower ? MarkdownOrderedListStyle.LowerRoman : MarkdownOrderedListStyle.UpperRoman;
            number = RomanToNumber(_text.AsSpan(markerStart, end - markerStart));
        }
        else
        {
            end = markerStart + 1;
            style = char.IsAsciiLetterLower(c) ? MarkdownOrderedListStyle.LowerAlpha : MarkdownOrderedListStyle.UpperAlpha;
            number = (c | 0x20) - 'a' + 1;
        }

        return Peek(end) is '.' or ')';
    }

    // Markdig reads i, v and x only
    private static bool IsRomanLetter(char c) => c is 'i' or 'v' or 'x' or 'I' or 'V' or 'X';

    internal static int RomanToNumber(ReadOnlySpan<char> text)
    {
        var result = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var value = GetValue(text[i]);
            result += i + 1 < text.Length && value < GetValue(text[i + 1]) ? -value : value;
        }

        return result;

        static int GetValue(char c) => (c | 0x20) switch
        {
            'i' => 1,
            'v' => 5,
            _ => 10,
        };
    }

    private static bool ListsMatch(ListData? list, ListData item) => list is not null && list.Ordered == item.Ordered && list.Delimiter == item.Delimiter && list.BulletChar == item.BulletChar && list.Style == item.Style;

    private void AddLine()
    {
        var start = _offset;
        var virtualSpaces = 0;
        if (_partiallyConsumedTab)
        {
            // The rest of the columns of the tab are spaces of the content
            start++;
            virtualSpaces = 4 - (_column % 4);
        }

        AddLine(_tip, new Line(start, _lineEnd, _eolEnd, virtualSpaces));
    }

    private void AddLine(Block block, Line line)
    {
        if (block.LineCount == 0)
        {
            block.LineStart = _lineCount;
        }
        else if (block.LineStart + block.LineCount != _lineCount)
        {
            throw new InvalidOperationException($"The lines of the {block.Kind} are not the last ones.");
        }

        if (_lineCount == _lines.Length)
        {
            var lines = ArrayPool<Line>.Shared.Rent(_lineCount * 2);
            _lines.AsSpan(0, _lineCount).CopyTo(lines);
            ArrayPool<Line>.Shared.Return(_lines);
            _lines = lines;
        }

        _lines[_lineCount++] = line;
        block.LineCount++;
    }

    private ReadOnlySpan<Line> GetLines(Block block) => _lines.AsSpan(block.LineStart, block.LineCount);

    private Block AddChild(SyntaxKind kind)
    {
        // A definition that Markdig closed only takes the list that replaces one of its lists, which the list starts in Markdig
        var replacesList = false;
        while (!CanContain(_tip.Kind, kind) || (_tip == _closingDefinition && !(kind == SyntaxKind.List && replacesList)))
        {
            replacesList = _tip.Kind == SyntaxKind.List;
            Finalize(_tip);
        }

        var block = new Block(kind, _tip);
        _tip.AddChild(block);
        _tip.LastIsBlankLine = false;
        _tip = block;
        return block;
    }

    private void CloseUnmatchedBlocks()
    {
        if (!_allClosed)
        {
            while (_oldTip != _lastMatchedContainer)
            {
                var parent = _oldTip.Parent!;
                Finalize(_oldTip);
                _oldTip = parent;
            }

            _allClosed = true;
        }
    }

    private void Finalize(Block block)
    {
        block.Open = false;
        switch (block.Kind)
        {
            case SyntaxKind.Paragraph:
                ParseDefinitions(block);
                if (_syntaxExtensions?.ParagraphExtensions is not null)
                {
                    ConvertParagraphLines(block);
                }

                break;

            case SyntaxKind.ExtensionBlock or RawExtensionBlock:
                CloseFencedExtension(block);
                break;

            case SyntaxKind.GridTable:
                // In a cell that Markdig does not close, the grid table that ends it is kept, even when it has no row
                if (!(_keepsGridTable && _atEnd) && !IsGridTable(block))
                {
                    block.Kind = SyntaxKind.Paragraph;
                    block.GridLineCount = block.LineCount;
                    ParseDefinitions(block);
                    if (_syntaxExtensions?.ParagraphExtensions is not null)
                    {
                        ConvertParagraphLines(block);
                    }
                }

                break;

            case SyntaxKind.IndentedCodeBlock:
                // The blank lines at the end are not part of the code
                while (block.LineCount > 0 && _lines[block.LineStart + block.LineCount - 1] is var line && _text.AsSpan(line.Start, line.End - line.Start).Trim(" \t").IsEmpty)
                {
                    block.LineCount--;
                }

                break;
        }

        _tip = block.Parent ?? _document;
    }

    private bool IsGridTable(Block table)
    {
        var lines = new List<string>(table.LineCount);
        foreach (var line in GetLines(table))
        {
            lines.Add(_text[line.Start..line.End]);
        }

        return GridTableLayout.Read(lines) is not null;
    }

    // With auto-identifiers, a reference link whose label is the first line of a heading links to it, as in Markdig
    private void AddHeadingLabels(Block block)
    {
        foreach (var child in block.Children)
        {
            switch (child.Kind)
            {
                case SyntaxKind.AtxHeading:
                    if (GetLines(child) is [var line, ..])
                    {
                        AddHeadingLabel(_text, line.Start, line.End);
                    }

                    break;

                case SyntaxKind.SetextHeading:
                {
                    var subject = child.Subject!;
                    var end = subject.From(child.ContentStart).IndexOf('\n');
                    AddHeadingLabel(subject.Text, child.ContentStart, end < 0 ? subject.End : child.ContentStart + end);
                    break;
                }

                default:
                    AddHeadingLabels(child);
                    break;
            }
        }
    }

    private void AddHeadingLabel(string text, int start, int end)
    {
        var label = HasExtension(MarkdownExtensions.GenericAttributes) ? HeadingIdentifier.GetLabelWithoutAttributes(text, start, end) : text[start..end];
        // A label has a character that is not whitespace
        if (Scanners.NormalizeLabel(label) is { Length: > 0 } normalized)
        {
            _inlineParser.HeadingLabels.Add(normalized);
        }
    }

    // The link reference definitions at the start of a paragraph are blocks of their own
    private void ParseDefinitions(Block paragraph)
    {
        // The definitions read by an earlier call are at the start of the lines, whose characters are the same in the new
        // subject: the content starts at the same offset from the start of the subject
        var subject = Subject.FromLines(_text, GetLines(paragraph));
        var position = subject.Start + (paragraph.Subject is { } previous ? paragraph.ContentStart - previous.Start : 0);
        while (position < subject.End && subject.Text[position] == '[')
        {
            var definitions = paragraph.DefinitionsOrNull;
            var end = _inlineParser.ParseReference(subject, position, ref definitions);
            if (end < 0)
            {
                break;
            }

            paragraph.DefinitionsOrNull = definitions;
            position = end;
        }

        paragraph.ContentStart = position;
        paragraph.DefinitionLineCount = position == subject.End ? paragraph.LineCount : subject.Text.AsSpan(subject.Start, position - subject.Start).Count('\n');
        paragraph.Subject = subject;
        paragraph.IsEmpty = subject.From(position).Trim(" \t\n").IsEmpty;
    }

    private void Convert(Block block)
    {
        switch (block.Kind)
        {
            case SyntaxKind.ExtensionBlock or RawExtensionBlock:
            {
                // The opening fence and its info, the lines or the blocks, then the closing fence
                var node = _tree.StartNode(SyntaxKind.ExtensionBlock);
                _tree.AddRange(block.LeadingOrNull);
                if (block.InfoInlines && block.InfoRange is var (infoStart, infoEnd) && infoEnd > infoStart)
                {
                    _inlineParser.Parse(Subject.FromLines(_text, [new Line(infoStart, infoEnd, infoEnd, 0)]), _tree);
                }

                if (block.Kind == RawExtensionBlock)
                {
                    foreach (var line in GetLines(block))
                    {
                        _tree.AddLineToken(SyntaxKind.ExtensionLineToken, line.Start, line.EolEnd, line.VirtualSpaces);
                    }
                }
                else
                {
                    foreach (var child in block.Children)
                    {
                        Convert(child);
                    }
                }

                _tree.AddRange(block.TrailingOrNull);
                _tree.EndNode(node);
                return;
            }

            case SyntaxKind.Paragraph or SyntaxKind.SetextHeading:
                _tree.AddRange(block.DefinitionsOrNull);
                if (block.ExtensionSegments is { } segments)
                {
                    ConvertSegments(segments, trimEnd: block.GridLineCount != block.LineCount);
                    return;
                }

                if (block == _alertParagraph)
                {
                    // The kind of the alert is the first token of the paragraph, and its content starts on the next line
                    var node = _tree.StartNode(block.Kind);
                    _tree.AddToken(SyntaxKind.AlertKindToken, block.Subject!.ToSourceStart(_alertKind.KindStart), block.Subject.ToSourceEnd(_alertKind.KindEnd));
                    _inlineParser.Parse(block.Subject, _alertKind.ContentStart, _tree);
                    _tree.EndNode(node);
                    _alertParagraph = null;
                }
                else if (block.Kind == SyntaxKind.Paragraph && !block.IsEmpty && HasExtension(MarkdownExtensions.PipeTables) && block.Subject!.From(block.ContentStart).Contains('|'))
                {
                    ConvertPipeTables(block.Subject, block.ContentStart, trimEnd: block.GridLineCount != block.LineCount);
                }
                else if (block.Kind == SyntaxKind.SetextHeading || !block.IsEmpty)
                {
                    var node = _tree.StartNode(block.Kind);
                    _inlineParser.Parse(block.Subject!, block.ContentStart, _tree, trimEnd: block.GridLineCount != block.LineCount);
                    _tree.AddRange(block.TrailingOrNull);
                    _tree.EndNode(node);
                }

                return;

            case SyntaxKind.AtxHeading:
            {
                var node = _tree.StartNode(block.Kind);
                _tree.AddRange(block.LeadingOrNull);
                _inlineParser.Parse(Subject.FromLines(_text, GetLines(block)), _tree);
                _tree.AddRange(block.TrailingOrNull);
                _tree.EndNode(node);
                return;
            }

            case SyntaxKind.Table:
                ConvertTable(block);
                return;

            case SyntaxKind.Figure:
            {
                // The opening fence and its caption, the blocks, then the closing fence and its caption
                var node = _tree.StartNode(block.Kind);
                _tree.AddRange(block.LeadingOrNull);
                AddCaption(block.OpeningCaption);
                foreach (var child in block.Children)
                {
                    Convert(child);
                }

                _tree.AddRange(block.TrailingOrNull);
                AddCaption(block.ClosingCaption);
                _tree.EndNode(node);
                return;
            }

            case SyntaxKind.DefinitionItem:
            {
                // The terms, the marker, whose value is the number of blank lines that Markdig leaves in the definition, and the blocks
                var node = _tree.StartNode(block.Kind);
                var terms = _lines.AsSpan(block.TermLineStart, block.TermLineCount);
                for (var i = 0; i < terms.Length; i++)
                {
                    // Markdig keeps the whitespace at the end of a term, except at the end of a paragraph that was closed
                    var term = _tree.StartNode(SyntaxKind.DefinitionTerm);
                    _inlineParser.Parse(Subject.FromLines(_text, [terms[i]]), _tree, trimEnd: block.TermsTrimmed && i == terms.Length - 1);
                    _tree.EndNode(term);
                }

                _tree.AddToken(SyntaxKind.DefinitionMarkerToken, block.MarkerPosition, block.MarkerPosition + 1, value: block.BlankLineCount);
                foreach (var child in block.Children)
                {
                    Convert(child);
                }

                _tree.EndNode(node);
                return;
            }

            case SyntaxKind.FencedCodeBlock or SyntaxKind.IndentedCodeBlock or SyntaxKind.HtmlBlock or SyntaxKind.FrontMatter or SyntaxKind.MathBlock or SyntaxKind.GridTable:
            {
                var node = _tree.StartNode(block.Kind);
                _tree.AddRange(block.LeadingOrNull);
                var kind = block.Kind switch
                {
                    SyntaxKind.HtmlBlock => SyntaxKind.HtmlLineToken,
                    SyntaxKind.FrontMatter => SyntaxKind.FrontMatterLineToken,
                    SyntaxKind.MathBlock => SyntaxKind.MathLineToken,
                    SyntaxKind.GridTable => SyntaxKind.GridTableLineToken,
                    _ => SyntaxKind.CodeLineToken,
                };
                foreach (var line in GetLines(block))
                {
                    _tree.AddLineToken(kind, line.Start, line.EolEnd, line.VirtualSpaces);
                }

                _tree.AddRange(block.TrailingOrNull);
                _tree.EndNode(node);
                return;
            }

            default:
            {
                // A block quote whose paragraph starts with [!KIND] on its own line is an alert
                var alert = block.Kind == SyntaxKind.BlockQuote && HasExtension(MarkdownExtensions.Alerts) && !_inAlert && (_allowNestedAlerts || block.Parent == _document) && FindAlertParagraph(block);
                var node = _tree.StartNode(alert ? SyntaxKind.Alert : block.Kind);
                _tree.AddRange(block.LeadingOrNull);
                _inAlert |= alert;
                foreach (var child in block.Children)
                {
                    Convert(child);
                }

                _inAlert &= !alert;
                _tree.AddRange(block.TrailingOrNull);
                _tree.EndNode(node);
                return;
            }
        }
    }

    // The opening fence of a fenced block extension, the first one of the character that reads the line: the rest of the line is
    // the info, and the lines of the block follow
    private bool TryStartFencedExtension(Block container, MarkdownFencedBlockExtension[] extensions)
    {
        var interruptsParagraph = container.Kind == SyntaxKind.Paragraph || (!_allClosed && !_blank && _tip.Kind == SyntaxKind.Paragraph);
        var line = _text.AsSpan(_nextNonspace, _lineEnd - _nextNonspace);
        foreach (var extension in extensions)
        {
            if (interruptsParagraph && !extension.CanInterruptParagraph)
            {
                continue;
            }

            var length = extension.TryOpen(line);
            if (length <= 0)
            {
                continue;
            }

            if (length > line.Length)
            {
                throw new InvalidOperationException($"The fenced block extension '{extension.Name}' read an opening fence of {length} characters on a line of {line.Length}.");
            }

            var index = _syntaxExtensions!.GetIndex(extension);
            CloseUnmatchedBlocks();
            var raw = extension.Content == MarkdownFencedBlockContent.RawLines;
            var block = AddChild(raw ? RawExtensionBlock : SyntaxKind.ExtensionBlock);
            block.ExtensionIndex = index;
            block.InfoInlines = extension.ParseInfoAsInlines;
            block.FenceOffset = _indent;
            AdvanceNextNonspace();
            var fenceEnd = _offset + length;
            block.OpeningFence = (_offset, length);
            block.Leading.Add(BuildElement.Token(SyntaxKind.ExtensionFenceToken, _offset, fenceEnd, value: index + 1));

            // The info is the rest of the line, without the whitespace around it
            var infoStart = fenceEnd;
            var infoEnd = _lineEnd;
            while (infoStart < infoEnd && Scanners.IsSpaceOrTab(_text[infoStart]))
            {
                infoStart++;
            }

            while (infoEnd > infoStart && Scanners.IsSpaceOrTab(_text[infoEnd - 1]))
            {
                infoEnd--;
            }

            block.InfoRange = (infoStart, infoEnd);
            if (infoEnd > infoStart && !block.InfoInlines)
            {
                block.Leading.Add(BuildElement.Token(SyntaxKind.InfoStringToken, infoStart, infoEnd));
            }

            // The rest of the line is the opening fence: no block starts after it
            AdvanceOffset(_lineEnd - _offset, columns: false);
            _skipAddLine = raw;
            return true;
        }

        return false;
    }

    // A closing fence closes the block and the blocks in it, even a code block. The indentation of the opening fence is removed
    // from the raw lines, as from the lines of a fenced code block.
    private int ContinueFencedExtension(Block block)
    {
        var extension = (MarkdownFencedBlockExtension)_syntaxExtensions!.Extensions[block.ExtensionIndex];
        if (_indent <= 3 && !_blank)
        {
            var line = _text.AsSpan(_nextNonspace, _lineEnd - _nextNonspace);
            var (openingStart, openingLength) = block.OpeningFence;
            var length = extension.TryClose(line, _text.AsSpan(openingStart, openingLength));
            if (length > line.Length)
            {
                throw new InvalidOperationException($"The fenced block extension '{extension.Name}' read a closing fence of {length} characters on a line of {line.Length}.");
            }

            if (length > 0 && line[length..].Trim(" \t").IsEmpty)
            {
                while (_tip != block)
                {
                    Finalize(_tip);
                }

                block.Trailing.Add(BuildElement.Token(SyntaxKind.ExtensionFenceToken, _nextNonspace, _nextNonspace + length));
                Finalize(block);
                return 2;
            }
        }

        if (block.Kind == RawExtensionBlock)
        {
            for (var i = block.FenceOffset; i > 0 && Scanners.IsSpaceOrTab(Peek(_offset)); i--)
            {
                AdvanceOffset(1, columns: true);
            }
        }

        return 0;
    }

    // The block of a fenced block extension is closed: the extension can record it in its state
    private void CloseFencedExtension(Block block)
    {
        var extension = (MarkdownFencedBlockExtension)_syntaxExtensions!.Extensions[block.ExtensionIndex];
        var (openingStart, openingLength) = block.OpeningFence;
        var (infoStart, infoEnd) = block.InfoRange;
        var context = new MarkdownFencedBlockContext(_text, _text.AsSpan(openingStart, openingLength), _text.AsSpan(infoStart, infoEnd - infoStart), block.Kind == RawExtensionBlock ? GetLines(block) : default, _options, _states, block.ExtensionIndex);
        extension.OnClosed(ref context);
    }

    // Offers the lines of a closed paragraph to the paragraph extensions: the lines that one takes are a block of the extension,
    // the lines before them stay a paragraph, and the lines after them are offered again
    private void ConvertParagraphLines(Block paragraph)
    {
        if (_syntaxExtensions?.ParagraphExtensions is not { } extensions || paragraph.IsEmpty)
        {
            return;
        }

        // The content starts after the link reference definitions, on the first line that ends after its start: the content
        // can start at the line ending of the last definition, which the definition count of the paragraph does not count
        var end = paragraph.LineStart + paragraph.LineCount;
        var contentStart = paragraph.Subject!.ToSourceStart(paragraph.ContentStart);
        var first = paragraph.LineStart;
        while (first < end && _lines[first].End <= contentStart)
        {
            first++;
        }

        List<ParagraphSegment>? segments = null;
        while (first < end)
        {
            var lines = _lines.AsSpan(first, end - first);
            ParagraphSegment? found = null;
            foreach (var extension in extensions)
            {
                _paragraphRanges.Clear();
                var index = _syntaxExtensions.GetIndex(extension);
                var context = new MarkdownParagraphConvertContext(_text, lines, _options, _paragraphRanges, _states, index);
                if (!extension.TryConvert(ref context))
                {
                    continue;
                }

                if (context.CompletedLineCount == 0)
                {
                    throw new InvalidOperationException($"The paragraph extension '{extension.Name}' read a block without calling {nameof(MarkdownParagraphConvertContext.Complete)}.");
                }

                var ranges = new ParagraphExtensionRange[_paragraphRanges.Count];
                for (var i = 0; i < ranges.Length; i++)
                {
                    ranges[i] = _paragraphRanges[i] with { Line = _paragraphRanges[i].Line + first };
                }

                found = new ParagraphSegment(index, first + context.FirstLine, context.CompletedLineCount, ranges);
                break;
            }

            if (found is null)
            {
                break;
            }

            segments ??= [];
            if (found.LineStart > first)
            {
                segments.Add(ParagraphSegment.Paragraph(first, found.LineStart - first));
            }

            segments.Add(found);
            first = found.LineStart + found.LineCount;
        }

        if (segments is null)
        {
            return;
        }

        if (first < end)
        {
            segments.Add(ParagraphSegment.Paragraph(first, end - first));
        }

        paragraph.ExtensionSegments = segments;
    }

    // A paragraph that paragraph extensions split: their blocks, and the paragraphs around them
    // A paragraph that comes from a grid table keeps the whitespace at its end, as Markdig does: its last segment keeps it
    private void ConvertSegments(List<ParagraphSegment> segments, bool trimEnd)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Ranges is null)
            {
                ConvertParagraphSegment(_lines.AsSpan(segment.LineStart, segment.LineCount), trimEnd || i < segments.Count - 1);
            }
            else
            {
                ConvertExtensionSegment(segment);
            }
        }
    }

    // Lines of a paragraph that a paragraph extension split, which are a paragraph, or a pipe table
    private void ConvertParagraphSegment(ReadOnlySpan<Line> lines, bool trimEnd)
    {
        var subject = Subject.FromLines(_text, lines);
        if (HasExtension(MarkdownExtensions.PipeTables) && subject.From(subject.Start).Contains('|'))
        {
            ConvertPipeTables(subject, subject.Start, trimEnd);
            return;
        }

        var node = _tree.StartNode(SyntaxKind.Paragraph);
        _inlineParser.Parse(subject, _tree, trimEnd);
        _tree.EndNode(node);
    }

    // The block of a paragraph extension: its parts, line by line, and text for the characters between them that are not
    // whitespace. The first token has the index of the extension: the first part is a marker or text.
    private void ConvertExtensionSegment(ParagraphSegment segment)
    {
        var node = _tree.StartNode(SyntaxKind.ExtensionBlock);
        var ranges = segment.Ranges!;
        var first = true;
        var rangeIndex = 0;
        for (var lineIndex = segment.LineStart; lineIndex < segment.LineStart + segment.LineCount; lineIndex++)
        {
            var line = _lines[lineIndex];
            var position = line.Start;
            for (; rangeIndex < ranges.Length && ranges[rangeIndex].Line == lineIndex; rangeIndex++)
            {
                var range = ranges[rangeIndex];
                var start = line.Start + range.Start;
                var end = start + range.Length;
                AddExtensionText(position, start, segment.ExtensionIndex, ref first);
                if (range.Kind == ExtensionRangeKind.Inlines)
                {
                    _inlineParser.Parse(Subject.FromLines(_text, [new Line(start, end, end, 0)]), _tree);
                }
                else
                {
                    _tree.AddToken(range.Kind == ExtensionRangeKind.Marker ? SyntaxKind.ExtensionMarkerToken : SyntaxKind.ExtensionTextToken, start, end, value: first ? segment.ExtensionIndex + 1 : 0);
                    first = false;
                }

                position = end;
            }

            AddExtensionText(position, line.End, segment.ExtensionIndex, ref first);
        }

        _tree.EndNode(node);
    }

    private void AddExtensionText(int start, int end, int index, ref bool first)
    {
        while (start < end && Scanners.IsSpaceOrTab(_text[start]))
        {
            start++;
        }

        while (end > start && Scanners.IsSpaceOrTab(_text[end - 1]))
        {
            end--;
        }

        if (end > start)
        {
            _tree.AddToken(SyntaxKind.ExtensionTextToken, start, end, value: first ? index + 1 : 0);
            first = false;
        }
    }

    // The first paragraph of the quote that starts with [!, letters, and ], followed by spaces and a line break or its end
    private bool FindAlertParagraph(Block quote)
    {
        foreach (var child in quote.Children)
        {
            if (child.Kind != SyntaxKind.Paragraph || child.IsEmpty || child.ExtensionSegments is not null || child.Subject is not { } subject)
            {
                continue;
            }

            var text = subject.Text;
            var end = subject.End;
            var start = child.ContentStart;
            while (start < end && text[start] is ' ' or '\t')
            {
                start++;
            }

            var position = start + 2;
            if (position > end || text[start] != '[' || text[start + 1] != '!')
            {
                continue;
            }

            while (position < end && char.IsAsciiLetter(text[position]))
            {
                position++;
            }

            if (position == start + 2 || position >= end || text[position] != ']')
            {
                continue;
            }

            var kindEnd = position + 1;
            position = kindEnd;
            while (position < end && text[position] is ' ' or '\t')
            {
                position++;
            }

            if (position < end && text[position] != '\n')
            {
                continue;
            }

            _alertParagraph = child;
            _alertKind = (start, kindEnd, position);
            return true;
        }

        return false;
    }

    private void AddCaption((int Start, int End) caption)
    {
        if (caption.End > caption.Start)
        {
            var node = _tree.StartNode(SyntaxKind.FigureCaption);
            _inlineParser.Parse(Subject.FromLines(_text, [new Line(caption.Start, caption.End, caption.End, 0)]), _tree, trimEnd: false);
            _tree.EndNode(node);
        }
    }

    // A paragraph whose lines have pipe tables: the lines before a table are a paragraph, and the lines after it are a paragraph,
    // which can have a table, as in Markdig
    // trimEnd: whether the whitespace at the end of the lines is not text, which it is after lines that are not a grid table
    private void ConvertPipeTables(Subject subject, int start, bool trimEnd)
    {
        // The paragraph has the inlines of its lines when they are not a table: the scan adds them
        var node = _tree.StartNode(SyntaxKind.Paragraph);
        while (_inlineParser.ScanPipeTable(subject, start, _tree, trimEnd) is { } scan)
        {
            _tree.Truncate(node);
            _inlineParser.PipeTableQuotes = scan.Quotes;
            try
            {
                if (!subject.Text.AsSpan(start, scan.LeadingEnd - start).Trim(" \t\n\v\f\r").IsEmpty)
                {
                    var paragraph = _tree.StartNode(SyntaxKind.Paragraph);
                    _inlineParser.ParseBeforePipeTable(subject, start, scan.LeadingEnd, scan.SplitLineBreak, _tree);
                    _tree.EndNode(paragraph);
                }

                ConvertPipeTable(subject, scan);
            }
            finally
            {
                _inlineParser.PipeTableQuotes = null;
            }

            // The text after the table is a paragraph unless it is whitespace that is trivia: Unicode whitespace is text
            if (scan.TrailingStart < 0 || subject.From(scan.TrailingStart).Trim(" \t\n\v\f\r").IsEmpty)
            {
                return;
            }

            start = scan.TrailingStart;
            node = _tree.StartNode(SyntaxKind.Paragraph);
        }

        _tree.EndNode(node);
    }

    private void ConvertPipeTable(Subject subject, PipeTableScan scan)
    {
        var node = _tree.StartNode(SyntaxKind.PipeTable);
        var elements = new List<(int Start, int End, int Kind)>();
        for (var i = 0; i < scan.Rows.Count; i++)
        {
            var row = scan.Rows[i];
            var rowNode = _tree.StartNode(i switch
            {
                _ when !scan.HasDelimiterRow => SyntaxKind.TableRow,
                0 => SyntaxKind.TableHeaderRow,
                1 => SyntaxKind.TableDelimiterRow,
                _ => SyntaxKind.TableRow,
            });

            // The pipes (0), the cells (1) and the generic attributes (2), in the order of the text
            elements.Clear();
            elements.AddRange(row.Pipes.Select(pipe => (pipe, pipe + 1, 0)));
            elements.AddRange(row.Cells.Select(cell => (cell.Start, cell.End, 1)));
            elements.AddRange(row.Attributes.Select(attributes => (attributes.Start, attributes.End, 2)));
            elements.Sort();
            var position = row.Start;
            foreach (var (start, end, kind) in elements)
            {
                AddText(subject, _tree, position, start);
                position = Math.Max(position, end);
                switch (kind)
                {
                    case 0:
                        _tree.AddToken(SyntaxKind.PipeToken, subject.ToSourceStart(start), subject.ToSourceEnd(end));
                        break;
                    case 1 when i == 1 && scan.HasDelimiterRow:
                        _tree.AddToken(SyntaxKind.TableDelimiterToken, subject.ToSourceStart(start), subject.ToSourceEnd(end));
                        break;
                    case 1:
                        var cell = _tree.StartNode(SyntaxKind.TableCell);
                        if (end > start)
                        {
                            _inlineParser.ParseCell(subject, start, end, _tree);
                        }

                        _tree.EndNode(cell);
                        break;
                    default:
                        _inlineParser.ParseCell(subject, start, end, _tree);
                        break;
                }
            }

            AddText(subject, _tree, position, row.End);
            _tree.EndNode(rowNode);
        }

        _tree.EndNode(node);

        // The text of the row that is in no cell, such as the Unicode whitespace that Markdig trims around a cell, is a text
        // token of the row, which the renderer does not write
        static void AddText(Subject subject, TreeBuffer row, int start, int end)
        {
            var text = subject.Text;
            var lineStart = start;
            for (var i = start; i <= end; i++)
            {
                if (i < end && text[i] != '\n')
                {
                    continue;
                }

                var tokenStart = lineStart;
                var tokenEnd = i;
                while (tokenStart < tokenEnd && text[tokenStart] is ' ' or '\t' or '\v' or '\f' or '\r')
                {
                    tokenStart++;
                }

                while (tokenEnd > tokenStart && text[tokenEnd - 1] is ' ' or '\t' or '\v' or '\f' or '\r')
                {
                    tokenEnd--;
                }

                if (tokenEnd > tokenStart)
                {
                    row.AddToken(SyntaxKind.TextToken, subject.ToSourceStart(tokenStart), subject.ToSourceEnd(tokenEnd));
                }

                lineStart = i + 1;
            }
        }
    }

    // A table: its header row, its delimiter row, and its other rows. The pipes are tokens of the rows, and the content of a
    // cell is its inlines.
    private void ConvertTable(Block table)
    {
        var node = _tree.StartNode(SyntaxKind.Table);
        var cells = new List<(int Start, int End)>();
        var pipes = new List<int>();
        for (var i = 0; i < table.LineCount; i++)
        {
            var line = _lines[table.LineStart + i];
            cells.Clear();
            pipes.Clear();
            var row = _tree.StartNode(i switch
            {
                0 => SyntaxKind.TableHeaderRow,
                1 => SyntaxKind.TableDelimiterRow,
                _ => SyntaxKind.TableRow,
            });

            if (i == 1)
            {
                TableScanner.ScanDelimiterRow(_text, line.Start, line.End, cells, pipes);
            }
            else
            {
                TableScanner.ScanRow(_text, line.Start, line.End, cells, pipes);
            }

            // The pipes and the cells, in the order of the text
            var pipeIndex = 0;
            foreach (var (start, end) in cells)
            {
                while (pipeIndex < pipes.Count && pipes[pipeIndex] < start)
                {
                    _tree.AddToken(SyntaxKind.PipeToken, pipes[pipeIndex], pipes[pipeIndex] + 1);
                    pipeIndex++;
                }

                if (i == 1)
                {
                    _tree.AddToken(SyntaxKind.TableDelimiterToken, start, end);
                }
                else
                {
                    var cell = _tree.StartNode(SyntaxKind.TableCell);
                    if (end > start)
                    {
                        _inlineParser.Parse(Subject.FromTableCell(_text, start, end), _tree);
                    }

                    _tree.EndNode(cell);
                }
            }

            for (; pipeIndex < pipes.Count; pipeIndex++)
            {
                _tree.AddToken(SyntaxKind.PipeToken, pipes[pipeIndex], pipes[pipeIndex] + 1);
            }

            _tree.EndNode(row);
        }

        _tree.EndNode(node);
    }

    private void FindNextNonspace()
    {
        var i = _offset;
        var columns = _column;
        while (i < _lineEnd)
        {
            var c = _text[i];
            if (c == ' ')
            {
                i++;
                columns++;
            }
            else if (c == '\t')
            {
                i++;
                columns += 4 - (columns % 4);
            }
            else
            {
                break;
            }
        }

        _blank = i >= _lineEnd;
        _nextNonspace = i;
        _nextNonspaceColumn = columns;
        _indent = _nextNonspaceColumn - _column;
        _indented = _indent >= CodeIndent;
    }

    private void AdvanceNextNonspace()
    {
        _offset = _nextNonspace;
        _column = _nextNonspaceColumn;
        _partiallyConsumedTab = false;
    }

    // Advances by characters or by columns: a tab that is wider than the columns left is taken in part
    private void AdvanceOffset(int count, bool columns)
    {
        while (count > 0 && _offset < _lineEnd)
        {
            var c = _text[_offset];
            if (c == '\t')
            {
                var charsToTab = 4 - (_column % 4);
                if (columns)
                {
                    _partiallyConsumedTab = charsToTab > count;
                    var charsToAdvance = Math.Min(charsToTab, count);
                    _column += charsToAdvance;
                    _offset += _partiallyConsumedTab ? 0 : 1;
                    count -= charsToAdvance;
                }
                else
                {
                    _partiallyConsumedTab = false;
                    _column += charsToTab;
                    _offset++;
                    count--;
                }
            }
            else
            {
                _partiallyConsumedTab = false;
                _offset++;
                _column++;
                count--;
            }
        }
    }

    private sealed class Block(SyntaxKind kind, Block? parent)
    {
        public SyntaxKind Kind { get; set; } = kind;

        public Block? Parent { get; } = parent;

        // The number of ancestors: the document is at depth 0
        public int Depth { get; } = parent is null ? 0 : parent.Depth + 1;

        private static readonly List<Block> NoChildren = [];

        private List<Block>? _children;

        // The fields that few blocks use, apart so that the thousands of paragraphs and items of a document are small
        private BlockExtra? _extra;

        // The children: a leaf block has none, and allocates no list
        public List<Block> Children => _children ?? NoChildren;

        public Block? LastChild => _children is { Count: > 0 } children ? children[^1] : null;

        private BlockExtra Extra => _extra ??= new();

        public void AddChild(Block child) => (_children ??= []).Add(child);

        public void InsertChild(int index, Block child) => (_children ??= []).Insert(index, child);

        public void RemoveChild(Block child) => _children?.Remove(child);

        public bool Open { get; set; } = true;

        // The tokens of the block before and after its children or its lines, created when the block has some
        public List<BuildElement> Leading => LeadingOrNull ??= new(2);

        public List<BuildElement> Trailing => TrailingOrNull ??= new(1);

        public List<BuildElement>? LeadingOrNull { get; private set; }

        public List<BuildElement>? TrailingOrNull { get; private set; }

        // The range of the lines of a leaf block in the lines of the parser
        public int LineStart { get; set; }

        public int LineCount { get; set; }

        public ListData? ListData { get; set; }

        public char FenceChar
        {
            get => _extra is null ? default : _extra.FenceChar;
            set => Extra.FenceChar = value;
        }

        public int FenceLength
        {
            get => _extra is null ? default : _extra.FenceLength;
            set => Extra.FenceLength = value;
        }

        public int FenceOffset
        {
            get => _extra is null ? default : _extra.FenceOffset;
            set => Extra.FenceOffset = value;
        }

        public int HtmlBlockType
        {
            get => _extra is null ? default : _extra.HtmlBlockType;
            set => Extra.HtmlBlockType = value;
        }

        // A definition: its terms, which are lines of the paragraph before it, where its marker is, and the blank lines that Markdig
        // leaves in it, the last one being after its last block
        public int TermLineStart
        {
            get => _extra is null ? default : _extra.TermLineStart;
            set => Extra.TermLineStart = value;
        }

        // The captions of a figure, after its fences
        public (int Start, int End) OpeningCaption
        {
            get => _extra is null ? default : _extra.OpeningCaption;
            set => Extra.OpeningCaption = value;
        }

        public (int Start, int End) ClosingCaption
        {
            get => _extra is null ? default : _extra.ClosingCaption;
            set => Extra.ClosingCaption = value;
        }

        public int TermLineCount
        {
            get => _extra is null ? default : _extra.TermLineCount;
            set => Extra.TermLineCount = value;
        }

        public bool TermsTrimmed
        {
            get => _extra is null ? default : _extra.TermsTrimmed;
            set => Extra.TermsTrimmed = value;
        }

        public int MarkerPosition
        {
            get => _extra is null ? default : _extra.MarkerPosition;
            set => Extra.MarkerPosition = value;
        }

        public int BlankLineCount
        {
            get => _extra is null ? default : _extra.BlankLineCount;
            set => Extra.BlankLineCount = value;
        }

        public bool LastIsBlankLine { get; set; }

        // A footnote whose last line is blank, which a line that is not indented ends
        public bool FootnoteLastLineBlank
        {
            get => _extra is null ? default : _extra.FootnoteLastLineBlank;
            set => Extra.FootnoteLastLineBlank = value;
        }

        // A paragraph whose last line was not the header row of a table: GFM tries a paragraph once
        public bool TableVisited { get; set; }

        // A table: the number of cells of its delimiter row, and the empty cells added to its short rows
        public int TableColumnCount
        {
            get => _extra is null ? default : _extra.TableColumnCount;
            set => Extra.TableColumnCount = value;
        }

        public long TableAutocompletedCells
        {
            get => _extra is null ? default : _extra.TableAutocompletedCells;
            set => Extra.TableAutocompletedCells = value;
        }

        // For a paragraph whose first lines are not a grid table, their number
        public int GridLineCount { get; set; } = -1;

        // A paragraph: its link reference definitions, then its inlines from ContentStart in its subject
        public List<BuildElement>? DefinitionsOrNull { get; set; }

        public int ContentStart { get; set; }

        // The lines that the link reference definitions take
        public int DefinitionLineCount { get; set; }

        public Subject? Subject { get; set; }

        public bool IsEmpty { get; set; }

        // A block of a fenced block extension: the index of the extension, its opening fence, and its info, which can be read as
        // inlines
        public int ExtensionIndex
        {
            get => _extra is null ? default : _extra.ExtensionIndex;
            set => Extra.ExtensionIndex = value;
        }

        public bool InfoInlines
        {
            get => _extra is not null && _extra.InfoInlines;
            set => Extra.InfoInlines = value;
        }

        public (int Start, int Length) OpeningFence
        {
            get => _extra is null ? default : _extra.OpeningFence;
            set => Extra.OpeningFence = value;
        }

        public (int Start, int End) InfoRange
        {
            get => _extra is null ? default : _extra.InfoRange;
            set => Extra.InfoRange = value;
        }

        // A paragraph that paragraph extensions split: its blocks and its paragraphs
        public List<ParagraphSegment>? ExtensionSegments
        {
            get => _extra?.ExtensionSegments;
            set => Extra.ExtensionSegments = value;
        }
    }

    private sealed class BlockExtra
    {
        public int ExtensionIndex { get; set; }

        public bool InfoInlines { get; set; }

        public (int Start, int Length) OpeningFence { get; set; }

        public (int Start, int End) InfoRange { get; set; }

        public List<ParagraphSegment>? ExtensionSegments { get; set; }

        public char FenceChar { get; set; }

        public int FenceLength { get; set; }

        public int FenceOffset { get; set; }

        public int HtmlBlockType { get; set; }

        public int TermLineStart { get; set; }

        public (int Start, int End) OpeningCaption { get; set; }

        public (int Start, int End) ClosingCaption { get; set; }

        public int TermLineCount { get; set; }

        public bool TermsTrimmed { get; set; }

        public int MarkerPosition { get; set; }

        public int BlankLineCount { get; set; }

        public bool FootnoteLastLineBlank { get; set; }

        public int TableColumnCount { get; set; }

        public long TableAutocompletedCells { get; set; }
    }

    private sealed record ListData(bool Ordered, char BulletChar, int Start, char Delimiter, int Padding, int MarkerOffset, int MarkerStart, int MarkerEnd)
    {
        public MarkdownOrderedListStyle Style { get; init; }
    }
}
