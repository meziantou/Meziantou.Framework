using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>Parser for the Windows command interpreter. Never throws; unrecognized text is kept as skipped text.</summary>
internal sealed partial class CmdParser
{
    private readonly SourceText _source;
    private readonly string _text;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly ShellParseOptions _options;
    private readonly List<GreenNode?> _pendingTrivia = [];
    private int _position;
    private int _pendingTriviaStart;
    private int _depth;

    /// <summary>
    /// Whether a <c>)</c> ends the text being read. Outside a block cmd treats parentheses as ordinary characters, so
    /// <c>echo a)b</c> prints <c>a)b</c>, while inside one the same <c>)</c> closes the block.
    /// </summary>
    private bool _stopAtCloseParen;
    private bool _stopAtEquality;

    public CmdParser(SourceText source, ShellParseOptions options)
    {
        _source = source;
        _text = source.Text;
        _options = options;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

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
        var endOfFileToken = new ScannedToken(SyntaxKind.EndOfFileToken, string.Empty, string.Empty, leadingTrivia: trivia, fullStart: fullStart);

        return new ShellScriptSyntax(statements, endOfFileToken);
    }

    // ---- statements ----

    private ShellStatementListSyntax ParseStatementList(bool stopAtCloseParen)
    {
        var statements = new List<ShellStatementSyntax>();
        var separators = new List<ScannedToken>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (IsAtEnd || (stopAtCloseParen && Current == ')'))
                break;

            var positionBefore = _position;

            if (Current == '&' && Peek(1) != '&')
            {
                var stray = ReadToken(SyntaxKind.AmpersandToken, length: 1);

                // The separator belongs to the statement in front of it, so pad first to land at the right index.
                while (separators.Count + 1 < statements.Count)
                {
                    separators.Add(MissingToken(SyntaxKind.AmpersandToken, stray.FullSpan.Start));
                }

                if (separators.Count < statements.Count)
                {
                    separators.Add(stray);
                }
                else
                {
                    AddDiagnostic(stray.Span, "SHELL0002", "Unexpected '&'.");
                    statements.Add(new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([stray])));
                    separators.Add(MissingToken(SyntaxKind.AmpersandToken, _position));
                }

                continue;
            }

            // `SeparatorTokens[i]` follows `Statements[i]`, so a statement that a line break ended rather than an `&`
            // still needs a placeholder; without one the next `&` would be rebuilt against the wrong statement.
            while (separators.Count < statements.Count)
            {
                separators.Add(MissingToken(SyntaxKind.AmpersandToken, _position));
            }

            statements.Add(ParseAndOrList(stopAtCloseParen));

            AccumulateInlineTrivia();
            if (!IsAtEnd && Current == '&' && Peek(1) != '&')
            {
                separators.Add(ReadToken(SyntaxKind.AmpersandToken, length: 1));
            }

            if (_position == positionBefore)
            {
                statements.Add(ConsumeUnexpectedCharacter());
            }
        }

        return new ShellStatementListSyntax(ParserHelpers.Separated(statements, separators));
    }

    private ShellStatementSyntax ParseAndOrList(bool stopAtCloseParen)
    {
        var first = ParsePipeline(stopAtCloseParen);
        List<ShellStatementSyntax>? pipelines = null;
        List<ScannedToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            var kind = (Current, Peek(1)) switch
            {
                ('&', '&') => SyntaxKind.AmpersandAmpersandToken,
                ('|', '|') => SyntaxKind.PipePipeToken,
                _ => SyntaxKind.None,
            };

            if (kind == SyntaxKind.None)
                break;

            pipelines ??= [first];
            operators ??= [];
            operators.Add(ReadToken(kind, length: 2));
            AccumulateStatementTrivia();
            pipelines.Add(ParsePipeline(stopAtCloseParen));
        }

        return pipelines is null ? first : new ShellCommandListSyntax(ParserHelpers.Separated(pipelines, operators));
    }

    private ShellStatementSyntax ParsePipeline(bool stopAtCloseParen)
    {
        var first = ParseStatement(stopAtCloseParen);
        List<ShellStatementSyntax>? commands = null;
        List<ScannedToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            if (Current != '|' || Peek(1) == '|')
                break;

            commands ??= [first];
            operators ??= [];
            operators.Add(ReadToken(SyntaxKind.PipeToken, length: 1));
            AccumulateStatementTrivia();
            commands.Add(ParseStatement(stopAtCloseParen));
        }

        return commands is null ? first : new ShellPipelineSyntax(bangToken: null, ParserHelpers.Separated(commands, operators));
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
        var openParen = ReadToken(SyntaxKind.OpenParenToken, length: 1);
        var statements = ParseStatementList(stopAtCloseParen: true);

        AccumulateStatementTrivia();
        ScannedToken closeParen;
        if (Current == ')')
        {
            closeParen = ReadToken(SyntaxKind.CloseParenToken, length: 1);
        }
        else
        {
            AddDiagnostic(openParen.Span, "SHELL0009", "Expected ')' to close the block.");
            closeParen = MissingToken(SyntaxKind.CloseParenToken, _position);
        }

        return new CmdParenthesizedBlockSyntax(openParen, statements, closeParen);
    }

    private CmdLabelStatementSyntax ParseLabel()
    {
        var colonToken = ReadToken(SyntaxKind.ColonToken, length: 1);
        var start = _position;

        // A label takes the rest of its line, except that inside a block the `)` still closes the block, which is what
        // makes `(call :VARDEL X)` a call inside a block rather than a label named `VARDEL X)`.
        while (!IsAtEnd && GetLineBreakLength(_position) == 0 && !(Current == ')' && _stopAtCloseParen))
        {
            _position++;
        }

        return new CmdLabelStatementSyntax(colonToken, CreateToken(SyntaxKind.GenericToken, start, null, start));
    }

    private CmdGotoStatementSyntax ParseGotoStatement()
    {
        var gotoKeyword = ReadKeyword();
        AccumulateInlineTrivia();
        var fullStart = PendingFullStart;
        var start = _position;
        while (!IsAtEnd && !IsWordBoundary(Current))
        {
            _position++;
        }

        var (trivia, _) = TakeTrivia();

        return new CmdGotoStatementSyntax(gotoKeyword, CreateToken(SyntaxKind.GenericToken, start, trivia, fullStart));
    }

    private CmdCallStatementSyntax ParseCallStatement(bool stopAtCloseParen)
    {
        var callKeyword = ReadKeyword();

        return new CmdCallStatementSyntax(callKeyword, ParseStatement(stopAtCloseParen));
    }

    private CmdSetStatementSyntax ParseSetStatement()
    {
        var setKeyword = ReadKeyword();

        ScannedToken switchToken = default;
        AccumulateInlineTrivia();
        if (Current == '/' && char.IsAsciiLetter(Peek(1)))
        {
            switchToken = ReadToken(SyntaxKind.ParameterToken, length: 2);
        }

        AccumulateInlineTrivia();
        ScannedToken nameToken = default;
        ScannedToken equalsToken = default;
        ShellWordSyntax? value = null;

        var nameFullStart = PendingFullStart;
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
            var (trivia, _) = TakeTrivia();
            nameToken = CreateToken(SyntaxKind.VariableNameToken, start, trivia, nameFullStart);
        }

        if (Current == '=')
        {
            equalsToken = ReadToken(SyntaxKind.EqualsToken, length: 1);
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

        ScannedToken caseInsensitiveToken = default;
        AccumulateInlineTrivia();
        if (Current == '/' && Peek(1) is 'i' or 'I')
        {
            caseInsensitiveToken = ReadToken(SyntaxKind.ParameterToken, length: 2);
        }

        ScannedToken notKeyword = default;
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
    /// Reads the condition of an <c>if</c>. The <c>errorlevel</c>, <c>defined</c>, <c>exist</c>, and
    /// <c>cmdextversion</c> forms become a unary expression and the <c>==</c> and <c>EQU</c> forms a binary one, so
    /// each operand is an ordinary word with its quoting and variable references exposed. Text matching none of the
    /// forms is left as a lone operand rather than reported as an error, as cmd only diagnoses it when the line runs.
    /// </summary>
    private ShellExpressionSyntax ParseIfCondition()
    {
        AccumulateInlineTrivia();

        var keyword = PeekKeyword();
        if (keyword is "errorlevel" or "defined" or "exist" or "cmdextversion")
        {
            var unaryToken = ReadToken(SyntaxKind.OperatorToken, keyword.Length);

            return new ShellUnaryExpressionSyntax(SyntaxKind.PrefixUnaryExpression, unaryToken, ParseIfConditionOperand(), postfixOperatorToken: null);
        }

        var previousStopAtEquality = _stopAtEquality;
        _stopAtEquality = true;
        ShellOperandExpressionSyntax left;
        try
        {
            left = ParseIfConditionOperand();
        }
        finally
        {
            _stopAtEquality = previousStopAtEquality;
        }

        AccumulateInlineTrivia();
        if (Current == '=' && Peek(1) == '=')
        {
            var equalsToken = ReadToken(SyntaxKind.OperatorToken, length: 2);

            return new ShellBinaryExpressionSyntax(left, equalsToken, ParseIfConditionOperand());
        }

        var comparison = PeekKeyword();
        if (comparison is "equ" or "neq" or "lss" or "leq" or "gtr" or "geq")
        {
            var comparisonToken = ReadToken(SyntaxKind.OperatorToken, comparison.Length);

            return new ShellBinaryExpressionSyntax(left, comparisonToken, ParseIfConditionOperand());
        }

        return left;
    }

    private ShellOperandExpressionSyntax ParseIfConditionOperand()
    {
        AccumulateInlineTrivia();

        var word = ParseWord();
        if (word.PartCount() == 0)
        {
            // A word with no parts reports no position of its own, which would leave every span around it wrong. An
            // empty token keeps the position and, with it, the trivia that has piled up in front of the missing operand.
            word = new ShellWordSyntax(GreenFactory.List([new ShellLiteralWordPartSyntax(ReadToken(SyntaxKind.BareTextToken, length: 0))]));
        }

        return new ShellOperandExpressionSyntax(word);
    }

    private CmdForStatementSyntax ParseForStatement()
    {
        var forKeyword = ReadKeyword();

        ScannedToken switchToken = default;
        AccumulateInlineTrivia();
        if (Current == '/' && char.IsAsciiLetter(Peek(1)))
        {
            switchToken = ReadToken(SyntaxKind.ParameterToken, length: 2);
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
        var variableFullStart = PendingFullStart;
        var variableStart = _position;
        while (!IsAtEnd && (Current == '%' || IsNameCharacter(Current)))
        {
            _position++;
        }

        var (variableTrivia, _) = TakeTrivia();
        var variableToken = _position > variableStart
            ? CreateToken(SyntaxKind.VariableNameToken, variableStart, variableTrivia, variableFullStart)
            : MissingToken(SyntaxKind.VariableNameToken, variableFullStart, variableTrivia);

        var inKeyword = ExpectKeyword("in");
        var openParen = ExpectCharacter('(', SyntaxKind.OpenParenToken);

        var items = new List<ShellWordSyntax>();
        var previousStopAtCloseParen = _stopAtCloseParen;
        _stopAtCloseParen = true;
        while (true)
        {
            AccumulateStatementTrivia();
            if (IsAtEnd || Current == ')')
                break;

            // An operator cannot start an item, so the list is malformed. Stopping here leaves the text to the
            // enclosing statement list; skipping the character would drop it from the tree entirely.
            if (IsWordBoundary(Current))
                break;

            var positionBefore = _position;
            items.Add(ParseWord());
            if (_position == positionBefore)
                break;
        }

        _stopAtCloseParen = previousStopAtCloseParen;
        var closeParen = ExpectCharacter(')', SyntaxKind.CloseParenToken);
        var doKeyword = ExpectKeyword("do");

        return new CmdForStatementSyntax(forKeyword, switchToken, ParserHelpers.List(switchArguments), variableToken, inKeyword, openParen, ParserHelpers.List(items), closeParen, doKeyword, ParseStatement(stopAtCloseParen: true));
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

            return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([MissingToken(SyntaxKind.GenericToken, fullStart, trivia)]));
        }

        return new ShellCommandSyntax(ParserHelpers.List(elements));
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
            ('>', '>') => (SyntaxKind.GreaterThanGreaterThanToken, 2),
            ('>', '&') => (SyntaxKind.GreaterThanAmpersandToken, 2),
            ('>', _) => (SyntaxKind.GreaterThanToken, 1),
            ('<', _) => (SyntaxKind.LessThanToken, 1),
            _ => (SyntaxKind.None, 0),
        };

        if (kind == SyntaxKind.None)
            return false;

        var (trivia, fullStart) = TakeTrivia();
        ScannedToken ioNumberToken = default;
        if (scan > _position)
        {
            var ioStart = _position;
            _position = scan;
            ioNumberToken = CreateToken(SyntaxKind.IoNumberToken, ioStart, trivia, fullStart);
            trivia = null;
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
