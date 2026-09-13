using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>Parser for the PowerShell family. Never throws; unrecognized text is kept as skipped text plus a diagnostic.</summary>
internal sealed partial class PowerShellParser
{
    private readonly PowerShellLexer _lexer;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly ShellParseOptions _options;
    private readonly List<GreenNode?> _pendingTrivia = [];
    private int _pendingTriviaStart;
    private int _depth;
    private bool _allowEmptyParentheses;
    private bool _paramBlockAllowed;
    private int _lineBreakTriviaEnd = -1;

    public PowerShellParser(SourceText source, ShellParseOptions options)
    {
        _options = options;
        _lexer = new PowerShellLexer(source, options.Dialect, _diagnostics);
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public ShellScriptSyntax ParseScript()
    {
        var statements = ParseStatementList(stopCharacter: '\0', StatementListKind.Script);
        var (trivia, fullStart) = TakeTrivia();
        var endOfFileToken = new ScannedToken(SyntaxKind.EndOfFileToken, string.Empty, string.Empty, leadingTrivia: trivia, fullStart: fullStart);

        return new ShellScriptSyntax(statements, endOfFileToken);
    }

    // ---- statements ----

    /// <summary>What a statement list is the body of, which decides what it may contain.</summary>
    private enum StatementListKind
    {
        /// <summary>A whole script: <c>using</c> statements, then a script body.</summary>
        Script,

        /// <summary>The body of a script block or function: either named blocks or plain statements, never both.</summary>
        ScriptBlock,

        /// <summary>The inside of <c>$( )</c> or <c>@( )</c>.</summary>
        SubExpression,

        /// <summary>The members of a class or enum, which the tree keeps as plain statements.</summary>
        TypeBody,
    }

    private ShellStatementListSyntax ParseStatementList(char stopCharacter, StatementListKind kind = StatementListKind.SubExpression)
    {
        var statements = new List<ShellStatementSyntax>();
        var separators = new List<ScannedToken>();

        // A script body opens with its `using` statements and `param` block. What comes first after them decides
        // whether the body is made of named blocks (`begin { } process { }`) or of plain statements.
        var hasBody = kind is StatementListKind.Script or StatementListKind.ScriptBlock;
        var inPreamble = hasBody;
        var usingAllowed = kind == StatementListKind.Script;
        var namedBlocks = false;
        var hasParamBlock = false;

        // Class members are kept as plain statements, so a method such as `M() { }` reads as a command whose
        // argument is an empty `()`. That is not an error there, although it is everywhere else.
        var allowedEmptyParentheses = _allowEmptyParentheses;
        _allowEmptyParentheses = kind == StatementListKind.TypeBody;

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || (stopCharacter != '\0' && _lexer.Current == stopCharacter))
                break;

            var positionBefore = _lexer.Position;

            // Only `;` separates statements. A leading `,` is the unary array operator, as in `$a = ,1`.
            if (_lexer.Current == ';')
            {
                var separator = ReadOperatorToken(SyntaxKind.SemicolonToken, length: 1);

                // The separator belongs to the statement in front of it, so pad first to land at the right index.
                while (separators.Count + 1 < statements.Count)
                {
                    separators.Add(MissingToken(SyntaxKind.SemicolonToken, separator.FullSpan.Start));
                }

                if (separators.Count == statements.Count)
                {
                    // An empty statement is legal, so `;; Get-Date` is not an error.
                    statements.Add(new ShellEmptyStatementSyntax());
                }

                separators.Add(separator);

                continue;
            }

            // `SeparatorTokens[i]` follows `Statements[i]`, so a statement that a line break ended rather than a `;`
            // still needs a placeholder; without one the next `;` would be rebuilt against the wrong statement.
            while (separators.Count < statements.Count)
            {
                separators.Add(MissingToken(SyntaxKind.SemicolonToken, _lexer.Position));
            }

            ShellStatementSyntax statement;
            if (_lexer.Current is ')' or '}')
            {
                // A closing character that does not close this list closes nothing at all.
                statement = ConsumeUnexpectedCharacter();
            }
            else
            {
                var keyword = PeekKeyword();
                var isNamedBlock = hasBody && IsNamedBlockKeyword(keyword);
                var isParamBlock = inPreamble && !hasParamBlock && IsParamBlockStart(keyword);
                if (inPreamble && !isParamBlock && !(keyword == "using" && usingAllowed))
                {
                    inPreamble = false;
                    namedBlocks = isNamedBlock;
                }

                // Only the first `param (…)` of a body declares its parameters; anywhere else `param` is a command.
                hasParamBlock |= isParamBlock;
                _paramBlockAllowed = isParamBlock;

                if (keyword == "using" && !usingAllowed)
                {
                    AddDiagnostic(new TextSpan(_lexer.Position, keyword.Length), "SHELL0024", "A 'using' statement must appear before any other statement in a script.");
                }
                else if (keyword != "using")
                {
                    usingAllowed = false;
                }

                if (namedBlocks && isNamedBlock && keyword is not null)
                {
                    statement = ParseNamedBlock(keyword);
                }
                else
                {
                    if (namedBlocks)
                    {
                        // Once a body is made of named blocks, nothing else may stand beside them.
                        AddUnexpectedTokenDiagnostic();
                    }

                    statement = ParseStatement();
                }

                _paramBlockAllowed = false;
            }

            statements.Add(statement);

            AccumulateInlineTrivia();
            if (!_lexer.IsAtEnd && _lexer.Current == ';')
            {
                separators.Add(ReadOperatorToken(SyntaxKind.SemicolonToken, length: 1));
            }
            else if (_lexer.Current == '&' && _lexer.Peek(1) != '&' && RequiresTerminator(statement))
            {
                // `Start-Job … &` runs the pipeline in the background, which PowerShell 7 introduced.
                var separator = ReadOperatorToken(SyntaxKind.AmpersandToken, length: 1);
                if (!_options.Dialect.HasFeature(ShellDialectFeatures.PipelineChainOperators))
                {
                    AddDiagnostic(separator.Span, "SHELL0002", "The '&' background operator requires PowerShell 7.");
                }

                separators.Add(separator);
            }
            else if ((_lexer.Current, _lexer.Peek(1)) is ('&', '&') or ('|', '|'))
            {
                // Only reachable where pipeline chains do not exist; keeping the operator lets the text round-trip.
                var separator = ReadOperatorToken(_lexer.Current == '&' ? SyntaxKind.AmpersandAmpersandToken : SyntaxKind.PipePipeToken, length: 2);
                AddDiagnostic(separator.Span, "SHELL0002", $"The '{separator.Text}' operator requires PowerShell 7.");
                separators.Add(separator);
            }
            else if (!IsAtStatementEnd(stopCharacter) && kind != StatementListKind.TypeBody && RequiresTerminator(statement))
            {
                // `$x = 1 2` and `Get-Date) ` leave something on the line that no statement can start with here.
                AddUnexpectedTokenDiagnostic();
            }

            if (_lexer.Position == positionBefore)
            {
                statements.Add(ConsumeUnexpectedCharacter());
            }
        }

        _allowEmptyParentheses = allowedEmptyParentheses;

        return new ShellStatementListSyntax(ParserHelpers.Separated(statements, separators));
    }

    private bool IsNamedBlockKeyword(string? keyword) => keyword switch
    {
        "begin" or "process" or "end" or "dynamicparam" => true,
        "clean" => _options.Dialect.HasFeature(ShellDialectFeatures.CleanBlock),
        _ => false,
    };

    /// <summary>Returns whether a <c>param</c> block, possibly preceded by attributes, starts at the current position.</summary>
    private bool IsParamBlockStart(string? keyword)
    {
        if (keyword == "param" && FollowedBy(keyword, '('))
            return true;

        return _lexer.Current == '[' && PeekKeywordAfterAttributes() == "param";
    }

    /// <summary>Returns whether nothing but the end of the statement follows on the current line.</summary>
    private bool IsAtStatementEnd(char stopCharacter)
    {
        if (_lexer.IsAtEnd || SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) > 0 || FollowsLineBreakTrivia())
            return true;

        return _lexer.Current is ';' or ')' or '}' || (stopCharacter != '\0' && _lexer.Current == stopCharacter);
    }

    /// <summary>
    /// Returns whether <paramref name="statement"/> is a pipeline, which has to be ended by a line break or a
    /// <c>;</c>. A statement that ends with a block, such as <c>if ($a) { } Get-Date</c>, needs no terminator.
    /// </summary>
    private static bool RequiresTerminator(ShellStatementSyntax statement) => statement.Kind is
        SyntaxKind.Command or SyntaxKind.Pipeline or SyntaxKind.CommandList or SyntaxKind.PowerShellExpressionStatement
        or SyntaxKind.PowerShellReturnStatement or SyntaxKind.PowerShellThrowStatement or SyntaxKind.PowerShellExitStatement
        or SyntaxKind.PowerShellUsingStatement;

    /// <summary>Reports the token at the current position as unexpected, without consuming it.</summary>
    private void AddUnexpectedTokenDiagnostic()
    {
        var text = _lexer.Text;
        var start = _lexer.Position;
        var end = start + 1;
        if (PowerShellLexer.IsNameCharacter(text[start]) || text[start] is '-' or '$')
        {
            while (end < text.Length && end - start < 40 && !char.IsWhiteSpace(text[end]) && !PowerShellLexer.IsArgumentBoundary(text[end]))
            {
                end++;
            }
        }

        AddDiagnostic(TextSpan.FromBounds(start, end), "SHELL0002", $"Unexpected token '{text[start..end]}'.");
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
        // The statement list decides whether a `param` block may stand here; a nested statement never takes it over.
        var paramBlockAllowed = _paramBlockAllowed;
        _paramBlockAllowed = false;

        // `[Attribute()] param(...)` and `[Attribute()] class X {}` keep their attributes; a bare `[int]$x` is a cast.
        var keywordAfterAttributes = PeekKeywordAfterAttributes();
        if (keywordAfterAttributes is "class" or "enum" || (keywordAfterAttributes == "param" && paramBlockAllowed))
        {
            var attributes = ParseAttributeList();

            return keywordAfterAttributes == "param" ? ParseParamBlock(attributes) : ParseTypeDefinition(attributes);
        }

        // Statement keywords are reserved at the start of a statement: `if` with no condition is a malformed `if`, not a
        // command named `if`. `param` is the exception, and so are the named blocks, which only count as keywords at
        // the start of a script body (see ParseStatementList); elsewhere `param 1` or `process { }` is a command.
        var keyword = PeekKeyword();
        switch (keyword)
        {
            case "if":
                return ParseIfStatement();
            case "while":
                return ParseWhileStatement();
            case "do":
                return ParseDoStatement();
            case "for":
                return ParseForStatement();
            case "foreach":
                return ParseForEachStatement();
            case "switch":
                return ParseSwitchStatement();
            case "try":
                return ParseTryStatement();
            case "trap":
                return ParseTrapStatement();
            case "function":
                return ParseFunctionDefinition(SyntaxKind.PowerShellFunctionDefinition);
            case "filter":
                return ParseFunctionDefinition(SyntaxKind.PowerShellFilterDefinition);
            case "workflow":
                return ParseFunctionDefinition(SyntaxKind.PowerShellWorkflowDefinition);
            case "class" or "enum":
                return ParseTypeDefinition([]);
            case "param" when paramBlockAllowed && FollowedBy(keyword, '('):
                return ParseParamBlock([]);
            case "data":
                return ParseDataStatement();
            case "using":
                return ParseUsingStatement();
            case "from" or "define" or "var":
                AddDiagnostic(new TextSpan(_lexer.Position, keyword.Length), "SHELL0025", $"The '{keyword}' keyword is reserved for future use.");
                break;
            case "parallel" or "sequence" when !_options.Dialect.HasFeature(ShellDialectFeatures.PipelineChainOperators):
                break;
            case "parallel" or "sequence":
                // Workflows, where these keywords belong, do not exist in PowerShell 7.
                AddDiagnostic(new TextSpan(_lexer.Position, keyword.Length), "SHELL0025", $"The '{keyword}' keyword can only be used in a workflow.");
                break;
            case "break":
                return ParseFlowStatement(SyntaxKind.PowerShellBreakStatement);
            case "continue":
                return ParseFlowStatement(SyntaxKind.PowerShellContinueStatement);
            case "return":
                return ParseFlowStatement(SyntaxKind.PowerShellReturnStatement);
            case "exit":
                return ParseFlowStatement(SyntaxKind.PowerShellExitStatement);
            case "throw":
                return ParseFlowStatement(SyntaxKind.PowerShellThrowStatement);
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

    private ShellStatementSyntax ParseAndOrList() => ContinueAndOrList(ParsePipeline());

    /// <summary>Continues a pipeline chain, <c>a &amp;&amp; b || c</c>, whose first pipeline has already been read.</summary>
    private ShellStatementSyntax ContinueAndOrList(ShellStatementSyntax first)
    {
        if (!_options.Dialect.HasFeature(ShellDialectFeatures.PipelineChainOperators))
            return first;

        List<ShellStatementSyntax>? pipelines = null;
        List<ScannedToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            var kind = (_lexer.Current, _lexer.Peek(1)) switch
            {
                ('&', '&') => SyntaxKind.AmpersandAmpersandToken,
                ('|', '|') => SyntaxKind.PipePipeToken,
                _ => SyntaxKind.None,
            };

            if (kind == SyntaxKind.None)
                break;

            pipelines ??= [first];
            operators ??= [];
            operators.Add(ReadOperatorToken(kind, length: 2));
            AccumulateStatementTrivia();
            pipelines.Add(ParsePipeline());
        }

        return pipelines is null ? first : new ShellCommandListSyntax(ParserHelpers.Separated(pipelines, operators));
    }

    private ShellStatementSyntax ParsePipeline() => ContinuePipeline(ParsePipelineElement());

    /// <summary>Continues a pipeline whose first element has already been read.</summary>
    private ShellStatementSyntax ContinuePipeline(ShellStatementSyntax first)
    {
        List<ShellStatementSyntax>? elements = null;
        List<ScannedToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            if (IsAtLeadingPipeOnNextLine())
            {
                AccumulateStatementTrivia();
            }

            if (_lexer.Current != '|' || _lexer.Peek(1) == '|')
                break;

            elements ??= [first];
            operators ??= [];
            operators.Add(ReadOperatorToken(SyntaxKind.PipeToken, length: 1));
            AccumulateStatementTrivia();
            AccumulateInlineTrivia();
            var elementStart = _lexer.Position;
            var element = ParsePipelineElement();
            if (element.Kind == SyntaxKind.PowerShellExpressionStatement)
            {
                // `Get-Item | $x` has nothing to pipe into: only a command can receive pipeline input.
                AddDiagnostic(new TextSpan(elementStart, 0), "SHELL0002", "An expression is only allowed as the first element of a pipeline.");
            }

            elements.Add(element);
        }

        return elements is null ? first : new ShellPipelineSyntax(bangToken: null, ParserHelpers.Separated(elements, operators));
    }

    /// <summary>
    /// Returns whether the pipeline continues with a <c>|</c> at the start of a following line, which PowerShell 7.4
    /// accepts. Only comments may come between the two lines; a blank line ends the pipeline.
    /// </summary>
    private bool IsAtLeadingPipeOnNextLine()
    {
        if (!_options.Dialect.HasFeature(ShellDialectFeatures.PipelineChainOperators))
            return false;

        var text = _lexer.Text;
        var scan = _lexer.Position;
        var crossedLineBreak = false;
        var lineHasContent = true;
        while (scan < text.Length)
        {
            if (text[scan] is ' ' or '\t')
            {
                scan++;
            }
            else if (text[scan] == '<' && scan + 1 < text.Length && text[scan + 1] == '#')
            {
                var end = text.IndexOf("#>", scan + 2, StringComparison.Ordinal);
                if (end < 0)
                    return false;

                scan = end + 2;
                lineHasContent = true;
            }
            else if (text[scan] == '#')
            {
                while (scan < text.Length && SourceText.GetLineBreakLength(text, scan) == 0)
                {
                    scan++;
                }

                lineHasContent = true;
            }
            else if (SourceText.GetLineBreakLength(text, scan) is var lineBreak and > 0)
            {
                if (!lineHasContent)
                    return false;

                scan += lineBreak;
                crossedLineBreak = true;
                lineHasContent = false;
            }
            else
            {
                return crossedLineBreak && text[scan] == '|' && (scan + 1 >= text.Length || text[scan + 1] != '|');
            }
        }

        return false;
    }

    private ShellStatementSyntax ParsePipelineElement()
    {
        AccumulateInlineTrivia();

        if (!IsExpressionStart())
            return ParseCommand();

        var expression = ParseExpression();

        // PowerShell has no `>` operator, so after an expression `>` always redirects, even in `$x>out.txt`. A
        // stream number such as `2>&1` still needs whitespace in front of it, or it would continue the expression.
        AccumulateInlineTrivia();
        var redirections = _pendingTrivia.Count > 0 || _lexer.Current is '>' or '<' ? ParseRedirections() : [];

        return new PowerShellExpressionStatementSyntax(expression, ParserHelpers.List(redirections));
    }

    /// <summary>Decides between expression mode and command mode, which is the central ambiguity of PowerShell syntax.</summary>
    private bool IsExpressionStart()
    {
        var current = _lexer.Current;
        if (current is '(' or '@' or '[' or '\'' or '"' or '{')
            return true;

        // A `$` that names nothing, as in `$ foo`, is a command word.
        if (current == '$')
            return IsVariableNameStart(_lexer.Peek(1));

        // A leading comma builds a one-element array: `,1` and `$a = ,$b`.
        if (current == ',')
            return true;

        // A number only starts an expression when it is a whole token: `1kb` is a number, but `1abc` and `1[int]` are
        // command names.
        if (char.IsAsciiDigit(current) || (current == '.' && char.IsAsciiDigit(_lexer.Peek(1))))
            return IsNumberToken();

        // `+` is always an operator. `-` is one too, except where it starts a parameter such as `-Name` (a command
        // word) or the stop-parsing token `--%`.
        if (current == '+')
            return true;

        if (current == '-')
        {
            var next = _lexer.Peek(1);
            if (next == '-')
                return _lexer.Peek(2) != '%';

            if (char.IsAsciiDigit(next) || next is '$' or '(' or '+' or '\'' or '"' or '[' or '@' or '!' or ',' or ';' or ')' or '}' or '|' or '&' || IsSpaceOrLineEnd(1))
                return true;

            // A leading `-not` or `-split` is a unary operator, not the start of a command word.
            return PeekWordOperator() is { } wordOperator && Array.IndexOf(UnaryWordOperators, wordOperator) >= 0;
        }

        // `!$a` negates, but `!abc` is a command word.
        if (current == '!' && _lexer.Peek(1) != '=' && !PowerShellLexer.IsNameCharacter(_lexer.Peek(1)))
            return true;

        return false;
    }

    /// <summary>Returns whether the number at the current position ends where a number token has to end.</summary>
    private bool IsNumberToken()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        if (scan + 1 < text.Length && text[scan] == '0' && text[scan + 1] is 'x' or 'X')
        {
            scan += 2;
            while (scan < text.Length && char.IsAsciiHexDigit(text[scan]))
            {
                scan++;
            }
        }
        else if (scan + 1 < text.Length && text[scan] == '0' && text[scan + 1] is 'b' or 'B')
        {
            scan += 2;
            while (scan < text.Length && text[scan] is '0' or '1')
            {
                scan++;
            }
        }
        else
        {
            while (scan < text.Length && char.IsAsciiDigit(text[scan]))
            {
                scan++;
            }

            if (scan < text.Length && text[scan] == '.' && scan + 1 < text.Length && char.IsAsciiDigit(text[scan + 1]))
            {
                scan++;
                while (scan < text.Length && char.IsAsciiDigit(text[scan]))
                {
                    scan++;
                }
            }

            if (scan + 1 < text.Length && text[scan] is 'e' or 'E' && (char.IsAsciiDigit(text[scan + 1]) || (text[scan + 1] is '+' or '-' && scan + 2 < text.Length && char.IsAsciiDigit(text[scan + 2]))))
            {
                scan += 2;
                while (scan < text.Length && char.IsAsciiDigit(text[scan]))
                {
                    scan++;
                }
            }
        }

        var suffixStart = scan;
        while (scan < text.Length && char.IsAsciiLetter(text[scan]))
        {
            scan++;
        }

        if (!IsNumberSuffix(text.AsSpan(suffixStart, scan - suffixStart)))
            return false;

        return scan >= text.Length
            || char.IsWhiteSpace(text[scan])
            || text[scan] is ';' or ')' or '}' or ']' or '|' or '&' or ',' or '.' or '+' or '-' or '*' or '/' or '%' or '=' or '!' or '<' or '>' or '#';
    }

    /// <summary>Returns whether <paramref name="suffix"/> is a numeric type suffix, a multiplier, or both, as in <c>1lkb</c>.</summary>
    private static bool IsNumberSuffix(ReadOnlySpan<char> suffix)
    {
        if (suffix.Length >= 2 && suffix[^1] is 'b' or 'B' && suffix[^2] is 'k' or 'K' or 'm' or 'M' or 'g' or 'G' or 't' or 'T' or 'p' or 'P')
        {
            suffix = suffix[..^2];
        }

        return suffix.Length switch
        {
            0 => true,
            1 => suffix[0] is 'l' or 'L' or 'd' or 'D' or 'u' or 'U' or 's' or 'S' or 'y' or 'Y' or 'n' or 'N',
            2 => suffix.Equals("ul", StringComparison.OrdinalIgnoreCase) || suffix.Equals("lu", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("us", StringComparison.OrdinalIgnoreCase) || suffix.Equals("uy", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>Returns whether the character at <paramref name="offset"/> is whitespace, a line break, or the end of the text.</summary>
    private bool IsSpaceOrLineEnd(int offset) => _lexer.Peek(offset) is '\0' or ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

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
            ('>', '>', _) => (SyntaxKind.GreaterThanGreaterThanToken, 2),
            ('>', '&', '1') => (SyntaxKind.GreaterThanAmpersandToken, 3),
            ('>', _, _) => (SyntaxKind.GreaterThanToken, 1),
            ('<', _, _) => (SyntaxKind.LessThanToken, 1),
            _ => (SyntaxKind.None, 0),
        };

        if (kind == SyntaxKind.None)
            return false;

        var (trivia, fullStart) = TakeTrivia();
        ScannedToken ioNumberToken = default;
        if (scan > _lexer.Position)
        {
            var ioStart = _lexer.Position;
            _lexer.Position = scan;
            ioNumberToken = _lexer.CreateToken(SyntaxKind.IoNumberToken, ioStart, trivia, fullStart);
            trivia = null;
            fullStart = _lexer.Position;
        }

        var operatorStart = _lexer.Position;
        _lexer.Position += length;
        var operatorToken = _lexer.CreateToken(kind, operatorStart, trivia, fullStart);

        ShellWordSyntax? target = null;
        if (kind != SyntaxKind.GreaterThanAmpersandToken)
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

            // A leading `&` is the call operator, `& $command args`; elsewhere `&` ends the command, and so does the
            // `&&` of a pipeline chain.
            if (current == '&')
            {
                if (elements.Count > 0 || _lexer.Peek(1) == '&')
                    break;

                var callOperator = ReadOperatorToken(SyntaxKind.AmpersandToken, length: 1);
                elements.Add(new ShellWordSyntax(GreenFactory.List([new ShellLiteralWordPartSyntax(callOperator)])));
                ReportMissingInvocationTarget(callOperator);
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
            var word = ParseCommandWord(isCommandName: elements.Count == 0);
            elements.Add(word);

            var wordText = word.ToString();
            if (elements.Count == 1 && wordText == ".")
            {
                // The dot-source operator, like the call operator, needs something to invoke.
                ReportMissingInvocationTarget(new ScannedToken(SyntaxKind.GenericToken, ".", fullStart: positionBefore));
            }
            else if (wordText.Length > 2 && wordText[0] == '-' && wordText[^1] == ':' && PowerShellLexer.IsNameStart(wordText[1]) && IsAtCommandEnd())
            {
                // `-Path:` binds the argument that follows it, so it cannot end the command.
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0026", $"The parameter '{wordText}' requires an argument.");
            }

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

            return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([MissingToken(SyntaxKind.GenericToken, fullStart, trivia)]));
        }

        return new ShellCommandSyntax(ParserHelpers.List(elements));
    }

    /// <summary>Returns whether the command ends at the current position, looking past inline whitespace only.</summary>
    private bool IsAtCommandEnd()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        while (scan < text.Length && text[scan] is ' ' or '\t')
        {
            scan++;
        }

        return scan >= text.Length || SourceText.GetLineBreakLength(text, scan) > 0 || text[scan] is ';' or '|' or ')' or '}' or '&';
    }

    /// <summary>Reports a call or dot-source operator that has nothing after it to invoke.</summary>
    private void ReportMissingInvocationTarget(ScannedToken operatorToken)
    {
        if (IsAtCommandEnd())
        {
            AddDiagnostic(operatorToken.Span, "SHELL0001", $"Expected a command after '{operatorToken.Text}'.");
        }
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

        return new ShellWordSyntax(GreenFactory.List([new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.GenericToken, start, trivia, fullStart))]));
    }

    /// <summary>Reads one command word: literal text mixed with quoting, variables, and embedded expressions.</summary>
    private ShellWordSyntax ParseCommandWord(bool isCommandName = false)
    {
        var parts = new List<ShellWordPartSyntax>();
        var takeTrivia = true;
        var atElementStart = true;

        while (!_lexer.IsAtEnd && !PowerShellLexer.IsArgumentBoundary(_lexer.Current))
        {
            // A quote that opens an argument closes it too: `Write-Output 'a'b` passes two arguments, while
            // `Write-Output a'b'c` passes the single argument `abc`. A here-string opens with `@`, not a quote.
            var endsAtClosingQuote = atElementStart && _lexer.Current is '\'' or '"';

            var (trivia, fullStart) = takeTrivia ? TakeTrivia() : (null, _lexer.Position);
            var partAtElementStart = atElementStart;
            takeTrivia = false;
            atElementStart = false;

            var positionBefore = _lexer.Position;
            parts.Add(ParseCommandWordPart(trivia, fullStart, partAtElementStart));
            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }

            // A comma keeps the argument going, which is how `a,b`, `a , b`, and `'a','b'` stay one array argument.
            // The next element may even start on the following line.
            if (!isCommandName && IsAtArgumentComma())
            {
                // A parameter name is not a value, so it cannot start an array: `-Path ,a` and `-Path,a` are errors.
                if (parts.Count == 1 && parts[0].ToString() is { Length: > 1 } partText && partText[0] == '-' && PowerShellLexer.IsNameStart(partText[1]) && !partText.Contains(':', StringComparison.Ordinal))
                {
                    AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0026", $"The parameter '{partText}' requires an argument.");
                }

                AccumulateInlineTrivia();
                parts.Add(new ShellLiteralWordPartSyntax(ReadOperatorToken(SyntaxKind.CommaToken, length: 1)));
                AccumulateStatementTrivia();
                if (_lexer.IsAtEnd || PowerShellLexer.IsArgumentBoundary(_lexer.Current))
                {
                    AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0023", "Expected an argument after ','.");
                    break;
                }

                takeTrivia = true;
                atElementStart = true;
                continue;
            }

            if (endsAtClosingQuote)
                break;
        }

        return new ShellWordSyntax(ParserHelpers.List(parts));
    }

    /// <summary>Returns whether a <c>,</c> follows, possibly after inline whitespace.</summary>
    private bool IsAtArgumentComma()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        while (scan < text.Length && text[scan] is ' ' or '\t')
        {
            scan++;
        }

        return scan < text.Length && text[scan] == ',';
    }

    private ShellWordPartSyntax ParseCommandWordPart(GreenNode? leadingTrivia, int fullStart, bool atElementStart)
    {
        switch (_lexer.Current)
        {
            // `@` only splats or opens `@( )` at the start of an argument; `$PSScriptRoot@` ends with a literal `@`.
            case '@' when !atElementStart:
                return ParseBareWordRun(leadingTrivia, fullStart);
            case '\'':
                return ParseVerbatimString(leadingTrivia, fullStart);
            // An argument that starts with a value may go on with member access, indexing, or a method call, as in
            // `Write-Host $item.Name.ToUpper()` or `Join-Path (Get-Location).Path x`.
            case '"':
                return WrapExpression(leadingTrivia, fullStart, () => ParsePostfixOperators(ParseExpandableString(), argumentMode: true));
            case '`':
                return ParseEscapeSequence(leadingTrivia, fullStart);
            case '(':
                return WrapExpression(leadingTrivia, fullStart, () => ParsePostfixOperators(ParseParenthesizedExpression(), argumentMode: true));
            case '{':
                return WrapExpression(leadingTrivia, fullStart, ParseScriptBlock);
            case '$' when _lexer.Peek(1) == '(':
            case '@' when _lexer.Peek(1) is '(' or '{':
            case '@' when IsAtHereStringStart():
                return WrapExpression(leadingTrivia, fullStart, () => ParsePostfixOperators(ParsePrimaryExpression(), argumentMode: true));
            // A `$` that names nothing is literal text: `Write-Output $` passes a single dollar sign.
            case '$' when !IsVariableNameStart(_lexer.Peek(1)):
                return ParseBareWordRun(leadingTrivia, fullStart);
            case '@' when _lexer.Peek(1) is '"' or '\'':
                return WrapExpression(leadingTrivia, fullStart, ParseMalformedHereStringHeader);
            case '$':
                return WrapExpression(leadingTrivia, fullStart, () => ParsePostfixOperators(ParseVariableExpression(), argumentMode: true));
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

    private ShellEmbeddedExpressionSyntax WrapExpression(GreenNode? leadingTrivia, int fullStart, Func<ShellExpressionSyntax> parse)
    {
        PushTrivia(leadingTrivia, fullStart);

        return new ShellEmbeddedExpressionSyntax(parse());
    }

    private ShellLiteralWordPartSyntax ParseBareWordRun(GreenNode? leadingTrivia, int fullStart)
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

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.GenericToken, start, leadingTrivia, fullStart));
    }

    // ---- shared token helpers ----

    private ShellEscapeSequenceSyntax ParseEscapeSequence(GreenNode? leadingTrivia, int fullStart)
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

        return new ShellEscapeSequenceSyntax(_lexer.CreateToken(SyntaxKind.EscapeToken, start, leadingTrivia, fullStart, value));
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

    private ShellQuotedStringSyntax ParseVerbatimString(GreenNode? leadingTrivia, int fullStart)
    {
        var quoteStart = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(SyntaxKind.SingleQuoteToken, quoteStart, leadingTrivia, fullStart);

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
            ? new ShellLiteralWordPartSyntax(new ScannedToken(
                SyntaxKind.BareTextToken,
                _lexer.Text[contentStart..contentEnd],
                value.ToString(),
                fullStart: contentStart))
            : null;

        ScannedToken closeToken;
        if (!terminated)
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated single-quoted string.");
            closeToken = MissingToken(SyntaxKind.SingleQuoteToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(SyntaxKind.SingleQuoteToken, closeStart, null, closeStart);
        }

        return new ShellQuotedStringSyntax(openToken, (GreenNode?)content, closeToken);
    }

    // ---- infrastructure ----

    /// <summary>Puts already-read trivia back so the next token created picks it up as its leading trivia.</summary>
    private void PushTrivia(GreenNode? trivia, int fullStart)
    {
        if (trivia is null)
            return;

        _pendingTrivia.InsertRange(0, Flatten(trivia));
        _pendingTriviaStart = fullStart;
    }

    /// <summary>Reads a green trivia node back as the pieces it was built from.</summary>
    private static IEnumerable<GreenNode?> Flatten(GreenNode trivia)
    {
        if (!trivia.IsList)
        {
            yield return trivia;
            yield break;
        }

        for (var index = 0; index < trivia.SlotCount; index++)
        {
            yield return trivia.GetSlot(index);
        }
    }

    private void AccumulateInlineTrivia()
    {
        if (_pendingTrivia.Count == 0)
        {
            _pendingTriviaStart = _lexer.Position;
        }

        AddTrivia(_lexer.ReadInlineTrivia());
    }

    private void AccumulateStatementTrivia()
    {
        if (_pendingTrivia.Count == 0)
        {
            _pendingTriviaStart = _lexer.Position;
        }

        var trivia = _lexer.ReadStatementTrivia();
        AddTrivia(trivia);
        if (trivia is not null && Flatten(trivia).Any(piece => piece?.RawKind == (int)SyntaxKind.EndOfLineTrivia))
        {
            _lineBreakTriviaEnd = _lexer.Position;
        }
    }

    /// <summary>
    /// Returns whether a line break was read as trivia right before the current position. A token the parser had to
    /// synthesize, such as a missing <c>)</c>, may have taken that line break as its trivia, and the statement still
    /// ended there.
    /// </summary>
    private bool FollowsLineBreakTrivia() => _lineBreakTriviaEnd == _lexer.Position;

    /// <summary>A point the parser can go back to, when what it read turns out to belong to something else.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ParserCheckpoint(int Position, GreenNode?[] PendingTrivia, int PendingTriviaStart, int DiagnosticCount);

    private ParserCheckpoint CreateCheckpoint() => new(_lexer.Position, [.. _pendingTrivia], _pendingTriviaStart, _diagnostics.Count);

    private void Restore(ParserCheckpoint checkpoint)
    {
        _lexer.Position = checkpoint.Position;
        _pendingTrivia.Clear();
        _pendingTrivia.AddRange(checkpoint.PendingTrivia);
        _pendingTriviaStart = checkpoint.PendingTriviaStart;
        _diagnostics.RemoveRange(checkpoint.DiagnosticCount, _diagnostics.Count - checkpoint.DiagnosticCount);
    }

    private void AddTrivia(GreenNode? trivia)
    {
        if (trivia is not null)
        {
            _pendingTrivia.AddRange(Flatten(trivia));
        }
    }

    private (GreenNode? Trivia, int FullStart) TakeTrivia()
    {
        if (_pendingTrivia.Count == 0)
            return (null, _lexer.Position);

        var trivia = GreenFactory.List(CollectionsMarshal.AsSpan(_pendingTrivia));
        var start = _pendingTriviaStart;
        _pendingTrivia.Clear();

        return (trivia, start);
    }

    private ScannedToken ReadOperatorToken(SyntaxKind kind, int length)
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

        return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([new ScannedToken(SyntaxKind.BadToken, text, text, leadingTrivia: trivia, fullStart: fullStart)]));
    }

    private ShellSkippedTextSyntax ConsumeUnexpectedCharacter()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _lexer.Position;
        _lexer.Position = Math.Min(_lexer.Position + 1, _lexer.Text.Length);
        var token = _lexer.CreateToken(SyntaxKind.BadToken, start, trivia, fullStart);
        AddDiagnostic(token.Span, "SHELL0002", $"Unexpected '{token.Text}'.");

        return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([token]));
    }

    private static ScannedToken MissingToken(SyntaxKind kind, int position, GreenNode? leadingTrivia = null)
    {
        return new ScannedToken(kind, string.Empty, string.Empty, isMissing: true, leadingTrivia: leadingTrivia, fullStart: position);
    }

    private void AddDiagnostic(TextSpan span, string id, string message)
    {
        // One mistake tends to leave several expected pieces missing at the same place, as in `function f(` with no
        // `)` and no body. Reporting each of them would bury the actual error, so only the first one is kept.
        if (_diagnostics.Count > 0 && _diagnostics[^1].Location.SourceSpan.Start == span.Start && (span.Length == 0 || _diagnostics[^1].Id == id))
            return;

        _diagnostics.Add(new Diagnostic(id, message, DiagnosticSeverity.Error, new Location(span, _lexer.Source)));
    }
}
