using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

/// <summary>
/// The tokenizer of ECMAScript (https://tc39.es/ecma262/#sec-ecmascript-language-lexical-grammar). It is driven by the
/// parser: a '/' is scanned as a division and rescanned as a regular expression when the parser expects an operand, and a
/// '}' is rescanned as the continuation of a template when the parser ends a substitution.
/// </summary>
internal sealed class Lexer
{
    private static readonly FrozenDictionary<string, TokenKind> Keywords = new Dictionary<string, TokenKind>(StringComparer.Ordinal)
    {
        ["break"] = TokenKind.Break,
        ["case"] = TokenKind.Case,
        ["catch"] = TokenKind.Catch,
        ["class"] = TokenKind.Class,
        ["const"] = TokenKind.Const,
        ["continue"] = TokenKind.Continue,
        ["debugger"] = TokenKind.Debugger,
        ["default"] = TokenKind.Default,
        ["delete"] = TokenKind.Delete,
        ["do"] = TokenKind.Do,
        ["else"] = TokenKind.Else,
        ["enum"] = TokenKind.Enum,
        ["export"] = TokenKind.Export,
        ["extends"] = TokenKind.Extends,
        ["false"] = TokenKind.False,
        ["finally"] = TokenKind.Finally,
        ["for"] = TokenKind.For,
        ["function"] = TokenKind.Function,
        ["if"] = TokenKind.If,
        ["import"] = TokenKind.Import,
        ["in"] = TokenKind.In,
        ["instanceof"] = TokenKind.InstanceOf,
        ["new"] = TokenKind.New,
        ["null"] = TokenKind.Null,
        ["return"] = TokenKind.Return,
        ["super"] = TokenKind.Super,
        ["switch"] = TokenKind.Switch,
        ["this"] = TokenKind.This,
        ["throw"] = TokenKind.Throw,
        ["true"] = TokenKind.True,
        ["try"] = TokenKind.Try,
        ["typeof"] = TokenKind.TypeOf,
        ["var"] = TokenKind.Var,
        ["void"] = TokenKind.Void,
        ["while"] = TokenKind.While,
        ["with"] = TokenKind.With,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, TokenKind>.AlternateLookup<ReadOnlySpan<char>> KeywordLookup = Keywords.GetAlternateLookup<ReadOnlySpan<char>>();

    // 1: identifier start, 2: identifier part (ASCII only)
    private static readonly byte[] AsciiIdentifierFlags = CreateAsciiIdentifierFlags();

    private readonly SourceText _source;
    private readonly string _text;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _namesLookup;
    private StringBuilder? _builder;
    private int _pos;
    private bool _scannedToken;
    private int _suppressErrors;

    public Lexer(SourceText source, bool isModule, bool allowHashbang)
    {
        _source = source;
        _text = source.Text;
        IsModule = isModule;
        AllowHashbang = allowHashbang;
        _namesLookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public bool IsModule { get; }

    public bool AllowHashbang { get; }

    /// <summary>Gets or sets whether the code is strict mode code (legacy octal literals and escapes are errors).</summary>
    public bool Strict { get; set; }

    public SourceText Source => _source;

    public string Text => _text;

    public int Position => _pos;

    public string Intern(ReadOnlySpan<char> name)
    {
        if (_namesLookup.TryGetValue(name, out var result))
            return result;

        result = name.ToString();
        _names.Add(result, result);
        return result;
    }

    public string Intern(string name)
    {
        if (_names.TryGetValue(name, out var result))
            return result;

        _names.Add(name, name);
        return name;
    }

    /// <summary>Scans the token that starts at <paramref name="position"/> (after its white space) without changing the state of the lexer.</summary>
    public Token Peek(int position)
    {
        var oldPos = _pos;
        var oldScanned = _scannedToken;
        _pos = position;
        _suppressErrors++;
        try
        {
            return NextToken();
        }
        catch (JsSyntaxErrorException)
        {
            return new Token { Kind = TokenKind.EndOfFile, Start = position, End = position };
        }
        finally
        {
            _suppressErrors--;
            _pos = oldPos;
            _scannedToken = oldScanned;
        }
    }

    /// <summary>Moves to <paramref name="position"/>, which is not the start of the input (the start of a function parsed again by the lazy compilation).</summary>
    public void MoveTo(int position)
    {
        _pos = position;
        _scannedToken = true;
    }

    /// <summary>Scans <paramref name="token"/> again (e.g. after the code became strict).</summary>
    public Token Rescan(in Token token)
    {
        _pos = token.Start;
        var result = NextToken();
        result.Flags |= token.Flags & TokenFlags.NewLineBefore;
        return result;
    }

    [DoesNotReturn]
    public void Raise(int position, string message)
    {
        if (_suppressErrors > 0)
            throw new JsSyntaxErrorException(message);

        throw Parser.CreateError(_source, position, message);
    }

    public Token NextToken()
    {
        var newLine = SkipTrivia();
        _scannedToken = true;
        var token = new Token { Start = _pos, Flags = newLine ? TokenFlags.NewLineBefore : TokenFlags.None };
        var text = _text;
        if (_pos >= text.Length)
        {
            token.Kind = TokenKind.EndOfFile;
            token.End = _pos;
            return token;
        }

        var c = text[_pos];
        switch (c)
        {
            case '(':
                return Punctuator(ref token, TokenKind.LeftParen, 1);
            case ')':
                return Punctuator(ref token, TokenKind.RightParen, 1);
            case '{':
                return Punctuator(ref token, TokenKind.LeftBrace, 1);
            case '}':
                return Punctuator(ref token, TokenKind.RightBrace, 1);
            case '[':
                return Punctuator(ref token, TokenKind.LeftBracket, 1);
            case ']':
                return Punctuator(ref token, TokenKind.RightBracket, 1);
            case ';':
                return Punctuator(ref token, TokenKind.Semicolon, 1);
            case ',':
                return Punctuator(ref token, TokenKind.Comma, 1);
            case ':':
                return Punctuator(ref token, TokenKind.Colon, 1);
            case '~':
                return Punctuator(ref token, TokenKind.Tilde, 1);
            case '.':
                if (IsDecimalDigit(CharAt(_pos + 1)))
                    return ScanNumber(ref token);

                if (CharAt(_pos + 1) == '.' && CharAt(_pos + 2) == '.')
                    return Punctuator(ref token, TokenKind.Ellipsis, 3);

                return Punctuator(ref token, TokenKind.Dot, 1);
            case '?':
                if (CharAt(_pos + 1) == '?')
                {
                    return CharAt(_pos + 2) == '=' ? Punctuator(ref token, TokenKind.QuestionQuestionEqual, 3) : Punctuator(ref token, TokenKind.QuestionQuestion, 2);
                }

                if (CharAt(_pos + 1) == '.' && !IsDecimalDigit(CharAt(_pos + 2)))
                    return Punctuator(ref token, TokenKind.QuestionDot, 2);

                return Punctuator(ref token, TokenKind.Question, 1);
            case '=':
                if (CharAt(_pos + 1) == '=')
                {
                    return CharAt(_pos + 2) == '=' ? Punctuator(ref token, TokenKind.EqualEqualEqual, 3) : Punctuator(ref token, TokenKind.EqualEqual, 2);
                }

                if (CharAt(_pos + 1) == '>')
                    return Punctuator(ref token, TokenKind.Arrow, 2);

                return Punctuator(ref token, TokenKind.Equal, 1);
            case '!':
                if (CharAt(_pos + 1) == '=')
                {
                    return CharAt(_pos + 2) == '=' ? Punctuator(ref token, TokenKind.NotEqualEqual, 3) : Punctuator(ref token, TokenKind.NotEqual, 2);
                }

                return Punctuator(ref token, TokenKind.Exclamation, 1);
            case '<':
                if (CharAt(_pos + 1) == '<')
                {
                    return CharAt(_pos + 2) == '=' ? Punctuator(ref token, TokenKind.LeftShiftEqual, 3) : Punctuator(ref token, TokenKind.LeftShift, 2);
                }

                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.LessThanEqual, 2) : Punctuator(ref token, TokenKind.LessThan, 1);
            case '>':
                if (CharAt(_pos + 1) == '>')
                {
                    if (CharAt(_pos + 2) == '>')
                    {
                        return CharAt(_pos + 3) == '=' ? Punctuator(ref token, TokenKind.UnsignedRightShiftEqual, 4) : Punctuator(ref token, TokenKind.UnsignedRightShift, 3);
                    }

                    return CharAt(_pos + 2) == '=' ? Punctuator(ref token, TokenKind.RightShiftEqual, 3) : Punctuator(ref token, TokenKind.RightShift, 2);
                }

                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.GreaterThanEqual, 2) : Punctuator(ref token, TokenKind.GreaterThan, 1);
            case '+':
                if (CharAt(_pos + 1) == '+')
                    return Punctuator(ref token, TokenKind.PlusPlus, 2);

                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.PlusEqual, 2) : Punctuator(ref token, TokenKind.Plus, 1);
            case '-':
                if (CharAt(_pos + 1) == '-')
                    return Punctuator(ref token, TokenKind.MinusMinus, 2);

                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.MinusEqual, 2) : Punctuator(ref token, TokenKind.Minus, 1);
            case '*':
                if (CharAt(_pos + 1) == '*')
                {
                    return CharAt(_pos + 2) == '=' ? Punctuator(ref token, TokenKind.StarStarEqual, 3) : Punctuator(ref token, TokenKind.StarStar, 2);
                }

                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.StarEqual, 2) : Punctuator(ref token, TokenKind.Star, 1);
            case '/':
                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.SlashEqual, 2) : Punctuator(ref token, TokenKind.Slash, 1);
            case '%':
                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.PercentEqual, 2) : Punctuator(ref token, TokenKind.Percent, 1);
            case '&':
                if (CharAt(_pos + 1) == '&')
                {
                    return CharAt(_pos + 2) == '=' ? Punctuator(ref token, TokenKind.AmpersandAmpersandEqual, 3) : Punctuator(ref token, TokenKind.AmpersandAmpersand, 2);
                }

                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.AmpersandEqual, 2) : Punctuator(ref token, TokenKind.Ampersand, 1);
            case '|':
                if (CharAt(_pos + 1) == '|')
                {
                    return CharAt(_pos + 2) == '=' ? Punctuator(ref token, TokenKind.BarBarEqual, 3) : Punctuator(ref token, TokenKind.BarBar, 2);
                }

                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.BarEqual, 2) : Punctuator(ref token, TokenKind.Bar, 1);
            case '^':
                return CharAt(_pos + 1) == '=' ? Punctuator(ref token, TokenKind.CaretEqual, 2) : Punctuator(ref token, TokenKind.Caret, 1);
            case '"':
            case '\'':
                return ScanString(ref token, c);
            case '`':
                return ScanTemplate(ref token);
            case '#':
                return ScanPrivateName(ref token);
            case >= '0' and <= '9':
                return ScanNumber(ref token);
            default:
                if (c < 128)
                {
                    if ((AsciiIdentifierFlags[c] & 1) != 0 || c == '\\')
                        return ScanIdentifier(ref token);
                }
                else
                {
                    var cp = CodePointAt(_pos);
                    if (IsIdentifierStartCodePoint(cp))
                        return ScanIdentifier(ref token);
                }

                Raise(_pos, "Invalid or unexpected token");
                return token;
        }
    }

    /// <summary>Scans a regular expression literal that starts at the '/' of <paramref name="token"/>.</summary>
    public Token RescanRegularExpression(in Token token)
    {
        var text = _text;
        var start = token.Start;
        var pos = start + 1;
        var inClass = false;
        while (true)
        {
            if (pos >= text.Length || IsLineTerminator(text[pos]))
                Raise(start, "Invalid regular expression: missing /");

            var c = text[pos++];
            if (c == '\\')
            {
                if (pos >= text.Length || IsLineTerminator(text[pos]))
                    Raise(start, "Invalid regular expression: missing /");

                pos++;
            }
            else if (c == '[')
            {
                inClass = true;
            }
            else if (c == ']')
            {
                inClass = false;
            }
            else if (c == '/' && !inClass)
            {
                break;
            }
        }

        var patternEnd = pos - 1;
        var flagsStart = pos;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c < 128)
            {
                if (c == '\\')
                    Raise(pos, "Invalid regular expression flags");

                if ((AsciiIdentifierFlags[c] & 2) == 0)
                    break;

                pos++;
            }
            else
            {
                var cp = CodePointAt(pos);
                if (!IsIdentifierPartCodePoint(cp))
                    break;

                pos += cp > 0xFFFF ? 2 : 1;
            }
        }

        _pos = pos;
        return new Token
        {
            Kind = TokenKind.RegularExpression,
            Flags = token.Flags & TokenFlags.NewLineBefore,
            Start = start,
            End = pos,
            Value = text.Substring(start + 1, patternEnd - start - 1),
            Raw = text[flagsStart..pos],
        };
    }

    /// <summary>Scans the template part that follows the '}' of <paramref name="token"/> (the end of a substitution).</summary>
    public Token RescanTemplateContinuation(in Token token)
    {
        _pos = token.Start;
        var result = new Token { Start = token.Start, Flags = token.Flags & TokenFlags.NewLineBefore };
        return ScanTemplate(ref result);
    }

    private Token Punctuator(ref Token token, TokenKind kind, int length)
    {
        _pos += length;
        token.Kind = kind;
        token.End = _pos;
        return token;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CharAt(int position) => (uint)position < (uint)_text.Length ? _text[position] : -1;

    private int CodePointAt(int position)
    {
        var c = _text[position];
        if (char.IsHighSurrogate(c) && position + 1 < _text.Length && char.IsLowSurrogate(_text[position + 1]))
            return char.ConvertToUtf32(c, _text[position + 1]);

        return c;
    }

    /// <summary>Skips the white space and the comments; returns whether they contain a line terminator.</summary>
    private bool SkipTrivia()
    {
        var text = _text;
        var newLine = false;
        if (_pos == 0 && AllowHashbang && text.Length >= 2 && text[0] == '#' && text[1] == '!')
        {
            SkipLineComment(2);
        }

        while (_pos < text.Length)
        {
            var c = text[_pos];
            switch (c)
            {
                case ' ':
                case '\t':
                case '\v':
                case '\f':
                case '\u00A0':
                case '\uFEFF':
                    _pos++;
                    continue;
                case '\n':
                case '\r':
                case '\u2028':
                case '\u2029':
                    _pos++;
                    newLine = true;
                    continue;
                case '/':
                    var next = CharAt(_pos + 1);
                    if (next == '/')
                    {
                        SkipLineComment(2);
                        continue;
                    }

                    if (next == '*')
                    {
                        if (SkipBlockComment())
                        {
                            newLine = true;
                        }

                        continue;
                    }

                    return newLine;
                case '<':
                    // HTML-like comments (https://tc39.es/ecma262/#sec-html-like-comments)
                    if (!IsModule && CharAt(_pos + 1) == '!' && CharAt(_pos + 2) == '-' && CharAt(_pos + 3) == '-')
                    {
                        SkipLineComment(4);
                        continue;
                    }

                    return newLine;
                case '-':
                    if (!IsModule && (newLine || !_scannedToken) && CharAt(_pos + 1) == '-' && CharAt(_pos + 2) == '>')
                    {
                        SkipLineComment(3);
                        continue;
                    }

                    return newLine;
                default:
                    if (c > 127 && IsWhiteSpaceNonAscii(c))
                    {
                        _pos++;
                        continue;
                    }

                    return newLine;
            }
        }

        return newLine;
    }

    private void SkipLineComment(int length)
    {
        var text = _text;
        var pos = _pos + length;
        while (pos < text.Length && !IsLineTerminator(text[pos]))
        {
            pos++;
        }

        _pos = pos;
    }

    private bool SkipBlockComment()
    {
        var text = _text;
        var start = _pos;
        var pos = _pos + 2;
        var newLine = false;
        while (true)
        {
            if (pos >= text.Length)
                Raise(start, "Unterminated comment");

            var c = text[pos];
            if (c == '*' && CharAt(pos + 1) == '/')
            {
                pos += 2;
                break;
            }

            if (IsLineTerminator(c))
            {
                newLine = true;
            }

            pos++;
        }

        _pos = pos;

        // A SingleLineHTMLCloseComment can follow a MultiLineComment that contains a line terminator
        return newLine;
    }

    private Token ScanPrivateName(ref Token token)
    {
        var start = _pos;
        _pos++;
        if (_pos < _text.Length)
        {
            var c = _text[_pos];
            var isStart = c < 128 ? (AsciiIdentifierFlags[c] & 1) != 0 || c == '\\' : IsIdentifierStartCodePoint(CodePointAt(_pos));
            if (isStart)
            {
                ScanIdentifier(ref token);

                // A reserved word is a valid private name (#if)
                token.Value ??= Intern(_text.AsSpan(start + 1, token.End - start - 1));
                token.Kind = TokenKind.PrivateName;
                token.Start = start;
                return token;
            }
        }

        Raise(start, "Invalid or unexpected token");
        return token;
    }

    private Token ScanIdentifier(ref Token token)
    {
        var text = _text;
        var start = _pos;
        var pos = _pos;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c < 128 && (AsciiIdentifierFlags[c] & 2) != 0)
            {
                pos++;
                continue;
            }

            if (c == '\\' || c >= 128)
            {
                _pos = pos;
                return ScanIdentifierSlow(ref token, start);
            }

            break;
        }

        _pos = pos;
        token.End = pos;
        var span = text.AsSpan(start, pos - start);
        if (span.Length is >= 2 and <= 10 && span[0] is >= 'a' and <= 'z' && KeywordLookup.TryGetValue(span, out var kind))
        {
            token.Kind = kind;
            return token;
        }

        token.Kind = TokenKind.Identifier;
        token.Value = Intern(span);
        return token;
    }

    private Token ScanIdentifierSlow(ref Token token, int start)
    {
        var text = _text;
        var builder = GetBuilder();
        builder.Append(text, start, _pos - start);
        var escaped = false;
        var first = _pos == start;
        while (_pos < text.Length)
        {
            var c = text[_pos];
            int cp;
            if (c == '\\')
            {
                var escapeStart = _pos;
                if (CharAt(_pos + 1) != 'u')
                    Raise(escapeStart, "Invalid Unicode escape sequence");

                _pos += 2;
                cp = ScanUnicodeEscapeValue();
                if (cp < 0)
                    Raise(escapeStart, "Invalid Unicode escape sequence");

                if (first ? !IsIdentifierStartCodePoint(cp) : !IsIdentifierPartCodePoint(cp))
                    Raise(escapeStart, "Invalid Unicode escape sequence");

                escaped = true;
            }
            else
            {
                cp = c < 128 ? c : CodePointAt(_pos);
                if (first ? !IsIdentifierStartCodePoint(cp) : !IsIdentifierPartCodePoint(cp))
                    break;

                _pos += cp > 0xFFFF ? 2 : 1;
            }

            if (cp > 0xFFFF)
            {
                builder.Append(char.ConvertFromUtf32(cp));
            }
            else
            {
                builder.Append((char)cp);
            }

            first = false;
        }

        token.End = _pos;
        var name = Intern(builder.ToString());
        if (escaped)
        {
            token.Flags |= TokenFlags.Escaped;
        }
        else if (name.Length is >= 2 and <= 10 && Keywords.TryGetValue(name, out var kind))
        {
            token.Kind = kind;
            return token;
        }

        token.Kind = TokenKind.Identifier;
        token.Value = name;
        return token;
    }

    /// <summary>Reads the code point of <c>XXXX</c> or <c>{X...}</c> after <c>\u</c>; returns -1 when the escape is malformed.</summary>
    private int ScanUnicodeEscapeValue()
    {
        var text = _text;
        if (CharAt(_pos) == '{')
        {
            var pos = _pos + 1;
            var value = 0;
            var digits = 0;
            while (pos < text.Length && HexValue(text[pos]) is var d and >= 0)
            {
                value = (value * 16) + d;
                if (value > 0x10FFFF)
                    return -1;

                digits++;
                pos++;
            }

            if (digits == 0 || CharAt(pos) != '}')
                return -1;

            _pos = pos + 1;
            return value;
        }

        if (_pos + 4 > text.Length)
            return -1;

        var result = 0;
        for (var i = 0; i < 4; i++)
        {
            var d = HexValue(text[_pos + i]);
            if (d < 0)
                return -1;

            result = (result * 16) + d;
        }

        _pos += 4;
        return result;
    }

    private Token ScanString(ref Token token, char quote)
    {
        var text = _text;
        var start = _pos;
        var pos = _pos + 1;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == quote)
            {
                _pos = pos + 1;
                token.Kind = TokenKind.StringLiteral;
                token.End = _pos;
                token.Value = pos - start - 1 <= 1 ? Intern(text.AsSpan(start + 1, pos - start - 1)) : text.Substring(start + 1, pos - start - 1);
                return token;
            }

            if (c == '\\' || c == '\n' || c == '\r')
                break;

            pos++;
        }

        var builder = GetBuilder();
        builder.Append(text, start + 1, pos - start - 1);
        _pos = pos;
        token.Kind = TokenKind.StringLiteral;
        while (true)
        {
            if (_pos >= text.Length)
                Raise(start, "Invalid or unexpected token");

            var c = text[_pos];
            if (c == quote)
            {
                _pos++;
                break;
            }

            if (c == '\\')
            {
                token.Flags |= TokenFlags.HasEscape;
                ScanEscape(builder, ref token, isTemplate: false);
                continue;
            }

            if (c is '\n' or '\r')
                Raise(start, "Invalid or unexpected token");

            builder.Append(c);
            _pos++;
        }

        token.End = _pos;
        token.Value = builder.ToString();
        return token;
    }

    /// <summary>
    /// Scans an escape sequence (at the backslash) of a string or of a template. In a template, an invalid escape sets
    /// <see cref="TokenFlags.InvalidEscape"/> instead of reporting an error.
    /// </summary>
    private void ScanEscape(StringBuilder builder, ref Token token, bool isTemplate)
    {
        var text = _text;
        var escapeStart = _pos;
        _pos++;
        if (_pos >= text.Length)
        {
            if (isTemplate)
                Raise(token.Start, "Unterminated template literal");

            Raise(token.Start, "Invalid or unexpected token");
        }

        var c = text[_pos];
        switch (c)
        {
            case 'b':
                builder.Append('\b');
                _pos++;
                return;
            case 'f':
                builder.Append('\f');
                _pos++;
                return;
            case 'n':
                builder.Append('\n');
                _pos++;
                return;
            case 'r':
                builder.Append('\r');
                _pos++;
                return;
            case 't':
                builder.Append('\t');
                _pos++;
                return;
            case 'v':
                builder.Append('\v');
                _pos++;
                return;
            case '\r':
                _pos++;
                if (CharAt(_pos) == '\n')
                {
                    _pos++;
                }

                return;
            case '\n':
            case '\u2028':
            case '\u2029':
                _pos++;
                return;
            case 'x':
            {
                _pos++;
                var d1 = HexValue(CharAt(_pos));
                var d2 = d1 >= 0 ? HexValue(CharAt(_pos + 1)) : -1;
                if (d2 < 0)
                {
                    InvalidEscape(ref token, escapeStart, isTemplate, "Invalid hexadecimal escape sequence");
                    return;
                }

                _pos += 2;
                builder.Append((char)((d1 * 16) + d2));
                return;
            }

            case 'u':
            {
                _pos++;
                var position = _pos;
                var cp = ScanUnicodeEscapeValue();
                if (cp < 0)
                {
                    _pos = position;
                    InvalidEscape(ref token, escapeStart, isTemplate, "Invalid Unicode escape sequence");
                    return;
                }

                if (cp > 0xFFFF)
                {
                    builder.Append(char.ConvertFromUtf32(cp));
                }
                else
                {
                    builder.Append((char)cp);
                }

                return;
            }

            case >= '0' and <= '7':
            {
                if (c == '0' && !IsDecimalDigit(CharAt(_pos + 1)))
                {
                    builder.Append('\0');
                    _pos++;
                    return;
                }

                if (isTemplate)
                {
                    InvalidEscape(ref token, escapeStart, isTemplate, "Octal escape sequences are not allowed in template strings");
                    return;
                }

                LegacyEscape(ref token, escapeStart, "Octal escape sequences are not allowed in strict mode");

                // LegacyOctalEscapeSequence
                var value = c - '0';
                _pos++;
                if (IsOctalDigit(CharAt(_pos)))
                {
                    value = (value * 8) + (text[_pos] - '0');
                    _pos++;
                    if (c <= '3' && IsOctalDigit(CharAt(_pos)))
                    {
                        value = (value * 8) + (text[_pos] - '0');
                        _pos++;
                    }
                }

                builder.Append((char)value);
                return;
            }

            case '8':
            case '9':
                if (isTemplate)
                {
                    InvalidEscape(ref token, escapeStart, isTemplate, "\\8 and \\9 are not allowed in template strings");
                    return;
                }

                LegacyEscape(ref token, escapeStart, "\\8 and \\9 are not allowed in strict mode");
                builder.Append(c);
                _pos++;
                return;
            default:
                if (char.IsHighSurrogate(c) && char.IsLowSurrogate((char)CharAt(_pos + 1)))
                {
                    builder.Append(c).Append(text[_pos + 1]);
                    _pos += 2;
                    return;
                }

                builder.Append(c);
                _pos++;
                return;
        }
    }

    private void LegacyEscape(ref Token token, int position, string message)
    {
        if (Strict)
            Raise(position, message);

        if ((token.Flags & TokenFlags.LegacyOctal) == 0)
        {
            token.Flags |= TokenFlags.LegacyOctal;
            token.EscapePosition = position;
        }
    }

    private void InvalidEscape(ref Token token, int position, bool isTemplate, string message)
    {
        if (!isTemplate)
            Raise(position, message);

        if ((token.Flags & TokenFlags.InvalidEscape) == 0)
        {
            token.Flags |= TokenFlags.InvalidEscape;
            token.EscapePosition = position;
        }
    }

    /// <summary>Scans a template part: <paramref name="token"/> starts at the backtick or at the '}' that ends a substitution.</summary>
    private Token ScanTemplate(ref Token token)
    {
        var text = _text;
        var start = token.Start;
        _pos = start + 1;
        var builder = GetBuilder();
        bool tail;
        var hasCarriageReturn = false;
        while (true)
        {
            if (_pos >= text.Length)
                Raise(start, "Unterminated template literal");

            var c = text[_pos];
            if (c == '`')
            {
                _pos++;
                tail = true;
                break;
            }

            if (c == '$' && CharAt(_pos + 1) == '{')
            {
                _pos += 2;
                tail = false;
                break;
            }

            if (c == '\\')
            {
                if (CharAt(_pos + 1) == '\r')
                {
                    hasCarriageReturn = true;
                }

                ScanEscape(builder, ref token, isTemplate: true);
                continue;
            }

            if (c == '\r')
            {
                hasCarriageReturn = true;
                _pos++;
                if (CharAt(_pos) == '\n')
                {
                    _pos++;
                }

                builder.Append('\n');
                continue;
            }

            builder.Append(c);
            _pos++;
        }

        token.Kind = TokenKind.Template;
        token.End = _pos;
        if (tail)
        {
            token.Flags |= TokenFlags.TemplateTail;
        }

        token.Value = (token.Flags & TokenFlags.InvalidEscape) != 0 ? null : builder.ToString();
        var rawStart = start + 1;
        var rawEnd = _pos - (tail ? 1 : 2);
        if (hasCarriageReturn)
        {
            var raw = GetBuilder();
            for (var i = rawStart; i < rawEnd; i++)
            {
                var ch = text[i];
                if (ch == '\r')
                {
                    if (i + 1 < rawEnd && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    raw.Append('\n');
                }
                else
                {
                    raw.Append(ch);
                }
            }

            token.Raw = raw.ToString();
        }
        else
        {
            token.Raw = text[rawStart..rawEnd];
        }

        return token;
    }

    private Token ScanNumber(ref Token token)
    {
        var text = _text;
        var start = _pos;
        var c = text[_pos];
        if (c == '0' && _pos + 1 < text.Length)
        {
            var radix = (text[_pos + 1] | 0x20) switch
            {
                'x' => 16,
                'o' => 8,
                'b' => 2,
                _ => 0,
            };

            if (radix != 0)
            {
                _pos += 2;
                var digitsStart = _pos;
                if (!ScanDigits(radix))
                    Raise(start, "Invalid or unexpected token");

                var digitsEnd = _pos;
                if (CharAt(_pos) == 'n')
                {
                    _pos++;
                    CheckAfterNumber(start);
                    token.Kind = TokenKind.BigIntLiteral;
                    token.BigInt = ParseRadixBigInteger(text.AsSpan(digitsStart, digitsEnd - digitsStart), radix);
                    token.End = _pos;
                    return token;
                }

                CheckAfterNumber(start);
                token.Kind = TokenKind.NumericLiteral;
                token.Number = (double)ParseRadixBigInteger(text.AsSpan(digitsStart, digitsEnd - digitsStart), radix);
                token.End = _pos;
                return token;
            }

            var second = text[_pos + 1];
            if (second == '_')
                Raise(_pos + 1, "Numeric separator can not be used after leading 0");

            if (IsDecimalDigit(second))
            {
                // LegacyOctalIntegerLiteral or NonOctalDecimalIntegerLiteral
                var pos = _pos + 1;
                var isOctal = true;
                while (pos < text.Length && IsDecimalDigit(text[pos]))
                {
                    if (text[pos] >= '8')
                    {
                        isOctal = false;
                    }

                    pos++;
                }

                if (Strict)
                    Raise(start, isOctal ? "Octal literals are not allowed in strict mode" : "Decimals with leading zeros are not allowed in strict mode");

                if (CharAt(pos) == '_')
                    Raise(pos, "Numeric separators are not allowed here");

                token.Flags |= TokenFlags.LegacyOctal;
                if (isOctal)
                {
                    _pos = pos;
                    if (CharAt(_pos) == 'n')
                        Raise(start, "Invalid BigInt literal");

                    CheckAfterNumber(start);
                    token.Kind = TokenKind.NumericLiteral;
                    token.Number = (double)ParseRadixBigInteger(text.AsSpan(start + 1, pos - start - 1), 8);
                    token.End = _pos;
                    return token;
                }

                _pos = pos;
                return ScanDecimalRest(ref token, start, isNonOctalDecimal: true);
            }
        }

        if (c != '.')
        {
            ScanDigits(10);
        }

        return ScanDecimalRest(ref token, start, isNonOctalDecimal: false);
    }

    private Token ScanDecimalRest(ref Token token, int start, bool isNonOctalDecimal)
    {
        var text = _text;
        var isInteger = true;
        if (CharAt(_pos) == '.')
        {
            isInteger = false;
            _pos++;
            if (CharAt(_pos) == '_')
                Raise(_pos, "Numeric separators are not allowed here");

            ScanDigits(10);
        }

        if ((CharAt(_pos) | 0x20) == 'e')
        {
            isInteger = false;
            _pos++;
            if (CharAt(_pos) is '+' or '-')
            {
                _pos++;
            }

            if (!ScanDigits(10))
                Raise(start, "Invalid or unexpected token");
        }

        var end = _pos;
        if (CharAt(_pos) == 'n')
        {
            if (!isInteger || isNonOctalDecimal)
                Raise(start, "Invalid BigInt literal");

            _pos++;
            CheckAfterNumber(start);
            token.Kind = TokenKind.BigIntLiteral;
            token.BigInt = BigInteger.Parse(RemoveSeparators(text.AsSpan(start, end - start), stackalloc char[64]), NumberStyles.None, CultureInfo.InvariantCulture);
            token.End = _pos;
            return token;
        }

        CheckAfterNumber(start);
        token.Kind = TokenKind.NumericLiteral;
        token.End = _pos;
        var span = text.AsSpan(start, end - start);
        if (isInteger && span.Length <= 15)
        {
            long value = 0;
            var ok = true;
            foreach (var ch in span)
            {
                if (ch == '_')
                    continue;

                if (!IsDecimalDigit(ch))
                {
                    ok = false;
                    break;
                }

                value = (value * 10) + (ch - '0');
            }

            if (ok)
            {
                token.Number = value;
                return token;
            }
        }

        token.Number = double.Parse(RemoveSeparators(span, stackalloc char[64]), NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
        return token;
    }

    private static ReadOnlySpan<char> RemoveSeparators(ReadOnlySpan<char> span, Span<char> buffer)
    {
        if (!span.Contains('_'))
            return span;

        if (buffer.Length < span.Length)
        {
            buffer = new char[span.Length];
        }

        var length = 0;
        foreach (var c in span)
        {
            if (c != '_')
            {
                buffer[length++] = c;
            }
        }

        return buffer[..length];
    }

    private static BigInteger ParseRadixBigInteger(ReadOnlySpan<char> digits, int radix)
    {
        if (digits.Length <= 12)
        {
            long small = 0;
            foreach (var c in digits)
            {
                if (c != '_')
                {
                    small = (small * radix) + HexValue(c);
                }
            }

            return small;
        }

        BigInteger value = BigInteger.Zero;
        foreach (var c in digits)
        {
            if (c != '_')
            {
                value = (value * radix) + HexValue(c);
            }
        }

        return value;
    }

    /// <summary>Scans digits of the radix with the numeric separators; returns whether there is at least one digit.</summary>
    private bool ScanDigits(int radix)
    {
        var text = _text;
        var any = false;
        var lastWasSeparator = false;
        while (_pos < text.Length)
        {
            var c = text[_pos];
            if (c == '_')
            {
                if (!any || lastWasSeparator)
                    Raise(_pos, lastWasSeparator ? "Only one underscore is allowed as numeric separator" : "Numeric separators are not allowed here");

                lastWasSeparator = true;
                _pos++;
                continue;
            }

            var d = HexValue(c);
            if (d < 0 || d >= radix)
                break;

            any = true;
            lastWasSeparator = false;
            _pos++;
        }

        if (lastWasSeparator)
            Raise(_pos - 1, "Numeric separators are not allowed at the end of numeric literals");

        return any;
    }

    private void CheckAfterNumber(int start)
    {
        if (_pos >= _text.Length)
            return;

        var c = _text[_pos];
        if (c < 128)
        {
            if ((AsciiIdentifierFlags[c] & 2) != 0 || c == '\\')
                Raise(start, "Invalid or unexpected token");
        }
        else if (IsIdentifierStartCodePoint(CodePointAt(_pos)))
        {
            Raise(start, "Invalid or unexpected token");
        }
    }

    private StringBuilder GetBuilder()
    {
        var builder = _builder ??= new StringBuilder();
        builder.Clear();
        return builder;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLineTerminator(int c) => c is '\n' or '\r' or '\u2028' or '\u2029';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDecimalDigit(int c) => (uint)(c - '0') <= 9;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsOctalDigit(int c) => (uint)(c - '0') <= 7;

    public static int HexValue(int c)
    {
        if ((uint)(c - '0') <= 9)
            return c - '0';

        var lower = c | 0x20;
        if ((uint)(lower - 'a') <= 5)
            return lower - 'a' + 10;

        return -1;
    }

    /// <summary>The white space other than the ASCII ones: NBSP, ZWNBSP and the other code points of the Zs category.</summary>
    public static bool IsWhiteSpaceNonAscii(int c) => c is '\u00A0' or '\uFEFF' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u202F' or '\u205F' or '\u3000';

    public static bool IsIdentifierStartCodePoint(int cp)
    {
        if (cp < 128)
            return (AsciiIdentifierFlags[cp] & 1) != 0;

        return UnicodeProperties.IsIdStart(cp);
    }

    public static bool IsIdentifierPartCodePoint(int cp)
    {
        if (cp < 128)
            return (AsciiIdentifierFlags[cp] & 2) != 0;

        return cp is 0x200C or 0x200D || UnicodeProperties.IsIdContinue(cp);
    }

    private static byte[] CreateAsciiIdentifierFlags()
    {
        var result = new byte[128];
        for (var c = 'a'; c <= 'z'; c++)
        {
            result[c] = 3;
        }

        for (var c = 'A'; c <= 'Z'; c++)
        {
            result[c] = 3;
        }

        for (var c = '0'; c <= '9'; c++)
        {
            result[c] = 2;
        }

        result['$'] = 3;
        result['_'] = 3;
        return result;
    }
}
