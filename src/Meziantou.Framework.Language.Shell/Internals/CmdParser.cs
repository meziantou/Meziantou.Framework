namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>Parser for the Windows command interpreter. Never throws; unrecognized text is kept as skipped text.</summary>
internal sealed partial class CmdParser
{
    private readonly string _text;
    private readonly List<ShellDiagnostic> _diagnostics = [];
    private readonly ShellParseOptions _options;
    private readonly List<ShellSyntaxTrivia> _pendingTrivia = [];
    private int _position;
    private int _pendingTriviaStart;
    private int _depth;

    /// <summary>
    /// Whether a <c>)</c> ends the text being read. Outside a block cmd treats parentheses as ordinary characters, so
    /// <c>echo a)b</c> prints <c>a)b</c>, while inside one the same <c>)</c> closes the block.
    /// </summary>
    private bool _stopAtCloseParen;

    public CmdParser(string text, ShellParseOptions options)
    {
        _text = text;
        _options = options;
    }

    public IReadOnlyList<ShellDiagnostic> Diagnostics => _diagnostics;

    private bool IsAtEnd => _position >= _text.Length;
    private char Current => _position < _text.Length ? _text[_position] : '\0';

    private char Peek(int offset)
    {
        var index = _position + offset;

        return index >= 0 && index < _text.Length ? _text[index] : '\0';
    }

    public ShellScriptSyntax ParseScript()
    {
        var statements = ParseStatementList(stopAtCloseParen: false);
        var (trivia, fullStart) = TakeTrivia();
        var endOfFileToken = new ShellSyntaxToken(ShellSyntaxKind.EndOfFileToken, string.Empty, string.Empty, leadingTrivia: trivia, fullStart: fullStart);

        return new ShellScriptSyntax(statements, endOfFileToken, _text);
    }

    // ---- statements ----

    private ShellStatementListSyntax ParseStatementList(bool stopAtCloseParen)
    {
        var statements = new List<ShellStatementSyntax>();
        var separators = new List<ShellSyntaxToken>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (IsAtEnd || (stopAtCloseParen && Current == ')'))
                break;

            var positionBefore = _position;

            if (Current == '&' && Peek(1) != '&')
            {
                var stray = ReadToken(ShellSyntaxKind.AmpersandToken, length: 1);
                if (separators.Count < statements.Count)
                {
                    separators.Add(stray);
                }
                else
                {
                    AddDiagnostic(stray.Span, "SHELL0002", "Unexpected '&'.");
                    statements.Add(new ShellSkippedTextSyntax([stray], stray.FullSpan.Start));
                    separators.Add(MissingToken(ShellSyntaxKind.AmpersandToken, _position));
                }

                continue;
            }

            statements.Add(ParseAndOrList(stopAtCloseParen));

            AccumulateInlineTrivia();
            if (!IsAtEnd && Current == '&' && Peek(1) != '&')
            {
                separators.Add(ReadToken(ShellSyntaxKind.AmpersandToken, length: 1));
            }

            if (_position == positionBefore)
            {
                statements.Add(ConsumeUnexpectedCharacter());
            }
        }

        return new ShellStatementListSyntax(statements, separators);
    }

    private ShellStatementSyntax ParseAndOrList(bool stopAtCloseParen)
    {
        var first = ParsePipeline(stopAtCloseParen);
        List<ShellStatementSyntax>? pipelines = null;
        List<ShellSyntaxToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            var kind = (Current, Peek(1)) switch
            {
                ('&', '&') => ShellSyntaxKind.AmpersandAmpersandToken,
                ('|', '|') => ShellSyntaxKind.PipePipeToken,
                _ => ShellSyntaxKind.None,
            };

            if (kind == ShellSyntaxKind.None)
                break;

            pipelines ??= [first];
            operators ??= [];
            operators.Add(ReadToken(kind, length: 2));
            AccumulateStatementTrivia();
            pipelines.Add(ParsePipeline(stopAtCloseParen));
        }

        return pipelines is null ? first : new ShellCommandListSyntax(pipelines, operators);
    }

    private ShellStatementSyntax ParsePipeline(bool stopAtCloseParen)
    {
        var first = ParseStatement(stopAtCloseParen);
        List<ShellStatementSyntax>? commands = null;
        List<ShellSyntaxToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            if (Current != '|' || Peek(1) == '|')
                break;

            commands ??= [first];
            operators ??= [];
            operators.Add(ReadToken(ShellSyntaxKind.PipeToken, length: 1));
            AccumulateStatementTrivia();
            commands.Add(ParseStatement(stopAtCloseParen));
        }

        return commands is null ? first : new ShellPipelineSyntax(bangToken: null, commands, operators);
    }

    private ShellStatementSyntax ParseStatement(bool stopAtCloseParen)
    {
        AccumulateStatementTrivia();

        if (!TryEnterRecursion(new TextSpan(_position, 0)))
            return ConsumeRestAsSkippedText();

        var previousStopAtCloseParen = _stopAtCloseParen;
        _stopAtCloseParen = stopAtCloseParen;
        try
        {
            return ParseStatementCore(stopAtCloseParen);
        }
        finally
        {
            _stopAtCloseParen = previousStopAtCloseParen;
            _depth--;
        }
    }

    private ShellStatementSyntax ParseStatementCore(bool stopAtCloseParen)
    {
        if (Current == '(')
            return ParseParenthesizedBlock();

        if (Current == ':' && Peek(1) != ':')
            return ParseLabel();

        return PeekKeyword() switch
        {
            "if" => ParseIfStatement(),
            "for" => ParseForStatement(),
            "goto" => ParseGotoStatement(),
            "call" => ParseCallStatement(stopAtCloseParen),
            "set" => ParseSetStatement(),
            _ => ParseCommand(),
        };
    }

    private CmdParenthesizedBlockSyntax ParseParenthesizedBlock()
    {
        var openParen = ReadToken(ShellSyntaxKind.OpenParenToken, length: 1);
        var statements = ParseStatementList(stopAtCloseParen: true);

        AccumulateStatementTrivia();
        ShellSyntaxToken closeParen;
        if (Current == ')')
        {
            closeParen = ReadToken(ShellSyntaxKind.CloseParenToken, length: 1);
        }
        else
        {
            AddDiagnostic(openParen.Span, "SHELL0009", "Expected ')' to close the block.");
            closeParen = MissingToken(ShellSyntaxKind.CloseParenToken, _position);
        }

        return new CmdParenthesizedBlockSyntax(openParen, statements, closeParen);
    }

    private CmdLabelStatementSyntax ParseLabel()
    {
        var colonToken = ReadToken(ShellSyntaxKind.ColonToken, length: 1);
        var start = _position;

        // A label takes the rest of its line, except that inside a block the `)` still closes the block, which is what
        // makes `(call :VARDEL X)` a call inside a block rather than a label named `VARDEL X)`.
        while (!IsAtEnd && GetLineBreakLength(_position) == 0 && !(Current == ')' && _stopAtCloseParen))
        {
            _position++;
        }

        return new CmdLabelStatementSyntax(colonToken, CreateToken(ShellSyntaxKind.GenericToken, start, [], start));
    }

    private CmdGotoStatementSyntax ParseGotoStatement()
    {
        var gotoKeyword = ReadKeyword();
        AccumulateInlineTrivia();
        var start = _position;
        while (!IsAtEnd && !IsWordBoundary(Current))
        {
            _position++;
        }

        var (trivia, fullStart) = TakeTrivia();

        return new CmdGotoStatementSyntax(gotoKeyword, CreateToken(ShellSyntaxKind.GenericToken, start, trivia, fullStart));
    }

    private CmdCallStatementSyntax ParseCallStatement(bool stopAtCloseParen)
    {
        var callKeyword = ReadKeyword();

        return new CmdCallStatementSyntax(callKeyword, ParseStatement(stopAtCloseParen));
    }

    private CmdSetStatementSyntax ParseSetStatement()
    {
        var setKeyword = ReadKeyword();

        ShellSyntaxToken? switchToken = null;
        AccumulateInlineTrivia();
        if (Current == '/' && char.IsAsciiLetter(Peek(1)))
        {
            switchToken = ReadToken(ShellSyntaxKind.ParameterToken, length: 2);
        }

        AccumulateInlineTrivia();
        ShellSyntaxToken? nameToken = null;
        ShellSyntaxToken? equalsToken = null;
        ShellWordSyntax? value = null;

        var start = _position;
        var scan = start;
        var inQuotes = false;
        while (scan < _text.Length && _text[scan] != '=' && GetLineBreakLength(scan) == 0)
        {
            // `set "NAME=value"` wraps the whole assignment, so metacharacters inside the quotes are not separators.
            if (_text[scan] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && (_text[scan] is '&' or '|' || (_text[scan] == ')' && _stopAtCloseParen)))
            {
                break;
            }

            scan++;
        }

        if (scan > start)
        {
            _position = scan;
            var (trivia, fullStart) = TakeTrivia();
            nameToken = CreateToken(ShellSyntaxKind.VariableNameToken, start, trivia, fullStart);
        }

        if (Current == '=')
        {
            equalsToken = ReadToken(ShellSyntaxKind.EqualsToken, length: 1);
            if (!IsAtEnd && GetLineBreakLength(_position) == 0)
            {
                value = ParseSetValue();
            }
        }

        return new CmdSetStatementSyntax(setKeyword, switchToken, nameToken, equalsToken, value);
    }

    private CmdIfStatementSyntax ParseIfStatement()
    {
        var ifKeyword = ReadKeyword();

        ShellSyntaxToken? caseInsensitiveToken = null;
        AccumulateInlineTrivia();
        if (Current == '/' && Peek(1) is 'i' or 'I')
        {
            caseInsensitiveToken = ReadToken(ShellSyntaxKind.ParameterToken, length: 2);
        }

        ShellSyntaxToken? notKeyword = null;
        if (PeekKeywordAfterTrivia() == "not")
        {
            notKeyword = ReadKeyword();
        }

        var condition = ParseIfCondition();
        var body = ParseStatement(stopAtCloseParen: true);

        CmdElseClauseSyntax? elseClause = null;
        if (PeekKeywordAfterTrivia() == "else")
        {
            elseClause = new CmdElseClauseSyntax(ReadKeyword(), ParseStatement(stopAtCloseParen: true));
        }

        return new CmdIfStatementSyntax(ifKeyword, caseInsensitiveToken, notKeyword, condition, body, elseClause);
    }

    /// <summary>
    /// Reads the condition of an <c>if</c>. The four forms differ enough that the text is kept verbatim; the scan
    /// stops before the statement that follows.
    /// </summary>
    private ShellRawExpressionSyntax ParseIfCondition()
    {
        AccumulateInlineTrivia();
        var (trivia, fullStart) = TakeTrivia();
        var start = _position;

        var keyword = PeekKeyword();
        if (keyword is "errorlevel" or "defined" or "exist" or "cmdextversion")
        {
            SkipWord();
            SkipBlanks();

            // The operand may be quoted and contain spaces, as in `if exist "C:\Program Files\app.exe"`.
            SkipComparisonOperand();
        }
        else
        {
            // Comparison form: `a==b` or `a EQU b`.
            SkipComparisonOperand();
            SkipBlanks();
            if (Current == '=' && Peek(1) == '=')
            {
                _position += 2;
                SkipComparisonOperand();
            }
            else if (PeekKeyword() is "equ" or "neq" or "lss" or "leq" or "gtr" or "geq")
            {
                SkipWord();
                SkipBlanks();
                SkipComparisonOperand();
            }
        }

        return new ShellRawExpressionSyntax(CreateToken(ShellSyntaxKind.BareTextToken, start, trivia, fullStart));
    }

    private void SkipComparisonOperand()
    {
        if (Current == '"')
        {
            _position++;
            while (!IsAtEnd && Current != '"' && GetLineBreakLength(_position) == 0)
            {
                _position++;
            }

            if (!IsAtEnd && Current == '"')
            {
                _position++;
            }

            return;
        }

        while (!IsAtEnd && !IsWordBoundary(Current) && !(Current == '=' && Peek(1) == '='))
        {
            _position++;
        }
    }

    private CmdForStatementSyntax ParseForStatement()
    {
        var forKeyword = ReadKeyword();

        ShellSyntaxToken? switchToken = null;
        AccumulateInlineTrivia();
        if (Current == '/' && char.IsAsciiLetter(Peek(1)))
        {
            switchToken = ReadToken(ShellSyntaxKind.ParameterToken, length: 2);
        }

        // `/f "tokens=1,2"` puts an option string between the switch and the loop variable.
        var switchArguments = new List<ShellWordSyntax>();
        while (true)
        {
            AccumulateInlineTrivia();
            if (IsAtEnd || Current == '%' || IsWordBoundary(Current))
                break;

            var positionBefore = _position;
            switchArguments.Add(ParseWord());
            if (_position == positionBefore)
                break;
        }

        AccumulateInlineTrivia();
        var variableStart = _position;
        while (!IsAtEnd && (Current == '%' || IsNameCharacter(Current)))
        {
            _position++;
        }

        var (variableTrivia, variableFullStart) = TakeTrivia();
        var variableToken = _position > variableStart
            ? CreateToken(ShellSyntaxKind.VariableNameToken, variableStart, variableTrivia, variableFullStart)
            : MissingToken(ShellSyntaxKind.VariableNameToken, variableFullStart, variableTrivia);

        var inKeyword = ExpectKeyword("in");
        var openParen = ExpectCharacter('(', ShellSyntaxKind.OpenParenToken);

        var items = new List<ShellWordSyntax>();
        var previousStopAtCloseParen = _stopAtCloseParen;
        _stopAtCloseParen = true;
        while (true)
        {
            AccumulateStatementTrivia();
            if (IsAtEnd || Current == ')')
                break;

            var positionBefore = _position;
            items.Add(ParseWord());
            if (_position == positionBefore)
            {
                _position++;
            }
        }

        _stopAtCloseParen = previousStopAtCloseParen;
        var closeParen = ExpectCharacter(')', ShellSyntaxKind.CloseParenToken);
        var doKeyword = ExpectKeyword("do");

        return new CmdForStatementSyntax(forKeyword, switchToken, switchArguments, variableToken, inKeyword, openParen, items, closeParen, doKeyword, ParseStatement(stopAtCloseParen: true));
    }

    private ShellStatementSyntax ParseCommand()
    {
        var elements = new List<ShellSyntaxNode>();

        while (true)
        {
            AccumulateInlineTrivia();
            if (IsAtEnd || GetLineBreakLength(_position) > 0)
                break;

            if (Current is '&' or '|' || (Current == ')' && _stopAtCloseParen))
                break;

            if (TryParseRedirection(out var redirection))
            {
                elements.Add(redirection);
                continue;
            }

            if (IsWordBoundary(Current))
                break;

            var positionBefore = _position;
            elements.Add(ParseWord());
            if (_position == positionBefore)
                break;
        }

        if (elements.Count == 0)
        {
            var (trivia, fullStart) = TakeTrivia();
            AddDiagnostic(new TextSpan(_position, 0), "SHELL0001", "Expected a command.");

            return new ShellSkippedTextSyntax([MissingToken(ShellSyntaxKind.GenericToken, fullStart, trivia)], fullStart);
        }

        return new ShellCommandSyntax(elements);
    }

    private bool TryParseRedirection([NotNullWhen(true)] out ShellRedirectionSyntax? redirection)
    {
        redirection = null;

        var scan = _position;
        while (scan < _text.Length && char.IsAsciiDigit(_text[scan]))
        {
            scan++;
        }

        char At(int offset) => scan + offset < _text.Length ? _text[scan + offset] : '\0';

        var (kind, length) = (At(0), At(1)) switch
        {
            ('>', '>') => (ShellSyntaxKind.GreaterThanGreaterThanToken, 2),
            ('>', '&') => (ShellSyntaxKind.GreaterThanAmpersandToken, 2),
            ('>', _) => (ShellSyntaxKind.GreaterThanToken, 1),
            ('<', _) => (ShellSyntaxKind.LessThanToken, 1),
            _ => (ShellSyntaxKind.None, 0),
        };

        if (kind == ShellSyntaxKind.None)
            return false;

        var (trivia, fullStart) = TakeTrivia();
        ShellSyntaxToken? ioNumberToken = null;
        if (scan > _position)
        {
            var ioStart = _position;
            _position = scan;
            ioNumberToken = CreateToken(ShellSyntaxKind.IoNumberToken, ioStart, trivia, fullStart);
            trivia = [];
            fullStart = _position;
        }

        var operatorStart = _position;
        _position += length;
        var operatorToken = CreateToken(kind, operatorStart, trivia, fullStart);

        AccumulateInlineTrivia();
        ShellWordSyntax? target = null;
        if (!IsAtEnd && !IsWordBoundary(Current))
        {
            target = ParseWord();
        }
        else
        {
            AddDiagnostic(operatorToken.Span, "SHELL0004", $"Expected a target after '{operatorToken.Text}'.");
        }

        redirection = new ShellRedirectionSyntax(ioNumberToken, operatorToken, target);

        return true;
    }
}
