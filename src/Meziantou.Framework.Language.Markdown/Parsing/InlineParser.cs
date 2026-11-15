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
    private readonly bool _strikethrough;
    private readonly bool _subscript;
    private readonly bool _superscript;
    private readonly bool _inserted;
    private readonly bool _marked;
    private readonly bool _mathematics;
    private readonly bool _genericAttributes;
    private readonly bool _customContainers;
    private readonly bool _smartyPants;

    // The quotes of SmartyPants, which are paired at the end of the inlines, where the inlines start, and whether there are dashes
    private readonly List<InlineNode> _smartQuotes = [];
    private int _inlineStart;

    // Whether attributes are valid from each position of the subject, computed at the first {
    private byte[]? _attributesOutcomes;
    private int _attributesOutcomesStart;
    private long _abbreviationExpansion;
    private readonly bool _autolinks;
    private readonly bool _disableHtml;
    private Subject _subject = null!;
    private string _text = "";
    private int _pos;
    private int _end;
    private InlineNode _root = null!;

    // The nodes of the inlines, reused from one leaf block to the next
    private readonly List<InlineNode> _nodes = [];
    private int _usedNodes;
    private Delimiter? _delimiters;
    private Bracket? _brackets;

    public InlineParser(Dictionary<string, bool> references, MarkdownParseOptions options)
    {
        var extensions = options.Extensions;
        _disableHtml = options.DisableHtml;
        _references = references;
        _strikethrough = extensions.HasFlag(MarkdownExtensions.Strikethrough);
        _subscript = extensions.HasFlag(MarkdownExtensions.Subscript);
        _superscript = extensions.HasFlag(MarkdownExtensions.Superscript);
        _inserted = extensions.HasFlag(MarkdownExtensions.Inserted);
        _marked = extensions.HasFlag(MarkdownExtensions.Marked);
        _mathematics = extensions.HasFlag(MarkdownExtensions.Mathematics);
        _genericAttributes = extensions.HasFlag(MarkdownExtensions.GenericAttributes);
        _customContainers = extensions.HasFlag(MarkdownExtensions.CustomContainers);
        _smartyPants = extensions.HasFlag(MarkdownExtensions.SmartyPants);
        _autolinks = extensions.HasFlag(MarkdownExtensions.Autolinks);
    }

    /// <summary>Parses the inlines of the subject from <paramref name="start"/>, without the whitespace around them, or only before them.</summary>
    public void Parse(Subject subject, int start, List<BuildElement> output, bool trimEnd = true)
    {
        _subject = subject;
        _text = subject.Text;
        _end = _text.Length;
        while (trimEnd && _end > start && _text[_end - 1] is ' ' or '\t' or '\n')
        {
            _end--;
        }

        _pos = start;
        while (_pos < _end && _text[_pos] is ' ' or '\t' or '\n')
        {
            _pos++;
        }

        _usedNodes = 0;
        _inlineStart = _pos;
        _smartQuotes.Clear();
        _attributesOutcomes = null;
        _root = NewNode(SyntaxKind.Paragraph, _pos, _pos);
        _delimiters = null;
        _brackets = null;
        while (_pos < _end)
        {
            ParseInline();
        }

        ProcessEmphasis(stackBottom: null);
        if (_smartyPants)
        {
            // As in Markdig: the dashes after the emphasis, then the quotes are paired
            MarkDashes(_root);
            PairSmartQuotes();
        }

        if (_genericAttributes)
        {
            DetachAttributes(_root);
        }

        if (_autolinks)
        {
            LinkEmailAddresses(_root);
        }

        if (Abbreviations.Count > 0)
        {
            MarkAbbreviations(_root);
        }

        output.EnsureCapacity(output.Count + CountChildren(_root) + 1);
        for (var child = _root.FirstChild; child is not null; child = child.Next)
        {
            AddBuildElements(child, output);
        }
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
            case '~' when _strikethrough || _subscript:
                HandleDelimiter(c);
                break;
            case '^' when _superscript:
                HandleDelimiter(c);
                break;
            case '+' when _inserted:
                HandleDelimiter(c);
                break;
            case '=' when _marked:
                HandleDelimiter(c);
                break;
            case '{' when _genericAttributes:
                if (!ParseAttributes())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case '$' when _mathematics:
                if (!ParseMath())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case 'w' when _autolinks:
                if (!ParseWwwAutolink())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case ':' when _autolinks || _customContainers:
                if (_autolinks && ParseUrlAutolink())
                {
                    break;
                }

                if (_customContainers)
                {
                    HandleDelimiter(c);
                }
                else
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

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
            case '\'' or '"' or '>' or '.' when _smartyPants:
                if (!ParseSmartyPant())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case '<':
                if (!(_smartyPants && ParseSmartyPant()) && !ParseAutolink() && (_disableHtml || !ParseHtmlTag()))
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
        while (_pos < _end && _text[_pos] is not ('\n' or '\\' or '`' or '*' or '_' or '[' or ']' or '!' or '<' or '&') && !IsExtensionCharacter(_text[_pos]))
        {
            _pos++;
        }

        AppendText(start, _pos);
    }

    // The characters where a construct of an extension can start: a tilde, the colon of a URL, the start of www.
    private bool IsExtensionCharacter(char c) => c switch
    {
        '~' => _strikethrough || _subscript,
        '^' => _superscript,
        '+' => _inserted,
        '=' => _marked,
        '$' => _mathematics,
        '{' => _genericAttributes,
        ':' => _autolinks || _customContainers,
        '\'' or '"' or '>' or '.' => _smartyPants,
        'w' => _autolinks && _pos + 3 < _end && _text[_pos + 1] == 'w' && _text[_pos + 2] == 'w' && _text[_pos + 3] == '.',
        _ => false,
    };

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
            _root.Append(NewNode(SyntaxKind.HardLineBreakToken, spacesStart, newline + 1));
        }
        else
        {
            _root.Append(NewNode(SyntaxKind.LineEndingToken, newline, newline + 1));
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
            _root.Append(NewNode(SyntaxKind.HardLineBreakToken, start, _pos));
            while (_pos < _end && _text[_pos] == ' ')
            {
                _pos++;
            }
        }
        else if (_pos < _end && Scanners.IsEscapable(_text[_pos]))
        {
            _pos++;
            _root.Append(NewNode(SyntaxKind.EscapedCharacterToken, start, _pos));
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
                var code = NewNode(SyntaxKind.CodeSpan, start, position);
                code.Append(NewNode(SyntaxKind.BacktickStringToken, start, afterOpenTicks));
                AppendLines(code, SyntaxKind.CodeTextToken, afterOpenTicks, runStart);
                code.Append(NewNode(SyntaxKind.BacktickStringToken, runStart, position));
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

        var link = NewNode(SyntaxKind.Autolink, _pos, _pos + length);
        link.Append(NewNode(SyntaxKind.AutolinkToken, _pos, _pos + length));
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

        var html = NewNode(SyntaxKind.RawHtml, _pos, _pos + length);
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

        _root.Append(NewNode(SyntaxKind.EntityToken, _pos, _pos + length));
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

        // A run of more than two tildes is text in GFM. A run shorter than its emphasis needs is text: ++ and == need two.
        var extra = IsExtraEmphasis(c);
        if ((c == '~' && !_subscript && count > 2) || (extra && count < GetMinimumCount(c)))
        {
            AppendText(start, _pos);
            return;
        }

        // The extra emphasis read an entity as the character it stands for, and some punctuation as text: 10^-18^
        var before = start == 0 ? new Rune('\n') : extra && _root.LastChild is { Kind: SyntaxKind.EntityToken } entity && entity.End == start ? GetLastRune(HtmlEntities.Decode(_text[entity.Start..entity.End])) : GetRuneBefore(start);
        var after = _pos >= _end ? new Rune('\n') : extra && Match(Scanners.EntityRegex()) is > 0 and var entityLength ? GetFirstRune(HtmlEntities.Decode(_text.Substring(_pos, entityLength))) : GetRuneAt(_pos);
        var afterIsWhitespace = Scanners.IsUnicodeWhitespace(after);
        var afterIsPunctuation = Scanners.IsUnicodePunctuation(after) && !(extra && IsPunctuationException(after));
        var beforeIsWhitespace = Scanners.IsUnicodeWhitespace(before);
        var beforeIsPunctuation = Scanners.IsUnicodePunctuation(before);
        var leftFlanking = !afterIsWhitespace && (!afterIsPunctuation || beforeIsWhitespace || beforeIsPunctuation);
        var rightFlanking = !beforeIsWhitespace && (!(beforeIsPunctuation && !(extra && IsPunctuationException(before))) || afterIsWhitespace || Scanners.IsUnicodePunctuation(after));
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
            node.IsDelimiterRun = true;
            var delimiter = new Delimiter(c, count, node, canOpen, canClose) { Previous = _delimiters };
            if (_delimiters is not null)
            {
                _delimiters.Next = delimiter;
            }

            _delimiters = delimiter;
        }
    }

    /// <summary>Gets the normalized labels of the footnotes of the document, which the block parser adds as it reads their definitions.</summary>
    public HashSet<string> Footnotes { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the normalized labels of the headings, which a reference link without definition links to with auto-identifiers.</summary>
    public HashSet<string> HeadingLabels { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the labels of the abbreviations of the document, with the length of their title, which the block parser adds as it reads their definitions.</summary>
    public Dictionary<string, int> Abbreviations { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets how many characters the abbreviations can add to the document, so that a short text cannot render as a huge one.</summary>
    public long MaximumAbbreviationExpansion { get; set; }

    // The abbreviations of the text, as Markdig finds them once the inlines are parsed, in each of its literals: a run of text
    // tokens, which an escaped character can start. The longest label is found at the start of a literal or after whitespace,
    // and must be followed by whitespace, or by ASCII punctuation up to whitespace or the end of the literal.
    private void MarkAbbreviations(InlineNode container)
    {
        var run = new List<InlineNode>();
        var child = container.FirstChild;
        while (child is not null)
        {
            if (IsLiteral(child))
            {
                run.Clear();
                run.Add(child);
                while (run[^1].Next is { Kind: SyntaxKind.TextToken, IsDelimiterRun: false } next && next.Start == run[^1].End)
                {
                    run.Add(next);
                }

                child = run[^1].Next;
                if (!MarkAbbreviationsInLiteral(run))
                {
                    return;
                }

                continue;
            }

            if (!child.IsToken && child.Kind is not (SyntaxKind.CodeSpan or SyntaxKind.Autolink or SyntaxKind.ExtendedAutolink or SyntaxKind.RawHtml or SyntaxKind.MathInline or SyntaxKind.Abbreviation or SyntaxKind.FootnoteReference))
            {
                MarkAbbreviations(child);
            }

            child = child.Next;
        }
    }

    private static bool IsLiteral(InlineNode node) => node.Kind == SyntaxKind.EscapedCharacterToken || (node.Kind == SyntaxKind.TextToken && !node.IsDelimiterRun);

    private bool MarkAbbreviationsInLiteral(List<InlineNode> run)
    {
        // The content of the literal, where an escaped character is the character, and where each character is in the subject
        var content = new System.Text.StringBuilder();
        var positions = new List<int>();
        foreach (var token in run)
        {
            for (var position = token.Kind == SyntaxKind.EscapedCharacterToken ? token.End - 1 : token.Start; position < token.End; position++)
            {
                content.Append(_text[position]);
                positions.Add(position);
            }
        }

        var text = content.ToString();
        var after = run[^1].End < _text.Length ? _text[run[^1].End] : '\0';
        var ranges = new List<(int Start, int End)>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            // An abbreviation starts at the start of the literal or after whitespace
            if (i != start)
            {
                var whitespace = i - 1;
                while (whitespace < text.Length && !IsAbbreviationWhitespace(text[whitespace]))
                {
                    whitespace++;
                }

                if (whitespace >= text.Length - 1)
                {
                    break;
                }

                i = whitespace + 1;
            }

            var (length, titleLength) = MatchLongestAbbreviation(text, i);
            if (length == 0 || !IsValidAbbreviationEnding(text, i + length, after))
            {
                continue;
            }

            // Each abbreviation writes its title
            if (_abbreviationExpansion + titleLength > MaximumAbbreviationExpansion)
            {
                if (ranges.Count > 0)
                {
                    WrapRanges(run, ranges, SyntaxKind.Abbreviation);
                }

                return false;
            }

            _abbreviationExpansion += titleLength;

            // The escaped character at the start of the literal starts at its backslash
            var rangeStart = i == 0 && run[0].Kind == SyntaxKind.EscapedCharacterToken ? run[0].Start : positions[i];
            ranges.Add((rangeStart, positions[i + length - 1] + 1));
            start = i + length;
            i = start - 1;
        }

        if (ranges.Count > 0)
        {
            WrapRanges(run, ranges, SyntaxKind.Abbreviation);
        }

        return true;
    }

    private (int Length, int TitleLength) MatchLongestAbbreviation(string text, int start)
    {
        (int Length, int TitleLength) longest = default;
        foreach (var (label, titleLength) in Abbreviations)
        {
            if (label.Length > longest.Length && text.AsSpan(start).StartsWith(label, StringComparison.Ordinal))
            {
                longest = (label.Length, titleLength);
            }
        }

        return longest;
    }

    // After the abbreviation, ASCII punctuation up to whitespace, the end of the text, or the character after the literal
    private static bool IsValidAbbreviationEnding(string text, int index, char after)
    {
        for (; index <= text.Length; index++)
        {
            var c = index < text.Length ? text[index] : after;
            if (IsAbbreviationWhitespace(c))
            {
                break;
            }

            if (c != '\0' && !(c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAbbreviationWhitespace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    // Inline math between $ or $$, as Markdig reads it: the opening is after whitespace or punctuation, the closing has at
    // least as many $ and is before whitespace or punctuation, and spaces after the opening need spaces before the closing.
    // A $ after a backslash is text of the math. The math does not span lines.
    private bool ParseMath()
    {
        var open = _pos;
        var previous = open == 0 ? '\0' : _text[open - 1];
        if (previous == '$' || !(IsMathWhitespace(previous) || IsMathPunctuation(previous)))
        {
            return false;
        }

        var i = open + 1;
        var openDollars = 1;
        if (MathAt(i) == '$')
        {
            openDollars++;
            i++;
        }

        var c = MathAt(i);
        var openNextIsWhitespace = IsMathWhitespace(c);
        while (c is ' ' or '\t')
        {
            c = MathAt(++i);
        }

        var contentStart = i;
        var lastWhitespace = -1;
        var closeDollars = 0;
        var pc = '$';
        while (c != '\0')
        {
            if (c is '\r' or '\n')
            {
                return false;
            }

            if (pc != '\\')
            {
                if (c is ' ' or '\t')
                {
                    if (lastWhitespace < 0)
                    {
                        lastWhitespace = i;
                    }
                }
                else
                {
                    var closing = c == '$';
                    if (closing)
                    {
                        while (MathAt(i) == '$')
                        {
                            closeDollars++;
                            i++;
                        }

                        c = MathAt(i);
                    }

                    if (closeDollars >= openDollars)
                    {
                        break;
                    }

                    lastWhitespace = -1;
                    if (closing)
                    {
                        pc = '$';
                        continue;
                    }
                }
            }

            if (closeDollars > 0)
            {
                closeDollars = 0;
            }
            else
            {
                pc = c;
                c = MathAt(++i);
            }
        }

        if (closeDollars < openDollars)
        {
            return false;
        }

        var closePreviousIsWhitespace = IsMathWhitespace(pc);
        if ((!IsMathPunctuation(c) && !IsMathWhitespace(c)) || openNextIsWhitespace != closePreviousIsWhitespace)
        {
            return false;
        }

        // The content is the text between the dollars, without the whitespace around it; the $ that close beyond the
        // number that opened are text of the math when no whitespace is before them
        int contentEnd;
        int closeStart;
        if (closePreviousIsWhitespace && lastWhitespace > 0)
        {
            contentEnd = lastWhitespace;
            closeStart = i - closeDollars;
        }
        else
        {
            contentEnd = i - openDollars;
            closeStart = contentEnd;
        }

        var math = NewNode(SyntaxKind.MathInline, open, i);
        math.Append(NewNode(SyntaxKind.MathDelimiterToken, open, open + openDollars));
        if (contentEnd > contentStart)
        {
            math.Append(NewNode(SyntaxKind.MathTextToken, contentStart, contentEnd));
        }

        math.Append(NewNode(SyntaxKind.MathDelimiterToken, closeStart, i));
        _root.Append(math);
        _pos = i;
        return true;
    }

    private char MathAt(int index) => index < _end ? _text[index] : '\0';

    // Attributes after a delimiter that is text, or after a bracket that opens no link, are for that delimiter in Markdig, which
    // writes them nowhere
    private static void DetachAttributes(InlineNode container)
    {
        InlineNode? previous = null;
        for (var child = container.FirstChild; child is not null; child = child.Next)
        {
            if (child.Kind == SyntaxKind.GenericAttributes)
            {
                if (previous is { IsDelimiterRun: true } or { Kind: SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken } && child.FirstChild is { Value: 0 } first)
                {
                    first.Value = 1;
                }

                continue;
            }

            if (!child.IsToken)
            {
                DetachAttributes(child);
            }

            previous = child;
        }
    }

    // Generic attributes: {#id .class name=value}, with the line ending after them. They apply to the inline before them, or to
    // the block when the inline before them is text; the whitespace before attributes that end the text is not text.
    private bool ParseAttributes()
    {
        if (_attributesOutcomes is null)
        {
            _attributesOutcomesStart = _pos;
            _attributesOutcomes = AttributesScanner.ComputeOutcomes(_text, _pos, _end);
        }

        if (!AttributesScanner.IsValid(_attributesOutcomes, _attributesOutcomesStart, _pos) || AttributesScanner.TryParse(_text, _pos, _end, out _) is not (>= 0 and var end))
        {
            return false;
        }

        // The text before attributes that end the text, and before the attributes before them, ends with no whitespace
        var afterText = false;
        if (_text.AsSpan(end, _end - end).IndexOfAnyExcept(" \t\n\v\f\r") < 0)
        {
            while (GetLastNonAttributes(_root) is { Kind: SyntaxKind.TextToken, IsDelimiterRun: false } text)
            {
                afterText = true;
                while (text.End > text.Start && _text[text.End - 1] is ' ' or '\t' or '\v' or '\f')
                {
                    text.End--;
                }

                if (text.End > text.Start)
                {
                    break;
                }

                // The attributes after the whitespace were after text: they are for the block
                for (var next = text.Next; next is not null; next = next.Next)
                {
                    if (next.FirstChild is { Value: 0 } token)
                    {
                        token.Value = 2;
                    }
                }

                text.Unlink();
            }
        }

        var attributes = NewNode(SyntaxKind.GenericAttributes, _pos, end);
        AppendLines(attributes, SyntaxKind.AttributesToken, _pos, end);
        if (afterText && attributes.FirstChild is { } first && (GetLastNonAttributes(_root) is not { Kind: SyntaxKind.TextToken } last || last.End < _pos))
        {
            // The whitespace before them was text: they are for the block
            first.Value = 2;
        }

        _root.Append(attributes);
        _pos = end;
        return true;
    }

    private char CharAt(int position) => position < _end ? _text[position] : '\0';

    // SmartyPants, as Markdig reads it: a quote that can open or close as _ can, a pair of angle quotes, an ellipsis that ends a word
    private bool ParseSmartyPant()
    {
        var start = _pos;
        var c = _text[start];
        MarkdownSmartyPant pant;
        int end;
        switch (c)
        {
            case '\'' or '"':
                var isDouble = c == '"' || CharAt(start + 1) == '\'';
                end = start + (c == '\'' && isDouble ? 2 : 1);
                pant = isDouble ? MarkdownSmartyPant.LeftDoubleQuote : MarkdownSmartyPant.LeftQuote;
                break;
            case '<' or '>' when CharAt(start + 1) == c:
                end = start + 2;
                pant = c == '<' ? MarkdownSmartyPant.LeftAngleQuote : MarkdownSmartyPant.RightAngleQuote;
                break;
            case '.' when CharAt(start + 1) == '.' && CharAt(start + 2) == '.':
                end = start + 3;
                pant = MarkdownSmartyPant.Ellipsis;
                break;
            default:
                return false;
        }

        GetSmartyPantFlanking(start, end, out var canOpen, out var canClose);
        switch (pant)
        {
            case MarkdownSmartyPant.LeftQuote or MarkdownSmartyPant.LeftDoubleQuote:
                if (canOpen == canClose)
                {
                    return false;
                }

                if (canClose)
                {
                    pant = pant == MarkdownSmartyPant.LeftQuote ? MarkdownSmartyPant.RightQuote : MarkdownSmartyPant.RightDoubleQuote;
                }

                break;
            case MarkdownSmartyPant.LeftAngleQuote when !canOpen || canClose:
            case MarkdownSmartyPant.RightAngleQuote or MarkdownSmartyPant.Ellipsis when canOpen || !canClose:
                return false;
        }

        var node = NewNode(SyntaxKind.SmartyPantToken, start, end);
        node.Value = (int)pant;
        _root.Append(node);
        if (pant != MarkdownSmartyPant.Ellipsis)
        {
            _smartQuotes.Add(node);
        }

        _pos = end;
        return true;
    }

    // The start and the end of the text are whitespace and punctuation for Markdig
    private void GetSmartyPantFlanking(int start, int end, out bool canOpen, out bool canClose)
    {
        var beforeIsWhitespace = true;
        var beforeIsPunctuation = true;
        var beforeIsException = false;
        if (start > _inlineStart)
        {
            var before = GetRuneBefore(start);
            beforeIsWhitespace = Scanners.IsUnicodeWhitespace(before);
            beforeIsPunctuation = !beforeIsWhitespace && Scanners.IsUnicodePunctuation(before);
            beforeIsException = beforeIsPunctuation && IsPunctuationException(before);
        }

        var afterIsWhitespace = true;
        var afterIsPunctuation = true;
        var afterIsException = false;
        if (end < _end)
        {
            var after = GetRuneAt(end);
            afterIsWhitespace = Scanners.IsUnicodeWhitespace(after);
            afterIsPunctuation = !afterIsWhitespace && Scanners.IsUnicodePunctuation(after);
            afterIsException = afterIsPunctuation && IsPunctuationException(after);
        }

        var leftFlanking = !afterIsWhitespace && (!afterIsPunctuation || afterIsException || beforeIsWhitespace || beforeIsPunctuation);
        var rightFlanking = !beforeIsWhitespace && (!beforeIsPunctuation || beforeIsException || afterIsWhitespace || afterIsPunctuation);
        canOpen = leftFlanking && (!rightFlanking || beforeIsPunctuation);
        canClose = rightFlanking && (!leftFlanking || afterIsPunctuation);
    }

    // A closing quote closes the last opening quote of its kind, and the quotes that are opened after it are text
    private void PairSmartQuotes()
    {
        var openers = new Stack<InlineNode>();
        foreach (var quote in _smartQuotes)
        {
            var pant = (MarkdownSmartyPant)quote.Value;
            if (pant is MarkdownSmartyPant.LeftQuote or MarkdownSmartyPant.LeftDoubleQuote or MarkdownSmartyPant.LeftAngleQuote)
            {
                openers.Push(quote);
                continue;
            }

            var opening = pant switch
            {
                MarkdownSmartyPant.RightQuote => MarkdownSmartyPant.LeftQuote,
                MarkdownSmartyPant.RightDoubleQuote => MarkdownSmartyPant.LeftDoubleQuote,
                _ => MarkdownSmartyPant.LeftAngleQuote,
            };
            var found = false;
            while (openers.TryPop(out var opener))
            {
                if ((MarkdownSmartyPant)opener.Value == opening)
                {
                    found = true;
                    break;
                }

                ToSmartyPantText(opener);
            }

            if (!found)
            {
                ToSmartyPantText(quote);
            }
        }

        foreach (var opener in openers)
        {
            ToSmartyPantText(opener);
        }
    }

    // A quote that Markdig writes as text, in a literal of its own
    private static void ToSmartyPantText(InlineNode quote)
    {
        quote.ChangeKind(SyntaxKind.TextToken);
        quote.Value = 0;
        quote.IsDelimiterRun = true;
    }

    // The dashes of the literals of Markdig: -- and ---, except after an escape that starts the literal
    private void MarkDashes(InlineNode container)
    {
        var child = container.FirstChild;
        while (child is not null)
        {
            if (IsLiteral(child))
            {
                var first = child;
                var last = child;
                while (last.Next is { Kind: SyntaxKind.TextToken, IsDelimiterRun: false } next && next.Start == last.End)
                {
                    last = next;
                }

                child = last.Next;
                MarkDashesInLiteral(first, last);
                continue;
            }

            if (!child.IsToken && child.Kind is not (SyntaxKind.CodeSpan or SyntaxKind.Autolink or SyntaxKind.ExtendedAutolink or SyntaxKind.RawHtml or SyntaxKind.MathInline or SyntaxKind.FootnoteReference))
            {
                MarkDashes(child);
            }

            child = child.Next;
        }
    }

    // Replaces the text of the literal by its text and its dashes. An escaped dash is never the start of a dash.
    private void MarkDashesInLiteral(InlineNode first, InlineNode last)
    {
        var escaped = first.Kind == SyntaxKind.EscapedCharacterToken;
        var start = escaped ? first.End : first.Start;
        var end = last.End;
        var position = _text.AsSpan(start, end - start).IndexOf("--", StringComparison.Ordinal);
        if (position < 0)
        {
            return;
        }

        var parent = first.Parent!;
        var anchor = escaped ? first : first.Previous;
        var token = escaped ? first.Next : first;
        while (token is not null)
        {
            var next = token.Next;
            token.Unlink();
            if (token == last)
            {
                break;
            }

            token = next;
        }

        var textStart = start;
        while (position >= 0)
        {
            var dashStart = start + position;
            var dashEnd = dashStart + (dashStart + 2 < end && _text[dashStart + 2] == '-' ? 3 : 2);
            if (dashStart > textStart)
            {
                anchor = Insert(parent, anchor, NewNode(SyntaxKind.TextToken, textStart, dashStart));
            }

            var dash = NewNode(SyntaxKind.SmartyPantToken, dashStart, dashEnd);
            dash.Value = (int)(dashEnd - dashStart == 3 ? MarkdownSmartyPant.EmDash : MarkdownSmartyPant.EnDash);
            anchor = Insert(parent, anchor, dash);
            textStart = dashEnd;
            start = dashEnd;
            position = _text.AsSpan(start, end - start).IndexOf("--", StringComparison.Ordinal);
        }

        if (textStart < end)
        {
            Insert(parent, anchor, NewNode(SyntaxKind.TextToken, textStart, end));
        }

        static InlineNode Insert(InlineNode parent, InlineNode? anchor, InlineNode node)
        {
            if (anchor is null)
            {
                parent.Prepend(node);
            }
            else
            {
                anchor.InsertAfter(node);
            }

            return node;
        }
    }

    private static InlineNode? GetLastNonAttributes(InlineNode parent)
    {
        var node = parent.LastChild;
        while (node is { Kind: SyntaxKind.GenericAttributes })
        {
            node = node.Previous;
        }

        return node;
    }

    // As Markdig: the end of the text is whitespace, and an ASCII punctuation character or a Unicode one is punctuation
    private static bool IsMathWhitespace(char c) => c is '\0' or ' ' or '\t' or '\n' or '\r' or '\v' or '\f' || (c > 127 && char.IsWhiteSpace(c));

    private static bool IsMathPunctuation(char c) => c == '\0' || (c <= 127 ? char.IsAsciiLetterOrDigit(c) is false && c > ' ' && c != '\x7f' : char.IsPunctuation(c) || char.IsSymbol(c));

    // The extra emphasis of Markdig: ~ when subscript is on, ^, ++, == and the :: of custom containers
    private bool IsExtraEmphasis(char c) => c switch
    {
        '~' => _subscript,
        '^' or '+' or '=' or ':' => true,
        _ => false,
    };

    private int GetMinimumCount(char c) => c switch
    {
        '~' when _subscript => 1,
        '~' => 2,
        '+' or '=' or ':' => 2,
        _ => 1,
    };

    private int GetMaximumCount(char c) => c switch
    {
        '~' => _strikethrough ? 2 : 1,
        '^' => 1,
        _ => 2,
    };

    private static bool IsPunctuationException(Rune rune) => rune.Value is '\u2212' or '-' or '+' or '\u2020' or '\u2021';

    private static Rune GetFirstRune(string text) => Rune.DecodeFromUtf16(text, out var rune, out _) == System.Buffers.OperationStatus.Done ? rune : Rune.ReplacementChar;

    private static Rune GetLastRune(string text) => Rune.DecodeLastFromUtf16(text, out var rune, out _) == System.Buffers.OperationStatus.Done ? rune : Rune.ReplacementChar;

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
        var node = NewNode(image ? SyntaxKind.ExclamationOpenBracketToken : SyntaxKind.OpenBracketToken, start, _pos);
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
        var footnote = false;

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
                    suffix.Add(NewNode(SyntaxKind.OpenParenthesisToken, openParenthesis, openParenthesis + 1));
                    if (destinationEnd > destinationStart)
                    {
                        suffix.Add(NewNode(SyntaxKind.LinkDestinationToken, destinationStart, destinationEnd));
                    }

                    if (titleStart >= 0)
                    {
                        AddLineNodes(suffix, SyntaxKind.LinkTitleToken, titleStart, titleEnd);
                    }

                    suffix.Add(NewNode(SyntaxKind.CloseParenthesisToken, _pos, _pos + 1));
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

            // A label of a footnote makes a reference to the footnote, as in Markdig, an image included
            var normalized = label is null ? null : Scanners.NormalizeLabel(label);
            if (normalized is not null && (_references.ContainsKey(normalized) || (footnote = Footnotes.Contains(normalized)) || (HeadingLabels.Contains(normalized) && !HasActiveBracket(opener.Previous))))
            {
                matched = true;
                if (labelLength >= 2)
                {
                    suffix.Add(NewNode(SyntaxKind.OpenBracketToken, beforeLabel, beforeLabel + 1));
                    if (labelLength > 2)
                    {
                        AddLineNodes(suffix, SyntaxKind.LinkLabelToken, beforeLabel + 1, beforeLabel + labelLength - 1);
                    }

                    suffix.Add(NewNode(SyntaxKind.CloseBracketToken, beforeLabel + labelLength - 1, beforeLabel + labelLength));
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

        var link = NewNode(footnote ? SyntaxKind.FootnoteReference : opener.Image ? SyntaxKind.Image : SyntaxKind.Link, opener.Node.Start, _pos);
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
        link.Append(NewNode(SyntaxKind.CloseBracketToken, closeStart, closeStart + 1));
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

    // As in Markdig, a heading is not linked to inside a bracket that can still be a link or an image, as in [text [Heading]](url)
    private static bool HasActiveBracket(Bracket? bracket)
    {
        for (; bracket is not null; bracket = bracket.Previous)
        {
            if (bracket.Active)
            {
                return true;
            }
        }

        return false;
    }

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
        var node = NewNode(SyntaxKind.TextToken, start, end);
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
                nodes.Add(NewNode(kind, lineStart, i));
                nodes.Add(NewNode(SyntaxKind.LineEndingToken, i, i + 1));
                lineStart = i + 1;
            }
        }

        nodes.Add(NewNode(kind, lineStart, end));
    }

    private void ProcessEmphasis(Delimiter? stackBottom)
    {
        if (_delimiters == stackBottom)
        {
            return;
        }

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

            // What is left of a run after a match can be too short for its emphasis: it is text
            var extra = IsExtraEmphasis(closer.Character);
            if (extra && closer.Count < GetMinimumCount(closer.Character))
            {
                var next = closer.Next;
                RemoveDelimiter(closer);
                closer = next;
                continue;
            }

            var bottomIndex = (closer.Character, (closer.CanOpen ? 3 : 0) + (closer.OriginalCount % 3));
            var bottom = openersBottom.GetValueOrDefault(bottomIndex, stackBottom);
            var opener = closer.Previous;
            var openerFound = false;
            while (opener is not null && opener != stackBottom && opener != bottom)
            {
                var oddMatch = (closer.CanOpen || opener.CanClose) && closer.OriginalCount % 3 != 0 && (opener.OriginalCount + closer.OriginalCount) % 3 == 0;
                if (opener.Character == closer.Character && opener.CanOpen && !oddMatch && (!extra || opener.Count >= GetMinimumCount(opener.Character)))
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

            // A strikethrough is between runs of the same length: other runs of tildes are text
            var tilde = closer.Character == '~' && !extra;
            if (tilde && opener!.Count != closer.Count)
            {
                closer = closer.Next;
                RemoveDelimiter(oldCloser);
                RemoveDelimiter(opener);
                continue;
            }

            var use = tilde ? closer.Count
                : extra ? Math.Min(Math.Min(opener!.Count, closer.Count), GetMaximumCount(closer.Character))
                : closer.Count >= 2 && opener!.Count >= 2 ? 2 : 1;
            var openerNode = opener!.Node;
            var closerNode = closer.Node;
            opener.Count -= use;
            closer.Count -= use;

            // The delimiters used are the ones next to the content: the end of the opener and the start of the closer
            var kind = closer.Character switch
            {
                '~' when tilde || use == 2 => SyntaxKind.Strikethrough,
                '~' => SyntaxKind.Subscript,
                '^' => SyntaxKind.Superscript,
                '+' => SyntaxKind.Inserted,
                '=' => SyntaxKind.Marked,
                ':' => SyntaxKind.CustomContainerInline,
                _ => use == 1 ? SyntaxKind.Emphasis : SyntaxKind.StrongEmphasis,
            };
            var delimiterKind = kind == SyntaxKind.Strikethrough ? SyntaxKind.StrikethroughDelimiterToken : SyntaxKind.EmphasisDelimiterToken;
            var emphasis = NewNode(kind, openerNode.End - use, closerNode.Start + use);
            emphasis.Append(NewNode(delimiterKind, openerNode.End - use, openerNode.End));
            openerNode.End -= use;
            var current = openerNode.Next;
            while (current is not null && current != closerNode)
            {
                var next = current.Next;
                current.Unlink();
                emphasis.Append(current);
                current = next;
            }

            emphasis.Append(NewNode(delimiterKind, closerNode.Start, closerNode.Start + use));
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

    // An extended www autolink: www. and a domain, after the start of the text, whitespace, or one of *, _, ~ and (
    private bool ParseWwwAutolink()
    {
        if (_brackets is not null || (_pos > 0 && _text[_pos - 1] is not ('*' or '_' or '~' or '(') && !IsSpace(_text[_pos - 1])))
        {
            return false;
        }

        if (_end - _pos < 4 || !_text.AsSpan(_pos).StartsWith("www.", StringComparison.Ordinal) || CheckDomain(_pos, _end - _pos, allowShort: false) == 0)
        {
            return false;
        }

        var length = AutolinkEnd(_pos, _pos);
        if (length == 0)
        {
            return false;
        }

        AppendAutolink(_pos, _pos + length);
        return true;
    }

    // An extended URL autolink: http://, https:// or ftp://, and a domain. The scheme is already text, which the link takes back.
    private bool ParseUrlAutolink()
    {
        if (_brackets is not null || _end - _pos < 4 || _text[_pos + 1] != '/' || _text[_pos + 2] != '/' || _root.LastChild is not { Kind: SyntaxKind.TextToken } scheme || scheme.End != _pos)
        {
            return false;
        }

        var start = _pos;
        while (start > scheme.Start && char.IsAsciiLetter(_text[start - 1]))
        {
            start--;
        }

        if (!IsSafeScheme(start) || CheckDomain(_pos + 3, _end - _pos - 3, allowShort: true) == 0)
        {
            return false;
        }

        var length = AutolinkEnd(_pos, _pos + 3);
        if (length == 0)
        {
            return false;
        }

        scheme.End = start;
        if (scheme.End == scheme.Start)
        {
            scheme.Unlink();
        }

        AppendAutolink(start, _pos + length);
        return true;
    }

    private bool IsSafeScheme(int start)
    {
        foreach (var scheme in (ReadOnlySpan<string>)["http://", "https://", "ftp://"])
        {
            if (_end - start > scheme.Length && _text.AsSpan(start, scheme.Length).Equals(scheme, StringComparison.OrdinalIgnoreCase) && char.IsAsciiLetterOrDigit(_text[start + scheme.Length]))
            {
                return true;
            }
        }

        return false;
    }

    private void AppendAutolink(int start, int end)
    {
        var link = NewNode(SyntaxKind.ExtendedAutolink, start, end);
        link.Append(NewNode(SyntaxKind.ExtendedAutolinkToken, start, end));
        _root.Append(link);
        _pos = end;
    }

    // The length of the link that starts at data: up to whitespace or <, without the punctuation at its end. Returns 0 when nothing
    // is left.
    private int AutolinkEnd(int data, int from)
    {
        var end = from;
        while (end < _end && !IsSpace(_text[end]) && _text[end] != '<')
        {
            end++;
        }

        return TrimAutolink(_text, data, end - data);
    }

    // Whether the text at data is a domain: segments of characters that are not whitespace or punctuation, or are - or _, separated
    // by dots, with no _ in the last two segments. The first character is not read, and neither is the last one of the
    // paragraph, as in cmark-gfm.
    private int CheckDomain(int data, int size, bool allowShort)
    {
        var dots = 0;
        var underscoresBefore = 0;
        var underscores = 0;
        int i;
        for (i = 1; i < size - 1; i++)
        {
            if (_text[data + i] == '\\' && i < size - 2)
            {
                i++;
            }

            var c = _text[data + i];
            if (c == '_')
            {
                underscores++;
            }
            else if (c == '.')
            {
                underscoresBefore = underscores;
                underscores = 0;
                dots++;
            }
            else if (c != '-' && !IsHostCharacter(c))
            {
                break;
            }
        }

        if (underscoresBefore > 0 || underscores > 0)
        {
            return 0;
        }

        return allowShort || dots > 0 ? i : 0;
    }

    // cmark-gfm reads the domain byte by byte in UTF-8, so it stops after the first byte of a character that is not ASCII
    private static bool IsHostCharacter(char c) => c < 0x80 && !IsSpace(c) && !(c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~'));

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    // Removes the punctuation at the end of a link: ?, !, ., ,, :, *, _, ~, a ) that is not matched, and an entity reference.
    // Returns the new length.
    private static int TrimAutolink(string text, int data, int length)
    {
        var opening = 0;
        var closing = 0;
        for (var i = 0; i < length; i++)
        {
            if (text[data + i] == '(')
            {
                opening++;
            }
            else if (text[data + i] == ')')
            {
                closing++;
            }
        }

        while (length > 0)
        {
            switch (text[data + length - 1])
            {
                case ')':
                    if (closing <= opening)
                    {
                        return length;
                    }

                    closing--;
                    length--;
                    break;

                case '?' or '!' or '.' or ',' or ':' or '*' or '_' or '~':
                    length--;
                    break;

                case ';':
                {
                    // &name; at the end looks like an entity reference
                    var start = length - 2;
                    while (start > 0 && char.IsAsciiLetterOrDigit(text[data + start]))
                    {
                        start--;
                    }

                    length = start < length - 2 && text[data + start] == '&' ? start : length - 1;
                    break;
                }

                default:
                    return length;
            }
        }

        return length;
    }

    // The email addresses of the text become extended autolinks, as cmark-gfm does once the inlines are parsed: a run of text,
    // escaped characters and entities is read as the text it renders as.
    private void LinkEmailAddresses(InlineNode container)
    {
        var run = new List<InlineNode>();
        var child = container.FirstChild;
        while (child is not null)
        {
            var next = child.Next;
            if (child.Kind is SyntaxKind.TextToken or SyntaxKind.EscapedCharacterToken or SyntaxKind.EntityToken)
            {
                run.Add(child);
            }
            else
            {
                LinkEmailAddresses(run);
                run.Clear();
                if (child.Kind is SyntaxKind.Emphasis or SyntaxKind.StrongEmphasis or SyntaxKind.Strikethrough or SyntaxKind.Subscript or SyntaxKind.Superscript or SyntaxKind.Inserted or SyntaxKind.Marked or SyntaxKind.CustomContainerInline or SyntaxKind.Image)
                {
                    LinkEmailAddresses(child);
                }
            }

            child = next;
        }

        LinkEmailAddresses(run);
    }

    private void LinkEmailAddresses(List<InlineNode> run)
    {
        // An address has an @, which an entity can be too
        if (!MayContainAt(run))
        {
            return;
        }

        // The rendered text of the run, and for each of its characters, the token it comes from and its position in a text token
        var text = new StringBuilder();
        var origins = new List<(int Token, int Position)>();
        for (var i = 0; i < run.Count; i++)
        {
            var token = run[i];
            switch (token.Kind)
            {
                case SyntaxKind.TextToken:
                    for (var position = token.Start; position < token.End; position++)
                    {
                        text.Append(_text[position]);
                        origins.Add((i, position));
                    }

                    break;

                case SyntaxKind.EscapedCharacterToken:
                    text.Append(_text[token.End - 1]);
                    origins.Add((i, -1));
                    break;

                default:
                    foreach (var c in HtmlEntities.Decode(_text[token.Start..token.End]))
                    {
                        text.Append(c);
                        origins.Add((i, -1));
                    }

                    break;
            }
        }

        var matches = new List<(int Start, int End)>();
        foreach (var (start, end) in FindEmailAddresses(text.ToString()))
        {
            // A link starts and ends at the edges of the tokens, or in a text token
            var first = origins[start];
            var last = origins[end - 1];
            var startsAtEdge = first.Position >= 0 || start == 0 || origins[start - 1].Token != first.Token;
            var endsAtEdge = last.Position >= 0 || end == origins.Count || origins[end].Token != last.Token;
            if (startsAtEdge && endsAtEdge)
            {
                matches.Add((first.Position >= 0 ? first.Position : run[first.Token].Start, last.Position >= 0 ? last.Position + 1 : run[last.Token].End));
            }
        }

        if (matches.Count == 0)
        {
            return;
        }

        WrapRanges(run, matches, SyntaxKind.ExtendedAutolink);
    }

    // Replaces the tokens of a run by the same tokens split at the edges of the ranges, the ones in a range being the children
    // of a node of the kind
    private void WrapRanges(List<InlineNode> run, List<(int Start, int End)> ranges, SyntaxKind kind)
    {
        var nodes = new List<InlineNode>();
        var rangeIndex = 0;
        InlineNode? wrapper = null;
        foreach (var token in run)
        {
            var position = token.Start;
            while (position < token.End)
            {
                InlineNode piece;
                if (rangeIndex < ranges.Count && position >= ranges[rangeIndex].Start)
                {
                    if (wrapper is null)
                    {
                        wrapper = NewNode(kind, ranges[rangeIndex].Start, ranges[rangeIndex].End);
                        nodes.Add(wrapper);
                    }

                    var end = Math.Min(token.End, ranges[rangeIndex].End);
                    piece = NewNode(token.Kind, position, end);
                    wrapper.Append(piece);
                    position = end;
                    if (position >= ranges[rangeIndex].End)
                    {
                        rangeIndex++;
                        wrapper = null;
                    }
                }
                else
                {
                    var end = rangeIndex < ranges.Count ? Math.Min(token.End, ranges[rangeIndex].Start) : token.End;
                    piece = NewNode(token.Kind, position, end);
                    nodes.Add(piece);
                    position = end;
                }

                piece.IsDelimiterRun = token.IsDelimiterRun;
            }
        }

        var anchor = run[0];
        foreach (var node in nodes)
        {
            anchor.InsertAfter(node);
            anchor = node;
        }

        foreach (var token in run)
        {
            token.Unlink();
        }
    }

    private bool MayContainAt(List<InlineNode> run)
    {
        foreach (var token in run)
        {
            if (token.Kind == SyntaxKind.EntityToken || _text.AsSpan(token.Start, token.End - token.Start).Contains('@'))
            {
                return true;
            }
        }

        return false;
    }

    // The email addresses of a text, as cmark-gfm finds them: letters, digits, ., +, - and _ before an @, and segments of letters,
    // digits, - and _ separated by dots after it, which end with a letter
    private static List<(int Start, int End)> FindEmailAddresses(string text)
    {
        var result = new List<(int Start, int End)>();
        var lower = 0;
        var at = text.IndexOf('@', StringComparison.Ordinal);
        while (at >= 0)
        {
            var start = at;
            while (start > lower && (char.IsAsciiLetterOrDigit(text[start - 1]) || text[start - 1] is '.' or '+' or '-' or '_'))
            {
                start--;
            }

            if (start == at)
            {
                lower = at + 1;
                at = text.IndexOf('@', lower, StringComparison.Ordinal);
                continue;
            }

            var length = 1;
            var dots = 0;
            var nextAt = -1;
            for (; at + length < text.Length; length++)
            {
                var c = text[at + length];
                if (char.IsAsciiLetterOrDigit(c))
                {
                    continue;
                }

                if (c == '@')
                {
                    nextAt = at + length;
                    break;
                }

                if (c == '.' && at + length + 1 < text.Length && char.IsAsciiLetterOrDigit(text[at + length + 1]))
                {
                    dots++;
                }
                else if (c is not ('-' or '_'))
                {
                    break;
                }
            }

            // Another @: the address is the one before it, if any
            if (nextAt >= 0)
            {
                lower = at + 1;
                at = nextAt;
                continue;
            }

            var lastCharacter = text[at + length - 1];
            if (length < 2 || dots == 0 || (!char.IsAsciiLetter(lastCharacter) && lastCharacter != '.'))
            {
                lower = at + length;
                at = text.IndexOf('@', lower, StringComparison.Ordinal);
                continue;
            }

            length = TrimAutolink(text, at, length);
            if (length == 0)
            {
                lower = at + 1;
                at = text.IndexOf('@', lower, StringComparison.Ordinal);
                continue;
            }

            result.Add((start, at + length));
            lower = at + length;
            at = text.IndexOf('@', lower, StringComparison.Ordinal);
        }

        return result;
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
                result.Add(BuildElement.Token(node.Kind, _subject.ToSourceStart(node.Start), _subject.ToSourceEnd(node.End), node.Value));
            }

            return;
        }

        var buildNode = new BuildNode(node.Kind, CountChildren(node));
        for (var child = node.FirstChild; child is not null; child = child.Next)
        {
            AddBuildElements(child, buildNode.Children);
        }

        result.Add(buildNode);
    }

    private static int CountChildren(InlineNode node)
    {
        var count = 0;
        for (var child = node.FirstChild; child is not null; child = child.Next)
        {
            count++;
        }

        return count;
    }

    private InlineNode NewNode(SyntaxKind kind, int start, int end)
    {
        if (_usedNodes == _nodes.Count)
        {
            _nodes.Add(new InlineNode());
        }

        return _nodes[_usedNodes++].Reset(kind, start, end);
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
