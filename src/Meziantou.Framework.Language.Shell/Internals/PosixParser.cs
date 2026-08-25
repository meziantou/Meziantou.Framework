namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>Parser for the POSIX shell family (<c>sh</c>, <c>bash</c>, and <c>zsh</c>).</summary>
/// <remarks>
/// The parser never throws. Anything it cannot recognize is kept as <see cref="ShellSkippedTextSyntax"/> alongside a
/// diagnostic, so <c>ToFullString()</c> always reproduces the input exactly.
/// </remarks>
internal sealed partial class PosixParser
{
    private readonly PosixLexer _lexer;
    private readonly List<ShellDiagnostic> _diagnostics = [];
    private readonly ShellParseOptions _options;
    private readonly List<ShellSyntaxTrivia> _pendingTrivia = [];
    private readonly List<PendingHereDocument> _pendingHereDocuments = [];
    private int _pendingTriviaStart;
    private int _depth;
    private int _backtickDepth;

    public PosixParser(string text, ShellParseOptions options)
    {
        _options = options;
        _lexer = new PosixLexer(text, options.Dialect, _diagnostics);
    }

    public IReadOnlyList<ShellDiagnostic> Diagnostics => _diagnostics;

    public ShellScriptSyntax ParseScript()
    {
        var statements = ParseStatementList(ParseContext.TopLevel);
        var (trivia, fullStart) = TakeTrivia();
        var endOfFileToken = new ShellSyntaxToken(ShellSyntaxKind.EndOfFileToken, string.Empty, string.Empty, leadingTrivia: trivia, fullStart: fullStart);

        return new ShellScriptSyntax(statements, endOfFileToken, _lexer.Text);
    }

    // ---- statements ----

    private ShellStatementListSyntax ParseStatementList(ParseContext context)
    {
        var statements = new List<ShellStatementSyntax>();
        var separators = new List<ShellSyntaxToken>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || IsAtStop(context))
                break;

            var positionBeforeItem = _lexer.Position;

            if (_lexer.Current is ';' or '&' && !IsAndOrOperator() && !IsAtCaseTerminator())
            {
                var separator = ReadSeparatorToken();

                // The separator belongs to the statement in front of it, so pad first to land at the right index.
                while (separators.Count + 1 < statements.Count)
                {
                    separators.Add(MissingToken(ShellSyntaxKind.SemicolonToken, separator.FullSpan.Start));
                }

                if (separators.Count < statements.Count)
                {
                    separators.Add(separator);
                }
                else
                {
                    // A separator with nothing in front of it is not valid; keep it so the text still round-trips.
                    AddDiagnostic(separator.Span, "SHELL0002", $"Unexpected '{separator.Text}'.");
                    statements.Add(new ShellSkippedTextSyntax([separator], separator.FullSpan.Start));
                    separators.Add(MissingToken(ShellSyntaxKind.SemicolonToken, _lexer.Position));
                }

                continue;
            }

            // `SeparatorTokens[i]` follows `Statements[i]`, so a statement that a line break ended rather than a `;`
            // still needs a placeholder; without one the next `;` would be rebuilt against the wrong statement.
            while (separators.Count < statements.Count)
            {
                separators.Add(MissingToken(ShellSyntaxKind.SemicolonToken, _lexer.Position));
            }

            var statement = ParseAndOrList();
            statements.Add(statement);

            AccumulateInlineTrivia();
            if (!_lexer.IsAtEnd && _lexer.Current is ';' or '&' && !IsAndOrOperator() && !IsAtCaseTerminator())
            {
                separators.Add(ReadSeparatorToken());
            }

            // Any `<<` on the line just parsed takes its body from the lines that follow it.
            DrainHereDocuments(statements);

            if (_lexer.Position == positionBeforeItem)
            {
                // Nothing was consumed: force progress so a malformed script cannot spin forever.
                statements.Add(ConsumeUnexpectedCharacter());
            }
        }

        return new ShellStatementListSyntax(statements, separators);
    }

    private ShellStatementSyntax ParseAndOrList()
    {
        var first = ParsePipeline();
        List<ShellStatementSyntax>? pipelines = null;
        List<ShellSyntaxToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            var kind = _lexer.Current switch
            {
                '&' when _lexer.Peek(1) == '&' => ShellSyntaxKind.AmpersandAmpersandToken,
                '|' when _lexer.Peek(1) == '|' => ShellSyntaxKind.PipePipeToken,
                _ => ShellSyntaxKind.None,
            };

            if (kind == ShellSyntaxKind.None)
                break;

            pipelines ??= [first];
            operators ??= [];
            operators.Add(ReadOperatorToken(kind, length: 2));

            // A line break is allowed between the operator and the next pipeline.
            AccumulateStatementTrivia();
            pipelines.Add(ParsePipeline());
        }

        if (pipelines is null)
            return first;

        return new ShellCommandListSyntax(pipelines, operators);
    }

    private ShellStatementSyntax ParsePipeline()
    {
        AccumulateInlineTrivia();

        ShellSyntaxToken? bangToken = null;
        if (_lexer.Current == '!' && (PosixLexer.IsWordBoundary(_lexer.Peek(1)) || _lexer.Peek(1) == '\0'))
        {
            bangToken = ReadOperatorToken(ShellSyntaxKind.ExclamationToken, length: 1);
            AccumulateInlineTrivia();
        }

        var first = ParseCommandOrCompound();
        List<ShellStatementSyntax>? commands = null;
        List<ShellSyntaxToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current != '|' || _lexer.Peek(1) == '|')
                break;

            var isPipeAmpersand = _lexer.Peek(1) == '&';
            commands ??= [first];
            operators ??= [];
            operators.Add(isPipeAmpersand
                ? ReadOperatorToken(ShellSyntaxKind.PipeAmpersandToken, length: 2)
                : ReadOperatorToken(ShellSyntaxKind.PipeToken, length: 1));

            AccumulateStatementTrivia();
            commands.Add(ParseCommandOrCompound());
        }

        if (commands is null && bangToken is null)
            return first;

        return new ShellPipelineSyntax(bangToken, commands ?? [first], operators ?? []);
    }

    private ShellStatementSyntax ParseSimpleCommand()
    {
        var elements = new List<ShellSyntaxNode>();
        var sawWord = false;

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.IsAtEnd)
                break;

            var current = _lexer.Current;
            if (SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) > 0)
                break;

            if (current is ';' or '|' or '(' or ')')
                break;

            if (current == '&' && !IsAmpersandRedirection())
                break;

            if (TryParseRedirection(out var redirection))
            {
                elements.Add(redirection);
                continue;
            }

            if (!sawWord && TryParseAssignment(out var assignment))
            {
                elements.Add(assignment);
                continue;
            }

            if (IsWordTerminator(current) && !IsAtProcessSubstitution())
                break;

            elements.Add(ParseWord());
            sawWord = true;
        }

        if (elements.Count == 0)
        {
            var (trivia, fullStart) = TakeTrivia();
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0001", "Expected a command.");

            return new ShellSkippedTextSyntax([MissingToken(ShellSyntaxKind.BareTextToken, fullStart, trivia)], fullStart);
        }

        return new ShellCommandSyntax(elements);
    }

    // ---- command parts ----

    private bool TryParseAssignment([NotNullWhen(true)] out ShellSyntaxNode? assignment)
    {
        assignment = null;
        if (!PosixLexer.IsNameStart(_lexer.Current))
            return false;

        var scan = _lexer.Position;
        while (scan < _lexer.Text.Length && PosixLexer.IsNameCharacter(_lexer.Text[scan]))
        {
            scan++;
        }

        if (scan >= _lexer.Text.Length || _lexer.Text[scan] != '=' || scan == _lexer.Position)
            return false;

        var (trivia, fullStart) = TakeTrivia();
        var nameStart = _lexer.Position;
        _lexer.Position = scan;
        var nameToken = _lexer.CreateToken(ShellSyntaxKind.VariableNameToken, nameStart, trivia, fullStart);

        var equalsStart = _lexer.Position;
        _lexer.Position++;
        var equalsToken = _lexer.CreateToken(ShellSyntaxKind.EqualsToken, equalsStart, [], equalsStart);

        if (_lexer.Current == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.Arrays))
        {
            assignment = ParseArrayAssignment(nameToken, equalsToken);

            return true;
        }

        // The value binds tightly: `FOO= bar` assigns an empty value and runs `bar`.
        var value = _lexer.IsAtEnd || PosixLexer.IsWordBoundary(_lexer.Current) ? null : ParseWord();
        assignment = new ShellAssignmentSyntax(nameToken, equalsToken, value);

        return true;
    }

    private bool TryParseRedirection([NotNullWhen(true)] out ShellRedirectionSyntax? redirection)
    {
        redirection = null;

        var scan = _lexer.Position;
        while (scan < _lexer.Text.Length && char.IsAsciiDigit(_lexer.Text[scan]))
        {
            scan++;
        }

        var hasIoNumber = scan > _lexer.Position;
        var operatorStart = scan;
        if (IsProcessSubstitutionAt(operatorStart))
            return false;

        var (kind, length) = ReadRedirectionOperatorKind(operatorStart);
        if (kind == ShellSyntaxKind.None)
            return false;

        var (trivia, fullStart) = TakeTrivia();

        ShellSyntaxToken? ioNumberToken = null;
        if (hasIoNumber)
        {
            var ioStart = _lexer.Position;
            _lexer.Position = scan;
            ioNumberToken = _lexer.CreateToken(ShellSyntaxKind.IoNumberToken, ioStart, trivia, fullStart);
            trivia = [];
            fullStart = _lexer.Position;
        }

        var tokenStart = _lexer.Position;
        _lexer.Position += length;
        var operatorToken = _lexer.CreateToken(kind, tokenStart, trivia, fullStart);

        AccumulateInlineTrivia();
        ShellWordSyntax? target = null;
        if (!_lexer.IsAtEnd && !PosixLexer.IsWordBoundary(_lexer.Current))
        {
            target = ParseWord();
        }
        else
        {
            AddDiagnostic(operatorToken.Span, "SHELL0004", $"Expected a target after '{operatorToken.Text}'.");
        }

        redirection = new ShellRedirectionSyntax(ioNumberToken, operatorToken, target);
        if (kind is ShellSyntaxKind.LessThanLessThanToken or ShellSyntaxKind.LessThanLessThanDashToken)
        {
            _pendingHereDocuments.Add(new PendingHereDocument(redirection, target?.Value ?? string.Empty));
        }

        return true;
    }

    private (ShellSyntaxKind Kind, int Length) ReadRedirectionOperatorKind(int position)
    {
        var text = _lexer.Text;
        char At(int offset) => position + offset < text.Length ? text[position + offset] : '\0';

        return (At(0), At(1), At(2)) switch
        {
            ('<', '<', '<') when _options.Dialect.HasFeature(ShellDialectFeatures.HereString) => (ShellSyntaxKind.LessThanLessThanLessThanToken, 3),
            ('<', '<', '-') => (ShellSyntaxKind.LessThanLessThanDashToken, 3),
            ('<', '<', _) => (ShellSyntaxKind.LessThanLessThanToken, 2),
            ('<', '&', _) => (ShellSyntaxKind.LessThanAmpersandToken, 2),
            ('<', '>', _) => (ShellSyntaxKind.LessThanGreaterThanToken, 2),
            ('<', _, _) => (ShellSyntaxKind.LessThanToken, 1),
            ('>', '>', _) => (ShellSyntaxKind.GreaterThanGreaterThanToken, 2),
            ('>', '&', _) => (ShellSyntaxKind.GreaterThanAmpersandToken, 2),
            ('>', '|', _) => (ShellSyntaxKind.GreaterThanPipeToken, 2),
            ('>', _, _) => (ShellSyntaxKind.GreaterThanToken, 1),
            ('&', '>', '>') => (ShellSyntaxKind.AmpersandGreaterThanGreaterThanToken, 3),
            ('&', '>', _) => (ShellSyntaxKind.AmpersandGreaterThanToken, 2),
            _ => (ShellSyntaxKind.None, 0),
        };
    }

    private bool IsAmpersandRedirection() => _lexer.Current == '&' && _lexer.Peek(1) == '>';

    private bool IsAndOrOperator() =>
        (_lexer.Current == '&' && _lexer.Peek(1) == '&') || (_lexer.Current == '|' && _lexer.Peek(1) == '|');

    private ShellSyntaxToken ReadSeparatorToken()
    {
        var kind = _lexer.Current == ';' ? ShellSyntaxKind.SemicolonToken : ShellSyntaxKind.AmpersandToken;

        return ReadOperatorToken(kind, length: 1);
    }

    private ShellSyntaxToken ReadOperatorToken(ShellSyntaxKind kind, int length)
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _lexer.Position;
        _lexer.Position = Math.Min(_lexer.Position + length, _lexer.Text.Length);

        return _lexer.CreateToken(kind, start, trivia, fullStart);
    }

    private ShellSkippedTextSyntax ConsumeUnexpectedCharacter()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _lexer.Position;
        _lexer.Position = Math.Min(_lexer.Position + 1, _lexer.Text.Length);
        var token = _lexer.CreateToken(ShellSyntaxKind.BadToken, start, trivia, fullStart);
        AddDiagnostic(token.Span, "SHELL0002", $"Unexpected '{token.Text}'.");

        return new ShellSkippedTextSyntax([token], fullStart);
    }

    // ---- words ----

    private ShellWordSyntax ParseWord()
    {
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;

        while (!_lexer.IsAtEnd && (!IsWordTerminator(_lexer.Current) || IsAtProcessSubstitution()))
        {
            var (trivia, fullStart) = isFirst ? TakeTrivia() : ([], _lexer.Position);
            isFirst = false;

            var positionBefore = _lexer.Position;
            parts.Add(ParseWordPart(trivia, fullStart));
            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }
        }

        return new ShellWordSyntax(parts);
    }

    private ShellWordPartSyntax ParseWordPart(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        if (IsAtProcessSubstitution())
            return ParseProcessSubstitution(leadingTrivia, fullStart);

        return _lexer.Current switch
        {
            '\'' => ParseSingleQuotedString(leadingTrivia, fullStart),
            '"' => ParseDoubleQuotedString(leadingTrivia, fullStart),
            '`' => ParseBackquoteSubstitution(leadingTrivia, fullStart),
            '$' => ParseDollarPart(leadingTrivia, fullStart),
            '\\' => ParseEscapeSequence(leadingTrivia, fullStart),
            '*' or '?' => ParseGlob(leadingTrivia, fullStart),
            '[' when FindBracketExpressionEnd() > 0 => ParseBracketExpression(leadingTrivia, fullStart),
            _ => ParseLiteralRun(leadingTrivia, fullStart),
        };
    }

    private ShellLiteralWordPartSyntax ParseLiteralRun(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        while (!_lexer.IsAtEnd && !IsWordTerminator(_lexer.Current) && !IsWordPartStart(_lexer.Current))
        {
            _lexer.Position++;
        }

        if (_lexer.Position == start)
        {
            _lexer.Position++;
        }

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, start, leadingTrivia, fullStart));
    }

    private static bool IsWordPartStart(char value) => value is '\'' or '"' or '`' or '$' or '\\' or '*' or '?' or '[';

    private ShellGlobSyntax ParseGlob(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        ShellSyntaxKind kind;
        if (_lexer.Current == '*' && _lexer.Peek(1) == '*')
        {
            // `**` matches across directory separators, so it is one glob rather than two.
            kind = ShellSyntaxKind.AsteriskAsteriskToken;
            _lexer.Position += 2;
        }
        else
        {
            kind = _lexer.Current == '*' ? ShellSyntaxKind.AsteriskToken : ShellSyntaxKind.QuestionToken;
            _lexer.Position++;
        }

        return new ShellGlobSyntax(_lexer.CreateToken(kind, start, leadingTrivia, fullStart));
    }

    private ShellGlobSyntax ParseBracketExpression(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position = FindBracketExpressionEnd();

        return new ShellGlobSyntax(_lexer.CreateToken(ShellSyntaxKind.BracketExpressionToken, start, leadingTrivia, fullStart));
    }

    /// <summary>
    /// Returns the position just past a bracket expression such as <c>[abc]</c>, or -1 when the <c>[</c> is not one.
    /// A lone <c>[</c> is the name of the <c>test</c> command, so a closing bracket has to appear in the same word.
    /// </summary>
    private int FindBracketExpressionEnd()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position + 1;

        // A leading `!` or `^` negates the set, and a `]` right after it is a literal member.
        if (scan < text.Length && text[scan] is '!' or '^')
        {
            scan++;
        }

        if (scan < text.Length && text[scan] == ']')
        {
            scan++;
        }

        while (scan < text.Length && text[scan] != ']')
        {
            if (PosixLexer.IsWordBoundary(text[scan]))
                return -1;

            scan++;
        }

        return scan < text.Length && text[scan] == ']' && scan > _lexer.Position + 1 ? scan + 1 : -1;
    }

    /// <summary>
    /// Reads a backslash escape. Unquoted, a backslash escapes any character. Inside double quotes it is only
    /// special before <c>$</c>, <c>`</c>, <c>"</c>, <c>\</c>, and a line break; anywhere else it stays literal,
    /// so <c>"a\qb"</c> really is <c>a\qb</c>.
    /// </summary>
    private ShellEscapeSequenceSyntax ParseEscapeSequence(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart, bool inDoubleQuotes = false)
    {
        var start = _lexer.Position;
        _lexer.Position++;
        string value;
        if (_lexer.IsAtEnd)
        {
            value = "\\";
        }
        else if (SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) is var lineBreak && lineBreak > 0)
        {
            // A backslash before a line break joins the two lines and contributes nothing to the word.
            _lexer.Position += lineBreak;
            value = string.Empty;
        }
        else if (inDoubleQuotes && _lexer.Current is not '$' and not '`' and not '"' and not '\\')
        {
            value = "\\" + _lexer.Current;
            _lexer.Position++;
        }
        else
        {
            value = _lexer.Current.ToString();
            _lexer.Position++;
        }

        return new ShellEscapeSequenceSyntax(_lexer.CreateToken(ShellSyntaxKind.EscapeToken, start, leadingTrivia, fullStart, value));
    }

    private ShellQuotedStringSyntax ParseSingleQuotedString(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var quoteStart = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(ShellSyntaxKind.SingleQuoteToken, quoteStart, leadingTrivia, fullStart);

        var contentStart = _lexer.Position;
        while (!_lexer.IsAtEnd && _lexer.Current != '\'')
        {
            _lexer.Position++;
        }

        var content = _lexer.Position > contentStart
            ? new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, contentStart, [], contentStart))
            : null;

        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated single-quoted string.");
            closeToken = MissingToken(ShellSyntaxKind.SingleQuoteToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.SingleQuoteToken, closeStart, [], closeStart);
        }

        return new ShellQuotedStringSyntax(openToken, content is null ? [] : [content], closeToken);
    }

    /// <summary>
    /// Reads the bash <c>$'...'</c> form. Unlike a plain single-quoted string it resolves ANSI-C escapes, so
    /// <c>$'a\tb'</c> holds a real tab.
    /// </summary>
    private ShellQuotedStringSyntax ParseAnsiCQuotedString(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var quoteStart = _lexer.Position;
        _lexer.Position += 2;
        var openToken = _lexer.CreateToken(ShellSyntaxKind.DollarSingleQuoteToken, quoteStart, leadingTrivia, fullStart);

        var contentStart = _lexer.Position;
        var value = new StringBuilder();
        var terminated = false;
        while (!_lexer.IsAtEnd)
        {
            if (_lexer.Current == '\'')
            {
                terminated = true;
                break;
            }

            if (_lexer.Current == '\\' && _lexer.Position + 1 < _lexer.Text.Length)
            {
                _lexer.Position++;
                AppendAnsiCEscape(value);
                continue;
            }

            value.Append(_lexer.Current);
            _lexer.Position++;
        }

        var contentEnd = _lexer.Position;
        ShellWordPartSyntax? content = contentEnd > contentStart
            ? new ShellLiteralWordPartSyntax(new ShellSyntaxToken(
                ShellSyntaxKind.BareTextToken,
                _lexer.Text[contentStart..contentEnd],
                value.ToString(),
                fullStart: contentStart))
            : null;

        ShellSyntaxToken closeToken;
        if (!terminated)
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated quoted string.");
            closeToken = MissingToken(ShellSyntaxKind.SingleQuoteToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.SingleQuoteToken, closeStart, [], closeStart);
        }

        return new ShellQuotedStringSyntax(openToken, content is null ? [] : [content], closeToken);
    }

    /// <summary>Appends the character named by the ANSI-C escape at the current position, then consumes it.</summary>
    private void AppendAnsiCEscape(StringBuilder value)
    {
        var escape = _lexer.Current;
        _lexer.Position++;

        switch (escape)
        {
            case 'a': value.Append('\a'); return;
            case 'b': value.Append('\b'); return;
            case 'e' or 'E': value.Append('\u001b'); return;
            case 'f': value.Append('\f'); return;
            case 'n': value.Append('\n'); return;
            case 'r': value.Append('\r'); return;
            case 't': value.Append('\t'); return;
            case 'v': value.Append('\v'); return;
            case '\\' or '\'' or '"' or '?': value.Append(escape); return;

            case 'x':
                AppendNumericEscape(value, 16, maxDigits: 2);
                return;

            case 'u':
                AppendNumericEscape(value, 16, maxDigits: 4);
                return;

            case 'U':
                AppendNumericEscape(value, 16, maxDigits: 8);
                return;

            case >= '0' and <= '7':
                _lexer.Position--;
                AppendNumericEscape(value, 8, maxDigits: 3);
                return;

            default:
                // An unknown escape keeps both characters, as bash does.
                value.Append('\\').Append(escape);
                return;
        }
    }

    private void AppendNumericEscape(StringBuilder value, int radix, int maxDigits)
    {
        var result = 0;
        var digits = 0;
        while (digits < maxDigits && !_lexer.IsAtEnd)
        {
            var digit = GetDigitValue(_lexer.Current, radix);
            if (digit < 0)
                break;

            result = (result * radix) + digit;
            digits++;
            _lexer.Position++;
        }

        if (digits == 0)
        {
            value.Append(radix == 16 ? 'x' : '0');
            return;
        }

        value.Append(char.ConvertFromUtf32(Math.Clamp(result, 0, 0x10FFFF)));
    }

    private static int GetDigitValue(char value, int radix)
    {
        var digit = value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            >= 'A' and <= 'F' => value - 'A' + 10,
            _ => -1,
        };

        return digit >= 0 && digit < radix ? digit : -1;
    }

    private ShellQuotedStringSyntax ParseDoubleQuotedString(
        IReadOnlyList<ShellSyntaxTrivia> leadingTrivia,
        int fullStart,
        ShellSyntaxKind openKind = ShellSyntaxKind.DoubleQuoteToken)
    {
        var quoteStart = _lexer.Position;
        _lexer.Position += openKind == ShellSyntaxKind.DollarDoubleQuoteToken ? 2 : 1;
        var openToken = _lexer.CreateToken(openKind, quoteStart, leadingTrivia, fullStart);

        var parts = new List<ShellWordPartSyntax>();
        while (!_lexer.IsAtEnd && _lexer.Current != '"')
        {
            var positionBefore = _lexer.Position;
            parts.Add(_lexer.Current switch
            {
                '\\' => ParseEscapeSequence([], _lexer.Position, inDoubleQuotes: true),
                '`' => ParseBackquoteSubstitution([], _lexer.Position),
                '$' => ParseDollarPart([], _lexer.Position),
                _ => ParseDoubleQuotedLiteral(),
            });

            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }
        }

        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated double-quoted string.");
            closeToken = MissingToken(ShellSyntaxKind.DoubleQuoteToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.DoubleQuoteToken, closeStart, [], closeStart);
        }

        return new ShellQuotedStringSyntax(openToken, parts, closeToken);
    }

    private ShellLiteralWordPartSyntax ParseDoubleQuotedLiteral()
    {
        var start = _lexer.Position;
        while (!_lexer.IsAtEnd && _lexer.Current is not '"' and not '\\' and not '`' and not '$')
        {
            _lexer.Position++;
        }

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, start, [], start));
    }

    private ShellWordPartSyntax ParseDollarPart(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var next = _lexer.Peek(1);

        if (next == '\'' && _options.Dialect.HasFeature(ShellDialectFeatures.DollarQuoting))
            return ParseAnsiCQuotedString(leadingTrivia, fullStart);

        if (next == '"' && _options.Dialect.HasFeature(ShellDialectFeatures.DollarQuoting))
            return ParseDoubleQuotedString(leadingTrivia, fullStart, ShellSyntaxKind.DollarDoubleQuoteToken);

        if (next == '(' && _lexer.Peek(2) == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.Arithmetic))
            return ParseArithmeticExpansion(leadingTrivia, fullStart);

        if (next == '(')
            return ParseCommandSubstitution(leadingTrivia, fullStart);

        if (next == '{')
            return ParseBracedVariableReference(leadingTrivia, fullStart);

        if (PosixLexer.IsNameStart(next) || PosixLexer.IsSpecialParameter(next))
            return ParseSimpleVariableReference(leadingTrivia, fullStart);

        // A bare '$' is literal text.
        var start = _lexer.Position;
        _lexer.Position++;

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, start, leadingTrivia, fullStart));
    }

    private ShellVariableReferenceSyntax ParseSimpleVariableReference(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var dollarStart = _lexer.Position;
        _lexer.Position++;
        var dollarToken = _lexer.CreateToken(ShellSyntaxKind.DollarToken, dollarStart, leadingTrivia, fullStart);

        var nameStart = _lexer.Position;
        if (PosixLexer.IsNameStart(_lexer.Current))
        {
            while (!_lexer.IsAtEnd && PosixLexer.IsNameCharacter(_lexer.Current))
            {
                _lexer.Position++;
            }
        }
        else
        {
            _lexer.Position++;
        }

        var nameToken = _lexer.CreateToken(ShellSyntaxKind.VariableNameToken, nameStart, [], nameStart);

        return new ShellVariableReferenceSyntax(dollarToken, openBraceToken: null, nameToken, closeBraceToken: null);
    }

    private ShellVariableReferenceSyntax ParseBracedVariableReference(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var dollarStart = _lexer.Position;
        _lexer.Position++;
        var dollarToken = _lexer.CreateToken(ShellSyntaxKind.DollarToken, dollarStart, leadingTrivia, fullStart);

        var braceStart = _lexer.Position;
        _lexer.Position++;
        var openBraceToken = _lexer.CreateToken(ShellSyntaxKind.OpenBraceToken, braceStart, [], braceStart);

        // The whole expansion body is kept as one token; `${var:-default}` round-trips without modeling operators.
        var nameStart = _lexer.Position;
        var depth = 0;
        while (!_lexer.IsAtEnd && (_lexer.Current != '}' || depth > 0))
        {
            // A `}` inside quotes, as in `${x:-"}"}`, does not close the expansion.
            if (_lexer.Current is '\'' or '"')
            {
                SkipQuotedSectionOrCharacter();
                continue;
            }

            if (_lexer.Current == '{')
            {
                depth++;
            }
            else if (_lexer.Current == '}')
            {
                depth--;
            }

            _lexer.Position++;
        }

        var nameToken = _lexer.CreateToken(ShellSyntaxKind.VariableNameToken, nameStart, [], nameStart);

        ShellSyntaxToken closeBraceToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openBraceToken.Span, "SHELL0005", "Unterminated parameter expansion.");
            closeBraceToken = MissingToken(ShellSyntaxKind.CloseBraceToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeBraceToken = _lexer.CreateToken(ShellSyntaxKind.CloseBraceToken, closeStart, [], closeStart);
        }

        return new ShellVariableReferenceSyntax(dollarToken, openBraceToken, nameToken, closeBraceToken);
    }

    private ShellWordPartSyntax ParseCommandSubstitution(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position += 2;
        var openToken = _lexer.CreateToken(ShellSyntaxKind.DollarOpenParenToken, start, leadingTrivia, fullStart);

        if (!TryEnterRecursion(openToken.Span))
            return new ShellLiteralWordPartSyntax(ConsumeRestAsText(openToken));

        var statements = ParseStatementList(ParseContext.UntilCharacter(')'));
        _depth--;

        var (trivia, closeFullStart) = TakeTrivia();
        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0006", "Unterminated command substitution.");
            closeToken = MissingToken(ShellSyntaxKind.CloseParenToken, closeFullStart, trivia);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.CloseParenToken, closeStart, trivia, closeFullStart);
        }

        return new ShellCommandSubstitutionSyntax(openToken, statements, closeToken);
    }

    private ShellWordPartSyntax ParseBackquoteSubstitution(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(ShellSyntaxKind.BacktickToken, start, leadingTrivia, fullStart);

        if (!TryEnterRecursion(openToken.Span))
            return new ShellLiteralWordPartSyntax(ConsumeRestAsText(openToken));

        _backtickDepth++;
        var statements = ParseStatementList(ParseContext.UntilCharacter('`'));
        _backtickDepth--;
        _depth--;

        var (trivia, closeFullStart) = TakeTrivia();
        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0006", "Unterminated command substitution.");
            closeToken = MissingToken(ShellSyntaxKind.BacktickToken, closeFullStart, trivia);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.BacktickToken, closeStart, trivia, closeFullStart);
        }

        return new ShellCommandSubstitutionSyntax(openToken, statements, closeToken);
    }

    private PosixArithmeticExpansionSyntax ParseArithmeticExpansion(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position += 3;
        var openToken = _lexer.CreateToken(ShellSyntaxKind.DollarOpenParenToken, start, leadingTrivia, fullStart);

        var expressionStart = _lexer.Position;
        var depth = 0;
        while (!_lexer.IsAtEnd && !(depth == 0 && _lexer.Current == ')' && _lexer.Peek(1) == ')'))
        {
            if (_lexer.Current == '(')
            {
                depth++;
            }
            else if (_lexer.Current == ')')
            {
                depth--;
            }

            _lexer.Position++;
        }

        var expressionToken = _lexer.CreateToken(ShellSyntaxKind.BareTextToken, expressionStart, [], expressionStart);

        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0007", "Unterminated arithmetic expansion.");
            closeToken = MissingToken(ShellSyntaxKind.CloseParenToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position += 2;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.CloseParenToken, closeStart, [], closeStart);
        }

        return new PosixArithmeticExpansionSyntax(openToken, expressionToken, closeToken);
    }

    // ---- helpers ----

    /// <summary>
    /// Returns whether <paramref name="value"/> ends a word here. Inside a backquoted substitution the closing
    /// backtick terminates the word rather than opening a nested substitution.
    /// </summary>
    private bool IsWordTerminator(char value) => PosixLexer.IsWordBoundary(value) || (_backtickDepth > 0 && value == '`');

    private bool IsAtStop(ParseContext context)
    {
        if (context.StopCharacter != '\0' && _lexer.Current == context.StopCharacter)
            return true;

        if (context.StopAtCaseTerminator && IsAtCaseTerminator())
            return true;

        return context.StopWords is { Length: > 0 } stopWords && PeekBareWord() is { } word && Array.IndexOf(stopWords, word) >= 0;
    }

    /// <summary>Returns <see langword="true"/> at <c>;;</c>, <c>;&amp;</c>, or <c>;;&amp;</c>, which only end a case clause.</summary>
    private bool IsAtCaseTerminator() => _lexer.Current == ';' && _lexer.Peek(1) is ';' or '&';

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

    /// <summary>Folds the remaining text into a single token so a too-deeply-nested script still round-trips.</summary>
    private ShellSyntaxToken ConsumeRestAsText(ShellSyntaxToken openToken)
    {
        var start = _lexer.Position;
        _lexer.Position = _lexer.Text.Length;
        var text = openToken.Text + _lexer.Text[start..];

        return new ShellSyntaxToken(ShellSyntaxKind.BadToken, text, text, leadingTrivia: openToken.LeadingTrivia, fullStart: openToken.FullSpan.Start);
    }

    private void AccumulateInlineTrivia()
    {
        if (_pendingTrivia.Count == 0)
        {
            _pendingTriviaStart = _lexer.Position;
        }

        _pendingTrivia.AddRange(_lexer.ReadInlineTrivia());
    }

    private void AccumulateStatementTrivia()
    {
        if (_pendingTrivia.Count == 0)
        {
            _pendingTriviaStart = _lexer.Position;
        }

        _pendingTrivia.AddRange(_lexer.ReadStatementTrivia());
    }

    private (IReadOnlyList<ShellSyntaxTrivia> Trivia, int FullStart) TakeTrivia()
    {
        if (_pendingTrivia.Count == 0)
            return ([], _lexer.Position);

        var trivia = _pendingTrivia.ToArray();
        var start = _pendingTriviaStart;
        _pendingTrivia.Clear();

        return (trivia, start);
    }

    private static ShellSyntaxToken MissingToken(ShellSyntaxKind kind, int position, IReadOnlyList<ShellSyntaxTrivia>? leadingTrivia = null)
    {
        return new ShellSyntaxToken(kind, string.Empty, string.Empty, isMissing: true, leadingTrivia: leadingTrivia, fullStart: position);
    }

    private void AddDiagnostic(TextSpan span, string id, string message)
    {
        _diagnostics.Add(new ShellDiagnostic(id, message, ShellDiagnosticSeverity.Error, span));
    }
}
