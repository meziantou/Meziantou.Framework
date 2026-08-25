namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>Compound statements: control flow, function definitions, groups, and here-documents.</summary>
internal sealed partial class PosixParser
{
    private static readonly string[] ThenWord = ["then"];
    private static readonly string[] IfBodyWords = ["elif", "else", "fi"];
    private static readonly string[] FiWord = ["fi"];
    private static readonly string[] DoWord = ["do"];
    private static readonly string[] DoneWord = ["done"];
    private static readonly string[] CloseBraceWord = ["}"];

    private ShellStatementSyntax ParseCommandOrCompound()
    {
        AccumulateInlineTrivia();

        if (!TryEnterRecursion(new TextSpan(_lexer.Position, 0)))
            return ConsumeRestAsSkippedText();

        try
        {
            return ParseCommandOrCompoundCore();
        }
        finally
        {
            _depth--;
        }
    }

    private ShellStatementSyntax ParseCommandOrCompoundCore()
    {
        var dialect = _options.Dialect;

        switch (PeekBareWord())
        {
            case "if":
                return ParseIfStatement();
            case "while":
                return ParseWhileStatement(ShellSyntaxKind.PosixWhileStatement);
            case "until":
                return ParseWhileStatement(ShellSyntaxKind.PosixUntilStatement);
            case "for":
                return ParseForStatement(ShellSyntaxKind.PosixForStatement);
            case "select" when dialect.HasFeature(ShellDialectFeatures.SelectLoop):
                return ParseForStatement(ShellSyntaxKind.PosixSelectStatement);
            case "case":
                return ParseCaseStatement();
            case "function" when dialect.HasFeature(ShellDialectFeatures.FunctionKeyword):
                return ParseFunctionDefinitionWithKeyword();
            case "time":
                return ParsePrefixedStatement(ShellSyntaxKind.PosixTimeStatement, hasName: false);
            case "coproc" when dialect.HasFeature(ShellDialectFeatures.Coproc):
                return ParsePrefixedStatement(ShellSyntaxKind.PosixCoprocStatement, hasName: true);
            case "{":
                return ParseBraceGroup();
        }

        if (_lexer.Current == '(' && _lexer.Peek(1) == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.Arithmetic))
            return ParseArithmeticCommand();

        if (_lexer.Current == '(')
            return ParseSubshell();

        // `[[` is a reserved word, so it only counts when it forms a whole word: `[[$x` is a command name.
        if (_lexer.Current == '[' && _lexer.Peek(1) == '[' && dialect.HasFeature(ShellDialectFeatures.ExtendedTest) && IsDelimiterAfter(2))
            return ParseConditionalExpression();

        if (TryParseFunctionDefinition(out var functionDefinition))
            return functionDefinition;

        return ParseSimpleCommand();
    }

    /// <summary>Returns whether the character <paramref name="offset"/> ahead is a word boundary or the end of input.</summary>
    private bool IsDelimiterAfter(int offset)
    {
        var index = _lexer.Position + offset;

        return index >= _lexer.Text.Length || PosixLexer.IsWordBoundary(_lexer.Text[index]);
    }

    // ---- control flow ----

    private PosixIfStatementSyntax ParseIfStatement()
    {
        var ifKeyword = ReadKeyword();
        var condition = ParseStatementList(ParseContext.UntilWords(ThenWord));
        var thenKeyword = ExpectKeyword("then");
        var body = ParseStatementList(ParseContext.UntilWords(IfBodyWords));

        var elifClauses = new List<PosixElifClauseSyntax>();
        while (PeekBareWordAfterTrivia() == "elif")
        {
            var elifKeyword = ReadKeyword();
            var elifCondition = ParseStatementList(ParseContext.UntilWords(ThenWord));
            var elifThenKeyword = ExpectKeyword("then");
            var elifBody = ParseStatementList(ParseContext.UntilWords(IfBodyWords));
            elifClauses.Add(new PosixElifClauseSyntax(elifKeyword, elifCondition, elifThenKeyword, elifBody));
        }

        PosixElseClauseSyntax? elseClause = null;
        if (PeekBareWordAfterTrivia() == "else")
        {
            var elseKeyword = ReadKeyword();
            elseClause = new PosixElseClauseSyntax(elseKeyword, ParseStatementList(ParseContext.UntilWords(FiWord)));
        }

        return new PosixIfStatementSyntax(ifKeyword, condition, thenKeyword, body, elifClauses, elseClause, ExpectKeyword("fi"));
    }

    private PosixWhileStatementSyntax ParseWhileStatement(ShellSyntaxKind kind)
    {
        var keyword = ReadKeyword();
        var condition = ParseStatementList(ParseContext.UntilWords(DoWord));
        var doKeyword = ExpectKeyword("do");
        var body = ParseStatementList(ParseContext.UntilWords(DoneWord));

        return new PosixWhileStatementSyntax(kind, keyword, condition, doKeyword, body, ExpectKeyword("done"));
    }

    private ShellStatementSyntax ParseForStatement(ShellSyntaxKind kind)
    {
        var keyword = ReadKeyword();
        AccumulateInlineTrivia();

        // bash's C-style loop, `for (( i = 0; i < n; i++ ))`, keeps its header verbatim.
        if (_lexer.Current == '(' && _lexer.Peek(1) == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.Arithmetic))
        {
            var header = ParseArithmeticCommand();
            var cStyleDo = ExpectKeyword("do");
            var cStyleBody = ParseStatementList(ParseContext.UntilWords(DoneWord));

            return new PosixPrefixedStatementSyntax(
                kind,
                keyword,
                nameToken: null,
                new PosixWhileStatementSyntax(ShellSyntaxKind.PosixWhileStatement, HiddenKeyword(), new ShellStatementListSyntax([header]), cStyleDo, cStyleBody, ExpectKeyword("done")));
        }

        var variableToken = ReadBareWordToken(ShellSyntaxKind.VariableNameToken);

        ShellSyntaxToken? inKeyword = null;
        var items = new List<ShellWordSyntax>();
        if (PeekBareWordAfterTrivia() == "in")
        {
            inKeyword = ReadKeyword();
            while (true)
            {
                AccumulateInlineTrivia();
                if (_lexer.IsAtEnd || SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) > 0)
                    break;

                if (_lexer.Current is ';' or '&' or '|' or ')')
                    break;

                if (PosixLexer.IsWordBoundary(_lexer.Current) && !IsAtProcessSubstitution())
                    break;

                if (PeekBareWord() == "do")
                    break;

                items.Add(ParseWord());
            }
        }

        ShellSyntaxToken? listTerminatorToken = null;
        AccumulateInlineTrivia();
        if (_lexer.Current == ';')
        {
            listTerminatorToken = ReadOperatorToken(ShellSyntaxKind.SemicolonToken, length: 1);
        }

        var doKeyword = ExpectKeyword("do");
        var body = ParseStatementList(ParseContext.UntilWords(DoneWord));

        return new PosixForStatementSyntax(kind, keyword, variableToken, inKeyword, items, listTerminatorToken, doKeyword, body, ExpectKeyword("done"));
    }

    private PosixCaseStatementSyntax ParseCaseStatement()
    {
        var caseKeyword = ReadKeyword();
        AccumulateInlineTrivia();
        var subject = _lexer.IsAtEnd || PosixLexer.IsWordBoundary(_lexer.Current)
            ? new ShellWordSyntax([])
            : ParseWord();

        var inKeyword = ExpectKeyword("in");
        var clauses = new List<PosixCaseClauseSyntax>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || PeekBareWord() == "esac")
                break;

            var positionBefore = _lexer.Position;
            var clause = ParseCaseClause();
            clauses.Add(clause);

            if (clause.TerminatorToken is null || _lexer.Position == positionBefore)
                break;
        }

        return new PosixCaseStatementSyntax(caseKeyword, subject, inKeyword, clauses, ExpectKeyword("esac"));
    }

    private PosixCaseClauseSyntax ParseCaseClause()
    {
        AccumulateStatementTrivia();

        ShellSyntaxToken? openParenToken = null;
        if (_lexer.Current == '(')
        {
            openParenToken = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
        }

        var patterns = new List<ShellWordSyntax>();
        var separators = new List<ShellSyntaxToken>();
        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
                break;

            if (PosixLexer.IsWordBoundary(_lexer.Current) && !IsAtProcessSubstitution())
                break;

            patterns.Add(ParseWord());
            AccumulateInlineTrivia();
            if (_lexer.Current != '|' || _lexer.Peek(1) == '|')
                break;

            separators.Add(ReadOperatorToken(ShellSyntaxKind.PipeToken, length: 1));
        }

        ShellSyntaxToken closeParenToken;
        AccumulateInlineTrivia();
        if (_lexer.Current == ')')
        {
            closeParenToken = ReadOperatorToken(ShellSyntaxKind.CloseParenToken, length: 1);
        }
        else
        {
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0008", "Expected ')' after the case pattern.");
            closeParenToken = MissingToken(ShellSyntaxKind.CloseParenToken, _lexer.Position);
        }

        var body = ParseStatementList(ParseContext.CaseClauseBody);

        ShellSyntaxToken? terminatorToken = null;
        AccumulateStatementTrivia();
        if (IsAtCaseTerminator())
        {
            var (kind, length) = (_lexer.Peek(1), _lexer.Peek(2)) switch
            {
                (';', '&') => (ShellSyntaxKind.SemicolonSemicolonAmpersandToken, 3),
                (';', _) => (ShellSyntaxKind.SemicolonSemicolonToken, 2),
                _ => (ShellSyntaxKind.SemicolonAmpersandToken, 2),
            };

            terminatorToken = ReadOperatorToken(kind, length);
        }

        return new PosixCaseClauseSyntax(openParenToken, patterns, separators, closeParenToken, body, terminatorToken);
    }

    // ---- functions, groups, subshells ----

    private PosixFunctionDefinitionSyntax ParseFunctionDefinitionWithKeyword()
    {
        var functionKeyword = ReadKeyword();
        var nameToken = ReadBareWordToken(ShellSyntaxKind.VariableNameToken);

        ShellSyntaxToken? openParenToken = null;
        ShellSyntaxToken? closeParenToken = null;
        AccumulateInlineTrivia();
        if (_lexer.Current == '(')
        {
            openParenToken = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
            AccumulateInlineTrivia();
            closeParenToken = _lexer.Current == ')'
                ? ReadOperatorToken(ShellSyntaxKind.CloseParenToken, length: 1)
                : MissingToken(ShellSyntaxKind.CloseParenToken, _lexer.Position);
        }

        return new PosixFunctionDefinitionSyntax(functionKeyword, nameToken, openParenToken, closeParenToken, ParseFunctionBody());
    }

    private bool TryParseFunctionDefinition([NotNullWhen(true)] out ShellStatementSyntax? functionDefinition)
    {
        functionDefinition = null;

        var scan = _lexer.Position;
        var text = _lexer.Text;
        while (scan < text.Length && !PosixLexer.IsWordBoundary(text[scan]) && text[scan] is not '\'' and not '"' and not '`' and not '$' and not '\\' and not '=')
        {
            scan++;
        }

        if (scan == _lexer.Position)
            return false;

        var afterName = scan;
        while (afterName < text.Length && text[afterName] is ' ' or '\t')
        {
            afterName++;
        }

        if (afterName >= text.Length || text[afterName] != '(')
            return false;

        var afterOpen = afterName + 1;
        while (afterOpen < text.Length && text[afterOpen] is ' ' or '\t')
        {
            afterOpen++;
        }

        if (afterOpen >= text.Length || text[afterOpen] != ')')
            return false;

        var nameToken = ReadBareWordToken(ShellSyntaxKind.VariableNameToken);
        AccumulateInlineTrivia();
        var openParenToken = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
        AccumulateInlineTrivia();
        var closeParenToken = ReadOperatorToken(ShellSyntaxKind.CloseParenToken, length: 1);

        functionDefinition = new PosixFunctionDefinitionSyntax(functionKeyword: null, nameToken, openParenToken, closeParenToken, ParseFunctionBody());

        return true;
    }

    /// <summary>Reads the body of a function definition, which is normally a brace group or a subshell.</summary>
    private ShellStatementSyntax ParseFunctionBody()
    {
        AccumulateStatementTrivia();

        return ParseCommandOrCompound();
    }

    private PosixCompoundStatementSyntax ParseBraceGroup()
    {
        var openToken = ReadKeyword(ShellSyntaxKind.OpenBraceToken);
        var statements = ParseStatementList(ParseContext.UntilWords(CloseBraceWord));

        AccumulateStatementTrivia();
        ShellSyntaxToken closeToken;
        if (PeekBareWord() == "}")
        {
            closeToken = ReadKeyword(ShellSyntaxKind.CloseBraceToken);
        }
        else
        {
            AddDiagnostic(openToken.Span, "SHELL0009", "Expected '}' to close the group.");
            closeToken = MissingToken(ShellSyntaxKind.CloseBraceToken, _lexer.Position);
        }

        return new PosixCompoundStatementSyntax(ShellSyntaxKind.PosixGroup, openToken, statements, closeToken);
    }

    private PosixCompoundStatementSyntax ParseSubshell()
    {
        var openToken = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
        var statements = ParseStatementList(ParseContext.UntilCharacter(')'));

        AccumulateStatementTrivia();
        ShellSyntaxToken closeToken;
        if (_lexer.Current == ')')
        {
            closeToken = ReadOperatorToken(ShellSyntaxKind.CloseParenToken, length: 1);
        }
        else
        {
            AddDiagnostic(openToken.Span, "SHELL0009", "Expected ')' to close the subshell.");
            closeToken = MissingToken(ShellSyntaxKind.CloseParenToken, _lexer.Position);
        }

        return new PosixCompoundStatementSyntax(ShellSyntaxKind.PosixSubshell, openToken, statements, closeToken);
    }

    private PosixPrefixedStatementSyntax ParsePrefixedStatement(ShellSyntaxKind kind, bool hasName)
    {
        var keyword = ReadKeyword();

        ShellSyntaxToken? nameToken = null;
        if (hasName)
        {
            AccumulateInlineTrivia();
            var word = PeekBareWord();
            if (word is not null && word != "{" && !IsReservedWord(word) && LooksLikeCoprocName(word))
            {
                nameToken = ReadBareWordToken(ShellSyntaxKind.VariableNameToken);
            }
        }

        // `time` and `coproc` are complete statements on their own.
        AccumulateInlineTrivia();
        var hasCommand = !_lexer.IsAtEnd
            && SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) == 0
            && _lexer.Current is not ';' and not '&' and not '|' and not ')';

        var statement = hasCommand ? ParseCommandOrCompound() : new ShellEmptyStatementSyntax(_lexer.Position);

        return new PosixPrefixedStatementSyntax(kind, keyword, nameToken, statement);
    }

    /// <summary>A coprocess name is followed by a compound command; otherwise the word is the command itself.</summary>
    private bool LooksLikeCoprocName(string word)
    {
        var scan = _lexer.Position + word.Length;
        var text = _lexer.Text;
        while (scan < text.Length && text[scan] is ' ' or '\t')
        {
            scan++;
        }

        return scan < text.Length && text[scan] is '{' or '(';
    }

    // ---- delimited expressions ----

    private PosixDelimitedExpressionStatementSyntax ParseArithmeticCommand()
    {
        var openToken = ReadOperatorToken(ShellSyntaxKind.OpenParenParenToken, length: 2);
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

        var expression = new ShellRawExpressionSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, expressionStart, [], expressionStart));

        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0007", "Unterminated arithmetic command.");
            closeToken = MissingToken(ShellSyntaxKind.CloseParenParenToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position += 2;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.CloseParenParenToken, closeStart, [], closeStart);
        }

        return new PosixDelimitedExpressionStatementSyntax(ShellSyntaxKind.PosixArithmeticCommand, openToken, expression, closeToken);
    }

    private PosixDelimitedExpressionStatementSyntax ParseConditionalExpression()
    {
        var openToken = ReadOperatorToken(ShellSyntaxKind.OpenBracketBracketToken, length: 2);
        var expressionStart = _lexer.Position;
        while (!_lexer.IsAtEnd && !(_lexer.Current == ']' && _lexer.Peek(1) == ']'))
        {
            SkipQuotedSectionOrCharacter();
        }

        var expression = new ShellRawExpressionSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, expressionStart, [], expressionStart));

        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0010", "Unterminated conditional expression.");
            closeToken = MissingToken(ShellSyntaxKind.CloseBracketBracketToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position += 2;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.CloseBracketBracketToken, closeStart, [], closeStart);
        }

        return new PosixDelimitedExpressionStatementSyntax(ShellSyntaxKind.PosixConditionalExpression, openToken, expression, closeToken);
    }

    /// <summary>Advances one character, or past a whole quoted section so a <c>]]</c> inside quotes does not end the expression.</summary>
    private void SkipQuotedSectionOrCharacter()
    {
        var quote = _lexer.Current;
        if (quote is not '\'' and not '"')
        {
            _lexer.Position++;
            return;
        }

        _lexer.Position++;
        while (!_lexer.IsAtEnd && _lexer.Current != quote)
        {
            // A backslash escapes the next character inside double quotes, including a closing quote.
            if (quote == '"' && _lexer.Current == '\\' && _lexer.Position + 1 < _lexer.Text.Length)
            {
                _lexer.Position++;
            }

            _lexer.Position++;
        }

        if (!_lexer.IsAtEnd)
        {
            _lexer.Position++;
        }
    }

    // ---- arrays and process substitution ----

    private PosixArrayAssignmentSyntax ParseArrayAssignment(ShellSyntaxToken nameToken, ShellSyntaxToken equalsToken)
    {
        var openParenToken = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
        var elements = new List<ShellWordSyntax>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
                break;

            if (PosixLexer.IsWordBoundary(_lexer.Current) && !IsAtProcessSubstitution())
                break;

            var positionBefore = _lexer.Position;
            elements.Add(ParseWord());
            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }
        }

        AccumulateStatementTrivia();
        ShellSyntaxToken closeParenToken;
        if (_lexer.Current == ')')
        {
            closeParenToken = ReadOperatorToken(ShellSyntaxKind.CloseParenToken, length: 1);
        }
        else
        {
            AddDiagnostic(openParenToken.Span, "SHELL0009", "Expected ')' to close the array assignment.");
            closeParenToken = MissingToken(ShellSyntaxKind.CloseParenToken, _lexer.Position);
        }

        return new PosixArrayAssignmentSyntax(nameToken, equalsToken, openParenToken, elements, closeParenToken);
    }

    private bool IsAtProcessSubstitution() => IsProcessSubstitutionAt(_lexer.Position);

    private bool IsProcessSubstitutionAt(int position)
    {
        if (!_options.Dialect.HasFeature(ShellDialectFeatures.ProcessSubstitution))
            return false;

        var text = _lexer.Text;

        return position + 1 < text.Length && text[position] is '<' or '>' && text[position + 1] == '(';
    }

    private PosixProcessSubstitutionSyntax ParseProcessSubstitution(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var kind = _lexer.Current == '<' ? ShellSyntaxKind.LessThanOpenParenToken : ShellSyntaxKind.GreaterThanOpenParenToken;
        var start = _lexer.Position;
        _lexer.Position += 2;
        var openToken = _lexer.CreateToken(kind, start, leadingTrivia, fullStart);

        var statements = ParseStatementList(ParseContext.UntilCharacter(')'));

        var (trivia, closeFullStart) = TakeTrivia();
        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0009", "Unterminated process substitution.");
            closeToken = MissingToken(ShellSyntaxKind.CloseParenToken, closeFullStart, trivia);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.CloseParenToken, closeStart, trivia, closeFullStart);
        }

        return new PosixProcessSubstitutionSyntax(openToken, statements, closeToken);
    }

    // ---- here-documents ----

    /// <summary>
    /// Reads the bodies announced by any <c>&lt;&lt;</c> redirections on the line just parsed. The bodies start after
    /// the next line break, so they are appended to the command rather than nested inside the redirection.
    /// </summary>
    private void DrainHereDocuments(List<ShellStatementSyntax> statements)
    {
        if (_pendingHereDocuments.Count == 0)
            return;

        var pending = _pendingHereDocuments.ToArray();
        _pendingHereDocuments.Clear();

        foreach (var hereDocument in pending)
        {
            var (trivia, fullStart) = TakeTrivia();
            var bodyStart = _lexer.Position;
            var stripsTabs = hereDocument.Redirection.OperatorToken.Kind == ShellSyntaxKind.LessThanLessThanDashToken;

            var lineBreakLength = SourceText.GetLineBreakLength(_lexer.Text, Math.Min(_lexer.Position, Math.Max(0, _lexer.Text.Length - 1)));
            if (!_lexer.IsAtEnd && lineBreakLength > 0)
            {
                _lexer.Position += lineBreakLength;
            }

            var delimiterStart = -1;
            while (!_lexer.IsAtEnd)
            {
                var lineStart = _lexer.Position;
                while (!_lexer.IsAtEnd && SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) == 0)
                {
                    _lexer.Position++;
                }

                var lineText = _lexer.Text[lineStart.._lexer.Position];
                if (!_lexer.IsAtEnd)
                {
                    _lexer.Position += SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position);
                }

                var candidate = stripsTabs ? lineText.TrimStart('\t') : lineText;
                if (string.Equals(candidate, hereDocument.Delimiter, StringComparison.Ordinal))
                {
                    delimiterStart = lineStart;
                    break;
                }
            }

            ShellSyntaxToken delimiterToken;
            if (delimiterStart < 0)
            {
                AddDiagnostic(hereDocument.Redirection.OperatorToken.Span, "SHELL0011", $"The here-document is not closed by '{hereDocument.Delimiter}'.");
                delimiterStart = _lexer.Position;
                delimiterToken = MissingToken(ShellSyntaxKind.BareTextToken, _lexer.Position);
            }
            else
            {
                var delimiterText = _lexer.Text[delimiterStart.._lexer.Position];
                delimiterToken = new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, delimiterText, delimiterText, fullStart: delimiterStart);
            }

            var bodyText = _lexer.Text[bodyStart..delimiterStart];
            var bodyToken = new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, bodyText, bodyText, leadingTrivia: trivia, fullStart: fullStart);

            var node = new PosixHereDocumentSyntax(bodyToken, delimiterToken, hereDocument.Redirection);
            hereDocument.Redirection.HereDocument = node;
            statements.Add(node);
        }
    }

    // ---- reserved words ----

    private static readonly string[] ReservedWords =
    [
        "if", "then", "elif", "else", "fi",
        "for", "select", "while", "until", "do", "done",
        "case", "esac", "in",
        "function", "time", "coproc",
        "{", "}", "!",
    ];

    private static bool IsReservedWord(string word) => Array.IndexOf(ReservedWords, word) >= 0;

    /// <summary>
    /// Returns the unquoted word starting at the current position without consuming it, or <see langword="null"/>
    /// when the word continues into quoting or an expansion. A reserved word is only reserved when it forms a whole
    /// word, so <c>for$(cmd)</c> is a command name rather than the <c>for</c> keyword.
    /// </summary>
    private string? PeekBareWord()
    {
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start;
        while (scan < text.Length && !PosixLexer.IsWordBoundary(text[scan]) && text[scan] is not '\'' and not '"' and not '`' and not '$' and not '\\')
        {
            scan++;
        }

        if (scan == start)
            return null;

        // The scan stopped on quoting or an expansion, so the word carries on and is not a keyword.
        if (scan < text.Length && !PosixLexer.IsWordBoundary(text[scan]))
            return null;

        return text[start..scan];
    }

    private string? PeekBareWordAfterTrivia()
    {
        AccumulateStatementTrivia();

        return PeekBareWord();
    }

    private ShellSyntaxToken ReadKeyword(ShellSyntaxKind kind = ShellSyntaxKind.KeywordToken)
    {
        AccumulateStatementTrivia();
        var word = PeekBareWord() ?? string.Empty;

        return ReadOperatorToken(kind, word.Length);
    }

    private ShellSyntaxToken ExpectKeyword(string keyword)
    {
        AccumulateStatementTrivia();
        if (string.Equals(PeekBareWord(), keyword, StringComparison.Ordinal))
            return ReadOperatorToken(ShellSyntaxKind.KeywordToken, keyword.Length);

        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", $"Expected '{keyword}'.");
        var (trivia, fullStart) = TakeTrivia();

        return MissingToken(ShellSyntaxKind.KeywordToken, fullStart, trivia);
    }

    private ShellSyntaxToken ReadBareWordToken(ShellSyntaxKind kind)
    {
        AccumulateStatementTrivia();
        var word = PeekBareWord();
        if (word is null)
        {
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0013", "Expected a name.");
            var (trivia, fullStart) = TakeTrivia();

            return MissingToken(kind, fullStart, trivia);
        }

        return ReadOperatorToken(kind, word.Length);
    }

    /// <summary>A zero-width keyword used where a synthesized node needs a token that has no source text.</summary>
    private ShellSyntaxToken HiddenKeyword() => MissingToken(ShellSyntaxKind.KeywordToken, _lexer.Position);

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
}
