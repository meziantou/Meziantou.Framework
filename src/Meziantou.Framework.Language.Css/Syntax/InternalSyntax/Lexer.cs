using System.Text;
using Meziantou.Framework.Language.Css.Internals;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>Turns CSS text into the tokens of the CSS tokenizer, keeping whitespace and comments as trivia.</summary>
/// <remarks>
/// <para>
/// The tokenizer of CSS Syntax Level 3 is context-free, so the whole text is read up front. Its whitespace tokens are
/// trivia here, together with the comments it drops: whatever is on the line after a token, up to and including the
/// line break, belongs to that token, and the rest belongs to the token that follows. A parser that needs to know
/// whether two tokens touch -- <c>a.b</c> is one compound selector, <c>a .b</c> two -- looks at the trivia between them.
/// </para>
/// <para>
/// The only exception to context-freedom is the value of a <c>unicode-range</c> descriptor, which the tokenizer reads
/// with unicode ranges enabled. It is recognized from the tokens before it, as a declaration's name and colon.
/// </para>
/// </remarks>
internal sealed class Lexer(SourceText source)
{
    private readonly string _text = source.Text;
    private List<SyntaxDiagnosticInfo>? _tokenDiagnostics;
    private int _tokenBodyStart;
    private int _position;

    /// <summary>Reads every token of the text. The last one is the end-of-file token, which holds the trivia after the last real token.</summary>
    public List<GreenToken> LexAll()
    {
        var tokens = new List<GreenToken>();
        var unicodeRanges = false;
        while (true)
        {
            var token = Lex(unicodeRanges);
            tokens.Add(token);
            var kind = (SyntaxKind)token.RawKind;
            if (kind == SyntaxKind.EndOfFileToken)
                return tokens;

            if (kind == SyntaxKind.ColonToken && tokens.Count >= 2 && tokens[^2] is { RawKind: (int)SyntaxKind.IdentToken } name && CssIdentifier.EqualsIgnoreAsciiCase(name.ValueText, "unicode-range"))
            {
                unicodeRanges = true;
            }
            else if (kind is SyntaxKind.SemicolonToken or SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken or SyntaxKind.ExclamationToken)
            {
                unicodeRanges = false;
            }
        }
    }

    private GreenToken Lex(bool unicodeRanges)
    {
        _tokenDiagnostics = null;

        var fullStart = _position;
        var leading = LexTrivia(isTrailing: false);
        _tokenBodyStart = _position;

        var token = ScanToken(unicodeRanges);
        var trailing = token.RawKind == (int)SyntaxKind.EndOfFileToken ? null : LexTrivia(isTrailing: true);
        if (leading is not null || trailing is not null)
        {
            token = token.WithTrivia(leading, trailing);
        }

        if (_tokenDiagnostics is not { Count: > 0 })
            return token;

        // Diagnostics are recorded against the text, and stored against the start of the token, trivia included.
        var leadingWidth = _tokenBodyStart - fullStart;
        var moved = _tokenDiagnostics.Select(info => info.WithOffset(info.Offset + leadingWidth)).ToArray();

        return (GreenToken)token.WithAdditionalDiagnostics(moved);
    }

    private GreenToken ScanToken(bool unicodeRanges)
    {
        if (_position >= _text.Length)
            return SyntaxFactory.Token(SyntaxKind.EndOfFileToken);

        var c = _text[_position];
        switch (c)
        {
            case '"' or '\'':
                return ScanString(c);
            case '#':
                if (CssIdentifier.IsIdentChar(Peek(1)) || CssIdentifier.IsValidEscape(_text, _position + 1))
                {
                    var start = _position;
                    _position++;
                    var name = CssIdentifier.ConsumeIdentSequence(_text, ref _position);
                    return Value(SyntaxKind.HashToken, start, name);
                }

                return Delim();
            case '(':
                return Fixed(SyntaxKind.OpenParenToken);
            case ')':
                return Fixed(SyntaxKind.CloseParenToken);
            case '[':
                return Fixed(SyntaxKind.OpenBracketToken);
            case ']':
                return Fixed(SyntaxKind.CloseBracketToken);
            case '{':
                return Fixed(SyntaxKind.OpenBraceToken);
            case '}':
                return Fixed(SyntaxKind.CloseBraceToken);
            case ',':
                return Fixed(SyntaxKind.CommaToken);
            case ':':
                return Fixed(SyntaxKind.ColonToken);
            case ';':
                return Fixed(SyntaxKind.SemicolonToken);
            case '+' or '.':
                return CssIdentifier.WouldStartNumber(_text, _position) ? ScanNumeric() : Delim();
            case '-':
                if (CssIdentifier.WouldStartNumber(_text, _position))
                    return ScanNumeric();

                if (Peek(1) == '-' && Peek(2) == '>')
                {
                    _position += 3;
                    return SyntaxFactory.Token(SyntaxKind.CdcToken);
                }

                return CssIdentifier.WouldStartIdentSequence(_text, _position) ? ScanIdentLike() : Delim();
            case '<':
                if (Peek(1) == '!' && Peek(2) == '-' && Peek(3) == '-')
                {
                    _position += 4;
                    return SyntaxFactory.Token(SyntaxKind.CdoToken);
                }

                return Delim();
            case '@':
                if (CssIdentifier.WouldStartIdentSequence(_text, _position + 1))
                {
                    var start = _position;
                    _position++;
                    var name = CssIdentifier.ConsumeIdentSequence(_text, ref _position);
                    return Value(SyntaxKind.AtKeywordToken, start, name);
                }

                return Delim();
            case '\\':
                if (CssIdentifier.IsValidEscape(_text, _position))
                    return ScanIdentLike();

                AddDiagnostic(_position, 1, CssDiagnosticDescriptors.InvalidEscape);
                return Delim();
            case 'u' or 'U' when unicodeRanges && Peek(1) == '+' && (Peek(2) == '?' || CssIdentifier.IsHexDigit(Peek(2))):
                return ScanUnicodeRange();
        }

        if (char.IsAsciiDigit(c))
            return ScanNumeric();

        if (CssIdentifier.IsIdentStart(c))
            return ScanIdentLike();

        return Delim();
    }

    private GreenToken Fixed(SyntaxKind kind)
    {
        _position++;
        return SyntaxFactory.Token(kind);
    }

    /// <summary>Reads one character as a delimiter, keeping a surrogate pair together.</summary>
    private GreenToken Delim()
    {
        var start = _position;
        _position += char.IsHighSurrogate(_text[_position]) && char.IsLowSurrogate(Peek(1)) ? 2 : 1;
        var kind = SyntaxFacts.GetDelimKind(_text[start]);

        return kind == SyntaxKind.DelimToken ? SyntaxFactory.Token(leading: null, kind, _text[start.._position], trailing: null) : SyntaxFactory.Token(kind);
    }

    private GreenToken Value(SyntaxKind kind, int start, string value)
    {
        var text = _text[start.._position];

        return string.Equals(text, value, StringComparison.Ordinal)
            ? SyntaxFactory.Token(leading: null, kind, text, trailing: null)
            : SyntaxFactory.TokenWithValue(leading: null, kind, text, value, value, trailing: null);
    }

    private GreenToken ScanIdentLike()
    {
        var start = _position;
        var name = CssIdentifier.ConsumeIdentSequence(_text, ref _position);
        if (Peek(0) != '(')
            return Value(SyntaxKind.IdentToken, start, name);

        _position++;
        if (CssIdentifier.EqualsIgnoreAsciiCase(name, "url"))
        {
            // A quoted URL is a function holding a string; only an unquoted one is a url token.
            var afterParenthesis = _position;
            while (CssIdentifier.IsWhitespace(Peek(0)) && _position < _text.Length)
            {
                _position++;
            }

            var next = Peek(0);
            if (next is not ('"' or '\''))
            {
                _position = afterParenthesis;
                return ScanUrl(start);
            }

            _position = afterParenthesis;
        }

        return SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.FunctionToken, _text[start.._position], name, name, trailing: null);
    }

    private GreenToken ScanUrl(int start)
    {
        var value = new StringBuilder();
        SkipWhitespace();
        while (true)
        {
            if (_position >= _text.Length)
            {
                AddDiagnostic(start, _position - start, CssDiagnosticDescriptors.UnterminatedUrl);
                return UrlToken();
            }

            var c = _text[_position];
            if (c == ')')
            {
                _position++;
                return UrlToken();
            }

            if (CssIdentifier.IsWhitespace(c))
            {
                SkipWhitespace();
                if (_position >= _text.Length)
                {
                    AddDiagnostic(start, _position - start, CssDiagnosticDescriptors.UnterminatedUrl);
                    return UrlToken();
                }

                if (_text[_position] == ')')
                {
                    _position++;
                    return UrlToken();
                }

                return BadUrl("whitespace");
            }

            switch (c)
            {
                case '"' or '\'' or '(':
                    return BadUrl($"'{c}'");
                case '\\':
                    if (!CssIdentifier.IsValidEscape(_text, _position))
                        return BadUrl("a backslash followed by a line break");

                    _position++;
                    CssIdentifier.AppendCodePoint(value, CssIdentifier.ConsumeEscapedCodePoint(_text, ref _position));
                    continue;
            }

            if (CssIdentifier.IsNonPrintable(c))
                return BadUrl(string.Create(CultureInfo.InvariantCulture, $"the control character U+{(int)c:X4}"));

            if (char.IsHighSurrogate(c) && char.IsLowSurrogate(Peek(1)))
            {
                value.Append(c).Append(_text[_position + 1]);
                _position += 2;
                continue;
            }

            value.Append(c is '\0' || char.IsSurrogate(c) ? CssIdentifier.ReplacementCharacter : c);
            _position++;
        }

        GreenToken UrlToken()
        {
            var url = value.ToString();
            return SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.UrlToken, _text[start.._position], url, url, trailing: null);
        }

        GreenToken BadUrl(string reason)
        {
            // What remains of a bad URL is read up to its closing parenthesis, which an escape does not end.
            var errorStart = _position;
            while (_position < _text.Length && _text[_position] != ')')
            {
                if (CssIdentifier.IsValidEscape(_text, _position))
                {
                    _position++;
                    CssIdentifier.ConsumeEscapedCodePoint(_text, ref _position);
                }
                else
                {
                    _position++;
                }
            }

            if (_position < _text.Length)
            {
                _position++;
            }

            AddDiagnostic(errorStart, 1, CssDiagnosticDescriptors.BadUrl, reason);
            return SyntaxFactory.Token(leading: null, SyntaxKind.BadUrlToken, _text[start.._position], trailing: null);
        }
    }

    private GreenToken ScanString(char quote)
    {
        var start = _position;
        _position++;
        var value = new StringBuilder();
        while (true)
        {
            if (_position >= _text.Length)
            {
                AddDiagnostic(start, _position - start, CssDiagnosticDescriptors.UnterminatedString);
                break;
            }

            var c = _text[_position];
            if (c == quote)
            {
                _position++;
                break;
            }

            if (CssIdentifier.IsNewline(c))
            {
                // The line break is not part of the string: it is trivia after it.
                AddDiagnostic(start, _position - start, CssDiagnosticDescriptors.UnterminatedString);
                return SyntaxFactory.Token(leading: null, SyntaxKind.BadStringToken, _text[start.._position], trailing: null);
            }

            if (c == '\\')
            {
                _position++;
                if (_position >= _text.Length)
                    continue;

                if (CssIdentifier.IsNewline(_text[_position]))
                {
                    // An escaped line break continues the string on the next line, and is not part of its value.
                    _position += _text[_position] == '\r' && Peek(1) == '\n' ? 2 : 1;
                    continue;
                }

                CssIdentifier.AppendCodePoint(value, CssIdentifier.ConsumeEscapedCodePoint(_text, ref _position));
                continue;
            }

            if (char.IsHighSurrogate(c) && char.IsLowSurrogate(Peek(1)))
            {
                value.Append(c).Append(_text[_position + 1]);
                _position += 2;
                continue;
            }

            value.Append(c is '\0' || char.IsSurrogate(c) ? CssIdentifier.ReplacementCharacter : c);
            _position++;
        }

        var text = _text[start.._position];
        var stringValue = value.ToString();
        return SyntaxFactory.TokenWithValue(leading: null, SyntaxKind.StringToken, text, stringValue, stringValue, trailing: null);
    }

    private GreenToken ScanNumeric()
    {
        var start = _position;
        if (Peek(0) is '+' or '-')
        {
            _position++;
        }

        while (char.IsAsciiDigit(Peek(0)))
        {
            _position++;
        }

        if (Peek(0) == '.' && char.IsAsciiDigit(Peek(1)))
        {
            _position++;
            while (char.IsAsciiDigit(Peek(0)))
            {
                _position++;
            }
        }

        if (Peek(0) is 'e' or 'E' && (char.IsAsciiDigit(Peek(1)) || (Peek(1) is '+' or '-' && char.IsAsciiDigit(Peek(2)))))
        {
            _position += 2;
            while (char.IsAsciiDigit(Peek(0)))
            {
                _position++;
            }
        }

        var number = ParseNumber(_text.AsSpan(start, _position - start));
        SyntaxKind kind;
        if (CssIdentifier.WouldStartIdentSequence(_text, _position))
        {
            CssIdentifier.ConsumeIdentSequence(_text, ref _position);
            kind = SyntaxKind.DimensionToken;
        }
        else if (Peek(0) == '%')
        {
            _position++;
            kind = SyntaxKind.PercentageToken;
        }
        else
        {
            kind = SyntaxKind.NumberToken;
        }

        var text = _text[start.._position];
        return SyntaxFactory.TokenWithValue(leading: null, kind, text, number, text, trailing: null);
    }

    /// <summary>Reads a number the tokenizer has already delimited. One too large for a double is infinite, as in a browser.</summary>
    internal static double ParseNumber(ReadOnlySpan<char> text)
        => double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

    private GreenToken ScanUnicodeRange()
    {
        var start = _position;
        _position += 2;
        var digits = 0;
        while (digits < 6 && CssIdentifier.IsHexDigit(Peek(0)))
        {
            _position++;
            digits++;
        }

        var hasQuestionMarks = false;
        while (digits < 6 && Peek(0) == '?')
        {
            _position++;
            digits++;
            hasQuestionMarks = true;
        }

        if (!hasQuestionMarks && Peek(0) == '-' && CssIdentifier.IsHexDigit(Peek(1)))
        {
            _position++;
            var endDigits = 0;
            while (endDigits < 6 && CssIdentifier.IsHexDigit(Peek(0)))
            {
                _position++;
                endDigits++;
            }
        }

        return SyntaxFactory.Token(leading: null, SyntaxKind.UnicodeRangeToken, _text[start.._position], trailing: null);
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && CssIdentifier.IsWhitespace(_text[_position]))
        {
            _position++;
        }
    }

    private GreenNode? LexTrivia(bool isTrailing)
    {
        List<GreenNode?>? trivia = null;
        while (_position < _text.Length)
        {
            var start = _position;
            var c = _text[_position];
            if (c is ' ' or '\t')
            {
                while (_position < _text.Length && _text[_position] is ' ' or '\t')
                {
                    _position++;
                }

                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            if (CssIdentifier.IsNewline(c))
            {
                _position += c == '\r' && Peek(1) == '\n' ? 2 : 1;
                Add(ref trivia, SyntaxKind.EndOfLineTrivia, start);

                // The line is over, so whatever comes next belongs to the next token.
                if (isTrailing)
                    break;

                continue;
            }

            if (c == '/' && Peek(1) == '*')
            {
                var end = _text.IndexOf("*/", _position + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    _position = _text.Length;
                    Add(ref trivia, SyntaxKind.MultiLineCommentTrivia, start, new SyntaxDiagnosticInfo(0, _position - start, CssDiagnosticDescriptors.UnterminatedComment, []));
                }
                else
                {
                    _position = end + 2;
                    Add(ref trivia, SyntaxKind.MultiLineCommentTrivia, start);
                }

                continue;
            }

            break;
        }

        return trivia is null ? null : SyntaxFactory.List(trivia);
    }

    private void Add(ref List<GreenNode?>? trivia, SyntaxKind kind, int start, params SyntaxDiagnosticInfo[] diagnostics)
    {
        GreenNode item = SyntaxFactory.Trivia(kind, _text[start.._position]);
        if (diagnostics.Length > 0)
        {
            item = item.WithAdditionalDiagnostics(diagnostics);
        }

        (trivia ??= []).Add(item);
    }

    private void AddDiagnostic(int start, int width, DiagnosticDescriptor descriptor, params object?[]? arguments)
        => (_tokenDiagnostics ??= []).Add(new SyntaxDiagnosticInfo(start - _tokenBodyStart, Math.Max(0, width), descriptor, arguments));

    private char Peek(int offset) => CssIdentifier.CharAt(_text, _position + offset);
}
