namespace Meziantou.Framework.Language.Shell.Internals;

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

        return new PowerShellIfStatementSyntax(ifKeyword, openParen, condition, closeParen, body, elseIfClauses, elseClause);
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
        var openParen = ExpectCharacter('(', ShellSyntaxKind.OpenParenToken);

        var initializer = ParseOptionalClauseExpression();
        var firstSemicolon = TryReadCharacter(';', ShellSyntaxKind.SemicolonToken);
        var condition = ParseOptionalClauseExpression();
        var secondSemicolon = TryReadCharacter(';', ShellSyntaxKind.SemicolonToken);
        var iterator = ParseOptionalClauseExpression();
        var closeParen = ExpectCharacter(')', ShellSyntaxKind.CloseParenToken);

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
            return ParsePipeline();

        var expression = parseExpression();
        AccumulateInlineTrivia();

        return _lexer.Current == '|' && _lexer.Peek(1) != '|'
            ? ContinuePipeline(new PowerShellExpressionStatementSyntax(expression, []))
            : expression;
    }

    private PowerShellForEachStatementSyntax ParseForEachStatement()
    {
        var forEachKeyword = ReadKeywordToken();
        var openParen = ExpectCharacter('(', ShellSyntaxKind.OpenParenToken);
        AccumulateStatementTrivia();
        var variable = ParseVariableExpression();
        var inKeyword = ExpectKeyword("in");
        AccumulateStatementTrivia();
        var collection = ParseClause();
        var closeParen = ExpectCharacter(')', ShellSyntaxKind.CloseParenToken);

        return new PowerShellForEachStatementSyntax(forEachKeyword, openParen, variable, inKeyword, collection, closeParen, ParseScriptBlock());
    }

    private PowerShellSwitchStatementSyntax ParseSwitchStatement()
    {
        var switchKeyword = ReadKeywordToken();

        var parameters = new List<ShellSyntaxToken>();
        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current != '-' || !PowerShellLexer.IsNameStart(_lexer.Peek(1)))
                break;

            parameters.Add(ReadParameterToken());
        }

        // `switch -File data.txt { }` gives the value without parentheses.
        AccumulateStatementTrivia();
        ShellSyntaxToken? openParen = null;
        ShellSyntaxToken? closeParen = null;
        ShellStatementListSyntax condition;
        if (_lexer.Current == '(')
        {
            (openParen, condition, closeParen) = ParseParenthesizedCondition();
        }
        else
        {
            // Only the value belongs to the condition; the `{` that follows opens the clause list.
            var value = IsExpressionStart()
                ? (ShellSyntaxNode)ParseTernaryExpression()
                : ParseCommandWord();

            condition = new ShellStatementListSyntax([new PowerShellExpressionStatementSyntax(value, [])]);
        }

        var openBrace = ExpectCharacter('{', ShellSyntaxKind.OpenBraceToken);

        var clauses = new List<PowerShellSwitchClauseSyntax>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == '}')
                break;

            var positionBefore = _lexer.Position;
            var pattern = ParseSwitchPattern();
            var body = ParseScriptBlock();
            clauses.Add(new PowerShellSwitchClauseSyntax(pattern, body));

            if (_lexer.Position == positionBefore)
                break;
        }

        return new PowerShellSwitchStatementSyntax(switchKeyword, parameters, openParen, condition, closeParen, openBrace, clauses, ExpectCharacter('}', ShellSyntaxKind.CloseBraceToken));
    }

    /// <summary>Reads a switch clause pattern, which is a value, the <c>default</c> keyword, or a condition block.</summary>
    private ShellSyntaxNode ParseSwitchPattern()
    {
        AccumulateStatementTrivia();
        if (_lexer.Current == '{')
            return ParseScriptBlock();

        if (IsExpressionStart())
            return ParseTernaryExpression();

        return new PowerShellLiteralExpressionSyntax(ShellSyntaxKind.PowerShellBareWord, ReadBareToken());
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
            var separators = new List<ShellSyntaxToken>();
            while (true)
            {
                AccumulateStatementTrivia();
                if (_lexer.Current != '[')
                    break;

                types.Add(ParseTypeLiteral());
                AccumulateInlineTrivia();
                if (_lexer.Current != ',')
                    break;

                separators.Add(ReadOperatorToken(ShellSyntaxKind.CommaToken, length: 1));
            }

            catchClauses.Add(new PowerShellCatchClauseSyntax(catchKeyword, types, separators, ParseScriptBlock()));
        }

        PowerShellFinallyClauseSyntax? finallyClause = null;
        if (PeekKeywordAfterTrivia() == "finally")
        {
            finallyClause = new PowerShellFinallyClauseSyntax(ReadKeywordToken(), ParseScriptBlock());
        }

        return new PowerShellTryStatementSyntax(tryKeyword, body, catchClauses, finallyClause);
    }

    private PowerShellTrapStatementSyntax ParseTrapStatement()
    {
        var trapKeyword = ReadKeywordToken();
        AccumulateStatementTrivia();
        var typeFilter = _lexer.Current == '[' ? ParseTypeLiteral() : null;

        return new PowerShellTrapStatementSyntax(trapKeyword, typeFilter, ParseScriptBlock());
    }

    private PowerShellFunctionDefinitionSyntax ParseFunctionDefinition(ShellSyntaxKind kind)
    {
        var keyword = ReadKeywordToken();
        var nameToken = ReadBareToken();

        ShellSyntaxToken? openParen = null;
        ShellSyntaxToken? closeParen = null;
        var parameters = new List<PowerShellParameterSyntax>();
        var separators = new List<ShellSyntaxToken>();

        AccumulateStatementTrivia();
        if (_lexer.Current == '(')
        {
            openParen = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
            ParseParameterList(parameters, separators);
            closeParen = ExpectCharacter(')', ShellSyntaxKind.CloseParenToken);
        }

        return new PowerShellFunctionDefinitionSyntax(kind, keyword, nameToken, openParen, parameters, separators, closeParen, ParseScriptBlock());
    }

    private PowerShellParamBlockSyntax ParseParamBlock(IReadOnlyList<PowerShellAttributeSyntax> attributes)
    {
        var paramKeyword = ReadKeywordToken();
        var openParen = ExpectCharacter('(', ShellSyntaxKind.OpenParenToken);
        var parameters = new List<PowerShellParameterSyntax>();
        var separators = new List<ShellSyntaxToken>();
        ParseParameterList(parameters, separators);

        return new PowerShellParamBlockSyntax(attributes, paramKeyword, openParen, parameters, separators, ExpectCharacter(')', ShellSyntaxKind.CloseParenToken));
    }

    private void ParseParameterList(List<PowerShellParameterSyntax> parameters, List<ShellSyntaxToken> separators)
    {
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
                break;

            var positionBefore = _lexer.Position;
            parameters.Add(ParseParameter());

            AccumulateStatementTrivia();
            if (_lexer.Current != ',')
                break;

            separators.Add(ReadOperatorToken(ShellSyntaxKind.CommaToken, length: 1));
            if (_lexer.Position == positionBefore)
                break;
        }
    }

    private PowerShellParameterSyntax ParseParameter()
    {
        var attributes = ParseAttributeList();
        AccumulateStatementTrivia();
        var variable = ParseVariableExpression();

        ShellSyntaxToken? equalsToken = null;
        ShellSyntaxNode? defaultValue = null;
        AccumulateStatementTrivia();
        if (_lexer.Current == '=' && _lexer.Peek(1) != '=')
        {
            equalsToken = ReadOperatorToken(ShellSyntaxKind.EqualsToken, length: 1);
            AccumulateStatementTrivia();
            defaultValue = ParseTernaryExpression();
        }

        return new PowerShellParameterSyntax(attributes, variable, equalsToken, defaultValue);
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
        var openBracket = ReadOperatorToken(ShellSyntaxKind.OpenBracketToken, length: 1);
        var nameToken = ReadTypeNameToken(insideBrackets: true);

        ShellSyntaxToken? openParen = null;
        ShellSyntaxToken? closeParen = null;
        var arguments = new List<ShellExpressionSyntax>();
        var separators = new List<ShellSyntaxToken>();

        AccumulateInlineTrivia();
        if (_lexer.Current == '(')
        {
            openParen = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
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
                    separators.Add(ReadOperatorToken(ShellSyntaxKind.CommaToken, length: 1));
                    continue;
                }

                if (_lexer.Position == positionBefore)
                {
                    _lexer.Position++;
                }

                break;
            }

            closeParen = ExpectCharacter(')', ShellSyntaxKind.CloseParenToken);
        }

        return new PowerShellAttributeSyntax(openBracket, nameToken, openParen, arguments, separators, closeParen, ExpectCharacter(']', ShellSyntaxKind.CloseBracketToken));
    }

    /// <summary>
    /// Reads one attribute argument. Attributes take either positional values or named ones written
    /// <c>Name = value</c>, which assignment-level parsing would confuse with the surrounding comma list.
    /// </summary>
    private ShellExpressionSyntax ParseAttributeArgument()
    {
        var value = ParseTernaryExpression();

        AccumulateStatementTrivia();
        if (_lexer.Current != '=' || _lexer.Peek(1) == '=')
            return value;

        var equalsToken = ReadOperatorToken(ShellSyntaxKind.EqualsToken, length: 1);
        AccumulateStatementTrivia();

        return new PowerShellAssignmentExpressionSyntax(value, equalsToken, ParseTernaryExpression());
    }

    private PowerShellNamedBlockSyntax ParseNamedBlock(ShellSyntaxKind kind) => new(kind, ReadKeywordToken(), ParseScriptBlock());

    private PowerShellTypeDefinitionSyntax ParseTypeDefinition(IReadOnlyList<PowerShellAttributeSyntax> attributes)
    {
        var keyword = ReadKeywordToken();
        var kind = string.Equals(keyword.Text, "enum", StringComparison.OrdinalIgnoreCase)
            ? ShellSyntaxKind.PowerShellEnumDefinition
            : ShellSyntaxKind.PowerShellClassDefinition;

        var nameToken = ReadBareToken();

        ShellSyntaxToken? colonToken = null;
        var baseTypes = new List<PowerShellTypeLiteralSyntax>();
        var baseSeparators = new List<ShellSyntaxToken>();

        AccumulateStatementTrivia();
        if (_lexer.Current == ':')
        {
            colonToken = ReadOperatorToken(ShellSyntaxKind.ColonToken, length: 1);
            while (true)
            {
                AccumulateStatementTrivia();
                if (_lexer.IsAtEnd || _lexer.Current == '{')
                    break;

                baseTypes.Add(ParseBaseTypeReference());
                AccumulateInlineTrivia();
                if (_lexer.Current != ',')
                    break;

                baseSeparators.Add(ReadOperatorToken(ShellSyntaxKind.CommaToken, length: 1));
            }
        }

        var openBrace = ExpectCharacter('{', ShellSyntaxKind.OpenBraceToken);
        var members = ParseStatementList(stopCharacter: '}');

        return new PowerShellTypeDefinitionSyntax(kind, attributes, keyword, nameToken, colonToken, baseTypes, baseSeparators, openBrace, members, ExpectCharacter('}', ShellSyntaxKind.CloseBraceToken));
    }

    /// <summary>A base type in a class declaration is written without brackets, unlike a type literal.</summary>
    private PowerShellTypeLiteralSyntax ParseBaseTypeReference()
    {
        if (_lexer.Current == '[')
            return ParseTypeLiteral();

        var nameToken = ReadTypeNameToken();
        var empty = MissingToken(ShellSyntaxKind.OpenBracketToken, nameToken.FullSpan.Start);

        return new PowerShellTypeLiteralSyntax(empty, nameToken, MissingToken(ShellSyntaxKind.CloseBracketToken, _lexer.Position));
    }

    private PowerShellFlowStatementSyntax ParseFlowStatement(ShellSyntaxKind kind)
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
            if (kind is ShellSyntaxKind.PowerShellBreakStatement or ShellSyntaxKind.PowerShellContinueStatement)
            {
                value = IsExpressionStart() ? ParseExpression() : ParseCommandWord();
            }
            else if (IsExpressionStart())
            {
                var expression = ParseExpression();
                AccumulateInlineTrivia();
                value = _lexer.Current == '|' && _lexer.Peek(1) != '|'
                    ? ContinuePipeline(new PowerShellExpressionStatementSyntax(expression, []))
                    : expression;
            }
            else
            {
                value = ParsePipeline();
            }
        }

        return new PowerShellFlowStatementSyntax(kind, keyword, value);
    }

    private PowerShellUsingStatementSyntax ParseUsingStatement()
    {
        var usingKeyword = ReadKeywordToken();
        var kindToken = ReadBareToken();
        AccumulateInlineTrivia();
        ShellSyntaxNode target = IsExpressionStart() ? ParseExpression() : ParseCommandWord();

        return new PowerShellUsingStatementSyntax(usingKeyword, kindToken, target);
    }

    private PowerShellDataStatementSyntax ParseDataStatement()
    {
        var dataKeyword = ReadKeywordToken();

        ShellSyntaxToken? nameToken = null;
        AccumulateInlineTrivia();
        if (!_lexer.IsAtEnd && _lexer.Current is not '{' and not '-')
        {
            nameToken = ReadBareToken();
        }

        var parameters = new List<ShellSyntaxToken>();
        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current != '-' || !PowerShellLexer.IsNameStart(_lexer.Peek(1)))
                break;

            parameters.Add(ReadParameterToken());
        }

        return new PowerShellDataStatementSyntax(dataKeyword, nameToken, parameters, ParseScriptBlock());
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

        var labelToken = _lexer.CreateToken(ShellSyntaxKind.LabelToken, start, trivia, fullStart);

        return new PowerShellLabeledStatementSyntax(labelToken, ParseStatement());
    }

    // ---- shared pieces ----

    private (ShellSyntaxToken OpenParen, ShellStatementListSyntax Condition, ShellSyntaxToken CloseParen) ParseParenthesizedCondition()
    {
        var openParen = ExpectCharacter('(', ShellSyntaxKind.OpenParenToken);
        var condition = ParseStatementList(stopCharacter: ')');
        var closeParen = ExpectCharacter(')', ShellSyntaxKind.CloseParenToken);

        return (openParen, condition, closeParen);
    }

    private PowerShellScriptBlockSyntax ParseScriptBlock()
    {
        AccumulateStatementTrivia();
        var openBrace = ExpectCharacter('{', ShellSyntaxKind.OpenBraceToken);
        var statements = ParseStatementList(stopCharacter: '}');

        return new PowerShellScriptBlockSyntax(openBrace, statements, ExpectCharacter('}', ShellSyntaxKind.CloseBraceToken));
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

    private ShellSyntaxToken ReadKeywordToken()
    {
        AccumulateStatementTrivia();
        var keyword = PeekKeyword() ?? string.Empty;

        return ReadOperatorToken(ShellSyntaxKind.KeywordToken, keyword.Length);
    }

    private ShellSyntaxToken ExpectKeyword(params string[] keywords)
    {
        AccumulateStatementTrivia();
        var actual = PeekKeyword();
        if (actual is not null && Array.IndexOf(keywords, actual) >= 0)
            return ReadOperatorToken(ShellSyntaxKind.KeywordToken, actual.Length);

        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", $"Expected '{string.Join("' or '", keywords)}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(ShellSyntaxKind.KeywordToken, fullStart, trivia);
    }

    private ShellSyntaxToken ExpectCharacter(char expected, ShellSyntaxKind kind)
    {
        AccumulateStatementTrivia();
        if (_lexer.Current == expected)
            return ReadOperatorToken(kind, length: 1);

        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", $"Expected '{expected}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(kind, fullStart, trivia);
    }

    private ShellSyntaxToken? TryReadCharacter(char expected, ShellSyntaxKind kind)
    {
        AccumulateStatementTrivia();

        return _lexer.Current == expected ? ReadOperatorToken(kind, length: 1) : null;
    }

    /// <summary>Reads a bare identifier such as a function or class name.</summary>
    private ShellSyntaxToken ReadBareToken()
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

            return MissingToken(ShellSyntaxKind.GenericToken, fullStart, trivia);
        }

        return ReadOperatorToken(ShellSyntaxKind.GenericToken, scan - start);
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
    private ShellSyntaxToken ReadTypeNameToken(bool includeArgumentList = false, bool insideBrackets = false)
    {
        AccumulateInlineTrivia();
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start;
        var depth = 0;
        while (scan < text.Length)
        {
            var current = text[scan];
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

            return MissingToken(ShellSyntaxKind.GenericToken, fullStart, trivia);
        }

        return ReadOperatorToken(ShellSyntaxKind.GenericToken, scan - start);
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

    private ShellSyntaxToken ReadParameterToken()
    {
        AccumulateInlineTrivia();
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start + 1;
        while (scan < text.Length && PowerShellLexer.IsNameCharacter(text[scan]))
        {
            scan++;
        }

        return ReadOperatorToken(ShellSyntaxKind.ParameterToken, scan - start);
    }
}
