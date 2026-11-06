using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Internals;

/// <summary>
/// Minimal JSON parser shared by the library and the source generator, which cannot depend on System.Text.Json.
/// Objects are <see cref="Dictionary{TKey, TValue}"/> of <see cref="string"/> to <see cref="object"/>, arrays are <see cref="List{T}"/> of <see cref="object"/>,
/// and values are <see cref="string"/>, <see cref="double"/>, <see cref="bool"/> or <see langword="null"/>.
/// </summary>
internal sealed class IconifyJsonParser
{
    private const int MaxDepth = 64;

    private readonly string _text;
    private int _position;
    private int _depth;

    private IconifyJsonParser(string text)
    {
        _text = text;
    }

    public static object? Parse(string text)
    {
        var parser = new IconifyJsonParser(text);
        parser.SkipByteOrderMark();
        parser.SkipWhitespace();
        var value = parser.ParseValue();
        parser.SkipWhitespace();
        if (parser._position < text.Length)
            throw parser.CreateException("Unexpected character after the end of the JSON value");

        return value;
    }

    private void SkipByteOrderMark()
    {
        if (_position < _text.Length && _text[_position] == '\uFEFF')
        {
            _position++;
        }
    }

    private object? ParseValue()
    {
        if (_position >= _text.Length)
            throw CreateException("Unexpected end of the JSON data");

        var c = _text[_position];
        switch (c)
        {
            case '{':
                return ParseObject();
            case '[':
                return ParseArray();
            case '"':
                return ParseString();
            case 't':
                ExpectLiteral("true");
                return true;
            case 'f':
                ExpectLiteral("false");
                return false;
            case 'n':
                ExpectLiteral("null");
                return null;
            default:
                if (c is '-' or (>= '0' and <= '9'))
                    return ParseNumber();

                throw CreateException($"Unexpected character '{c}'");
        }
    }

    private Dictionary<string, object?> ParseObject()
    {
        EnterNesting();
        _position++; // '{'
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        SkipWhitespace();
        if (TryConsume('}'))
        {
            _depth--;
            return result;
        }

        while (true)
        {
            SkipWhitespace();
            if (_position >= _text.Length || _text[_position] != '"')
                throw CreateException("Expected a property name");

            var name = ParseString();
            SkipWhitespace();
            Expect(':');
            SkipWhitespace();
            result[name] = ParseValue();
            SkipWhitespace();
            if (TryConsume(','))
                continue;

            Expect('}');
            _depth--;
            return result;
        }
    }

    private List<object?> ParseArray()
    {
        EnterNesting();
        _position++; // '['
        var result = new List<object?>();
        SkipWhitespace();
        if (TryConsume(']'))
        {
            _depth--;
            return result;
        }

        while (true)
        {
            SkipWhitespace();
            result.Add(ParseValue());
            SkipWhitespace();
            if (TryConsume(','))
                continue;

            Expect(']');
            _depth--;
            return result;
        }
    }

    private string ParseString()
    {
        _position++; // '"'
        var start = _position;

        // Fast path: no escape sequence
        while (_position < _text.Length)
        {
            var c = _text[_position];
            if (c == '"')
            {
                var value = _text.Substring(start, _position - start);
                _position++;
                return value;
            }

            if (c == '\\')
                break;

            if (c < 0x20)
                throw CreateException("Control characters must be escaped in strings");

            _position++;
        }

        var sb = new StringBuilder();
        sb.Append(_text, start, _position - start);
        while (_position < _text.Length)
        {
            var c = _text[_position++];
            if (c == '"')
                return sb.ToString();

            if (c < 0x20)
                throw CreateException("Control characters must be escaped in strings");

            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (_position >= _text.Length)
                break;

            var escaped = _text[_position++];
            switch (escaped)
            {
                case '"':
                    sb.Append('"');
                    break;
                case '\\':
                    sb.Append('\\');
                    break;
                case '/':
                    sb.Append('/');
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u':
                    sb.Append(ParseUnicodeEscape());
                    break;
                default:
                    throw CreateException($"Invalid escape sequence '\\{escaped}'");
            }
        }

        throw CreateException("Unterminated string");
    }

    private char ParseUnicodeEscape()
    {
        if (_position + 4 > _text.Length)
            throw CreateException("Invalid unicode escape sequence");

        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var c = _text[_position++];
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => throw CreateException("Invalid unicode escape sequence"),
            };

            value = (value * 16) + digit;
        }

        return (char)value;
    }

    private double ParseNumber()
    {
        var start = _position;
        TryConsume('-');
        if (TryConsume('0'))
        {
            // A leading zero cannot be followed by another digit
        }
        else if (!ConsumeDigits())
        {
            throw CreateException("Invalid number");
        }

        if (TryConsume('.') && !ConsumeDigits())
            throw CreateException("Invalid number");

        if (_position < _text.Length && _text[_position] is 'e' or 'E')
        {
            _position++;
            if (!TryConsume('+'))
            {
                TryConsume('-');
            }

            if (!ConsumeDigits())
                throw CreateException("Invalid number");
        }

        var text = _text.Substring(start, _position - start);
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private bool ConsumeDigits()
    {
        var start = _position;
        while (_position < _text.Length && _text[_position] is >= '0' and <= '9')
        {
            _position++;
        }

        return _position > start;
    }

    private void ExpectLiteral(string literal)
    {
        if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
            throw CreateException("Invalid literal");

        _position += literal.Length;
    }

    private void Expect(char c)
    {
        if (!TryConsume(c))
            throw CreateException($"Expected '{c}'");
    }

    private bool TryConsume(char c)
    {
        if (_position < _text.Length && _text[_position] == c)
        {
            _position++;
            return true;
        }

        return false;
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && _text[_position] is ' ' or '\t' or '\n' or '\r')
        {
            _position++;
        }
    }

    private void EnterNesting()
    {
        _depth++;
        if (_depth > MaxDepth)
            throw CreateException("The JSON data is nested too deeply");
    }

    private FormatException CreateException(string message)
    {
        return new FormatException(message + " at position " + _position.ToString(CultureInfo.InvariantCulture) + ".");
    }
}
