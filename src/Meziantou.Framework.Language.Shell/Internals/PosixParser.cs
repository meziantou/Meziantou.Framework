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
        var statements = new List<ShellStatementSyntax>();
        var separators = new List<ScannedToken>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || IsAtStop(context))
                break;

            var positionBeforeItem = _lexer.Position;

            if (_lexer.Current is ';' or '&' && !IsAndOrOperator() && !IsAtCaseTerminator())
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
                else
                {
                    // A separator with nothing in front of it is not valid; keep it so the text still round-trips.
                    AddDiagnostic(separator.Span, "SHELL0002", $"Unexpected '{separator.Text}'.");
                    statements.Add(new ShellSkippedTextSyntax(ParserHelpers.SkippedTokens([separator])));
                    separators.Add(MissingToken(SyntaxKind.SemicolonToken, _lexer.Position));
                }

                continue;
            }

            // `SeparatorTokens[i]` follows `Statements[i]`, so a statement that a line break ended rather than a `;`
            // still needs a placeholder; without one the next `;` would be rebuilt against the wrong statement.
            while (separators.Count < statements.Count)
            {
                separators.Add(MissingToken(SyntaxKind.SemicolonToken, _lexer.Position));
            }

            var statement = ParseAndOrList();
            statements.Add(statement);

            AccumulateInlineTrivia();
            if (!_lexer.IsAtEnd && _lexer.Current is ';' or '&' && !IsAndOrOperator() && !IsAtCaseTerminator())
            {
                separators.Add(ReadSeparatorToken());
            }

            // Any `<<` on the line just parsed takes its body from the lines that follow it.
            DrainHereDocuments(statements);

            if (_lexer.Position == positionBeforeItem)
            {
                // Nothing was consumed: force progress so a malformed script cannot spin forever.
                statements.Add(ConsumeUnexpectedCharacter());
            }
        }

        return new ShellStatementListSyntax(ParserHelpers.Separated(statements, separators));
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

            // A line break is allowed between the operator and the next pipeline.
            AccumulateStatementTrivia();
            pipelines.Add(ParsePipeline());
        }

        if (pipelines is null)
            return first;

        return new ShellCommandListSyntax(ParserHelpers.Separated(pipelines, operators));
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

            var isPipeAmpersand = _lexer.Peek(1) == '&';
            commands ??= [first];
            operators ??= [];
            operators.Add(isPipeAmpersand
                ? ReadOperatorToken(SyntaxKind.PipeAmpersandToken, length: 2)
                : ReadOperatorToken(SyntaxKind.PipeToken, length: 1));

            AccumulateStatementTrivia();
            commands.Add(TryAttachZshAlways(ParseCommandOrCompound()));
        }

        if (commands is null && !bangToken.IsPresent)
            return first;

        return new ShellPipelineSyntax(bangToken, ParserHelpers.Separated(commands ?? [first], operators ?? []));
    }

    private ShellStatementSyntax ParseSimpleCommand()
    {
        var elements = new List<ShellSyntaxNode>();
        var sawWord = false;

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.IsAtEnd)
                break;

            var current = _lexer.Current;
            if (SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) > 0)
                break;

            if (current is ';' or '|' or '(' or ')')
                break;

            if (current == '&' && !IsAmpersandRedirection())
                break;

            if (TryParseRedirection(out var redirection))
            {
                elements.Add(redirection);
                continue;
            }

            if (!sawWord && TryParseAssignment(out var assignment))
            {
                elements.Add(assignment);
                continue;
            }

            if (IsWordTerminator(current) && !IsAtProcessSubstitution())
                break;

            elements.Add(ParseWord());
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

    private bool TryParseAssignment([NotNullWhen(true)] out ShellSyntaxNode? assignment)
    {
        assignment = null;
        if (!PosixLexer.IsNameStart(_lexer.Current))
            return false;

        var scan = _lexer.Position;
        while (scan < _lexer.Text.Length && PosixLexer.IsNameCharacter(_lexer.Text[scan]))
        {
            scan++;
        }

        if (scan == _lexer.Position)
            return false;

        // `name+=value` appends in bash and zsh; plain `sh` only has `name=value`.
        var isAppend = scan + 1 < _lexer.Text.Length
            && _lexer.Text[scan] == '+'
            && _lexer.Text[scan + 1] == '='
            && _options.Dialect.HasFeature(ShellDialectFeatures.Arrays);

        if (!isAppend && (scan >= _lexer.Text.Length || _lexer.Text[scan] != '='))
            return false;

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
        if (IsProcessSubstitutionAt(operatorStart))
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
        if (!_lexer.IsAtEnd && !PosixLexer.IsWordBoundary(_lexer.Current))
        {
            target = ParseWord();
        }
        else
        {
            AddDiagnostic(operatorToken.Span, "SHELL0004", $"Expected a target after '{operatorToken.Text}'.");
        }

        redirection = new ShellRedirectionSyntax(ioNumberToken, operatorToken, target);
        if (kind is SyntaxKind.LessThanLessThanToken or SyntaxKind.LessThanLessThanDashToken)
        {
            _pendingHereDocuments.Add(new PendingHereDocument(redirection, target?.WordValue() ?? string.Empty));
        }

        return true;
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
            ('>', '>', _) => (SyntaxKind.GreaterThanGreaterThanToken, 2),
            ('>', '&', _) => (SyntaxKind.GreaterThanAmpersandToken, 2),
            ('>', '|', _) => (SyntaxKind.GreaterThanPipeToken, 2),
            ('>', _, _) => (SyntaxKind.GreaterThanToken, 1),
            ('&', '>', '>') => (SyntaxKind.AmpersandGreaterThanGreaterThanToken, 3),
            ('&', '>', _) => (SyntaxKind.AmpersandGreaterThanToken, 2),
            _ => (SyntaxKind.None, 0),
        };
    }

    private bool IsAmpersandRedirection() => _lexer.Current == '&' && _lexer.Peek(1) == '>';

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
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;

        while (!_lexer.IsAtEnd && (!IsWordTerminator(_lexer.Current) || IsAtProcessSubstitution()))
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

    private ShellLiteralWordPartSyntax ParseLiteralRun(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        while (!_lexer.IsAtEnd && !IsWordTerminator(_lexer.Current) && !IsWordPartStart(_lexer.Current))
        {
            if (_regexParenDepth >= 0 && _lexer.Current is '(' or ')')
            {
                _regexParenDepth += _lexer.Current == '(' ? 1 : -1;
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
                '$' => ParseDollarPart(null, _lexer.Position),
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

    private ShellWordPartSyntax ParseDollarPart(GreenNode? leadingTrivia, int fullStart)
    {
        var next = _lexer.Peek(1);

        if (next == '\'' && _options.Dialect.HasFeature(ShellDialectFeatures.DollarQuoting))
            return ParseAnsiCQuotedString(leadingTrivia, fullStart);

        if (next == '"' && _options.Dialect.HasFeature(ShellDialectFeatures.DollarQuoting))
            return ParseDoubleQuotedString(leadingTrivia, fullStart, SyntaxKind.DollarDoubleQuoteToken);

        if (next == '(' && _lexer.Peek(2) == '(' && _options.Dialect.HasFeature(ShellDialectFeatures.ArithmeticExpansion))
            return ParseArithmeticExpansion(leadingTrivia, fullStart);

        if (next == '(')
            return ParseCommandSubstitution(leadingTrivia, fullStart);

        if (next == '{')
            return ParseBracedVariableReference(leadingTrivia, fullStart);

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

    private ShellVariableReferenceSyntax ParseBracedVariableReference(GreenNode? leadingTrivia, int fullStart)
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
            // A `}` inside quotes, as in `${x:-"}"}`, does not close the expansion.
            if (_lexer.Current is '\'' or '"')
            {
                SkipQuotedSectionOrCharacter();
                continue;
            }

            if (_lexer.Current == '{')
            {
                depth++;
            }
            else if (_lexer.Current == '}')
            {
                depth--;
            }

            _lexer.Position++;
        }

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

        var statements = ParseStatementList(ParseContext.UntilCharacter(')'));
        _depth--;

        var (trivia, closeFullStart) = TakeTrivia();
        ScannedToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0006", "Unterminated command substitution.");
            closeToken = MissingToken(SyntaxKind.CloseParenToken, closeFullStart, trivia);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(SyntaxKind.CloseParenToken, closeStart, trivia, closeFullStart);
        }

        return new ShellCommandSubstitutionSyntax(openToken, statements, closeToken);
    }

    private ShellWordPartSyntax ParseBackquoteSubstitution(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(SyntaxKind.BacktickToken, start, leadingTrivia, fullStart);

        if (!TryEnterRecursion(openToken.Span))
            return new ShellLiteralWordPartSyntax(ConsumeRestAsText(openToken));

        _backtickDepth++;
        var statements = ParseStatementList(ParseContext.UntilCharacter('`'));
        _backtickDepth--;
        _depth--;

        var (trivia, closeFullStart) = TakeTrivia();
        ScannedToken closeToken;
        if (_lexer.IsAtEnd)
        {
            AddDiagnostic(openToken.Span, "SHELL0006", "Unterminated command substitution.");
            closeToken = MissingToken(SyntaxKind.BacktickToken, closeFullStart, trivia);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(SyntaxKind.BacktickToken, closeStart, trivia, closeFullStart);
        }

        return new ShellCommandSubstitutionSyntax(openToken, statements, closeToken);
    }

    private ShellWordPartSyntax ParseArithmeticExpansion(GreenNode? leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        _lexer.Position += 3;
        var openToken = _lexer.CreateToken(SyntaxKind.DollarOpenParenToken, start, leadingTrivia, fullStart);

        // `$(( ))` nests like `$( )` does, so it needs the same depth guard.
        if (!TryEnterRecursion(openToken.Span))
            return new ShellLiteralWordPartSyntax(ConsumeRestAsText(openToken));

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

        var expressionEnd = _lexer.Position;
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
        if (_regexParenDepth >= 0 && (value is '(' or '|' || (value == ')' && _regexParenDepth > 0)))
            return false;

        if (PosixLexer.IsWordBoundary(value))
            return true;

        if (_backtickDepth > 0 && value == '`')
            return true;

        // Inside a zsh brace group a `}` closes the group even mid-word, so `{ echo a}` is complete.
        return value == '}' && _zshBraceDepth > 0 && _options.Dialect.HasFeature(ShellDialectFeatures.ZshExtensions);
    }

    private bool IsAtStop(ParseContext context)
    {
        if (context.StopCharacter != '\0' && _lexer.Current == context.StopCharacter)
            return true;

        if (context.StopAtCaseTerminator && IsAtCaseTerminator())
            return true;

        return context.StopWords is { Length: > 0 } stopWords && PeekBareWord() is { } word && Array.IndexOf(stopWords, word) >= 0;
    }

    /// <summary>Returns <see langword="true"/> at <c>;;</c>, <c>;&amp;</c>, or <c>;;&amp;</c>, which only end a case clause.</summary>
    private bool IsAtCaseTerminator() => _lexer.Current == ';' && _lexer.Peek(1) is ';' or '&';

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

    private void AddDiagnostic(TextSpan span, string id, string message)
    {
        _diagnostics.Add(new Diagnostic(id, message, DiagnosticSeverity.Error, new Location(span, _lexer.Source)));
    }
}
