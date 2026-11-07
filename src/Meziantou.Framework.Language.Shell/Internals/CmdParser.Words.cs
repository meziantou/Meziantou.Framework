using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>Cmd words, variable references, trivia, and the shared token helpers.</summary>
internal sealed partial class CmdParser
{
    // ---- words ----

    private bool IsDelayedExpansionEnabled => _options.Dialect.HasFeature(ShellDialectFeatures.DelayedExpansion);

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
    /// Reads the value of a <c>set</c> statement up to <paramref name="end"/>. Unlike an ordinary argument it keeps
    /// its spaces, so parentheses and other metacharacters inside <c>set /a "x=(1+2)*3"</c> stay part of the value.
    /// </summary>
    private ShellWordSyntax? ParseSetValue(int end)
    {
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;
        var inQuotes = false;

        while (_position < end)
        {
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
                '^' when !inQuotes => ParseEscapeSequence(trivia, fullStart),
                '%' => ParsePercentReference(trivia, fullStart, end),
                '!' when IsDelayedExpansionEnabled && IsDelayedExpansion(inQuotes, end) => ParseDelayedReference(trivia, fullStart),
                _ => ParseSetValueLiteralRun(trivia, fullStart, end, inQuotes),
            });
        }

        return parts.Count == 0 ? null : new ShellWordSyntax(ParserHelpers.List(parts));
    }

    private ShellLiteralWordPartSyntax ParseSetValueLiteralRun(GreenNode? leadingTrivia, int fullStart, int end, bool inQuotes)
    {
        var start = _position;
        while (_position < end && Current is not '"' and not '%' and not '!' && !(Current == '^' && !inQuotes))
        {
            _position++;
        }

        // A `!` that opens no delayed expansion is ordinary text, so it has to be taken into the run.
        if (_position == start)
        {
            _position++;
        }

        return new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.GenericToken, start, leadingTrivia, fullStart));
    }

    private ShellLiteralWordPartSyntax ParseLiteralRunUntil(int end, SyntaxKind kind)
    {
        var start = _position;
        while (_position < end && Current is not '%' and not '!')
        {
            _position++;
        }

        if (_position == start)
        {
            _position++;
        }

        return new ShellLiteralWordPartSyntax(CreateToken(kind, start, null, start));
    }

    private ShellWordPartSyntax ParseWordPart(GreenNode? leadingTrivia, int fullStart)
    {
        return Current switch
        {
            '"' => ParseQuotedString(leadingTrivia, fullStart),
            '^' => ParseEscapeSequence(leadingTrivia, fullStart),
            '%' => ParsePercentReference(leadingTrivia, fullStart),
            '!' when IsDelayedExpansionEnabled && IsDelayedExpansion(inQuotes: false) => ParseDelayedReference(leadingTrivia, fullStart),
            '*' or '?' => ParseGlob(leadingTrivia, fullStart),
            _ => ParseLiteralRun(leadingTrivia, fullStart),
        };
    }

    /// <summary>
    /// Returns whether the <c>!</c> at the current position opens a delayed expansion. Delayed expansion runs after
    /// cmd has split the line, so the closing <c>!</c> cannot lie past a quote or, outside quotes, an operator:
    /// <c>echo Done! &amp; echo ok!</c> is two commands.
    /// </summary>
    private bool IsDelayedExpansion(bool inQuotes, int limit = int.MaxValue)
    {
        limit = Math.Min(limit, _text.Length);
        var scan = _position + 1;
        while (scan < limit && _text[scan] != '!' && GetLineBreakLength(scan) == 0)
        {
            var value = _text[scan];
            if (value == '"' || (!inQuotes && (value is '&' or '|' or '<' or '>' || (value == ')' && _stopAtCloseParen))))
                return false;

            scan++;
        }

        return scan < limit && _text[scan] == '!' && scan > _position + 1;
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
        _position = start + MeasureEscapeSequence(start);

        string value;
        if (_position == start + 1)
        {
            // A caret at the end of the text escapes nothing; one before a percent expansion escapes a character the
            // expansion only produces when the line runs.
            value = start + 1 >= _text.Length ? "^" : string.Empty;
        }
        else if (GetLineBreakLength(start + 1) is var lineBreakLength and > 0)
        {
            // A caret escaping a line break joins the two lines and escapes the first character of the second, so
            // `echo a^` followed by `&b` echoes `a&b`, and an empty second line makes the escaped character a line feed.
            var next = start + 1 + lineBreakLength;
            value = _position == next ? string.Empty : GetLineBreakLength(next) > 0 ? "\n" : _text[next].ToString();
        }
        else
        {
            value = _text[start + 1].ToString();
        }

        return new ShellEscapeSequenceSyntax(CreateToken(SyntaxKind.EscapeToken, start, leadingTrivia, fullStart, value));
    }

    /// <summary>Returns the length of the escape sequence whose caret is at <paramref name="position"/>.</summary>
    private int MeasureEscapeSequence(int position)
    {
        var next = position + 1;
        if (next >= _text.Length)
            return 1;

        var lineBreakLength = GetLineBreakLength(next);
        if (lineBreakLength > 0)
        {
            var afterLineBreak = next + lineBreakLength;
            if (afterLineBreak < _text.Length)
            {
                if (GetLineBreakLength(afterLineBreak) is var secondLineBreakLength and > 0)
                    return afterLineBreak + secondLineBreakLength - position;

                // Only the characters whose meaning the escape changes are taken; a letter or a space reads the same
                // either way, and leaving it alone keeps `msbuild ^` followed by an indented line two arguments.
                if (_text[afterLineBreak] is '&' or '|' or '<' or '>' or '(' or ')' or '"' or '^')
                    return afterLineBreak + 1 - position;
            }

            return afterLineBreak - position;
        }

        // Percent expansion happens before carets are processed, so `^%PATH%` does not escape the percent sign.
        return _text[next] == '%' ? 1 : 2;
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
                '!' when IsDelayedExpansionEnabled && IsDelayedExpansion(inQuotes: true) => ParseDelayedReference(null, _position),
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
    /// A lone <c>%</c> that closes nothing, or whose reference would run past <paramref name="limit"/>, stays literal
    /// text.
    /// </summary>
    private ShellWordPartSyntax ParsePercentReference(GreenNode? leadingTrivia, int fullStart, int limit = int.MaxValue)
    {
        var start = _position;
        var length = MeasurePercentReference(start);
        if (length == 1 || start + length > limit)
        {
            _position++;

            return new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.GenericToken, start, leadingTrivia, fullStart));
        }

        var end = start + length;

        // `%%` that names nothing is an escaped literal percent.
        if (Peek(1) == '%' && length == 2)
        {
            _position = end;

            return new ShellEscapeSequenceSyntax(CreateToken(SyntaxKind.EscapeToken, start, leadingTrivia, fullStart, "%"));
        }

        // `%%i` is a for-loop variable inside a batch file, and `%1`, `%*`, and `%~dp0` have no closing percent.
        if (Peek(1) == '%' || char.IsAsciiDigit(Peek(1)) || Peek(1) is '*' or '~')
        {
            _position += Peek(1) == '%' ? 2 : 1;
            var openToken = CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart);
            var nameStart = _position;
            _position = end;
            var nameToken = CreateToken(SyntaxKind.VariableNameToken, nameStart, null, nameStart);

            return new CmdVariableReferenceSyntax(openToken, nameToken, closeToken: null);
        }

        _position++;
        var percentOpenToken = CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart);
        var variableNameStart = _position;
        _position = end - 1;
        var variableNameToken = CreateToken(SyntaxKind.VariableNameToken, variableNameStart, null, variableNameStart);
        var closeStart = _position;
        _position++;

        return new CmdVariableReferenceSyntax(percentOpenToken, variableNameToken, CreateToken(SyntaxKind.BareTextToken, closeStart, null, closeStart));
    }

    /// <summary>
    /// Returns how many characters the percent sign at <paramref name="position"/> starts: the whole reference, or 1
    /// for a lone percent. Percent expansion runs before the line is split, so a reference may contain spaces and
    /// operators, and every scan that looks for the end of a statement has to step over it the same way.
    /// </summary>
    private int MeasurePercentReference(int position)
    {
        var next = At(position + 1);
        if (next == '%')
        {
            var afterPercents = At(position + 2);

            return !IsNameCharacter(afterPercents) && afterPercents != '~' ? 2 : GetArgumentSelectorEnd(position + 2) - position;
        }

        if (char.IsAsciiDigit(next) || next is '*' or '~')
            return GetArgumentSelectorEnd(position + 1) - position;

        var closingIndex = FindClosing(position, '%');

        return closingIndex < 0 ? 1 : closingIndex + 1 - position;
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

    private int GetArgumentSelectorEnd(int position)
    {
        var scan = position;
        if (At(scan) == '~')
        {
            scan++;
            while (char.IsAsciiLetter(At(scan)) || At(scan) == '$')
            {
                scan++;
            }

            if (At(scan) == ':')
            {
                scan++;
            }
        }

        if (char.IsAsciiLetterOrDigit(At(scan)) || At(scan) == '*')
        {
            scan++;
        }

        return scan;
    }

    private int FindClosing(int position, char terminator)
    {
        var scan = position + 1;
        while (scan < _text.Length && GetLineBreakLength(scan) == 0)
        {
            if (_text[scan] == terminator)
                return scan;

            scan++;
        }

        return -1;
    }

    // ---- set statements ----

    /// <summary>
    /// Returns where the <c>set</c> statement starting at <paramref name="position"/> ends: at the line end, or at an
    /// <c>&amp;</c>, <c>|</c>, or closing parenthesis that no quote or caret protects.
    /// </summary>
    private int FindSetStatementEnd(int position)
    {
        var inQuotes = false;
        var scan = position;
        while (scan < _text.Length && GetLineBreakLength(scan) == 0)
        {
            var value = _text[scan];
            if (value == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (value == '%')
            {
                scan += MeasurePercentReference(scan);
                continue;
            }
            else if (!inQuotes)
            {
                if (value is '&' or '|' || (value == ')' && _stopAtCloseParen))
                    break;

                if (value == '^')
                {
                    scan += MeasureEscapeSequence(scan);
                    continue;
                }
            }

            scan++;
        }

        return Math.Min(scan, _text.Length);
    }

    /// <summary>
    /// Returns where the redirections at the end of a <c>set</c> statement start, or <paramref name="end"/> when it
    /// has none. <c>set /p V=&lt;file</c> reads <c>file</c>, but a redirection followed by more text is left in the
    /// value, because the node keeps its redirections after the value.
    /// </summary>
    private int FindTrailingRedirections(int start, int end)
    {
        var inQuotes = false;
        var scan = start;
        while (scan < end)
        {
            var value = _text[scan];
            if (value == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (value == '%')
            {
                scan += MeasurePercentReference(scan);
                continue;
            }
            else if (!inQuotes && value == '^')
            {
                scan += MeasureEscapeSequence(scan);
                continue;
            }
            else if (!inQuotes && value is '<' or '>')
            {
                var mismatch = FindRedirectionTailMismatch(scan, end);
                if (mismatch < 0)
                {
                    // A single digit before the operator names the handle, as in `2>nul`, when it starts a token.
                    var hasHandle = scan > start && char.IsAsciiDigit(_text[scan - 1]) && (scan - 1 == start || _text[scan - 2] is ' ' or '\t');

                    return hasHandle ? scan - 1 : scan;
                }

                // Every operator up to the mismatch belongs to the same failed chain, so resuming there keeps the scan
                // linear.
                scan = mismatch;
                continue;
            }

            scan++;
        }

        return end;
    }

    /// <summary>
    /// Returns -1 when only redirections follow the operator at <paramref name="operatorPosition"/> up to
    /// <paramref name="end"/>, or otherwise the position of the first text that is not part of a redirection.
    /// </summary>
    private int FindRedirectionTailMismatch(int operatorPosition, int end)
    {
        var scan = operatorPosition;
        while (true)
        {
            scan += _text[scan] == '>' && At(scan + 1) is '>' or '&' ? 2 : 1;
            scan = SkipInlineWhitespace(scan);

            while (scan < end && _text[scan] is not (' ' or '\t' or '<' or '>'))
            {
                scan += _text[scan] switch
                {
                    '"' => FindClosing(scan, '"') is var closing and >= 0 ? closing + 1 - scan : end - scan,
                    '^' => MeasureEscapeSequence(scan),
                    '%' => MeasurePercentReference(scan),
                    _ => 1,
                };
            }

            scan = SkipInlineWhitespace(scan);
            if (scan >= end)
                return -1;

            var tokenStart = scan;
            while (scan < end && char.IsAsciiDigit(_text[scan]))
            {
                scan++;
            }

            if (scan >= end || _text[scan] is not ('<' or '>'))
                return tokenStart;
        }
    }

    private int TrimTrailingWhitespace(int start, int end)
    {
        while (end > start && _text[end - 1] is ' ' or '\t')
        {
            end--;
        }

        return end;
    }

    // ---- trivia and tokens ----

    private void AccumulateInlineTrivia() => AccumulateTrivia(includeLineBreaks: false);

    private void AccumulateStatementTrivia() => AccumulateTrivia(includeLineBreaks: true);

    /// <summary>
    /// Accumulates the trivia in front of a command that an operator or a keyword asks for. Outside a block the line
    /// ends at its line break, so the command has to start on the same line; inside a block cmd keeps reading.
    /// </summary>
    private void AccumulateCommandTrivia() => AccumulateTrivia(includeLineBreaks: _blockDepth > 0);

    private void AccumulateTrivia(bool includeLineBreaks)
    {
        if (_pendingTrivia.Count == 0)
        {
            _pendingTriviaStart = _position;
        }

        while (!IsAtEnd)
        {
            var start = _position;

            if (Current is ' ' or '\t' || (_splitOnTokenDelimiters && Current is ',' or ';' or '='))
            {
                while (!IsAtEnd && (Current is ' ' or '\t' || (_splitOnTokenDelimiters && Current is ',' or ';' or '=')))
                {
                    _position++;
                }

                _pendingTrivia.Add(GreenFactory.Trivia(SyntaxKind.WhitespaceTrivia, _text[start.._position]));
                continue;
            }

            // A caret immediately before a line break joins two physical lines. When it also escapes a character
            // that matters, such as the `&` of `echo a ^` followed by `& b`, the word parser has to read it instead.
            if (Current == '^' && GetLineBreakLength(_position + 1) is var lineBreakLength and > 0 && MeasureEscapeSequence(_position) == 1 + lineBreakLength)
            {
                _position += 1 + lineBreakLength;
                _pendingTrivia.Add(GreenFactory.Trivia(SyntaxKind.LineContinuationTrivia, _text[start.._position]));
                continue;
            }

            if (Current == ':' && Peek(1) == ':' && !_inForSet && IsAtStatementStart(start))
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
                var lineBreak = GetLineBreakLength(_position);
                if (lineBreak > 0)
                {
                    _position += lineBreak;
                    _pendingTrivia.Add(GreenFactory.Trivia(SyntaxKind.EndOfLineTrivia, _text[start.._position]));
                    continue;
                }
            }

            break;
        }
    }

    /// <summary>
    /// A <c>REM</c> comment runs to the end of the line and must be followed by a token delimiter. A leading <c>@</c>
    /// only suppresses echoing, so <c>@rem</c> is a comment too.
    /// </summary>
    private bool IsRemComment()
    {
        if (_inForSet || !IsAtStatementStart(_position))
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

        return At(scan + 3) is '\0' or ' ' or '\t' or '\r' or '\n' or ',' or ';' or '=';
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

    /// <summary>Reads a keyword, including the <c>@</c> in front of it, whose value is the keyword alone.</summary>
    private ScannedToken ReadKeyword(int prefixLength, int keywordLength)
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _position;
        _position = Math.Min(_position + prefixLength + keywordLength, _text.Length);

        return CreateToken(SyntaxKind.KeywordToken, start, trivia, fullStart, _text[Math.Min(start + prefixLength, _position).._position]);
    }

    /// <summary>Returns the lowercased run of ASCII letters starting <paramref name="offset"/> characters ahead.</summary>
    private string? PeekLetters(int offset)
    {
        var start = _position + offset;
        if (start >= _text.Length || !char.IsAsciiLetter(_text[start]))
            return null;

        var scan = start;
        while (scan < _text.Length && char.IsAsciiLetter(_text[scan]))
        {
            scan++;
        }

        return _text[start..scan].ToLowerInvariant();
    }

    /// <summary>
    /// Returns the command keyword starting <paramref name="offset"/> characters ahead. cmd only recognizes one that
    /// ends its token, so <c>for_each.bat</c> and <c>goto2</c> run programs, while <c>set/a</c>, <c>goto:eof</c>, and
    /// <c>call:sub</c> are the keyword followed by its argument.
    /// </summary>
    private string? PeekCommandKeyword(int offset)
    {
        var word = PeekLetters(offset);
        if (word is null)
            return null;

        var next = At(_position + offset + word.Length);

        return word switch
        {
            "if" or "for" when IsTokenEnd(next) || next == '/' => word,
            "goto" or "call" or "set" when IsTokenEnd(next) || next is '/' or ':' or '.' or '\\' or '+' or '[' or ']' => word,
            "else" when IsTokenEnd(next) => word,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the lowercased word at the current position when it forms a whole token, as <c>in</c>, <c>do</c>,
    /// <c>not</c>, <c>else</c>, and the <c>if</c> operators must.
    /// </summary>
    private string? PeekClauseKeyword()
    {
        var word = PeekLetters(0);

        return word is not null && IsTokenEnd(At(_position + word.Length)) ? word : null;
    }

    private ScannedToken ExpectKeyword(string keyword)
    {
        AccumulateInlineTrivia();
        if (string.Equals(PeekClauseKeyword(), keyword, StringComparison.Ordinal))
            return ReadToken(SyntaxKind.KeywordToken, keyword.Length);

        AddDiagnostic(new TextSpan(_position, 0), "SHELL0012", $"Expected '{keyword}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(SyntaxKind.KeywordToken, fullStart, trivia);
    }

    private ScannedToken ExpectCharacter(char expected, SyntaxKind kind)
    {
        AccumulateInlineTrivia();
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
        // Once one piece is missing, the pieces expected after it are missing at the same position too; reporting the
        // first is enough to explain the rest.
        if (span.Length == 0 && _diagnostics.Count > 0 && _diagnostics[^1].Location.SourceSpan is { Length: 0 } previous && previous.Start == span.Start)
            return;

        _diagnostics.Add(new Diagnostic(id, message, DiagnosticSeverity.Error, new Location(span, _source)));
    }

    private int GetLineBreakLength(int position) => position < _text.Length ? SourceText.GetLineBreakLength(_text, position) : 0;

    private char At(int index) => index >= 0 && index < _text.Length ? _text[index] : '\0';

    /// <summary>
    /// Returns <see langword="true"/> when a <c>==</c> starts here and the caller asked to stop at one. Only the left
    /// operand of a cmd comparison does: <c>=</c> is an ordinary word character everywhere else, so <c>if a==b</c>
    /// would otherwise read as the single word <c>a==b</c>.
    /// </summary>
    private bool IsAtEqualityOperator() => _stopAtEquality && Current == '=' && Peek(1) == '=';

    /// <summary>
    /// Characters that end a word. <c>(</c> is not among them: it only opens a block at the start of a command, so
    /// <c>echo a(b</c> is a single word. <c>)</c> ends a word only inside a block or a <c>for</c> item list.
    /// </summary>
    private bool IsWordBoundary(char value) =>
        value is '\0' or ' ' or '\t' or '\r' or '\n' or '&' or '|' or '<' or '>'
        || (value == ')' && _stopAtCloseParen)
        || (_splitOnTokenDelimiters && value is ',' or ';' or '=');

    /// <summary>
    /// Characters that end the token a keyword forms: the token delimiters <c>space , ; = tab</c>, a line break, an
    /// operator, a parenthesis, or the end of the text.
    /// </summary>
    private static bool IsTokenEnd(char value) =>
        value is '\0' or ' ' or '\t' or ',' or ';' or '=' or '\v' or '\f' or '\r' or '\n' or '&' or '|' or '<' or '>' or '(' or ')';

    private static bool IsNameCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}
