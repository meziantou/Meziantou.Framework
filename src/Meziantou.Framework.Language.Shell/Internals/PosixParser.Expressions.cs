namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>
/// The two expression grammars the POSIX family has: arithmetic, used by <c>$(( ))</c> and <c>(( ))</c>, and the
/// conditional used by <c>[[ ]]</c>.
/// </summary>
/// <remarks>
/// Both entry points parse the text between the delimiters and check that the whole of it was consumed. Anything
/// left over means the grammar did not fit, and the caller falls back to keeping the text verbatim, so a construct
/// this file does not understand still round-trips.
/// </remarks>
internal sealed partial class PosixParser
{
    /// <summary>Binary arithmetic operators, longest first so that <c>&lt;&lt;=</c> wins over <c>&lt;&lt;</c> and <c>&lt;</c>.</summary>
    private static readonly (string Text, int Precedence, bool RightAssociative)[] ArithmeticOperators =
    [
        ("<<=", 1, true), (">>=", 1, true),
        ("+=", 1, true), ("-=", 1, true), ("*=", 1, true), ("/=", 1, true), ("%=", 1, true),
        ("&=", 1, true), ("^=", 1, true), ("|=", 1, true),
        ("||", 3, false), ("&&", 4, false),
        ("==", 8, false), ("!=", 8, false),
        ("<<", 10, false), (">>", 10, false),
        ("<=", 9, false), (">=", 9, false),
        ("=", 1, true),
        ("|", 5, false), ("^", 6, false), ("&", 7, false),
        ("<", 9, false), (">", 9, false),
        ("+", 11, false), ("-", 11, false),
        ("*", 12, false), ("/", 12, false), ("%", 12, false),
        (",", 0, false),
    ];

    private const int TernaryPrecedence = 2;

    /// <summary>
    /// Set when the grammar does not fit, for instance an operator with nothing to operate on. The partial tree is
    /// then thrown away and the caller keeps the text verbatim, so a half-built node never reaches the tree.
    /// </summary>
    private bool _expressionFailed;

    /// <summary>Unary tests in a conditional expression, such as <c>-f</c> in <c>[[ -f path ]]</c>.</summary>
    private static readonly string[] ConditionalUnaryOperators =
    [
        "-a", "-b", "-c", "-d", "-e", "-f", "-g", "-h", "-k", "-p", "-r", "-s", "-t", "-u", "-w", "-x",
        "-G", "-L", "-N", "-O", "-S", "-R", "-o", "-v", "-z", "-n",
    ];

    /// <summary>Comparisons in a conditional expression. Longest first so <c>==</c> wins over <c>=</c>.</summary>
    private static readonly string[] ConditionalBinaryOperators =
    [
        "==", "!=", "=~", "<=", ">=", "=", "<", ">",
        "-eq", "-ne", "-lt", "-le", "-gt", "-ge", "-nt", "-ot", "-ef",
    ];

    // ---- entry points ----

    /// <summary>Parses the text up to <paramref name="end"/> as an arithmetic expression, or returns null.</summary>
    private ShellExpressionSyntax? TryParseArithmeticExpression(int end)
    {
        return TryParseDelimited(end, () => ParseArithmetic(minimumPrecedence: 0));
    }

    /// <summary>Parses the text up to <paramref name="end"/> as a conditional expression, or returns null.</summary>
    private ShellExpressionSyntax? TryParseConditionalExpression(int end)
    {
        return TryParseDelimited(end, ParseConditionalOr);
    }

    /// <summary>
    /// Runs <paramref name="parse"/> and keeps the result only if it consumed everything up to <paramref name="end"/>.
    /// On anything else the lexer, the pending trivia, and the diagnostics are rolled back to where they were.
    /// </summary>
    private ShellExpressionSyntax? TryParseDelimited(int end, Func<ShellExpressionSyntax> parse)
    {
        var startPosition = _lexer.Position;
        var startTrivia = _pendingTrivia.ToArray();
        var startTriviaStart = _pendingTriviaStart;
        var startDiagnostics = _diagnostics.Count;

        if (startPosition >= end)
            return null;

        // A nested `$(( ))` runs this same method, so the outer attempt's flag has to survive the inner one.
        var enclosingFailed = _expressionFailed;
        _expressionFailed = false;
        var expression = parse();
        AccumulateInlineTrivia();

        var succeeded = !_expressionFailed && _lexer.Position == end && _diagnostics.Count == startDiagnostics;
        _expressionFailed = enclosingFailed;

        // Trailing trivia belongs to the closing delimiter, so only real text left over is a failure.
        if (succeeded)
            return expression;

        _lexer.Position = startPosition;
        _pendingTrivia.Clear();
        _pendingTrivia.AddRange(startTrivia);
        _pendingTriviaStart = startTriviaStart;
        _diagnostics.RemoveRange(startDiagnostics, _diagnostics.Count - startDiagnostics);

        return null;
    }

    /// <summary>
    /// Bounds the recursive descent of both expression grammars. Nesting past the limit marks the parse as failed,
    /// so the caller keeps the text verbatim instead of the stack running out.
    /// </summary>
    private bool TryEnterExpressionRecursion()
    {
        if (_depth >= _options.MaxRecursionDepth)
        {
            _expressionFailed = true;

            return false;
        }

        _depth++;

        return true;
    }

    /// <summary>A zero-width placeholder for an abandoned subtree; the whole parse is discarded anyway.</summary>
    private ShellRawExpressionSyntax AbandonedExpression() =>
        new ShellRawExpressionSyntax(new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, string.Empty, string.Empty, fullStart: _lexer.Position));

    private ShellExpressionSyntax ParseArithmetic(int minimumPrecedence)
    {
        if (!TryEnterExpressionRecursion())
            return AbandonedExpression();

        try
        {
            return ParseArithmeticCore(minimumPrecedence);
        }
        finally
        {
            _depth--;
        }
    }

    private ShellExpressionSyntax ParseArithmeticUnary()
    {
        if (!TryEnterExpressionRecursion())
            return AbandonedExpression();

        try
        {
            return ParseArithmeticUnaryCore();
        }
        finally
        {
            _depth--;
        }
    }

    private ShellExpressionSyntax ParseConditionalOr()
    {
        if (!TryEnterExpressionRecursion())
            return AbandonedExpression();

        try
        {
            return ParseConditionalOrCore();
        }
        finally
        {
            _depth--;
        }
    }

    private ShellExpressionSyntax ParseConditionalUnary()
    {
        if (!TryEnterExpressionRecursion())
            return AbandonedExpression();

        try
        {
            return ParseConditionalUnaryCore();
        }
        finally
        {
            _depth--;
        }
    }

    // ---- arithmetic ----

    private ShellExpressionSyntax ParseArithmeticCore(int minimumPrecedence)
    {
        var left = ParseArithmeticUnary();

        while (true)
        {
            AccumulateInlineTrivia();

            if (_lexer.Current == '?' && TernaryPrecedence >= minimumPrecedence)
            {
                var questionToken = ReadOperatorToken(ShellSyntaxKind.QuestionToken, length: 1);
                var whenTrue = ParseArithmetic(0);
                AccumulateInlineTrivia();
                if (_lexer.Current != ':')
                {
                    _expressionFailed = true;

                    return left;
                }

                var colonToken = ReadOperatorToken(ShellSyntaxKind.ColonToken, length: 1);
                left = new ShellConditionalExpressionSyntax(left, questionToken, whenTrue, colonToken, ParseArithmetic(TernaryPrecedence));
                continue;
            }

            var op = PeekArithmeticOperator();
            if (op is null || op.Value.Precedence < minimumPrecedence)
                break;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, op.Value.Text.Length);
            var next = op.Value.RightAssociative ? op.Value.Precedence : op.Value.Precedence + 1;
            left = new ShellBinaryExpressionSyntax(left, operatorToken, ParseArithmetic(next));
        }

        return left;
    }

    private (string Text, int Precedence, bool RightAssociative)? PeekArithmeticOperator()
    {
        foreach (var candidate in ArithmeticOperators)
        {
            if (!MatchesAt(_lexer.Position, candidate.Text))
                continue;

            // `=` is assignment, but `==` is equality; the table is ordered so the longer one is seen first.
            return candidate;
        }

        return null;
    }

    private ShellExpressionSyntax ParseArithmeticUnaryCore()
    {
        AccumulateInlineTrivia();

        foreach (var prefix in new[] { "++", "--" })
        {
            if (MatchesAt(_lexer.Position, prefix))
            {
                var token = ReadOperatorToken(ShellSyntaxKind.OperatorToken, prefix.Length);

                return new ShellUnaryExpressionSyntax(ShellSyntaxKind.PrefixUnaryExpression, token, ParseArithmeticUnary(), postfixOperatorToken: null);
            }
        }

        if (_lexer.Current is '!' or '~' or '+' or '-')
        {
            var token = ReadOperatorToken(ShellSyntaxKind.OperatorToken, length: 1);

            return new ShellUnaryExpressionSyntax(ShellSyntaxKind.PrefixUnaryExpression, token, ParseArithmeticUnary(), postfixOperatorToken: null);
        }

        return ParseArithmeticPostfix();
    }

    private ShellExpressionSyntax ParseArithmeticPostfix()
    {
        var operand = ParseArithmeticPrimary();

        AccumulateInlineTrivia();
        foreach (var postfix in new[] { "++", "--" })
        {
            if (MatchesAt(_lexer.Position, postfix))
            {
                var token = ReadOperatorToken(ShellSyntaxKind.OperatorToken, postfix.Length);

                return new ShellUnaryExpressionSyntax(ShellSyntaxKind.PostfixUnaryExpression, prefixOperatorToken: null, operand, token);
            }
        }

        return operand;
    }

    private ShellExpressionSyntax ParseArithmeticPrimary()
    {
        AccumulateInlineTrivia();

        if (_lexer.Current == '(')
        {
            var openParenToken = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
            var inner = ParseArithmetic(0);
            AccumulateInlineTrivia();
            if (_lexer.Current != ')')
            {
                _expressionFailed = true;

                return inner;
            }

            var closeParenToken = ReadOperatorToken(ShellSyntaxKind.CloseParenToken, length: 1);

            return new ShellGroupedExpressionSyntax(openParenToken, inner, closeParenToken);
        }

        var operand = ParseArithmeticOperand();
        if (operand.Parts.Count == 0)
        {
            // An operator with nothing to operate on: the text is not arithmetic.
            _expressionFailed = true;
        }

        return new ShellOperandExpressionSyntax(operand);
    }

    /// <summary>
    /// Reads one arithmetic operand. A bare name is a variable in arithmetic, so the run stops at any operator
    /// character rather than at the usual word boundaries.
    /// </summary>
    private ShellWordSyntax ParseArithmeticOperand()
    {
        var parts = new List<ShellWordPartSyntax>();
        var isFirst = true;

        while (!_lexer.IsAtEnd && !IsArithmeticOperandBoundary(_lexer.Current))
        {
            var (trivia, fullStart) = isFirst ? TakeTrivia() : ([], _lexer.Position);
            isFirst = false;

            var positionBefore = _lexer.Position;
            parts.Add(_lexer.Current switch
            {
                '$' => ParseDollarPart(trivia, fullStart),
                '\'' => ParseSingleQuotedString(trivia, fullStart),
                '"' => ParseDoubleQuotedString(trivia, fullStart),
                '`' => ParseBackquoteSubstitution(trivia, fullStart),
                _ => ParseArithmeticLiteralRun(trivia, fullStart),
            });

            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }
        }

        return new ShellWordSyntax(parts);
    }

    private ShellLiteralWordPartSyntax ParseArithmeticLiteralRun(IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart)
    {
        var start = _lexer.Position;
        while (!_lexer.IsAtEnd && !IsArithmeticOperandBoundary(_lexer.Current) && _lexer.Current is not '$' and not '\'' and not '"' and not '`')
        {
            _lexer.Position++;
        }

        if (_lexer.Position == start)
        {
            _lexer.Position++;
        }

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, start, leadingTrivia, fullStart));
    }

    private static bool IsArithmeticOperandBoundary(char value) =>
        value is ' ' or '\t' or '\r' or '\n' or '\0'
            or '+' or '-' or '*' or '/' or '%' or '=' or '!' or '~' or '<' or '>' or '&' or '|' or '^'
            or '?' or ':' or ',' or '(' or ')';

    // ---- conditional ----

    private ShellExpressionSyntax ParseConditionalOrCore()
    {
        var left = ParseConditionalAnd();

        while (true)
        {
            AccumulateInlineTrivia();
            if (!MatchesAt(_lexer.Position, "||"))
                break;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.PipePipeToken, length: 2);
            left = new ShellBinaryExpressionSyntax(left, operatorToken, ParseConditionalAnd());
        }

        return left;
    }

    private ShellExpressionSyntax ParseConditionalAnd()
    {
        var left = ParseConditionalUnary();

        while (true)
        {
            AccumulateInlineTrivia();
            if (!MatchesAt(_lexer.Position, "&&"))
                break;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.AmpersandAmpersandToken, length: 2);
            left = new ShellBinaryExpressionSyntax(left, operatorToken, ParseConditionalUnary());
        }

        return left;
    }

    private ShellExpressionSyntax ParseConditionalUnaryCore()
    {
        AccumulateInlineTrivia();

        if (_lexer.Current == '!')
        {
            var token = ReadOperatorToken(ShellSyntaxKind.ExclamationToken, length: 1);

            return new ShellUnaryExpressionSyntax(ShellSyntaxKind.PrefixUnaryExpression, token, ParseConditionalUnary(), postfixOperatorToken: null);
        }

        if (_lexer.Current == '(')
        {
            var openParenToken = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
            var inner = ParseConditionalOr();
            AccumulateInlineTrivia();
            if (_lexer.Current != ')')
            {
                _expressionFailed = true;

                return inner;
            }

            var closeParenToken = ReadOperatorToken(ShellSyntaxKind.CloseParenToken, length: 1);

            return new ShellGroupedExpressionSyntax(openParenToken, inner, closeParenToken);
        }

        if (PeekConditionalWord() is { } unary && Array.IndexOf(ConditionalUnaryOperators, unary) >= 0)
        {
            var token = ReadOperatorToken(ShellSyntaxKind.OperatorToken, unary.Length);

            return new ShellUnaryExpressionSyntax(ShellSyntaxKind.PrefixUnaryExpression, token, ParseConditionalOperand(), postfixOperatorToken: null);
        }

        var left = ParseConditionalOperand();

        AccumulateInlineTrivia();
        foreach (var candidate in ConditionalBinaryOperators)
        {
            if (!MatchesAt(_lexer.Position, candidate))
                continue;

            // A `-eq` style operator has to be a whole word; `-eqx` is not one.
            if (candidate[0] == '-' && !IsConditionalWordEnd(_lexer.Position + candidate.Length))
                continue;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, candidate.Length);

            return new ShellBinaryExpressionSyntax(left, operatorToken, ParseConditionalOperand());
        }

        return left;
    }

    private ShellOperandExpressionSyntax ParseConditionalOperand()
    {
        AccumulateInlineTrivia();

        var word = _lexer.IsAtEnd || PosixLexer.IsWordBoundary(_lexer.Current)
            ? new ShellWordSyntax([])
            : ParseWord();

        if (word.Parts.Count == 0)
        {
            _expressionFailed = true;
        }

        return new ShellOperandExpressionSyntax(word);
    }

    private string? PeekConditionalWord()
    {
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start;
        if (scan < text.Length && text[scan] == '-')
        {
            scan++;
        }

        while (scan < text.Length && char.IsAsciiLetter(text[scan]))
        {
            scan++;
        }

        return scan == start || !IsConditionalWordEnd(scan) ? null : text[start..scan];
    }

    private bool IsConditionalWordEnd(int position) =>
        position >= _lexer.Text.Length || PosixLexer.IsWordBoundary(_lexer.Text[position]);

    private bool MatchesAt(int position, string value)
    {
        var text = _lexer.Text;
        if (position + value.Length > text.Length)
            return false;

        return text.AsSpan(position, value.Length).SequenceEqual(value);
    }
}
