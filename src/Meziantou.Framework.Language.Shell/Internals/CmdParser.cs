using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>Parser for the Windows command interpreter. Never throws; unrecognized text is kept as skipped text.</summary>
/// <remarks>
/// The structure follows the grammar cmd.exe itself implements: a line is <c>s0 -&gt; s1 [&amp; s0]</c>,
/// <c>s1 -&gt; s2 [|| s1]</c>, <c>s2 -&gt; s3 [&amp;&amp; s2]</c>, <c>s3 -&gt; s4 [| s3]</c>, where <c>s4</c> is a block,
/// an <c>if</c>, a <c>for</c>, or a simple command with its redirections. The command of an <c>if</c>, an <c>else</c>, or
/// a <c>for ... do</c> is a whole <c>s0</c>, so it runs to the end of the logical line, <c>&amp;</c> included.
/// </remarks>
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

    /// <summary>
    /// Whether <c>,</c>, <c>;</c>, and <c>=</c> separate words, which they do between the parentheses of a <c>for</c>
    /// set: <c>for /l %%n in (1,1,10)</c> has three items.
    /// </summary>
    private bool _splitOnTokenDelimiters;

    /// <summary>
    /// Whether the parser is between the parentheses of a <c>for</c> set, where <c>rem</c> and <c>::</c> are items
    /// rather than comments.
    /// </summary>
    private bool _inForSet;

    /// <summary>The number of parenthesized blocks the parser is inside.</summary>
    private int _blockDepth;

    /// <summary>
    /// Where the text after the most recently closed block starts. An <c>else</c> is only recognized right there,
    /// because a simple command would have taken the word as one of its arguments.
    /// </summary>
    private int _lastBlockEnd = -1;

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

    /// <summary>
    /// Reads the command of an <c>if</c>, an <c>else</c>, or a <c>for ... do</c>: everything up to the end of the
    /// logical line, so <c>if errorlevel 1 echo failed &amp; exit /b 1</c> only exits on failure.
    /// </summary>
    private ShellStatementSyntax ParseBody()
    {
        AccumulateCommandTrivia();
        var stopAtCloseParen = _blockDepth > 0;
        var first = ParseAndOrList(stopAtCloseParen);
        List<ShellStatementSyntax>? commands = null;
        List<ScannedToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();

            // An `&` with nothing after it on the line ends the statement rather than extending the body; cmd
            // accepts it either way, and leaving it to the enclosing list keeps the body a single command.
            if (Current != '&' || Peek(1) == '&' || !HasCommandAfterAmpersand(stopAtCloseParen))
                break;

            commands ??= [first];
            operators ??= [];
            operators.Add(ReadToken(SyntaxKind.AmpersandToken, length: 1));
            AccumulateInlineTrivia();
            commands.Add(ParseAndOrList(stopAtCloseParen));
        }

        return commands is null ? first : new ShellCommandListSyntax(ParserHelpers.Separated(commands, operators));
    }

    private bool HasCommandAfterAmpersand(bool stopAtCloseParen)
    {
        var scan = _position + 1;
        while (scan < _text.Length && _text[scan] is ' ' or '\t')
        {
            scan++;
        }

        return scan < _text.Length
            && GetLineBreakLength(scan) == 0
            && _text[scan] != '&'
            && !(_text[scan] == ')' && stopAtCloseParen);
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
            AccumulateCommandTrivia();
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
            AccumulateCommandTrivia();
            commands.Add(ParseStatement(stopAtCloseParen));
        }

        return commands is null ? first : new ShellPipelineSyntax(bangToken: null, ParserHelpers.Separated(commands, operators));
    }

    private ShellStatementSyntax ParseStatement(bool stopAtCloseParen)
    {
        // Callers have already consumed the line breaks they allow: outside a block a command must start on the line
        // that asks for it.
        AccumulateInlineTrivia();

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
        // `@` only turns echoing off for the command, so `@if` is still an `if` and `@(` still opens a block.
        var prefixLength = Current == '@' ? 1 : 0;

        if (Peek(prefixLength) == '(')
            return ParseParenthesizedBlock(prefixLength);

        if (Current == ')' && !stopAtCloseParen && (IsTokenEnd(Peek(1)) || Peek(1) is '^' or '"'))
            return ParseStrayCloseParen();

        if (Current == ':' && Peek(1) != ':')
            return ParseLabel();

        switch (PeekCommandKeyword(prefixLength))
        {
            case "if":
                return ParseIfStatement(prefixLength);
            case "for":
                return ParseForStatement(prefixLength);
            case "goto":
                return ParseGotoStatement(prefixLength);
            case "call":
                return ParseCallStatement(prefixLength, stopAtCloseParen);
            case "set":
                return ParseSetStatement(prefixLength);
            case "else" when prefixLength == 0:
                AddDiagnostic(new TextSpan(_position, 4), "SHELL0002", "Unexpected 'else'; it must follow the ')' that closes the command of an 'if', on the same line.");
                break;
        }

        return ParseCommand();
    }

    private CmdParenthesizedBlockSyntax ParseParenthesizedBlock(int prefixLength)
    {
        var (openTrivia, openFullStart) = TakeTrivia();
        var openStart = _position;
        _position += prefixLength + 1;
        var openParen = CreateToken(SyntaxKind.OpenParenToken, openStart, openTrivia, openFullStart, "(");

        _blockDepth++;
        ShellStatementListSyntax statements;
        try
        {
            statements = ParseStatementList(stopAtCloseParen: true);
        }
        finally
        {
            _blockDepth--;
        }

        AccumulateStatementTrivia();
        ScannedToken closeParen;
        GreenNode? redirections = null;
        if (Current == ')')
        {
            // cmd rejects a block with nothing in it, which is why an empty `if` body needs at least a `rem`.
            if (_text.AsSpan(openParen.End, _position - openParen.End).IndexOfAnyExcept(" \t\r\n") < 0)
            {
                AddDiagnostic(new TextSpan(_position, 0), "SHELL0001", "Expected a command inside the block.");
            }

            closeParen = ReadToken(SyntaxKind.CloseParenToken, length: 1);

            // `(echo a & echo b) > out.txt` redirects the output of the whole block.
            redirections = ParseTrailingRedirections();
            _lastBlockEnd = PendingFullStart;

            AccumulateInlineTrivia();
            if (!IsAtEnd
                && GetLineBreakLength(_position) == 0
                && Current is not ('&' or '|')
                && !(Current == ')' && _stopAtCloseParen)
                && PeekClauseKeyword() != "else")
            {
                AddDiagnostic(new TextSpan(_position, 1), "SHELL0002", $"Unexpected '{Current}' after ')'.");
            }
        }
        else
        {
            AddDiagnostic(openParen.Span, "SHELL0009", "Expected ')' to close the block.");
            closeParen = MissingToken(SyntaxKind.CloseParenToken, _position);
        }

        return new CmdParenthesizedBlockSyntax(openParen, statements, closeParen, redirections);
    }

    /// <summary>
    /// With no block open, cmd discards a <c>)</c> in command position together with the rest of its line, the way it
    /// discards a <c>rem</c>. A script only has one when its parentheses do not balance, so it is reported.
    /// </summary>
    private ShellSkippedTextSyntax ParseStrayCloseParen()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _position;
        while (!IsAtEnd && GetLineBreakLength(_position) == 0)
        {
            // A caret still joins the next line to this one.
            _position += Current == '^' && GetLineBreakLength(_position + 1) is var lineBreakLength and > 0 ? 1 + lineBreakLength : 1;
        }

        AddDiagnostic(new TextSpan(start, 1), "SHELL0002", "Unexpected ')' with no open block; cmd ignores the rest of the line.");
        var token = CreateToken(SyntaxKind.BadToken, start, trivia, fullStart);

        return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([token]));
    }

    private CmdLabelStatementSyntax ParseLabel()
    {
        var colonToken = ReadToken(SyntaxKind.ColonToken, length: 1);
        var start = _position;

        // A label takes the rest of its line, except that inside a block the `)` still closes the block.
        while (!IsAtEnd && GetLineBreakLength(_position) == 0 && !(Current == ')' && _stopAtCloseParen))
        {
            _position++;
        }

        // Only the first token names the label, so `:strLen string len -- returns the length` is the label `strLen`
        // followed by a description that `goto` and `call` never look at.
        var nameEnd = start;
        while (nameEnd < _position && !IsTokenEnd(_text[nameEnd]))
        {
            nameEnd++;
        }

        return new CmdLabelStatementSyntax(colonToken, CreateToken(SyntaxKind.GenericToken, start, null, start, _text[start..nameEnd]));
    }

    private CmdGotoStatementSyntax ParseGotoStatement(int prefixLength)
    {
        var gotoKeyword = ReadKeyword(prefixLength, "goto".Length);
        AccumulateInlineTrivia();
        var fullStart = PendingFullStart;
        var start = _position;
        while (!IsAtEnd && !IsWordBoundary(Current))
        {
            _position++;
        }

        var (trivia, _) = TakeTrivia();
        ScannedToken labelToken;
        if (_position == start)
        {
            AddDiagnostic(new TextSpan(start, 0), "SHELL0013", "Expected a label after 'goto'.");
            labelToken = MissingToken(SyntaxKind.GenericToken, fullStart, trivia);
        }
        else
        {
            // `goto` only reads its first argument and ignores the rest of the command, which stays in the token
            // rather than turning into a command that never runs.
            var labelEnd = _position;
            while (!IsAtEnd && GetLineBreakLength(_position) == 0 && Current is not ('&' or '|') && !(Current == ')' && _stopAtCloseParen))
            {
                _position++;
            }

            _position = TrimTrailingWhitespace(labelEnd, _position);
            labelToken = CreateToken(SyntaxKind.GenericToken, start, trivia, fullStart, _text[start..labelEnd]);
        }

        return new CmdGotoStatementSyntax(gotoKeyword, labelToken);
    }

    private CmdCallStatementSyntax ParseCallStatement(int prefixLength, bool stopAtCloseParen)
    {
        var callKeyword = ReadKeyword(prefixLength, "call".Length);
        AccumulateInlineTrivia();

        // `call :sub a b` is an ordinary command whose name is the label: its arguments, redirections, and the
        // operators after it are parsed like any other command's, so `call :sub || goto :error` is two commands.
        var target = Current == ':' ? ParseCommand() : ParseStatement(stopAtCloseParen);

        return new CmdCallStatementSyntax(callKeyword, target);
    }

    private CmdSetStatementSyntax ParseSetStatement(int prefixLength)
    {
        var setKeyword = ReadKeyword(prefixLength, "set".Length);

        ScannedToken switchToken = default;
        AccumulateInlineTrivia();
        if (Current == '/' && char.IsAsciiLetter(Peek(1)))
        {
            switchToken = ReadToken(SyntaxKind.ParameterToken, length: 2);
        }

        AccumulateInlineTrivia();
        var end = FindSetStatementEnd(_position);
        var redirectionStart = FindTrailingRedirections(_position, end);

        var (nameToken, equalsToken, value) = Current == '"'
            ? ParseQuotedSetAssignment(redirectionStart)
            : ParseSetAssignment(redirectionStart);

        return new CmdSetStatementSyntax(setKeyword, switchToken, nameToken, equalsToken, value, ParseTrailingRedirections());
    }

    private (ScannedToken Name, ScannedToken EqualsToken, ShellWordSyntax? Value) ParseSetAssignment(int end)
    {
        ScannedToken nameToken = default;
        ScannedToken equalsToken = default;
        ShellWordSyntax? value = null;

        var nameFullStart = PendingFullStart;
        var start = _position;
        var scan = start;
        while (scan < end && _text[scan] != '=')
        {
            scan++;
        }

        if (scan > start)
        {
            _position = scan;
            var (trivia, _) = TakeTrivia();
            nameToken = CreateToken(SyntaxKind.VariableNameToken, start, trivia, nameFullStart);
        }

        if (_position < end && Current == '=')
        {
            equalsToken = ReadToken(SyntaxKind.EqualsToken, length: 1);
            value = ParseSetValue(end);
        }

        return (nameToken, equalsToken, value);
    }

    /// <summary>
    /// Reads <c>set "NAME=value"</c>. cmd removes the first quote and the last one on the line, and ignores anything
    /// after the last, so the name and value exposed here are the ones without the quotes.
    /// </summary>
    private (ScannedToken Name, ScannedToken EqualsToken, ShellWordSyntax? Value) ParseQuotedSetAssignment(int end)
    {
        var openQuote = _position;
        var closeQuote = _text.AsSpan(openQuote + 1, end - openQuote - 1).LastIndexOf('"');
        if (closeQuote < 0)
        {
            AddDiagnostic(new TextSpan(openQuote, 1), "SHELL0003", "Unterminated quoted string.");
        }
        else
        {
            closeQuote += openQuote + 1;
        }

        var contentEnd = closeQuote < 0 ? end : closeQuote;
        var equalsIndex = _text.AsSpan(openQuote + 1, contentEnd - openQuote - 1).IndexOf('=');
        if (equalsIndex >= 0)
        {
            equalsIndex += openQuote + 1;
        }

        var nameFullStart = PendingFullStart;
        ScannedToken equalsToken = default;
        ShellWordSyntax? value = null;

        if (equalsIndex < 0)
        {
            // `set "PREFIX"` lists variables; the quotes and whatever follows them are not part of the name.
            _position = closeQuote < 0 ? end : TrimTrailingWhitespace(closeQuote + 1, end);
            var (trivia, _) = TakeTrivia();
            var name = _text[(openQuote + 1)..contentEnd];

            return (CreateToken(SyntaxKind.VariableNameToken, openQuote, trivia, nameFullStart, name), equalsToken, value);
        }

        _position = equalsIndex;
        var (nameTrivia, _) = TakeTrivia();
        var nameToken = CreateToken(SyntaxKind.VariableNameToken, openQuote, nameTrivia, nameFullStart, _text[(openQuote + 1)..equalsIndex]);
        equalsToken = ReadToken(SyntaxKind.EqualsToken, length: 1);

        var parts = new List<ShellWordPartSyntax>();
        while (_position < contentEnd)
        {
            var positionBefore = _position;
            parts.Add(Current switch
            {
                '%' => ParsePercentReference(null, _position, contentEnd),
                '!' when IsDelayedExpansionEnabled && IsDelayedExpansion(inQuotes: true, contentEnd) => ParseDelayedReference(null, _position),
                _ => ParseLiteralRunUntil(contentEnd, SyntaxKind.GenericToken),
            });

            if (_position == positionBefore)
            {
                _position++;
            }
        }

        if (closeQuote >= 0)
        {
            _position = closeQuote + 1;
            parts.Add(new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.DoubleQuoteToken, closeQuote, null, closeQuote, string.Empty)));

            // Text after the closing quote is ignored, which is what makes `set "x=1" junk` assign `1`.
            var junkEnd = TrimTrailingWhitespace(_position, end);
            if (junkEnd > _position)
            {
                var junkStart = _position;
                _position = junkEnd;
                parts.Add(new ShellLiteralWordPartSyntax(CreateToken(SyntaxKind.GenericToken, junkStart, null, junkStart, string.Empty)));
            }
        }

        if (parts.Count > 0)
        {
            value = new ShellWordSyntax(ParserHelpers.List(parts));
        }

        return (nameToken, equalsToken, value);
    }

    private GreenNode? ParseTrailingRedirections()
    {
        List<ShellRedirectionSyntax>? redirections = null;
        while (true)
        {
            AccumulateInlineTrivia();
            if (IsAtEnd || !TryParseRedirection(out var redirection))
                break;

            redirections ??= [];
            redirections.Add(redirection);
        }

        return ParserHelpers.List(redirections);
    }

    private CmdIfStatementSyntax ParseIfStatement(int prefixLength)
    {
        var ifKeyword = ReadKeyword(prefixLength, "if".Length);

        ScannedToken caseInsensitiveToken = default;
        AccumulateInlineTrivia();
        if (Current == '/' && (Peek(1) is 'i' or 'I') && IsTokenEnd(Peek(2)))
        {
            caseInsensitiveToken = ReadToken(SyntaxKind.ParameterToken, length: 2);
        }

        ScannedToken notKeyword = default;
        AccumulateInlineTrivia();
        if (PeekClauseKeyword() == "not")
        {
            notKeyword = ReadKeyword(prefixLength: 0, "not".Length);
        }

        var condition = ParseIfCondition();
        var body = ParseBody();

        CmdElseClauseSyntax? elseClause = null;
        AccumulateInlineTrivia();
        if (PendingFullStart == _lastBlockEnd && PeekClauseKeyword() == "else")
        {
            var elseKeyword = ReadKeyword(prefixLength: 0, "else".Length);
            elseClause = new CmdElseClauseSyntax(elseKeyword, ParseBody());
        }

        return new CmdIfStatementSyntax(ifKeyword, caseInsensitiveToken, notKeyword, condition, body, elseClause);
    }

    /// <summary>
    /// Reads the condition of an <c>if</c>. The <c>errorlevel</c>, <c>defined</c>, <c>exist</c>, and
    /// <c>cmdextversion</c> forms become a unary expression and the <c>==</c> and <c>EQU</c> forms a binary one, so
    /// each operand is an ordinary word with its quoting and variable references exposed.
    /// </summary>
    private ShellExpressionSyntax ParseIfCondition()
    {
        AccumulateInlineTrivia();

        var keyword = PeekClauseKeyword();
        if (keyword is "errorlevel" or "defined" or "exist" or "cmdextversion")
        {
            var unaryToken = ReadToken(SyntaxKind.OperatorToken, keyword.Length);

            return new ShellUnaryExpressionSyntax(SyntaxKind.PrefixUnaryExpression, unaryToken, ParseIfConditionOperand($"'{unaryToken.Text}'"), postfixOperatorToken: null);
        }

        var previousStopAtEquality = _stopAtEquality;
        _stopAtEquality = true;
        ShellOperandExpressionSyntax left;
        var leftStart = _position;
        int leftEnd;
        try
        {
            left = ParseIfConditionOperand(description: null);
            leftEnd = _position;
        }
        finally
        {
            _stopAtEquality = previousStopAtEquality;
        }

        AccumulateInlineTrivia();
        if (Current == '=' && Peek(1) == '=')
        {
            var equalsToken = ReadToken(SyntaxKind.OperatorToken, length: 2);

            return new ShellBinaryExpressionSyntax(left, equalsToken, ParseIfConditionOperand("'=='"));
        }

        var comparison = PeekClauseKeyword();
        if (comparison is "equ" or "neq" or "lss" or "leq" or "gtr" or "geq")
        {
            var comparisonToken = ReadToken(SyntaxKind.OperatorToken, comparison.Length);

            return new ShellBinaryExpressionSyntax(left, comparisonToken, ParseIfConditionOperand($"'{comparisonToken.Text}'"));
        }

        if (leftEnd == leftStart)
        {
            AddDiagnostic(new TextSpan(leftStart, 0), "SHELL0040", "Expected a condition after 'if'.");
        }
        else if (_text.AsSpan(leftStart, leftEnd - leftStart).IndexOfAny('%', '!') < 0)
        {
            // Only an expansion can turn `if %C% echo hi` into a comparison when the line runs; without one, cmd
            // rejects the line.
            AddDiagnostic(new TextSpan(leftStart, leftEnd - leftStart), "SHELL0041", "Expected a comparison operator such as '==' after the operand of 'if'.");
        }

        return left;
    }

    private ShellOperandExpressionSyntax ParseIfConditionOperand(string? description)
    {
        AccumulateInlineTrivia();

        var word = ParseWord();
        if (word.PartCount() == 0)
        {
            if (description is not null)
            {
                AddDiagnostic(new TextSpan(_position, 0), "SHELL0040", $"Expected an operand after {description}.");
            }

            // A word with no parts reports no position of its own, which would leave every span around it wrong. An
            // empty token keeps the position and, with it, the trivia that has piled up in front of the missing operand.
            word = new ShellWordSyntax(GreenFactory.List([new ShellLiteralWordPartSyntax(ReadToken(SyntaxKind.BareTextToken, length: 0))]));
        }

        return new ShellOperandExpressionSyntax(word);
    }

    private CmdForStatementSyntax ParseForStatement(int prefixLength)
    {
        var forKeyword = ReadKeyword(prefixLength, "for".Length);

        ScannedToken switchToken = default;
        AccumulateInlineTrivia();
        if (Current == '/' && char.IsAsciiLetter(Peek(1)))
        {
            switchToken = ReadToken(SyntaxKind.ParameterToken, length: 2);
        }

        var isForF = switchToken.IsPresent && switchToken.Text is "/f" or "/F";

        // `/f "tokens=1,2"` puts an option string between the switch and the loop variable, and `/r` a root path.
        var switchArguments = new List<ShellWordSyntax>();
        while (true)
        {
            AccumulateInlineTrivia();
            if (IsAtEnd || IsWordBoundary(Current) || PeekClauseKeyword() == "in")
                break;

            // `for /r %1 %%f in` has two tokens shaped like a loop variable; the variable is the one before `in`.
            if (GetForVariableLength(_position) is var candidateLength and > 0 && GetForVariableLength(SkipInlineWhitespace(_position + candidateLength)) == 0)
                break;

            var positionBefore = _position;
            switchArguments.Add(ParseWord());
            if (_position == positionBefore)
                break;
        }

        AccumulateInlineTrivia();
        var variableFullStart = PendingFullStart;
        var variableStart = _position;
        if (GetForVariableLength(_position) is var variableLength and > 0)
        {
            _position += variableLength;
        }
        else
        {
            while (!IsAtEnd && (Current == '%' || IsNameCharacter(Current)))
            {
                _position++;
            }
        }

        var (variableTrivia, _) = TakeTrivia();
        ScannedToken variableToken;
        if (_position > variableStart)
        {
            variableToken = CreateToken(SyntaxKind.VariableNameToken, variableStart, variableTrivia, variableFullStart);
        }
        else
        {
            AddDiagnostic(new TextSpan(_position, 0), "SHELL0013", "Expected a loop variable such as '%%i'.");
            variableToken = MissingToken(SyntaxKind.VariableNameToken, variableFullStart, variableTrivia);
        }

        var inKeyword = ExpectKeyword("in");
        var openParen = ExpectCharacter('(', SyntaxKind.OpenParenToken);

        var items = new List<ShellWordSyntax>();
        var previousStopAtCloseParen = _stopAtCloseParen;
        var previousSplitOnTokenDelimiters = _splitOnTokenDelimiters;
        var previousInForSet = _inForSet;
        _stopAtCloseParen = true;
        _inForSet = true;

        // `for /f` hands the text between the parentheses to its own parser, so only the other forms split on `,`.
        _splitOnTokenDelimiters = !isForF;
        try
        {
            while (true)
            {
                // The set may span lines, but only once its `(` has opened it; without one, stopping at the line end
                // keeps a malformed header from swallowing the rest of the script.
                if (openParen.IsMissing)
                {
                    AccumulateInlineTrivia();
                    if (IsAtEnd || GetLineBreakLength(_position) > 0 || PeekClauseKeyword() == "do")
                        break;
                }
                else
                {
                    AccumulateStatementTrivia();
                }

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
        }
        finally
        {
            _stopAtCloseParen = previousStopAtCloseParen;
            _splitOnTokenDelimiters = previousSplitOnTokenDelimiters;
            _inForSet = previousInForSet;
        }

        var closeParen = ExpectCharacter(')', SyntaxKind.CloseParenToken);
        var doKeyword = ExpectKeyword("do");

        return new CmdForStatementSyntax(forKeyword, switchToken, ParserHelpers.List(switchArguments), variableToken, inKeyword, openParen, ParserHelpers.List(items), closeParen, doKeyword, ParseBody());
    }

    /// <summary>
    /// Returns the length of the loop variable at <paramref name="position"/>: <c>%%</c> in a batch file or <c>%</c> on
    /// the command line, then any one character, then the end of the token. Returns 0 when there is none.
    /// </summary>
    private int GetForVariableLength(int position)
    {
        var percents = At(position) == '%' ? At(position + 1) == '%' ? 2 : 1 : 0;
        if (percents == 0)
            return 0;

        var name = At(position + percents);
        if (name is '%' || IsTokenEnd(name))
            return 0;

        return IsTokenEnd(At(position + percents + 1)) ? percents + 1 : 0;
    }

    private int SkipInlineWhitespace(int position)
    {
        while (position < _text.Length && _text[position] is ' ' or '\t')
        {
            position++;
        }

        return position;
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

        var (kind, length) = (At(scan), At(scan + 1)) switch
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
