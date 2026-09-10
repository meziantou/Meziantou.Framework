using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>Cmd words, variable references, trivia, and the shared token helpers.</summary>
internal sealed partial class CmdParser
{
    // ---- words ----

    private ShellWordSyntax ParseWord()
    {
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;

        while (!IsAtEnd && !IsWordBoundary(Current) && !IsAtEqualityOperator())
        {
            var (trivia, fullStart) = isFirst ? TakeTrivia() : (null, _position);
            isFirst = false;

            var positionBefore = _position;
            parts.Add(ParseWordPart(trivia, fullStart));
            if (_position == positionBefore)
            {
                _position++;
            }
        }

        return new ShellWordSyntax(ParserHelpers.List(parts));
    }

    /// <summary>
    /// Reads the value of a <c>set</c> statement. Unlike an ordinary argument it runs to the end of the line, so
    /// parentheses and other metacharacters inside <c>set /a "x=(1+2)*3"</c> stay part of the value.
    /// </summary>
    private ShellWordSyntax ParseSetValue()
    {
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;
        var inQuotes = false;

        while (!IsAtEnd && GetLineBreakLength(_position) == 0)
        {
            // `if 1==1 (set N=5)` assigns `5`; at the top level `set N=5)` assigns `5)`, parenthesis included.
            if (!inQuotes && (Current is '&' or '|' || (Current == ')' && _stopAtCloseParen)))
                break;

            var (trivia, fullStart) = isFirst ? TakeTrivia() : (null, _position);
            isFirst = false;

            var positionBefore = _position;
            if (Current == '"')
            {
                inQuotes = !inQuotes;
                _position++;
                parts.Add(new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.DoubleQuoteToken, positionBefore, trivia, fullStart)));
                continue;
            }

            parts.Add(Current switch
            {
                '^' => ParseEscapeSequence(trivia, fullStart),
                '%' => ParsePercentReference(trivia, fullStart),
                '!' when _options.Dialect.HasFeature(ShellDialectFeatures.DelayedExpansion) && IsDelayedExpansion() => ParseDelayedReference(trivia, fullStart),
                _ => ParseSetValueLiteralRun(trivia, fullStart, inQuotes),
            });

            if (_position == positionBefore)
            {
                _position++;
            }
        }

        return new ShellWordSyntax(ParserHelpers.List(parts));
    }

    private ShellLiteralWordPartSyntax ParseSetValueLiteralRun(GreenNode? leadingTrivia, int fullStart, bool inQuotes)
    {
        var start = _position;
        while (!IsAtEnd
            && GetLineBreakLength(_position) == 0
            && Current is not '"' and not '^' and not '%' and not '!'
            && (inQuotes || (Current is not ('&' or '|') && !(Current == ')' && _stopAtCloseParen))))
        {
            _position++;
        }

        if (_position == start)
        {
            _position++;
        }

        return new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.GenericToken, start, leadingTrivia, fullStart));
    }

    private ShellWordPartSyntax ParseWordPart(GreenNode? leadingTrivia, int fullStart)
    {
        return Current switch
        {
            '"' => ParseQuotedString(leadingTrivia, fullStart),
            '^' => ParseEscapeSequence(leadingTrivia, fullStart),
            '%' => ParsePercentReference(leadingTrivia, fullStart),
            '!' when _options.Dialect.HasFeature(ShellDialectFeatures.DelayedExpansion) && IsDelayedExpansion() => ParseDelayedReference(leadingTrivia, fullStart),
            '*' or '?' => ParseGlob(leadingTrivia, fullStart),
            _ => ParseLiteralRun(leadingTrivia, fullStart),
        };
    }

    private bool IsDelayedExpansion()
    {
        var scan = _position + 1;
        while (scan < _text.Length && _text[scan] != '!' && GetLineBreakLength(scan) == 0)
        {
            scan++;
        }

        return scan < _text.Length && _text[scan] == '!' && scan > _position + 1;
    }

    private ShellLiteralWordPartSyntax ParseLiteralRun(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _position;
        while (!IsAtEnd && !IsWordBoundary(Current) && !IsAtEqualityOperator() && Current is not '"' and not '^' and not '%' and not '!' and not '*' and not '?')
        {
            _position++;
        }

        if (_position == start)
        {
            _position++;
        }

        return new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.GenericToken, start, leadingTrivia, fullStart));
    }

    private ShellGlobSyntax ParseGlob(GreenNode? leadingTrivia, int fullStart)
    {
        var kind = Current == '*' ? SyntaxKind.AsteriskToken : SyntaxKind.QuestionToken;
        var start = _position;
        _position++;

        return new ShellGlobSyntax(CreateToken(kind, start, leadingTrivia, fullStart));
    }

    private ShellEscapeSequenceSyntax ParseEscapeSequence(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _position;
        _position++;
        string value;
        if (IsAtEnd)
        {
            value = "^";
        }
        else if (GetLineBreakLength(_position) is var lineBreakLength && lineBreakLength > 0)
        {
            // A caret escaping a line break joins the two lines, so `echo a^` followed by `b` echoes `ab`.
            _position += lineBreakLength;
            value = string.Empty;
        }
        else
        {
            value = Current.ToString();
            _position++;
        }

        return new ShellEscapeSequenceSyntax(CreateToken(SyntaxKind.EscapeToken, start, leadingTrivia, fullStart, value));
    }

    private ShellQuotedStringSyntax ParseQuotedString(GreenNode? leadingTrivia, int fullStart)
    {
        var quoteStart = _position;
        _position++;
        var openToken = CreateToken(SyntaxKind.DoubleQuoteToken, quoteStart, leadingTrivia, fullStart);

        var parts = new List<ShellWordPartSyntax>();
        while (!IsAtEnd && Current != '"' && GetLineBreakLength(_position) == 0)
        {
            var positionBefore = _position;
            parts.Add(Current switch
            {
                '%' => ParsePercentReference(null, _position),
                '!' when _options.Dialect.HasFeature(ShellDialectFeatures.DelayedExpansion) && IsDelayedExpansion() => ParseDelayedReference(null, _position),
                _ => ParseQuotedLiteralRun(),
            });

            if (_position == positionBefore)
            {
                _position++;
            }
        }

        ScannedToken closeToken;
        if (IsAtEnd || Current != '"')
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated quoted string.");
            closeToken = MissingToken(SyntaxKind.DoubleQuoteToken, _position);
        }
        else
        {
            var closeStart = _position;
            _position++;
            closeToken = CreateToken(SyntaxKind.DoubleQuoteToken, closeStart, null, closeStart);
        }

        return new ShellQuotedStringSyntax(openToken, ParserHelpers.List(parts), closeToken);
    }

    private ShellLiteralWordPartSyntax ParseQuotedLiteralRun()
    {
        var start = _position;
        while (!IsAtEnd && Current is not '"' and not '%' and not '!' && GetLineBreakLength(_position) == 0)
        {
            _position++;
        }

        // A `!` that opens no delayed expansion is ordinary text, so it has to be taken into the run rather than
        // skipped; otherwise the character would be missing from the tree.
        if (_position == start && !IsAtEnd && GetLineBreakLength(_position) == 0)
        {
            _position++;
        }

        return new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.BareTextToken, start, null, start));
    }

    /// <summary>
    /// Reads <c>%VAR%</c>, a positional argument such as <c>%1</c> or <c>%~dp0</c>, or a loop variable <c>%%i</c>.
    /// A lone <c>%</c> that closes nothing stays literal text.
    /// </summary>
    private ShellWordPartSyntax ParsePercentReference(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _position;

        // `%%` that names nothing is an escaped literal percent.
        if (Peek(1) == '%' && !IsNameCharacter(Peek(2)) && Peek(2) != '~')
        {
            _position += 2;

            return new ShellEscapeSequenceSyntax(CreateToken(SyntaxKind.EscapeToken, start, leadingTrivia, fullStart, "%"));
        }

        // `%%i` is a for-loop variable inside a batch file.
        if (Peek(1) == '%')
        {
            _position += 2;
            var openToken = CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart);
            var loopNameStart = _position;
            SkipArgumentSelector();
            var loopNameToken = CreateToken(SyntaxKind.VariableNameToken, loopNameStart, null, loopNameStart);

            return new CmdVariableReferenceSyntax(openToken, loopNameToken, closeToken: null);
        }

        // `%1`, `%*`, and `%~dp0` have no closing percent.
        if (char.IsAsciiDigit(Peek(1)) || Peek(1) == '*' || Peek(1) == '~')
        {
            _position++;
            var openToken = CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart);
            var argumentStart = _position;
            SkipArgumentSelector();
            var argumentToken = CreateToken(SyntaxKind.VariableNameToken, argumentStart, null, argumentStart);

            return new CmdVariableReferenceSyntax(openToken, argumentToken, closeToken: null);
        }

        var closingIndex = FindClosing('%');
        if (closingIndex < 0)
        {
            _position++;

            return new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.GenericToken, start, leadingTrivia, fullStart));
        }

        _position++;
        var percentOpenToken = CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart);
        var nameStart = _position;
        _position = closingIndex;
        var nameToken = CreateToken(SyntaxKind.VariableNameToken, nameStart, null, nameStart);
        var closeStart = _position;
        _position++;

        return new CmdVariableReferenceSyntax(percentOpenToken, nameToken, CreateToken(SyntaxKind.BareTextToken, closeStart, null, closeStart));
    }

    private CmdVariableReferenceSyntax ParseDelayedReference(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _position;
        _position++;
        var openToken = CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart);

        var nameStart = _position;
        while (!IsAtEnd && Current != '!' && GetLineBreakLength(_position) == 0)
        {
            _position++;
        }

        var nameToken = CreateToken(SyntaxKind.VariableNameToken, nameStart, null, nameStart);

        ScannedToken closeToken;
        if (IsAtEnd || Current != '!')
        {
            closeToken = MissingToken(SyntaxKind.BareTextToken, _position);
        }
        else
        {
            var closeStart = _position;
            _position++;
            closeToken = CreateToken(SyntaxKind.BareTextToken, closeStart, null, closeStart);
        }

        return new CmdVariableReferenceSyntax(openToken, nameToken, closeToken);
    }

    private void SkipArgumentSelector()
    {
        if (Current == '~')
        {
            _position++;
            while (!IsAtEnd && (char.IsAsciiLetter(Current) || Current == '$'))
            {
                _position++;
            }

            if (Current == ':')
            {
                _position++;
            }
        }

        if (!IsAtEnd && (char.IsAsciiLetterOrDigit(Current) || Current == '*'))
        {
            _position++;
        }
    }

    private int FindClosing(char terminator)
    {
        var scan = _position + 1;
        while (scan < _text.Length && GetLineBreakLength(scan) == 0)
        {
            if (_text[scan] == terminator)
                return scan;

            scan++;
        }

        return -1;
    }

    // ---- trivia and tokens ----

    private void AccumulateInlineTrivia() => AccumulateTrivia(includeLineBreaks: false);

    private void AccumulateStatementTrivia() => AccumulateTrivia(includeLineBreaks: true);

    private void AccumulateTrivia(bool includeLineBreaks)
    {
        if (_pendingTrivia.Count == 0)
        {
            _pendingTriviaStart = _position;
        }

        while (!IsAtEnd)
        {
            var start = _position;

            if (Current is ' ' or '\t')
            {
                while (!IsAtEnd && Current is ' ' or '\t')
                {
                    _position++;
                }

                _pendingTrivia.Add(GreenFactory.Trivia(SyntaxKind.WhitespaceTrivia, _text[start.._position]));
                continue;
            }

            // A caret immediately before a line break joins two physical lines.
            if (Current == '^' && GetLineBreakLength(_position + 1) > 0)
            {
                _position += 1 + GetLineBreakLength(_position + 1);
                _pendingTrivia.Add(GreenFactory.Trivia(SyntaxKind.LineContinuationTrivia, _text[start.._position]));
                continue;
            }

            if (Current == ':' && Peek(1) == ':' && IsAtStatementStart(start))
            {
                SkipToEndOfLine();
                _pendingTrivia.Add(GreenFactory.Trivia(SyntaxKind.CmdDoubleColonCommentTrivia, _text[start.._position]));
                continue;
            }

            if (IsRemComment())
            {
                SkipToEndOfLine();
                _pendingTrivia.Add(GreenFactory.Trivia(SyntaxKind.CmdRemCommentTrivia, _text[start.._position]));
                continue;
            }

            if (includeLineBreaks)
            {
                var lineBreakLength = GetLineBreakLength(_position);
                if (lineBreakLength > 0)
                {
                    _position += lineBreakLength;
                    _pendingTrivia.Add(GreenFactory.Trivia(SyntaxKind.EndOfLineTrivia, _text[start.._position]));
                    continue;
                }
            }

            break;
        }
    }

    /// <summary>
    /// A <c>REM</c> comment runs to the end of the line and must be followed by a separator. A leading <c>@</c> only
    /// suppresses echoing, so <c>@rem</c> is a comment too.
    /// </summary>
    private bool IsRemComment()
    {
        if (!IsAtStatementStart(_position))
            return false;

        var scan = _position;
        if (scan < _text.Length && _text[scan] == '@')
        {
            scan++;
            while (scan < _text.Length && _text[scan] is ' ' or '\t')
            {
                scan++;
            }
        }

        if (scan + 3 > _text.Length)
            return false;

        if (!_text.AsSpan(scan, 3).Equals("rem", StringComparison.OrdinalIgnoreCase))
            return false;

        var next = scan + 3 < _text.Length ? _text[scan + 3] : '\0';

        return next is '\0' or ' ' or '\t' or '\r' or '\n';
    }

    private bool IsAtStatementStart(int position)
    {
        var scan = position - 1;
        while (scan >= 0 && _text[scan] is ' ' or '\t')
        {
            scan--;
        }

        return scan < 0 || _text[scan] is '\n' or '\r' or '&' or '(' or '|';
    }

    private void SkipToEndOfLine()
    {
        while (!IsAtEnd && GetLineBreakLength(_position) == 0)
        {
            _position++;
        }
    }

    /// <summary>
    /// Where the next token's text starts once its leading trivia is counted. It has to be read before the scan that
    /// measures the token, because <see cref="TakeTrivia"/> falls back to the current position when no trivia is
    /// pending, which by then would be the end of the token rather than its start.
    /// </summary>
    private int PendingFullStart => _pendingTrivia.Count == 0 ? _position : _pendingTriviaStart;

    private (GreenNode? Trivia, int FullStart) TakeTrivia()
    {
        if (_pendingTrivia.Count == 0)
            return (null, _position);

        var trivia = GreenFactory.List(CollectionsMarshal.AsSpan(_pendingTrivia));
        var start = _pendingTriviaStart;
        _pendingTrivia.Clear();

        return (trivia, start);
    }

    private ScannedToken CreateToken(SyntaxKind kind, int tokenStart, GreenNode? leadingTrivia, int fullStart, string? valueText = null)
    {
        _position = Math.Clamp(_position, 0, _text.Length);
        tokenStart = Math.Clamp(tokenStart, 0, _position);
        var text = _text[tokenStart.._position];

        return new ScannedToken(kind, text, valueText ?? text, leadingTrivia: leadingTrivia, fullStart: fullStart);
    }

    private ScannedToken ReadToken(SyntaxKind kind, int length)
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _position;
        _position = Math.Min(_position + length, _text.Length);

        return CreateToken(kind, start, trivia, fullStart);
    }

    private string? PeekKeyword()
    {
        var start = _position;
        if (start >= _text.Length || !char.IsAsciiLetter(_text[start]))
            return null;

        var scan = start;
        while (scan < _text.Length && char.IsAsciiLetter(_text[scan]))
        {
            scan++;
        }

        return _text[start..scan].ToLowerInvariant();
    }

    private string? PeekKeywordAfterTrivia()
    {
        AccumulateStatementTrivia();

        return PeekKeyword();
    }

    private ScannedToken ReadKeyword()
    {
        AccumulateStatementTrivia();
        var keyword = PeekKeyword() ?? string.Empty;

        return ReadToken(SyntaxKind.KeywordToken, keyword.Length);
    }

    private ScannedToken ExpectKeyword(string keyword)
    {
        AccumulateStatementTrivia();
        if (string.Equals(PeekKeyword(), keyword, StringComparison.Ordinal))
            return ReadToken(SyntaxKind.KeywordToken, keyword.Length);

        AddDiagnostic(new TextSpan(_position, 0), "SHELL0012", $"Expected '{keyword}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(SyntaxKind.KeywordToken, fullStart, trivia);
    }

    private ScannedToken ExpectCharacter(char expected, SyntaxKind kind)
    {
        AccumulateStatementTrivia();
        if (Current == expected)
            return ReadToken(kind, length: 1);

        AddDiagnostic(new TextSpan(_position, 0), "SHELL0012", $"Expected '{expected}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(kind, fullStart, trivia);
    }

    private bool TryEnterRecursion(TextSpan span)
    {
        if (_depth >= _options.MaxRecursionDepth)
        {
            AddDiagnostic(span, "SHELL0100", "The script nests constructs more deeply than the configured maximum.");
            return false;
        }

        _depth++;

        return true;
    }

    private ShellSkippedTextSyntax ConsumeRestAsSkippedText()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _position;
        _position = _text.Length;
        var text = _text[start..];

        return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([new ScannedToken(SyntaxKind.BadToken, text, text, leadingTrivia: trivia, fullStart: fullStart)]));
    }

    private ShellSkippedTextSyntax ConsumeUnexpectedCharacter()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _position;
        _position = Math.Min(_position + 1, _text.Length);
        var token = CreateToken(SyntaxKind.BadToken, start, trivia, fullStart);
        AddDiagnostic(token.Span, "SHELL0002", $"Unexpected '{token.Text}'.");

        return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([token]));
    }

    private static ScannedToken MissingToken(SyntaxKind kind, int position, GreenNode? leadingTrivia = null)
    {
        return new ScannedToken(kind, string.Empty, string.Empty, isMissing: true, leadingTrivia: leadingTrivia, fullStart: position);
    }

    private void AddDiagnostic(TextSpan span, string id, string message)
    {
        _diagnostics.Add(new Diagnostic(id, message, DiagnosticSeverity.Error, new Location(span, _source)));
    }

    private int GetLineBreakLength(int position) => position < _text.Length ? SourceText.GetLineBreakLength(_text, position) : 0;

    /// <summary>
    /// Characters that end a word. <c>(</c> is not among them: it only opens a block at the start of a command, so
    /// <c>echo a(b</c> is a single word. <c>)</c> ends a word only inside a block or a <c>for</c> item list.
    /// </summary>
    /// <summary>
    /// Returns <see langword="true"/> when a <c>==</c> starts here and the caller asked to stop at one. Only the left
    /// operand of a cmd comparison does: <c>=</c> is an ordinary word character everywhere else, so <c>if a==b</c>
    /// would otherwise read as the single word <c>a==b</c>.
    /// </summary>
    private bool IsAtEqualityOperator() => _stopAtEquality && Current == '=' && Peek(1) == '=';

    private bool IsWordBoundary(char value) =>
        value is '\0' or ' ' or '\t' or '\r' or '\n' or '&' or '|' or '<' or '>'
        || (value == ')' && _stopAtCloseParen);

    private static bool IsNameCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}
