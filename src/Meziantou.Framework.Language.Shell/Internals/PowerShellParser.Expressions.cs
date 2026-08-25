namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>PowerShell expressions, in precedence order from assignment down to primary expressions.</summary>
internal sealed partial class PowerShellParser
{
    private static readonly string[] LogicalOperators = ["and", "or", "xor"];
    private static readonly string[] BitwiseOperators = ["band", "bor", "bxor", "shl", "shr"];

    private static readonly string[] ComparisonOperators =
    [
        "eq", "ne", "gt", "ge", "lt", "le", "like", "notlike", "match", "notmatch", "contains", "notcontains",
        "in", "notin", "is", "isnot", "replace", "split", "join", "f", "as",
        "ieq", "ine", "igt", "ige", "ilt", "ile", "ilike", "inotlike", "imatch", "inotmatch", "icontains",
        "inotcontains", "iin", "inotin", "ireplace", "isplit",
        "ceq", "cne", "cgt", "cge", "clt", "cle", "clike", "cnotlike", "cmatch", "cnotmatch", "ccontains",
        "cnotcontains", "cin", "cnotin", "creplace", "csplit",
    ];

    private static readonly string[] UnaryWordOperators = ["not", "bnot", "split", "join"];

    private ShellExpressionSyntax ParseExpression()
    {
        if (!TryEnterRecursion(new TextSpan(_lexer.Position, 0)))
            return new ShellRawExpressionSyntax(ConsumeRestAsToken());

        try
        {
            return ParseAssignmentExpression();
        }
        finally
        {
            _depth--;
        }
    }

    private ShellExpressionSyntax ParseAssignmentExpression()
    {
        var left = ParseArrayLiteralExpression();

        AccumulateInlineTrivia();
        var length = GetAssignmentOperatorLength();
        if (length == 0)
            return left;

        var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, length);
        AccumulateStatementTrivia();
        ShellSyntaxNode value = IsExpressionStart() ? ParseAssignmentExpression() : ParseCommand();

        return new PowerShellAssignmentExpressionSyntax(left, operatorToken, value);
    }

    private int GetAssignmentOperatorLength()
    {
        var (a, b, c) = (_lexer.Current, _lexer.Peek(1), _lexer.Peek(2));

        return (a, b, c) switch
        {
            ('?', '?', '=') when _options.Dialect.HasFeature(ShellDialectFeatures.NullCoalescing) => 3,
            ('+', '=', _) or ('-', '=', _) or ('*', '=', _) or ('/', '=', _) or ('%', '=', _) => 2,
            ('=', not '=', _) => 1,
            _ => 0,
        };
    }

    private ShellExpressionSyntax ParseArrayLiteralExpression()
    {
        var first = ParseTernaryExpression();

        AccumulateInlineTrivia();
        if (_lexer.Current != ',')
            return first;

        var elements = new List<ShellExpressionSyntax> { first };
        var separators = new List<ShellSyntaxToken>();
        while (_lexer.Current == ',')
        {
            separators.Add(ReadOperatorToken(ShellSyntaxKind.CommaToken, length: 1));
            AccumulateStatementTrivia();
            elements.Add(ParseTernaryExpression());
            AccumulateInlineTrivia();
        }

        return new PowerShellArrayLiteralSyntax(elements, separators);
    }

    private ShellExpressionSyntax ParseTernaryExpression()
    {
        var condition = ParseNullCoalescingExpression();
        if (!_options.Dialect.HasFeature(ShellDialectFeatures.TernaryOperator))
            return condition;

        AccumulateInlineTrivia();
        if (_lexer.Current != '?' || _lexer.Peek(1) == '?')
            return condition;

        var questionToken = ReadOperatorToken(ShellSyntaxKind.QuestionToken, length: 1);
        AccumulateStatementTrivia();
        var whenTrue = ParseTernaryExpression();
        var colonToken = ExpectCharacter(':', ShellSyntaxKind.ColonToken);
        AccumulateStatementTrivia();

        return new PowerShellTernaryExpressionSyntax(condition, questionToken, whenTrue, colonToken, ParseTernaryExpression());
    }

    private ShellExpressionSyntax ParseNullCoalescingExpression()
    {
        var left = ParseWordOperatorExpression(LogicalOperators, ParseBitwiseExpression);
        if (!_options.Dialect.HasFeature(ShellDialectFeatures.NullCoalescing))
            return left;

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current != '?' || _lexer.Peek(1) != '?' || _lexer.Peek(2) == '=')
                break;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.QuestionQuestionToken, length: 2);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(ShellSyntaxKind.PowerShellBinaryExpression, left, operatorToken, ParseWordOperatorExpression(LogicalOperators, ParseBitwiseExpression));
        }

        return left;
    }

    private ShellExpressionSyntax ParseBitwiseExpression() => ParseWordOperatorExpression(BitwiseOperators, ParseComparisonExpression);

    private ShellExpressionSyntax ParseComparisonExpression() => ParseWordOperatorExpression(ComparisonOperators, ParseRangeExpression);

    /// <summary>Handles the <c>-name</c> style operators, which all share the same shape.</summary>
    private ShellExpressionSyntax ParseWordOperatorExpression(string[] operators, Func<ShellExpressionSyntax> parseOperand)
    {
        var left = parseOperand();

        while (true)
        {
            AccumulateInlineTrivia();
            var name = PeekWordOperator();
            if (name is null || Array.IndexOf(operators, name) < 0)
                break;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, name.Length + 1);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(ShellSyntaxKind.PowerShellBinaryExpression, left, operatorToken, parseOperand());
        }

        return left;
    }

    /// <summary>Returns the lowercase name of a <c>-eq</c> style operator at the current position, without consuming it.</summary>
    private string? PeekWordOperator()
    {
        if (_lexer.Current != '-')
            return null;

        var text = _lexer.Text;
        var scan = _lexer.Position + 1;
        var start = scan;
        while (scan < text.Length && char.IsAsciiLetter(text[scan]))
        {
            scan++;
        }

        if (scan == start)
            return null;

        // `-eqx` is not an operator; the name has to end the token.
        if (scan < text.Length && PowerShellLexer.IsNameCharacter(text[scan]))
            return null;

        return text[start..scan].ToLowerInvariant();
    }

    private ShellExpressionSyntax ParseRangeExpression()
    {
        var left = ParseAdditiveExpression();

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current != '.' || _lexer.Peek(1) != '.')
                break;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, length: 2);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(ShellSyntaxKind.PowerShellRangeExpression, left, operatorToken, ParseAdditiveExpression());
        }

        return left;
    }

    private ShellExpressionSyntax ParseAdditiveExpression()
    {
        var left = ParseMultiplicativeExpression();

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current is not ('+' or '-') || _lexer.Peek(1) is '=' || _lexer.Peek(1) == _lexer.Current)
                break;

            // `-f` and `-eq` are operators in their own right, not a minus applied to a bare word.
            if (PeekWordOperator() is not null)
                break;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, length: 1);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(ShellSyntaxKind.PowerShellBinaryExpression, left, operatorToken, ParseMultiplicativeExpression());
        }

        return left;
    }

    private ShellExpressionSyntax ParseMultiplicativeExpression()
    {
        var left = ParseUnaryExpression();

        while (true)
        {
            AccumulateInlineTrivia();
            if (_lexer.Current is not ('*' or '/' or '%') || _lexer.Peek(1) == '=')
                break;

            var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, length: 1);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(ShellSyntaxKind.PowerShellBinaryExpression, left, operatorToken, ParseUnaryExpression());
        }

        return left;
    }

    private ShellExpressionSyntax ParseUnaryExpression()
    {
        AccumulateStatementTrivia();

        // The unary comma wraps its operand in a one-element array: `,1`.
        if (_lexer.Current == ',')
        {
            var commaToken = ReadOperatorToken(ShellSyntaxKind.CommaToken, length: 1);

            return new PowerShellUnaryExpressionSyntax(ShellSyntaxKind.PowerShellPrefixUnaryExpression, commaToken, ParseUnaryExpression(), postfixOperatorToken: null);
        }

        if (_lexer.Current is '+' or '-' && _lexer.Peek(1) == _lexer.Current)
        {
            var incrementToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, length: 2);

            return new PowerShellUnaryExpressionSyntax(ShellSyntaxKind.PowerShellPrefixUnaryExpression, incrementToken, ParseUnaryExpression(), postfixOperatorToken: null);
        }

        // A word operator has to be recognized before the single-character `-`, or `-not $x` reads as `-(not $x)`.
        if (PeekWordOperator() is { } wordOperator)
        {
            if (Array.IndexOf(UnaryWordOperators, wordOperator) < 0)
                return ParsePostfixExpression();

            var wordOperatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, wordOperator.Length + 1);

            return new PowerShellUnaryExpressionSyntax(ShellSyntaxKind.PowerShellPrefixUnaryExpression, wordOperatorToken, ParseUnaryExpression(), postfixOperatorToken: null);
        }

        if (_lexer.Current is '!' or '+' or '-' && _lexer.Peek(1) != '=')
        {
            var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, length: 1);

            return new PowerShellUnaryExpressionSyntax(ShellSyntaxKind.PowerShellPrefixUnaryExpression, operatorToken, ParseUnaryExpression(), postfixOperatorToken: null);
        }

        // `[type]` followed by a value is a cast; on its own it is a type literal.
        if (_lexer.Current == '[')
        {
            var type = ParseTypeLiteral();
            AccumulateInlineTrivia();
            if (!_lexer.IsAtEnd && IsCastOperandStart())
                return new PowerShellCastExpressionSyntax(type, ParseUnaryExpression());

            return ParsePostfixOperators(type);
        }

        return ParsePostfixExpression();
    }

    private bool IsCastOperandStart() => _lexer.Current is '$' or '(' or '@' or '"' or '\'' or '[' || char.IsAsciiDigit(_lexer.Current);

    private ShellExpressionSyntax ParsePostfixExpression() => ParsePostfixOperators(ParsePrimaryExpression());

    private ShellExpressionSyntax ParsePostfixOperators(ShellExpressionSyntax expression)
    {
        var nullConditional = _options.Dialect.HasFeature(ShellDialectFeatures.NullCoalescing);
        while (true)
        {
            if (_lexer.Current is '.' && (PowerShellLexer.IsNameStart(_lexer.Peek(1)) || _lexer.Peek(1) is '$' or '\'' or '"'))
            {
                var operatorToken = ReadOperatorToken(ShellSyntaxKind.DotToken, length: 1);
                expression = ContinueMemberAccess(expression, operatorToken);
                continue;
            }

            // `$x?.y` and `$x?[0]` only null-conditional when the `?` touches the accessor; `$x ?.y` is an error in
            // PowerShell, and a detached `?` belongs to the ternary operator.
            if (nullConditional && _lexer.Current == '?' && _lexer.Peek(1) == '.' && (PowerShellLexer.IsNameStart(_lexer.Peek(2)) || _lexer.Peek(2) == '$'))
            {
                var operatorToken = ReadOperatorToken(ShellSyntaxKind.DotToken, length: 2);
                expression = ContinueMemberAccess(expression, operatorToken);
                continue;
            }

            if (_lexer.Current == ':' && _lexer.Peek(1) == ':')
            {
                var operatorToken = ReadOperatorToken(ShellSyntaxKind.ColonColonToken, length: 2);
                expression = ContinueMemberAccess(expression, operatorToken);
                continue;
            }

            if (_lexer.Current == '[' || (nullConditional && _lexer.Current == '?' && _lexer.Peek(1) == '['))
            {
                var openBracket = ReadOperatorToken(ShellSyntaxKind.OpenBracketToken, _lexer.Current == '?' ? 2 : 1);
                AccumulateStatementTrivia();
                var index = ParseArrayLiteralExpression();
                expression = new PowerShellIndexExpressionSyntax(expression, openBracket, index, ExpectCharacter(']', ShellSyntaxKind.CloseBracketToken));
                continue;
            }

            // `$x++` and `$x ++` are both postfix increments; the whitespace becomes the operator's leading trivia.
            if (IsAtPostfixIncrement())
            {
                AccumulateInlineTrivia();
                var operatorToken = ReadOperatorToken(ShellSyntaxKind.OperatorToken, length: 2);
                expression = new PowerShellUnaryExpressionSyntax(ShellSyntaxKind.PowerShellPostfixUnaryExpression, prefixOperatorToken: null, expression, operatorToken);
                continue;
            }

            return expression;
        }
    }

    /// <summary>Returns whether a <c>++</c> or <c>--</c> follows, possibly after inline whitespace.</summary>
    private bool IsAtPostfixIncrement()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        while (scan < text.Length && text[scan] is ' ' or '\t')
        {
            scan++;
        }

        return scan + 1 < text.Length && text[scan] is '+' or '-' && text[scan + 1] == text[scan];
    }

    private ShellExpressionSyntax ContinueMemberAccess(ShellExpressionSyntax target, ShellSyntaxToken operatorToken)
    {
        var memberNameToken = ReadMemberNameToken();
        var access = new PowerShellMemberAccessExpressionSyntax(target, operatorToken, memberNameToken);

        if (_lexer.Current != '(')
            return access;

        var openParen = ReadOperatorToken(ShellSyntaxKind.OpenParenToken, length: 1);
        var arguments = new List<ShellExpressionSyntax>();
        var separators = new List<ShellSyntaxToken>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
                break;

            var positionBefore = _lexer.Position;
            arguments.Add(ParseTernaryExpression());
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

        return new PowerShellInvocationExpressionSyntax(access, openParen, arguments, separators, ExpectCharacter(')', ShellSyntaxKind.CloseParenToken));
    }

    private ShellSyntaxToken ReadMemberNameToken()
    {
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start;

        // A member name may be quoted when it is not a valid identifier, as in `$xml.results.'test-case'`.
        if (scan < text.Length && text[scan] is '\'' or '"')
        {
            var quote = text[scan];
            scan++;
            while (scan < text.Length && text[scan] != quote)
            {
                scan++;
            }

            if (scan < text.Length)
            {
                scan++;
            }

            return ReadOperatorToken(ShellSyntaxKind.GenericToken, scan - start);
        }

        // A member name given by a variable keeps its scope prefix, as in `$info.$script:Version`.
        var variable = scan < text.Length && text[scan] == '$';
        if (variable)
        {
            scan++;
        }

        while (scan < text.Length && (variable ? PowerShellLexer.IsVariableNameCharacter(text[scan]) : PowerShellLexer.IsNameCharacter(text[scan])))
        {
            scan++;
        }

        if (scan == start)
        {
            AddDiagnostic(new TextSpan(start, 0), "SHELL0021", "Expected a member name.");

            return MissingToken(ShellSyntaxKind.GenericToken, start);
        }

        return ReadOperatorToken(ShellSyntaxKind.GenericToken, scan - start);
    }

    // ---- primary expressions ----

    private ShellExpressionSyntax ParsePrimaryExpression()
    {
        AccumulateStatementTrivia();

        switch (_lexer.Current)
        {
            case '$' when _lexer.Peek(1) == '(':
                return ParseSubExpression(ShellSyntaxKind.PowerShellSubExpression, openLength: 2);
            case '$':
                return ParseVariableExpression();
            case '@' when _lexer.Peek(1) == '(':
                return ParseSubExpression(ShellSyntaxKind.PowerShellArrayExpression, openLength: 2);
            case '@' when _lexer.Peek(1) == '{':
                return ParseHashLiteral();
            case '@' when IsAtHereStringStart():
                return ParseHereString();
            case '@':
                return ParseVariableExpression();
            case '(':
                return ParseParenthesizedExpression();
            case '{':
                return ParseScriptBlock();
            case '"':
                return ParseExpandableString();
            case '\'':
                return ParseVerbatimStringExpression();
            case '[':
                return ParseTypeLiteral();
        }

        if (char.IsAsciiDigit(_lexer.Current) || (_lexer.Current == '.' && char.IsAsciiDigit(_lexer.Peek(1))))
            return ParseNumberLiteral();

        return new PowerShellLiteralExpressionSyntax(ShellSyntaxKind.PowerShellBareWord, ReadBareToken());
    }

    private PowerShellParenthesizedExpressionSyntax ParseParenthesizedExpression()
    {
        var openParen = ExpectCharacter('(', ShellSyntaxKind.OpenParenToken);
        var statements = ParseStatementList(stopCharacter: ')');

        return new PowerShellParenthesizedExpressionSyntax(openParen, statements, ExpectCharacter(')', ShellSyntaxKind.CloseParenToken));
    }

    private PowerShellSubExpressionSyntax ParseSubExpression(ShellSyntaxKind kind, int openLength)
    {
        var openToken = ReadOperatorToken(kind == ShellSyntaxKind.PowerShellArrayExpression ? ShellSyntaxKind.AtParenToken : ShellSyntaxKind.DollarOpenParenToken, openLength);
        var statements = ParseStatementList(stopCharacter: ')');

        return new PowerShellSubExpressionSyntax(kind, openToken, statements, ExpectCharacter(')', ShellSyntaxKind.CloseParenToken));
    }

    private PowerShellHashLiteralSyntax ParseHashLiteral()
    {
        var openToken = ReadOperatorToken(ShellSyntaxKind.AtBraceToken, length: 2);
        var entries = new List<PowerShellHashEntrySyntax>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == '}')
                break;

            if (_lexer.Current == ';')
            {
                // A stray separator before any entry still has to survive the round trip.
                var stray = ReadOperatorToken(ShellSyntaxKind.SemicolonToken, length: 1);
                AddDiagnostic(stray.Span, "SHELL0002", "Unexpected ';'.");
                continue;
            }

            var positionBefore = _lexer.Position;

            // A key may be any simple expression, as in `@{ $parameter.Name = $parameter.Value }`, and a value may be
            // an array or a whole pipeline, as in `@{ Names = $items | Sort-Object }`.
            var key = ParsePostfixExpression();
            var equalsToken = ExpectCharacter('=', ShellSyntaxKind.EqualsToken);
            AccumulateStatementTrivia();
            var value = ParseClause(ParseArrayLiteralExpression);

            AccumulateInlineTrivia();
            var separator = _lexer.Current == ';' ? ReadOperatorToken(ShellSyntaxKind.SemicolonToken, length: 1) : null;
            entries.Add(new PowerShellHashEntrySyntax(key, equalsToken, value, separator));

            if (_lexer.Position == positionBefore)
                break;
        }

        return new PowerShellHashLiteralSyntax(openToken, entries, ExpectCharacter('}', ShellSyntaxKind.CloseBraceToken));
    }

    private PowerShellVariableExpressionSyntax ParseVariableExpression()
    {
        AccumulateInlineTrivia();
        var (trivia, fullStart) = TakeTrivia();
        var sigilStart = _lexer.Position;
        _lexer.Position++;
        var sigilToken = _lexer.CreateToken(ShellSyntaxKind.DollarToken, sigilStart, trivia, fullStart);

        var nameStart = _lexer.Position;
        if (_lexer.Current == '{')
        {
            _lexer.Position++;
            while (!_lexer.IsAtEnd && _lexer.Current != '}')
            {
                _lexer.Position++;
            }

            if (_lexer.IsAtEnd)
            {
                AddDiagnostic(sigilToken.Span, "SHELL0005", "Unterminated variable name.");
            }
            else
            {
                _lexer.Position++;
            }
        }
        else if (PowerShellLexer.IsVariableNameCharacter(_lexer.Current))
        {
            while (!_lexer.IsAtEnd && PowerShellLexer.IsVariableNameCharacter(_lexer.Current))
            {
                _lexer.Position++;
            }

            // An automatic variable keeps its scope prefix, as in `$global:?`.
            if (!_lexer.IsAtEnd && _lexer.Current is '?' or '^' && _lexer.Peek(-1) == ':')
            {
                _lexer.Position++;
            }
        }
        else if (!_lexer.IsAtEnd && _lexer.Current is '?' or '^' or '$' or '_')
        {
            _lexer.Position++;
        }

        var rawName = _lexer.Text[nameStart..Math.Clamp(_lexer.Position, nameStart, _lexer.Text.Length)];
        var nameToken = _lexer.CreateToken(ShellSyntaxKind.VariableNameToken, nameStart, [], nameStart, rawName.Trim('{', '}'));

        return new PowerShellVariableExpressionSyntax(sigilToken, nameToken);
    }

    private PowerShellTypeLiteralSyntax ParseTypeLiteral()
    {
        var openBracket = ExpectCharacter('[', ShellSyntaxKind.OpenBracketToken);
        var nameToken = ReadTypeNameToken(includeArgumentList: true, insideBrackets: true);

        return new PowerShellTypeLiteralSyntax(openBracket, nameToken, ExpectCharacter(']', ShellSyntaxKind.CloseBracketToken));
    }

    private PowerShellLiteralExpressionSyntax ParseNumberLiteral()
    {
        AccumulateInlineTrivia();
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start;

        if (scan + 1 < text.Length && text[scan] == '0' && (text[scan + 1] is 'x' or 'X' or 'b' or 'B'))
        {
            scan += 2;
            while (scan < text.Length && (char.IsAsciiLetterOrDigit(text[scan]) || text[scan] == '_'))
            {
                scan++;
            }
        }
        else
        {
            while (scan < text.Length && (char.IsAsciiDigit(text[scan]) || text[scan] == '_'))
            {
                scan++;
            }

            // A dot only continues the number when it is not the range operator or a member access.
            if (scan < text.Length && text[scan] == '.' && scan + 1 < text.Length && char.IsAsciiDigit(text[scan + 1]))
            {
                scan++;
                while (scan < text.Length && char.IsAsciiDigit(text[scan]))
                {
                    scan++;
                }
            }

            if (scan < text.Length && text[scan] is 'e' or 'E' && scan + 1 < text.Length && (char.IsAsciiDigit(text[scan + 1]) || text[scan + 1] is '+' or '-'))
            {
                scan += 2;
                while (scan < text.Length && char.IsAsciiDigit(text[scan]))
                {
                    scan++;
                }
            }

            // Multiplier and type suffixes: 10kb, 5mb, 3L, 2d.
            while (scan < text.Length && char.IsAsciiLetter(text[scan]))
            {
                scan++;
            }
        }

        return new PowerShellLiteralExpressionSyntax(ShellSyntaxKind.PowerShellNumberLiteral, ReadOperatorToken(ShellSyntaxKind.NumberToken, scan - start));
    }

    private PowerShellLiteralExpressionSyntax ParseVerbatimStringExpression()
    {
        var (trivia, fullStart) = TakeTrivia();
        var quoted = ParseVerbatimString(trivia, fullStart);
        var value = quoted.Parts.Count == 0 ? string.Empty : ((ShellLiteralWordPartSyntax)quoted.Parts[0]).Value;
        var text = quoted.ToFullString();
        var token = new ShellSyntaxToken(ShellSyntaxKind.GenericToken, text[(quoted.Span.Start - quoted.FullSpan.Start)..], value, leadingTrivia: trivia, fullStart: fullStart);

        return new PowerShellLiteralExpressionSyntax(ShellSyntaxKind.PowerShellStringLiteral, token);
    }

    /// <summary>Reads a double-quoted string, keeping embedded variables and subexpressions as child nodes.</summary>
    private PowerShellExpandableStringSyntax ParseExpandableString()
    {
        AccumulateInlineTrivia();
        var (trivia, fullStart) = TakeTrivia();
        var quoteStart = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(ShellSyntaxKind.DoubleQuoteToken, quoteStart, trivia, fullStart);

        var parts = new List<ShellSyntaxNode>();
        var terminated = false;
        while (!_lexer.IsAtEnd)
        {
            if (_lexer.Current == '"')
            {
                // Two double quotes stand for one literal quote inside an expandable string.
                if (_lexer.Peek(1) == '"')
                {
                    var escapeStart = _lexer.Position;
                    _lexer.Position += 2;
                    parts.Add(new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, escapeStart, [], escapeStart, "\"")));
                    continue;
                }

                terminated = true;
                break;
            }

            var positionBefore = _lexer.Position;
            if (_lexer.Current == '`')
            {
                parts.Add(ParseEscapeSequence([], _lexer.Position));
            }
            else if (_lexer.Current == '$' && _lexer.Peek(1) == '(')
            {
                parts.Add(ParseSubExpression(ShellSyntaxKind.PowerShellSubExpression, openLength: 2));
            }
            else if (_lexer.Current == '$' && (PowerShellLexer.IsVariableNameCharacter(_lexer.Peek(1)) || _lexer.Peek(1) == '{'))
            {
                parts.Add(ParseVariableExpression());
            }
            else
            {
                parts.Add(ReadStringLiteralRun(1));
            }

            if (_lexer.Position == positionBefore)
            {
                _lexer.Position++;
            }
        }

        ShellSyntaxToken closeToken;
        if (!terminated)
        {
            AddDiagnostic(openToken.Span, "SHELL0003", "Unterminated double-quoted string.");
            closeToken = MissingToken(ShellSyntaxKind.DoubleQuoteToken, _lexer.Position);
        }
        else
        {
            var closeStart = _lexer.Position;
            _lexer.Position++;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.DoubleQuoteToken, closeStart, [], closeStart);
        }

        return new PowerShellExpandableStringSyntax(ShellSyntaxKind.PowerShellExpandableString, openToken, parts, closeToken);
    }

    private ShellLiteralWordPartSyntax ReadStringLiteralRun(int minimumLength)
    {
        var start = _lexer.Position;
        _lexer.Position += minimumLength;
        while (!_lexer.IsAtEnd && _lexer.Current is not '"' and not '`' and not '$')
        {
            _lexer.Position++;
        }

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(ShellSyntaxKind.BareTextToken, start, [], start));
    }

    /// <summary>Reads a here-string, <c>@" ... "@</c> or <c>@' ... '@</c>, whose body is kept verbatim.</summary>
    private PowerShellExpandableStringSyntax ParseHereString()
    {
        AccumulateInlineTrivia();
        var (trivia, fullStart) = TakeTrivia();
        var quote = _lexer.Peek(1);
        var start = _lexer.Position;
        _lexer.Position += 2;
        var openToken = _lexer.CreateToken(ShellSyntaxKind.HereStringStartToken, start, trivia, fullStart);

        var bodyStart = _lexer.Position;
        var closeStart = FindHereStringTerminator(bodyStart, quote, depth: 0);
        _lexer.Position = closeStart < 0 ? _lexer.Text.Length : closeStart;

        if (closeStart < 0)
        {
            AddDiagnostic(openToken.Span, "SHELL0022", "Unterminated here-string.");
            closeStart = _lexer.Position;
        }

        var bodyText = _lexer.Text[bodyStart..closeStart];
        var body = new ShellLiteralWordPartSyntax(new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, bodyText, StripHereStringDelimiterLineBreaks(bodyText), fullStart: bodyStart));

        ShellSyntaxToken closeToken;
        if (_lexer.IsAtEnd)
        {
            closeToken = MissingToken(ShellSyntaxKind.HereStringEndToken, _lexer.Position);
        }
        else
        {
            _lexer.Position += 2;
            closeToken = _lexer.CreateToken(ShellSyntaxKind.HereStringEndToken, closeStart, [], closeStart);
        }

        var kind = quote == '"' ? ShellSyntaxKind.PowerShellHereString : ShellSyntaxKind.PowerShellStringLiteral;

        return new PowerShellExpandableStringSyntax(kind, openToken, bodyText.Length == 0 ? [] : [body], closeToken);
    }

    /// <summary>
    /// Removes the line break that follows the opening delimiter and the one that precedes the closing delimiter.
    /// Both belong to the delimiters rather than the content, so they are dropped from the value but kept in the text.
    /// </summary>
    private static string StripHereStringDelimiterLineBreaks(string bodyText)
    {
        var start = 0;
        var end = bodyText.Length;

        if (start < end && SourceText.GetLineBreakLength(bodyText, start) is var leading && leading > 0)
        {
            start += leading;
        }

        if (end > start && bodyText[end - 1] == '\n')
        {
            end--;
            if (end > start && bodyText[end - 1] == '\r')
            {
                end--;
            }
        }
        else if (end > start && bodyText[end - 1] == '\r')
        {
            end--;
        }

        return bodyText[start..end];
    }

    private bool IsAtLineStart(int position)
    {
        if (position == 0)
            return true;

        var previous = _lexer.Text[position - 1];

        return previous is '\n' or '\r';
    }

    /// <summary>
    /// Returns the index of the <c>"@</c> that ends a here-string body, or <c>-1</c> when there is none. An expandable
    /// here-string may embed a <c>$( … )</c> that itself contains a here-string, and the inner terminator must not be
    /// mistaken for the outer one.
    /// </summary>
    private int FindHereStringTerminator(int index, char quote, int depth)
    {
        var text = _lexer.Text;
        var expandable = quote == '"';
        while (index < text.Length)
        {
            if (text[index] == quote && index + 1 < text.Length && text[index + 1] == '@' && IsAtLineStart(index))
                return index;

            if (expandable && depth < _options.MaxRecursionDepth && text[index] == '$' && index + 1 < text.Length && text[index + 1] == '(')
            {
                index = SkipSubexpression(index + 1, depth + 1);
                continue;
            }

            index++;
        }

        return -1;
    }

    /// <summary>Returns the index just past the <c>( … )</c> that starts at <paramref name="index"/>.</summary>
    private int SkipSubexpression(int index, int depth)
    {
        var text = _lexer.Text;
        var parenDepth = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '`')
            {
                index += 2;
                continue;
            }

            if (current == '(')
            {
                parenDepth++;
            }
            else if (current == ')')
            {
                parenDepth--;
                if (parenDepth == 0)
                    return index + 1;
            }
            else if (current == '@' && index + 1 < text.Length && text[index + 1] is '"' or '\'' && IsHereStringOpenAt(index))
            {
                var innerQuote = text[index + 1];
                var terminator = depth < _options.MaxRecursionDepth ? FindHereStringTerminator(index + 2, innerQuote, depth + 1) : -1;
                index = terminator < 0 ? text.Length : terminator + 2;
                continue;
            }
            else if (current is '\'' or '"')
            {
                index = SkipQuotedString(index);
                continue;
            }

            index++;
        }

        return index;
    }

    /// <summary>Returns the index just past the single-line quoted string that starts at <paramref name="index"/>.</summary>
    private int SkipQuotedString(int index)
    {
        var text = _lexer.Text;
        var quote = text[index];
        index++;
        while (index < text.Length)
        {
            if (quote == '"' && text[index] == '`')
            {
                index += 2;
                continue;
            }

            if (text[index] == quote)
            {
                // A doubled quote is an escaped quote, not the end of the string.
                if (index + 1 < text.Length && text[index + 1] == quote)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return index;
    }

    /// <summary>Returns whether the <c>@"</c> at <paramref name="index"/> opens a here-string rather than a splat.</summary>
    private bool IsHereStringOpenAt(int index)
    {
        var text = _lexer.Text;
        var scan = index + 2;
        while (scan < text.Length && text[scan] is ' ' or '\t')
        {
            scan++;
        }

        return scan >= text.Length || SourceText.GetLineBreakLength(text, scan) > 0;
    }

    private ShellSyntaxToken ConsumeRestAsToken()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _lexer.Position;
        _lexer.Position = _lexer.Text.Length;
        var text = _lexer.Text[start..];

        return new ShellSyntaxToken(ShellSyntaxKind.BadToken, text, text, leadingTrivia: trivia, fullStart: fullStart);
    }
}
