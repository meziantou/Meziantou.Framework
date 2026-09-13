using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>Parser for the POSIX shell family (<c>sh</c>, <c>bash</c>, and <c>zsh</c>).</summary>
/// <remarks>
/// The parser never throws. Anything it cannot recognize is kept as <see cref="ShellSkippedTextSyntax"/> alongside a
/// diagnostic, so <c>ToFullString()</c> always reproduces the input exactly.
/// </remarks>
internal sealed partial class PosixParser
{
    private readonly PosixLexer _lexer;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly ShellParseOptions _options;
    private readonly List<GreenNode?> _pendingTrivia = [];
    private readonly List<PendingHereDocument> _pendingHereDocuments = [];
    private int _pendingTriviaStart;
    private int _depth;
    private int _backtickDepth;
    private int _zshBraceDepth;

    /// <summary>Unmatched <c>{</c> in the word being read, which a <c>}</c> closes rather than ending a zsh group.</summary>
    private int _wordBraceDepth;

    /// <summary>Set while reading a pattern context, where a zsh glob group may hold blanks, as in <c>*list(| *)</c>.</summary>
    private int _patternDepth;

    /// <summary>The statement lists being parsed, outermost first, so an inner list can end where an outer one does.</summary>
    private readonly List<ParseContext> _contexts = [];

    /// <summary>Whether the command parsed last was <c>(( ))</c> or <c>[[ ]]</c>, after which bash reads no reserved word.</summary>
    private bool _lastCommandEndsWithExpressionDelimiter;

    /// <summary>Whether the command parsed last was a zsh anonymous function, which the arguments it runs with can follow.</summary>
    private bool _lastCommandIsAnonymousFunction;

    /// <summary>Open-parenthesis count inside the pattern of a <c>=~</c>, or -1 when no pattern is being read.</summary>
    private int _regexParenDepth = -1;

    public PosixParser(SourceText source, ShellParseOptions options)
    {
        _options = options;
        _lexer = new PosixLexer(source, options.Dialect, _diagnostics);
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public ShellScriptSyntax ParseScript()
    {
        var statements = ParseStatementList(ParseContext.TopLevel);
        var (trivia, fullStart) = TakeTrivia();
        var endOfFileToken = new ScannedToken(SyntaxKind.EndOfFileToken, string.Empty, string.Empty, leadingTrivia: trivia, fullStart: fullStart);

        return new ShellScriptSyntax(statements, endOfFileToken);
    }

    // ---- statements ----

    private ShellStatementListSyntax ParseStatementList(ParseContext context)
    {
        _contexts.Add(context);
        try
        {
            return ParseStatementListCore(context);
        }
        finally
        {
            _contexts.RemoveAt(_contexts.Count - 1);
        }
    }

    private ShellStatementListSyntax ParseStatementListCore(ParseContext context)
    {
        var statements = new List<ShellStatementSyntax>();
        var separators = new List<ScannedToken>();

        // Set while the statement just read still needs a `;`, a `&`, or a line break before another one can start.
        var awaitingSeparator = false;

        while (true)
        {
            if (AccumulateTriviaAndHereDocuments(statements, separators, SyntaxKind.SemicolonToken))
            {
                awaitingSeparator = false;
            }

            if (_lexer.IsAtEnd)
                break;

            if (IsAtStop(context) || IsAtEnclosingStop())
            {
                if (awaitingSeparator)
                {
                    ReportReservedWordAfterDelimitedExpression();
                }

                break;
            }

            var positionBeforeItem = _lexer.Position;

            if (_lexer.Current is ';' or '&' && !IsAndOrOperator() && !IsAtCaseTerminator() && !IsAmpersandRedirection())
            {
                var separator = ReadSeparatorToken();

                // The separator belongs to the statement in front of it, so pad first to land at the right index.
                while (separators.Count + 1 < statements.Count)
                {
                    separators.Add(MissingToken(SyntaxKind.SemicolonToken, separator.FullSpan.Start));
                }

                if (separators.Count < statements.Count)
                {
                    separators.Add(separator);
                }
                else if (IsZsh && separator.Kind == SyntaxKind.SemicolonToken)
                {
                    // zsh accepts a `;` with nothing in front of it, as in `if ; then`: the statement is empty.
                    statements.Add(new ShellEmptyStatementSyntax());
                    separators.Add(separator);
                }
                else
                {
                    // A separator with nothing in front of it is not valid; keep it so the text still round-trips.
                    AddDiagnostic(separator.Span, "SHELL0002", $"Unexpected '{separator.Text}'.");
                    statements.Add(new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([separator])));
                    separators.Add(MissingToken(SyntaxKind.SemicolonToken, _lexer.Position));
                }

                awaitingSeparator = false;
                continue;
            }

            PadSeparators(statements, separators);

            if (TrySkipMisplacedToken(out var skipped))
            {
                statements.Add(skipped);
                awaitingSeparator = false;
                continue;
            }

            // zsh defines several functions at once with `a b () body`, where the names before `()` read as a command,
            // and passes arguments to an anonymous function with `() { ... } args`, where they read as one.
            if (awaitingSeparator && !(IsZsh && (IsAtAnonymousFunction() || _lastCommandIsAnonymousFunction)))
            {
                ReportUnexpectedCurrentToken();
            }

            statements.Add(ParseAndOrList());
            awaitingSeparator = true;

            AccumulateInlineTrivia();
            if (!_lexer.IsAtEnd && _lexer.Current is ';' or '&' && !IsAndOrOperator() && !IsAtCaseTerminator() && !IsAmpersandRedirection())
            {
                separators.Add(ReadSeparatorToken());
                awaitingSeparator = false;
            }

            if (_lexer.Position == positionBeforeItem)
            {
                // Nothing was consumed: force progress so a malformed script cannot spin forever.
                PadSeparators(statements, separators);
                statements.Add(ConsumeUnexpectedCharacter());
                awaitingSeparator = false;
            }
        }

        return new ShellStatementListSyntax(ParserHelpers.Separated(statements, separators));
    }

    /// <summary>
    /// <c>SeparatorTokens[i]</c> follows <c>Statements[i]</c>, so a statement that a line break ended rather than a
    /// <c>;</c> still needs a placeholder; without one the next <c>;</c> would be rebuilt against the wrong statement.
    /// </summary>
    private void PadSeparators<TNode>(List<TNode> elements, List<ScannedToken> separators, SyntaxKind kind = SyntaxKind.SemicolonToken)
    {
        while (separators.Count < elements.Count)
        {
            separators.Add(MissingToken(kind, _lexer.Position));
        }
    }

    /// <summary>
    /// Reads the trivia in front of the next element of a list. A line break read while here-documents are pending
    /// is where their bodies start, so the bodies are read there and added to the list as statements of their own.
    /// </summary>
    /// <returns>Whether a line break was crossed, which ends the element in front of it.</returns>
    private bool AccumulateTriviaAndHereDocuments<TNode>(List<TNode> elements, List<ScannedToken> separators, SyntaxKind separatorKind)
        where TNode : ShellStatementSyntax
    {
        var crossedLineBreak = false;
        if (_pendingHereDocuments.Count > 0)
        {
            AccumulateInlineTrivia();
            if (!_lexer.IsAtEnd && SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) == 0)
                return false;

            foreach (var hereDocument in ReadPendingHereDocuments())
            {
                PadSeparators(elements, separators, separatorKind);
                elements.Add((TNode)(ShellStatementSyntax)hereDocument);
            }

            crossedLineBreak = true;
        }

        var triviaCount = _pendingTrivia.Count;
        AccumulateStatementTrivia();
        for (var index = triviaCount; index < _pendingTrivia.Count; index++)
        {
            if (_pendingTrivia[index]?.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                return true;
        }

        return crossedLineBreak;
    }

    /// <summary>Reports the token at the current position as unexpected, without consuming it.</summary>
    private void ReportUnexpectedCurrentToken()
    {
        var length = GetCurrentTokenLength();
        AddDiagnostic(new TextSpan(_lexer.Position, length), "SHELL0002", $"Unexpected '{_lexer.Text.Substring(_lexer.Position, length)}'.");
    }

    /// <summary>The length of the operator or word at the current position, for a diagnostic that quotes it.</summary>
    private int GetCurrentTokenLength()
    {
        var text = _lexer.Text;
        var start = _lexer.Position;
        if (start >= text.Length)
            return 0;

        if (PosixLexer.IsWordBoundary(text[start]))
            return (_lexer.Current, _lexer.Peek(1)) is ('&', '&') or ('|', '|') or (';', ';') or ('>', '>') or ('<', '<') ? 2 : 1;

        var scan = start;
        while (scan < text.Length && !PosixLexer.IsWordBoundary(text[scan]))
        {
            scan++;
        }

        return scan - start;
    }

    /// <summary>
    /// Skips a token that can never start a statement: a reserved word that closes a construct nothing here opened, a
    /// <c>)</c> that closes nothing, or a case terminator outside a case clause.
    /// </summary>
    private bool TrySkipMisplacedToken([NotNullWhen(true)] out ShellStatementSyntax? skipped)
    {
        skipped = null;

        int length;
        if (PeekBareWord() is { } word && IsMisplacedReservedWord(word))
        {
            length = word.Length;
        }
        else if (_lexer.Current == ')')
        {
            length = 1;
        }
        else if (IsAtCaseTerminator())
        {
            length = GetCaseTerminator().Length;
        }
        else
        {
            return false;
        }

        var token = ReadOperatorToken(SyntaxKind.BadToken, length);
        AddDiagnostic(token.Span, "SHELL0002", $"Unexpected '{token.Text}'.");
        skipped = new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([token]));

        return true;
    }

    private bool IsMisplacedReservedWord(string word) => word switch
    {
        "then" or "elif" or "else" or "fi" or "do" or "done" or "esac" or "}" => true,

        // zsh only treats `in` as reserved inside `for` and `case`.
        "in" => !IsZsh,
        _ => false,
    };

    /// <summary>Returns whether a list that encloses the one being parsed ends here, so the inner list has to end too.</summary>
    private bool IsAtEnclosingStop()
    {
        for (var index = _contexts.Count - 2; index >= 0; index--)
        {
            if (IsAtStop(_contexts[index]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// bash only recognizes a reserved word right after a closing delimiter of its own grammar, such as <c>)</c>,
    /// <c>}</c>, or <c>fi</c>; <c>))</c> and <c>]]</c> close an expression instead, so <c>if (( 1 )) then</c> is an
    /// error there. zsh accepts it.
    /// </summary>
    private void ReportReservedWordAfterDelimitedExpression()
    {
        if (!IsZsh && _lastCommandEndsWithExpressionDelimiter && PeekBareWord() is not null)
        {
            ReportUnexpectedCurrentToken();
        }
    }

    private ShellStatementSyntax ParseAndOrList()
    {
        var first = ParsePipeline();
        List<ShellStatementSyntax>? pipelines = null;
        List<ScannedToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            var kind = _lexer.Current switch
            {
                '&' when _lexer.Peek(1) == '&' => SyntaxKind.AmpersandAmpersandToken,
                '|' when _lexer.Peek(1) == '|' => SyntaxKind.PipePipeToken,
                _ => SyntaxKind.None,
            };

            if (kind == SyntaxKind.None)
                break;

            pipelines ??= [first];
            operators ??= [];
            operators.Add(ReadOperatorToken(kind, length: 2));

            // A line break is allowed between the operator and the next pipeline, and a here-document announced
            // before the operator starts right after that line break.
            AccumulateTriviaAndHereDocuments(pipelines, operators, kind);
            PadSeparators(pipelines, operators, kind);
            pipelines.Add(ParsePipeline());
        }

        if (pipelines is null)
            return first;

        return new ShellCommandListSyntax(ParserHelpers.Separated(pipelines, operators, SyntaxKind.AmpersandAmpersandToken));
    }

    /// <summary>
    /// Wraps a brace group that a zsh <c>always</c> block follows. The construct only exists in zsh, and only after a
    /// group, so anywhere else the word <c>always</c> stays an ordinary command name.
    /// </summary>
    private ShellStatementSyntax TryAttachZshAlways(ShellStatementSyntax statement)
    {
        if (statement.Kind != SyntaxKind.PosixGroup || !_options.Dialect.HasFeature(ShellDialectFeatures.ZshExtensions))
            return statement;

        AccumulateInlineTrivia();
        if (PeekBareWord() != "always")
            return statement;

        var alwaysKeyword = ReadKeyword();

        return new ZshAlwaysStatementSyntax(statement, alwaysKeyword, ParseCommandOrCompound());
    }

    private ShellStatementSyntax ParsePipeline()
    {
        AccumulateInlineTrivia();

        ScannedToken bangToken = default;
        if (_lexer.Current == '!' && (PosixLexer.IsWordBoundary(_lexer.Peek(1)) || _lexer.Peek(1) == '\0'))
        {
            bangToken = ReadOperatorToken(SyntaxKind.ExclamationToken, length: 1);
            AccumulateInlineTrivia();
        }

        var first = TryAttachZshAlways(ParseCommandOrCompound());
        List<ShellStatementSyntax>? commands = null;
        List<ScannedToken>? operators = null;

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current != '|' || _lexer.Peek(1) == '|')
                break;

            // `|&` is a bash and zsh shorthand for `2>&1 |`; in sh the `&` ends the pipeline instead.
            var isPipeAmpersand = _lexer.Peek(1) == '&' && !IsPosixSh;
            commands ??= [first];
            operators ??= [];
            operators.Add(isPipeAmpersand
                ? ReadOperatorToken(SyntaxKind.PipeAmpersandToken, length: 2)
                : ReadOperatorToken(SyntaxKind.PipeToken, length: 1));

            AccumulateTriviaAndHereDocuments(commands, operators, SyntaxKind.PipeToken);
            PadSeparators(commands, operators, SyntaxKind.PipeToken);
            commands.Add(TryAttachZshAlways(ParseCommandOrCompound()));
        }

        if (commands is null && !bangToken.IsPresent)
            return first;

        return new ShellPipelineSyntax(bangToken, ParserHelpers.Separated(commands ?? [first], operators ?? [], SyntaxKind.PipeToken));
    }

    private ShellStatementSyntax ParseSimpleCommand()
    {
        var elements = new List<ShellSyntaxNode>();
        var sawWord = false;
        var isDeclarationCommand = false;

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.IsAtEnd)
                break;

            var current = _lexer.Current;
            if (SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) > 0)
                break;

            if (current == '(' && sawWord && IsAtZshGlobGroup())
            {
                elements.Add(ParseWord());
                continue;
            }

            if (current is ';' or '|' or '(' or ')')
                break;

            if (current == '&' && !IsAmpersandRedirection())
                break;

            if (TryParseRedirection(out var redirection))
            {
                elements.Add(redirection);
                continue;
            }

            // An assignment prefixes the command. A declaration command such as `local` also takes array assignments
            // as arguments, which bash parses as assignments rather than as a word followed by a subshell.
            if ((!sawWord || (isDeclarationCommand && IsAtArrayAssignment())) && TryParseAssignment(out var assignment))
            {
                elements.Add(assignment);
                continue;
            }

            if (IsWordTerminator(current) && !IsAtProcessSubstitution())
                break;

            var word = ParseWord();
            if (!sawWord)
            {
                isDeclarationCommand = IsDeclarationCommandName(word.WordValue());
            }

            elements.Add(word);
            sawWord = true;
        }

        if (elements.Count == 0)
        {
            var (trivia, fullStart) = TakeTrivia();
            AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0001", "Expected a command.");

            return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([MissingToken(SyntaxKind.BareTextToken, fullStart, trivia)]));
        }

        return new ShellCommandSyntax(ParserHelpers.List(elements));
    }

    // ---- command parts ----

    private bool IsDeclarationCommandName(string? name) => _options.Dialect.HasFeature(ShellDialectFeatures.Arrays) && name switch
    {
        "declare" or "typeset" or "local" or "export" or "readonly" => true,
        "integer" or "float" => IsZsh,
        _ => false,
    };

    /// <summary>Returns whether an array assignment such as <c>name=(...)</c> starts at the current position.</summary>
    private bool IsAtArrayAssignment()
        => FindAssignmentOperator() is var scan && scan > 0 && _lexer.Peek(scan - _lexer.Position + (_lexer.Text[scan] == '+' ? 2 : 1)) == '(';

    /// <summary>
    /// Returns the position of the <c>=</c> or <c>+=</c> of an assignment starting at the current position, or -1 when
    /// no assignment starts there. In bash and zsh the name may carry a subscript, as in <c>a[i+1]=x</c>.
    /// </summary>
    private int FindAssignmentOperator()
    {
        var text = _lexer.Text;
        if (!PosixLexer.IsNameStart(_lexer.Current))
            return -1;

        var scan = _lexer.Position;
        while (scan < text.Length && PosixLexer.IsNameCharacter(text[scan]))
        {
            scan++;
        }

        var hasArrays = _options.Dialect.HasFeature(ShellDialectFeatures.Arrays);
        if (scan < text.Length && text[scan] == '[' && hasArrays)
        {
            scan = FindSubscriptEnd(scan);
            if (scan < 0)
                return -1;
        }

        // `name+=value` appends in bash and zsh; plain `sh` only has `name=value`.
        if (scan + 1 < text.Length && text[scan] == '+' && text[scan + 1] == '=' && hasArrays)
            return scan;

        return scan < text.Length && text[scan] == '=' ? scan : -1;
    }

    /// <summary>Returns the position just past the <c>]</c> matching the <c>[</c> at <paramref name="position"/>, or -1.</summary>
    private int FindSubscriptEnd(int position)
    {
        var text = _lexer.Text;
        var depth = 0;
        var scan = position;
        while (scan < text.Length)
        {
            var current = text[scan];
            if (current == '[')
            {
                depth++;
            }
            else if (current == ']')
            {
                depth--;
                if (depth == 0)
                    return scan + 1;
            }
            else if (PosixLexer.IsWordBoundary(current) && current is not '(' and not ')')
            {
                // zsh subscript flags such as `a[(Re)x]` use parentheses.
                return -1;
            }
            else if (SkipQuotedOrSubstitution(scan) is var next && next > 0)
            {
                scan = next;
                continue;
            }

            scan++;
        }

        return -1;
    }

    private bool TryParseAssignment([NotNullWhen(true)] out ShellSyntaxNode? assignment)
    {
        assignment = null;
        var scan = FindAssignmentOperator();
        if (scan < 0)
            return false;

        var isAppend = _lexer.Text[scan] == '+';
        var (trivia, fullStart) = TakeTrivia();
        var nameStart = _lexer.Position;
        _lexer.Position = scan;
        var nameToken = _lexer.CreateToken(SyntaxKind.VariableNameToken, nameStart, trivia, fullStart);

        var equalsStart = _lexer.Position;
        _lexer.Position += isAppend ? 2 : 1;
        var equalsToken = _lexer.CreateToken(
            isAppend ? SyntaxKind.PlusEqualsToken : SyntaxKind.EqualsToken,
            equalsStart,
            leadingTrivia: null,
            equalsStart);

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
        if (IsProcessSubstitutionAt(operatorStart) || FindZshNumericRangeEnd(operatorStart) > 0)
            return false;

        var (kind, length) = ReadRedirectionOperatorKind(operatorStart);
        if (kind == SyntaxKind.None)
            return false;

        var (trivia, fullStart) = TakeTrivia();

        ScannedToken ioNumberToken = default;
        if (hasIoNumber)
        {
            var ioStart = _lexer.Position;
            _lexer.Position = scan;
            ioNumberToken = _lexer.CreateToken(SyntaxKind.IoNumberToken, ioStart, trivia, fullStart);
            trivia = null;
            fullStart = _lexer.Position;
        }

        var tokenStart = _lexer.Position;
        _lexer.Position += length;
        var operatorToken = _lexer.CreateToken(kind, tokenStart, trivia, fullStart);

        AccumulateInlineTrivia();
        ShellWordSyntax? target = null;
        if (!_lexer.IsAtEnd && (!PosixLexer.IsWordBoundary(_lexer.Current) || IsAtProcessSubstitution()))
        {
            target = ParseWord();
        }
        else
        {
            AddDiagnostic(operatorToken.Span, "SHELL0004", $"Expected a target after '{operatorToken.Text}'.");
        }

        redirection = new ShellRedirectionSyntax(ioNumberToken, operatorToken, target);
        if (kind is SyntaxKind.LessThanLessThanToken or SyntaxKind.LessThanLessThanDashToken && target is not null)
        {
            _pendingHereDocuments.Add(new PendingHereDocument(redirection, GetHereDocumentDelimiter(target.ToString())));
        }

        return true;
    }

    /// <summary>
    /// Returns the line that ends a here-document introduced with <paramref name="word"/>. The delimiter is the word
    /// with its quotes removed and nothing expanded, so <c>&lt;&lt;"$EOF"</c> ends at a line reading <c>$EOF</c>.
    /// </summary>
    private static string GetHereDocumentDelimiter(string word)
    {
        var delimiter = new StringBuilder(word.Length);
        var quote = '\0';
        for (var index = 0; index < word.Length; index++)
        {
            var current = word[index];
            if (quote == '\'')
            {
                if (current == '\'')
                {
                    quote = '\0';
                }
                else
                {
                    delimiter.Append(current);
                }
            }
            else if (current == '\\' && index + 1 < word.Length && (quote == '\0' || word[index + 1] is '$' or '`' or '"' or '\\'))
            {
                delimiter.Append(word[++index]);
            }
            else if (current == '"')
            {
                quote = quote == '"' ? '\0' : '"';
            }
            else if (current == '\'' && quote == '\0')
            {
                quote = '\'';
            }
            else if (current == '$' && quote == '\0' && index + 1 < word.Length && word[index + 1] is '\'' or '"')
            {
                // `$'EOF'` and `$"EOF"` quote the delimiter the same way the plain forms do.
            }
            else
            {
                delimiter.Append(current);
            }
        }

        return delimiter.ToString();
    }

    private (SyntaxKind Kind, int Length) ReadRedirectionOperatorKind(int position)
    {
        var text = _lexer.Text;
        char At(int offset) => position + offset < text.Length ? text[position + offset] : '\0';

        return (At(0), At(1), At(2)) switch
        {
            ('<', '<', '<') when _options.Dialect.HasFeature(ShellDialectFeatures.HereString) => (SyntaxKind.LessThanLessThanLessThanToken, 3),
            ('<', '<', '-') => (SyntaxKind.LessThanLessThanDashToken, 3),
            ('<', '<', _) => (SyntaxKind.LessThanLessThanToken, 2),
            ('<', '&', _) => (SyntaxKind.LessThanAmpersandToken, 2),
            ('<', '>', _) => (SyntaxKind.LessThanGreaterThanToken, 2),
            ('<', _, _) => (SyntaxKind.LessThanToken, 1),
            ('>', '>', '!') when IsZsh => (SyntaxKind.GreaterThanGreaterThanToken, 3),
            ('>', '>', _) => (SyntaxKind.GreaterThanGreaterThanToken, 2),
            ('>', '&', _) => (SyntaxKind.GreaterThanAmpersandToken, 2),
            ('>', '|', _) => (SyntaxKind.GreaterThanPipeToken, 2),

            // zsh also spells the clobbering redirections with `!`.
            ('>', '!', _) when IsZsh => (SyntaxKind.GreaterThanPipeToken, 2),
            ('>', _, _) => (SyntaxKind.GreaterThanToken, 1),
            ('&', '>', '>') when !IsPosixSh => (SyntaxKind.AmpersandGreaterThanGreaterThanToken, 3),
            ('&', '>', _) when !IsPosixSh => (SyntaxKind.AmpersandGreaterThanToken, 2),
            _ => (SyntaxKind.None, 0),
        };
    }

    /// <summary>Returns whether <c>&amp;&gt;</c> starts here. In sh the <c>&amp;</c> is a separator of its own.</summary>
    private bool IsAmpersandRedirection() => _lexer.Current == '&' && _lexer.Peek(1) == '>' && !IsPosixSh;

    /// <summary>Whether the dialect is plain POSIX sh, which has none of the bash and zsh extensions.</summary>
    private bool IsPosixSh => !_options.Dialect.HasFeature(ShellDialectFeatures.ExtendedTest);

    private bool IsZsh => _options.Dialect.HasFeature(ShellDialectFeatures.ZshExtensions);

    private bool IsAndOrOperator() =>
        (_lexer.Current == '&' && _lexer.Peek(1) == '&') || (_lexer.Current == '|' && _lexer.Peek(1) == '|');

    private ScannedToken ReadSeparatorToken()
    {
        var kind = _lexer.Current == ';' ? SyntaxKind.SemicolonToken : SyntaxKind.AmpersandToken;

        return ReadOperatorToken(kind, length: 1);
    }

    private ScannedToken ReadOperatorToken(SyntaxKind kind, int length)
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
        var token = _lexer.CreateToken(SyntaxKind.BadToken, start, trivia, fullStart);
        AddDiagnostic(token.Span, "SHELL0002", $"Unexpected '{token.Text}'.");

        return new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([token]));
    }

    // ---- words ----

    private ShellWordSyntax ParseWord()
    {
        var enclosingWordBraceDepth = _wordBraceDepth;
        _wordBraceDepth = 0;
        try
        {
            return ParseWordCore();
        }
        finally
        {
            _wordBraceDepth = enclosingWordBraceDepth;
        }
    }

    private ShellWordSyntax ParseWordCore()
    {
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;

        while (!_lexer.IsAtEnd && (!IsWordTerminator(_lexer.Current) || IsAtProcessSubstitution() || IsAtZshGlobGroup()))
        {
            var (trivia, fullStart) = isFirst ? TakeTrivia() : (null, _lexer.Position);
            isFirst = false;

            var positionBefore = _lexer.Position;
            parts.Add(ParseWordPart(trivia, fullStart));
            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }
        }

        return new ShellWordSyntax(ParserHelpers.List(parts));
    }

    private ShellWordPartSyntax ParseWordPart(GreenNode? leadingTrivia, int fullStart)
    {
        if (IsAtProcessSubstitution())
            return ParseProcessSubstitution(leadingTrivia, fullStart);

        if (FindExtendedGlobEnd() is var extendedGlobEnd && extendedGlobEnd > 0)
            return ParseLiteralUpTo(extendedGlobEnd, leadingTrivia, fullStart);

        if (IsAtZshGlobGroup())
            return ParseLiteralUpTo(FindGlobGroupEnd(_lexer.Position), leadingTrivia, fullStart);

        if (FindZshNumericRangeEnd(_lexer.Position) is var rangeEnd && rangeEnd > 0)
            return ParseLiteralUpTo(rangeEnd, leadingTrivia, fullStart);

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

    private ShellLiteralWordPartSyntax ParseLiteralUpTo(int end, GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position = end;

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart));
    }

    /// <summary>
    /// Returns the position just past a bash extended glob such as <c>@(a|b)</c> or <c>!(*.txt)</c> starting here, or
    /// -1 when there is none. The pattern holds <c>(</c> and <c>|</c>, which would otherwise end the word.
    /// </summary>
    private int FindExtendedGlobEnd()
    {
        // zsh reads `*(...)` as a glob qualifier and the other forms as a character followed by a glob group.
        if (IsPosixSh || IsZsh || _lexer.Current is not ('?' or '*' or '+' or '@' or '!') || _lexer.Peek(1) != '(')
            return -1;

        return FindGlobGroupEnd(_lexer.Position + 1);
    }

    /// <summary>
    /// Returns whether a zsh glob group such as <c>(a|b)</c> starts here. zsh reads it as part of a word wherever a
    /// word can continue, which is also why <c>echo a (b)</c> is a single command there.
    /// </summary>
    private bool IsAtZshGlobGroup() => IsZsh && _lexer.Current == '(' && _regexParenDepth < 0 && FindGlobGroupEnd(_lexer.Position) > 0;

    /// <summary>
    /// Returns the position just past a zsh numeric range glob such as <c>&lt;-&gt;</c> or <c>&lt;1-10&gt;</c> starting at
    /// <paramref name="position"/>, or -1 when there is none. zsh reads it as a pattern rather than as two redirections.
    /// </summary>
    private int FindZshNumericRangeEnd(int position)
    {
        var text = _lexer.Text;
        if (!IsZsh || position >= text.Length || text[position] != '<')
            return -1;

        var scan = position + 1;
        while (scan < text.Length && char.IsAsciiDigit(text[scan]))
        {
            scan++;
        }

        if (scan >= text.Length || text[scan] != '-')
            return -1;

        scan++;
        while (scan < text.Length && char.IsAsciiDigit(text[scan]))
        {
            scan++;
        }

        return scan < text.Length && text[scan] == '>' ? scan + 1 : -1;
    }

    /// <summary>Returns the position just past the <c>)</c> matching the <c>(</c> at <paramref name="position"/>, or -1.</summary>
    /// <remarks>A pattern group ends on its line and holds no blanks, so neither ends up inside one.</remarks>
    private int FindGlobGroupEnd(int position)
    {
        var text = _lexer.Text;
        var depth = 0;
        var scan = position;
        while (scan < text.Length)
        {
            var current = text[scan];
            switch (current)
            {
                case '(':
                    depth++;
                    break;

                // `()` is not a pattern: after a word, zsh reads it as a function definition.
                case ')' when scan == position + 1:
                    return -1;

                case '<' when FindZshNumericRangeEnd(scan) is var rangeEnd && rangeEnd > 0:
                    scan = rangeEnd;
                    continue;

                case ' ' or '\t' when _patternDepth > 0:
                    break;

                case ')':
                    depth--;
                    if (depth == 0)
                        return scan + 1;

                    break;

                case ' ' or '\t' or '\r' or '\n' or ';' or '&' or '<' or '>':
                    return -1;

                default:
                    if (SkipQuotedOrSubstitution(scan) is var next && next > 0)
                    {
                        scan = next;
                        continue;
                    }

                    break;
            }

            scan++;
        }

        return -1;
    }

    /// <summary>
    /// Returns the position just past the quoting or substitution starting at <paramref name="position"/>, or -1 when
    /// none starts there. Scans that look for a closing delimiter use it so a delimiter inside quotes, or inside a
    /// nested substitution, does not end what they are scanning.
    /// </summary>
    private int SkipQuotedOrSubstitution(int position, int depth = 0)
    {
        var text = _lexer.Text;
        if (position >= text.Length)
            return -1;

        // Nesting this deep is reported by the parser proper; the scan only has to stay off the stack limit.
        if (depth >= _options.MaxRecursionDepth)
            return text.Length;

        switch (text[position])
        {
            case '\\':
                return Math.Min(position + 2, text.Length);

            case '\'':
                var singleQuoteEnd = text.IndexOf('\'', position + 1, StringComparison.Ordinal);
                return singleQuoteEnd < 0 ? text.Length : singleQuoteEnd + 1;

            case '"':
            {
                var scan = position + 1;
                while (scan < text.Length && text[scan] != '"')
                {
                    scan = text[scan] is '\\' or '$' or '`' && SkipQuotedOrSubstitution(scan, depth + 1) is var next && next > 0 ? next : scan + 1;
                }

                return Math.Min(scan + 1, text.Length);
            }

            case '`':
            {
                var scan = position + 1;
                while (scan < text.Length && text[scan] != '`')
                {
                    scan += text[scan] == '\\' ? 2 : 1;
                }

                return Math.Min(scan + 1, text.Length);
            }

            case '$' when position + 1 < text.Length && text[position + 1] is '(' or '{':
            {
                var (open, close) = text[position + 1] == '(' ? ('(', ')') : ('{', '}');
                var nesting = 0;
                var scan = position + 2;
                while (scan < text.Length)
                {
                    var current = text[scan];
                    if (current == close)
                    {
                        if (nesting == 0)
                            return scan + 1;

                        nesting--;
                    }
                    else if (current == open)
                    {
                        nesting++;
                    }
                    else if (SkipQuotedOrSubstitution(scan, depth + 1) is var next && next > 0)
                    {
                        scan = next;
                        continue;
                    }

                    scan++;
                }

                return text.Length;
            }

            default:
                return -1;
        }
    }

    private ShellLiteralWordPartSyntax ParseLiteralRun(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        while (!_lexer.IsAtEnd && !IsWordTerminator(_lexer.Current) && !IsWordPartStart(_lexer.Current) && FindExtendedGlobEnd() < 0 && FindZshNumericRangeEnd(_lexer.Position) < 0)
        {
            if (_regexParenDepth >= 0 && _lexer.Current is '(' or ')')
            {
                _regexParenDepth += _lexer.Current == '(' ? 1 : -1;
            }

            if (_lexer.Current == '{')
            {
                _wordBraceDepth++;
            }
            else if (_lexer.Current == '}' && _wordBraceDepth > 0)
            {
                _wordBraceDepth--;
            }

            _lexer.Position++;
        }

        if (_lexer.Position == start)
        {
            _lexer.Position++;
        }

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart));
    }

    private static bool IsWordPartStart(char value) => value is '\'' or '"' or '`' or '$' or '\\' or '*' or '?' or '[';

    private ShellGlobSyntax ParseGlob(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        SyntaxKind kind;
        if (_lexer.Current == '*' && _lexer.Peek(1) == '*')
        {
            // `**` matches across directory separators, so it is one glob rather than two.
            kind = SyntaxKind.AsteriskAsteriskToken;
            _lexer.Position += 2;
        }
        else
        {
            kind = _lexer.Current == '*' ? SyntaxKind.AsteriskToken : SyntaxKind.QuestionToken;
            _lexer.Position++;
        }

        // zsh allows a qualifier group directly after the pattern, as in `*(.)` or `foo*(N)`.
        if (_lexer.Current == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.ZshExtensions) && FindGlobQualifierEnd() is var end && end > 0)
        {
            _lexer.Position = end;
        }

        return new ShellGlobSyntax(_lexer.CreateToken(kind, start, leadingTrivia, fullStart));
    }

    /// <summary>Returns the position just past a balanced glob qualifier group, or -1 when it is not one.</summary>
    private int FindGlobQualifierEnd()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        var depth = 0;
        while (scan < text.Length)
        {
            var current = text[scan];
            if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
                if (depth == 0)
                    return scan + 1;
            }
            else if (current is '\r' or '\n' or ' ' or '\t')
            {
                return -1;
            }

            scan++;
        }

        return -1;
    }

    private ShellGlobSyntax ParseBracketExpression(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position = FindBracketExpressionEnd();

        return new ShellGlobSyntax(_lexer.CreateToken(SyntaxKind.BracketExpressionToken, start, leadingTrivia, fullStart));
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
    private ShellEscapeSequenceSyntax ParseEscapeSequence(GreenNode? leadingTrivia, int fullStart, bool inDoubleQuotes = false)
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

        return new ShellEscapeSequenceSyntax(_lexer.CreateToken(SyntaxKind.EscapeToken, start, leadingTrivia, fullStart, value));
    }

    private ShellQuotedStringSyntax ParseSingleQuotedString(GreenNode? leadingTrivia, int fullStart)
    {
        var quoteStart = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(SyntaxKind.SingleQuoteToken, quoteStart, leadingTrivia, fullStart);

        var contentStart = _lexer.Position;
        while (!_lexer.IsAtEnd && _lexer.Current != '\'')
        {
            _lexer.Position++;
        }

        var content = _lexer.Position > contentStart
            ? new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.BareTextToken, contentStart, null, contentStart))
            : null;

        ScannedToken closeToken;
        if (_lexer.IsAtEnd)
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

    /// <summary>
    /// Reads the bash <c>$'...'</c> form. Unlike a plain single-quoted string it resolves ANSI-C escapes, so
    /// <c>$'a\tb'</c> holds a real tab.
    /// </summary>
    private ShellQuotedStringSyntax ParseAnsiCQuotedString(GreenNode? leadingTrivia, int fullStart)
    {
        var quoteStart = _lexer.Position;
        _lexer.Position += 2;
        var openToken = _lexer.CreateToken(SyntaxKind.DollarSingleQuoteToken, quoteStart, leadingTrivia, fullStart);

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
            ? new ShellLiteralWordPartSyntax(new ScannedToken(
                SyntaxKind.BareTextToken,
                _lexer.Text[contentStart..contentEnd],
                value.ToString(),
                fullStart: contentStart))
            : null;

        ScannedToken closeToken;
        if (!terminated)
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated quoted string.");
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
        GreenNode? leadingTrivia,
        int fullStart,
        SyntaxKind openKind = SyntaxKind.DoubleQuoteToken)
    {
        var quoteStart = _lexer.Position;
        _lexer.Position += openKind == SyntaxKind.DollarDoubleQuoteToken ? 2 : 1;
        var openToken = _lexer.CreateToken(openKind, quoteStart, leadingTrivia, fullStart);

        var parts = new List<ShellWordPartSyntax>();
        while (!_lexer.IsAtEnd && _lexer.Current != '"')
        {
            var positionBefore = _lexer.Position;
            parts.Add(_lexer.Current switch
            {
                '\\' => ParseEscapeSequence(null, _lexer.Position, inDoubleQuotes: true),
                '`' => ParseBackquoteSubstitution(null, _lexer.Position),
                '$' => ParseDollarPart(null, _lexer.Position, inDoubleQuotes: true),
                _ => ParseDoubleQuotedLiteral(),
            });

            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }
        }

        ScannedToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated double-quoted string.");
            closeToken = MissingToken(SyntaxKind.DoubleQuoteToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(SyntaxKind.DoubleQuoteToken, closeStart, null, closeStart);
        }

        return new ShellQuotedStringSyntax(openToken, ParserHelpers.List(parts), closeToken);
    }

    private ShellLiteralWordPartSyntax ParseDoubleQuotedLiteral()
    {
        var start = _lexer.Position;
        while (!_lexer.IsAtEnd && _lexer.Current is not '"' and not '\\' and not '`' and not '$')
        {
            _lexer.Position++;
        }

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.BareTextToken, start, null, start));
    }

    private ShellWordPartSyntax ParseDollarPart(GreenNode? leadingTrivia, int fullStart, bool inDoubleQuotes = false)
    {
        var next = _lexer.Peek(1);

        // Inside double quotes `$'` and `$"` are not quoting: `"a$"` is the text `a$`.
        if (next == '\'' && !inDoubleQuotes && _options.Dialect.HasFeature(ShellDialectFeatures.DollarQuoting))
            return ParseAnsiCQuotedString(leadingTrivia, fullStart);

        if (next == '"' && !inDoubleQuotes && _options.Dialect.HasFeature(ShellDialectFeatures.DollarQuoting))
            return ParseDoubleQuotedString(leadingTrivia, fullStart, SyntaxKind.DollarDoubleQuoteToken);

        if (next == '(' && _lexer.Peek(2) == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.ArithmeticExpansion))
            return ParseArithmeticExpansion(leadingTrivia, fullStart);

        if (next == '(')
            return ParseCommandSubstitution(leadingTrivia, fullStart);

        if (next == '{')
            return ParseBracedVariableReference(leadingTrivia, fullStart, inDoubleQuotes);

        if (PosixLexer.IsNameStart(next) || PosixLexer.IsSpecialParameter(next))
            return ParseSimpleVariableReference(leadingTrivia, fullStart);

        // A bare '$' is literal text.
        var start = _lexer.Position;
        _lexer.Position++;

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.BareTextToken, start, leadingTrivia, fullStart));
    }

    private ShellVariableReferenceSyntax ParseSimpleVariableReference(GreenNode? leadingTrivia, int fullStart)
    {
        var dollarStart = _lexer.Position;
        _lexer.Position++;
        var dollarToken = _lexer.CreateToken(SyntaxKind.DollarToken, dollarStart, leadingTrivia, fullStart);

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

        var nameToken = _lexer.CreateToken(SyntaxKind.VariableNameToken, nameStart, null, nameStart);

        return new ShellVariableReferenceSyntax(dollarToken, openBraceToken: null, nameToken, closeBraceToken: null);
    }

    private ShellVariableReferenceSyntax ParseBracedVariableReference(GreenNode? leadingTrivia, int fullStart, bool inDoubleQuotes)
    {
        var dollarStart = _lexer.Position;
        _lexer.Position++;
        var dollarToken = _lexer.CreateToken(SyntaxKind.DollarToken, dollarStart, leadingTrivia, fullStart);

        var braceStart = _lexer.Position;
        _lexer.Position++;
        var openBraceToken = _lexer.CreateToken(SyntaxKind.OpenBraceToken, braceStart, null, braceStart);

        // The whole expansion body is kept as one token; `${var:-default}` round-trips without modeling operators.
        var nameStart = _lexer.Position;
        var depth = 0;
        while (!_lexer.IsAtEnd && (_lexer.Current != '}' || depth > 0))
        {
            // A `}` that is quoted, escaped, or inside a nested substitution, as in `${x:-"}"}`, `${x:-\}}`, or
            // `${x:-$(echo })}`, does not close the expansion. Inside double quotes only bash reads `'` as a quote.
            var isLiteralQuote = _lexer.Current == '\'' && inDoubleQuotes && (IsPosixSh || IsZsh);
            if (!isLiteralQuote && SkipQuotedOrSubstitution(_lexer.Position) is var next && next > 0)
            {
                _lexer.Position = next;
                continue;
            }

            // Only zsh pairs a bare `{` with a `}`, and only outside double quotes: `${x:-{a}b}` is `{ab}` elsewhere.
            if (_lexer.Current == '{' && IsZsh && !inDoubleQuotes)
            {
                depth++;
            }
            else if (_lexer.Current == '}')
            {
                depth--;
            }

            _lexer.Position++;
        }

        _lexer.Position = Math.Min(_lexer.Position, _lexer.Text.Length);

        var nameToken = _lexer.CreateToken(SyntaxKind.VariableNameToken, nameStart, null, nameStart);

        ScannedToken closeBraceToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openBraceToken.Span, "SHELL0005", "Unterminated parameter expansion.");
            closeBraceToken = MissingToken(SyntaxKind.CloseBraceToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeBraceToken = _lexer.CreateToken(SyntaxKind.CloseBraceToken, closeStart, null, closeStart);
        }

        return new ShellVariableReferenceSyntax(dollarToken, openBraceToken, nameToken, closeBraceToken);
    }

    private ShellWordPartSyntax ParseCommandSubstitution(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position += 2;
        var openToken = _lexer.CreateToken(SyntaxKind.DollarOpenParenToken, start, leadingTrivia, fullStart);

        if (!TryEnterRecursion(openToken.Span))
            return new ShellLiteralWordPartSyntax(ConsumeRestAsText(openToken));

        var outerHereDocuments = EnterHereDocumentScope();
        var statements = ParseStatementList(ParseContext.UntilCharacter(')'));
        _depth--;

        var closeToken = ReadSubstitutionCloseToken(openToken, ')', SyntaxKind.CloseParenToken, "SHELL0006", "Unterminated command substitution.");
        ExitHereDocumentScope(outerHereDocuments, closeToken);

        return new ShellCommandSubstitutionSyntax(openToken, statements, closeToken);
    }

    /// <summary>
    /// Reads the character that closes a substitution, or reports it missing. The list inside can also stop at a
    /// keyword that closes an enclosing construct, as in <c>{ echo $(date; }</c>, which must not be taken for it.
    /// </summary>
    private ScannedToken ReadSubstitutionCloseToken(ScannedToken openToken, char close, SyntaxKind kind, string id, string message)
    {
        var (trivia, closeFullStart) = TakeTrivia();
        if (_lexer.Current != close || _lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, id, message);

            return MissingToken(kind, closeFullStart, trivia);
        }

        var closeStart = _lexer.Position;
        _lexer.Position++;

        return _lexer.CreateToken(kind, closeStart, trivia, closeFullStart);
    }

    /// <summary>
    /// A substitution reads its own here-documents: a body announced inside it starts on a line inside it, and one
    /// announced before it is still waiting for the line break after it.
    /// </summary>
    private PendingHereDocument[] EnterHereDocumentScope()
    {
        var outer = _pendingHereDocuments.ToArray();
        _pendingHereDocuments.Clear();

        return outer;
    }

    private void ExitHereDocumentScope(PendingHereDocument[] outer, ScannedToken closeToken)
    {
        foreach (var unread in _pendingHereDocuments)
        {
            AddDiagnostic(new TextSpan(closeToken.Start, 0), "SHELL0011", $"The here-document is not closed by '{unread.Delimiter}'.", DiagnosticSeverity.Warning);
        }

        _pendingHereDocuments.Clear();
        _pendingHereDocuments.AddRange(outer);
    }

    private ShellWordPartSyntax ParseBackquoteSubstitution(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(SyntaxKind.BacktickToken, start, leadingTrivia, fullStart);

        if (!TryEnterRecursion(openToken.Span))
            return new ShellLiteralWordPartSyntax(ConsumeRestAsText(openToken));

        var outerHereDocuments = EnterHereDocumentScope();
        _backtickDepth++;
        var statements = ParseStatementList(ParseContext.UntilCharacter('`'));
        _backtickDepth--;
        _depth--;

        var closeToken = ReadSubstitutionCloseToken(openToken, '`', SyntaxKind.BacktickToken, "SHELL0006", "Unterminated command substitution.");
        ExitHereDocumentScope(outerHereDocuments, closeToken);

        return new ShellCommandSubstitutionSyntax(openToken, statements, closeToken);
    }

    /// <summary>
    /// Returns the position of the <c>))</c> that closes an arithmetic construct whose text starts at
    /// <paramref name="position"/>, or -1 when a <c>)</c> closes it alone. bash then reads the text as a command
    /// substitution or a subshell that happens to start with a subshell, as in <c>$((cd /tmp); ls)</c>.
    /// Running off the end returns the end of the text, so an unterminated construct is still arithmetic.
    /// </summary>
    private int FindArithmeticEnd(int position)
    {
        var text = _lexer.Text;
        var depth = 0;
        var scan = position;
        while (scan < text.Length)
        {
            var current = text[scan];
            if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                if (depth == 0)
                    return scan + 1 < text.Length && text[scan + 1] == ')' ? scan : -1;

                depth--;
            }
            else if (SkipQuotedOrSubstitution(scan) is var next && next > 0)
            {
                scan = next;
                continue;
            }

            scan++;
        }

        return text.Length;
    }

    private ShellWordPartSyntax ParseArithmeticExpansion(GreenNode? leadingTrivia, int fullStart)
    {
        var expressionEnd = FindArithmeticEnd(_lexer.Position + 3);
        if (expressionEnd < 0)
            return ParseCommandSubstitution(leadingTrivia, fullStart);

        var start = _lexer.Position;
        _lexer.Position += 3;
        var openToken = _lexer.CreateToken(SyntaxKind.DollarOpenParenToken, start, leadingTrivia, fullStart);

        // `$(( ))` nests like `$( )` does, so it needs the same depth guard.
        if (!TryEnterRecursion(openToken.Span))
            return new ShellLiteralWordPartSyntax(ConsumeRestAsText(openToken));

        var expressionStart = _lexer.Position;
        _lexer.Position = expressionStart;
        var expression = TryParseArithmeticExpression(expressionEnd)
            ?? new ShellRawExpressionSyntax(ReadRawExpressionToken(expressionStart, expressionEnd));

        // Whatever trivia the expression left pending sits between it and `))`, so the close token owns it.
        var (closeTrivia, closeFullStart) = TakeTrivia();
        ScannedToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0007", "Unterminated arithmetic expansion.");
            closeToken = MissingToken(SyntaxKind.CloseParenToken, closeFullStart, closeTrivia);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position += 2;
            closeToken = _lexer.CreateToken(SyntaxKind.CloseParenToken, closeStart, closeTrivia, closeFullStart);
        }

        _depth--;

        return new PosixArithmeticExpansionSyntax(openToken, expression, closeToken);
    }

    // ---- helpers ----

    /// <summary>Consumes the text between two offsets as one token, for expression text no grammar here fits.</summary>
    private ScannedToken ReadRawExpressionToken(int start, int end)
    {
        _lexer.Position = Math.Clamp(end, start, _lexer.Text.Length);

        return _lexer.CreateToken(SyntaxKind.BareTextToken, start, null, start);
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> ends a word here. Inside a backquoted substitution the closing
    /// backtick terminates the word rather than opening a nested substitution.
    /// </summary>
    private bool IsWordTerminator(char value)
    {
        // Inside a `=~` pattern the regular expression grammar wins: `(`, `|`, and the `)` that closes an open group
        // belong to the pattern. A `)` that closes nothing still ends it, so `[[ (a =~ b) ]]` keeps its group.
        if (_regexParenDepth >= 0 && (value is '(' or '|' || (value is ')' or ' ' or '\t' && _regexParenDepth > 0)))
            return false;

        if (PosixLexer.IsWordBoundary(value))
            return !(value == '<' && FindZshNumericRangeEnd(_lexer.Position) > 0);

        if (_backtickDepth > 0 && value == '`')
            return true;

        // Inside a zsh brace group a `}` closes the group even mid-word, so `{ echo a}` is complete, but only when a
        // delimiter follows it: `{ echo {a,b}x }` and `{ echo a}b }` keep the brace in the word.
        // A brace the word itself opened, as in `-{i,x}`, is closed by it instead.
        return value == '}' && _zshBraceDepth > 0 && IsZsh && _wordBraceDepth == 0 && (PosixLexer.IsWordBoundary(_lexer.Peek(1)) || (_backtickDepth > 0 && _lexer.Peek(1) == '`'));
    }

    private bool IsAtStop(ParseContext context)
    {
        if (context.StopCharacter != '\0' && _lexer.Current == context.StopCharacter)
            return true;

        if (context.StopAtCaseTerminator && IsAtCaseTerminator())
            return true;

        return context.StopWords is { Length: > 0 } stopWords && PeekBareWord() is { } word && Array.IndexOf(stopWords, word) >= 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> at <c>;;</c>, <c>;&amp;</c>, <c>;;&amp;</c>, or the zsh <c>;|</c>, which only
    /// end a case clause.
    /// </summary>
    private bool IsAtCaseTerminator() => _lexer.Current == ';' && (_lexer.Peek(1) is ';' or '&' || (_lexer.Peek(1) == '|' && IsZsh));

    /// <summary>Returns the case terminator at the current position; <c>;;&amp;</c> is bash only.</summary>
    private (SyntaxKind Kind, int Length) GetCaseTerminator() => (_lexer.Peek(1), _lexer.Peek(2)) switch
    {
        (';', '&') when _options.Dialect.HasFeature(ShellDialectFeatures.ExtendedTest) && !IsZsh => (SyntaxKind.SemicolonSemicolonAmpersandToken, 3),
        (';', _) => (SyntaxKind.SemicolonSemicolonToken, 2),
        ('|', _) => (SyntaxKind.SemicolonPipeToken, 2),
        _ => (SyntaxKind.SemicolonAmpersandToken, 2),
    };

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
    private ScannedToken ConsumeRestAsText(ScannedToken openToken)
    {
        var start = _lexer.Position;
        _lexer.Position = _lexer.Text.Length;
        var text = openToken.Text + _lexer.Text[start..];

        return new ScannedToken(SyntaxKind.BadToken, text, text, leadingTrivia: openToken.Green?.LeadingTrivia, fullStart: openToken.FullSpan.Start);
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

        AddTrivia(_lexer.ReadStatementTrivia());
    }

    /// <summary>Adds a green trivia node to the pending buffer as the pieces it was built from.</summary>
    private void AddTrivia(GreenNode? trivia)
    {
        if (trivia is null)
            return;

        if (!trivia.IsList)
        {
            _pendingTrivia.Add(trivia);
            return;
        }

        for (var index = 0; index < trivia.SlotCount; index++)
        {
            _pendingTrivia.Add(trivia.GetSlot(index));
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

    private static ScannedToken MissingToken(SyntaxKind kind, int position, GreenNode? leadingTrivia = null)
    {
        return new ScannedToken(kind, string.Empty, string.Empty, isMissing: true, leadingTrivia: leadingTrivia, fullStart: position);
    }

    private void AddDiagnostic(TextSpan span, string id, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        _diagnostics.Add(new Diagnostic(id, message, severity, new Location(span, _lexer.Source)));
    }
}
