using System.Globalization;

namespace Meziantou.Framework.Mermaid.Internals.XyChartDiagrams;

/// <summary>
/// The lexer of the XY charts of Mermaid (<c>xychart.jison</c>): its rules apply in order, the first that matches wins,
/// and the keywords ignore the case. States change which rules apply: after <c>x-axis</c> the numbers are values and a
/// line feed ends the axis, inside brackets the numbers are data points, inside quotes everything is text.
/// </summary>
internal sealed class XyChartLexer
{
    private readonly ParseContext _context;
    private readonly string _text;
    private readonly List<XyChartLexerState> _states = [];
    private int _position;

    public XyChartLexer(ParseContext context, int position)
    {
        _context = context;

        // Mermaid adds a line feed after the text
        _text = context.Text + "\n";
        _position = position;
    }

    private XyChartLexerState State => _states.Count is 0 ? XyChartLexerState.Initial : _states[^1];

    public XyChartToken Next()
    {
        while (true)
        {
            if (_position >= _text.Length)
                return new XyChartToken(XyChartTokenKind.EndOfFile, string.Empty, _text.Length - 1);

            if (TryRead() is { } token)
                return token;
        }
    }

    /// <summary>Applies the first rule that matches: returns its token, or <see langword="null"/> for a rule that skips its text.</summary>
    private XyChartToken? TryRead()
    {
        var start = _position;
        var state = State;
        switch (state)
        {
            case XyChartLexerState.String:
                if (_text[_position] == '"')
                {
                    Pop();
                    _position++;
                    return null;
                }

                var end = _text.IndexOfOrdinal('"', _position);
                _position = end < 0 ? _text.Length : end;
                return new XyChartToken(XyChartTokenKind.String, _text[start.._position], start);

            case XyChartLexerState.MarkdownString:
                if (StartsWith("`\""))
                {
                    Pop();
                    _position += 2;
                    return null;
                }

                while (_position < _text.Length && !StartsWith("`\"") && !IsLineTerminator(_text[_position]))
                {
                    _position++;
                }

                if (_position == start)
                    throw Unexpected(start);

                return new XyChartToken(XyChartTokenKind.MarkdownString, _text[start.._position], start);

            case XyChartLexerState.AccessibleTitle or XyChartLexerState.AccessibleDescription:
                Pop();
                _position = _text.IndexOfOrdinal('\n', _position);
                return new XyChartToken(state is XyChartLexerState.AccessibleTitle ? XyChartTokenKind.AccessibleTitle : XyChartTokenKind.AccessibleDescription, _text[start.._position], start);

            case XyChartLexerState.AccessibleDescriptionBlock:
                if (_text[_position] == '}')
                {
                    Pop();
                    _position++;
                    return null;
                }

                var close = _text.IndexOfOrdinal('}', _position);
                _position = close < 0 ? _text.Length : close;
                return new XyChartToken(XyChartTokenKind.AccessibleDescription, _text[start.._position], start);
        }

        var c = _text[_position];

        // Comments: %% to the end of the line, and the character before them
        if (StartsWith("%%") && Peek(2) != '{')
        {
            SkipToEndOfLine();
            return null;
        }

        if (c != '}' && Peek(1) == '%' && Peek(2) == '%')
        {
            _position++;
            SkipToEndOfLine();
            return null;
        }

        if (c is '\n' or '\r')
        {
            if (state is XyChartLexerState.AxisData or XyChartLexerState.Data)
            {
                // A line feed ends an axis or a series
                if (c == '\r' && Peek(1) != '\n')
                    return ReadLineFeeds(start);

                _position += c == '\r' ? 2 : 1;
                Pop();
                return new XyChartToken(XyChartTokenKind.NewLine, "\n", start);
            }

            return ReadLineFeeds(start);
        }

        if (StartsWith("%%"))
        {
            SkipToEndOfLine();
            return null;
        }

        if (TryKeyword("title", XyChartTokenKind.Title) is { } title)
            return title;

        if (TryAccessibility("accTitle", ':', XyChartLexerState.AccessibleTitle) || TryAccessibility("accDescr", ':', XyChartLexerState.AccessibleDescription) || TryAccessibility("accDescr", '{', XyChartLexerState.AccessibleDescriptionBlock))
            return null;

        if ((TryKeyword("xychart-beta", XyChartTokenKind.XyChart) ?? TryKeyword("xychart", XyChartTokenKind.XyChart) ?? TryKeyword("vertical", XyChartTokenKind.Orientation, wordBoundary: false) ?? TryKeyword("horizontal", XyChartTokenKind.Orientation, wordBoundary: false)) is { } keyword)
            return keyword;

        if (TryKeyword("x-axis", XyChartTokenKind.XAxis) is { } xAxis)
        {
            Push(XyChartLexerState.AxisData);
            return xAxis;
        }

        if (TryKeyword("y-axis", XyChartTokenKind.YAxis) is { } yAxis)
        {
            Push(XyChartLexerState.AxisData);
            return yAxis;
        }

        if (state is XyChartLexerState.AxisData && c == '[')
        {
            _position++;
            Push(XyChartLexerState.AxisBandData);
            return new XyChartToken(XyChartTokenKind.OpenBracket, "[", start);
        }

        if (state is XyChartLexerState.AxisData && StartsWith("-->"))
        {
            _position += 3;
            return new XyChartToken(XyChartTokenKind.Arrow, "-->", start);
        }

        if (TryKeyword("line", XyChartTokenKind.Line) is { } line)
        {
            Push(XyChartLexerState.Data);
            return line;
        }

        if (TryKeyword("bar", XyChartTokenKind.Bar) is { } bar)
        {
            Push(XyChartLexerState.Data);
            return bar;
        }

        if (state is XyChartLexerState.Data && c == '[')
        {
            _position++;
            Push(XyChartLexerState.DataInner);
            return new XyChartToken(XyChartTokenKind.OpenBracket, "[", start);
        }

        if (state is XyChartLexerState.AxisData or XyChartLexerState.DataInner && TryNumber() is { } number)
            return number;

        if (state is XyChartLexerState.DataInner or XyChartLexerState.AxisBandData && c == ']')
        {
            _position++;
            Pop();
            return new XyChartToken(XyChartTokenKind.CloseBracket, "]", start);
        }

        if (StartsWith("\"`"))
        {
            _position += 2;
            Push(XyChartLexerState.MarkdownString);
            return null;
        }

        if (c == '"')
        {
            _position++;
            Push(XyChartLexerState.String);
            return null;
        }

        if (c is '[' or ']')
        {
            _position++;
            return new XyChartToken(c == '[' ? XyChartTokenKind.OpenBracket : XyChartTokenKind.CloseBracket, c.ToString(), start);
        }

        if (char.IsAsciiLetter(c))
        {
            while (_position < _text.Length && char.IsAsciiLetter(_text[_position]))
            {
                _position++;
            }

            return new XyChartToken(XyChartTokenKind.Alpha, _text[start.._position], start);
        }

        // Mermaid replaces the entity codes before it reads the text: out of quotes, they are not valid
        if (c == '#' && IsEntityCode(_text, _position))
            throw _context.Error(start, "An entity code such as '#35;' must be in a quoted text.");

        var kind = c switch
        {
            ':' => XyChartTokenKind.Colon,
            '+' => XyChartTokenKind.Plus,
            ',' => XyChartTokenKind.Comma,
            '=' => XyChartTokenKind.Equals,
            '*' => XyChartTokenKind.Multiply,
            '#' => XyChartTokenKind.Hash,
            '_' => XyChartTokenKind.Underscore,
            '.' => XyChartTokenKind.Dot,
            '&' => XyChartTokenKind.Ampersand,
            '-' => XyChartTokenKind.Minus,
            _ => (XyChartTokenKind?)null,
        };

        if (kind is { } punctuation)
        {
            _position++;
            return new XyChartToken(punctuation, c.ToString(), start);
        }

        if (char.IsAsciiDigit(c))
        {
            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }

            return new XyChartToken(XyChartTokenKind.Digits, _text[start.._position], start);
        }

        if (IsWhiteSpace(c))
        {
            // The spaces include the line feeds that follow them
            while (_position < _text.Length && IsWhiteSpace(_text[_position]))
            {
                _position++;
            }

            return null;
        }

        if (c == ';')
        {
            _position++;
            return new XyChartToken(XyChartTokenKind.Semicolon, ";", start);
        }

        throw Unexpected(start);
    }

    private MermaidParseException Unexpected(int position) => _context.Error(position, $"Unexpected character '{Describe(_text, position)}'.");

    internal static string Describe(string text, int position) => position >= text.Length || text[position] == '\n' ? "end of line" : char.IsSurrogate(text[position]) || char.IsControl(text[position]) ? "\\u" + ((int)text[position]).ToString("x4", CultureInfo.InvariantCulture) : text[position].ToString();

    private XyChartToken ReadLineFeeds(int start)
    {
        while (_position < _text.Length && _text[_position] is '\n' or '\r')
        {
            _position++;
        }

        return new XyChartToken(XyChartTokenKind.NewLine, "\n", start);
    }

    /// <summary>Reads a keyword. jison appends <c>\b</c> to the rules that end with a word character: <c>bar</c> does not start <c>barName</c>.</summary>
    private XyChartToken? TryKeyword(string keyword, XyChartTokenKind kind, bool wordBoundary = true)
    {
        if (_position + keyword.Length > _text.Length || string.Compare(_text, _position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return null;

        if (wordBoundary && _position + keyword.Length < _text.Length && IsWordCharacter(_text[_position + keyword.Length]))
            return null;

        var start = _position;
        _position += keyword.Length;
        return new XyChartToken(kind, _text[start.._position], start);
    }

    /// <summary>Reads <c>accTitle\s*:\s*</c>, <c>accDescr\s*:\s*</c> or <c>accDescr\s*{\s*</c>, and enters the state that reads the value.</summary>
    private bool TryAccessibility(string keyword, char separator, XyChartLexerState state)
    {
        if (_position + keyword.Length > _text.Length || string.Compare(_text, _position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var position = _position + keyword.Length;
        while (position < _text.Length && IsWhiteSpace(_text[position]))
        {
            position++;
        }

        if (position >= _text.Length || _text[position] != separator)
            return false;

        position++;
        while (position < _text.Length && IsWhiteSpace(_text[position]))
        {
            position++;
        }

        _position = position;
        Push(state);
        return true;
    }

    /// <summary>Reads a number: <c>[+-]?(\d+(\.\d+)?|\.\d+)</c>.</summary>
    private XyChartToken? TryNumber()
    {
        var start = _position;
        var position = _position;
        if (position < _text.Length && _text[position] is '+' or '-')
        {
            position++;
        }

        var integerStart = position;
        while (position < _text.Length && char.IsAsciiDigit(_text[position]))
        {
            position++;
        }

        var hasInteger = position > integerStart;
        if (position + 1 < _text.Length && _text[position] == '.' && char.IsAsciiDigit(_text[position + 1]))
        {
            position++;
            while (position < _text.Length && char.IsAsciiDigit(_text[position]))
            {
                position++;
            }
        }
        else if (!hasInteger)
        {
            return null;
        }

        _position = position;
        return new XyChartToken(XyChartTokenKind.Number, _text[start..position], start);
    }

    private void SkipToEndOfLine()
    {
        var end = _text.IndexOfOrdinal('\n', _position);
        _position = end < 0 ? _text.Length : end;
    }

    private bool StartsWith(string value) => string.CompareOrdinal(_text, _position, value, 0, value.Length) == 0 && _position + value.Length <= _text.Length;

    private char Peek(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    // A statement can leave its state when a space precedes its line feed: the size of the text bounds the states
    private void Push(XyChartLexerState state) => _states.Add(state);

    private void Pop()
    {
        if (_states.Count > 0)
        {
            _states.RemoveAt(_states.Count - 1);
        }
    }

    /// <summary>Indicates whether an entity code that Mermaid replaces, <c>#\w+;</c>, starts at a position.</summary>
    internal static bool IsEntityCode(string text, int position)
    {
        var end = position + 1;
        while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        return end > position + 1 && end < text.Length && text[end] == ';';
    }

    /// <summary>Indicates whether a character is a space of JavaScript, <c>\s</c>, which <c>trim</c> also removes.</summary>
    internal static bool IsWhiteSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or '\u2028' or '\u2029' or '\ufeff' || char.GetUnicodeCategory(c) is UnicodeCategory.SpaceSeparator;

    private static bool IsWordCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    private static bool IsLineTerminator(char c) => c is '\n' or '\r' or '\u2028' or '\u2029';
}
