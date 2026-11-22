using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>Splits a DOT text into tokens, as the scanner of Graphviz: comments, preprocessor lines, quoted and HTML strings, numerals.</summary>
internal sealed class DotLexer
{
    private const char ByteOrderMark = (char)0xFEFF;

    private readonly DotContext _context;
    private readonly string _text;
    private int _position;
    private DotToken? _peeked;

    public DotLexer(DotContext context)
    {
        _context = context;
        _text = context.Text;

        // A byte order mark is not part of the graph
        if (_text.Length > 0 && _text[0] is ByteOrderMark)
        {
            _position = 1;
        }
    }

    public DotToken Peek()
    {
        _peeked ??= Read();
        return _peeked.Value;
    }

    public DotToken Next()
    {
        var token = Peek();
        _peeked = null;
        return token;
    }

    internal static bool IsIdentifierStart(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' or >= (char)0x80;

    internal static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || char.IsAsciiDigit(c);

    private DotToken Read()
    {
        SkipTrivia();
        if (_position >= _text.Length)
            return new DotToken(DotTokenKind.End, string.Empty, _text.Length);

        var start = _position;
        var c = _text[_position];
        switch (c)
        {
            case '{':
                _position++;
                return new DotToken(DotTokenKind.LeftBrace, "{", start);
            case '}':
                _position++;
                return new DotToken(DotTokenKind.RightBrace, "}", start);
            case '[':
                _position++;
                return new DotToken(DotTokenKind.LeftBracket, "[", start);
            case ']':
                _position++;
                return new DotToken(DotTokenKind.RightBracket, "]", start);
            case '=':
                _position++;
                return new DotToken(DotTokenKind.Equal, "=", start);
            case ';':
                _position++;
                return new DotToken(DotTokenKind.Semicolon, ";", start);
            case ',':
                _position++;
                return new DotToken(DotTokenKind.Comma, ",", start);
            case ':':
                _position++;
                return new DotToken(DotTokenKind.Colon, ":", start);
            case '+':
                _position++;
                return new DotToken(DotTokenKind.Plus, "+", start);
            case '"':
                return ReadQuotedString();
            case '<':
                return ReadHtmlString();
            case '-' when Next(1) is '>':
                _position += 2;
                return new DotToken(DotTokenKind.DirectedEdge, "->", start);
            case '-' when Next(1) is '-':
                _position += 2;
                return new DotToken(DotTokenKind.UndirectedEdge, "--", start);
            case '-' or '.' or (>= '0' and <= '9'):
                return ReadNumeral();
        }

        if (IsIdentifierStart(c))
        {
            while (_position < _text.Length && IsIdentifierPart(_text[_position]))
            {
                _position++;
            }

            return new DotToken(DotTokenKind.Identifier, _text[start.._position], start);
        }

        throw _context.Error(start, $"Unexpected character '{FormatCharacter(c)}'.");
    }

    private char Next(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private void SkipTrivia()
    {
        while (_position < _text.Length)
        {
            var c = _text[_position];
            if (char.IsWhiteSpace(c) && c < (char)0x80)
            {
                _position++;
            }
            else if (c is '/' && Next(1) is '/')
            {
                SkipLine();
            }
            else if (c is '/' && Next(1) is '*')
            {
                var end = _text.IndexOf("*/", _position + 2, StringComparison.Ordinal);
                if (end < 0)
                    throw _context.Error(_position, "The comment is not closed: '*/' is missing.");

                _position = end + 2;
            }
            else if (c is '#' && IsAtLineStart(_position))
            {
                // A line of the output of the C preprocessor
                SkipLine();
            }
            else
            {
                return;
            }
        }
    }

    private bool IsAtLineStart(int position) => position is 0 || _text[position - 1] is '\n' or '\r' || (position is 1 && _text[0] is ByteOrderMark);

    private void SkipLine()
    {
        while (_position < _text.Length && _text[_position] is not '\n' and not '\r')
        {
            _position++;
        }
    }

    private DotToken ReadQuotedString()
    {
        var start = _position;
        _position++;
        var builder = new StringBuilder();
        while (_position < _text.Length)
        {
            var c = _text[_position];
            if (c is '"')
            {
                _position++;
                return new DotToken(DotTokenKind.QuotedString, builder.ToString(), start);
            }

            if (c is '\\')
            {
                var next = Next(1);
                switch (next)
                {
                    case '"':
                        builder.Append('"');
                        _position += 2;
                        continue;
                    case '\\':
                        // The pair is kept: the escapes of the labels read it later
                        builder.Append("\\\\");
                        _position += 2;
                        continue;
                    case '\n':
                        _position += 2;
                        continue;
                    case '\r':
                        _position += Next(2) is '\n' ? 3 : 2;
                        continue;
                }
            }

            builder.Append(c);
            _position++;
        }

        throw _context.Error(start, "The string is not closed: '\"' is missing.");
    }

    private DotToken ReadHtmlString()
    {
        var start = _position;
        var depth = 0;
        while (_position < _text.Length)
        {
            var c = _text[_position];
            if (c is '<')
            {
                depth++;
            }
            else if (c is '>')
            {
                depth--;
                if (depth is 0)
                {
                    _position++;
                    return new DotToken(DotTokenKind.HtmlString, _text[(start + 1)..(_position - 1)], start);
                }
            }

            _position++;
        }

        throw _context.Error(start, "The HTML string is not closed: '>' is missing.");
    }

    private DotToken ReadNumeral()
    {
        var start = _position;
        if (_text[_position] is '-')
        {
            _position++;
        }

        var digits = 0;
        while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
        {
            _position++;
            digits++;
        }

        if (_position < _text.Length && _text[_position] is '.')
        {
            _position++;
            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
                digits++;
            }
        }

        if (digits is 0)
            throw _context.Error(start, $"Unexpected character '{FormatCharacter(_text[start])}'.");

        if (_position < _text.Length && (IsIdentifierStart(_text[_position]) || _text[_position] is '.'))
        {
            // Graphviz splits "2abc" into "2" and "abc" with a warning
            _context.Warn(start, $"The number '{_text[start.._position]}' is followed by '{FormatCharacter(_text[_position])}' with no space: they are read as two identifiers.");
        }

        return new DotToken(DotTokenKind.Numeral, _text[start.._position], start);
    }

    internal static string FormatCharacter(char c) => char.IsControl(c) || char.IsSurrogate(c) ? string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}") : c.ToString();
}
