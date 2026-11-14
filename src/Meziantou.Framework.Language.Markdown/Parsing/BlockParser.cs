namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>
/// Parses the blocks of a document line by line, as the CommonMark reference implementation (commonmark.js) does. The
/// marker that opens a container (a <c>&gt;</c>, a list marker) is a token of the container; the markers and the indentation
/// that continue it on the next lines are trivia, whose kind is recorded here. A leaf block keeps the ranges of its lines.
/// </summary>
internal sealed class BlockParser
{
    private const int CodeIndent = 4;

    private readonly string _text;
    private readonly MarkdownExtensions _extensions;
    private readonly bool _disableHtml;
    private readonly bool _allowNestedAlerts;

    // The alert being converted, which a quote in it cannot be, and the paragraph that starts with its kind
    private bool _inAlert;
    private Block? _alertParagraph;
    private (int KindStart, int KindEnd, int ContentStart) _alertKind;
    private readonly List<TriviaSpan> _triviaSpans = [];

    // The lines of all the leaf blocks: a block has a range of them. Only the last open leaf block gets lines, so its lines
    // are always the last ones.
    private readonly List<Line> _lines;
    private readonly Dictionary<string, bool> _references = new(StringComparer.Ordinal);
    private readonly InlineParser _inlineParser;
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

    private BlockParser(string text, MarkdownParseOptions options)
    {
        _text = text;
        _extensions = options.Extensions;
        _disableHtml = options.DisableHtml;
        _allowNestedAlerts = options.AllowNestedAlerts;

        // A block has at most every line of the text
        _lines = new List<Line>(text.AsSpan().Count('\n') + 1);
        _tip = _document;
        _oldTip = _document;
        _lastMatchedContainer = _document;
        _inlineParser = new InlineParser(_references, options) { MaximumAbbreviationExpansion = Math.Max(100_000, text.Length) };
    }

    public static (BuildNode Document, List<TriviaSpan> TriviaSpans) Parse(string text, MarkdownParseOptions options)
    {
        var parser = new BlockParser(text, options);
        var position = parser.HasExtension(MarkdownExtensions.YamlFrontMatter) ? parser.ParseFrontMatter() : 0;
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

            parser.IncorporateLine(position, lineEnd, eolEnd);
            position = eolEnd;
        }

        while (parser._tip is { } tip && tip != parser._document)
        {
            parser.Finalize(tip);
        }

        parser.Finalize(parser._document);
        if (parser.HasExtension(MarkdownExtensions.AutoIdentifiers) && options.AutoIdentifierOptions.HasFlag(MarkdownAutoIdentifierOptions.AutoLink))
        {
            parser.AddHeadingLabels(parser._document);
        }

        var document = new List<BuildElement>(1);
        parser.Convert(parser._document, document);
        return (document[0].Node!, parser._triviaSpans);
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
                _document.Children.Add(block);
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
            if (!_indented && !IsMaybeSpecial(Peek(_nextNonspace)) && !IsMaybeExtension(container, Peek(_nextNonspace)))
            {
                AdvanceNextNonspace();
                break;
            }

            var result = TryStart(container);
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
            // A lazy continuation line of a paragraph
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

    private static bool AcceptsLines(SyntaxKind kind) => kind is SyntaxKind.Paragraph or SyntaxKind.IndentedCodeBlock or SyntaxKind.FencedCodeBlock or SyntaxKind.MathBlock or SyntaxKind.HtmlBlock;

    private static bool CanContain(SyntaxKind parent, SyntaxKind child) => parent switch
    {
        SyntaxKind.Document or SyntaxKind.BlockQuote or SyntaxKind.ListItem or SyntaxKind.FootnoteDefinition or SyntaxKind.CustomContainer => child != SyntaxKind.ListItem,
        SyntaxKind.List => child == SyntaxKind.ListItem,
        _ => false,
    };

    private static bool IsMaybeSpecial(char c) => c is '#' or '`' or '~' or '*' or '+' or '_' or '=' or '<' or '>' or '-' or (>= '0' and <= '9');

    // Whether the line can start a block of an extension: a delimiter row under a paragraph, a row of a table, or the task
    // list item marker after the marker of a list item
    private bool IsMaybeExtension(Block container, char c) => (c == '$' && HasExtension(MarkdownExtensions.Mathematics)) || (c == ':' && HasExtension(MarkdownExtensions.CustomContainers)) || (c == '[' && IsFootnoteContainer(container)) || container.Kind switch
    {
        SyntaxKind.Paragraph => HasExtension(MarkdownExtensions.Tables) && c is '|' or ':',
        SyntaxKind.Table => true,
        SyntaxKind.ListItem => HasExtension(MarkdownExtensions.TaskLists) && c == '[' && container == _itemOpenedOnLine,
        _ => false,
    };

    private bool HasExtension(MarkdownExtensions extension) => _extensions.HasFlag(extension);

    // 0: the line continues the block, 1: it does not, 2: the block takes the whole line
    private int Continue(Block block)
    {
        switch (block.Kind)
        {
            case SyntaxKind.Document or SyntaxKind.List:
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

                if (leaf.Kind is not (SyntaxKind.FencedCodeBlock or SyntaxKind.MathBlock))
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

            case SyntaxKind.Table:
                // A row has at least one cell: a line with only a pipe ends the table
                return _blank || TableScanner.ScanRow(_text, _nextNonspace, _lineEnd, cells: null, pipes: null) == 0 ? 1 : 0;

            default:
                return 1;
        }
    }

    // 0: no block starts, 1: a container starts, 2: a leaf block starts
    private int TryStart(Block container)
    {
        var c = Peek(_nextNonspace);

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
            item.Leading.Add(BuildElement.Token(SyntaxKind.ListMarkerToken, data.MarkerStart, data.MarkerEnd));
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
        if (HasExtension(MarkdownExtensions.Tables) && container.Kind == SyntaxKind.Paragraph && !container.TableVisited && TryStartTable(container))
        {
            return 2;
        }

        // A row of a table
        if (container.Kind == SyntaxKind.Table && !_blank)
        {
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
            parent.Children.Insert(parent.Children.Count - 1, before);
            ParseDefinitions(before);
        }

        // The header row is the last line of the paragraph, so the delimiter row is next to it
        paragraph.Kind = SyntaxKind.Table;
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
            block.Trailing.Add(attributes);
        }

        AddLine(block, new Line(_offset, contentEnd, contentEnd, 0));
        _offset = _lineEnd;
        return true;
    }

    // The generic attributes of a block, from the first { of its line: the text after them is dropped, as Markdig drops it
    private BuildNode? ParseBlockAttributes(int start, ref int lineEnd)
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
        var node = new BuildNode(SyntaxKind.GenericAttributes, 2);
        node.AddToken(SyntaxKind.AttributesToken, brace, end, value: 3);
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

        node.AddToken(SyntaxKind.AttributesTrailingTextToken, rest, restEnd);
        lineEnd = brace;
        return node;
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
            block.Leading.Add(attributes);
        }

        _skipAddLine = true;
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
            block.Leading.Add(attributes);
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
        int markerEnd;
        if (c is '*' or '+' or '-')
        {
            ordered = false;
            markerEnd = markerStart + 1;
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

        return new ListData(ordered, ordered ? '\0' : c, start, delimiter, padding, markerOffset, markerStart, markerEnd);
    }

    private static bool ListsMatch(ListData? list, ListData item) => list is not null && list.Ordered == item.Ordered && list.Delimiter == item.Delimiter && list.BulletChar == item.BulletChar;

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
            block.LineStart = _lines.Count;
        }
        else if (block.LineStart + block.LineCount != _lines.Count)
        {
            throw new InvalidOperationException($"The lines of the {block.Kind} are not the last ones.");
        }

        _lines.Add(line);
        block.LineCount++;
    }

    private ReadOnlySpan<Line> GetLines(Block block) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_lines).Slice(block.LineStart, block.LineCount);

    private Block AddChild(SyntaxKind kind)
    {
        while (!CanContain(_tip.Kind, kind))
        {
            Finalize(_tip);
        }

        var block = new Block(kind, _tip);
        _tip.Children.Add(block);
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
                    var text = child.Subject!.Text;
                    var end = text.IndexOf('\n', child.ContentStart, StringComparison.Ordinal);
                    AddHeadingLabel(text, child.ContentStart, end < 0 ? text.Length : end);
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
        var subject = Subject.FromLines(_text, GetLines(paragraph));
        var position = paragraph.ContentStart;
        while (position < subject.Length && subject.Text[position] == '[')
        {
            var end = _inlineParser.ParseReference(subject, position, out var definition);
            if (end < 0)
            {
                break;
            }

            paragraph.Definitions.Add(definition!);
            position = end;
        }

        paragraph.ContentStart = position;
        paragraph.DefinitionLineCount = position == subject.Length ? paragraph.LineCount : subject.Text.AsSpan(0, position).Count('\n');
        paragraph.Subject = subject;
        paragraph.IsEmpty = subject.Text.AsSpan(position).Trim(" \t\n").IsEmpty;
    }

    private void Convert(Block block, List<BuildElement> output)
    {
        switch (block.Kind)
        {
            case SyntaxKind.Paragraph or SyntaxKind.SetextHeading:
                if (block.DefinitionsOrNull is { } definitions)
                {
                    foreach (var definition in definitions)
                    {
                        output.Add(definition);
                    }
                }

                if (block == _alertParagraph)
                {
                    // The kind of the alert is the first token of the paragraph, and its content starts on the next line
                    var node = new BuildNode(block.Kind);
                    node.Children.Add(BuildElement.Token(SyntaxKind.AlertKindToken, block.Subject!.ToSourceStart(_alertKind.KindStart), block.Subject.ToSourceEnd(_alertKind.KindEnd)));
                    _inlineParser.Parse(block.Subject, _alertKind.ContentStart, node.Children);
                    output.Add(node);
                    _alertParagraph = null;
                }
                else if (block.Kind == SyntaxKind.SetextHeading || !block.IsEmpty)
                {
                    var node = new BuildNode(block.Kind);
                    _inlineParser.Parse(block.Subject!, block.ContentStart, node.Children);
                    AddTokens(node, block.TrailingOrNull);
                    output.Add(node);
                }

                return;

            case SyntaxKind.AtxHeading:
            {
                var node = new BuildNode(block.Kind);
                AddTokens(node, block.LeadingOrNull);
                _inlineParser.Parse(Subject.FromLines(_text, GetLines(block)), 0, node.Children);
                AddTokens(node, block.TrailingOrNull);
                output.Add(node);
                return;
            }

            case SyntaxKind.Table:
                output.Add(ConvertTable(block));
                return;

            case SyntaxKind.FencedCodeBlock or SyntaxKind.IndentedCodeBlock or SyntaxKind.HtmlBlock or SyntaxKind.FrontMatter or SyntaxKind.MathBlock:
            {
                var node = new BuildNode(block.Kind, block.TokenCount + block.LineCount);
                AddTokens(node, block.LeadingOrNull);
                var kind = block.Kind switch
                {
                    SyntaxKind.HtmlBlock => SyntaxKind.HtmlLineToken,
                    SyntaxKind.FrontMatter => SyntaxKind.FrontMatterLineToken,
                    SyntaxKind.MathBlock => SyntaxKind.MathLineToken,
                    _ => SyntaxKind.CodeLineToken,
                };
                foreach (var line in GetLines(block))
                {
                    node.AddLineToken(kind, line.Start, line.EolEnd, line.VirtualSpaces);
                }

                AddTokens(node, block.TrailingOrNull);
                output.Add(node);
                return;
            }

            default:
            {
                // A block quote whose paragraph starts with [!KIND] on its own line is an alert
                var alert = block.Kind == SyntaxKind.BlockQuote && HasExtension(MarkdownExtensions.Alerts) && !_inAlert && (_allowNestedAlerts || block.Parent == _document) && FindAlertParagraph(block);
                var node = new BuildNode(alert ? SyntaxKind.Alert : block.Kind, block.TokenCount + block.Children.Count);
                AddTokens(node, block.LeadingOrNull);
                _inAlert |= alert;
                foreach (var child in block.Children)
                {
                    Convert(child, node.Children);
                }

                _inAlert &= !alert;
                AddTokens(node, block.TrailingOrNull);
                output.Add(node);
                return;
            }
        }
    }

    // The first paragraph of the quote that starts with [!, letters, and ], followed by spaces and a line break or its end
    private bool FindAlertParagraph(Block quote)
    {
        foreach (var child in quote.Children)
        {
            if (child.Kind != SyntaxKind.Paragraph || child.IsEmpty || child.Subject is not { } subject)
            {
                continue;
            }

            var text = subject.Text;
            var start = child.ContentStart;
            while (start < text.Length && text[start] is ' ' or '\t')
            {
                start++;
            }

            var position = start + 2;
            if (position > text.Length || text[start] != '[' || text[start + 1] != '!')
            {
                continue;
            }

            while (position < text.Length && char.IsAsciiLetter(text[position]))
            {
                position++;
            }

            if (position == start + 2 || position >= text.Length || text[position] != ']')
            {
                continue;
            }

            var kindEnd = position + 1;
            position = kindEnd;
            while (position < text.Length && text[position] is ' ' or '\t')
            {
                position++;
            }

            if (position < text.Length && text[position] != '\n')
            {
                continue;
            }

            _alertParagraph = child;
            _alertKind = (start, kindEnd, position);
            return true;
        }

        return false;
    }

    private static void AddTokens(BuildNode node, List<BuildElement>? tokens)
    {
        if (tokens is not null)
        {
            node.Children.AddRange(tokens);
        }
    }

    // A table: its header row, its delimiter row, and its other rows. The pipes are tokens of the rows, and the content of a
    // cell is its inlines.
    private BuildNode ConvertTable(Block table)
    {
        var node = new BuildNode(SyntaxKind.Table);
        var cells = new List<(int Start, int End)>();
        var pipes = new List<int>();
        for (var i = 0; i < table.LineCount; i++)
        {
            var line = _lines[table.LineStart + i];
            cells.Clear();
            pipes.Clear();
            var row = new BuildNode(i switch
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
                    row.AddToken(SyntaxKind.PipeToken, pipes[pipeIndex], pipes[pipeIndex] + 1);
                    pipeIndex++;
                }

                if (i == 1)
                {
                    row.AddToken(SyntaxKind.TableDelimiterToken, start, end);
                }
                else
                {
                    var cell = new BuildNode(SyntaxKind.TableCell);
                    if (end > start)
                    {
                        _inlineParser.Parse(Subject.FromTableCell(_text, start, end), 0, cell.Children);
                    }

                    row.Children.Add(cell);
                }
            }

            for (; pipeIndex < pipes.Count; pipeIndex++)
            {
                row.AddToken(SyntaxKind.PipeToken, pipes[pipeIndex], pipes[pipeIndex] + 1);
            }

            node.Children.Add(row);
        }

        return node;
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

        public List<Block> Children { get; } = [];

        public Block? LastChild => Children.Count == 0 ? null : Children[^1];

        public bool Open { get; set; } = true;

        // The tokens of the block before and after its children or its lines, created when the block has some
        public List<BuildElement> Leading => LeadingOrNull ??= new(2);

        public List<BuildElement> Trailing => TrailingOrNull ??= new(1);

        public List<BuildElement>? LeadingOrNull { get; private set; }

        public List<BuildElement>? TrailingOrNull { get; private set; }

        public int TokenCount => (LeadingOrNull?.Count ?? 0) + (TrailingOrNull?.Count ?? 0);

        // The range of the lines of a leaf block in the lines of the parser
        public int LineStart { get; set; }

        public int LineCount { get; set; }

        public ListData? ListData { get; set; }

        public char FenceChar { get; set; }

        public int FenceLength { get; set; }

        public int FenceOffset { get; set; }

        public int HtmlBlockType { get; set; }

        // A footnote whose last line is blank, which a line that is not indented ends
        public bool FootnoteLastLineBlank { get; set; }

        // A paragraph whose last line was not the header row of a table: GFM tries a paragraph once
        public bool TableVisited { get; set; }

        // A paragraph: its link reference definitions, then its inlines from ContentStart in its subject
        public List<BuildNode> Definitions => DefinitionsOrNull ??= [];

        public List<BuildNode>? DefinitionsOrNull { get; private set; }

        public int ContentStart { get; set; }

        // The lines that the link reference definitions take
        public int DefinitionLineCount { get; set; }

        public Subject? Subject { get; set; }

        public bool IsEmpty { get; set; }
    }

    private sealed record ListData(bool Ordered, char BulletChar, int Start, char Delimiter, int Padding, int MarkerOffset, int MarkerStart, int MarkerEnd);
}
