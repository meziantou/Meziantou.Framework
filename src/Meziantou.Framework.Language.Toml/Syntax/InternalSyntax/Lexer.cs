using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Toml.Internals;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Turns TOML text into tokens, keeping whitespace, line breaks, and comments as trivia.</summary>
/// <remarks>
/// Trivia is split the way it reads: whitespace and comments up to and including the end of a line belong to the
/// token that ends that line, and everything after belongs to the token that follows. That is what makes a comment on
/// its own line attach to the thing it describes rather than to the thing above it.
/// </remarks>
internal sealed class Lexer(SourceText source, TomlVersion version)
{
    private readonly string _text = source.Text;
    private List<SyntaxDiagnosticInfo>? _tokenDiagnostics;
    private int _tokenBodyStart;

    /// <summary>Gets or sets where the next token will be read from.</summary>
    public int Position { get; set; }

    public GreenToken Lex(LexerMode mode)
    {
        _tokenDiagnostics = null;

        var fullStart = Position;
        var leading = LexTrivia(isTrailing: false);
        _tokenBodyStart = Position;

        var kind = ScanTokenBody(mode, out var text, out var value, out var valueText);
        var trailing = LexTrivia(isTrailing: true);

        var token = kind switch
        {
            SyntaxKind.BadToken => SyntaxFactory.BadToken(leading, text, trailing),
            SyntaxKind.EndOfFileToken => SyntaxFactory.Token(leading, kind, text, trailing),
            _ when valueText is not null => SyntaxFactory.TokenWithValue(leading, kind, text, value, valueText, trailing),
            _ when SyntaxFacts.GetText(kind).Length > 0 => SyntaxFactory.Token(leading, kind, trailing),
            _ => SyntaxFactory.Token(leading, kind, text, trailing),
        };

        if (_tokenDiagnostics is not { Count: > 0 })
            return token;

        // Diagnostics are recorded against the text, and stored against the start of the token, trivia included.
        var leadingWidth = _tokenBodyStart - fullStart;
        var moved = _tokenDiagnostics.Select(info => info.WithOffset(info.Offset + leadingWidth)).ToArray();

        return (GreenToken)token.WithAdditionalDiagnostics(moved);
    }

    private SyntaxKind ScanTokenBody(LexerMode mode, out string text, out object? value, out string? valueText)
    {
        value = null;
        valueText = null;
        if (IsAtEnd)
        {
            text = "";
            return SyntaxKind.EndOfFileToken;
        }

        switch (Current)
        {
            case '[' when mode == LexerMode.Key && LookAhead == '[':
                return Punctuation(SyntaxKind.OpenBracketOpenBracketToken, out text);
            case ']' when mode == LexerMode.Key && LookAhead == ']':
                return Punctuation(SyntaxKind.CloseBracketCloseBracketToken, out text);
            case '[':
                return Punctuation(SyntaxKind.OpenBracketToken, out text);
            case ']':
                return Punctuation(SyntaxKind.CloseBracketToken, out text);
            case '{':
                return Punctuation(SyntaxKind.OpenBraceToken, out text);
            case '}':
                return Punctuation(SyntaxKind.CloseBraceToken, out text);
            case '=':
                return Punctuation(SyntaxKind.EqualsToken, out text);
            case ',':
                return Punctuation(SyntaxKind.CommaToken, out text);
            case '.' when mode == LexerMode.Key:
                return Punctuation(SyntaxKind.DotToken, out text);
            case '"':
                return ScanBasicString(out text, out value, out valueText);
            case '\'':
                return ScanLiteralString(out text, out value, out valueText);
            default:
                return mode == LexerMode.Key ? ScanBareKey(out text, out value, out valueText) : ScanBareValue(out text, out value, out valueText);
        }
    }

    private SyntaxKind Punctuation(SyntaxKind kind, out string text)
    {
        text = SyntaxFacts.GetText(kind);
        Position += text.Length;

        return kind;
    }

    /// <summary>Reads a bare key, and anything glued to it, as one token.</summary>
    /// <remarks>
    /// A character a bare key cannot hold does not end it: <c>a!b</c> is one mistake, reported once, rather than a key
    /// followed by stray text that would also be reported as a missing <c>=</c>.
    /// </remarks>
    private SyntaxKind ScanBareKey(out string text, out object? value, out string? valueText)
    {
        var start = ScanWord(IsKeyDelimiter);
        text = _text[start..Position];
        if (SyntaxFacts.IsBareKey(text))
        {
            value = text;
            valueText = text;
            return SyntaxKind.BareKeyToken;
        }

        value = null;
        valueText = null;
        AddDiagnostic(start, Position - start, TomlDiagnosticDescriptors.InvalidKey, text);
        return SyntaxKind.BadToken;
    }

    /// <summary>Reads a value written without quotes: a boolean, a number, a date, or a time.</summary>
    private SyntaxKind ScanBareValue(out string text, out object? value, out string? valueText)
    {
        value = null;
        valueText = null;
        var start = ScanWord(IsValueDelimiter);

        // A date and a time may be separated by a space, which otherwise ends a word.
        if (Position - start == 10 && Current == ' ' && IsDateShaped(start) && IsAsciiDigit(Position + 1) && IsAsciiDigit(Position + 2) && CharAt(Position + 3) == ':')
        {
            Position++;
            ScanWord(IsValueDelimiter);
        }

        text = _text[start..Position];
        switch (text)
        {
            case "true":
                return SyntaxKind.TrueKeyword;
            case "false":
                return SyntaxKind.FalseKeyword;
        }

        switch (ScalarParser.ParseDateTime(text, out var dateTimeKind, out var dateTime, out var omitsSeconds, out var reason))
        {
            case ScalarParser.Result.Success:
                if (omitsSeconds && version < TomlVersion.V1_1)
                {
                    AddDiagnostic(start, Position - start, TomlDiagnosticDescriptors.RequiresNewerVersion, "A time without seconds", "1.1");
                }

                value = dateTime;
                valueText = text;
                return dateTimeKind;
            case ScalarParser.Result.Invalid:
                return Bad(TomlDiagnosticDescriptors.InvalidDateTime, text);
            case ScalarParser.Result.Unsupported:
                return Bad(TomlDiagnosticDescriptors.UnsupportedDateTime, text, reason);
        }

        switch (ScalarParser.ParseInteger(text, out var integer))
        {
            case ScalarParser.Result.Success:
                value = integer;
                valueText = integer.ToString(CultureInfo.InvariantCulture);
                return SyntaxKind.IntegerToken;
            case ScalarParser.Result.Unsupported:
                return Bad(TomlDiagnosticDescriptors.IntegerOutOfRange, text);
        }

        switch (ScalarParser.ParseFloat(text, out var number))
        {
            case ScalarParser.Result.Success:
                value = number;
                valueText = number.ToString(CultureInfo.InvariantCulture);
                return SyntaxKind.FloatToken;
            case ScalarParser.Result.Unsupported:
                return Bad(TomlDiagnosticDescriptors.FloatOutOfRange, text);
        }

        return Bad(LooksNumeric(text) ? TomlDiagnosticDescriptors.InvalidNumber : TomlDiagnosticDescriptors.InvalidValue, text);

        SyntaxKind Bad(DiagnosticDescriptor descriptor, params object?[] arguments)
        {
            AddDiagnostic(start, Position - start, descriptor, arguments);
            return SyntaxKind.BadToken;
        }

        static bool LooksNumeric(string text) => text switch
        {
            [>= '0' and <= '9', ..] => true,
            ['+' or '-', >= '0' and <= '9' or '.' or 'i' or 'n', ..] => true,
            ['.', >= '0' and <= '9', ..] => true,
            _ => false,
        };
    }

    /// <summary>Consumes characters up to the next delimiter, and at least one, so that lexing always moves.</summary>
    private int ScanWord(Func<char, bool> isDelimiter)
    {
        var start = Position;
        while (!IsAtEnd && !isDelimiter(Current))
        {
            Position++;
        }

        if (Position == start)
        {
            Position++;
        }

        return start;
    }

    /// <summary>Reads a basic string, or a multi-line one when it opens with three quotes.</summary>
    /// <remarks>
    /// A single-line string ends at its closing quote or at the end of its line, whichever comes first. Stopping at the
    /// line keeps the lines below out of it: otherwise one missing quote would turn the rest of the document into a
    /// single string, and every quote after it would open or close the wrong one.
    /// </remarks>
    private SyntaxKind ScanBasicString(out string text, out object? value, out string? valueText)
    {
        var start = Position;
        var builder = new StringBuilder();
        var isMultiLine = LookAhead == '"' && CharAt(Position + 2) == '"';
        var terminated = false;
        if (isMultiLine)
        {
            Position += 3;
            SkipNewLine();
            while (!IsAtEnd)
            {
                var current = Current;
                if (current == '"' && LookAhead == '"' && CharAt(Position + 2) == '"')
                {
                    terminated = true;
                    ScanMultiLineClosingDelimiter('"', builder);
                    break;
                }

                if (current == '\\')
                {
                    if (TrySkipLineEndingBackslash())
                        continue;

                    ScanEscapeSequence(builder, isMultiLine: true);
                    continue;
                }

                ScanStringCharacter(builder, isMultiLine: true);
            }
        }
        else
        {
            Position++;
            while (!IsAtEnd)
            {
                var current = Current;
                if (current == '"')
                {
                    Position++;
                    terminated = true;
                    break;
                }

                if (current is '\r' or '\n')
                    break;

                if (current == '\\')
                {
                    ScanEscapeSequence(builder, isMultiLine: false);
                    continue;
                }

                ScanStringCharacter(builder, isMultiLine: false);
            }
        }

        if (!terminated)
        {
            AddDiagnostic(start, Position - start, TomlDiagnosticDescriptors.UnterminatedString);
        }

        text = _text[start..Position];
        valueText = builder.ToString();
        value = valueText;

        return isMultiLine ? SyntaxKind.MultiLineBasicStringToken : SyntaxKind.BasicStringToken;
    }

    /// <summary>Reads a literal string, or a multi-line one when it opens with three single quotes.</summary>
    private SyntaxKind ScanLiteralString(out string text, out object? value, out string? valueText)
    {
        var start = Position;
        var builder = new StringBuilder();
        var isMultiLine = LookAhead == '\'' && CharAt(Position + 2) == '\'';
        var terminated = false;
        if (isMultiLine)
        {
            Position += 3;
            SkipNewLine();
            while (!IsAtEnd)
            {
                if (Current == '\'' && LookAhead == '\'' && CharAt(Position + 2) == '\'')
                {
                    terminated = true;
                    ScanMultiLineClosingDelimiter('\'', builder);
                    break;
                }

                ScanStringCharacter(builder, isMultiLine: true);
            }
        }
        else
        {
            Position++;
            while (!IsAtEnd)
            {
                var current = Current;
                if (current == '\'')
                {
                    Position++;
                    terminated = true;
                    break;
                }

                if (current is '\r' or '\n')
                    break;

                ScanStringCharacter(builder, isMultiLine: false);
            }
        }

        if (!terminated)
        {
            AddDiagnostic(start, Position - start, TomlDiagnosticDescriptors.UnterminatedString);
        }

        text = _text[start..Position];
        valueText = builder.ToString();
        value = valueText;

        return isMultiLine ? SyntaxKind.MultiLineLiteralStringToken : SyntaxKind.LiteralStringToken;
    }

    /// <summary>Skips the line break that may follow the opening delimiter of a multi-line string, which is not part of its value.</summary>
    private void SkipNewLine()
    {
        if (Current == '\n')
        {
            Position++;
        }
        else if (Current == '\r' && LookAhead == '\n')
        {
            Position += 2;
        }
    }

    /// <summary>Reads the closing delimiter of a multi-line string, and the one or two quotes that may come just before it.</summary>
    /// <remarks>
    /// <c>""""a""""</c> is <c>"a"</c>: the content may end with up to two quotes, so the delimiter is the last three
    /// of a run of up to five. A sixth quote is not part of the string.
    /// </remarks>
    private void ScanMultiLineClosingDelimiter(char quote, StringBuilder builder)
    {
        var count = 3;
        while (count < 5 && CharAt(Position + count) == quote)
        {
            count++;
        }

        builder.Append(quote, count - 3);
        Position += count;
    }

    /// <summary>Reads one character of a string, reporting the ones a string cannot contain unescaped.</summary>
    /// <remarks>Line breaks in a multi-line string are read as a line feed whatever the text uses.</remarks>
    private void ScanStringCharacter(StringBuilder builder, bool isMultiLine)
    {
        var current = Current;
        if (isMultiLine && current == '\n')
        {
            builder.Append('\n');
            Position++;
            return;
        }

        if (isMultiLine && current == '\r' && LookAhead == '\n')
        {
            builder.Append('\n');
            Position += 2;
            return;
        }

        if (char.IsHighSurrogate(current) && char.IsLowSurrogate(LookAhead))
        {
            builder.Append(current).Append(LookAhead);
            Position += 2;
            return;
        }

        if (IsControlCharacter(current) || char.IsSurrogate(current))
        {
            AddDiagnostic(Position, 1, current == '\r' ? TomlDiagnosticDescriptors.BareCarriageReturn : TomlDiagnosticDescriptors.InvalidCharacter, ToCodePoint(current));
        }

        builder.Append(current);
        Position++;
    }

    /// <summary>Skips a backslash that ends its line in a multi-line basic string, with all the whitespace and line breaks after it.</summary>
    private bool TrySkipLineEndingBackslash()
    {
        var next = Position + 1;
        while (CharAt(next) is ' ' or '\t')
        {
            next++;
        }

        if (!(CharAt(next) == '\n' || (CharAt(next) == '\r' && CharAt(next + 1) == '\n')))
            return false;

        Position = next;
        while (!IsAtEnd)
        {
            if (Current is ' ' or '\t' or '\n')
            {
                Position++;
            }
            else if (Current == '\r' && LookAhead == '\n')
            {
                Position += 2;
            }
            else
            {
                break;
            }
        }

        return true;
    }

    private void ScanEscapeSequence(StringBuilder builder, bool isMultiLine)
    {
        var escapeStart = Position;
        Position++;

        // A backslash that ends the text or a single-line string has nothing to escape.
        if (IsAtEnd || (!isMultiLine && Current is '\r' or '\n'))
        {
            AddDiagnostic(escapeStart, 1, TomlDiagnosticDescriptors.InvalidEscapeSequence, "\\");
            return;
        }

        var current = Current;
        switch (current)
        {
            case '"' or '\\':
                builder.Append(current);
                Position++;
                break;
            case 'b':
                builder.Append('\b');
                Position++;
                break;
            case 't':
                builder.Append('\t');
                Position++;
                break;
            case 'n':
                builder.Append('\n');
                Position++;
                break;
            case 'f':
                builder.Append('\f');
                Position++;
                break;
            case 'r':
                builder.Append('\r');
                Position++;
                break;
            case 'e':
                builder.Append('\u001B');
                Position++;
                ReportIfTooNew(escapeStart, "The escape sequence '\\e'");
                break;
            case 'x':
                ScanHexEscape(builder, escapeStart, digitCount: 2);
                ReportIfTooNew(escapeStart, "The escape sequence '\\xHH'");
                break;
            case 'u':
                ScanHexEscape(builder, escapeStart, digitCount: 4);
                break;
            case 'U':
                ScanHexEscape(builder, escapeStart, digitCount: 8);
                break;
            default:
                // The character after the backslash is kept, so the value is still close to what was meant.
                AddDiagnostic(escapeStart, 2, TomlDiagnosticDescriptors.InvalidEscapeSequence, _text.Substring(escapeStart, 2));
                ScanStringCharacter(builder, isMultiLine);
                break;
        }
    }

    /// <summary>Reads the hex digits of <c>\x</c>, <c>\u</c>, or <c>\U</c>, stopping at the first character that is not one.</summary>
    /// <remarks>Stopping there matters: that character may be the closing quote, which must still end the string.</remarks>
    private void ScanHexEscape(StringBuilder builder, int escapeStart, int digitCount)
    {
        Position++;

        var codePoint = 0L;
        for (var i = 0; i < digitCount; i++)
        {
            if (IsAtEnd || !char.IsAsciiHexDigit(Current))
            {
                AddDiagnostic(escapeStart, Position - escapeStart, TomlDiagnosticDescriptors.InvalidEscapeSequence, _text[escapeStart..Position]);
                return;
            }

            codePoint = (codePoint * 16) + (char.IsAsciiDigit(Current) ? Current - '0' : (Current | 0x20) - 'a' + 10);
            Position++;
        }

        if (codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
        {
            AddDiagnostic(escapeStart, Position - escapeStart, TomlDiagnosticDescriptors.InvalidUnicodeEscapeSequence, _text[escapeStart..Position]);
            return;
        }

        builder.Append(char.ConvertFromUtf32((int)codePoint));
    }

    private void ReportIfTooNew(int escapeStart, string feature)
    {
        if (version < TomlVersion.V1_1)
        {
            AddDiagnostic(escapeStart, Position - escapeStart, TomlDiagnosticDescriptors.RequiresNewerVersion, feature, "1.1");
        }
    }

    private GreenNode? LexTrivia(bool isTrailing)
    {
        List<GreenNode?>? trivia = null;
        while (!IsAtEnd)
        {
            var start = Position;
            if (Current is ' ' or '\t')
            {
                while (!IsAtEnd && Current is ' ' or '\t')
                {
                    Position++;
                }

                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            // A byte order mark is not part of the document it starts.
            if (Position == 0 && Current == '﻿')
            {
                Position++;
                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            // Anything else that looks like whitespace is kept as whitespace, so the tokens around it still read the
            // way they were meant to, but TOML only has two whitespace characters.
            if (IsNonTomlWhitespace(Current))
            {
                while (!IsAtEnd && IsNonTomlWhitespace(Current))
                {
                    Position++;
                }

                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start, new SyntaxDiagnosticInfo(0, Position - start, TomlDiagnosticDescriptors.InvalidWhitespace, [ToCodePoint(_text[start])]));
                continue;
            }

            if (Current is '\r' or '\n')
            {
                if (Current == '\r' && LookAhead != '\n')
                {
                    // A carriage return on its own is not a line break in TOML, but it ends a line for everyone reading
                    // the file, so it is kept as one and reported.
                    Position++;
                    Add(ref trivia, SyntaxKind.EndOfLineTrivia, start, new SyntaxDiagnosticInfo(0, 1, TomlDiagnosticDescriptors.BareCarriageReturn, []));
                }
                else
                {
                    Position += Current == '\r' ? 2 : 1;
                    Add(ref trivia, SyntaxKind.EndOfLineTrivia, start);
                }

                // The line is over, so whatever comes next belongs to the next token.
                if (isTrailing)
                    break;

                continue;
            }

            if (Current == '#')
            {
                List<SyntaxDiagnosticInfo>? diagnostics = null;
                Position++;
                while (!IsAtEnd && Current is not '\r' and not '\n')
                {
                    if (char.IsHighSurrogate(Current) && char.IsLowSurrogate(LookAhead))
                    {
                        Position += 2;
                        continue;
                    }

                    if (IsControlCharacter(Current) || char.IsSurrogate(Current))
                    {
                        (diagnostics ??= []).Add(new SyntaxDiagnosticInfo(Position - start, 1, TomlDiagnosticDescriptors.InvalidCharacter, [ToCodePoint(Current)]));
                    }

                    Position++;
                }

                Add(ref trivia, SyntaxKind.CommentTrivia, start, diagnostics?.ToArray() ?? []);
                continue;
            }

            break;
        }

        return trivia is null ? null : SyntaxFactory.List(trivia.ToArray());
    }

    private void Add(ref List<GreenNode?>? trivia, SyntaxKind kind, int start, params SyntaxDiagnosticInfo[] diagnostics)
    {
        GreenNode item = SyntaxFactory.Trivia(kind, _text[start..Position]);
        if (diagnostics.Length > 0)
        {
            item = item.WithAdditionalDiagnostics(diagnostics);
        }

        (trivia ??= []).Add(item);
    }

    private void AddDiagnostic(int start, int width, DiagnosticDescriptor descriptor, params object?[]? arguments)
        => (_tokenDiagnostics ??= []).Add(new SyntaxDiagnosticInfo(start - _tokenBodyStart, Math.Max(0, width), descriptor, arguments));

    private bool IsAtEnd => Position >= _text.Length;
    private char Current => CharAt(Position);
    private char LookAhead => CharAt(Position + 1);

    private char CharAt(int position) => position < _text.Length ? _text[position] : '\0';

    private bool IsAsciiDigit(int position) => position < _text.Length && char.IsAsciiDigit(_text[position]);

    private bool IsDateShaped(int start)
        => IsAsciiDigit(start) && IsAsciiDigit(start + 1) && IsAsciiDigit(start + 2) && IsAsciiDigit(start + 3) && CharAt(start + 4) == '-'
            && IsAsciiDigit(start + 5) && IsAsciiDigit(start + 6) && CharAt(start + 7) == '-' && IsAsciiDigit(start + 8) && IsAsciiDigit(start + 9);

    /// <summary>Gets whether a character ends a bare key.</summary>
    private static bool IsKeyDelimiter(char value)
        => value is ' ' or '\t' or '\r' or '\n' or '#' or '=' or '.' or '[' or ']' or '{' or '}' or ',' or '"' or '\'' || IsNonTomlWhitespace(value);

    /// <summary>Gets whether a character ends a value written without quotes.</summary>
    /// <remarks>Unlike in a key, a dot does not: it is part of a float.</remarks>
    private static bool IsValueDelimiter(char value)
        => value is ' ' or '\t' or '\r' or '\n' or '#' or '=' or '[' or ']' or '{' or '}' or ',' or '"' or '\'' || IsNonTomlWhitespace(value);

    private static bool IsNonTomlWhitespace(char value)
        => value is not (' ' or '\t' or '\r' or '\n') && (char.IsWhiteSpace(value) || value == '﻿');

    /// <summary>Gets whether a character is a control character, other than tab, that TOML does not allow in text.</summary>
    private static bool IsControlCharacter(char value) => value is (< ' ' and not '\t') or '\u007F';

    private static string ToCodePoint(char value) => ((int)value).ToString("X4", CultureInfo.InvariantCulture);
}
