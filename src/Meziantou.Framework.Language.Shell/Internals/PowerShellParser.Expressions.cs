using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

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

    /// <summary>
    /// Reads a single expression from the start of the text, for <c>ShellSyntaxTree.ParseExpression</c>.
    /// </summary>
    /// <param name="hasTrailingContent">Set when something other than trivia follows the expression.</param>
    public ShellExpressionSyntax ParseSingleExpression(out bool hasTrailingContent)
    {
        AccumulateStatementTrivia();
        if (_lexer.IsAtEnd)
        {
            hasTrailingContent = false;
            var (trivia, fullStart) = TakeTrivia();

            return new ShellRawExpressionSyntax(new ScannedToken(
                SyntaxKind.BareTextToken, string.Empty, string.Empty, leadingTrivia: trivia, fullStart: fullStart));
        }

        var expression = ParseExpression();

        AccumulateStatementTrivia();
        hasTrailingContent = !_lexer.IsAtEnd;

        return expression;
    }

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
        // Recurses into itself for a right-associative chain, so it needs the guard even though ParseExpression above
        // already applied one: a chain never goes back through ParseExpression.
        if (!TryEnterRecursion(new TextSpan(_lexer.Position, 0)))
            return new ShellRawExpressionSyntax(ConsumeRestAsToken());

        try
        {
            return ParseAssignmentExpressionCore();
        }
        finally
        {
            _depth--;
        }
    }

    private ShellExpressionSyntax ParseAssignmentExpressionCore()
    {
        var left = ParseArrayLiteralExpression();

        AccumulateInlineTrivia();
        var length = GetAssignmentOperatorLength();
        if (length == 0)
            return left;

        var operatorToken = ReadOperatorToken(SyntaxKind.OperatorToken, length);
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
        var separators = new List<ScannedToken>();
        while (_lexer.Current == ',')
        {
            separators.Add(ReadOperatorToken(SyntaxKind.CommaToken, length: 1));
            AccumulateStatementTrivia();
            elements.Add(ParseTernaryExpression());
            AccumulateInlineTrivia();
        }

        return new PowerShellArrayLiteralSyntax(ParserHelpers.Separated(elements, separators));
    }

    private ShellExpressionSyntax ParseTernaryExpression()
    {
        // Recurses into itself for a right-associative chain, so it needs the guard even though ParseExpression above
        // already applied one: a chain never goes back through ParseExpression.
        if (!TryEnterRecursion(new TextSpan(_lexer.Position, 0)))
            return new ShellRawExpressionSyntax(ConsumeRestAsToken());

        try
        {
            return ParseTernaryExpressionCore();
        }
        finally
        {
            _depth--;
        }
    }

    private ShellExpressionSyntax ParseTernaryExpressionCore()
    {
        var condition = ParseNullCoalescingExpression();
        if (!_options.Dialect.HasFeature(ShellDialectFeatures.TernaryOperator))
            return condition;

        AccumulateInlineTrivia();
        if (_lexer.Current != '?' || _lexer.Peek(1) == '?')
            return condition;

        var questionToken = ReadOperatorToken(SyntaxKind.QuestionToken, length: 1);
        AccumulateStatementTrivia();
        var whenTrue = ParseTernaryExpression();
        var colonToken = ExpectCharacter(':', SyntaxKind.ColonToken);
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

            var operatorToken = ReadOperatorToken(SyntaxKind.QuestionQuestionToken, length: 2);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(SyntaxKind.PowerShellBinaryExpression, left, operatorToken, ParseWordOperatorExpression(LogicalOperators, ParseBitwiseExpression));
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

            var operatorToken = ReadOperatorToken(SyntaxKind.OperatorToken, name.Length + 1);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(SyntaxKind.PowerShellBinaryExpression, left, operatorToken, parseOperand());
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

            var operatorToken = ReadOperatorToken(SyntaxKind.OperatorToken, length: 2);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(SyntaxKind.PowerShellRangeExpression, left, operatorToken, ParseAdditiveExpression());
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

            var operatorToken = ReadOperatorToken(SyntaxKind.OperatorToken, length: 1);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(SyntaxKind.PowerShellBinaryExpression, left, operatorToken, ParseMultiplicativeExpression());
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

            var operatorToken = ReadOperatorToken(SyntaxKind.OperatorToken, length: 1);
            AccumulateStatementTrivia();
            left = new PowerShellBinaryExpressionSyntax(SyntaxKind.PowerShellBinaryExpression, left, operatorToken, ParseUnaryExpression());
        }

        return left;
    }

    private ShellExpressionSyntax ParseUnaryExpression()
    {
        if (!TryEnterRecursion(new TextSpan(_lexer.Position, 0)))
            return new ShellRawExpressionSyntax(ConsumeRestAsToken());

        try
        {
            return ParseUnaryExpressionCore();
        }
        finally
        {
            _depth--;
        }
    }

    private ShellExpressionSyntax ParseUnaryExpressionCore()
    {
        AccumulateStatementTrivia();

        // The unary comma wraps its operand in a one-element array: `,1`.
        if (_lexer.Current == ',')
        {
            var commaToken = ReadOperatorToken(SyntaxKind.CommaToken, length: 1);

            return new PowerShellUnaryExpressionSyntax(SyntaxKind.PowerShellPrefixUnaryExpression, commaToken, ParseUnaryExpression(), postfixOperatorToken: null);
        }

        if (_lexer.Current is '+' or '-' && _lexer.Peek(1) == _lexer.Current)
        {
            var incrementToken = ReadOperatorToken(SyntaxKind.OperatorToken, length: 2);

            return new PowerShellUnaryExpressionSyntax(SyntaxKind.PowerShellPrefixUnaryExpression, incrementToken, ParseUnaryExpression(), postfixOperatorToken: null);
        }

        // A word operator has to be recognized before the single-character `-`, or `-not $x` reads as `-(not $x)`.
        if (PeekWordOperator() is { } wordOperator)
        {
            if (Array.IndexOf(UnaryWordOperators, wordOperator) < 0)
                return ParsePostfixExpression();

            var wordOperatorToken = ReadOperatorToken(SyntaxKind.OperatorToken, wordOperator.Length + 1);

            return new PowerShellUnaryExpressionSyntax(SyntaxKind.PowerShellPrefixUnaryExpression, wordOperatorToken, ParseUnaryExpression(), postfixOperatorToken: null);
        }

        if (_lexer.Current is '!' or '+' or '-' && _lexer.Peek(1) != '=')
        {
            var operatorToken = ReadOperatorToken(SyntaxKind.OperatorToken, length: 1);

            return new PowerShellUnaryExpressionSyntax(SyntaxKind.PowerShellPrefixUnaryExpression, operatorToken, ParseUnaryExpression(), postfixOperatorToken: null);
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
                var operatorToken = ReadOperatorToken(SyntaxKind.DotToken, length: 1);
                expression = ContinueMemberAccess(expression, operatorToken);
                continue;
            }

            // `$x?.y` and `$x?[0]` only null-conditional when the `?` touches the accessor; `$x ?.y` is an error in
            // PowerShell, and a detached `?` belongs to the ternary operator.
            if (nullConditional && _lexer.Current == '?' && _lexer.Peek(1) == '.' && (PowerShellLexer.IsNameStart(_lexer.Peek(2)) || _lexer.Peek(2) == '$'))
            {
                var operatorToken = ReadOperatorToken(SyntaxKind.DotToken, length: 2);
                expression = ContinueMemberAccess(expression, operatorToken);
                continue;
            }

            if (_lexer.Current == ':' && _lexer.Peek(1) == ':')
            {
                var operatorToken = ReadOperatorToken(SyntaxKind.ColonColonToken, length: 2);
                expression = ContinueMemberAccess(expression, operatorToken);
                continue;
            }

            if (_lexer.Current == '[' || (nullConditional && _lexer.Current == '?' && _lexer.Peek(1) == '['))
            {
                var openBracket = ReadOperatorToken(SyntaxKind.OpenBracketToken, _lexer.Current == '?' ? 2 : 1);
                AccumulateStatementTrivia();
                var index = ParseArrayLiteralExpression();
                expression = new PowerShellIndexExpressionSyntax(expression, openBracket, index, ExpectCharacter(']', SyntaxKind.CloseBracketToken));
                continue;
            }

            // `$x++` and `$x ++` are both postfix increments; the whitespace becomes the operator's leading trivia.
            if (IsAtPostfixIncrement())
            {
                AccumulateInlineTrivia();
                var operatorToken = ReadOperatorToken(SyntaxKind.OperatorToken, length: 2);
                expression = new PowerShellUnaryExpressionSyntax(SyntaxKind.PowerShellPostfixUnaryExpression, prefixOperatorToken: null, expression, operatorToken);
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

    private ShellExpressionSyntax ContinueMemberAccess(ShellExpressionSyntax target, ScannedToken operatorToken)
    {
        var memberNameToken = ReadMemberNameToken();
        var access = new PowerShellMemberAccessExpressionSyntax(target, operatorToken, memberNameToken);

        if (_lexer.Current != '(')
            return access;

        var openParen = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        var arguments = new List<ShellExpressionSyntax>();
        var separators = new List<ScannedToken>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
                break;

            arguments.Add(ParseTernaryExpression());
            AccumulateStatementTrivia();
            if (_lexer.Current == ',')
            {
                separators.Add(ReadOperatorToken(SyntaxKind.CommaToken, length: 1));
                continue;
            }

            // Nothing was consumed, so the argument list is malformed. Stopping here leaves the text to the caller;
            // skipping the character would drop it from the tree entirely.
            break;
        }

        return new PowerShellInvocationExpressionSyntax(access, openParen, ParserHelpers.Separated(arguments, separators), ExpectCharacter(')', SyntaxKind.CloseParenToken));
    }

    private ScannedToken ReadMemberNameToken()
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

            return ReadOperatorToken(SyntaxKind.GenericToken, scan - start);
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

            return MissingToken(SyntaxKind.GenericToken, start);
        }

        return ReadOperatorToken(SyntaxKind.GenericToken, scan - start);
    }

    // ---- primary expressions ----

    private ShellExpressionSyntax ParsePrimaryExpression()
    {
        AccumulateStatementTrivia();

        switch (_lexer.Current)
        {
            case '$' when _lexer.Peek(1) == '(':
                return ParseSubExpression(SyntaxKind.PowerShellSubExpression, openLength: 2);
            case '$':
                return ParseVariableExpression();
            case '@' when _lexer.Peek(1) == '(':
                return ParseSubExpression(SyntaxKind.PowerShellArrayExpression, openLength: 2);
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

        return new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellBareWord, ReadBareToken());
    }

    private PowerShellParenthesizedExpressionSyntax ParseParenthesizedExpression()
    {
        var openParen = ExpectCharacter('(', SyntaxKind.OpenParenToken);
        var statements = ParseStatementList(stopCharacter: ')');

        return new PowerShellParenthesizedExpressionSyntax(openParen, statements, ExpectCharacter(')', SyntaxKind.CloseParenToken));
    }

    private PowerShellSubExpressionSyntax ParseSubExpression(SyntaxKind kind, int openLength)
    {
        var openToken = ReadOperatorToken(kind == SyntaxKind.PowerShellArrayExpression ? SyntaxKind.AtParenToken : SyntaxKind.DollarOpenParenToken, openLength);
        var statements = ParseStatementList(stopCharacter: ')');

        return new PowerShellSubExpressionSyntax(kind, openToken, statements, ExpectCharacter(')', SyntaxKind.CloseParenToken));
    }

    private PowerShellHashLiteralSyntax ParseHashLiteral()
    {
        var openToken = ReadOperatorToken(SyntaxKind.AtBraceToken, length: 2);
        var entries = new List<PowerShellHashEntrySyntax>();

        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == '}')
                break;

            if (_lexer.Current == ';')
            {
                // A separator with no entry in front of it is an error in PowerShell too. Leaving it unread ends the
                // literal here and keeps the text, which consuming the token would drop.
                AddDiagnostic(new TextSpan(_lexer.Position, 1), "SHELL0002", "Unexpected ';'.");
                break;
            }

            var positionBefore = _lexer.Position;

            // A key may be any simple expression, as in `@{ $parameter.Name = $parameter.Value }`, and a value may be
            // an array or a whole pipeline, as in `@{ Names = $items | Sort-Object }`.
            var key = ParsePostfixExpression();
            var equalsToken = ExpectCharacter('=', SyntaxKind.EqualsToken);
            AccumulateStatementTrivia();
            var value = ParseClause(ParseArrayLiteralExpression);

            AccumulateInlineTrivia();
            var separator = _lexer.Current == ';' ? ReadOperatorToken(SyntaxKind.SemicolonToken, length: 1) : default;
            entries.Add(new PowerShellHashEntrySyntax(key, equalsToken, value, separator));

            if (_lexer.Position == positionBefore)
                break;
        }

        return new PowerShellHashLiteralSyntax(openToken, ParserHelpers.List(entries), ExpectCharacter('}', SyntaxKind.CloseBraceToken));
    }

    private PowerShellVariableExpressionSyntax ParseVariableExpression()
    {
        AccumulateInlineTrivia();
        var (trivia, fullStart) = TakeTrivia();
        var sigilStart = _lexer.Position;
        _lexer.Position++;
        var sigilToken = _lexer.CreateToken(SyntaxKind.DollarToken, sigilStart, trivia, fullStart);

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
        var nameToken = _lexer.CreateToken(SyntaxKind.VariableNameToken, nameStart, null, nameStart, rawName.Trim('{', '}'));

        return new PowerShellVariableExpressionSyntax(sigilToken, nameToken);
    }

    private PowerShellTypeLiteralSyntax ParseTypeLiteral()
    {
        var openBracket = ExpectCharacter('[', SyntaxKind.OpenBracketToken);
        var nameToken = ReadTypeNameToken(includeArgumentList: true, insideBrackets: true);

        return new PowerShellTypeLiteralSyntax(openBracket, nameToken, ExpectCharacter(']', SyntaxKind.CloseBracketToken));
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

        return new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellNumberLiteral, ReadOperatorToken(SyntaxKind.NumberToken, scan - start));
    }

    private PowerShellLiteralExpressionSyntax ParseVerbatimStringExpression()
    {
        var (trivia, fullStart) = TakeTrivia();
        var quoted = ParseVerbatimString(trivia, fullStart);
        var value = quoted.PartCount() == 0 ? string.Empty : (quoted.FirstPart() as ShellLiteralWordPartSyntax)?.GetSlot(0) is Meziantou.Framework.Language.InternalSyntax.SyntaxToken firstPart ? firstPart.ValueText : string.Empty;
        // The node's own text, without the trivia the token will carry instead.
        var text = quoted.ToString();
        var token = new ScannedToken(SyntaxKind.GenericToken, text, value, leadingTrivia: trivia, fullStart: fullStart);

        return new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellStringLiteral, token);
    }

    /// <summary>Reads a double-quoted string, keeping embedded variables and subexpressions as child nodes.</summary>
    private PowerShellExpandableStringSyntax ParseExpandableString()
    {
        AccumulateInlineTrivia();
        var (trivia, fullStart) = TakeTrivia();
        var quoteStart = _lexer.Position;
        _lexer.Position++;
        var openToken = _lexer.CreateToken(SyntaxKind.DoubleQuoteToken, quoteStart, trivia, fullStart);

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
                    parts.Add(new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.BareTextToken, escapeStart, null, escapeStart, "\"")));
                    continue;
                }

                terminated = true;
                break;
            }

            var positionBefore = _lexer.Position;
            if (_lexer.Current == '`')
            {
                parts.Add(ParseEscapeSequence(null, _lexer.Position));
            }
            else if (_lexer.Current == '$' && _lexer.Peek(1) == '(')
            {
                parts.Add(ParseSubExpression(SyntaxKind.PowerShellSubExpression, openLength: 2));
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

        ScannedToken closeToken;
        if (!terminated)
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

        return new PowerShellExpandableStringSyntax(SyntaxKind.PowerShellExpandableString, openToken, ParserHelpers.List(parts), closeToken);
    }

    private ShellLiteralWordPartSyntax ReadStringLiteralRun(int minimumLength)
    {
        var start = _lexer.Position;
        _lexer.Position += minimumLength;
        while (!_lexer.IsAtEnd && _lexer.Current is not '"' and not '`' and not '$')
        {
            _lexer.Position++;
        }

        return new ShellLiteralWordPartSyntax(_lexer.CreateToken(SyntaxKind.BareTextToken, start, null, start));
    }

    /// <summary>Reads a here-string, <c>@" ... "@</c> or <c>@' ... '@</c>, whose body is kept verbatim.</summary>
    private PowerShellExpandableStringSyntax ParseHereString()
    {
        AccumulateInlineTrivia();
        var (trivia, fullStart) = TakeTrivia();
        var quote = _lexer.Peek(1);
        var start = _lexer.Position;
        _lexer.Position += 2;
        var openToken = _lexer.CreateToken(SyntaxKind.HereStringStartToken, start, trivia, fullStart);

        var bodyStart = _lexer.Position;
        var closeStart = FindHereStringTerminator(bodyStart, quote, depth: 0);
        _lexer.Position = closeStart < 0 ? _lexer.Text.Length : closeStart;

        if (closeStart < 0)
        {
            AddDiagnostic(openToken.Span, "SHELL0022", "Unterminated here-string.");
            closeStart = _lexer.Position;
        }

        var bodyText = _lexer.Text[bodyStart..closeStart];
        var body = new ShellLiteralWordPartSyntax(new ScannedToken(SyntaxKind.BareTextToken, bodyText, StripHereStringDelimiterLineBreaks(bodyText), fullStart: bodyStart));

        ScannedToken closeToken;
        if (_lexer.IsAtEnd)
        {
            closeToken = MissingToken(SyntaxKind.HereStringEndToken, _lexer.Position);
        }
        else
        {
            _lexer.Position += 2;
            closeToken = _lexer.CreateToken(SyntaxKind.HereStringEndToken, closeStart, null, closeStart);
        }

        var kind = quote == '"' ? SyntaxKind.PowerShellHereString : SyntaxKind.PowerShellStringLiteral;

        return new PowerShellExpandableStringSyntax(kind, openToken, (bodyText.Length == 0 ? null : (GreenNode?)body), closeToken);
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

    private ScannedToken ConsumeRestAsToken()
    {
        var (trivia, fullStart) = TakeTrivia();
        var start = _lexer.Position;
        _lexer.Position = _lexer.Text.Length;
        var text = _lexer.Text[start..];

        return new ScannedToken(SyntaxKind.BadToken, text, text, leadingTrivia: trivia, fullStart: fullStart);
    }
}
