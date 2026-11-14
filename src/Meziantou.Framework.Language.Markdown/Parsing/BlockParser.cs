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
    private readonly List<TriviaSpan> _triviaSpans = [];
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

    private BlockParser(string text, MarkdownExtensions extensions)
    {
        _text = text;
        _extensions = extensions;
        _tip = _document;
        _oldTip = _document;
        _lastMatchedContainer = _document;
        _inlineParser = new InlineParser(_references, extensions);
    }

    public static (BuildNode Document, List<TriviaSpan> TriviaSpans) Parse(string text, MarkdownExtensions extensions)
    {
        var parser = new BlockParser(text, extensions);
        var position = 0;
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
        return ((BuildNode)parser.Convert(parser._document)[0], parser._triviaSpans);
    }

    private char Peek(int index) => index < _lineEnd ? _text[index] : '\0';

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

    private static bool AcceptsLines(SyntaxKind kind) => kind is SyntaxKind.Paragraph or SyntaxKind.IndentedCodeBlock or SyntaxKind.FencedCodeBlock or SyntaxKind.HtmlBlock;

    private static bool CanContain(SyntaxKind parent, SyntaxKind child) => parent switch
    {
        SyntaxKind.Document or SyntaxKind.BlockQuote or SyntaxKind.ListItem => child != SyntaxKind.ListItem,
        SyntaxKind.List => child == SyntaxKind.ListItem,
        _ => false,
    };

    private static bool IsMaybeSpecial(char c) => c is '#' or '`' or '~' or '*' or '+' or '_' or '=' or '<' or '>' or '-' or (>= '0' and <= '9');

    // Whether the line can start a block of an extension: a delimiter row under a paragraph, a row of a table, or the task
    // list item marker after the marker of a list item
    private bool IsMaybeExtension(Block container, char c) => container.Kind switch
    {
        SyntaxKind.Paragraph => HasExtension(MarkdownExtensions.Tables) && c is '|' or ':',
        SyntaxKind.Table => true,
        SyntaxKind.ListItem => HasExtension(MarkdownExtensions.TaskLists) && c == '[' && container == _itemOpenedOnLine,
        _ => false,
    };

    private bool HasExtension(MarkdownExtensions extension) => _extensions.HasFlag(extension);

    // 0: the line continues the block, 1: it does not, 2: it ends the block and nothing else is on it
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

            case SyntaxKind.FencedCodeBlock:
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
                        block.Trailing.Add(new BuildToken(SyntaxKind.CodeFenceToken, _nextNonspace, end, 0));
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
            AddChild(SyntaxKind.BlockQuote).Leading.Add(new BuildToken(SyntaxKind.BlockQuoteMarkerToken, marker, marker + 1, 0));
            return 1;
        }

        // ATX heading
        if (!_indented && c == '#' && TryStartAtxHeading())
        {
            return 2;
        }

        // Fenced code block
        if (!_indented && c is '`' or '~' && TryStartFencedCodeBlock(c))
        {
            return 2;
        }

        // HTML block
        if (!_indented && c == '<')
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
                container.Trailing.Add(new BuildToken(SyntaxKind.SetextHeadingUnderlineToken, _nextNonspace, underlineEnd, 0));
                _offset = _lineEnd;
                return 2;
            }
        }

        // Thematic break
        if (!_indented && c is '*' or '_' or '-' && IsThematicBreak(out var breakEnd))
        {
            CloseUnmatchedBlocks();
            var block = AddChild(SyntaxKind.ThematicBreak);
            block.Leading.Add(new BuildToken(SyntaxKind.ThematicBreakToken, _nextNonspace, breakEnd, 0));
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
            item.Leading.Add(new BuildToken(SyntaxKind.ListMarkerToken, data.MarkerStart, data.MarkerEnd, 0));
            _itemOpenedOnLine = item;
            return 1;
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
            container.Lines.Add(new Line(_nextNonspace, _lineEnd, _eolEnd, 0));
            _offset = _lineEnd;
            return 2;
        }

        // Task list item: [ ], [x] or [X] at the start of the first paragraph of a list item, on the line of its marker
        if (HasExtension(MarkdownExtensions.TaskLists) && container == _itemOpenedOnLine && IsTaskListItemMarker())
        {
            container.Leading.Add(new BuildToken(SyntaxKind.TaskListItemMarkerToken, _nextNonspace, _nextNonspace + 3, 0));
            AdvanceNextNonspace();
            AdvanceOffset(3, columns: false);
            FindNextNonspace();
            _itemOpenedOnLine = null;
            return 0;
        }

        return 0;
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

        // The header row must have as many cells as the delimiter row. A paragraph that fails is never a table: its next lines
        // are not tried again.
        var header = paragraph.Lines[^1];
        if (TableScanner.ScanRow(_text, header.Start, header.End, cells: null, pipes: null) != columns)
        {
            paragraph.TableVisited = true;
            return false;
        }

        CloseUnmatchedBlocks();

        // The lines before the header row are a paragraph
        if (paragraph.Lines.Count > 1)
        {
            var parent = paragraph.Parent!;
            var before = new Block(SyntaxKind.Paragraph, parent) { Open = false };
            before.Lines.AddRange(paragraph.Lines.GetRange(0, paragraph.Lines.Count - 1));
            parent.Children.Insert(parent.Children.Count - 1, before);
            ParseDefinitions(before);
        }

        paragraph.Kind = SyntaxKind.Table;
        paragraph.Lines.Clear();
        paragraph.Lines.Add(header);
        paragraph.Lines.Add(new Line(_nextNonspace, _lineEnd, _eolEnd, 0));
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
        block.Leading.Add(new BuildToken(SyntaxKind.AtxHeadingMarkerToken, markerStart, position, 0));

        // The closing sequence is a run of '#' at the end, after a space, or alone
        var end = _lineEnd;
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
            block.Trailing.Add(new BuildToken(SyntaxKind.AtxHeadingClosingSequenceToken, hashStart, end, 0));
            contentEnd = hashStart;
            while (contentEnd > _offset && Scanners.IsSpaceOrTab(_text[contentEnd - 1]))
            {
                contentEnd--;
            }
        }

        block.Lines.Add(new Line(_offset, contentEnd, contentEnd, 0));
        _offset = _lineEnd;
        return true;
    }

    private bool TryStartFencedCodeBlock(char c)
    {
        var position = _nextNonspace;
        while (position < _lineEnd && _text[position] == c)
        {
            position++;
        }

        var length = position - _nextNonspace;
        if (length < 3 || (c == '`' && _text.AsSpan(position, _lineEnd - position).Contains('`')))
        {
            return false;
        }

        CloseUnmatchedBlocks();
        var block = AddChild(SyntaxKind.FencedCodeBlock);
        block.FenceChar = c;
        block.FenceLength = length;
        block.FenceOffset = _indent;
        AdvanceNextNonspace();
        block.Leading.Add(new BuildToken(SyntaxKind.CodeFenceToken, _offset, position, 0));
        AdvanceOffset(length, columns: false);

        // The info string is the rest of the line, without the whitespace around it
        var infoStart = _offset;
        var infoEnd = _lineEnd;
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
            block.Leading.Add(new BuildToken(SyntaxKind.InfoStringToken, infoStart, infoEnd, 0));
        }

        _skipAddLine = true;
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

        _tip.Lines.Add(new Line(start, _lineEnd, _eolEnd, virtualSpaces));
    }

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
                while (block.Lines.Count > 0 && block.Lines[^1] is var line && _text.AsSpan(line.Start, line.End - line.Start).Trim(" \t").IsEmpty)
                {
                    block.Lines.RemoveAt(block.Lines.Count - 1);
                }

                break;
        }

        _tip = block.Parent ?? _document;
    }

    // The link reference definitions at the start of a paragraph are blocks of their own
    private void ParseDefinitions(Block paragraph)
    {
        var subject = Subject.FromLines(_text, paragraph.Lines);
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
        paragraph.Subject = subject;
        paragraph.IsEmpty = subject.Text.AsSpan(position).Trim(" \t\n").IsEmpty;
    }

    private List<BuildElement> Convert(Block block)
    {
        var result = new List<BuildElement>();
        switch (block.Kind)
        {
            case SyntaxKind.Paragraph or SyntaxKind.SetextHeading:
                result.AddRange(block.Definitions);
                if (block.Kind == SyntaxKind.SetextHeading || !block.IsEmpty)
                {
                    var node = new BuildNode(block.Kind);
                    node.Children.AddRange(_inlineParser.Parse(block.Subject!, block.ContentStart));
                    node.Children.AddRange(block.Trailing);
                    result.Add(node);
                }

                return result;

            case SyntaxKind.AtxHeading:
            {
                var node = new BuildNode(block.Kind);
                node.Children.AddRange(block.Leading);
                node.Children.AddRange(_inlineParser.Parse(Subject.FromLines(_text, block.Lines), 0));
                node.Children.AddRange(block.Trailing);
                result.Add(node);
                return result;
            }

            case SyntaxKind.Table:
                result.Add(ConvertTable(block));
                return result;

            case SyntaxKind.FencedCodeBlock or SyntaxKind.IndentedCodeBlock or SyntaxKind.HtmlBlock:
            {
                var node = new BuildNode(block.Kind);
                node.Children.AddRange(block.Leading);
                var kind = block.Kind == SyntaxKind.HtmlBlock ? SyntaxKind.HtmlLineToken : SyntaxKind.CodeLineToken;
                foreach (var line in block.Lines)
                {
                    node.AddLineToken(kind, line.Start, line.EolEnd, line.VirtualSpaces);
                }

                node.Children.AddRange(block.Trailing);
                result.Add(node);
                return result;
            }

            default:
            {
                var node = new BuildNode(block.Kind);
                node.Children.AddRange(block.Leading);
                foreach (var child in block.Children)
                {
                    node.Children.AddRange(Convert(child));
                }

                node.Children.AddRange(block.Trailing);
                result.Add(node);
                return result;
            }
        }
    }

    // A table: its header row, its delimiter row, and its other rows. The pipes are tokens of the rows, and the content of a
    // cell is its inlines.
    private BuildNode ConvertTable(Block table)
    {
        var node = new BuildNode(SyntaxKind.Table);
        var cells = new List<(int Start, int End)>();
        var pipes = new List<int>();
        for (var i = 0; i < table.Lines.Count; i++)
        {
            var line = table.Lines[i];
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
                        cell.Children.AddRange(_inlineParser.Parse(Subject.FromTableCell(_text, start, end), 0));
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

        // The tokens of the block before and after its children or its lines
        public List<BuildToken> Leading { get; } = [];

        public List<BuildToken> Trailing { get; } = [];

        public List<Line> Lines { get; } = [];

        public ListData? ListData { get; set; }

        public char FenceChar { get; set; }

        public int FenceLength { get; set; }

        public int FenceOffset { get; set; }

        public int HtmlBlockType { get; set; }

        // A paragraph whose last line was not the header row of a table: GFM tries a paragraph once
        public bool TableVisited { get; set; }

        // A paragraph: its link reference definitions, then its inlines from ContentStart in its subject
        public List<BuildNode> Definitions { get; } = [];

        public int ContentStart { get; set; }

        public Subject? Subject { get; set; }

        public bool IsEmpty { get; set; }
    }

    private sealed record ListData(bool Ordered, char BulletChar, int Start, char Delimiter, int Padding, int MarkerOffset, int MarkerStart, int MarkerEnd);
}
