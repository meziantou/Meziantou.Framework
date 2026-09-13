using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>Compound statements: control flow, function definitions, groups, and here-documents.</summary>
internal sealed partial class PosixParser
{
    private static readonly string[] IfConditionWords = ["then", "elif", "else", "fi"];
    private static readonly string[] IfBodyWords = ["elif", "else", "fi"];
    private static readonly string[] FiWord = ["fi"];
    private static readonly string[] DoWords = ["do", "done"];
    private static readonly string[] DoneWord = ["done"];
    private static readonly string[] CloseBraceWord = ["}"];
    private static readonly string[] EndWord = ["end"];

    private ShellStatementSyntax ParseCommandOrCompound()
    {
        AccumulateInlineTrivia();

        if (!TryEnterRecursion(new TextSpan(_lexer.Position, 0)))
            return ConsumeRestAsSkippedText();

        try
        {
            var isAnonymousFunction = IsZsh && IsAtAnonymousFunction();
            var statement = ParseCommandOrCompoundCore();
            _lastCommandEndsWithExpressionDelimiter = statement.Kind is SyntaxKind.PosixArithmeticCommand or SyntaxKind.PosixConditionalExpression;
            _lastCommandIsAnonymousFunction = isAnonymousFunction;

            return statement;
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
                return ParseRedirections(ParseIfStatement());
            case "while":
                return ParseRedirections(ParseWhileStatement(SyntaxKind.PosixWhileStatement));
            case "until":
                return ParseRedirections(ParseWhileStatement(SyntaxKind.PosixUntilStatement));
            case "for":
                return ParseRedirections(ParseForStatement(SyntaxKind.PosixForStatement));
            case "select" when dialect.HasFeature(ShellDialectFeatures.SelectLoop):
                return ParseRedirections(ParseForStatement(SyntaxKind.PosixSelectStatement));
            case "case":
                return ParseRedirections(ParseCaseStatement());
            case "function" when dialect.HasFeature(ShellDialectFeatures.FunctionKeyword):
                return ParseFunctionDefinitionWithKeyword();

            // POSIX sh has a `time` utility rather than a reserved word, so there it is an ordinary command.
            case "time" when !IsPosixSh:
                return ParsePrefixedStatement(SyntaxKind.PosixTimeStatement, hasName: false);
            case "coproc" when dialect.HasFeature(ShellDialectFeatures.Coproc):
                return ParsePrefixedStatement(SyntaxKind.PosixCoprocStatement, hasName: true);
            case "{":
                return ParseRedirections(ParseBraceGroup());
            case "foreach" when dialect.HasFeature(ShellDialectFeatures.ZshExtensions):
                return ParseRedirections(ParseZshForeachStatement());
            case "repeat" when dialect.HasFeature(ShellDialectFeatures.ZshExtensions):
                return ParseZshRepeatStatement();
            case "!":
                // A pipeline takes a single `!`, in front of its first command; ParsePipeline reads that one.
                return ParseMisplacedBang();
        }

        // zsh anonymous function: `() { ... }` or `() command`.
        if (dialect.HasFeature(ShellDialectFeatures.ZshExtensions) && IsAtAnonymousFunction())
            return ParseZshAnonymousFunction();

        if (_lexer.Current == '(' && _lexer.Peek(1) == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.ArithmeticCommand) && FindArithmeticEnd(_lexer.Position + 2) >= 0)
            return ParseRedirections(ParseArithmeticCommand());

        if (_lexer.Current == '(')
            return ParseRedirections(ParseSubshell());

        // `[[` is a reserved word, so it only counts when it forms a whole word: `[[$x` is a command name.
        if (_lexer.Current == '[' && _lexer.Peek(1) == '[' && dialect.HasFeature(ShellDialectFeatures.ExtendedTest) && IsDelimiterAfter(2))
            return ParseRedirections(ParseConditionalExpression());

        if (TryParseFunctionDefinition(out var functionDefinition))
            return functionDefinition;

        return ParseSimpleCommand();
    }

    /// <summary>Reports a <c>!</c> that does not start a pipeline, then parses what it negates so nothing is lost.</summary>
    private ShellPipelineSyntax ParseMisplacedBang()
    {
        var bangToken = ReadOperatorToken(SyntaxKind.ExclamationToken, length: 1);
        AddDiagnostic(bangToken.Span, "SHELL0002", "Unexpected '!'.");

        return new ShellPipelineSyntax(bangToken, ParserHelpers.Separated([ParseCommandOrCompound()], []));
    }

    /// <summary>
    /// Reads the redirections that follow a compound command. A simple command keeps its redirections among its own
    /// elements; a compound command has no slot for them, so they wrap it.
    /// </summary>
    private ShellStatementSyntax ParseRedirections(ShellStatementSyntax statement)
    {
        List<ShellRedirectionSyntax>? redirections = null;
        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.IsAtEnd || !TryParseRedirection(out var redirection))
                break;

            redirections ??= [];
            redirections.Add(redirection);
        }

        if (redirections is null)
            return statement;

        return new PosixRedirectedStatementSyntax(statement, ParserHelpers.List(redirections));
    }

    /// <summary>
    /// Parses the list of a compound command. The grammar requires at least one command there, although zsh accepts
    /// an empty one, as in <c>if true; then fi</c>.
    /// </summary>
    /// <param name="context">Where the list ends.</param>
    /// <param name="introducer">The keyword in front of the list; when it is missing, the error is already reported.</param>
    private ShellStatementListSyntax ParseCompoundList(ParseContext context, ScannedToken introducer)
    {
        var diagnosticCount = _diagnostics.Count;
        var list = ParseStatementList(context);
        if (!IsZsh && !introducer.IsMissing && !_lexer.IsAtEnd && diagnosticCount == _diagnostics.Count && !ContainsCommand(list))
        {
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0001", "Expected a command.");
        }

        return list;
    }

    /// <summary>Returns whether a statement list holds anything other than here-document bodies.</summary>
    private static bool ContainsCommand(ShellStatementListSyntax list)
    {
        var statements = list.GetSlot(0);
        if (statements is null)
            return false;

        if (!statements.IsList)
            return statements is not PosixHereDocumentSyntax;

        for (var index = 0; index < statements.SlotCount; index++)
        {
            if (statements.GetSlot(index) is ShellStatementSyntax and not PosixHereDocumentSyntax)
                return true;
        }

        return false;
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

        // The condition also stops at the keywords after `then`, so a missing `then` is reported once and the rest of
        // the statement still pairs up with its own keywords.
        var condition = ParseCompoundList(ParseContext.UntilWords(IfConditionWords), ifKeyword);
        var thenKeyword = ExpectKeyword("then");
        var body = ParseCompoundList(ParseContext.UntilWords(IfBodyWords), thenKeyword);

        var elifClauses = new List<PosixElifClauseSyntax>();
        while (PeekBareWordAfterTrivia() == "elif")
        {
            var elifKeyword = ReadKeyword();
            var elifCondition = ParseCompoundList(ParseContext.UntilWords(IfConditionWords), elifKeyword);
            var elifThenKeyword = ExpectKeyword("then");
            var elifBody = ParseCompoundList(ParseContext.UntilWords(IfBodyWords), elifThenKeyword);
            elifClauses.Add(new PosixElifClauseSyntax(elifKeyword, elifCondition, elifThenKeyword, elifBody));
        }

        PosixElseClauseSyntax? elseClause = null;
        if (PeekBareWordAfterTrivia() == "else")
        {
            var elseKeyword = ReadKeyword();
            elseClause = new PosixElseClauseSyntax(elseKeyword, ParseCompoundList(ParseContext.UntilWords(FiWord), elseKeyword));
        }

        return new PosixIfStatementSyntax(ifKeyword, condition, thenKeyword, body, ParserHelpers.List(elifClauses), elseClause, ExpectKeyword("fi"));
    }

    private PosixWhileStatementSyntax ParseWhileStatement(SyntaxKind kind)
    {
        var keyword = ReadKeyword();
        var condition = ParseCompoundList(ParseContext.UntilWords(DoWords), keyword);
        var doKeyword = ExpectKeyword("do");
        var body = ParseCompoundList(ParseContext.UntilWords(DoneWord), doKeyword);

        return new PosixWhileStatementSyntax(kind, keyword, condition, doKeyword, body, ExpectKeyword("done"));
    }

    /// <summary>
    /// Reads the body of a <c>for</c> or <c>select</c> loop: <c>do ... done</c>, or in bash and zsh a brace group,
    /// which leaves both keywords missing without that being an error.
    /// </summary>
    private (ScannedToken DoKeyword, ShellStatementListSyntax Body, ScannedToken DoneKeyword) ParseLoopBody()
    {
        if (!IsPosixSh && PeekBareWordAfterTrivia() == "{")
        {
            var doKeyword = MissingToken(SyntaxKind.KeywordToken, _lexer.Position);
            var group = ParseRedirections(ParseBraceGroup());

            return (doKeyword, new ShellStatementListSyntax(group), MissingToken(SyntaxKind.KeywordToken, _lexer.Position));
        }

        var expectedDo = ExpectKeyword("do");
        var body = ParseCompoundList(ParseContext.UntilWords(DoneWord), expectedDo);

        return (expectedDo, body, ExpectKeyword("done"));
    }

    private ShellStatementSyntax ParseForStatement(SyntaxKind kind)
    {
        var keyword = ReadKeyword();
        AccumulateInlineTrivia();

        // bash's C-style loop, `for (( i = 0; i < n; i++ ))`, keeps its header verbatim.
        if (_lexer.Current == '(' && _lexer.Peek(1) == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.ArithmeticCommand))
        {
            var header = ParseArithmeticCommand();

            // The loop stands in for a `while` that has no text of its own, so anchor the placeholder at the header;
            // reading the position later would put the node's span after the text it covers.
            var hiddenWhileKeyword = MissingToken(SyntaxKind.KeywordToken, Math.Max(0, _lexer.Position - header.FullWidth));

            // A `;` may separate the header from the body, as in `for ((;;)); do`.
            AccumulateInlineTrivia();
            ScannedToken headerSeparator = default;
            if (_lexer.Current == ';' && !IsAtCaseTerminator())
            {
                headerSeparator = ReadSeparatorToken();
            }

            var (cStyleDo, cStyleBody, cStyleDone) = ParseLoopBody();
            var headerList = new ShellStatementListSyntax(headerSeparator.IsPresent ? ParserHelpers.Separated([header], [headerSeparator]) : header);

            return new PosixPrefixedStatementSyntax(
                kind,
                keyword,
                nameToken: null,
                new PosixWhileStatementSyntax(SyntaxKind.PosixWhileStatement, hiddenWhileKeyword, headerList, cStyleDo, cStyleBody, cStyleDone));
        }

        // zsh writes the word list in parentheses: `for x (a b) command`.
        if (_options.Dialect.HasFeature(ShellDialectFeatures.ZshExtensions) && kind == SyntaxKind.PosixForStatement && IsParenthesizedWordList())
            return ParseZshForeachStatement(keyword);

        var variableToken = ReadBareWordToken(SyntaxKind.VariableNameToken);
        // zsh also loops over the positional parameters by number, as in `for 1 2 in ...`.
        if (variableToken.IsPresent && !variableToken.IsMissing && !IsName(variableToken.Text) && !(IsZsh && variableToken.Text.All(char.IsAsciiDigit)))
        {
            AddDiagnostic(variableToken.Span, "SHELL0013", "Expected a name.");
        }

        ScannedToken inKeyword = default;
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

                if (IsWordTerminator(_lexer.Current) && !IsAtProcessSubstitution())
                    break;

                if (PeekBareWord() == "do")
                    break;

                items.Add(ParseWord());
            }
        }

        ScannedToken listTerminatorToken = default;
        AccumulateInlineTrivia();
        if (_lexer.Current == ';' && !IsAtCaseTerminator())
        {
            listTerminatorToken = ReadOperatorToken(SyntaxKind.SemicolonToken, length: 1);
        }

        var (doKeyword, body, doneKeyword) = ParseLoopBody();

        return new PosixForStatementSyntax(kind, keyword, variableToken, inKeyword, ParserHelpers.List(items), listTerminatorToken, doKeyword, body, doneKeyword);
    }

    private static bool IsName(string text)
    {
        if (text.Length == 0 || !PosixLexer.IsNameStart(text[0]))
            return false;

        foreach (var character in text)
        {
            if (!PosixLexer.IsNameCharacter(character))
                return false;
        }

        return true;
    }

    private PosixCaseStatementSyntax ParseCaseStatement()
    {
        var caseKeyword = ReadKeyword();
        AccumulateInlineTrivia();
        var subject = _lexer.IsAtEnd || PosixLexer.IsWordBoundary(_lexer.Current)
            ? new ShellWordSyntax(null)
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

            if (clause.TerminatorToken() is null || _lexer.Position == positionBefore)
                break;
        }

        return new PosixCaseStatementSyntax(caseKeyword, subject, inKeyword, ParserHelpers.List(clauses), ExpectKeyword("esac"));
    }

    private bool IsAtZshCasePatternGroup()
    {
        if (!IsAtZshGlobGroup())
            return false;

        var end = FindGlobGroupEnd(_lexer.Position);

        return end < _lexer.Text.Length && _lexer.Text[end] is not (' ' or '\t' or '\r' or '\n' or '|');
    }

    private PosixCaseClauseSyntax ParseCaseClause()
    {
        AccumulateStatementTrivia();

        // In zsh a pattern can start with a group, as in `(net|open)bsd*)`; the optional `(` of the POSIX form is
        // followed by a blank or a pattern instead.
        ScannedToken openParenToken = default;
        if (_lexer.Current == '(' && !IsAtZshCasePatternGroup())
        {
            openParenToken = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        }

        var patterns = new List<ShellWordSyntax>();
        var separators = new List<ScannedToken>();
        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
                break;

            _patternDepth++;
            var isAtGroup = IsAtZshGlobGroup();
            _patternDepth--;
            if (IsWordTerminator(_lexer.Current) && !IsAtProcessSubstitution() && !isAtGroup)
                break;

            _patternDepth++;
            patterns.Add(ParseWord());
            _patternDepth--;
            AccumulateInlineTrivia();
            if (_lexer.Current != '|' || _lexer.Peek(1) == '|')
                break;

            separators.Add(ReadOperatorToken(SyntaxKind.PipeToken, length: 1));
        }

        ScannedToken closeParenToken;
        AccumulateInlineTrivia();
        if (_lexer.Current == ')')
        {
            closeParenToken = ReadOperatorToken(SyntaxKind.CloseParenToken, length: 1);
        }
        else
        {
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0008", "Expected ')' after the case pattern.");
            closeParenToken = MissingToken(SyntaxKind.CloseParenToken, _lexer.Position);
        }

        var body = ParseStatementList(ParseContext.CaseClauseBody);

        ScannedToken terminatorToken = default;
        AccumulateStatementTrivia();
        if (IsAtCaseTerminator())
        {
            var (kind, length) = GetCaseTerminator();
            terminatorToken = ReadOperatorToken(kind, length);
        }

        return new PosixCaseClauseSyntax(openParenToken, ParserHelpers.Separated(patterns, separators), closeParenToken, body, terminatorToken);
    }

    // ---- zsh extensions ----

    /// <summary>Returns whether a <c>name (</c> word list follows the loop keyword, which is the zsh short form.</summary>
    private bool IsParenthesizedWordList()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        while (scan < text.Length && PosixLexer.IsNameCharacter(text[scan]))
        {
            scan++;
        }

        if (scan == _lexer.Position)
            return false;

        while (scan < text.Length && text[scan] is ' ' or '\t')
        {
            scan++;
        }

        return scan < text.Length && text[scan] == '(';
    }

    /// <summary>Returns whether the text at the current position is an anonymous function header, <c>()</c>.</summary>
    /// <remarks>zsh reads <c>( )</c>, with a blank inside, as an empty subshell instead.</remarks>
    private bool IsAtAnonymousFunction() => _lexer.Current == '(' && _lexer.Peek(1) == ')';

    private PosixFunctionDefinitionSyntax ParseZshAnonymousFunction()
    {
        var openParenToken = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        AccumulateInlineTrivia();
        var closeParenToken = ReadOperatorToken(SyntaxKind.CloseParenToken, length: 1);
        var nameToken = MissingToken(SyntaxKind.VariableNameToken, openParenToken.FullSpan.Start);

        return new PosixFunctionDefinitionSyntax(functionKeyword: null, nameToken, openParenToken, closeParenToken, ParseFunctionBody());
    }

    /// <summary>Reads <c>foreach x (a b) ... end</c> and the short <c>for x (a b) command</c> form.</summary>
    private ZshForeachStatementSyntax ParseZshForeachStatement(ScannedToken keyword = default)
    {
        if (!keyword.IsPresent)
        {
            keyword = ReadKeyword();
        }

        var variableToken = ReadBareWordToken(SyntaxKind.VariableNameToken);
        var openParenToken = ExpectCharacter('(', SyntaxKind.OpenParenToken);

        var items = new List<ShellWordSyntax>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
                break;

            if (IsWordTerminator(_lexer.Current) && !IsAtProcessSubstitution())
                break;

            items.Add(ParseWord());
        }

        var closeParenToken = ExpectCharacter(')', SyntaxKind.CloseParenToken);

        // `foreach` closes with `end`; the short `for` form takes a single statement and no terminator.
        var isForeach = string.Equals(keyword.Text, "foreach", StringComparison.Ordinal);
        if (!isForeach)
        {
            var shortBody = new ShellStatementListSyntax(ParseCommandOrCompound());

            return new ZshForeachStatementSyntax(keyword, variableToken, openParenToken, ParserHelpers.List(items), closeParenToken, shortBody, endKeyword: null);
        }

        AccumulateStatementTrivia();
        if (PeekBareWord() == "{")
        {
            var braceBody = new ShellStatementListSyntax(ParseBraceGroup());

            return new ZshForeachStatementSyntax(keyword, variableToken, openParenToken, ParserHelpers.List(items), closeParenToken, braceBody, endKeyword: null);
        }

        var body = ParseStatementList(ParseContext.UntilWords(EndWord));

        return new ZshForeachStatementSyntax(keyword, variableToken, openParenToken, ParserHelpers.List(items), closeParenToken, body, ExpectKeyword("end"));
    }

    /// <summary>Reads <c>repeat N</c> followed by a <c>do ... done</c> block, a brace group, or a single command.</summary>
    private ZshRepeatStatementSyntax ParseZshRepeatStatement()
    {
        var repeatKeyword = ReadKeyword();
        AccumulateInlineTrivia();
        var count = _lexer.IsAtEnd || PosixLexer.IsWordBoundary(_lexer.Current)
            ? new ShellWordSyntax(null)
            : ParseWord();

        ScannedToken listTerminatorToken = default;
        AccumulateInlineTrivia();
        if (_lexer.Current == ';')
        {
            listTerminatorToken = ReadOperatorToken(SyntaxKind.SemicolonToken, length: 1);
        }

        if (PeekBareWordAfterTrivia() == "do")
        {
            var doKeyword = ReadKeyword();
            var doBody = ParseStatementList(ParseContext.UntilWords(DoneWord));

            return new ZshRepeatStatementSyntax(repeatKeyword, count, listTerminatorToken, doKeyword, doBody, ExpectKeyword("done"));
        }

        var body = new ShellStatementListSyntax(ParseCommandOrCompound());

        return new ZshRepeatStatementSyntax(repeatKeyword, count, listTerminatorToken, doKeyword: null, body, doneKeyword: null);
    }

    // ---- functions, groups, subshells ----

    private PosixFunctionDefinitionSyntax ParseFunctionDefinitionWithKeyword()
    {
        var functionKeyword = ReadKeyword();

        // `function { ... }` is an anonymous function in zsh; bash needs a name, and `{` cannot be one.
        ScannedToken nameToken;
        AccumulateInlineTrivia();
        if (IsZsh && (_lexer.Current == '(' || (!PosixLexer.IsWordBoundary(_lexer.Current) && PeekBareWord() is null)))
        {
            // zsh takes `function () { }` as an anonymous function, and a name built by an expansion, as in
            // `function $w-by-keymap { }`.
            nameToken = _lexer.Current == '(' ? MissingToken(SyntaxKind.VariableNameToken, _lexer.Position) : ReadRawWordToken(SyntaxKind.VariableNameToken);
        }
        else if (PeekBareWord() == "{")
        {
            nameToken = MissingToken(SyntaxKind.VariableNameToken, _lexer.Position);
            if (!IsZsh)
            {
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0013", "Expected a name.");
            }
        }
        else
        {
            nameToken = ReadBareWordToken(SyntaxKind.VariableNameToken);
        }

        ScannedToken openParenToken = default;
        ScannedToken closeParenToken = default;
        AccumulateInlineTrivia();
        if (_lexer.Current == '(')
        {
            openParenToken = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
            AccumulateInlineTrivia();
            if (_lexer.Current == ')')
            {
                closeParenToken = ReadOperatorToken(SyntaxKind.CloseParenToken, length: 1);
            }
            else
            {
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", "Expected ')'.");
                closeParenToken = MissingToken(SyntaxKind.CloseParenToken, _lexer.Position);
            }
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

        var nameToken = ReadBareWordToken(SyntaxKind.VariableNameToken);
        AccumulateInlineTrivia();
        var openParenToken = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        AccumulateInlineTrivia();
        var closeParenToken = ReadOperatorToken(SyntaxKind.CloseParenToken, length: 1);

        functionDefinition = new PosixFunctionDefinitionSyntax(functionKeyword: null, nameToken, openParenToken, closeParenToken, ParseFunctionBody());

        return true;
    }

    /// <summary>
    /// Reads the body of a function definition. POSIX and bash require a compound command there, normally a brace
    /// group or a subshell; zsh also takes a simple command, as in <c>f() echo hi</c>.
    /// </summary>
    private ShellStatementSyntax ParseFunctionBody()
    {
        AccumulateStatementTrivia();
        if (!IsZsh && !_lexer.IsAtEnd && !IsAtCompoundCommand())
        {
            AddDiagnostic(new TextSpan(_lexer.Position, GetCurrentTokenLength()), "SHELL0014", "Expected a compound command as the function body.");
        }

        return ParseCommandOrCompound();
    }

    private bool IsAtCompoundCommand() => PeekBareWord() switch
    {
        "if" or "while" or "until" or "for" or "case" or "{" => true,
        "select" => _options.Dialect.HasFeature(ShellDialectFeatures.SelectLoop),
        _ => _lexer.Current == '(' || (_lexer.Current == '[' && _lexer.Peek(1) == '[' && _options.Dialect.HasFeature(ShellDialectFeatures.ExtendedTest) && IsDelimiterAfter(2)),
    };

    private PosixCompoundStatementSyntax ParseBraceGroup()
    {
        var openToken = ReadKeyword(SyntaxKind.OpenBraceToken);
        _zshBraceDepth++;
        var statements = ParseCompoundList(ParseContext.UntilWords(CloseBraceWord), openToken);
        _zshBraceDepth--;

        AccumulateStatementTrivia();
        ScannedToken closeToken;
        if (PeekBareWord() == "}")
        {
            closeToken = ReadKeyword(SyntaxKind.CloseBraceToken);
        }
        else
        {
            AddDiagnostic(openToken.Span, "SHELL0009", "Expected '}' to close the group.");
            closeToken = MissingToken(SyntaxKind.CloseBraceToken, _lexer.Position);
        }

        return new PosixCompoundStatementSyntax(SyntaxKind.PosixGroup, openToken, statements, closeToken);
    }

    private PosixCompoundStatementSyntax ParseSubshell()
    {
        var openToken = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        var statements = ParseCompoundList(ParseContext.UntilCharacter(')'), openToken);

        AccumulateStatementTrivia();
        ScannedToken closeToken;
        if (_lexer.Current == ')')
        {
            closeToken = ReadOperatorToken(SyntaxKind.CloseParenToken, length: 1);
        }
        else
        {
            AddDiagnostic(openToken.Span, "SHELL0009", "Expected ')' to close the subshell.");
            closeToken = MissingToken(SyntaxKind.CloseParenToken, _lexer.Position);
        }

        return new PosixCompoundStatementSyntax(SyntaxKind.PosixSubshell, openToken, statements, closeToken);
    }

    private PosixPrefixedStatementSyntax ParsePrefixedStatement(SyntaxKind kind, bool hasName)
    {
        var keyword = ReadKeyword();

        ScannedToken nameToken = default;
        if (hasName)
        {
            AccumulateInlineTrivia();
            var word = PeekBareWord();
            if (word is not null && word != "{" && !IsReservedWord(word) && LooksLikeCoprocName(word))
            {
                nameToken = ReadBareWordToken(SyntaxKind.VariableNameToken);
            }
        }

        // `time` and `coproc` are complete statements on their own.
        AccumulateInlineTrivia();
        var hasCommand = !_lexer.IsAtEnd
            && SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) == 0
            && _lexer.Current is not ';' and not '&' and not '|' and not ')';

        // `time` measures a whole pipeline, and the `!` in front of one, while `coproc` takes a single command.
        var statement = !hasCommand ? new ShellEmptyStatementSyntax()
            : kind == SyntaxKind.PosixTimeStatement ? ParsePipeline()
            : ParseCommandOrCompound();

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
        var openToken = ReadOperatorToken(SyntaxKind.OpenParenParenToken, length: 2);
        var expressionStart = _lexer.Position;
        var expressionEnd = FindArithmeticEnd(expressionStart) is var end && end >= 0 ? end : _lexer.Text.Length;
        var expression = TryParseArithmeticExpression(expressionEnd)
            ?? new ShellRawExpressionSyntax(ReadRawExpressionToken(expressionStart, expressionEnd));

        var (closeTrivia, closeFullStart) = TakeTrivia();
        ScannedToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0007", "Unterminated arithmetic command.");
            closeToken = MissingToken(SyntaxKind.CloseParenParenToken, closeFullStart, closeTrivia);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position += 2;
            closeToken = _lexer.CreateToken(SyntaxKind.CloseParenParenToken, closeStart, closeTrivia, closeFullStart);
        }

        return new PosixDelimitedExpressionStatementSyntax(SyntaxKind.PosixArithmeticCommand, openToken, expression, closeToken);
    }

    private PosixDelimitedExpressionStatementSyntax ParseConditionalExpression()
    {
        var openToken = ReadOperatorToken(SyntaxKind.OpenBracketBracketToken, length: 2);
        var expressionStart = _lexer.Position;
        var expressionEnd = FindConditionalEnd(expressionStart);
        var expression = TryParseConditionalExpression(expressionEnd, out var failurePosition);
        if (expression is null)
        {
            if (expressionEnd < _lexer.Text.Length)
            {
                ReportInvalidConditionalExpression(expressionStart, expressionEnd, failurePosition);
            }

            expression = new ShellRawExpressionSyntax(ReadRawExpressionToken(expressionStart, expressionEnd));
        }

        var (closeTrivia, closeFullStart) = TakeTrivia();
        ScannedToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0010", "Unterminated conditional expression.");
            closeToken = MissingToken(SyntaxKind.CloseBracketBracketToken, closeFullStart, closeTrivia);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position += 2;
            closeToken = _lexer.CreateToken(SyntaxKind.CloseBracketBracketToken, closeStart, closeTrivia, closeFullStart);
        }

        return new PosixDelimitedExpressionStatementSyntax(SyntaxKind.PosixConditionalExpression, openToken, expression, closeToken);
    }

    /// <summary>
    /// Returns the position of the <c>]]</c> that closes a conditional expression whose text starts at
    /// <paramref name="position"/>, or the end of the text. <c>]]</c> is a word of its own there, so the one in
    /// <c>[[:alpha:]]</c>, or inside quotes or a substitution, does not close the expression.
    /// </summary>
    private int FindConditionalEnd(int position)
    {
        var text = _lexer.Text;
        var scan = position;
        while (scan < text.Length)
        {
            if (text[scan] == ']'
                && scan + 1 < text.Length
                && text[scan + 1] == ']'
                && text[scan - 1] is ' ' or '\t' or '\n' or '\r'
                && (scan + 2 >= text.Length || PosixLexer.IsWordBoundary(text[scan + 2])))
            {
                return scan;
            }

            scan = SkipQuotedOrSubstitution(scan) is var next && next > 0 ? next : scan + 1;
        }

        return text.Length;
    }

    private void ReportInvalidConditionalExpression(int start, int end, int failurePosition)
    {
        if (_lexer.Text.AsSpan(start, end - start).IsWhiteSpace())
        {
            AddDiagnostic(new TextSpan(end, 2), "SHELL0015", "Expected a conditional expression.");
            return;
        }

        var position = _lexer.Position;
        _lexer.Position = Math.Clamp(failurePosition, start, end);
        var length = _lexer.Position >= end ? 2 : Math.Min(GetCurrentTokenLength(), end - _lexer.Position);
        AddDiagnostic(new TextSpan(_lexer.Position, length), "SHELL0015", $"Unexpected '{_lexer.Text.Substring(_lexer.Position, length)}' in the conditional expression.");
        _lexer.Position = position;
    }

    // ---- arrays and process substitution ----

    private PosixArrayAssignmentSyntax ParseArrayAssignment(ScannedToken nameToken, ScannedToken equalsToken)
    {
        var openParenToken = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        var elements = new List<ShellWordSyntax>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
                break;

            if (IsWordTerminator(_lexer.Current) && !IsAtProcessSubstitution() && !IsAtZshGlobGroup())
                break;

            elements.Add(ParseWord());
        }

        AccumulateStatementTrivia();
        ScannedToken closeParenToken;
        if (_lexer.Current == ')')
        {
            closeParenToken = ReadOperatorToken(SyntaxKind.CloseParenToken, length: 1);
        }
        else
        {
            AddDiagnostic(openParenToken.Span, "SHELL0009", "Expected ')' to close the array assignment.");
            closeParenToken = MissingToken(SyntaxKind.CloseParenToken, _lexer.Position);
        }

        return new PosixArrayAssignmentSyntax(nameToken, equalsToken, openParenToken, ParserHelpers.List(elements), closeParenToken);
    }

    private bool IsAtProcessSubstitution() => IsProcessSubstitutionAt(_lexer.Position);

    private bool IsProcessSubstitutionAt(int position)
    {
        if (!_options.Dialect.HasFeature(ShellDialectFeatures.ProcessSubstitution))
            return false;

        var text = _lexer.Text;
        if (position + 1 >= text.Length || text[position + 1] != '(')
            return false;

        if (text[position] is '<' or '>')
            return true;

        // zsh also has `=(...)`, which passes a temporary file rather than a pipe.
        return text[position] == '=' && _options.Dialect.HasFeature(ShellDialectFeatures.ZshExtensions);
    }

    private PosixProcessSubstitutionSyntax ParseProcessSubstitution(GreenNode? leadingTrivia, int fullStart)
    {
        var kind = _lexer.Current switch
        {
            '<' => SyntaxKind.LessThanOpenParenToken,
            '=' => SyntaxKind.EqualsOpenParenToken,
            _ => SyntaxKind.GreaterThanOpenParenToken,
        };
        var start = _lexer.Position;
        _lexer.Position += 2;
        var openToken = _lexer.CreateToken(kind, start, leadingTrivia, fullStart);

        var outerHereDocuments = EnterHereDocumentScope();
        var statements = ParseStatementList(ParseContext.UntilCharacter(')'));

        var closeToken = ReadSubstitutionCloseToken(openToken, ')', SyntaxKind.CloseParenToken, "SHELL0009", "Unterminated process substitution.");
        ExitHereDocumentScope(outerHereDocuments, closeToken);

        return new PosixProcessSubstitutionSyntax(openToken, statements, closeToken);
    }

    // ---- here-documents ----

    /// <summary>
    /// Reads the bodies announced by the <c>&lt;&lt;</c> redirections since the last line break. The current position
    /// is the line break the bodies start after, so they follow the command line rather than nest inside it.
    /// </summary>
    private List<PosixHereDocumentSyntax> ReadPendingHereDocuments()
    {
        var pending = _pendingHereDocuments.ToArray();
        _pendingHereDocuments.Clear();

        var hereDocuments = new List<PosixHereDocumentSyntax>(pending.Length);
        foreach (var hereDocument in pending)
        {
            var (trivia, fullStart) = TakeTrivia();
            var bodyStart = _lexer.Position;
            var stripsTabs = hereDocument.Redirection.OperatorToken()?.RawKind == (int)SyntaxKind.LessThanLessThanDashToken;

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

            ScannedToken delimiterToken;
            if (delimiterStart < 0)
            {
                // The shells read the body up to the end of the input and only warn about it.
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0011", $"The here-document is not closed by '{hereDocument.Delimiter}'.", DiagnosticSeverity.Warning);
                delimiterStart = _lexer.Position;
                delimiterToken = MissingToken(SyntaxKind.BareTextToken, _lexer.Position);
            }
            else
            {
                var delimiterText = _lexer.Text[delimiterStart.._lexer.Position];
                delimiterToken = new ScannedToken(SyntaxKind.BareTextToken, delimiterText, delimiterText, fullStart: delimiterStart);
            }

            var bodyText = _lexer.Text[bodyStart..delimiterStart];
            var bodyToken = new ScannedToken(SyntaxKind.BareTextToken, bodyText, bodyText, leadingTrivia: trivia, fullStart: fullStart);

            hereDocuments.Add(new PosixHereDocumentSyntax(bodyToken, delimiterToken));
        }

        return hereDocuments;
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

        // The scan stopped on quoting or an expansion, so the word carries on and is not a keyword. Inside a
        // backquoted substitution the closing backtick ends the word instead, as in `{ sort; }`.
        if (scan < text.Length && !PosixLexer.IsWordBoundary(text[scan]) && !(text[scan] == '`' && _backtickDepth > 0))
            return null;

        return text[start..scan];
    }

    private string? PeekBareWordAfterTrivia()
    {
        AccumulateStatementTrivia();

        return PeekBareWord();
    }

    private ScannedToken ReadKeyword(SyntaxKind kind = SyntaxKind.KeywordToken)
    {
        AccumulateStatementTrivia();
        var word = PeekBareWord() ?? string.Empty;

        return ReadOperatorToken(kind, word.Length);
    }

    private ScannedToken ExpectKeyword(string keyword)
    {
        AccumulateStatementTrivia();
        if (string.Equals(PeekBareWord(), keyword, StringComparison.Ordinal))
            return ReadOperatorToken(SyntaxKind.KeywordToken, keyword.Length);

        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", $"Expected '{keyword}'.");
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

    /// <summary>Reads the text up to the next blank or operator as one token, quoting and expansions included.</summary>
    private ScannedToken ReadRawWordToken(SyntaxKind kind)
    {
        var end = _lexer.Position;
        while (end < _lexer.Text.Length && !PosixLexer.IsWordBoundary(_lexer.Text[end]))
        {
            end = SkipQuotedOrSubstitution(end) is var next && next > 0 ? next : end + 1;
        }

        return ReadOperatorToken(kind, end - _lexer.Position);
    }

    private ScannedToken ReadBareWordToken(SyntaxKind kind)
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

    private ShellSkippedTextSyntax ConsumeRestAsSkippedText()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _lexer.Position;
        _lexer.Position = _lexer.Text.Length;
        var text = _lexer.Text[start..];

        return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([new ScannedToken(SyntaxKind.BadToken, text, text, leadingTrivia: trivia, fullStart: fullStart)]));
    }
}
