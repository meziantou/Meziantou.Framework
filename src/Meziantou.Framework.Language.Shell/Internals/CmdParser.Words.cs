namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>Cmd words, variable references, trivia, and the shared token helpers.</summary>
internal sealed partial class CmdParser
{
    // ---- words ----

    private ShellWordSyntax ParseWord()
    {
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;

        while (!IsAtEnd && !IsWordBoundary(Current))
        {
            var (trivia, fullStart) = isFirst ? TakeTrivia() : ([], _position);
            isFirst = false;

            var positionBefore = _position;
            parts.Add(ParseWordPart(trivia, fullStart));
            if (_position == positionBefore)
            {
                _position++;
            }
        }

        return new ShellWordSyntax(parts);
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
            if (!inQuotes && Current is '&' or '|')
                break;

            var (trivia, fullStart) = isFirst ? TakeTrivia() : ([], _position);
            isFirst = false;

            var positionBefore = _position;
            if (Current == '"')
            {
                inQuotes = !inQuotes;
                _position++;
                parts.Add(new ShellLiteralWordPartSyntax(CreateToken(ShellSyntaxKind.DoubleQuoteToken, positionBefore, trivia, fullStart)));
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

        return new ShellWordSyntax(parts);
    }

    private ShellLiteralWordPartSyntax ParseSetValueLiteralRun(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart, bool inQuotes)
    {
        var start = _position;
        while (!IsAtEnd
            && GetLineBreakLength(_position) == 0
            && Current is not '"' and not '^' and not '%' and not '!'
            && (inQuotes || Current is not ('&' or '|')))
        {
            _position++;
        }

        if (_position == start)
        {
            _position++;
        }

        return new ShellLiteralWordPartSyntax(CreateToken(ShellSyntaxKind.GenericToken, start, leadingTrivia, fullStart));
    }

    private ShellWordPartSyntax ParseWordPart(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
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

    private ShellLiteralWordPartSyntax ParseLiteralRun(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _position;
        while (!IsAtEnd && !IsWordBoundary(Current) && Current is not '"' and not '^' and not '%' and not '!' and not '*' and not '?')
        {
            _position++;
        }

        if (_position == start)
        {
            _position++;
        }

        return new ShellLiteralWordPartSyntax(CreateToken(ShellSyntaxKind.GenericToken, start, leadingTrivia, fullStart));
    }

    private ShellGlobSyntax ParseGlob(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var kind = Current == '*' ? ShellSyntaxKind.AsteriskToken : ShellSyntaxKind.QuestionToken;
        var start = _position;
        _position++;

        return new ShellGlobSyntax(CreateToken(kind, start, leadingTrivia, fullStart));
    }

    private ShellEscapeSequenceSyntax ParseEscapeSequence(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _position;
        _position++;
        string value;
        if (IsAtEnd)
        {
            value = "^";
        }
        else
        {
            value = Current.ToString();
            _position++;
        }

        return new ShellEscapeSequenceSyntax(CreateToken(ShellSyntaxKind.EscapeToken, start, leadingTrivia, fullStart, value));
    }

    private ShellQuotedStringSyntax ParseQuotedString(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var quoteStart = _position;
        _position++;
        var openToken = CreateToken(ShellSyntaxKind.DoubleQuoteToken, quoteStart, leadingTrivia, fullStart);

        var parts = new List<ShellWordPartSyntax>();
        while (!IsAtEnd && Current != '"' && GetLineBreakLength(_position) == 0)
        {
            var positionBefore = _position;
            parts.Add(Current switch
            {
                '%' => ParsePercentReference([], _position),
                '!' when _options.Dialect.HasFeature(ShellDialectFeatures.DelayedExpansion) && IsDelayedExpansion() => ParseDelayedReference([], _position),
                _ => ParseQuotedLiteralRun(),
            });

            if (_position == positionBefore)
            {
                _position++;
            }
        }

        ShellSyntaxToken closeToken;
        if (IsAtEnd || Current != '"')
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated quoted string.");
            closeToken = MissingToken(ShellSyntaxKind.DoubleQuoteToken, _position);
        }
        else
        {
            var closeStart = _position;
            _position++;
            closeToken = CreateToken(ShellSyntaxKind.DoubleQuoteToken, closeStart, [], closeStart);
        }

        return new ShellQuotedStringSyntax(openToken, parts, closeToken);
    }

    private ShellLiteralWordPartSyntax ParseQuotedLiteralRun()
    {
        var start = _position;
        while (!IsAtEnd && Current is not '"' and not '%' and not '!' && GetLineBreakLength(_position) == 0)
        {
            _position++;
        }

        return new ShellLiteralWordPartSyntax(CreateToken(ShellSyntaxKind.BareTextToken, start, [], start));
    }

    /// <summary>
    /// Reads <c>%VAR%</c>, a positional argument such as <c>%1</c> or <c>%~dp0</c>, or a loop variable <c>%%i</c>.
    /// A lone <c>%</c> that closes nothing stays literal text.
    /// </summary>
    private ShellWordPartSyntax ParsePercentReference(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _position;

        // `%%` that names nothing is an escaped literal percent.
        if (Peek(1) == '%' && !IsNameCharacter(Peek(2)) && Peek(2) != '~')
        {
            _position += 2;

            return new ShellEscapeSequenceSyntax(CreateToken(ShellSyntaxKind.EscapeToken, start, leadingTrivia, fullStart, "%"));
        }

        // `%%i` is a for-loop variable inside a batch file.
        if (Peek(1) == '%')
        {
            _position += 2;
            var openToken = CreateToken(ShellSyntaxKind.BareTextToken, start, leadingTrivia, fullStart);
            var loopNameStart = _position;
            SkipArgumentSelector();
            var loopNameToken = CreateToken(ShellSyntaxKind.VariableNameToken, loopNameStart, [], loopNameStart);

            return new CmdVariableReferenceSyntax(openToken, loopNameToken, closeToken: null);
        }

        // `%1`, `%*`, and `%~dp0` have no closing percent.
        if (char.IsAsciiDigit(Peek(1)) || Peek(1) == '*' || Peek(1) == '~')
        {
            _position++;
            var openToken = CreateToken(ShellSyntaxKind.BareTextToken, start, leadingTrivia, fullStart);
            var argumentStart = _position;
            SkipArgumentSelector();
            var argumentToken = CreateToken(ShellSyntaxKind.VariableNameToken, argumentStart, [], argumentStart);

            return new CmdVariableReferenceSyntax(openToken, argumentToken, closeToken: null);
        }

        var closingIndex = FindClosing('%');
        if (closingIndex < 0)
        {
            _position++;

            return new ShellLiteralWordPartSyntax(CreateToken(ShellSyntaxKind.GenericToken, start, leadingTrivia, fullStart));
        }

        _position++;
        var percentOpenToken = CreateToken(ShellSyntaxKind.BareTextToken, start, leadingTrivia, fullStart);
        var nameStart = _position;
        _position = closingIndex;
        var nameToken = CreateToken(ShellSyntaxKind.VariableNameToken, nameStart, [], nameStart);
        var closeStart = _position;
        _position++;

        return new CmdVariableReferenceSyntax(percentOpenToken, nameToken, CreateToken(ShellSyntaxKind.BareTextToken, closeStart, [], closeStart));
    }

    private CmdVariableReferenceSyntax ParseDelayedReference(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _position;
        _position++;
        var openToken = CreateToken(ShellSyntaxKind.BareTextToken, start, leadingTrivia, fullStart);

        var nameStart = _position;
        while (!IsAtEnd && Current != '!' && GetLineBreakLength(_position) == 0)
        {
            _position++;
        }

        var nameToken = CreateToken(ShellSyntaxKind.VariableNameToken, nameStart, [], nameStart);

        ShellSyntaxToken closeToken;
        if (IsAtEnd || Current != '!')
        {
            closeToken = MissingToken(ShellSyntaxKind.BareTextToken, _position);
        }
        else
        {
            var closeStart = _position;
            _position++;
            closeToken = CreateToken(ShellSyntaxKind.BareTextToken, closeStart, [], closeStart);
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

                _pendingTrivia.Add(new ShellSyntaxTrivia(ShellSyntaxKind.WhitespaceTrivia, _text[start.._position], start));
                continue;
            }

            // A caret immediately before a line break joins two physical lines.
            if (Current == '^' && GetLineBreakLength(_position + 1) > 0)
            {
                _position += 1 + GetLineBreakLength(_position + 1);
                _pendingTrivia.Add(new ShellSyntaxTrivia(ShellSyntaxKind.LineContinuationTrivia, _text[start.._position], start));
                continue;
            }

            if (Current == ':' && Peek(1) == ':' && IsAtStatementStart(start))
            {
                SkipToEndOfLine();
                _pendingTrivia.Add(new ShellSyntaxTrivia(ShellSyntaxKind.CmdDoubleColonCommentTrivia, _text[start.._position], start));
                continue;
            }

            if (IsRemComment())
            {
                SkipToEndOfLine();
                _pendingTrivia.Add(new ShellSyntaxTrivia(ShellSyntaxKind.CmdRemCommentTrivia, _text[start.._position], start));
                continue;
            }

            if (includeLineBreaks)
            {
                var lineBreakLength = GetLineBreakLength(_position);
                if (lineBreakLength > 0)
                {
                    _position += lineBreakLength;
                    _pendingTrivia.Add(new ShellSyntaxTrivia(ShellSyntaxKind.EndOfLineTrivia, _text[start.._position], start));
                    continue;
                }
            }

            break;
        }
    }

    /// <summary>A <c>REM</c> comment runs to the end of the line and must be followed by a separator.</summary>
    private bool IsRemComment()
    {
        if (!IsAtStatementStart(_position))
            return false;

        if (_position + 3 > _text.Length)
            return false;

        if (!_text.AsSpan(_position, 3).Equals("rem", StringComparison.OrdinalIgnoreCase))
            return false;

        var next = Peek(3);

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

    private (IReadOnlyList<ShellSyntaxTrivia> Trivia, int FullStart) TakeTrivia()
    {
        if (_pendingTrivia.Count == 0)
            return ([], _position);

        var trivia = _pendingTrivia.ToArray();
        var start = _pendingTriviaStart;
        _pendingTrivia.Clear();

        return (trivia, start);
    }

    private ShellSyntaxToken CreateToken(ShellSyntaxKind kind, int tokenStart, IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart, string? valueText = null)
    {
        _position = Math.Clamp(_position, 0, _text.Length);
        tokenStart = Math.Clamp(tokenStart, 0, _position);
        var text = _text[tokenStart.._position];

        return new ShellSyntaxToken(kind, text, valueText ?? text, leadingTrivia: leadingTrivia, fullStart: fullStart);
    }

    private ShellSyntaxToken ReadToken(ShellSyntaxKind kind, int length)
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

    private ShellSyntaxToken ReadKeyword()
    {
        AccumulateStatementTrivia();
        var keyword = PeekKeyword() ?? string.Empty;

        return ReadToken(ShellSyntaxKind.KeywordToken, keyword.Length);
    }

    private ShellSyntaxToken ExpectKeyword(string keyword)
    {
        AccumulateStatementTrivia();
        if (string.Equals(PeekKeyword(), keyword, StringComparison.Ordinal))
            return ReadToken(ShellSyntaxKind.KeywordToken, keyword.Length);

        AddDiagnostic(new TextSpan(_position, 0), "SHELL0012", $"Expected '{keyword}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(ShellSyntaxKind.KeywordToken, fullStart, trivia);
    }

    private ShellSyntaxToken ExpectCharacter(char expected, ShellSyntaxKind kind)
    {
        AccumulateStatementTrivia();
        if (Current == expected)
            return ReadToken(kind, length: 1);

        AddDiagnostic(new TextSpan(_position, 0), "SHELL0012", $"Expected '{expected}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(kind, fullStart, trivia);
    }

    private void SkipWord()
    {
        while (!IsAtEnd && !IsWordBoundary(Current))
        {
            _position++;
        }
    }

    private void SkipBlanks()
    {
        while (!IsAtEnd && Current is ' ' or '\t')
        {
            _position++;
        }
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

        return new ShellSkippedTextSyntax(
            [new ShellSyntaxToken(ShellSyntaxKind.BadToken, text, text, leadingTrivia: trivia, fullStart: fullStart)],
            fullStart);
    }

    private ShellSkippedTextSyntax ConsumeUnexpectedCharacter()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _position;
        _position = Math.Min(_position + 1, _text.Length);
        var token = CreateToken(ShellSyntaxKind.BadToken, start, trivia, fullStart);
        AddDiagnostic(token.Span, "SHELL0002", $"Unexpected '{token.Text}'.");

        return new ShellSkippedTextSyntax([token], fullStart);
    }

    private static ShellSyntaxToken MissingToken(ShellSyntaxKind kind, int position, IReadOnlyList<ShellSyntaxTrivia>? leadingTrivia = null)
    {
        return new ShellSyntaxToken(kind, string.Empty, string.Empty, isMissing: true, leadingTrivia: leadingTrivia, fullStart: position);
    }

    private void AddDiagnostic(TextSpan span, string id, string message)
    {
        _diagnostics.Add(new ShellDiagnostic(id, message, ShellDiagnosticSeverity.Error, span));
    }

    private int GetLineBreakLength(int position) => position < _text.Length ? SourceText.GetLineBreakLength(_text, position) : 0;

    private static bool IsWordBoundary(char value) =>
        value is '\0' or ' ' or '\t' or '\r' or '\n' or '&' or '|' or '<' or '>' or '(' or ')';

    private static bool IsNameCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}
