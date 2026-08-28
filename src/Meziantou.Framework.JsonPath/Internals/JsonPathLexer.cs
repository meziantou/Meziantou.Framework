using System.Runtime.InteropServices;

namespace Meziantou.Framework.Json.Internals;

[StructLayout(LayoutKind.Auto)]
internal ref struct JsonPathLexer
{
    private readonly ReadOnlySpan<char> _input;
    private int _position;

    public JsonPathLexer(ReadOnlySpan<char> input)
    {
        _input = input;
        _position = 0;
    }

    public int InputLength => _input.Length;

    public JsonPathToken NextToken()
    {
        var positionBeforeWhitespace = _position;
        SkipWhitespace();

        if (_position >= _input.Length)
        {
            return new JsonPathToken(JsonPathTokenKind.EndOfInput, positionBeforeWhitespace);
        }

        var ch = _input[_position];
        var pos = _position;

        switch (ch)
        {
            case '$':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.RootIdentifier, pos);

            case '@':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.CurrentNodeIdentifier, pos);

            case '.':
                _position++;
                if (_position < _input.Length && _input[_position] == '.')
                {
                    _position++;
                    return new JsonPathToken(JsonPathTokenKind.DoubleDot, pos);
                }

                return new JsonPathToken(JsonPathTokenKind.Dot, pos);

            case '[':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.OpenBracket, pos);

            case ']':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.CloseBracket, pos);

            case '(':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.OpenParen, pos);

            case ')':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.CloseParen, pos);

            case ',':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.Comma, pos);

            case ':':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.Colon, pos);

            case '?':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.QuestionMark, pos);

            case '*':
                _position++;
                return new JsonPathToken(JsonPathTokenKind.Asterisk, pos);

            case '!':
                _position++;
                if (_position < _input.Length && _input[_position] == '=')
                {
                    _position++;
                    return new JsonPathToken(JsonPathTokenKind.NotEqual, pos);
                }

                return new JsonPathToken(JsonPathTokenKind.ExclamationMark, pos);

            case '=':
                _position++;
                if (_position < _input.Length && _input[_position] == '=')
                {
                    _position++;
                    return new JsonPathToken(JsonPathTokenKind.Equal, pos);
                }

                throw new FormatException($"Unexpected character '=' at position {pos}. Did you mean '=='?");

            case '<':
                _position++;
                if (_position < _input.Length && _input[_position] == '=')
                {
                    _position++;
                    return new JsonPathToken(JsonPathTokenKind.LessThanOrEqual, pos);
                }

                return new JsonPathToken(JsonPathTokenKind.LessThan, pos);

            case '>':
                _position++;
                if (_position < _input.Length && _input[_position] == '=')
                {
                    _position++;
                    return new JsonPathToken(JsonPathTokenKind.GreaterThanOrEqual, pos);
                }

                return new JsonPathToken(JsonPathTokenKind.GreaterThan, pos);

            case '&':
                _position++;
                if (_position < _input.Length && _input[_position] == '&')
                {
                    _position++;
                    return new JsonPathToken(JsonPathTokenKind.And, pos);
                }

                throw new FormatException($"Unexpected character '&' at position {pos}. Did you mean '&&'?");

            case '|':
                _position++;
                if (_position < _input.Length && _input[_position] == '|')
                {
                    _position++;
                    return new JsonPathToken(JsonPathTokenKind.Or, pos);
                }

                throw new FormatException($"Unexpected character '|' at position {pos}. Did you mean '||'?");

            case '\'':
            case '"':
                return ReadString(ch);

            case '-' when _position + 1 < _input.Length && (_input[_position + 1] is >= '0' and <= '9'):
            case >= '0' and <= '9':
                return ReadNumber(pos);

            default:
                if (TryReadNameCodePoint(_position, isFirst: true, out _, out var length))
                {
                    return ReadIdentifierOrKeyword(pos, length);
                }

                throw new FormatException($"Unexpected character '{ch}' at position {pos}.");
        }
    }

    /// <summary>
    /// Peeks at the character at the given offset from the current position without consuming tokens.
    /// Returns '\0' if at end of input.
    /// </summary>
    public readonly char PeekChar(int offset = 0)
    {
        var idx = _position + offset;
        if (idx >= _input.Length)
        {
            return '\0';
        }

        return _input[idx];
    }

    /// <summary>Peeks at the next non-whitespace character without consuming it.</summary>
    public readonly char PeekNonWhitespaceChar()
    {
        var idx = _position;
        while (idx < _input.Length && IsBlank(_input[idx]))
        {
            idx++;
        }

        if (idx >= _input.Length)
        {
            return '\0';
        }

        return _input[idx];
    }

    public readonly bool IsAtEnd()
    {
        var idx = _position;
        while (idx < _input.Length && IsBlank(_input[idx]))
        {
            idx++;
        }

        return idx >= _input.Length;
    }

    /// <summary>Saves the current lexer position for backtracking.</summary>
    public readonly int SavePosition() => _position;

    /// <summary>Restores the lexer to a previously saved position.</summary>
    public void RestorePosition(int position) => _position = position;

    private JsonPathToken ReadString(char quote)
    {
        var pos = _position;
        _position++; // skip opening quote

        var sb = new StringBuilder();
        while (_position < _input.Length)
        {
            var ch = _input[_position];
            if (ch == quote)
            {
                _position++;
                return new JsonPathToken(JsonPathTokenKind.StringLiteral, pos, sb.ToString());
            }

            if (ch == '\\')
            {
                _position++;
                if (_position >= _input.Length)
                {
                    throw new FormatException($"Unterminated escape sequence at position {_position - 1}.");
                }

                var escaped = _input[_position];
                switch (escaped)
                {
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
                    case '/':
                        sb.Append('/');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '\'':
                        if (quote != '\'')
                        {
                            throw new FormatException($"Invalid escape sequence '\\'' in double-quoted string at position {_position - 1}.");
                        }

                        sb.Append('\'');
                        break;
                    case '"':
                        if (quote != '"')
                        {
                            throw new FormatException($"Invalid escape sequence '\\\"' in single-quoted string at position {_position - 1}.");
                        }

                        sb.Append('"');
                        break;
                    case 'u':
                        sb.Append(ReadUnicodeEscape());
                        continue; // ReadUnicodeEscape already advances _position
                    default:
                        throw new FormatException($"Invalid escape sequence '\\{escaped}' at position {_position - 1}.");
                }

                _position++;
            }
            else if (ch < '\x20')
            {
                throw new FormatException($"Unescaped control character U+{(int)ch:X4} at position {_position}.");
            }
            else
            {
                sb.Append(ch);
                _position++;
            }
        }

        throw new FormatException($"Unterminated string starting at position {pos}.");
    }

    private string ReadUnicodeEscape()
    {
        _position++; // skip 'u'
        var codePoint = ReadHex4();

        // Handle surrogate pairs
        if (codePoint is >= 0xD800 and <= 0xDBFF)
        {
            if (_position + 1 < _input.Length && _input[_position] == '\\' && _input[_position + 1] == 'u')
            {
                _position += 2; // skip \u
                var low = ReadHex4();
                if (low is < 0xDC00 or > 0xDFFF)
                {
                    throw new FormatException($"Invalid low surrogate U+{low:X4} at position {_position - 4}.");
                }

                codePoint = 0x10000 + ((codePoint - 0xD800) * 0x400) + (low - 0xDC00);
            }
            else
            {
                throw new FormatException($"High surrogate U+{codePoint:X4} not followed by low surrogate at position {_position}.");
            }
        }
        else if (codePoint is >= 0xDC00 and <= 0xDFFF)
        {
            throw new FormatException($"Unexpected low surrogate U+{codePoint:X4} at position {_position - 4}.");
        }

        return char.ConvertFromUtf32(codePoint);
    }

    private int ReadHex4()
    {
        if (_position + 4 > _input.Length)
        {
            throw new FormatException($"Incomplete Unicode escape at position {_position}.");
        }

        var hex = _input.Slice(_position, 4);

        // RFC 9535 requires exactly 4 HEXDIG. int.TryParse with NumberStyles.HexNumber would also accept
        // leading and trailing whitespace (AllowLeadingWhite | AllowTrailingWhite), so the digits are
        // validated explicitly instead.
        var value = 0;
        foreach (var ch in hex)
        {
            var digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'f' => ch - 'a' + 10,
                >= 'A' and <= 'F' => ch - 'A' + 10,
                _ => -1,
            };

            if (digit < 0)
            {
                throw new FormatException($"Invalid hex digits '{hex}' at position {_position}.");
            }

            value = (value << 4) | digit;
        }

        _position += 4;
        return value;
    }

    private JsonPathToken ReadNumber(int pos)
    {
        var start = _position;

        // Optional leading minus
        if (_input[_position] == '-')
        {
            _position++;
        }

        // Integer part
        if (_position < _input.Length && _input[_position] == '0')
        {
            _position++;
            // After a leading 0, no more digits before '.' or 'e' (per JSON number rules)
            // But we need to allow "-0" as a valid number
        }
        else
        {
            if (_position >= _input.Length || _input[_position] is < '1' or > '9')
            {
                throw new FormatException($"Invalid number at position {pos}.");
            }

            _position++;
            while (_position < _input.Length && _input[_position] is >= '0' and <= '9')
            {
                _position++;
            }
        }

        // Check for fractional part
        var hasFraction = false;
        if (_position < _input.Length && _input[_position] == '.')
        {
            hasFraction = true;
            _position++;
            if (_position >= _input.Length || _input[_position] is < '0' or > '9')
            {
                throw new FormatException($"Invalid number: expected digit after '.' at position {_position}.");
            }

            while (_position < _input.Length && _input[_position] is >= '0' and <= '9')
            {
                _position++;
            }
        }

        // Check for exponent
        var hasExponent = false;
        if (_position < _input.Length && _input[_position] is 'e' or 'E')
        {
            hasExponent = true;
            _position++;
            if (_position < _input.Length && _input[_position] is '+' or '-')
            {
                _position++;
            }

            if (_position >= _input.Length || _input[_position] is < '0' or > '9')
            {
                throw new FormatException($"Invalid number: expected digit in exponent at position {_position}.");
            }

            while (_position < _input.Length && _input[_position] is >= '0' and <= '9')
            {
                _position++;
            }
        }

        var numberSpan = _input[start.._position];
        var isIntegerLiteral = !hasFraction && !hasExponent;

        // Detect negative zero (-0) which is not valid per RFC 9535 ABNF:
        // int = "0" / (["-"] DIGIT1 *DIGIT)
        // -0 is not produced by the 'int' grammar rule
        var isNegativeZero = isIntegerLiteral && numberSpan is ['-', '0'];

        if (isIntegerLiteral && !isNegativeZero)
        {
            // Try to parse as long for integer values (index/slice selectors need exact integers)
            if (long.TryParse(numberSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var longValue))
            {
                return new JsonPathToken(JsonPathTokenKind.NumberLiteral, pos, longValue, isIntegerLiteral: true);
            }
        }

        if (!double.TryParse(numberSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            throw new FormatException($"Invalid number '{numberSpan}' at position {pos}.");
        }

        return new JsonPathToken(JsonPathTokenKind.NumberLiteral, pos, doubleValue, isIntegerLiteral: false);
    }

    private JsonPathToken ReadIdentifierOrKeyword(int pos, int firstLength)
    {
        var start = _position;
        _position += firstLength;
        while (TryReadNameCodePoint(_position, isFirst: false, out _, out var length))
        {
            _position += length;
        }

        var text = _input[start.._position];
        if (text.SequenceEqual("true"))
        {
            return new JsonPathToken(JsonPathTokenKind.True, pos);
        }

        if (text.SequenceEqual("false"))
        {
            return new JsonPathToken(JsonPathTokenKind.False, pos);
        }

        if (text.SequenceEqual("null"))
        {
            return new JsonPathToken(JsonPathTokenKind.Null, pos);
        }

        return new JsonPathToken(JsonPathTokenKind.Identifier, pos, text.ToString());
    }

    private void SkipWhitespace()
    {
        while (_position < _input.Length && IsBlank(_input[_position]))
        {
            _position++;
        }
    }

    private static bool IsBlank(char ch) => ch is ' ' or '\t' or '\n' or '\r';

    /// <summary>
    /// Reads the code point at <paramref name="index"/> when it is a valid member-name-shorthand character.
    /// Code points above the BMP are encoded as a surrogate pair, so they are decoded here rather than
    /// tested one UTF-16 char at a time \u2014 a lone surrogate half is never a name character.
    /// </summary>
    /// <param name="index">The position to read from.</param>
    /// <param name="isFirst">Whether the code point is the first of the name, which excludes DIGIT.</param>
    /// <param name="codePoint">When this method returns, contains the decoded code point.</param>
    /// <param name="length">When this method returns, contains the number of UTF-16 chars consumed (1 or 2).</param>
    /// <returns><see langword="true"/> when a valid name code point was read; otherwise, <see langword="false"/>.</returns>
    private readonly bool TryReadNameCodePoint(int index, bool isFirst, out int codePoint, out int length)
    {
        codePoint = 0;
        length = 0;
        if (index >= _input.Length)
        {
            return false;
        }

        var ch = _input[index];
        if (char.IsHighSurrogate(ch))
        {
            if (index + 1 >= _input.Length || !char.IsLowSurrogate(_input[index + 1]))
            {
                return false;
            }

            codePoint = char.ConvertToUtf32(ch, _input[index + 1]);
            length = 2;
        }
        else if (char.IsLowSurrogate(ch))
        {
            return false;
        }
        else
        {
            codePoint = ch;
            length = 1;
        }

        return isFirst ? IsNameFirst(codePoint) : IsNameChar(codePoint);
    }

    /// <summary>name-first = ALPHA / "_" / %x80-D7FF / %xE000-10FFFF</summary>
    internal static bool IsNameFirst(int codePoint) => codePoint is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' or (>= 0x80 and <= 0xD7FF) or (>= 0xE000 and <= 0x10FFFF);

    /// <summary>name-char = name-first / DIGIT</summary>
    private static bool IsNameChar(int codePoint) => IsNameFirst(codePoint) || codePoint is >= '0' and <= '9';
}
