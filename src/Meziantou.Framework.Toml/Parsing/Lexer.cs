using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Parsing;

/// <summary>
/// Lexer enumerator that generates <see cref="SyntaxTokenValue"/>, to be used from a foreach.
/// </summary>
internal sealed class Lexer
{
    private SyntaxTokenValue _token;
    private List<DiagnosticMessage>? _errors;
    private const int Eof = -1;
    private readonly ReadOnlyMemory<char> _text;
    private readonly int _textLength;
    private readonly StringBuilder _textBuilder;
    private LexerInternalState _current;
    private readonly string _sourcePath;

    /// <summary>
    /// Initialize a new instance of this <see cref="Lexer" />.
    /// </summary>
    /// <param name="text">The TOML payload.</param>
    /// <param name="sourcePath">An optional source name used in diagnostics.</param>
    public Lexer(ReadOnlyMemory<char> text, string sourcePath)
    {
        _text = text;
        _textLength = text.Length;
        _sourcePath = sourcePath ?? string.Empty;
        _textBuilder = new StringBuilder();
        Reset();
    }

    internal ReadOnlyMemory<char> Text => _text;

    internal string SourcePath => _sourcePath;

    /// <summary>
    /// Gets a boolean indicating whether this lexer has errors.
    /// </summary>
    public bool HasErrors => _errors != null && _errors.Count > 0;

    /// <summary>
    /// Gets error messages.
    /// </summary>
    public IEnumerable<DiagnosticMessage> Errors => _errors ?? Enumerable.Empty<DiagnosticMessage>();

    /// <summary>
    /// Gets or sets a value indicating whether the lexer should decode scalar values (for example, string escape sequences).
    /// </summary>
    public bool DecodeScalars { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the text had a byte order mark, which is not part of <see cref="Text"/>.</summary>
    public bool HasByteOrderMark { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the lexer should eagerly materialize simple string values
    /// (single-line basic strings without escape sequences) even when <see cref="DecodeScalars"/> is <c>false</c>.
    /// </summary>
    public bool EagerStringValues { get; set; }

    // The number of errors the lexer records. A strict parser stops at the first one, so it does not need the others, and a
    // document with one invalid character per byte would record one error per byte.
    public int MaxErrorCount { get; set; } = int.MaxValue;

    /// <summary>
    /// Gets or sets a value indicating whether the lexer should emit hidden tokens (whitespace and comments).
    /// </summary>
    public bool EmitHiddenTokens { get; set; } = true;

    public bool MoveNext()
    {
        // If we have errors or we are already at the end of the file, we don't continue
        if (_token.Kind == TokenKind.Eof)
        {
            return false;
        }
        var emitHiddenTokens = EmitHiddenTokens;
        if (State == LexerState.Key)
        {
            NextTokenForKey(emitHiddenTokens);
        }
        else
        {
            NextTokenForValue(emitHiddenTokens);
        }

        return true;
    }

    public ref readonly SyntaxTokenValue Token => ref _token;

    public LexerState State { get; set; }

    private TextPosition CurrentPosition => _current.Position;

    private Char32 CurrentCharacter => _current.CurrentChar;

    private void NextTokenForKey(bool emitHiddenTokens)
    {
        while (true)
        {
            var start = CurrentPosition;
            var c = CurrentCharacter;
            switch (c)
            {
                case '\n':
                    _token = new SyntaxTokenValue(TokenKind.NewLine, start, start);
                    NextChar();
                    return;
                case '\r':
                    NextChar();
                    // case of: \r\n
                    if (CurrentCharacter == '\n')
                    {
                        _token = new SyntaxTokenValue(TokenKind.NewLine, start, CurrentPosition);
                        NextChar();
                        return;
                    }
                    AddError($"Invalid \\r not followed by \\n", start, start);
                    // case of \r
                    _token = new SyntaxTokenValue(TokenKind.NewLine, start, start);
                    return;
                case '#':
                {
                    NextChar();
                    var end = ReadCommentCore(start);
                    if (emitHiddenTokens)
                    {
                        _token = new SyntaxTokenValue(TokenKind.Comment, start, end);
                        return;
                    }
                    continue;
                }
                case '.':
                    NextChar();
                    _token = new SyntaxTokenValue(TokenKind.Dot, start, start);
                    return;
                case '=': // in the context of a key, we need to parse up to the =
                    NextChar();
                    _token = new SyntaxTokenValue(TokenKind.Equal, start, start);
                    return;
                case ',':
                    _token = new SyntaxTokenValue(TokenKind.Comma, start, start);
                    NextChar();
                    return;
                case '{':
                    _token = new SyntaxTokenValue(TokenKind.OpenBrace, start, start);
                    NextChar();
                    return;
                case '}':
                    _token = new SyntaxTokenValue(TokenKind.CloseBrace, start, start);
                    NextChar();
                    return;
                case '[':
                    NextChar();
                    // case of: ]]
                    if (CurrentCharacter == '[')
                    {
                        _token = new SyntaxTokenValue(TokenKind.OpenBracketDouble, start, CurrentPosition);
                        NextChar();
                        return;
                    }
                    _token = new SyntaxTokenValue(TokenKind.OpenBracket, start, start);
                    return;
                case ']':
                    NextChar();
                    // case of: ]]
                    if (CurrentCharacter == ']')
                    {
                        _token = new SyntaxTokenValue(TokenKind.CloseBracketDouble, start, CurrentPosition);
                        NextChar();
                        return;
                    }
                    _token = new SyntaxTokenValue(TokenKind.CloseBracket, start, start);
                    return;
                case '"':
                    ReadString(start, allowMultiline: false, materializeSimpleValue: false);
                    return;
                case '\'':
                    ReadStringLiteral(start, false);
                    return;
                case Eof:
                    _token = new SyntaxTokenValue(TokenKind.Eof, start, start);
                    return;
                default:
                    // Eat any whitespace
                    if (ConsumeWhitespaceCore(out var whitespaceEnd))
                    {
                        if (emitHiddenTokens)
                        {
                            _token = new SyntaxTokenValue(TokenKind.Whitespaces, start, whitespaceEnd);
                            return;
                        }

                        continue;
                    }

                    c = CurrentCharacter;
                    if (CharHelper.IsKeyStart(c))
                    {
                        ReadKey();
                        return;
                    }

                    // invalid char. The token ends at the last UTF-16 code unit of the character, so it covers a whole
                    // surrogate pair.
                    _token = new SyntaxTokenValue(TokenKind.Invalid, start, new TextPosition(_current.NextPosition.Offset - 1, start.Line, start.Column));
                    NextChar();
                    return;
            }
        }
    }

    private void NextTokenForValue(bool emitHiddenTokens)
    {
        while (true)
        {
            var start = CurrentPosition;
            var c = CurrentCharacter;
            switch (c)
            {
                case '\n':
                    _token = new SyntaxTokenValue(TokenKind.NewLine, start, CurrentPosition);
                    NextChar();
                    return;
                case '\r':
                    NextChar();
                    // case of: \r\n
                    if (CurrentCharacter == '\n')
                    {
                        _token = new SyntaxTokenValue(TokenKind.NewLine, start, CurrentPosition);
                        NextChar();
                        return;
                    }

                    AddError($"Invalid \\r not followed by \\n", start, start);
                    // case of \r
                    _token = new SyntaxTokenValue(TokenKind.NewLine, start, start);
                    return;
                case '#':
                {
                    NextChar();
                    var end = ReadCommentCore(start);
                    if (emitHiddenTokens)
                    {
                        _token = new SyntaxTokenValue(TokenKind.Comment, start, end);
                        return;
                    }
                    continue;
                }
                case ',':
                    _token = new SyntaxTokenValue(TokenKind.Comma, start, start);
                    NextChar();
                    return;
                case '[':
                    NextChar();
                    _token = new SyntaxTokenValue(TokenKind.OpenBracket, start, start);
                    return;
                case ']':
                    NextChar();
                    _token = new SyntaxTokenValue(TokenKind.CloseBracket, start, start);
                    return;
                case '{':
                    _token = new SyntaxTokenValue(TokenKind.OpenBrace, start, start);
                    NextChar();
                    return;
                case '}':
                    _token = new SyntaxTokenValue(TokenKind.CloseBrace, start, start);
                    NextChar();
                    return;
                case '"':
                    ReadString(start, allowMultiline: true, materializeSimpleValue: EagerStringValues);
                    return;
                case '\'':
                    ReadStringLiteral(start, true);
                    return;
                case Eof:
                    _token = new SyntaxTokenValue(TokenKind.Eof, start, start);
                    return;
                default:
                    // Eat any whitespace
                    if (ConsumeWhitespaceCore(out var whitespaceEnd))
                    {
                        if (emitHiddenTokens)
                        {
                            _token = new SyntaxTokenValue(TokenKind.Whitespaces, start, whitespaceEnd);
                            return;
                        }

                        continue;
                    }

                    // Handle inf, +inf, -inf, true, false
                    c = CurrentCharacter;
                    if (c == '+' || c == '-' || CharHelper.IsIdentifierStart(c))
                    {
                        ReadSpecialToken();
                        return;
                    }

                    if (CharHelper.IsDigit(c))
                    {
                        ReadNumberOrDate();
                        return;
                    }

                    // invalid char. The token ends at the last UTF-16 code unit of the character, so it covers a whole
                    // surrogate pair.
                    _token = new SyntaxTokenValue(TokenKind.Invalid, start, new TextPosition(_current.NextPosition.Offset - 1, start.Line, start.Column));
                    NextChar();
                    return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<char> GetSpanUnchecked(int offset, int length)
    {
        return _text.Span.Slice(offset, length);
    }

    internal ReadOnlySpan<char> GetSpan(int offset, int length)
    {
        if (offset < 0 || length < 0)
        {
            return default;
        }

        if ((uint)offset > (uint)_textLength || offset + length > _textLength)
        {
            return default;
        }

        return _text.Span.Slice(offset, length);
    }

    internal string? GetString(int offset, int length)
    {
        var span = GetSpan(offset, length);
        return span.IsEmpty ? null : span.ToString();
    }

    private bool ConsumeWhitespaceCore(out TextPosition end)
    {
        var text = _text.Span;
        var startPosition = CurrentPosition;
        var offset = startPosition.Offset;
        var remaining = _textLength - offset;
        if (remaining <= 0)
        {
            end = startPosition;
            return false;
        }

        var span = text.Slice(offset, remaining);
        var i = 0;
        while ((uint)i < (uint)span.Length)
        {
            var c = span[i];
            if (c != ' ' && c != '\t')
            {
                break;
            }
            i++;
        }

        if (i == 0)
        {
            end = startPosition;
            return false;
        }

        end = new TextPosition(offset + i - 1, startPosition.Line, startPosition.Column + i - 1);

        var nextPosition = new TextPosition(offset + i, startPosition.Line, startPosition.Column + i);
        _current.Position = nextPosition;
        _current.NextPosition = nextPosition;
        _current.CurrentChar = NextCharFromReader();
        return true;
    }

    private void ReadKey()
    {
        var text = _text.Span;
        static bool IsKeyContinueChar(char c)
            => (c >= 'a' && c <= 'z') ||
               (c >= 'A' && c <= 'Z') ||
               c == '_' ||
               c == '-' ||
               (c >= '0' && c <= '9');

        var start = CurrentPosition;
        var offset = start.Offset;
        var remaining = _textLength - offset;
        var span = remaining > 0 ? text.Slice(offset, remaining) : default;

        var hash = 14695981039346656037UL;
        var i = 0;
        while ((uint)i < (uint)span.Length)
        {
            var c = span[i];
            if (!IsKeyContinueChar(c))
            {
                break;
            }

            hash = HashAdd(hash, c);
            i++;
        }

        if (i > 0)
        {
            var end = new TextPosition(offset + i - 1, start.Line, start.Column + i - 1);
            _token = new SyntaxTokenValue(TokenKind.BasicKey, start, end, stringValue: null, data: hash);

            var nextPosition = new TextPosition(offset + i, start.Line, start.Column + i);
            _current.Position = nextPosition;
            _current.NextPosition = nextPosition;
            _current.CurrentChar = NextCharFromReader();
            return;
        }

        Debug.Assert(false, "ReadKey fast-path fallback should not run for valid key starts.");
        var slowStart = CurrentPosition;
        var slowEnd = CurrentPosition;
        var slowHash = 14695981039346656037UL;
        while (CharHelper.IsKeyContinue(CurrentCharacter))
        {
            slowHash = HashAdd(slowHash, CurrentCharacter.Code);
            slowEnd = CurrentPosition;
            NextChar();
        }

        _token = new SyntaxTokenValue(TokenKind.BasicKey, slowStart, slowEnd, stringValue: null, data: slowHash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong HashAdd(ulong hash, int codePoint)
    {
        hash ^= unchecked((uint)codePoint);
        return hash * 1099511628211UL;
    }

    private void ReadSpecialToken()
    {
        var start = CurrentPosition;
        var end = CurrentPosition;
        var firstChar = CurrentCharacter;
        NextChar();

        // If we have a digit, this is a -1 or +2
        if ((firstChar == '+' || firstChar == '-') && CharHelper.IsDigit(CurrentCharacter))
        {
            ReadNumberOrDate(firstChar, start);
            return;
        }

        if (firstChar == 't')
        {
            var text = _text.Span;
            // Fast-path for the common boolean literal `true`.
            // We already consumed the initial 't' with NextChar(), so we only need to validate and skip "rue".
            var offset = CurrentPosition.Offset;
            var remaining = _textLength - offset;
            if (remaining >= 3)
            {
                var span = text.Slice(offset, remaining);
                if (span.Length >= 3 && span[0] == 'r' && span[1] == 'u' && span[2] == 'e' &&
                    (span.Length == 3 || !CharHelper.IsIdentifierContinue((Char32)span[3])))
                {
                    end = new TextPosition(start.Offset + 3, start.Line, start.Column + 3);

                    var nextPosition = new TextPosition(start.Offset + 4, start.Line, start.Column + 4);
                    _current.Position = nextPosition;
                    _current.NextPosition = nextPosition;
                    _current.CurrentChar = NextCharFromReader();

                    _token = new SyntaxTokenValue(TokenKind.True, start, end, stringValue: null, data: 1);
                    return;
                }
            }

            if (TryConsumeIdentifierChar('r', ref end) &&
                TryConsumeIdentifierChar('u', ref end) &&
                TryConsumeIdentifierChar('e', ref end) &&
                !CharHelper.IsIdentifierContinue(CurrentCharacter))
            {
                _token = new SyntaxTokenValue(TokenKind.True, start, end, stringValue: null, data: 1);
                return;
            }

            ConsumeIdentifierContinue(ref end);
            _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
            return;
        }

        if (firstChar == 'f')
        {
            var text = _text.Span;
            // Fast-path for the boolean literal `false`.
            var offset = CurrentPosition.Offset;
            var remaining = _textLength - offset;
            if (remaining >= 4)
            {
                var span = text.Slice(offset, remaining);
                if (span.Length >= 4 && span[0] == 'a' && span[1] == 'l' && span[2] == 's' && span[3] == 'e' &&
                    (span.Length == 4 || !CharHelper.IsIdentifierContinue((Char32)span[4])))
                {
                    end = new TextPosition(start.Offset + 4, start.Line, start.Column + 4);

                    var nextPosition = new TextPosition(start.Offset + 5, start.Line, start.Column + 5);
                    _current.Position = nextPosition;
                    _current.NextPosition = nextPosition;
                    _current.CurrentChar = NextCharFromReader();

                    _token = new SyntaxTokenValue(TokenKind.False, start, end, stringValue: null, data: 0);
                    return;
                }
            }

            if (TryConsumeIdentifierChar('a', ref end) &&
                TryConsumeIdentifierChar('l', ref end) &&
                TryConsumeIdentifierChar('s', ref end) &&
                TryConsumeIdentifierChar('e', ref end) &&
                !CharHelper.IsIdentifierContinue(CurrentCharacter))
            {
                _token = new SyntaxTokenValue(TokenKind.False, start, end, stringValue: null, data: 0);
                return;
            }

            ConsumeIdentifierContinue(ref end);
            _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
            return;
        }

        if (firstChar == 'i')
        {
            if (TryConsumeIdentifierChar('n', ref end) &&
                TryConsumeIdentifierChar('f', ref end) &&
                !CharHelper.IsIdentifierContinue(CurrentCharacter))
            {
                _token = new SyntaxTokenValue(TokenKind.Infinite, start, end, stringValue: null,
                    data: unchecked((ulong)BitConverter.DoubleToInt64Bits(double.PositiveInfinity)));
                return;
            }

            ConsumeIdentifierContinue(ref end);
            _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
            return;
        }

        if (firstChar == 'n')
        {
            if (TryConsumeIdentifierChar('a', ref end) &&
                TryConsumeIdentifierChar('n', ref end) &&
                !CharHelper.IsIdentifierContinue(CurrentCharacter))
            {
                _token = new SyntaxTokenValue(TokenKind.Nan, start, end, stringValue: null,
                    data: unchecked((ulong)BitConverter.DoubleToInt64Bits(double.NaN)));
                return;
            }

            ConsumeIdentifierContinue(ref end);
            _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
            return;
        }

        if (firstChar == '+' || firstChar == '-')
        {
            if (CurrentCharacter == 'i')
            {
                if (TryConsumeIdentifierChar('i', ref end) &&
                    TryConsumeIdentifierChar('n', ref end) &&
                    TryConsumeIdentifierChar('f', ref end) &&
                    !CharHelper.IsIdentifierContinue(CurrentCharacter))
                {
                    var data = firstChar == '-'
                        ? unchecked((ulong)BitConverter.DoubleToInt64Bits(double.NegativeInfinity))
                        : unchecked((ulong)BitConverter.DoubleToInt64Bits(double.PositiveInfinity));
                    var kind = firstChar == '-' ? TokenKind.NegativeInfinite : TokenKind.PositiveInfinite;
                    _token = new SyntaxTokenValue(kind, start, end, stringValue: null, data: data);
                    return;
                }

                ConsumeIdentifierContinue(ref end);
                _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
                return;
            }

            if (CurrentCharacter == 'n')
            {
                if (TryConsumeIdentifierChar('n', ref end) &&
                    TryConsumeIdentifierChar('a', ref end) &&
                    TryConsumeIdentifierChar('n', ref end) &&
                    !CharHelper.IsIdentifierContinue(CurrentCharacter))
                {
                    var kind = firstChar == '-' ? TokenKind.NegativeNan : TokenKind.PositiveNan;
                    _token = new SyntaxTokenValue(kind, start, end, stringValue: null,
                        data: unchecked((ulong)BitConverter.DoubleToInt64Bits(double.NaN)));
                    return;
                }
            }
        }

        ConsumeIdentifierContinue(ref end);
        _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryConsumeIdentifierChar(Char32 expected, ref TextPosition end)
    {
        if (CurrentCharacter != expected)
        {
            return false;
        }

        end = CurrentPosition;
        NextChar();
        return true;
    }

    private void ConsumeIdentifierContinue(ref TextPosition end)
    {
        while (true)
        {
            var c = CurrentCharacter;
            if (!CharHelper.IsIdentifierContinue(c))
            {
                break;
            }

            end = CurrentPosition;
            NextChar();
        }
    }

    private void ReadNumberOrDate(Char32? signPrefix = null, TextPosition? signPrefixPos = null)
    {
        var start = signPrefixPos ?? CurrentPosition;
        var end = CurrentPosition;
        var isFloat = false;

        var positionFirstDigit = CurrentPosition;

        //var firstChar = numberPrefix ?? CurrentCharacter;
        var hasLeadingSign = signPrefix != null;
        var hasLeadingZero = CurrentCharacter == '0';

        // Reset parsing of integer
        _textBuilder.Length = 0;
        if (hasLeadingSign) _textBuilder.AppendUtf32(signPrefix!.Value);

        // If we start with 0, it might be an hexa, octal or binary literal
        if (hasLeadingZero)
        {
            NextChar(); // Skip first digit character
            if (!hasLeadingSign && (CurrentCharacter == 'x' || CurrentCharacter == 'X' || CurrentCharacter == 'o' || CurrentCharacter == 'O' || CurrentCharacter == 'b' || CurrentCharacter == 'B'))
            {
                string name;
                Func<Char32, bool> match;
                Func<Char32, int> convert;
                string range;
                string prefix;
                int shift;
                TokenKind tokenKind;
                if (CurrentCharacter == 'x' || CurrentCharacter == 'X')
                {
                    name = "hexadecimal";
                    range = "[0-9a-zA-Z]";
                    prefix = "0x";
                    match = CharHelper.IsHexFunc;
                    convert = CharHelper.HexToDecFunc;
                    shift = 4;
                    tokenKind = TokenKind.IntegerHexa;
                    if (CurrentCharacter == 'X')
                    {
                        AddError($"Invalid capital X for hexadecimal. Use `x` instead.", CurrentPosition, CurrentPosition);
                    }
                }
                else if (CurrentCharacter == 'o' || CurrentCharacter == 'O')
                {
                    name = "octal";
                    range = "[0-7]";
                    prefix = "0o";
                    match = CharHelper.IsOctalFunc;
                    convert = CharHelper.OctalToDecFunc;
                    shift = 3;
                    tokenKind = TokenKind.IntegerOctal;
                    if (CurrentCharacter == 'O')
                    {
                        AddError($"Invalid capital O for octal. Use `o` instead.", CurrentPosition, CurrentPosition);
                    }
                }
                else
                {
                    name = "binary";
                    range = "0 or 1";
                    prefix = "0b";
                    match = CharHelper.IsBinaryFunc;
                    convert = CharHelper.BinaryToDecFunc;
                    shift = 1;
                    tokenKind = TokenKind.IntegerBinary;
                    if (CurrentCharacter == 'B')
                    {
                        AddError($"Invalid capital B for binary. Use `b` instead.", CurrentPosition, CurrentPosition);
                    }
                }

                end = CurrentPosition;
                NextChar(); // skip x,X,o,O,b,B

                bool hasCharInRange = false;
                bool lastWasDigit = false;
                bool isOutOfRange = false;
                ulong value = 0;
                while (true)
                {
                    bool hasLocalCharInRange = false;
                    if (CurrentCharacter == '_' || (hasLocalCharInRange = match(CurrentCharacter)))
                    {
                        var nextIsDigit = CurrentCharacter != '_';
                        if (!lastWasDigit && !nextIsDigit)
                        {
                            // toml-specs: each underscore must be surrounded by at least one digit on each side.
                            AddError($"An underscore must be surrounded by at least one {name} digit on each side", start, start);
                        }
                        else if (nextIsDigit)
                        {
                            // toml-specs: 64 bit (signed long) range expected (−9,223,372,036,854,775,808 to 9,223,372,036,854,775,807).
                            if (value > ((ulong)long.MaxValue >> shift))
                            {
                                if (!isOutOfRange)
                                {
                                    AddError($"The {name} integer is greater than the maximum 64-bit signed integer ({long.MaxValue})", start, start);
                                }

                                isOutOfRange = true;
                            }

                            // Leading zeros are allowed, so only the value is limited, not the number of digits
                            value = (value << shift) + (ulong)convert(CurrentCharacter);
                        }

                        lastWasDigit = nextIsDigit;

                        if (hasLocalCharInRange)
                        {
                            hasCharInRange = true;
                        }
                        end = CurrentPosition;
                        NextChar();
                    }
                    else
                    {
                        break;
                    }
                }

                if (!hasCharInRange)
                {
                    AddError($"Invalid {name} integer. Expecting at least one {range} after {prefix}", start, start);
                    _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
                }
                else if (!lastWasDigit)
                {
                    AddError($"Invalid {name} integer. Expecting a {range} after the last character", start, start);
                    _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
                }
                else if (isOutOfRange)
                {
                    _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
                }
                else
                {
                    _token = new SyntaxTokenValue(tokenKind, start, end, stringValue: null, data: value);
                }
                return;
            }
            else
            {
                // Append the leading 0
                _textBuilder.Append('0');
            }
        }

        // Parse leading digits
        var beforeFollowingZero = CurrentPosition;
        bool hasMultipleLeadingZero = false;

        // Skip leading zeros
        var previousCharIsDigit = false;
        var hasUnderscore = false;
        if (hasLeadingZero)
        {
            int zeroDigit = 0;
            previousCharIsDigit = true;
            while (CurrentCharacter == '0' || CurrentCharacter == '_')
            {
                previousCharIsDigit = CurrentCharacter == '0';
                hasUnderscore |= !previousCharIsDigit;
                if (previousCharIsDigit)
                {
                    _textBuilder.Append((char)CurrentCharacter);
                    zeroDigit++;
                }
                end = CurrentPosition;
                NextChar();
            }

            hasMultipleLeadingZero = zeroDigit > 0;
        }

        hasUnderscore |= ReadDigits(ref end, previousCharIsDigit);

        // We are in the case of a date
        if (CurrentCharacter == '-' || CurrentCharacter == ':')
        {
            if (hasUnderscore)
            {
                AddError("An underscore `_` is not allowed in a date or a time", start, CurrentPosition);
            }

            // Offset Date-Time
            // odt1 = 1979-05-27T07:32:00Z
            // odt2 = 1979-05-27T00:32:00-07:00
            // odt3 = 1979-05-27T00:32:00.999999-07:00
            //
            // For the sake of readability, you may replace the T delimiter between date and time with a space (as permitted by RFC 3339 section 5.6).
            //  NOTE: ISO 8601 defines date and time separated by "T".
            //      Applications using this syntax may choose, for the sake of
            //      readability, to specify a full-date and full-time separated by
            //      (say) a space character.
            // odt4 = 1979-05-27 07:32:00Z
            //
            // Local Date-Time
            //
            // ldt1 = 1979-05-27T07:32:00
            //
            // Local Date
            //
            // ld1 = 1979-05-27
            //
            // Local Time
            //
            // lt1 = 07:32:00
            // lt2 = 00:32:00.999999

            // Parse the date/time
            while (CharHelper.IsDateTime(CurrentCharacter))
            {
                _textBuilder.AppendUtf32(CurrentCharacter);
                end = CurrentPosition;
                NextChar();
            }

            // If we have a space, followed by a digit, try to parse the following
            if (CharHelper.IsWhiteSpace(CurrentCharacter) && CharHelper.IsDateTime(PeekChar()))
            {
                _textBuilder.AppendUtf32(CurrentCharacter); // Append the space
                NextChar(); // skip the space
                while (CharHelper.IsDateTime(CurrentCharacter))
                {
                    _textBuilder.AppendUtf32(CurrentCharacter);
                    end = CurrentPosition;
                    NextChar();
                }
            }

            var dateTimeAsString = _textBuilder.ToString();

            if (hasLeadingSign)
            {
                AddError($"Invalid prefix `{signPrefix!.Value}` for the following offset/local date/time `{dateTimeAsString.ToPrintableInputText()}`", start, end);
                // Still try to recover
                dateTimeAsString = dateTimeAsString.Substring(1);
            }

            if (DateTimeRFC3339.TryParseOffsetDateTime(dateTimeAsString, out var datetime))
            {
                var tokenKind = datetime.Kind == TomlDateTimeKind.OffsetDateTimeByZ
                    ? TokenKind.OffsetDateTimeByZ
                    : TokenKind.OffsetDateTimeByNumber;
                _token = new SyntaxTokenValue(tokenKind, start, end, stringValue: dateTimeAsString);
            }
            else if (DateTimeRFC3339.TryParseLocalDateTime(dateTimeAsString, out _))
            {
                _token = new SyntaxTokenValue(TokenKind.LocalDateTime, start, end, stringValue: dateTimeAsString);
            }
            else if (DateTimeRFC3339.TryParseLocalDate(dateTimeAsString, out _))
            {
                _token = new SyntaxTokenValue(TokenKind.LocalDate, start, end, stringValue: dateTimeAsString);
            }
            else if (DateTimeRFC3339.TryParseLocalTime(dateTimeAsString, out _))
            {
                _token = new SyntaxTokenValue(TokenKind.LocalTime, start, end, stringValue: dateTimeAsString);
            }
            else if (DateTimeRFC3339.TryGetUnsupportedReason(dateTimeAsString, out var unsupportedReason))
            {
                _token = new SyntaxTokenValue(TokenKind.LocalDateTime, start, end, stringValue: dateTimeAsString);
                AddError($"The date or time `{dateTimeAsString.ToPrintableInputText()}` is valid TOML but cannot be represented: {unsupportedReason}", start, end);
            }
            else
            {
                // Try to recover the date using the standard C# (not necessarily RFC3339)
                if (DateTime.TryParse(dateTimeAsString, CultureInfo.InvariantCulture, DateTimeStyles.AllowInnerWhite, out _))
                {
                    _token = new SyntaxTokenValue(TokenKind.LocalDateTime, start, end, stringValue: dateTimeAsString);

                    // But we produce an error anyway
                    AddError($"Invalid format of date time/offset `{dateTimeAsString.ToPrintableInputText()}` not following RFC3339", start, end);
                }
                else
                {
                    _token = new SyntaxTokenValue(TokenKind.LocalDateTime, start, end, stringValue: dateTimeAsString);
                    // But we produce an error anyway
                    AddError($"Unable to parse the date time/offset `{dateTimeAsString.ToPrintableInputText()}`", start, end);
                }
            }

            return;
        }

        if (hasMultipleLeadingZero)
        {
            AddError("Multiple leading 0 are not allowed", beforeFollowingZero, beforeFollowingZero);
        }

        // Read any number following
        if (CurrentCharacter == '.')
        {
            _textBuilder.Append('.');
            end = CurrentPosition;
            NextChar(); // Skip the dot .

            // We expect at least a digit after .
            if (!CharHelper.IsDigit(CurrentCharacter))
            {
                AddError("Expecting at least one digit after the float dot .", CurrentPosition, CurrentPosition);
                _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
                return;
            }

            isFloat = true;
            ReadDigits(ref end, false);
        }

        // Parse only the exponent if we don't have a range
        if (CurrentCharacter == 'e' || CurrentCharacter == 'E')
        {
            isFloat = true;

            _textBuilder.AppendUtf32(CurrentCharacter);
            end = CurrentPosition;
            NextChar();
            if (CurrentCharacter == '+' || CurrentCharacter == '-')
            {
                _textBuilder.AppendUtf32(CurrentCharacter);
                end = CurrentPosition;
                NextChar();
            }

            if (!CharHelper.IsDigit(CurrentCharacter))
            {
                AddError("Expecting at least one digit after the exponent", CurrentPosition, CurrentPosition);
                _token = new SyntaxTokenValue(TokenKind.Invalid, start, end);
                return;
            }
            ReadDigits(ref end, false);
        }

        if (isFloat)
        {
            if (!TryParseDecimalDouble(out var doubleValue))
            {
                var numberAsText = _textBuilder.ToString();
                AddError($"Unable to parse floating point `{numberAsText.ToPrintableInputText()}`", start, end);
                doubleValue = 0.0;
            }
            else if (double.IsInfinity(doubleValue))
            {
                // A literal can only be infinite by overflowing, as `inf` is a keyword
                var numberAsText = _textBuilder.ToString();
                AddError($"The float `{numberAsText.ToPrintableInputText()}` is outside the range of a 64-bit floating-point number", start, end);
                doubleValue = 0.0;
            }

            if (hasLeadingZero && HasMultipleDigitsInIntegerPart())
            {
                var numberAsText = _textBuilder.ToString();
                AddError($"Unexpected leading zero (`0`) for float `{numberAsText.ToPrintableInputText()}`", positionFirstDigit, positionFirstDigit);
            }

            var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(doubleValue));
            _token = new SyntaxTokenValue(TokenKind.Float, start, end, stringValue: null, data: bits);
        }
        else
        {
            if (!TryParseDecimalInt64(out var longValue))
            {
                var numberAsText = _textBuilder.ToString();
                AddError($"Unable to parse integer `{numberAsText.ToPrintableInputText()}`", start, end);
                longValue = 0;
            }

            if (hasLeadingZero && longValue != 0)
            {
                var numberAsText = _textBuilder.ToString();
                AddError($"Unexpected leading zero (`0`) for integer `{numberAsText.ToPrintableInputText()}`", positionFirstDigit, positionFirstDigit);
            }

            _token = new SyntaxTokenValue(TokenKind.Integer, start, end, stringValue: null, data: unchecked((ulong)longValue));
        }
    }

    private bool TryParseDecimalInt64(out long value)
    {
        value = 0;
        var length = _textBuilder.Length;
        if (length == 0)
        {
            return false;
        }

        var index = 0;
        var negative = false;
        var first = _textBuilder[0];
        if (first == '+' || first == '-')
        {
            negative = first == '-';
            index = 1;
            if (index >= length)
            {
                return false;
            }
        }

        ulong accumulator = 0;
        for (; index < length; index++)
        {
            var digitChar = _textBuilder[index];
            var digit = digitChar - '0';
            if ((uint)digit > 9)
            {
                return false;
            }

            var digitValue = (ulong)digit;
            if (accumulator > (ulong.MaxValue - digitValue) / 10)
            {
                return false;
            }

            accumulator = (accumulator * 10) + digitValue;
        }

        if (negative)
        {
            if (accumulator == 0x8000_0000_0000_0000UL)
            {
                value = long.MinValue;
                return true;
            }

            if (accumulator > 0x8000_0000_0000_0000UL)
            {
                return false;
            }

            value = unchecked(-(long)accumulator);
            return true;
        }

        // A positive magnitude above long.MaxValue does not fit a signed 64-bit
        // integer; the negative branch already rejects its counterpart. Without
        // this, values in [2^63, 2^64-1] wrap to a negative long (TOML requires
        // an error when an integer cannot be represented losslessly).
        if (accumulator > long.MaxValue)
        {
            return false;
        }

        value = unchecked((long)accumulator);
        return true;
    }

    private bool TryParseDecimalDouble(out double value)
    {
        value = 0.0;
        var length = _textBuilder.Length;
        if (length == 0)
        {
            return false;
        }

        char[]? rented = null;
        try
        {
            Span<char> buffer = length <= 128
                ? stackalloc char[length]
                : (rented = ArrayPool<char>.Shared.Rent(length)).AsSpan(0, length);

            _textBuilder.CopyTo(0, buffer, length);
            return double.TryParse(buffer, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private bool HasMultipleDigitsInIntegerPart()
    {
        var length = _textBuilder.Length;
        if (length <= 1)
        {
            return false;
        }

        var index = 0;
        var first = _textBuilder[0];
        if (first == '+' || first == '-')
        {
            index = 1;
            if (index >= length)
            {
                return false;
            }
        }

        var end = index;
        while (end < length)
        {
            var c = _textBuilder[end];
            if (c == '.' || c == 'e' || c == 'E')
            {
                break;
            }

            end++;
        }

        return (end - index) > 1;
    }

    /// <returns><see langword="true"/> when the digits contain an underscore.</returns>
    private bool ReadDigits(ref TextPosition end, bool isPreviousDigit)
    {
        var hasUnderscore = false;
        while (true)
        {
            var c = CurrentCharacter;
            var isDigit = CharHelper.IsDigit(c);
            if (!isDigit && c != '_')
            {
                break;
            }

            if (isDigit)
            {
                _textBuilder.AppendUtf32(c);
                isPreviousDigit = true;
            }
            else if (!isPreviousDigit)
            {
                hasUnderscore = true;
                AddError("An underscore `_` must follow a digit and not another `_`", CurrentPosition, CurrentPosition);
            }
            else
            {
                hasUnderscore = true;
                isPreviousDigit = false;
            }
            end = CurrentPosition;
            NextChar();
        }

        if (!isPreviousDigit)
        {
            AddError("Missing a digit after a trailing underscore `_`", CurrentPosition, CurrentPosition);
        }

        return hasUnderscore;
    }

    private void ReadString(TextPosition start, bool allowMultiline, bool materializeSimpleValue)
    {
        var end = CurrentPosition;
        var decodeScalars = DecodeScalars;
        bool isMultiLine = false;

        NextChar(); // Skip "
        var c = CurrentCharacter;
        if (allowMultiline && c == '"')
        {
            end = CurrentPosition;
            NextChar();

            c = CurrentCharacter;
            if (c == '"')
            {
                end = CurrentPosition;
                NextChar();
                // we have an opening ''' -> this a multi-line string
                isMultiLine = true;
                SkipImmediateNextLine();
            }
            else
            {
                // Else this is an empty string
                _token = new SyntaxTokenValue(TokenKind.String, start, end, decodeScalars || materializeSimpleValue ? string.Empty : null);
                return;
            }
        }

        // Reset the current string buffer only when we might append to it.
        if (decodeScalars)
        {
            _textBuilder.Length = 0;
        }

        if (!isMultiLine && TryReadStringFastPath(start, ref end, decodeScalars, materializeSimpleValue))
        {
            // The fast path only applies when we can jump directly to the closing quote,
            // which means the string did not contain any escape sequences.
            return;
        }

        continue_parsing_string:
        while (true)
        {
            c = CurrentCharacter;
            if (c == '"' || c == Eof)
            {
                break;
            }

            c = CurrentCharacter;
            if (c == '\r' && PeekChar() != '\n')
            {
                AddError($"Invalid \\r not followed by \\n", CurrentPosition, CurrentPosition);
            }

            if (c != '\\' || !TryReadEscapeChar(ref end, isMultiLine))
            {
                // An escape cut by the end of the file is already reported; the loop reports the unterminated string
                c = CurrentCharacter;
                if (c == Eof)
                {
                    continue;
                }

                if (!isMultiLine && CharHelper.IsNewLine(c))
                {
                    AddError("Invalid newline in a string", CurrentPosition, CurrentPosition);
                }
                else if (CharHelper.IsControlCharacter(c) && c != '\t' && (!isMultiLine || !CharHelper.IsWhiteSpaceOrNewLine(c)))
                {
                    AddError($"Invalid control character found {((char)c).ToPrintableString()}", CurrentPosition, CurrentPosition);
                }

                if (decodeScalars)
                {
                    _textBuilder.AppendUtf32(c);
                }
                end = CurrentPosition;
                NextChar();
            }
        }

        if (isMultiLine)
        {
            if (CurrentCharacter == '"')
            {
                int count = 0;
                while (true)
                {
                    c = CurrentCharacter;
                    if (c != '"' || count >= 5)
                    {
                        break;
                    }

                    count++;
                    end = CurrentPosition;
                    NextChar();
                }

                if (count >= 3)
                {
                    for (int i = 0; i < count - 3; i++)
                    {
                        if (decodeScalars)
                        {
                            _textBuilder.Append('"');
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (decodeScalars)
                        {
                            _textBuilder.Append('"');
                        }
                    }
                    goto continue_parsing_string;
                }
            }
            else
            {
                // The string takes the rest of the file, including a trailing newline or backslash that did not move the end
                end = GetEndOfFilePosition(end);
                AddError("Invalid End-Of-File found for multi-line string", end, end);
            }
            _token = new SyntaxTokenValue(TokenKind.StringMulti, start, end, decodeScalars ? _textBuilder.ToString() : null);
        }
        else
        {
            if (CurrentCharacter == '"')
            {
                end = CurrentPosition;
                NextChar();
            }
            else
            {
                // The string takes the rest of the file, including a trailing newline or backslash that did not move the end
                end = GetEndOfFilePosition(end);
                AddError("Invalid End-Of-File found on string literal", end, end);
            }
            _token = new SyntaxTokenValue(TokenKind.String, start, end, decodeScalars ? _textBuilder.ToString() : null);
        }
    }

    private bool TryReadStringFastPath(TextPosition start, ref TextPosition end, bool decodeScalars, bool materializeSimpleValue)
    {
        // Hot path: basic strings without escapes/newlines.
        // We avoid per-character NextChar() updates by scanning the underlying text and jumping directly
        // to the closing quote, then consuming it once.
        //
        // This method is intentionally conservative: it only applies when the substring does not contain:
        // - escape sequences ('\\')
        // - newlines
        // - control characters (except '\t', to preserve existing behavior)
        // - surrogate code units (to preserve column tracking based on scalar values)
        var contentStart = CurrentPosition;
        var startOffset = contentStart.Offset;
        if ((uint)startOffset >= (uint)_textLength)
        {
            return false;
        }

        var text = _text.Span;
        var remaining = text.Slice(startOffset, _textLength - startOffset);
        if (remaining.IsEmpty)
        {
            return false;
        }

        // Scan once until the closing quote, validating that the string has no escapes/newlines/control chars.
        ref var startRef = ref unsafe(MemoryMarshal.GetReference(remaining));
        var index = 0;
        while ((uint)index < (uint)remaining.Length)
        {
            var ch = unsafe(Unsafe.Add(ref startRef, index));
            if (ch == '\"')
            {
                break;
            }

            if (ch == '\\' || ch == '\n' || ch == '\r')
            {
                return false;
            }

            // Fast surrogate-range check: (0xD800..0xDFFF)
            if ((uint)(ch - 0xD800) <= 0x7FFu)
            {
                return false;
            }

            if ((ch < ' ' && ch != '\t') || ch == '\u007F')
            {
                return false;
            }

            index++;
        }

        if ((uint)index >= (uint)remaining.Length)
        {
            return false;
        }

        var rawContent = remaining.Slice(0, index);

        var quoteOffset = startOffset + index;
        var quotePosition = new TextPosition(quoteOffset, contentStart.Line, contentStart.Column + index);
        end = quotePosition;

        // Jump to the closing quote and consume it (one NextChar()).
        _current.Position = quotePosition;
        _current.NextPosition = quotePosition;
        _current.CurrentChar = NextCharFromReader();
        if (CurrentCharacter != '\"')
        {
            return false;
        }

        NextChar();
        var value = decodeScalars || materializeSimpleValue ? rawContent.ToString() : null;
        _token = new SyntaxTokenValue(TokenKind.String, start, end, value);
        return true;
    }

    private void SkipImmediateNextLine()
    {
        // Skip any white spaces until the next line
        if (CurrentCharacter == '\r')
        {
            var start = CurrentPosition;
            NextChar();
            if (CurrentCharacter == '\n')
            {
                NextChar();
            }
            else
            {
                AddError($"Invalid \\r not followed by \\n", start, start);
            }
        }
        else if (CurrentCharacter == '\n')
        {
            NextChar();
        }
    }

    private bool TryReadEscapeChar(ref TextPosition end, bool isMultiLine)
    {
        var decodeScalars = DecodeScalars;
        end = CurrentPosition;
        NextChar();
        // 0 \ ' " a b f n r t v u0000-uFFFF x00-xFF
        var c = CurrentCharacter;
        switch (c)
        {
            case 'b':
                if (decodeScalars) _textBuilder.Append('\b');
                end = CurrentPosition;
                NextChar();
                return true;
            case 't':
                if (decodeScalars) _textBuilder.Append('\t');
                end = CurrentPosition;
                NextChar();
                return true;
            case 'n':
                if (decodeScalars) _textBuilder.Append('\n');
                end = CurrentPosition;
                NextChar();
                return true;
            case 'f':
                if (decodeScalars) _textBuilder.Append('\f');
                end = CurrentPosition;
                NextChar();
                return true;
            case 'r':
                if (decodeScalars) _textBuilder.Append('\r');
                end = CurrentPosition;
                NextChar();
                return true;
            case '"':
                if (decodeScalars) _textBuilder.Append('"');
                end = CurrentPosition;
                NextChar();
                return true;
            case '\\':
                if (decodeScalars) _textBuilder.Append('\\');
                end = CurrentPosition;
                NextChar();
                return true;
            case 'e':
                if (decodeScalars) _textBuilder.Append('\u001B');
                end = CurrentPosition;
                NextChar();
                return true;
            case 'x':
            {
                var start = CurrentPosition;
                end = CurrentPosition;
                NextChar();

                // The escape is reported, and the character that ends it, such as the closing quote, is read normally
                c = CurrentCharacter;
                if (!CharHelper.IsHexFunc(c))
                {
                    AddError("Invalid escape `\\x`. Expected 2 hexadecimal digits.", start, start);
                    return true;
                }

                var value = CharHelper.HexToDecimal(c);
                end = CurrentPosition;
                NextChar();

                c = CurrentCharacter;
                if (!CharHelper.IsHexFunc(c))
                {
                    AddError("Invalid escape `\\x`. Expected 2 hexadecimal digits.", start, start);
                    return true;
                }

                value = (value << 4) + CharHelper.HexToDecimal(c);
                end = CurrentPosition;
                NextChar();

                if (decodeScalars) _textBuilder.Append((char)value);
                return true;
            }

            // toml-specs: mlb-escaped-nl = escape ws newline *( wschar / newline )
            // When the last non-whitespace character on a line is a \, it will be trimmed along with all whitespace
            // (including newlines) up to the next non-whitespace character or closing delimiter. This is only allowed
            // in multi-line basic strings.
            case ' ':
            case '\t':
            case '\r':
            case '\n':
            {
                var escapePosition = end;
                while (c == ' ' || c == '\t')
                {
                    end = CurrentPosition;
                    NextChar();
                    c = CurrentCharacter;
                }

                if (c == '\r' && PeekChar() == '\n')
                {
                    end = CurrentPosition;
                    NextChar();
                    c = CurrentCharacter;
                }

                if (!isMultiLine)
                {
                    AddError("Invalid escape `\\`. A line ending backslash is only allowed in multi-line strings.", escapePosition, escapePosition);
                }
                else if (c != '\n')
                {
                    AddError("Invalid escape `\\`. It must be followed by a newline.", escapePosition, escapePosition);
                }

                while (true)
                {
                    c = CurrentCharacter;
                    if (!CharHelper.IsWhiteSpaceOrNewLine(c))
                    {
                        break;
                    }

                    if (c == '\r' && PeekChar() != '\n')
                    {
                        AddError("Invalid \\r not followed by \\n", CurrentPosition, CurrentPosition);
                    }

                    end = CurrentPosition;
                    NextChar();
                }

                return true;
            }

            case 'u':
            case 'U':
            {
                var start = CurrentPosition;
                end = CurrentPosition;
                var maxCount = c == 'u' ? 4 : 8;
                NextChar();

                // Must be followed 0 to 8 hex numbers (0-FFFFFFFF)
                int i = 0;
                int value = 0;
                while (i < maxCount)
                {
                    c = CurrentCharacter;
                    if (!CharHelper.IsHexFunc(c))
                    {
                        break;
                    }

                    value = (value << 4) + CharHelper.HexToDecimal(c);
                    end = CurrentPosition;
                    NextChar();
                    i++;
                }

                if (i == maxCount)
                {
                    if (!CharHelper.IsValidUnicodeScalarValue(value))
                    {
                        AddError($"Invalid Unicode scalar value [{value:X}]",start, start);
                    }
                    if (decodeScalars) _textBuilder.AppendUtf32((Char32)value);
                    return true;
                }

                // The escape is reported, and the character that ends it, such as the closing quote, is read normally
                if (CurrentCharacter != Eof)
                {
                    AddError($"Invalid escape `\\{(maxCount == 4 ? 'u' : 'U')}`. Expected {maxCount} hexadecimal digits.", start, start);
                    return true;
                }
            }
                break;
        }

        c = CurrentCharacter;
        if (c == Eof)
        {
            AddError("Unexpected end of file in an escape sequence", CurrentPosition, CurrentPosition);
            return false;
        }

        AddError($"Unexpected escape character [{c}] in string. Only b t n f r e \\ \" xHH u0000-uFFFF U00000000-UFFFFFFFF are allowed", CurrentPosition, CurrentPosition);
        return false;
    }

    private void ReadStringLiteral(TextPosition start, bool allowMultiline)
    {
        var end = CurrentPosition;
        var decodeScalars = DecodeScalars;

        bool isMultiLine = false;

        NextChar(); // Skip '
        var c = CurrentCharacter;
        if (allowMultiline && c == '\'')
        {
            end = CurrentPosition;
            NextChar();

            c = CurrentCharacter;
            if (c == '\'')
            {
                end = CurrentPosition;
                NextChar();
                // we have an opening ''' -> this a multi-line literal string
                isMultiLine = true;

                SkipImmediateNextLine();
            }
            else
            {
                // Else this is an empty literal string
                _token = new SyntaxTokenValue(TokenKind.StringLiteral, start, end, decodeScalars ? string.Empty : null);
                return;
            }
        }

        _textBuilder.Length = 0;
        continue_parsing_string:
        while (true)
        {
            c = CurrentCharacter;
            if (c == '\'' || c == Eof)
            {
                break;
            }

            if (c == '\r' && PeekChar() != '\n')
            {
                AddError($"Invalid \\r not followed by \\n", CurrentPosition, CurrentPosition);
            }

            if (!isMultiLine && CharHelper.IsNewLine(c))
            {
                AddError("Invalid newline in a string", CurrentPosition, CurrentPosition);
            }
            else if (CharHelper.IsControlCharacter(c) && c != '\t' && (!isMultiLine || !CharHelper.IsNewLine(c)))
            {
                AddError($"Invalid control character found {((char)c).ToPrintableString()}", CurrentPosition, CurrentPosition);
            }
            if (decodeScalars)
            {
                _textBuilder.AppendUtf32(c);
            }
            end = CurrentPosition;
            NextChar();
        }

        if (isMultiLine)
        {
            if (CurrentCharacter == '\'')
            {
                int count = 0;
                while (true)
                {
                    c = CurrentCharacter;
                    if (c != '\'' || count >= 5)
                    {
                        break;
                    }

                    count++;
                    end = CurrentPosition;
                    NextChar();
                }

                if (count >= 3)
                {
                    for (int i = 0; i < count - 3; i++)
                    {
                        if (decodeScalars)
                        {
                            _textBuilder.Append('\'');
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (decodeScalars)
                        {
                            _textBuilder.Append('\'');
                        }
                    }
                    goto continue_parsing_string;
                }

            }
            else
            {
                // The string takes the rest of the file, including a trailing newline or backslash that did not move the end
                end = GetEndOfFilePosition(end);
                AddError("Invalid End-Of-File found for multi-line literal string", end, end);
            }
            _token = new SyntaxTokenValue(TokenKind.StringLiteralMulti, start, end, decodeScalars ? _textBuilder.ToString() : null);
        }
        else
        {
            if (CurrentCharacter == '\'')
            {
                end = CurrentPosition;
                NextChar();
            }
            else
            {
                // The string takes the rest of the file, including a trailing newline or backslash that did not move the end
                end = GetEndOfFilePosition(end);
                AddError("Invalid End-Of-File found on string literal", end, end);
            }
            _token = new SyntaxTokenValue(TokenKind.StringLiteral, start, end, decodeScalars ? _textBuilder.ToString() : null);
        }
    }


    private TextPosition ReadCommentCore(TextPosition start)
    {
        var end = start;
        // Read until the end of the line/file
        while (true)
        {
            var c = CurrentCharacter;
            if (c == Eof || c == '\r' || c == '\n')
            {
                break;
            }

            // Invalid characters for comment
            // U+0000 to U+0008, U+000A to U+001F, U+007F
            if (c >= 0 && c <= 8 || c >= 0xa && c <= 0x1f || c == 0x7f)
            {
                AddError($"Invalid control character U+{c.Code:X4} in comment", CurrentPosition, CurrentPosition);
            }
            // CurrentPosition.Offset points at the start of the current UTF-32 scalar; use the lexer
            // internal next offset so end spans include the full UTF-8/UTF-16 sequence.
            end = new TextPosition(_current.NextPosition.Offset - 1, CurrentPosition.Line, CurrentPosition.Column);
            NextChar();
        }

        if (CurrentCharacter == '\r' && PeekChar() != '\n')
        {
            AddError($"Invalid control character U+{CurrentCharacter.Code:X4} in comment", CurrentPosition, CurrentPosition);
        }

        return end;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextChar()
    {
        _current.Position = _current.NextPosition;
        _current.CurrentChar = NextCharFromReader();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Char32 PeekChar()
    {
        int position = _current.NextPosition.Offset;
        if ((uint)position >= (uint)_textLength)
        {
            return Eof;
        }

        return ReadChar32(ref position, out _);
    }

    // Returns U+FFFD for an unpaired surrogate, and reports it with isInvalidSurrogate: a literal U+FFFD is valid
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Char32 ReadChar32(ref int position, out bool isInvalidSurrogate)
    {
        isInvalidSurrogate = false;
        var span = _text.Span;
        ref var start = ref unsafe(MemoryMarshal.GetReference(span));
        var c1 = unsafe(Unsafe.Add(ref start, position));
        position++;

        // Fast surrogate checks (avoid char.IsHighSurrogate/IsLowSurrogate)
        if (((uint)c1 & 0xFC00u) == 0xD800u)
        {
            if ((uint)position < (uint)_textLength)
            {
                // The next code unit is only part of this character when it completes the pair
                var c2 = unsafe(Unsafe.Add(ref start, position));
                if (((uint)c2 & 0xFC00u) == 0xDC00u)
                {
                    position++;
                    return char.ConvertToUtf32(c1, c2);
                }

                isInvalidSurrogate = true;
                return 0xFFFD;
            }

            position = _textLength;
            isInvalidSurrogate = true;
            return 0xFFFD;
        }

        if (((uint)c1 & 0xFC00u) == 0xDC00u)
        {
            isInvalidSurrogate = true;
            return 0xFFFD;
        }

        return c1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Char32 NextCharFromReaderCore()
    {
        ref readonly var currentPosition = ref _current.Position;
        ref var nextPosition = ref _current.NextPosition;

        int position = currentPosition.Offset;
        if ((uint)position >= (uint)_textLength)
        {
            nextPosition.Offset = _textLength;
            return Eof;
        }

        var nextChar = ReadChar32(ref position, out var isInvalidSurrogate);
        nextPosition.Offset = position;

        var nextc = nextChar;
        nextPosition.Line = currentPosition.Line;
        nextPosition.Column = currentPosition.Column;
        if (nextc == '\n')
        {
            nextPosition.Column = 0;
            nextPosition.Line++;
        }
        else
        {
            nextPosition.Column++;
        }

        if (isInvalidSurrogate)
        {
            AddError("Invalid UTF-16 surrogate sequence in TOML input.", _current.Position, _current.Position);
        }

        return nextc;
    }

    private Char32 NextCharFromReader() => NextCharFromReaderCore();

    // The position of the last character of the text, when a token ends at the end of the file. Its line and column are the
    // ones of that character, as for any other position, so they match its offset.
    private TextPosition GetEndOfFilePosition(TextPosition end)
    {
        if (_textLength == 0)
        {
            return end;
        }

        var span = _text.Span;
        var offset = _textLength - 1;
        if (offset > 0 && char.IsLowSurrogate(span[offset]) && char.IsHighSurrogate(span[offset - 1]))
        {
            offset--;
        }

        // The end of the file is just after the last character: after a newline, it is at the start of the next line
        var endOfFile = CurrentPosition;
        if (span[offset] != '\n')
        {
            return new TextPosition(offset, endOfFile.Line, endOfFile.Column - 1);
        }

        var lineStart = span[..offset].LastIndexOf('\n') + 1;
        var column = 0;
        for (var i = lineStart; i < offset; i++)
        {
            if (!(char.IsHighSurrogate(span[i]) && i + 1 < offset && char.IsLowSurrogate(span[i + 1])))
            {
                column++;
            }
        }

        return new TextPosition(offset, endOfFile.Line - 1, column);
    }

    private void AddError(string message, TextPosition start, TextPosition end)
    {
        _errors ??= new List<DiagnosticMessage>();

        // A run of the same invalid character, such as NUL characters in a comment, is reported once over the whole run
        if (_errors.Count > 0 && _errors[^1] is { } previous &&
            previous.Span.End.Offset + 1 == start.Offset &&
            string.Equals(previous.Message, message, StringComparison.Ordinal))
        {
            _errors[^1] = new DiagnosticMessage(previous.Kind, new SourceSpan(previous.Span.FileName, previous.Span.Start, end), message);
            return;
        }

        if (_errors.Count < MaxErrorCount)
        {
            _errors.Add(new DiagnosticMessage(DiagnosticMessageKind.Error, new SourceSpan(_sourcePath, start, end), message));
        }
    }

    private void Reset()
    {
        // Initialize the position at -1 when starting
        _current = new LexerInternalState {Position = new TextPosition(0, 0, 0)};
        // It is important to initialize this separately from the previous line
        _current.CurrentChar = NextCharFromReader();
        _token = new SyntaxTokenValue();
        _errors = null;
    }
}
