namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>Parser for the PowerShell family. Never throws; unrecognized text is kept as skipped text plus a diagnostic.</summary>
internal sealed partial class PowerShellParser
{
    private readonly PowerShellLexer _lexer;
    private readonly List<ShellDiagnostic> _diagnostics = [];
    private readonly ShellParseOptions _options;
    private readonly List<ShellSyntaxTrivia> _pendingTrivia = [];
    private int _pendingTriviaStart;
    private int _depth;

    public PowerShellParser(string text, ShellParseOptions options)
    {
        _options = options;
        _lexer = new PowerShellLexer(text, options.Dialect, _diagnostics);
    }

    public IReadOnlyList<ShellDiagnostic> Diagnostics => _diagnostics;

    public ShellScriptSyntax ParseScript()
    {
        var statements = ParseStatementList(stopCharacter: '\0');
        var (trivia, fullStart) = TakeTrivia();
        var endOfFileToken = new ShellSyntaxToken(ShellSyntaxKind.EndOfFileToken, string.Empty, string.Empty, leadingTrivia: trivia, fullStart: fullStart);

        return new ShellScriptSyntax(statements, endOfFileToken, _lexer.Text);
    }

    // ---- statements ----

    private ShellStatementListSyntax ParseStatementList(char stopCharacter)
    {
        var statements = new List<ShellStatementSyntax>();
        var separators = new List<ShellSyntaxToken>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || (stopCharacter != '\0' && _lexer.Current == stopCharacter))
                break;

            var positionBefore = _lexer.Position;

            // Only `;` separates statements. A leading `,` is the unary array operator, as in `$a = ,1`.
            if (_lexer.Current == ';')
            {
                var separator = ReadOperatorToken(ShellSyntaxKind.SemicolonToken, length: 1);

                // The separator belongs to the statement in front of it, so pad first to land at the right index.
                while (separators.Count + 1 < statements.Count)
                {
                    separators.Add(MissingToken(ShellSyntaxKind.SemicolonToken, separator.FullSpan.Start));
                }

                if (separators.Count == statements.Count)
                {
                    // An empty statement is legal, so `;; Get-Date` is not an error.
                    statements.Add(new ShellEmptyStatementSyntax(separator.FullSpan.Start));
                }

                separators.Add(separator);

                continue;
            }

            // `SeparatorTokens[i]` follows `Statements[i]`, so a statement that a line break ended rather than a `;`
            // still needs a placeholder; without one the next `;` would be rebuilt against the wrong statement.
            while (separators.Count < statements.Count)
            {
                separators.Add(MissingToken(ShellSyntaxKind.SemicolonToken, _lexer.Position));
            }

            statements.Add(ParseStatement());

            AccumulateInlineTrivia();
            if (!_lexer.IsAtEnd && _lexer.Current == ';')
            {
                separators.Add(ReadOperatorToken(ShellSyntaxKind.SemicolonToken, length: 1));
            }

            if (_lexer.Position == positionBefore)
            {
                statements.Add(ConsumeUnexpectedCharacter());
            }
        }

        return new ShellStatementListSyntax(statements, separators);
    }

    private ShellStatementSyntax ParseStatement()
    {
        AccumulateStatementTrivia();

        if (!TryEnterRecursion(new TextSpan(_lexer.Position, 0)))
            return ConsumeRestAsSkippedText();

        try
        {
            return ParseStatementCore();
        }
        finally
        {
            _depth--;
        }
    }

    private ShellStatementSyntax ParseStatementCore()
    {
        // `[Attribute()] param(...)` and `[Attribute()] class X {}` keep their attributes; a bare `[int]$x` is a cast.
        var keywordAfterAttributes = PeekKeywordAfterAttributes();
        if (keywordAfterAttributes is "param" or "class" or "enum")
        {
            var attributes = ParseAttributeList();

            return keywordAfterAttributes == "param" ? ParseParamBlock(attributes) : ParseTypeDefinition(attributes);
        }

        // A keyword only introduces its statement when what must follow it is actually there. Otherwise the word is
        // an ordinary command name, which is how PowerShell reads `param 1` or `clean -eq 2`.
        var keyword = PeekKeyword();
        switch (keyword)
        {
            case "if" when FollowedBy(keyword, '('):
                return ParseIfStatement();
            case "while" when FollowedBy(keyword, '('):
                return ParseWhileStatement();
            case "do" when FollowedBy(keyword, '{'):
                return ParseDoStatement();
            case "for" when FollowedBy(keyword, '('):
                return ParseForStatement();
            case "foreach" when FollowedBy(keyword, '('):
                return ParseForEachStatement();
            case "switch" when FollowedBy(keyword, '(', '-'):
                return ParseSwitchStatement();
            case "try" when FollowedBy(keyword, '{'):
                return ParseTryStatement();
            case "trap" when FollowedBy(keyword, '{', '['):
                return ParseTrapStatement();
            case "function" when FollowedByName(keyword):
                return ParseFunctionDefinition(ShellSyntaxKind.PowerShellFunctionDefinition);
            case "filter" when FollowedByName(keyword):
                return ParseFunctionDefinition(ShellSyntaxKind.PowerShellFilterDefinition);
            case "workflow" when FollowedByName(keyword):
                return ParseFunctionDefinition(ShellSyntaxKind.PowerShellWorkflowDefinition);
            case "class" or "enum" when FollowedByName(keyword):
                return ParseTypeDefinition([]);
            case "param" when FollowedBy(keyword, '('):
                return ParseParamBlock([]);
            case "begin" when FollowedBy(keyword, '{'):
                return ParseNamedBlock(ShellSyntaxKind.PowerShellBeginBlock);
            case "process" when FollowedBy(keyword, '{'):
                return ParseNamedBlock(ShellSyntaxKind.PowerShellProcessBlock);
            case "end" when FollowedBy(keyword, '{'):
                return ParseNamedBlock(ShellSyntaxKind.PowerShellEndBlock);
            case "clean" when _options.Dialect.HasFeature(ShellDialectFeatures.CleanBlock) && FollowedBy(keyword, '{'):
                return ParseNamedBlock(ShellSyntaxKind.PowerShellCleanBlock);
            case "dynamicparam" when FollowedBy(keyword, '{'):
                return ParseNamedBlock(ShellSyntaxKind.PowerShellDynamicParamBlock);
            case "data" when FollowedBy(keyword, '{') || FollowedByName(keyword):
                return ParseDataStatement();
            case "using" when FollowedByName(keyword):
                return ParseUsingStatement();
            case "break":
                return ParseFlowStatement(ShellSyntaxKind.PowerShellBreakStatement);
            case "continue":
                return ParseFlowStatement(ShellSyntaxKind.PowerShellContinueStatement);
            case "return":
                return ParseFlowStatement(ShellSyntaxKind.PowerShellReturnStatement);
            case "exit":
                return ParseFlowStatement(ShellSyntaxKind.PowerShellExitStatement);
            case "throw":
                return ParseFlowStatement(ShellSyntaxKind.PowerShellThrowStatement);
        }

        // A label only ever precedes a loop. `:name` followed by anything else is an ordinary command word.
        if (_lexer.Current == ':' && PowerShellLexer.IsNameStart(_lexer.Peek(1)) && IsLabelOnALoop())
            return ParseLabeledStatement();

        return ParseAndOrList();
    }

    /// <summary>Returns the first character after <paramref name="keyword"/> that is not whitespace or a line break.</summary>
    private char PeekAfterKeyword(string keyword)
    {
        var text = _lexer.Text;
        var scan = _lexer.Position + keyword.Length;
        while (scan < text.Length && (text[scan] is ' ' or '\t' || SourceText.GetLineBreakLength(text, scan) > 0))
        {
            scan += SourceText.GetLineBreakLength(text, scan) is var lineBreak && lineBreak > 0 ? lineBreak : 1;
        }

        return scan < text.Length ? text[scan] : '\0';
    }

    private bool FollowedBy(string? keyword, params ReadOnlySpan<char> expected)
    {
        if (keyword is null)
            return false;

        var next = PeekAfterKeyword(keyword);
        foreach (var candidate in expected)
        {
            if (next == candidate)
                return true;
        }

        return false;
    }

    private bool FollowedByName(string? keyword) => keyword is not null && PowerShellLexer.IsNameStart(PeekAfterKeyword(keyword));

    private ShellStatementSyntax ParseAndOrList()
    {
        var first = ParsePipeline();
        if (!_options.Dialect.HasFeature(ShellDialectFeatures.PipelineChainOperators))
            return first;

        List<ShellStatementSyntax>? pipelines = null;
        List<ShellSyntaxToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            var kind = (_lexer.Current, _lexer.Peek(1)) switch
            {
                ('&', '&') => ShellSyntaxKind.AmpersandAmpersandToken,
                ('|', '|') => ShellSyntaxKind.PipePipeToken,
                _ => ShellSyntaxKind.None,
            };

            if (kind == ShellSyntaxKind.None)
                break;

            pipelines ??= [first];
            operators ??= [];
            operators.Add(ReadOperatorToken(kind, length: 2));
            AccumulateStatementTrivia();
            pipelines.Add(ParsePipeline());
        }

        return pipelines is null ? first : new ShellCommandListSyntax(pipelines, operators);
    }

    private ShellStatementSyntax ParsePipeline() => ContinuePipeline(ParsePipelineElement());

    /// <summary>Continues a pipeline whose first element has already been read.</summary>
    private ShellStatementSyntax ContinuePipeline(ShellStatementSyntax first)
    {
        List<ShellStatementSyntax>? elements = null;
        List<ShellSyntaxToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current != '|' || _lexer.Peek(1) == '|')
                break;

            elements ??= [first];
            operators ??= [];
            operators.Add(ReadOperatorToken(ShellSyntaxKind.PipeToken, length: 1));
            AccumulateStatementTrivia();
            elements.Add(ParsePipelineElement());
        }

        return elements is null ? first : new ShellPipelineSyntax(bangToken: null, elements, operators);
    }

    private ShellStatementSyntax ParsePipelineElement()
    {
        AccumulateInlineTrivia();

        if (!IsExpressionStart())
            return ParseCommand();

        var expression = ParseExpression();
        var redirections = _pendingTrivia.Count > 0 ? ParseRedirections() : [];

        return new PowerShellExpressionStatementSyntax(expression, redirections);
    }

    /// <summary>Decides between expression mode and command mode, which is the central ambiguity of PowerShell syntax.</summary>
    private bool IsExpressionStart()
    {
        var current = _lexer.Current;
        if (current is '$' or '(' or '@' or '[' or '\'' or '"')
            return true;

        // A leading comma builds a one-element array: `,1` and `$a = ,$b`.
        if (current == ',')
            return true;

        if (char.IsAsciiDigit(current))
            return true;

        if (current == '.' && char.IsAsciiDigit(_lexer.Peek(1)))
            return true;

        if (current is '+' or '-' && (char.IsAsciiDigit(_lexer.Peek(1)) || _lexer.Peek(1) is '$' or '(' or '+' or '-'))
            return true;

        // A leading `-not` or `-split` is a unary operator, not the start of a command word.
        if (current == '-' && PeekWordOperator() is { } wordOperator && Array.IndexOf(UnaryWordOperators, wordOperator) >= 0)
            return true;

        if (current == '!' && _lexer.Peek(1) != '=')
            return true;

        return false;
    }

    /// <summary>Returns whether the label at the current position is followed by a loop keyword.</summary>
    private bool IsLabelOnALoop()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position + 1;
        while (scan < text.Length && PowerShellLexer.IsNameCharacter(text[scan]))
        {
            scan++;
        }

        while (scan < text.Length && (text[scan] is ' ' or '\t' || SourceText.GetLineBreakLength(text, scan) > 0))
        {
            scan += SourceText.GetLineBreakLength(text, scan) is var lineBreak && lineBreak > 0 ? lineBreak : 1;
        }

        var start = scan;
        while (scan < text.Length && PowerShellLexer.IsNameCharacter(text[scan]))
        {
            scan++;
        }

        return text[start..scan].ToLowerInvariant() is "while" or "for" or "foreach" or "do" or "switch";
    }

    private List<ShellRedirectionSyntax> ParseRedirections()
    {
        var redirections = new List<ShellRedirectionSyntax>();
        while (true)
        {
            AccumulateInlineTrivia();
            if (!TryParseRedirection(out var redirection))
                break;

            redirections.Add(redirection);
        }

        return redirections;
    }

    private bool TryParseRedirection([NotNullWhen(true)] out ShellRedirectionSyntax? redirection)
    {
        redirection = null;

        var scan = _lexer.Position;
        var text = _lexer.Text;
        while (scan < text.Length && (char.IsAsciiDigit(text[scan]) || text[scan] == '*'))
        {
            scan++;
        }

        char At(int offset) => scan + offset < text.Length ? text[scan + offset] : '\0';

        var (kind, length) = (At(0), At(1), At(2)) switch
        {
            ('>', '>', _) => (ShellSyntaxKind.GreaterThanGreaterThanToken, 2),
            ('>', '&', '1') => (ShellSyntaxKind.GreaterThanAmpersandToken, 3),
            ('>', _, _) => (ShellSyntaxKind.GreaterThanToken, 1),
            ('<', _, _) => (ShellSyntaxKind.LessThanToken, 1),
            _ => (ShellSyntaxKind.None, 0),
        };

        if (kind == ShellSyntaxKind.None)
            return false;

        var (trivia, fullStart) = TakeTrivia();
        ShellSyntaxToken? ioNumberToken = null;
        if (scan > _lexer.Position)
        {
            var ioStart = _lexer.Position;
            _lexer.Position = scan;
            ioNumberToken = _lexer.CreateToken(ShellSyntaxKind.IoNumberToken, ioStart, trivia, fullStart);
            trivia = [];
            fullStart = _lexer.Position;
        }

        var operatorStart = _lexer.Position;
        _lexer.Position += length;
        var operatorToken = _lexer.CreateToken(kind, operatorStart, trivia, fullStart);

        ShellWordSyntax? target = null;
        if (kind != ShellSyntaxKind.GreaterThanAmpersandToken)
        {
            AccumulateInlineTrivia();
            if (!_lexer.IsAtEnd && !PowerShellLexer.IsArgumentBoundary(_lexer.Current))
            {
                target = ParseCommandWord();
            }
            else
            {
                AddDiagnostic(operatorToken.Span, "SHELL0004", $"Expected a target after '{operatorToken.Text}'.");
            }
        }

        redirection = new ShellRedirectionSyntax(ioNumberToken, operatorToken, target);

        return true;
    }

    // ---- commands ----

    private ShellStatementSyntax ParseCommand()
    {
        var elements = new List<ShellSyntaxNode>();

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.IsAtEnd)
                break;

            if (SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) > 0)
                break;

            var current = _lexer.Current;
            if (current is ';' or '|' or ')' or '}' or ',')
                break;

            // A leading `&` is the call operator, `& $command args`; elsewhere `&` ends the command.
            if (current == '&')
            {
                if (elements.Count > 0)
                    break;

                elements.Add(new ShellWordSyntax([new ShellLiteralWordPartSyntax(ReadOperatorToken(ShellSyntaxKind.AmpersandToken, length: 1))]));
                continue;
            }

            // `>` only redirects when whitespace precedes it and there is already a command to redirect, which is
            // why `in>` is a single word while `Get-Item >` is a redirection missing its target.
            if (elements.Count > 0 && _pendingTrivia.Count > 0 && TryParseRedirection(out var redirection))
            {
                elements.Add(redirection);
                continue;
            }

            if (PowerShellLexer.IsArgumentBoundary(current))
                break;

            var positionBefore = _lexer.Position;
            var word = ParseCommandWord();
            elements.Add(word);

            // After the stop-parsing token the rest of the line is passed through verbatim, so nothing in it is
            // a variable, a redirection, or an operator.
            if (word.ToFullString().TrimStart() == "--%")
            {
                if (ReadRestOfLineVerbatim() is { } verbatim)
                {
                    elements.Add(verbatim);
                }

                break;
            }

            if (_lexer.Position == positionBefore)
                break;
        }

        if (elements.Count == 0)
        {
            var (trivia, fullStart) = TakeTrivia();
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0001", "Expected a command.");

            return new ShellSkippedTextSyntax([MissingToken(ShellSyntaxKind.GenericToken, fullStart, trivia)], fullStart);
        }

        return new ShellCommandSyntax(elements);
    }

    /// <summary>Reads whatever remains on the line as a single literal word, for the <c>--%</c> stop-parsing token.</summary>
    private ShellWordSyntax? ReadRestOfLineVerbatim()
    {
        AccumulateInlineTrivia();
        if (_lexer.IsAtEnd || SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) > 0)
            return null;

        // Take the trivia before advancing: its fallback start is the current position, which would otherwise
        // already be the end of the run and would shift every span that follows.
        var (trivia, fullStart) = TakeTrivia();
        var start = _lexer.Position;
        while (!_lexer.IsAtEnd && SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) == 0)
        {
            _lexer.Position++;
        }

        return new ShellWordSyntax([new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.GenericToken, start, trivia, fullStart))]);
    }

    /// <summary>Reads one command word: literal text mixed with quoting, variables, and embedded expressions.</summary>
    private ShellWordSyntax ParseCommandWord()
    {
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;
        var atElementStart = true;

        while (!_lexer.IsAtEnd && !PowerShellLexer.IsArgumentBoundary(_lexer.Current))
        {
            // A quote that opens an argument closes it too: `Write-Output 'a'b` passes two arguments, while
            // `Write-Output a'b'c` passes the single argument `abc`. A here-string opens with `@`, not a quote.
            var endsAtClosingQuote = atElementStart && _lexer.Current is '\'' or '"';

            var (trivia, fullStart) = isFirst ? TakeTrivia() : ([], _lexer.Position);
            isFirst = false;
            atElementStart = false;

            var positionBefore = _lexer.Position;
            parts.Add(ParseCommandWordPart(trivia, fullStart));
            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }

            // A comma keeps the argument going, which is how `a,b` and `'a','b'` stay one array argument.
            if (!_lexer.IsAtEnd && _lexer.Current == ',')
            {
                var commaStart = _lexer.Position;
                _lexer.Position++;
                parts.Add(new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.CommaToken, commaStart, [], commaStart)));
                atElementStart = true;
                continue;
            }

            if (endsAtClosingQuote)
                break;
        }

        return new ShellWordSyntax(parts);
    }

    private ShellWordPartSyntax ParseCommandWordPart(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        switch (_lexer.Current)
        {
            case '\'':
                return ParseVerbatimString(leadingTrivia, fullStart);
            case '"':
                return WrapExpression(leadingTrivia, fullStart, ParseExpandableString);
            case '`':
                return ParseEscapeSequence(leadingTrivia, fullStart);
            case '(':
                return WrapExpression(leadingTrivia, fullStart, ParseParenthesizedExpression);
            case '{':
                return WrapExpression(leadingTrivia, fullStart, ParseScriptBlock);
            case '$' when _lexer.Peek(1) == '(':
            case '@' when _lexer.Peek(1) is '(' or '{':
            case '@' when IsAtHereStringStart():
                return WrapExpression(leadingTrivia, fullStart, ParsePrimaryExpression);
            // A `$` that names nothing is literal text: `Write-Output $` passes a single dollar sign.
            case '$' when !IsVariableNameStart(_lexer.Peek(1)):
                return ParseBareWordRun(leadingTrivia, fullStart);
            case '$':
            case '@':
                return WrapExpression(leadingTrivia, fullStart, ParseVariableExpression);
            default:
                return ParseBareWordRun(leadingTrivia, fullStart);
        }
    }

    /// <summary>Returns whether <paramref name="value"/> can follow a <c>$</c> and still name a variable.</summary>
    private static bool IsVariableNameStart(char value) =>
        PowerShellLexer.IsNameCharacter(value) || value is '{' or '(' or '$' or '?' or '^' or '_' or ':';

    /// <summary>
    /// Returns <see langword="true"/> at the start of a here-string. The opening <c>@"</c> has to be the last thing
    /// on its line; otherwise the <c>@</c> introduces a splatted variable.
    /// </summary>
    private bool IsAtHereStringStart()
    {
        if (_lexer.Current != '@' || _lexer.Peek(1) is not ('"' or '\''))
            return false;

        var text = _lexer.Text;
        var scan = _lexer.Position + 2;
        while (scan < text.Length && text[scan] is ' ' or '\t')
        {
            scan++;
        }

        return scan >= text.Length || SourceText.GetLineBreakLength(text, scan) > 0;
    }

    private ShellEmbeddedExpressionSyntax WrapExpression(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart, Func<ShellExpressionSyntax> parse)
    {
        PushTrivia(leadingTrivia, fullStart);

        return new ShellEmbeddedExpressionSyntax(parse());
    }

    private ShellLiteralWordPartSyntax ParseBareWordRun(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        while (!_lexer.IsAtEnd
            && !PowerShellLexer.IsArgumentBoundary(_lexer.Current)
            && _lexer.Current is not '\'' and not '"' and not '`' and not '$' and not '(' and not '{')
        {
            _lexer.Position++;
        }

        if (_lexer.Position == start)
        {
            _lexer.Position++;
        }

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.GenericToken, start, leadingTrivia, fullStart));
    }

    // ---- shared token helpers ----

    private ShellEscapeSequenceSyntax ParseEscapeSequence(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position++;
        string value;
        if (_lexer.IsAtEnd)
        {
            value = "`";
        }
        else if (_lexer.Current == 'u' && _lexer.Peek(1) == '{' && TryReadUnicodeEscape(out var unicodeValue))
        {
            value = unicodeValue;
        }
        else
        {
            value = TranslateEscape(_lexer.Current);
            _lexer.Position++;
        }

        return new ShellEscapeSequenceSyntax(_lexer.CreateToken(ShellSyntaxKind.EscapeToken, start, leadingTrivia, fullStart, value));
    }

    /// <summary>
    /// Reads a <c>`u{HHHH}</c> escape, which names a code point by hexadecimal value. Returns <see langword="false"/>
    /// without consuming anything when the sequence is malformed, so the text still round-trips.
    /// </summary>
    private bool TryReadUnicodeEscape([NotNullWhen(true)] out string? value)
    {
        value = null;
        var text = _lexer.Text;
        var scan = _lexer.Position + 2;
        var digits = 0;
        var codePoint = 0;

        while (scan < text.Length && digits <= 6 && text[scan] != '}')
        {
            var digit = Uri.IsHexDigit(text[scan]) ? Convert.ToInt32(text[scan].ToString(), 16) : -1;
            if (digit < 0)
                return false;

            codePoint = (codePoint * 16) + digit;
            digits++;
            scan++;
        }

        if (digits == 0 || scan >= text.Length || text[scan] != '}' || codePoint > 0x10FFFF)
            return false;

        // Surrogate halves are not valid code points on their own.
        if (codePoint is >= 0xD800 and <= 0xDFFF)
            return false;

        _lexer.Position = scan + 1;
        value = char.ConvertFromUtf32(codePoint);

        return true;
    }

    private static string TranslateEscape(char value) => value switch
    {
        '0' => "\0",
        'a' => "\a",
        'b' => "\b",
        'e' => "",
        'f' => "\f",
        'n' => "\n",
        'r' => "\r",
        't' => "\t",
        'v' => "\v",
        _ => value.ToString(),
    };

    private ShellQuotedStringSyntax ParseVerbatimString(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var quoteStart = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(ShellSyntaxKind.SingleQuoteToken, quoteStart, leadingTrivia, fullStart);

        var contentStart = _lexer.Position;
        var value = new StringBuilder();
        var terminated = false;
        while (!_lexer.IsAtEnd)
        {
            if (_lexer.Current == '\'')
            {
                // Two single quotes stand for one literal quote.
                if (_lexer.Peek(1) == '\'')
                {
                    value.Append('\'');
                    _lexer.Position += 2;
                    continue;
                }

                terminated = true;
                break;
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

    // ---- infrastructure ----

    /// <summary>Puts already-read trivia back so the next token created picks it up as its leading trivia.</summary>
    private void PushTrivia(IReadOnlyList<ShellSyntaxTrivia> trivia, int fullStart)
    {
        if (trivia.Count == 0)
            return;

        _pendingTrivia.InsertRange(0, trivia);
        _pendingTriviaStart = fullStart;
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

    private ShellSyntaxToken ReadOperatorToken(ShellSyntaxKind kind, int length)
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _lexer.Position;
        _lexer.Position = Math.Min(_lexer.Position + length, _lexer.Text.Length);

        return _lexer.CreateToken(kind, start, trivia, fullStart);
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
        var start = _lexer.Position;
        _lexer.Position = _lexer.Text.Length;
        var text = _lexer.Text[start..];

        return new ShellSkippedTextSyntax(
            [new ShellSyntaxToken(ShellSyntaxKind.BadToken, text, text, leadingTrivia: trivia, fullStart: fullStart)],
            fullStart);
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

    private static ShellSyntaxToken MissingToken(ShellSyntaxKind kind, int position, IReadOnlyList<ShellSyntaxTrivia>? leadingTrivia = null)
    {
        return new ShellSyntaxToken(kind, string.Empty, string.Empty, isMissing: true, leadingTrivia: leadingTrivia, fullStart: position);
    }

    private void AddDiagnostic(TextSpan span, string id, string message)
    {
        _diagnostics.Add(new ShellDiagnostic(id, message, ShellDiagnosticSeverity.Error, span));
    }
}
