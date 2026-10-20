using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Css.Syntax;

/// <summary>The CSS tokenizer (https://drafts.csswg.org/css-syntax-3/#tokenization).</summary>
internal sealed class CssTokenizer
{
    private const int Eof = -1;

    /// <summary>The strings of the ASCII characters, for the delim tokens.</summary>
    private static readonly string[] AsciiStrings = CreateAsciiStrings();

    private readonly string _input;
    /// <summary>The value of the string token being consumed, when it contains escapes (created on first use).</summary>
    private StringBuilder? _buffer;
    private int _position;

    public CssTokenizer(string input)
    {
        _input = Preprocess(input);
    }

    /// <summary>The preprocessed input (https://drafts.csswg.org/css-syntax-3/#input-preprocessing).</summary>
    public string Input => _input;

    /// <summary>Gets or sets whether unicode-range tokens can be produced (https://drafts.csswg.org/css-syntax-3/#consume-token).</summary>
    public bool UnicodeRangesAllowed { get; set; }

    public static List<CssToken> Tokenize(string input)
    {
        var tokenizer = new CssTokenizer(input);
        var result = new List<CssToken>();
        while (true)
        {
            var token = tokenizer.ConsumeToken();
            if (token.Type == CssTokenType.EndOfFile)
                return result;

            result.Add(token);
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#input-preprocessing</summary>
    internal static string Preprocess(string input)
    {
        var index = input.AsSpan().IndexOfAny("\r\f\0");
        if (index < 0 && !HasLoneSurrogate(input))
            return input;

        var builder = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            switch (c)
            {
                case '\r':
                    builder.Append('\n');
                    if (i + 1 < input.Length && input[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case '\f':
                    builder.Append('\n');
                    break;
                case '\0':
                    builder.Append((char)0xFFFD);
                    break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                    {
                        builder.Append(c).Append(input[i + 1]);
                        i++;
                    }
                    else if (char.IsSurrogate(c))
                    {
                        builder.Append((char)0xFFFD);
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Returns true when the input contains a surrogate that is not part of a pair.</summary>
    private static bool HasLoneSurrogate(string input)
    {
        var span = input.AsSpan();
        while (true)
        {
            var index = span.IndexOfAnyInRange('\uD800', '\uDFFF');
            if (index < 0)
                return false;

            if (!char.IsHighSurrogate(span[index]) || index + 1 >= span.Length || !char.IsLowSurrogate(span[index + 1]))
                return true;

            span = span[(index + 2)..];
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Code point classification (https://drafts.csswg.org/css-syntax-3/#tokenizer-definitions)
    // ----------------------------------------------------------------------------------------------------------------

    private static bool IsDigit(int c) => c is >= '0' and <= '9';

    private static bool IsHexDigit(int c) => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');

    private static bool IsNewline(int c) => c == '\n';

    private static bool IsWhitespace(int c) => c is '\n' or '\t' or ' ';

    /// <summary>https://drafts.csswg.org/css-syntax-3/#non-ascii-ident-code-point</summary>
    private static bool IsNonAsciiIdentCodePoint(int c) => c is 0xB7
        or (>= 0xC0 and <= 0xD6)
        or (>= 0xD8 and <= 0xF6)
        or (>= 0xF8 and <= 0x37D)
        or (>= 0x37F and <= 0x1FFF)
        or 0x200C or 0x200D or 0x203F or 0x2040
        or (>= 0x2070 and <= 0x218F)
        or (>= 0x2C00 and <= 0x2FEF)
        or (>= 0x3001 and <= 0xD7FF)
        or (>= 0xF900 and <= 0xFDCF)
        or (>= 0xFDF0 and <= 0xFFFD)
        or >= 0x10000;

    internal static bool IsIdentStart(int c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' || IsNonAsciiIdentCodePoint(c);

    internal static bool IsIdentCodePoint(int c) => IsIdentStart(c) || IsDigit(c) || c == '-';

    private static bool IsNonPrintable(int c) => c is (>= 0 and <= 8) or 0xB or (>= 0xE and <= 0x1F) or 0x7F;

    // ----------------------------------------------------------------------------------------------------------------
    // Input stream (code points)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the code point at the given UTF-16 offset from the current position (surrogate pairs are combined).</summary>
    private int PeekAt(int position)
    {
        if (position >= _input.Length)
            return Eof;

        var c = _input[position];
        if (char.IsHighSurrogate(c) && position + 1 < _input.Length)
            return char.ConvertToUtf32(c, _input[position + 1]);

        return c;
    }

    private static int Length(int codePoint) => codePoint >= 0x10000 ? 2 : 1;

    private int Next => PeekAt(_position);

    private int Next2 => PeekAt(_position + Length(Math.Max(0, Next)));

    private int Next3
    {
        get
        {
            var second = _position + Length(Math.Max(0, Next));
            return PeekAt(second + Length(Math.Max(0, PeekAt(second))));
        }
    }

    private int Consume()
    {
        var c = Next;
        if (c != Eof)
        {
            _position += Length(c);
        }

        return c;
    }

    private void Reconsume(int codePoint)
    {
        if (codePoint != Eof)
        {
            _position -= Length(codePoint);
        }
    }

    private static bool IsValidEscape(int first, int second) => first == '\\' && !IsNewline(second);

    /// <summary>https://drafts.csswg.org/css-syntax-3/#would-start-an-identifier</summary>
    private static bool WouldStartIdentSequence(int first, int second, int third)
    {
        if (first == '-')
            return IsIdentStart(second) || second == '-' || IsValidEscape(second, third);

        if (IsIdentStart(first))
            return true;

        return first == '\\' && IsValidEscape(first, second);
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#starts-with-a-number</summary>
    private static bool WouldStartNumber(int first, int second, int third)
    {
        if (first is '+' or '-')
            return IsDigit(second) || (second == '.' && IsDigit(third));

        if (first == '.')
            return IsDigit(second);

        return IsDigit(first);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Tokenizer
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-token</summary>
    public CssToken ConsumeToken()
    {
        ConsumeComments();
        var start = _position;
        var token = ConsumeTokenCore();
        return token with { Start = start, End = _position };
    }

    private CssToken ConsumeTokenCore()
    {
        var c = Consume();
        switch (c)
        {
            case Eof:
                return Simple(CssTokenType.EndOfFile);

            case '\n' or '\t' or ' ':
                while (IsWhitespace(Next))
                {
                    Consume();
                }

                return Simple(CssTokenType.Whitespace);

            case '"' or '\'':
                return ConsumeStringToken(c);

            case '#':
                if (IsIdentCodePoint(Next) || IsValidEscape(Next, Next2))
                {
                    var isId = WouldStartIdentSequence(Next, Next2, Next3);
                    return new CssToken { Type = CssTokenType.Hash, IsIdHash = isId, Value = ConsumeIdentSequence() };
                }

                return Delim(c);

            case '(':
                return Simple(CssTokenType.LeftParenthesis);

            case ')':
                return Simple(CssTokenType.RightParenthesis);

            case '+' or '.':
                if (WouldStartNumber(c, Next, Next2))
                {
                    Reconsume(c);
                    return ConsumeNumericToken();
                }

                return Delim(c);

            case ',':
                return Simple(CssTokenType.Comma);

            case '-':
                if (WouldStartNumber(c, Next, Next2))
                {
                    Reconsume(c);
                    return ConsumeNumericToken();
                }

                if (Next == '-' && Next2 == '>')
                {
                    Consume();
                    Consume();
                    return Simple(CssTokenType.Cdc);
                }

                if (WouldStartIdentSequence(c, Next, Next2))
                {
                    Reconsume(c);
                    return ConsumeIdentLikeToken();
                }

                return Delim(c);

            case ':':
                return Simple(CssTokenType.Colon);

            case ';':
                return Simple(CssTokenType.Semicolon);

            case '<':
                if (Next == '!' && Next2 == '-' && Next3 == '-')
                {
                    Consume();
                    Consume();
                    Consume();
                    return Simple(CssTokenType.Cdo);
                }

                return Delim(c);

            case '@':
                if (WouldStartIdentSequence(Next, Next2, Next3))
                    return new CssToken { Type = CssTokenType.AtKeyword, Value = ConsumeIdentSequence() };

                return Delim(c);

            case '[':
                return Simple(CssTokenType.LeftSquareBracket);

            case '\\':
                if (IsValidEscape(c, Next))
                {
                    Reconsume(c);
                    return ConsumeIdentLikeToken();
                }

                // Parse error
                return Delim(c);

            case ']':
                return Simple(CssTokenType.RightSquareBracket);

            case '{':
                return Simple(CssTokenType.LeftCurlyBracket);

            case '}':
                return Simple(CssTokenType.RightCurlyBracket);

            case >= '0' and <= '9':
                Reconsume(c);
                return ConsumeNumericToken();

            case 'U' or 'u':
                if (UnicodeRangesAllowed && Next == '+' && (Next2 == '?' || IsHexDigit(Next2)))
                {
                    return ConsumeUnicodeRangeToken();
                }

                Reconsume(c);
                return ConsumeIdentLikeToken();
        }

        if (IsIdentStart(c))
        {
            Reconsume(c);
            return ConsumeIdentLikeToken();
        }

        return Delim(c);
    }

    private static CssToken Simple(CssTokenType type) => new() { Type = type, Value = "" };

    private static CssToken Delim(int c) => new() { Type = CssTokenType.Delim, Value = c < AsciiStrings.Length ? AsciiStrings[c] : char.ConvertFromUtf32(c) };

    private static string[] CreateAsciiStrings()
    {
        var result = new string[128];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ((char)i).ToString();
        }

        return result;
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-comment</summary>
    private void ConsumeComments()
    {
        while (_position + 1 < _input.Length && _input[_position] == '/' && _input[_position + 1] == '*')
        {
            var end = _input.IndexOf("*/", _position + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                // Parse error
                _position = _input.Length;
                return;
            }

            _position = end + 2;
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-numeric-token</summary>
    private CssToken ConsumeNumericToken()
    {
        var (value, isInteger, sign) = ConsumeNumber();
        if (WouldStartIdentSequence(Next, Next2, Next3))
        {
            var unit = ConsumeIdentSequence();
            return new CssToken { Type = CssTokenType.Dimension, NumericValue = value, IsInteger = isInteger, Sign = sign, Value = unit };
        }

        if (Next == '%')
        {
            Consume();
            return new CssToken { Type = CssTokenType.Percentage, NumericValue = value, Sign = sign, Value = "" };
        }

        return new CssToken { Type = CssTokenType.Number, NumericValue = value, IsInteger = isInteger, Sign = sign, Value = "" };
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-a-number</summary>
    private (double Value, bool IsInteger, char Sign) ConsumeNumber()
    {
        var isInteger = true;
        var sign = '\0';
        var start = _position;
        if (Next is '+' or '-')
        {
            sign = (char)Consume();
        }

        while (IsDigit(Next))
        {
            Consume();
        }

        if (Next == '.' && IsDigit(Next2))
        {
            Consume();
            while (IsDigit(Next))
            {
                Consume();
            }

            isInteger = false;
        }

        var numberEnd = _position;
        var exponentStart = -1;
        if (Next is 'E' or 'e' && (IsDigit(Next2) || (Next2 is '+' or '-' && IsDigit(Next3))))
        {
            Consume();
            exponentStart = _position;
            if (Next is '+' or '-')
            {
                Consume();
            }

            while (IsDigit(Next))
            {
                Consume();
            }

            isInteger = false;
        }

        var value = double.Parse(_input.AsSpan(start, numberEnd - start), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (exponentStart >= 0)
        {
            var exponent = double.Parse(_input.AsSpan(exponentStart, _position - exponentStart), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            value *= Math.Pow(10, exponent);
        }

        return (value, isInteger, sign);
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-an-ident-like-token</summary>
    private CssToken ConsumeIdentLikeToken()
    {
        var value = ConsumeIdentSequence();
        if (Infra.InfraAscii.EqualsIgnoreAsciiCase(value, "url") && Next == '(')
        {
            Consume();
            while (IsWhitespace(Next) && IsWhitespace(Next2))
            {
                Consume();
            }

            if (Next is '"' or '\'' || (IsWhitespace(Next) && Next2 is '"' or '\''))
                return new CssToken { Type = CssTokenType.Function, Value = value };

            return ConsumeUrlToken();
        }

        if (Next == '(')
        {
            Consume();
            return new CssToken { Type = CssTokenType.Function, Value = value };
        }

        return new CssToken { Type = CssTokenType.Ident, Value = value };
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-string-token</summary>
    private CssToken ConsumeStringToken(int ending)
    {
        // Most strings contain no escape and no newline: their value is a substring of the input
        var remaining = _input.AsSpan(_position);
        var length = remaining.IndexOfAny((char)ending, '\\', '\n');
        if (length >= 0 && remaining[length] == ending)
        {
            var value = _input.Substring(_position, length);
            _position += length + 1;
            return new CssToken { Type = CssTokenType.String, Value = value };
        }

        var buffer = _buffer ??= new StringBuilder();
        buffer.Clear();
        while (true)
        {
            var c = Consume();
            if (c == ending || c == Eof)
                return new CssToken { Type = CssTokenType.String, Value = buffer.ToString() };

            if (c == '\n')
            {
                // Parse error
                Reconsume(c);
                return new CssToken { Type = CssTokenType.BadString, Value = "" };
            }

            if (c == '\\')
            {
                if (Next == Eof)
                    continue;

                if (Next == '\n')
                {
                    Consume();
                    continue;
                }

                AppendCodePoint(buffer, ConsumeEscapedCodePoint());
                continue;
            }

            AppendCodePoint(buffer, c);
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-url-token</summary>
    private CssToken ConsumeUrlToken()
    {
        var value = new StringBuilder();
        while (IsWhitespace(Next))
        {
            Consume();
        }

        while (true)
        {
            var c = Consume();
            switch (c)
            {
                case ')' or Eof:
                    return new CssToken { Type = CssTokenType.Url, Value = value.ToString() };

                case '\n' or '\t' or ' ':
                    while (IsWhitespace(Next))
                    {
                        Consume();
                    }

                    if (Next is ')' or Eof)
                    {
                        Consume();
                        return new CssToken { Type = CssTokenType.Url, Value = value.ToString() };
                    }

                    ConsumeRemnantsOfBadUrl();
                    return new CssToken { Type = CssTokenType.BadUrl, Value = "" };

                case '"' or '\'' or '(':
                    ConsumeRemnantsOfBadUrl();
                    return new CssToken { Type = CssTokenType.BadUrl, Value = "" };

                case '\\':
                    if (IsValidEscape(c, Next))
                    {
                        AppendCodePoint(value, ConsumeEscapedCodePoint());
                        break;
                    }

                    ConsumeRemnantsOfBadUrl();
                    return new CssToken { Type = CssTokenType.BadUrl, Value = "" };

                default:
                    if (IsNonPrintable(c))
                    {
                        ConsumeRemnantsOfBadUrl();
                        return new CssToken { Type = CssTokenType.BadUrl, Value = "" };
                    }

                    AppendCodePoint(value, c);
                    break;
            }
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-remnants-of-bad-url</summary>
    private void ConsumeRemnantsOfBadUrl()
    {
        while (true)
        {
            var c = Consume();
            if (c is ')' or Eof)
                return;

            if (IsValidEscape(c, Next))
            {
                ConsumeEscapedCodePoint();
            }
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-escaped-code-point</summary>
    private int ConsumeEscapedCodePoint()
    {
        var c = Consume();
        if (c == Eof)
            return 0xFFFD;

        if (!IsHexDigit(c))
            return c;

        var value = HexValue(c);
        for (var i = 0; i < 5 && IsHexDigit(Next); i++)
        {
            value = (value * 16) + HexValue(Consume());
        }

        if (IsWhitespace(Next))
        {
            Consume();
        }

        if (value == 0 || value is >= 0xD800 and <= 0xDFFF || value > 0x10FFFF)
            return 0xFFFD;

        return value;
    }

    private static int HexValue(int c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-name</summary>
    private string ConsumeIdentSequence()
    {
        // Fast path: the ASCII code points without escape are a substring of the input
        var start = _position;
        var end = start;
        while (end < _input.Length && _input[end] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_')
        {
            end++;
        }

        _position = end;
        if (end == _input.Length || !IsIdentCodePoint(Next) && !IsValidEscape(Next, Next2))
            return _input.Substring(start, end - start);

        var result = new StringBuilder().Append(_input, start, end - start);
        while (true)
        {
            var c = Consume();
            if (IsIdentCodePoint(c))
            {
                AppendCodePoint(result, c);
            }
            else if (IsValidEscape(c, Next))
            {
                AppendCodePoint(result, ConsumeEscapedCodePoint());
            }
            else
            {
                Reconsume(c);
                return result.ToString();
            }
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-unicode-range-token (the "u" was already consumed)</summary>
    private CssToken ConsumeUnicodeRangeToken()
    {
        Consume(); // +
        var firstSegment = new StringBuilder();
        while (firstSegment.Length < 6 && IsHexDigit(Next))
        {
            firstSegment.Append((char)Consume());
        }

        var hasQuestionMarks = false;
        while (firstSegment.Length < 6 && Next == '?')
        {
            firstSegment.Append((char)Consume());
            hasQuestionMarks = true;
        }

        if (hasQuestionMarks)
        {
            var start = Convert.ToInt32(firstSegment.ToString().Replace('?', '0'), 16);
            var end = Convert.ToInt32(firstSegment.ToString().Replace('?', 'F'), 16);
            return new CssToken { Type = CssTokenType.UnicodeRange, Value = "", RangeStart = start, RangeEnd = end };
        }

        var rangeStart = Convert.ToInt32(firstSegment.ToString(), 16);
        if (Next == '-' && IsHexDigit(Next2))
        {
            Consume();
            var secondSegment = new StringBuilder();
            while (secondSegment.Length < 6 && IsHexDigit(Next))
            {
                secondSegment.Append((char)Consume());
            }

            return new CssToken { Type = CssTokenType.UnicodeRange, Value = "", RangeStart = rangeStart, RangeEnd = Convert.ToInt32(secondSegment.ToString(), 16) };
        }

        return new CssToken { Type = CssTokenType.UnicodeRange, Value = "", RangeStart = rangeStart, RangeEnd = rangeStart };
    }

    private static void AppendCodePoint(StringBuilder builder, int codePoint)
    {
        if (codePoint < 0x10000)
        {
            builder.Append((char)codePoint);
        }
        else
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }
    }
}
