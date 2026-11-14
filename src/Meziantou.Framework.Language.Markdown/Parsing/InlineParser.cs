using System.Text;

namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>
/// Parses the inlines of a leaf block, as the CommonMark reference implementation (commonmark.js) does, except that every
/// construct is a range of the subject: the delimiters, the brackets and the destinations of the links are tokens, not
/// values, so that the tree keeps them.
/// </summary>
internal sealed class InlineParser
{
    private const int MaximumLinkDestinationParentheses = 32;

    private readonly Dictionary<string, bool> _references;
    private Subject _subject = null!;
    private string _text = "";
    private int _pos;
    private int _end;
    private InlineNode _root = null!;
    private Delimiter? _delimiters;
    private Bracket? _brackets;

    public InlineParser(Dictionary<string, bool> references) => _references = references;

    /// <summary>Parses the inlines of the subject from <paramref name="start"/>, without the whitespace around them.</summary>
    public List<BuildElement> Parse(Subject subject, int start)
    {
        _subject = subject;
        _text = subject.Text;
        _end = _text.Length;
        while (_end > start && _text[_end - 1] is ' ' or '\t' or '\n')
        {
            _end--;
        }

        _pos = start;
        while (_pos < _end && _text[_pos] is ' ' or '\t' or '\n')
        {
            _pos++;
        }

        _root = new InlineNode(SyntaxKind.Paragraph, _pos, _pos);
        _delimiters = null;
        _brackets = null;
        while (_pos < _end)
        {
            ParseInline();
        }

        ProcessEmphasis(stackBottom: null);
        var result = new List<BuildElement>();
        foreach (var child in _root.Children())
        {
            AddBuildElements(child, result);
        }

        return result;
    }

    // Parses a link reference definition at the start of the subject. Returns where it ends, or -1 when there is none.
    public int ParseReference(Subject subject, int start, out BuildNode? definition)
    {
        _subject = subject;
        _text = subject.Text;
        _end = _text.Length;
        _pos = start;
        definition = null;

        var labelLength = ScanLinkLabel();
        if (labelLength == 0)
        {
            return -1;
        }

        var labelEnd = _pos + labelLength;
        var rawLabel = _text[(_pos + 1)..(labelEnd - 1)];
        _pos = labelEnd;
        if (Peek() != ':')
        {
            return -1;
        }

        var colon = _pos;
        _pos++;
        SkipSpacesAndNewline();
        var destinationStart = _pos;
        if (!ScanLinkDestination())
        {
            return -1;
        }

        var destinationEnd = _pos;
        var beforeTitle = _pos;
        SkipSpacesAndNewline();
        var titleStart = -1;
        var titleEnd = -1;
        if (_pos != beforeTitle && ScanLinkTitle() is > 0 and var titleLength)
        {
            titleStart = _pos;
            titleEnd = _pos + titleLength;
            _pos = titleEnd;
        }
        else
        {
            _pos = beforeTitle;
        }

        // The definition must end its line: else the title is not part of it
        if (!IsAtLineEnd())
        {
            if (titleStart < 0)
            {
                return -1;
            }

            titleStart = -1;
            _pos = beforeTitle;
            if (!IsAtLineEnd())
            {
                return -1;
            }
        }

        var normalized = Scanners.NormalizeLabel(rawLabel);
        if (normalized.Length == 0)
        {
            return -1;
        }

        _references.TryAdd(normalized, value: true);

        definition = new BuildNode(SyntaxKind.LinkReferenceDefinition);
        AddRange(definition, SyntaxKind.OpenBracketToken, start, start + 1);
        AddRange(definition, SyntaxKind.LinkLabelToken, start + 1, labelEnd - 1);
        AddRange(definition, SyntaxKind.CloseBracketToken, labelEnd - 1, labelEnd);
        AddRange(definition, SyntaxKind.ColonToken, colon, colon + 1);
        AddRange(definition, SyntaxKind.LinkDestinationToken, destinationStart, destinationEnd);
        if (titleStart >= 0)
        {
            AddRange(definition, SyntaxKind.LinkTitleToken, titleStart, titleEnd);
        }

        // The end of the line is part of the definition
        while (_pos < _end && _text[_pos] == ' ')
        {
            _pos++;
        }

        if (_pos < _end && _text[_pos] == '\n')
        {
            _pos++;
        }

        return _pos;
    }

    private bool IsAtLineEnd()
    {
        var position = _pos;
        while (position < _end && _text[position] == ' ')
        {
            position++;
        }

        return position == _end || _text[position] == '\n';
    }

    private char Peek() => _pos < _end ? _text[_pos] : '\0';

    private bool PeekIs(char c) => _pos < _end && _text[_pos] == c;

    private void ParseInline()
    {
        var c = _text[_pos];
        switch (c)
        {
            case '\n':
                ParseNewline();
                break;
            case '\\':
                ParseBackslash();
                break;
            case '`':
                ParseBackticks();
                break;
            case '*' or '_':
                HandleDelimiter(c);
                break;
            case '[':
                ParseOpenBracket(image: false);
                break;
            case '!':
                if (_pos + 1 < _end && _text[_pos + 1] == '[')
                {
                    ParseOpenBracket(image: true);
                }
                else
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case ']':
                ParseCloseBracket();
                break;
            case '<':
                if (!ParseAutolink() && !ParseHtmlTag())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case '&':
                if (!ParseEntity())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            default:
                ParseString();
                break;
        }
    }

    private void ParseString()
    {
        var start = _pos;
        while (_pos < _end && _text[_pos] is not ('\n' or '\\' or '`' or '*' or '_' or '[' or ']' or '!' or '<' or '&'))
        {
            _pos++;
        }

        AppendText(start, _pos);
    }

    private void ParseNewline()
    {
        var newline = _pos;
        _pos++;

        // The spaces at the end of the line are not text: two or more of them make a hard line break
        var last = _root.LastChild;
        var spacesStart = newline;
        if (last is { Kind: SyntaxKind.TextToken })
        {
            while (spacesStart > last.Start && _text[spacesStart - 1] == ' ')
            {
                spacesStart--;
            }

            last.End = spacesStart;
        }

        if (newline - spacesStart >= 2)
        {
            _root.Append(new InlineNode(SyntaxKind.HardLineBreakToken, spacesStart, newline + 1));
        }
        else
        {
            _root.Append(new InlineNode(SyntaxKind.LineEndingToken, newline, newline + 1));
        }

        // The spaces at the start of the next line are not text either
        while (_pos < _end && _text[_pos] == ' ')
        {
            _pos++;
        }
    }

    private void ParseBackslash()
    {
        var start = _pos;
        _pos++;
        if (PeekIs('\n'))
        {
            _pos++;
            _root.Append(new InlineNode(SyntaxKind.HardLineBreakToken, start, _pos));
            while (_pos < _end && _text[_pos] == ' ')
            {
                _pos++;
            }
        }
        else if (_pos < _end && Scanners.IsEscapable(_text[_pos]))
        {
            _pos++;
            _root.Append(new InlineNode(SyntaxKind.EscapedCharacterToken, start, _pos));
        }
        else
        {
            AppendText(start, _pos);
        }
    }

    private void ParseBackticks()
    {
        var start = _pos;
        while (_pos < _end && _text[_pos] == '`')
        {
            _pos++;
        }

        var tickCount = _pos - start;
        var afterOpenTicks = _pos;
        var position = _pos;
        while (position < _end)
        {
            if (_text[position] != '`')
            {
                position++;
                continue;
            }

            var runStart = position;
            while (position < _end && _text[position] == '`')
            {
                position++;
            }

            if (position - runStart == tickCount)
            {
                var code = new InlineNode(SyntaxKind.CodeSpan, start, position);
                code.Append(new InlineNode(SyntaxKind.BacktickStringToken, start, afterOpenTicks));
                AppendLines(code, SyntaxKind.CodeTextToken, afterOpenTicks, runStart);
                code.Append(new InlineNode(SyntaxKind.BacktickStringToken, runStart, position));
                _root.Append(code);
                _pos = position;
                return;
            }
        }

        // No closing backticks: the opening ones are text
        AppendText(start, afterOpenTicks);
    }

    private bool ParseAutolink()
    {
        var length = Match(Scanners.EmailAutolinkRegex());
        if (length == 0)
        {
            length = Match(Scanners.AutolinkRegex());
        }

        if (length == 0)
        {
            return false;
        }

        var link = new InlineNode(SyntaxKind.Autolink, _pos, _pos + length);
        link.Append(new InlineNode(SyntaxKind.AutolinkToken, _pos, _pos + length));
        _root.Append(link);
        _pos += length;
        return true;
    }

    private bool ParseHtmlTag()
    {
        var length = Match(Scanners.HtmlTagRegex());
        if (length == 0)
        {
            return false;
        }

        var html = new InlineNode(SyntaxKind.RawHtml, _pos, _pos + length);
        AppendLines(html, SyntaxKind.HtmlTagToken, _pos, _pos + length);
        _root.Append(html);
        _pos += length;
        return true;
    }

    private bool ParseEntity()
    {
        var length = Match(Scanners.EntityRegex());
        if (length == 0)
        {
            return false;
        }

        _root.Append(new InlineNode(SyntaxKind.EntityToken, _pos, _pos + length));
        _pos += length;
        return true;
    }

    private int Match(System.Text.RegularExpressions.Regex regex)
    {
        var match = regex.Match(_text, _pos, _end - _pos);
        return match.Success ? match.Length : 0;
    }

    private void HandleDelimiter(char c)
    {
        var start = _pos;
        while (_pos < _end && _text[_pos] == c)
        {
            _pos++;
        }

        var count = _pos - start;
        var before = start == 0 ? new Rune('\n') : GetRuneBefore(start);
        var after = _pos >= _end ? new Rune('\n') : GetRuneAt(_pos);
        var afterIsWhitespace = Scanners.IsUnicodeWhitespace(after);
        var afterIsPunctuation = Scanners.IsUnicodePunctuation(after);
        var beforeIsWhitespace = Scanners.IsUnicodeWhitespace(before);
        var beforeIsPunctuation = Scanners.IsUnicodePunctuation(before);
        var leftFlanking = !afterIsWhitespace && (!afterIsPunctuation || beforeIsWhitespace || beforeIsPunctuation);
        var rightFlanking = !beforeIsWhitespace && (!beforeIsPunctuation || afterIsWhitespace || afterIsPunctuation);
        bool canOpen;
        bool canClose;
        if (c == '_')
        {
            canOpen = leftFlanking && (!rightFlanking || beforeIsPunctuation);
            canClose = rightFlanking && (!leftFlanking || afterIsPunctuation);
        }
        else
        {
            canOpen = leftFlanking;
            canClose = rightFlanking;
        }

        var node = AppendText(start, _pos);
        if (canOpen || canClose)
        {
            var delimiter = new Delimiter(c, count, node, canOpen, canClose) { Previous = _delimiters };
            if (_delimiters is not null)
            {
                _delimiters.Next = delimiter;
            }

            _delimiters = delimiter;
        }
    }

    private Rune GetRuneBefore(int index)
    {
        if (index >= 2 && char.IsLowSurrogate(_text[index - 1]) && char.IsHighSurrogate(_text[index - 2]))
        {
            return new Rune(_text[index - 2], _text[index - 1]);
        }

        return Rune.IsValid(_text[index - 1]) ? new Rune(_text[index - 1]) : Rune.ReplacementChar;
    }

    private Rune GetRuneAt(int index)
    {
        if (index + 1 < _text.Length && char.IsHighSurrogate(_text[index]) && char.IsLowSurrogate(_text[index + 1]))
        {
            return new Rune(_text[index], _text[index + 1]);
        }

        return Rune.IsValid(_text[index]) ? new Rune(_text[index]) : Rune.ReplacementChar;
    }

    private void ParseOpenBracket(bool image)
    {
        var start = _pos;
        _pos += image ? 2 : 1;
        var node = new InlineNode(image ? SyntaxKind.ExclamationOpenBracketToken : SyntaxKind.OpenBracketToken, start, _pos);
        _root.Append(node);
        if (_brackets is not null)
        {
            _brackets.BracketAfter = true;
        }

        _brackets = new Bracket(node, _brackets, _delimiters, _pos, image);
    }

    private void ParseCloseBracket()
    {
        var closeStart = _pos;
        _pos++;
        var opener = _brackets;
        if (opener is null)
        {
            AppendText(closeStart, _pos);
            return;
        }

        if (!opener.Active)
        {
            AppendText(closeStart, _pos);
            RemoveBracket();
            return;
        }

        var savePosition = _pos;
        var suffix = new List<InlineNode>();
        var matched = false;

        // An inline link: (destination "title")
        if (PeekIs('('))
        {
            var openParenthesis = _pos;
            _pos++;
            SkipSpacesAndNewline();
            var destinationStart = _pos;
            if (ScanLinkDestination())
            {
                var destinationEnd = _pos;
                var beforeTitle = _pos;
                SkipSpacesAndNewline();
                var titleStart = -1;
                var titleEnd = -1;
                if (_pos > beforeTitle && _pos - 1 >= 0 && _text[_pos - 1] is ' ' or '\t' or '\n' && ScanLinkTitle() is > 0 and var titleLength)
                {
                    titleStart = _pos;
                    titleEnd = _pos + titleLength;
                    _pos = titleEnd;
                    SkipSpacesAndNewline();
                }

                if (PeekIs(')'))
                {
                    suffix.Add(new InlineNode(SyntaxKind.OpenParenthesisToken, openParenthesis, openParenthesis + 1));
                    if (destinationEnd > destinationStart)
                    {
                        suffix.Add(new InlineNode(SyntaxKind.LinkDestinationToken, destinationStart, destinationEnd));
                    }

                    if (titleStart >= 0)
                    {
                        AddLineNodes(suffix, SyntaxKind.LinkTitleToken, titleStart, titleEnd);
                    }

                    suffix.Add(new InlineNode(SyntaxKind.CloseParenthesisToken, _pos, _pos + 1));
                    _pos++;
                    matched = true;
                }
            }

            if (!matched)
            {
                _pos = savePosition;
                suffix.Clear();
            }
        }

        if (!matched)
        {
            // A reference link: [text][label], [text][] or [text]
            var beforeLabel = _pos;
            var labelLength = ScanLinkLabel();
            string? label = null;
            if (labelLength > 2)
            {
                label = _text[(beforeLabel + 1)..(beforeLabel + labelLength - 1)];
            }
            else if (!opener.BracketAfter)
            {
                label = _text[opener.Index..closeStart];
            }

            if (labelLength == 0)
            {
                _pos = savePosition;
            }

            if (label is not null && _references.ContainsKey(Scanners.NormalizeLabel(label)))
            {
                matched = true;
                if (labelLength >= 2)
                {
                    suffix.Add(new InlineNode(SyntaxKind.OpenBracketToken, beforeLabel, beforeLabel + 1));
                    if (labelLength > 2)
                    {
                        AddLineNodes(suffix, SyntaxKind.LinkLabelToken, beforeLabel + 1, beforeLabel + labelLength - 1);
                    }

                    suffix.Add(new InlineNode(SyntaxKind.CloseBracketToken, beforeLabel + labelLength - 1, beforeLabel + labelLength));
                    _pos = beforeLabel + labelLength;
                }
            }
        }

        if (!matched)
        {
            RemoveBracket();
            _pos = closeStart;
            AppendText(closeStart, closeStart + 1);
            _pos = closeStart + 1;
            return;
        }

        var link = new InlineNode(opener.Image ? SyntaxKind.Image : SyntaxKind.Link, opener.Node.Start, _pos);
        var current = opener.Node.Next;
        while (current is not null)
        {
            var next = current.Next;
            current.Unlink();
            link.Append(current);
            current = next;
        }

        _root.Append(link);
        ProcessEmphasis(opener.PreviousDelimiter);
        RemoveBracket();

        // The opening bracket is the first token of the link
        opener.Node.Unlink();
        link.Prepend(opener.Node);
        link.Append(new InlineNode(SyntaxKind.CloseBracketToken, closeStart, closeStart + 1));
        foreach (var node in suffix)
        {
            link.Append(node);
        }

        // There are no links in links
        if (!opener.Image)
        {
            for (var bracket = _brackets; bracket is not null; bracket = bracket.Previous)
            {
                if (!bracket.Image)
                {
                    bracket.Active = false;
                }
            }
        }
    }

    private void RemoveBracket() => _brackets = _brackets?.Previous;

    // Optional spaces or tabs, including up to one line ending
    private void SkipSpacesAndNewline()
    {
        while (_pos < _end && _text[_pos] is ' ' or '\t')
        {
            _pos++;
        }

        if (_pos < _end && _text[_pos] == '\n')
        {
            _pos++;
            while (_pos < _end && _text[_pos] is ' ' or '\t')
            {
                _pos++;
            }
        }
    }

    // Scans a link destination, which can be empty in angle brackets
    private bool ScanLinkDestination()
    {
        var braces = Match(Scanners.LinkDestinationBracesRegex());
        if (braces > 0)
        {
            _pos += braces;
            return true;
        }

        if (PeekIs('<'))
        {
            return false;
        }

        var start = _pos;
        var openParentheses = 0;
        while (_pos < _end)
        {
            var c = _text[_pos];
            if (c == '\\' && _pos + 1 < _end && Scanners.IsEscapable(_text[_pos + 1]))
            {
                _pos += 2;
            }
            else if (c == '(')
            {
                _pos++;
                openParentheses++;
                if (openParentheses > MaximumLinkDestinationParentheses)
                {
                    return false;
                }
            }
            else if (c == ')')
            {
                if (openParentheses < 1)
                {
                    break;
                }

                _pos++;
                openParentheses--;
            }
            else if (c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r' || c < ' ' || c == '\x7f')
            {
                break;
            }
            else
            {
                _pos++;
            }
        }

        if (_pos == start && !PeekIs(')'))
        {
            return false;
        }

        return openParentheses == 0;
    }

    private int ScanLinkTitle() => Match(Scanners.LinkTitleRegex());

    // A link label: brackets around at most 999 characters, where a bracket must be escaped
    private int ScanLinkLabel()
    {
        if (!PeekIs('['))
        {
            return 0;
        }

        var position = _pos + 1;
        while (position < _end && position - _pos <= 1000)
        {
            switch (_text[position])
            {
                case ']':
                    return position + 1 - _pos;
                case '[':
                    return 0;
                case '\\' when position + 1 < _end:
                    position += 2;
                    break;
                default:
                    position++;
                    break;
            }
        }

        return 0;
    }

    private InlineNode AppendText(int start, int end)
    {
        var node = new InlineNode(SyntaxKind.TextToken, start, end);
        _root.Append(node);
        return node;
    }

    // Adds the tokens of a range that can span lines: a token for each line, and the line endings between them. A token never
    // has a line break, which would put the markers of the containers of the next line in it.
    private void AppendLines(InlineNode parent, SyntaxKind kind, int start, int end)
    {
        var nodes = new List<InlineNode>();
        AddLineNodes(nodes, kind, start, end);
        foreach (var node in nodes)
        {
            parent.Append(node);
        }
    }

    private void AddLineNodes(List<InlineNode> nodes, SyntaxKind kind, int start, int end)
    {
        var lineStart = start;
        for (var i = start; i < end; i++)
        {
            if (_text[i] == '\n')
            {
                nodes.Add(new InlineNode(kind, lineStart, i));
                nodes.Add(new InlineNode(SyntaxKind.LineEndingToken, i, i + 1));
                lineStart = i + 1;
            }
        }

        nodes.Add(new InlineNode(kind, lineStart, end));
    }

    private void ProcessEmphasis(Delimiter? stackBottom)
    {
        // The lowest delimiter that can open for a closer, by character, whether the closer can open, and length modulo 3
        var openersBottom = new Dictionary<(char, int), Delimiter?>();

        var closer = _delimiters;
        while (closer is not null && closer.Previous != stackBottom)
        {
            closer = closer.Previous;
        }

        while (closer is not null)
        {
            if (!closer.CanClose)
            {
                closer = closer.Next;
                continue;
            }

            var bottomIndex = (closer.Character, (closer.CanOpen ? 3 : 0) + (closer.OriginalCount % 3));
            var bottom = openersBottom.GetValueOrDefault(bottomIndex, stackBottom);
            var opener = closer.Previous;
            var openerFound = false;
            while (opener is not null && opener != stackBottom && opener != bottom)
            {
                var oddMatch = (closer.CanOpen || opener.CanClose) && closer.OriginalCount % 3 != 0 && (opener.OriginalCount + closer.OriginalCount) % 3 == 0;
                if (opener.Character == closer.Character && opener.CanOpen && !oddMatch)
                {
                    openerFound = true;
                    break;
                }

                opener = opener.Previous;
            }

            var oldCloser = closer;
            if (!openerFound)
            {
                closer = closer.Next;

                // The openers below the closer cannot match the closers that follow it
                openersBottom[bottomIndex] = oldCloser.Previous;
                if (!oldCloser.CanOpen)
                {
                    RemoveDelimiter(oldCloser);
                }

                continue;
            }

            var use = closer.Count >= 2 && opener!.Count >= 2 ? 2 : 1;
            var openerNode = opener!.Node;
            var closerNode = closer.Node;
            opener.Count -= use;
            closer.Count -= use;

            // The delimiters used are the ones next to the content: the end of the opener and the start of the closer
            var emphasis = new InlineNode(use == 1 ? SyntaxKind.Emphasis : SyntaxKind.StrongEmphasis, openerNode.End - use, closerNode.Start + use);
            emphasis.Append(new InlineNode(SyntaxKind.EmphasisDelimiterToken, openerNode.End - use, openerNode.End));
            openerNode.End -= use;
            var current = openerNode.Next;
            while (current is not null && current != closerNode)
            {
                var next = current.Next;
                current.Unlink();
                emphasis.Append(current);
                current = next;
            }

            emphasis.Append(new InlineNode(SyntaxKind.EmphasisDelimiterToken, closerNode.Start, closerNode.Start + use));
            closerNode.Start += use;
            openerNode.InsertAfter(emphasis);

            // The delimiters between the opener and the closer are text now
            var between = closer.Previous;
            while (between is not null && between != opener)
            {
                var previous = between.Previous;
                RemoveDelimiter(between);
                between = previous;
            }

            if (opener.Count == 0)
            {
                openerNode.Unlink();
                RemoveDelimiter(opener);
            }

            if (closer.Count == 0)
            {
                closerNode.Unlink();
                var next = closer.Next;
                RemoveDelimiter(closer);
                closer = next;
            }
        }

        while (_delimiters is not null && _delimiters != stackBottom)
        {
            RemoveDelimiter(_delimiters);
        }
    }

    private void RemoveDelimiter(Delimiter delimiter)
    {
        if (delimiter.Previous is not null)
        {
            delimiter.Previous.Next = delimiter.Next;
        }

        if (delimiter.Next is null)
        {
            _delimiters = delimiter.Previous;
        }
        else
        {
            delimiter.Next.Previous = delimiter.Previous;
        }
    }

    private void AddBuildElements(InlineNode node, List<BuildElement> result)
    {
        if (node.IsToken)
        {
            if (node.End > node.Start)
            {
                result.Add(new BuildToken(node.Kind, _subject.ToSourceStart(node.Start), _subject.ToSourceEnd(node.End), 0));
            }

            return;
        }

        var buildNode = new BuildNode(node.Kind);
        foreach (var child in node.Children())
        {
            AddBuildElements(child, buildNode.Children);
        }

        result.Add(buildNode);
    }

    // Adds the tokens of a range of the subject, split at the line breaks
    private void AddRange(BuildNode node, SyntaxKind kind, int start, int end)
    {
        var lineStart = start;
        for (var i = start; i < end; i++)
        {
            if (_text[i] == '\n')
            {
                node.AddToken(kind, _subject.ToSourceStart(lineStart), _subject.ToSourceEnd(i));
                node.AddToken(SyntaxKind.LineEndingToken, _subject.ToSourceStart(i), _subject.ToSourceEnd(i + 1));
                lineStart = i + 1;
            }
        }

        if (end > lineStart)
        {
            node.AddToken(kind, _subject.ToSourceStart(lineStart), _subject.ToSourceEnd(end));
        }
    }

    private sealed class Delimiter(char character, int count, InlineNode node, bool canOpen, bool canClose)
    {
        public char Character { get; } = character;

        public int Count { get; set; } = count;

        public int OriginalCount { get; } = count;

        public InlineNode Node { get; } = node;

        public bool CanOpen { get; } = canOpen;

        public bool CanClose { get; } = canClose;

        public Delimiter? Previous { get; set; }

        public Delimiter? Next { get; set; }
    }

    private sealed class Bracket(InlineNode node, Bracket? previous, Delimiter? previousDelimiter, int index, bool image)
    {
        public InlineNode Node { get; } = node;

        public Bracket? Previous { get; } = previous;

        public Delimiter? PreviousDelimiter { get; } = previousDelimiter;

        // Where the text of the link starts, after the bracket
        public int Index { get; } = index;

        public bool Image { get; } = image;

        public bool Active { get; set; } = true;

        public bool BracketAfter { get; set; }
    }
}
