using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>PowerShell control flow, declarations, and blocks.</summary>
internal sealed partial class PowerShellParser
{
    private PowerShellIfStatementSyntax ParseIfStatement()
    {
        var ifKeyword = ReadKeywordToken();
        var (openParen, condition, closeParen) = ParseParenthesizedCondition();
        var body = ParseScriptBlock();

        var elseIfClauses = new List<PowerShellElseIfClauseSyntax>();
        while (PeekKeywordAfterTrivia() == "elseif")
        {
            var elseIfKeyword = ReadKeywordToken();
            var (elseIfOpen, elseIfCondition, elseIfClose) = ParseParenthesizedCondition();
            elseIfClauses.Add(new PowerShellElseIfClauseSyntax(elseIfKeyword, elseIfOpen, elseIfCondition, elseIfClose, ParseScriptBlock()));
        }

        PowerShellElseClauseSyntax? elseClause = null;
        if (PeekKeywordAfterTrivia() == "else")
        {
            elseClause = new PowerShellElseClauseSyntax(ReadKeywordToken(), ParseScriptBlock());
        }

        return new PowerShellIfStatementSyntax(ifKeyword, openParen, condition, closeParen, body, ParserHelpers.List(elseIfClauses), elseClause);
    }

    private PowerShellWhileStatementSyntax ParseWhileStatement()
    {
        var keyword = ReadKeywordToken();
        var (openParen, condition, closeParen) = ParseParenthesizedCondition();

        return new PowerShellWhileStatementSyntax(keyword, openParen, condition, closeParen, ParseScriptBlock());
    }

    private PowerShellDoStatementSyntax ParseDoStatement()
    {
        var doKeyword = ReadKeywordToken();
        var body = ParseScriptBlock();
        var conditionKeyword = ExpectKeyword("while", "until");
        var (openParen, condition, closeParen) = ParseParenthesizedCondition();

        return new PowerShellDoStatementSyntax(doKeyword, body, conditionKeyword, openParen, condition, closeParen);
    }

    private PowerShellForStatementSyntax ParseForStatement()
    {
        var forKeyword = ReadKeywordToken();
        AccumulateStatementTrivia();
        if (_lexer.Current != '(')
        {
            var missingOpen = ExpectCharacter('(', SyntaxKind.OpenParenToken);

            return new PowerShellForStatementSyntax(forKeyword, missingOpen, null, default, null, default, null, MissingToken(SyntaxKind.CloseParenToken, _lexer.Position), ParseScriptBlock());
        }

        var openParen = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);

        var initializer = ParseOptionalClauseExpression();
        var firstSemicolon = TryReadCharacter(';', SyntaxKind.SemicolonToken);
        var condition = ParseOptionalClauseExpression();
        var secondSemicolon = TryReadCharacter(';', SyntaxKind.SemicolonToken);
        var iterator = ParseOptionalClauseExpression();
        var closeParen = ExpectCharacter(')', SyntaxKind.CloseParenToken);

        return new PowerShellForStatementSyntax(forKeyword, openParen, initializer, firstSemicolon, condition, secondSemicolon, iterator, closeParen, ParseScriptBlock());
    }

    /// <summary>Reads one clause of a <c>for</c> header, which may be empty.</summary>
    private ShellSyntaxNode? ParseOptionalClauseExpression()
    {
        AccumulateStatementTrivia();
        if (_lexer.IsAtEnd || _lexer.Current is ';' or ')')
            return null;

        return ParseClause();
    }

    /// <summary>
    /// Reads one loop clause. A clause is usually an expression, but PowerShell also accepts a pipeline there, as in
    /// <c>for ($i = 0; Test-Path $p; $i++)</c> or <c>foreach ($x in Get-ChildItem -Directory)</c>.
    /// </summary>
    private ShellSyntaxNode ParseClause() => ParseClause(ParseExpression);

    private ShellSyntaxNode ParseClause(Func<ShellExpressionSyntax> parseExpression)
    {
        if (!IsExpressionStart())
            return ParseAndOrList();

        var expression = parseExpression();
        AccumulateInlineTrivia();

        if (_lexer.Current == '|' && _lexer.Peek(1) != '|')
            return ContinueAndOrList(ContinuePipeline(new PowerShellExpressionStatementSyntax(expression, null)));

        return (_lexer.Current, _lexer.Peek(1)) is ('&', '&') or ('|', '|') && _options.Dialect.HasFeature(ShellDialectFeatures.PipelineChainOperators)
            ? ContinueAndOrList(new PowerShellExpressionStatementSyntax(expression, null))
            : expression;
    }

    private PowerShellForEachStatementSyntax ParseForEachStatement()
    {
        var forEachKeyword = ReadKeywordToken();
        AccumulateStatementTrivia();
        if (_lexer.Current != '(')
        {
            var missingOpen = ExpectCharacter('(', SyntaxKind.OpenParenToken);
            var missingVariable = new PowerShellVariableExpressionSyntax(MissingToken(SyntaxKind.DollarToken, _lexer.Position), MissingToken(SyntaxKind.VariableNameToken, _lexer.Position));

            return new PowerShellForEachStatementSyntax(
                forEachKeyword,
                missingOpen,
                missingVariable,
                MissingToken(SyntaxKind.KeywordToken, _lexer.Position),
                new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellBareWord, MissingToken(SyntaxKind.GenericToken, _lexer.Position)),
                MissingToken(SyntaxKind.CloseParenToken, _lexer.Position),
                ParseScriptBlock());
        }

        var openParen = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        AccumulateStatementTrivia();
        var variable = ExpectVariable();
        var inKeyword = ExpectKeyword("in");
        AccumulateStatementTrivia();
        var collection = ParseClause();
        var closeParen = ExpectCharacter(')', SyntaxKind.CloseParenToken);

        return new PowerShellForEachStatementSyntax(forEachKeyword, openParen, variable, inKeyword, collection, closeParen, ParseScriptBlock());
    }

    private PowerShellSwitchStatementSyntax ParseSwitchStatement()
    {
        var switchKeyword = ReadKeywordToken();

        var parameters = new List<ScannedToken>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.Current != '-' || !PowerShellLexer.IsNameStart(_lexer.Peek(1)))
                break;

            var parameter = ReadParameterToken();
            if (!IsSwitchParameter(parameter.Text))
            {
                AddDiagnostic(parameter.Span, "SHELL0002", $"'{parameter.Text}' is not a valid switch statement parameter.");
            }

            parameters.Add(parameter);
        }

        // `switch -File data.txt { }` gives the value without parentheses.
        AccumulateStatementTrivia();
        ScannedToken openParen = default;
        ScannedToken closeParen = default;
        ShellStatementListSyntax condition;
        if (_lexer.Current == '(')
        {
            (openParen, condition, closeParen) = ParseParenthesizedCondition();
        }
        else if (_lexer.IsAtEnd || _lexer.Current is '{' or ';' or ')' or '}')
        {
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0023", "Expected the value to switch on.");
            condition = new ShellStatementListSyntax(null);
        }
        else
        {
            // Only the value belongs to the condition; the `{` that follows opens the clause list.
            var value = IsExpressionStart()
                ? (ShellSyntaxNode)ParseTernaryExpression()
                : ParseCommandWord();

            condition = new ShellStatementListSyntax(new PowerShellExpressionStatementSyntax(value, null));
        }

        AccumulateStatementTrivia();
        if (_lexer.Current != '{')
        {
            // Without the `{` there are no clauses to read; the rest belongs to whatever comes next.
            var missingOpen = ExpectCharacter('{', SyntaxKind.OpenBraceToken);

            return new PowerShellSwitchStatementSyntax(switchKeyword, ParserHelpers.TokenList(parameters), openParen, condition, closeParen, missingOpen, null, MissingToken(SyntaxKind.CloseBraceToken, _lexer.Position));
        }

        var openBrace = ReadOperatorToken(SyntaxKind.OpenBraceToken, length: 1);

        var clauses = new List<PowerShellSwitchClauseSyntax>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == '}')
                break;

            // A clause that is not a value followed by a block ends the clause list here; what remains is left to
            // the enclosing statement list rather than read as clauses that are not there.
            if (_lexer.Current == ')')
                break;

            if (_lexer.Current == ';')
            {
                AddUnexpectedTokenDiagnostic();
                break;
            }

            var positionBefore = _lexer.Position;
            var pattern = ParseSwitchPattern();
            AccumulateStatementTrivia();
            if (_lexer.Current != '{')
            {
                clauses.Add(new PowerShellSwitchClauseSyntax(pattern, ParseScriptBlock(), default));
                break;
            }

            var body = ParseScriptBlock();

            // Clauses are separated by line breaks or by `;`.
            AccumulateInlineTrivia();
            var separator = _lexer.Current == ';' ? ReadSemicolonRun() : default;
            clauses.Add(new PowerShellSwitchClauseSyntax(pattern, body, separator));

            if (_lexer.Position == positionBefore)
                break;
        }

        if (clauses.Count == 0 && _lexer.Current == '}')
        {
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0023", "Expected a switch clause.");
        }

        return new PowerShellSwitchStatementSyntax(switchKeyword, ParserHelpers.TokenList(parameters), openParen, condition, closeParen, openBrace, ParserHelpers.List(clauses), ExpectCharacter('}', SyntaxKind.CloseBraceToken));
    }

    /// <summary>Returns whether <paramref name="parameter"/> names a switch statement option, which may be abbreviated.</summary>
    private static bool IsSwitchParameter(string parameter)
    {
        var name = parameter[1..];
        foreach (var candidate in (ReadOnlySpan<string>)["regex", "wildcard", "exact", "casesensitive", "file", "parallel"])
        {
            if (candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Reads a switch clause pattern, which is a value, the <c>default</c> keyword, or a condition block.</summary>
    private ShellSyntaxNode ParseSwitchPattern()
    {
        AccumulateStatementTrivia();
        if (_lexer.Current == '{')
            return ParseScriptBlock();

        // A pattern is read like a command argument, so `[int]` is the text "[int]" rather than a type.
        if (_lexer.Current == '[')
            return ParseCommandWord();

        if (IsExpressionStart())
            return ParseTernaryExpression();

        return new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellBareWord, ReadBareToken());
    }

    private PowerShellTryStatementSyntax ParseTryStatement()
    {
        var tryKeyword = ReadKeywordToken();
        var body = ParseScriptBlock();

        var catchClauses = new List<PowerShellCatchClauseSyntax>();
        while (PeekKeywordAfterTrivia() == "catch")
        {
            var catchKeyword = ReadKeywordToken();
            var types = new List<PowerShellTypeLiteralSyntax>();
            var separators = new List<ScannedToken>();
            while (true)
            {
                AccumulateStatementTrivia();
                if (_lexer.Current != '[')
                {
                    if (separators.Count > 0)
                    {
                        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", "Expected a type after ','.");
                    }

                    break;
                }

                types.Add(ParseTypeLiteral());
                AccumulateStatementTrivia();
                if (_lexer.Current != ',')
                    break;

                separators.Add(ReadOperatorToken(SyntaxKind.CommaToken, length: 1));
            }

            catchClauses.Add(new PowerShellCatchClauseSyntax(catchKeyword, ParserHelpers.Separated(types, separators), ParseScriptBlock()));
        }

        PowerShellFinallyClauseSyntax? finallyClause = null;
        if (PeekKeywordAfterTrivia() == "finally")
        {
            finallyClause = new PowerShellFinallyClauseSyntax(ReadKeywordToken(), ParseScriptBlock());
        }
        else if (catchClauses.Count == 0 && body.GetSlot(2) is { IsMissing: false })
        {
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", "Expected 'catch' or 'finally'.");
        }

        return new PowerShellTryStatementSyntax(tryKeyword, body, ParserHelpers.List(catchClauses), finallyClause);
    }

    private PowerShellTrapStatementSyntax ParseTrapStatement()
    {
        var trapKeyword = ReadKeywordToken();
        AccumulateStatementTrivia();
        var typeFilter = _lexer.Current == '[' ? ParseTypeLiteral() : null;

        return new PowerShellTrapStatementSyntax(trapKeyword, typeFilter, ParseScriptBlock());
    }

    private PowerShellFunctionDefinitionSyntax ParseFunctionDefinition(SyntaxKind kind)
    {
        var keyword = ReadKeywordToken();

        // A function name is a command-mode word, so `function 1` is allowed, but a quoted string or a variable is not.
        AccumulateStatementTrivia();
        var nameToken = _lexer.Current is '\'' or '"' or '$' or '@'
            ? ReportMissingName()
            : ReadBareToken();

        ScannedToken openParen = default;
        ScannedToken closeParen = default;
        var parameters = new List<PowerShellParameterSyntax>();
        var separators = new List<ScannedToken>();

        AccumulateStatementTrivia();
        if (_lexer.Current == '(')
        {
            openParen = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
            ParseParameterList(parameters, separators);
            closeParen = ExpectCharacter(')', SyntaxKind.CloseParenToken);
        }

        return new PowerShellFunctionDefinitionSyntax(kind, keyword, nameToken, openParen, ParserHelpers.Separated(parameters, separators), closeParen, ParseScriptBlock());
    }

    private PowerShellParamBlockSyntax ParseParamBlock(IReadOnlyList<PowerShellAttributeSyntax> attributes)
    {
        var paramKeyword = ReadKeywordToken();
        var openParen = ExpectCharacter('(', SyntaxKind.OpenParenToken);
        var parameters = new List<PowerShellParameterSyntax>();
        var separators = new List<ScannedToken>();
        ParseParameterList(parameters, separators);

        return new PowerShellParamBlockSyntax(ParserHelpers.List(attributes), paramKeyword, openParen, ParserHelpers.Separated(parameters, separators), ExpectCharacter(')', SyntaxKind.CloseParenToken));
    }

    private void ParseParameterList(List<PowerShellParameterSyntax> parameters, List<ScannedToken> separators)
    {
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
            {
                if (separators.Count > 0)
                {
                    AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0013", "Expected a parameter after ','.");
                }

                break;
            }

            var positionBefore = _lexer.Position;
            parameters.Add(ParseParameter());

            AccumulateStatementTrivia();
            if (_lexer.Current != ',')
                break;

            separators.Add(ReadOperatorToken(SyntaxKind.CommaToken, length: 1));
            if (_lexer.Position == positionBefore)
                break;
        }
    }

    private PowerShellParameterSyntax ParseParameter()
    {
        var attributes = ParseAttributeList();
        AccumulateStatementTrivia();
        var variable = ExpectVariable();

        ScannedToken equalsToken = default;
        ShellSyntaxNode? defaultValue = null;
        AccumulateStatementTrivia();
        if (_lexer.Current == '=' && _lexer.Peek(1) != '=')
        {
            equalsToken = ReadOperatorToken(SyntaxKind.EqualsToken, length: 1);
            AccumulateStatementTrivia();
            defaultValue = ParseTernaryExpression();
        }

        return new PowerShellParameterSyntax(ParserHelpers.List(attributes), variable, equalsToken, defaultValue);
    }

    private List<PowerShellAttributeSyntax> ParseAttributeList()
    {
        var attributes = new List<PowerShellAttributeSyntax>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.Current != '[')
                break;

            var positionBefore = _lexer.Position;
            attributes.Add(ParseAttribute());
            if (_lexer.Position == positionBefore)
                break;
        }

        return attributes;
    }

    private PowerShellAttributeSyntax ParseAttribute()
    {
        var openBracket = ReadOperatorToken(SyntaxKind.OpenBracketToken, length: 1);
        var nameToken = ReadTypeNameToken(insideBrackets: true);

        ScannedToken openParen = default;
        ScannedToken closeParen = default;
        var arguments = new List<ShellExpressionSyntax>();
        var separators = new List<ScannedToken>();

        AccumulateInlineTrivia();
        if (_lexer.Current == '(')
        {
            openParen = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
            while (true)
            {
                AccumulateStatementTrivia();
                if (_lexer.IsAtEnd || _lexer.Current == ')')
                    break;

                var positionBefore = _lexer.Position;
                arguments.Add(ParseAttributeArgument());
                AccumulateStatementTrivia();
                if (_lexer.Current == ',')
                {
                    separators.Add(ReadOperatorToken(SyntaxKind.CommaToken, length: 1));
                    continue;
                }

                if (_lexer.Position == positionBefore)
                {
                    _lexer.Position++;
                }

                break;
            }

            closeParen = ExpectCharacter(')', SyntaxKind.CloseParenToken);
        }

        return new PowerShellAttributeSyntax(openBracket, nameToken, openParen, ParserHelpers.Separated(arguments, separators), closeParen, ExpectCharacter(']', SyntaxKind.CloseBracketToken));
    }

    /// <summary>
    /// Reads one attribute argument. Attributes take either positional values or named ones written
    /// <c>Name = value</c>, which assignment-level parsing would confuse with the surrounding comma list.
    /// </summary>
    private ShellExpressionSyntax ParseAttributeArgument()
    {
        // A positional argument or the name of a named one may be a bare word, as in `[Parameter(Mandatory)]`.
        var value = PowerShellLexer.IsNameStart(_lexer.Current)
            ? new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellBareWord, ReadIdentifierToken())
            : ParseTernaryExpression();

        AccumulateStatementTrivia();
        if (_lexer.Current != '=' || _lexer.Peek(1) == '=')
            return value;

        var equalsToken = ReadOperatorToken(SyntaxKind.EqualsToken, length: 1);
        AccumulateStatementTrivia();

        return new PowerShellAssignmentExpressionSyntax(value, equalsToken, ParseTernaryExpression());
    }

    private PowerShellNamedBlockSyntax ParseNamedBlock(string keyword)
    {
        var kind = keyword switch
        {
            "begin" => SyntaxKind.PowerShellBeginBlock,
            "process" => SyntaxKind.PowerShellProcessBlock,
            "end" => SyntaxKind.PowerShellEndBlock,
            "clean" => SyntaxKind.PowerShellCleanBlock,
            _ => SyntaxKind.PowerShellDynamicParamBlock,
        };

        return new(kind, ReadKeywordToken(), ParseScriptBlock());
    }

    private PowerShellTypeDefinitionSyntax ParseTypeDefinition(IReadOnlyList<PowerShellAttributeSyntax> attributes)
    {
        var keyword = ReadKeywordToken();
        var kind = string.Equals(keyword.Text, "enum", StringComparison.OrdinalIgnoreCase)
            ? SyntaxKind.PowerShellEnumDefinition
            : SyntaxKind.PowerShellClassDefinition;

        // Unlike a function name, a type name has to be a plain identifier.
        AccumulateStatementTrivia();
        var nameToken = PowerShellLexer.IsNameStart(_lexer.Current) ? ReadBareToken() : ReportMissingName();

        ScannedToken colonToken = default;
        var baseTypes = new List<PowerShellTypeLiteralSyntax>();
        var baseSeparators = new List<ScannedToken>();

        AccumulateStatementTrivia();
        if (_lexer.Current == ':')
        {
            colonToken = ReadOperatorToken(SyntaxKind.ColonToken, length: 1);
            while (true)
            {
                AccumulateStatementTrivia();
                if (_lexer.IsAtEnd || _lexer.Current == '{')
                {
                    AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0013", "Expected a type name.");
                    break;
                }

                baseTypes.Add(ParseBaseTypeReference());
                AccumulateStatementTrivia();
                if (_lexer.Current != ',')
                    break;

                baseSeparators.Add(ReadOperatorToken(SyntaxKind.CommaToken, length: 1));
            }
        }

        AccumulateStatementTrivia();
        if (_lexer.Current != '{')
        {
            var missingOpen = ExpectCharacter('{', SyntaxKind.OpenBraceToken);

            return new PowerShellTypeDefinitionSyntax(kind, ParserHelpers.List(attributes), keyword, nameToken, colonToken, ParserHelpers.Separated(baseTypes, baseSeparators), missingOpen, new ShellStatementListSyntax(null), MissingToken(SyntaxKind.CloseBraceToken, _lexer.Position));
        }

        var openBrace = ReadOperatorToken(SyntaxKind.OpenBraceToken, length: 1);
        var members = ParseStatementList(stopCharacter: '}', StatementListKind.TypeBody);

        return new PowerShellTypeDefinitionSyntax(kind, ParserHelpers.List(attributes), keyword, nameToken, colonToken, ParserHelpers.Separated(baseTypes, baseSeparators), openBrace, members, ExpectCharacter('}', SyntaxKind.CloseBraceToken));
    }

    /// <summary>Reports a missing name without consuming anything, for a declaration whose name is not a valid one.</summary>
    private ScannedToken ReportMissingName()
    {
        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0013", "Expected a name.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(SyntaxKind.GenericToken, fullStart, trivia);
    }

    /// <summary>Reads the variable a parameter or a <c>foreach</c> loop declares, reporting anything else.</summary>
    private PowerShellVariableExpressionSyntax ExpectVariable()
    {
        AccumulateStatementTrivia();
        if (_lexer.Current == '$')
            return ParseVariableExpression();

        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0013", "Expected a variable name.");
        var (trivia, fullStart) = TakeTrivia();

        return new PowerShellVariableExpressionSyntax(MissingToken(SyntaxKind.DollarToken, fullStart, trivia), MissingToken(SyntaxKind.VariableNameToken, _lexer.Position));
    }

    /// <summary>A base type in a class declaration is written without brackets, unlike a type literal.</summary>
    private PowerShellTypeLiteralSyntax ParseBaseTypeReference()
    {
        if (_lexer.Current == '[')
            return ParseTypeLiteral();

        var nameToken = ReadTypeNameToken();
        var empty = MissingToken(SyntaxKind.OpenBracketToken, nameToken.FullSpan.Start);

        return new PowerShellTypeLiteralSyntax(empty, nameToken, MissingToken(SyntaxKind.CloseBracketToken, _lexer.Position));
    }

    private PowerShellFlowStatementSyntax ParseFlowStatement(SyntaxKind kind)
    {
        var keyword = ReadKeywordToken();

        ShellSyntaxNode? value = null;
        AccumulateInlineTrivia();
        if (!_lexer.IsAtEnd
            && SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) == 0
            && _lexer.Current is not ';' and not '}' and not ')' and not '|')
        {
            // `break` and `continue` take a label, but `return`, `throw`, and `exit` take a whole pipeline, which is
            // why `return $x | Where-Object { $_ }` returns the piped result rather than piping the return statement.
            if (kind is SyntaxKind.PowerShellBreakStatement or SyntaxKind.PowerShellContinueStatement)
            {
                value = IsExpressionStart() ? ParseExpression() : ParseCommandWord();
            }
            else
            {
                value = ParseClause();
            }
        }

        return new PowerShellFlowStatementSyntax(kind, keyword, value);
    }

    private PowerShellUsingStatementSyntax ParseUsingStatement()
    {
        var usingKeyword = ReadKeywordToken();

        AccumulateInlineTrivia();
        var kind = PeekKeyword();
        ScannedToken kindToken;
        if (kind is "namespace" or "module" or "assembly")
        {
            kindToken = ReadBareToken();
        }
        else
        {
            // `using` alone, or followed by anything but its three directives, is not a statement PowerShell knows.
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", "Expected 'namespace', 'module', or 'assembly'.");
            var (trivia, fullStart) = TakeTrivia();
            kindToken = MissingToken(SyntaxKind.GenericToken, fullStart, trivia);
        }

        AccumulateInlineTrivia();
        ShellSyntaxNode target;
        if (_lexer.IsAtEnd || IsAtStatementEnd('\0'))
        {
            if (kindToken.Green is { IsMissing: false })
            {
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0013", $"Expected a name after 'using {kindToken.Text}'.");
            }

            var (trivia, fullStart) = TakeTrivia();
            target = new ShellWordSyntax(GreenFactory.List([new ShellLiteralWordPartSyntax(MissingToken(SyntaxKind.GenericToken, fullStart, trivia))]));
        }
        else
        {
            target = IsExpressionStart() ? ParseExpression() : ParseCommandWord();
        }

        return new PowerShellUsingStatementSyntax(usingKeyword, kindToken, target);
    }

    private PowerShellDataStatementSyntax ParseDataStatement()
    {
        var dataKeyword = ReadKeywordToken();

        // The section name, which is optional, is an identifier; the block may start on a following line.
        ScannedToken nameToken = default;
        AccumulateStatementTrivia();
        if (PowerShellLexer.IsNameStart(_lexer.Current))
        {
            nameToken = ReadBareToken();
        }

        // `-SupportedCommand` is the only parameter a data section takes, and it needs a list of commands after it.
        var parameters = new List<ScannedToken>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.Current != '-' || !PowerShellLexer.IsNameStart(_lexer.Peek(1)))
                break;

            var parameter = ReadParameterToken();
            parameters.Add(parameter);
            if (!"-supportedcommand".StartsWith(parameter.Text, StringComparison.OrdinalIgnoreCase))
            {
                AddDiagnostic(parameter.Span, "SHELL0002", $"'{parameter.Text}' is not a valid data section parameter.");
            }

            var argumentCount = 0;
            while (true)
            {
                AccumulateInlineTrivia();
                if (_lexer.IsAtEnd || _lexer.Current is '{' or '-' || IsAtStatementEnd('\0'))
                    break;

                parameters.Add(ReadDataSectionArgumentToken());
                argumentCount++;
            }

            if (argumentCount == 0)
            {
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0013", $"Expected a command name after '{parameter.Text}'.");
            }
        }

        return new PowerShellDataStatementSyntax(dataKeyword, nameToken, ParserHelpers.TokenList(parameters), ParseScriptBlock());
    }

    /// <summary>Reads one word of a data section's command list, such as <c>ConvertFrom-StringData,</c>.</summary>
    private ScannedToken ReadDataSectionArgumentToken()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        while (scan < text.Length && !char.IsWhiteSpace(text[scan]) && text[scan] is not '{' and not ';' and not '}' and not ')')
        {
            scan++;
        }

        return ReadOperatorToken(SyntaxKind.GenericToken, Math.Max(1, scan - _lexer.Position));
    }

    private PowerShellLabeledStatementSyntax ParseLabeledStatement()
    {
        var start = _lexer.Position;
        var (trivia, fullStart) = TakeTrivia();
        _lexer.Position++;
        while (!_lexer.IsAtEnd && PowerShellLexer.IsNameCharacter(_lexer.Current))
        {
            _lexer.Position++;
        }

        var labelToken = _lexer.CreateToken(SyntaxKind.LabelToken, start, trivia, fullStart);

        return new PowerShellLabeledStatementSyntax(labelToken, ParseStatement());
    }

    // ---- shared pieces ----

    private (ScannedToken OpenParen, ShellStatementListSyntax Condition, ScannedToken CloseParen) ParseParenthesizedCondition()
    {
        AccumulateStatementTrivia();
        if (_lexer.Current != '(')
        {
            // Without the `(` there is no condition to read; the rest belongs to whatever comes next.
            var openParen = ExpectCharacter('(', SyntaxKind.OpenParenToken);

            return (openParen, new ShellStatementListSyntax(null), MissingToken(SyntaxKind.CloseParenToken, _lexer.Position));
        }

        var open = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        var (condition, closeParen) = ParseParenthesizedPipeline();

        return (open, condition, closeParen);
    }

    /// <summary>
    /// Reads the single pipeline between an already-read <c>(</c> and its <c>)</c>. Unlike <c>$( )</c>, parentheses
    /// hold exactly one pipeline, so <c>(1; 2)</c> is an error and a line break cannot separate two statements there.
    /// </summary>
    private (ShellStatementListSyntax Statements, ScannedToken CloseParen) ParseParenthesizedPipeline()
    {
        AccumulateStatementTrivia();
        if (_lexer.Current == ')' || _lexer.IsAtEnd)
        {
            if (!_allowEmptyParentheses || _lexer.IsAtEnd)
            {
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0023", "Expected an expression.");
            }


            return (new ShellStatementListSyntax(null), ExpectCharacter(')', SyntaxKind.CloseParenToken));
        }

        if (_lexer.Current is ';' or '}')
        {
            AddUnexpectedTokenDiagnostic();

            return (new ShellStatementListSyntax(null), MissingToken(SyntaxKind.CloseParenToken, _lexer.Position));
        }

        var statement = ParseStatement();

        // `(Start-Job { } &)` runs the pipeline in the background.
        AccumulateInlineTrivia();
        ShellStatementListSyntax statements;
        if (_lexer.Current == '&' && _lexer.Peek(1) != '&' && _options.Dialect.HasFeature(ShellDialectFeatures.PipelineChainOperators))
        {
            statements = new ShellStatementListSyntax(ParserHelpers.Separated(new List<ShellStatementSyntax> { statement }, [ReadOperatorToken(SyntaxKind.AmpersandToken, length: 1)]));
        }
        else
        {
            statements = new ShellStatementListSyntax(ParserHelpers.Separated(new List<ShellStatementSyntax> { statement }, separators: null));
        }

        return (statements, ExpectCharacter(')', SyntaxKind.CloseParenToken));
    }

    private PowerShellScriptBlockSyntax ParseScriptBlock()
    {
        AccumulateStatementTrivia();
        if (_lexer.Current != '{')
        {
            // A missing body is reported once; reading statements for it would swallow the rest of the script.
            var missingOpen = ExpectCharacter('{', SyntaxKind.OpenBraceToken);

            return new PowerShellScriptBlockSyntax(missingOpen, new ShellStatementListSyntax(null), MissingToken(SyntaxKind.CloseBraceToken, _lexer.Position));
        }

        var openBrace = ReadOperatorToken(SyntaxKind.OpenBraceToken, length: 1);
        var statements = ParseStatementList(stopCharacter: '}', StatementListKind.ScriptBlock);

        return new PowerShellScriptBlockSyntax(openBrace, statements, ExpectCharacter('}', SyntaxKind.CloseBraceToken));
    }

    // ---- token helpers ----

    /// <summary>Returns the lowercase identifier at the current position without consuming it.</summary>
    private string? PeekKeyword()
    {
        var text = _lexer.Text;
        var start = _lexer.Position;
        if (start >= text.Length || !PowerShellLexer.IsNameStart(text[start]))
            return null;

        var scan = start;
        while (scan < text.Length && PowerShellLexer.IsNameCharacter(text[scan]))
        {
            scan++;
        }

        // A keyword must not be glued to more argument text, as in `iffy` or `for-each`.
        if (scan < text.Length && !PowerShellLexer.IsArgumentBoundary(text[scan]) && text[scan] is not '(' and not '{')
            return null;

        return text[start..scan].ToLowerInvariant();
    }

    private string? PeekKeywordAfterTrivia()
    {
        AccumulateStatementTrivia();

        return PeekKeyword();
    }

    /// <summary>Looks past a run of <c>[...]</c> attributes and returns the keyword that follows, without consuming anything.</summary>
    private string? PeekKeywordAfterAttributes()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        if (scan >= text.Length || text[scan] != '[')
            return null;

        while (scan < text.Length && text[scan] == '[')
        {
            var depth = 0;
            while (scan < text.Length)
            {
                if (text[scan] == '[')
                {
                    depth++;
                }
                else if (text[scan] == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        scan++;
                        break;
                    }
                }

                scan++;
            }

            while (scan < text.Length && (char.IsWhiteSpace(text[scan]) || text[scan] == '`'))
            {
                scan++;
            }
        }

        var start = scan;
        while (scan < text.Length && PowerShellLexer.IsNameCharacter(text[scan]))
        {
            scan++;
        }

        return scan == start ? null : text[start..scan].ToLowerInvariant();
    }

    private ScannedToken ReadKeywordToken()
    {
        AccumulateStatementTrivia();
        var keyword = PeekKeyword() ?? string.Empty;

        return ReadOperatorToken(SyntaxKind.KeywordToken, keyword.Length);
    }

    private ScannedToken ExpectKeyword(params string[] keywords)
    {
        AccumulateStatementTrivia();
        var actual = PeekKeyword();
        if (actual is not null && Array.IndexOf(keywords, actual) >= 0)
            return ReadOperatorToken(SyntaxKind.KeywordToken, actual.Length);

        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", $"Expected '{string.Join("' or '", keywords)}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(SyntaxKind.KeywordToken, fullStart, trivia);
    }

    private ScannedToken ExpectCharacter(char expected, SyntaxKind kind)
    {
        AccumulateStatementTrivia();
        if (_lexer.Current == expected)
            return ReadOperatorToken(kind, length: 1);

        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", $"Expected '{expected}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(kind, fullStart, trivia);
    }

    private ScannedToken TryReadCharacter(char expected, SyntaxKind kind)
    {
        AccumulateStatementTrivia();

        return _lexer.Current == expected ? ReadOperatorToken(kind, length: 1) : default;
    }

    /// <summary>Reads a bare identifier such as a function or class name.</summary>
    private ScannedToken ReadBareToken()
    {
        AccumulateStatementTrivia();
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start;
        while (scan < text.Length && !PowerShellLexer.IsArgumentBoundary(text[scan]) && text[scan] is not '{' and not '(' and not '=' and not ']')
        {
            scan++;
        }

        if (scan == start)
        {
            AddDiagnostic(new TextSpan(start, 0), "SHELL0013", "Expected a name.");
            var (trivia, fullStart) = TakeTrivia();

            return MissingToken(SyntaxKind.GenericToken, fullStart, trivia);
        }

        return ReadOperatorToken(SyntaxKind.GenericToken, scan - start);
    }

    /// <summary>Reads a type name, which may contain dots, generics, and array suffixes.</summary>
    /// <param name="includeArgumentList">
    /// When set, an attribute argument list is read as part of the name, so that <c>[ValidateRange(1, 5)]$x</c> is one
    /// type literal instead of an unterminated one. Attribute positions parse the argument list separately instead.
    /// </param>
    /// <param name="insideBrackets">
    /// When set, the name runs to the closing bracket, so an assembly-qualified name such as
    /// <c>[Some.Type, Some.Assembly]</c> is not cut short at the comma. A line break still ends it, so an unterminated
    /// bracket cannot swallow the rest of the file.
    /// </param>
    private ScannedToken ReadTypeNameToken(bool includeArgumentList = false, bool insideBrackets = false)
    {
        AccumulateInlineTrivia();
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start;
        var depth = 0;
        while (scan < text.Length)
        {
            var current = text[scan];

            // No type name contains these, so a bracket that runs into one of them is not closed, as in `[System).IO]`.
            if (current is ')' or ';' or '{' or '}' or '|' or '$' or '"' or '\'' or '&' or '@')
                break;

            if (current == '[')
            {
                depth++;
            }
            else if (current == ']')
            {
                if (depth == 0)
                    break;

                depth--;
            }
            else if (depth == 0 && current == '(')
            {
                if (!includeArgumentList)
                    break;

                scan = SkipBalancedParentheses(text, scan);
                continue;
            }
            else if (depth == 0 && SourceText.GetLineBreakLength(text, scan) > 0)
            {
                break;
            }
            else if (depth == 0 && !insideBrackets && (current is ',' || char.IsWhiteSpace(current)))
            {
                break;
            }

            scan++;
        }

        if (insideBrackets)
        {
            // Trailing whitespace belongs to the following token, not to the name.
            while (scan > start && char.IsWhiteSpace(text[scan - 1]))
            {
                scan--;
            }
        }

        if (scan == start)
        {
            var (trivia, fullStart) = TakeTrivia();

            return MissingToken(SyntaxKind.GenericToken, fullStart, trivia);
        }

        return ReadOperatorToken(SyntaxKind.GenericToken, scan - start);
    }

    /// <summary>Returns the index just past the parenthesized group that starts at <paramref name="index"/>.</summary>
    private static int SkipBalancedParentheses(string text, int index)
    {
        var depth = 0;
        var quote = '\0';
        while (index < text.Length)
        {
            var current = text[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }
            }
            else if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
                if (depth == 0)
                    return index + 1;
            }

            index++;
        }

        return index;
    }

    private ScannedToken ReadParameterToken()
    {
        AccumulateInlineTrivia();
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start + 1;
        while (scan < text.Length && PowerShellLexer.IsNameCharacter(text[scan]))
        {
            scan++;
        }

        return ReadOperatorToken(SyntaxKind.ParameterToken, scan - start);
    }
}
