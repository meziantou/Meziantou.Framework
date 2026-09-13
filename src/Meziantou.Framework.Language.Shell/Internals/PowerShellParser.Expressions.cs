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
        if (!IsAssignable(left))
        {
            AddDiagnostic(operatorToken.Span, "SHELL0027", "The left-hand side of an assignment must be a variable, a property, or an array element.");
        }

        AccumulateStatementTrivia();

        // The value of an assignment is a statement, so `$x = if ($a) { 1 }` and `$x = foreach (…) { }` assign what
        // the statement outputs.
        ShellSyntaxNode value;
        if (IsExpressionStart())
        {
            value = ParseAssignmentExpression();
        }
        else if (IsAtStatementKeyword())
        {
            value = ParseStatement();
        }
        else
        {
            value = ParseCommand();
        }

        return new PowerShellAssignmentExpressionSyntax(left, operatorToken, value);
    }

    /// <summary>
    /// Returns whether <paramref name="target"/> can be assigned to. PowerShell checks this while parsing, so
    /// <c>1 = 2</c> and <c>-$x = 1</c> are syntax errors, while <c>[int]$x, $y = 1, 2</c> is not.
    /// </summary>
    private static bool IsAssignable(ShellSyntaxNode? target) => target switch
    {
        PowerShellVariableExpressionSyntax or PowerShellMemberAccessExpressionSyntax or PowerShellIndexExpressionSyntax
            or PowerShellInvocationExpressionSyntax or PowerShellParenthesizedExpressionSyntax => true,
        PowerShellCastExpressionSyntax cast => IsAssignable(cast.GetSlot(1) as ShellSyntaxNode),

        // `,$x = 1` assigns to a one-element array of assignable targets.
        PowerShellUnaryExpressionSyntax unary when unary.GetSlot(0)?.ToString() == "," => IsAssignable(unary.GetSlot(1) as ShellSyntaxNode),
        PowerShellArrayLiteralSyntax array => AllAssignable(array.GetSlot(0)),
        _ => false,
    };

    private static bool AllAssignable(GreenNode? elements)
    {
        if (elements is null)
            return false;

        if (!elements.IsList)
            return IsAssignable(elements as ShellSyntaxNode);

        for (var index = 0; index < elements.SlotCount; index += 2)
        {
            if (!IsAssignable(elements.GetSlot(index) as ShellSyntaxNode))
                return false;
        }

        return true;
    }

    /// <summary>Returns whether a keyword that starts a statement other than a pipeline is at the current position.</summary>
    private bool IsAtStatementKeyword()
    {
        if (_lexer.Current == ':' && PowerShellLexer.IsNameStart(_lexer.Peek(1)) && IsLabelOnALoop())
            return true;

        return PeekKeyword() is "if" or "while" or "do" or "for" or "foreach" or "switch" or "try" or "trap" or "function" or "filter"
            or "workflow" or "class" or "enum" or "data" or "using" or "return" or "throw" or "exit" or "break" or "continue";
    }

    private int GetAssignmentOperatorLength()
    {
        var (a, b, c) = (_lexer.Current, _lexer.Peek(1), _lexer.Peek(2));

        return (a, b, c) switch
        {
            ('?', '?', '=') when _options.Dialect.HasFeature(ShellDialectFeatures.NullCoalescing) => 3,
            ('+', '=', _) or ('-', '=', _) or ('*', '=', _) or ('/', '=', _) or ('%', '=', _) => 2,
            // PowerShell has no `==` operator: `$a == 1` assigns the result of a command named `=`.
            ('=', _, _) => 1,
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

            // An attribute such as `[Parameter()]` may stand on its own line above what it applies to; a plain type
            // literal cannot, so `[int]` followed by a line break is a statement of its own.
            var isAttribute = type.GetSlot(1)?.ToString().Contains('(', StringComparison.Ordinal) == true;
            if (isAttribute)
            {
                AccumulateStatementTrivia();
            }
            else
            {
                AccumulateInlineTrivia();
            }

            if (!_lexer.IsAtEnd && IsCastOperandStart())
                return new PowerShellCastExpressionSyntax(type, ParseUnaryExpression());

            if (isAttribute && type.GetSlot(2) is { IsMissing: false })
            {
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0002", "An attribute has to be followed by the expression it applies to.");
            }

            return ParsePostfixOperators(type);
        }

        return ParsePostfixExpression();
    }

    /// <summary>Returns whether what follows a type literal is the operand of a cast, as in <c>[int]$x</c> or <c>[int] -1</c>.</summary>
    private bool IsCastOperandStart()
    {
        var current = _lexer.Current;
        if (current is '$' or '(' or '@' or '"' or '\'' or '[' or '!' or '{' || char.IsAsciiDigit(current))
            return true;

        // A binary operator such as `-and` or `-is` is not an operand: `$x -is [int] -and $y`.
        if (current is '-' or '+')
        {
            if (PeekWordOperator() is { } wordOperator)
                return current == '-' && Array.IndexOf(UnaryWordOperators, wordOperator) >= 0;

            var next = _lexer.Peek(1);

            return char.IsAsciiDigit(next) || next is '$' or '(' or '@' or '"' or '\'' or '[' || (next == '.' && char.IsAsciiDigit(_lexer.Peek(2)));
        }

        return false;
    }

    private ShellExpressionSyntax ParsePostfixExpression() => ParsePostfixOperators(ParsePrimaryExpression());

    /// <param name="expression">The expression the operators apply to.</param>
    /// <param name="argumentMode">
    /// Set for an expression inside a command argument, as in <c>Write-Host $a.b.c()</c>. There, a member name has to
    /// touch its <c>.</c> and <c>++</c> is plain text.
    /// </param>
    private ShellExpressionSyntax ParsePostfixOperators(ShellExpressionSyntax expression, bool argumentMode = false)
    {
        var nullConditional = _options.Dialect.HasFeature(ShellDialectFeatures.NullCoalescing);
        while (true)
        {
            // A `.` that touches the expression accesses a member, and in expression mode the name may follow after
            // whitespace, as in `$x. y`. Two dots are the range operator instead.
            if (_lexer.Current is '.' && _lexer.Peek(1) != '.' && (!argumentMode || !IsSpaceOrLineEnd(1)))
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
            if (!argumentMode && IsAtPostfixIncrement())
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

        // `$items.Where{ $_ }` calls the method with a script block and no parentheses at all.
        if (_lexer.Current == '{' && memberNameToken.Green is { IsMissing: false })
        {
            var blockStart = _lexer.Position;
            var scriptBlock = ParseScriptBlock();

            return new PowerShellInvocationExpressionSyntax(
                access,
                MissingToken(SyntaxKind.OpenParenToken, blockStart),
                ParserHelpers.Separated(new List<ShellExpressionSyntax> { scriptBlock }, separators: null),
                MissingToken(SyntaxKind.CloseParenToken, _lexer.Position));
        }

        if (_lexer.Current != '(')
            return access;

        var openParen = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        var arguments = new List<ShellExpressionSyntax>();
        var separators = new List<ScannedToken>();
        while (true)
        {
            AccumulateStatementTrivia();
            if (_lexer.IsAtEnd || _lexer.Current == ')')
            {
                if (separators.Count > 0)
                {
                    AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0023", "Expected an expression after ','.");
                }

                break;
            }

            // The commas belong to the argument list, so an argument cannot start with the unary `,`.
            if (_lexer.Current == ',')
            {
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0023", "Expected an expression.");
                break;
            }

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
        AccumulateStatementTrivia();
        var text = _lexer.Text;
        var start = _lexer.Position;
        var scan = start;

        // A member name may be computed, as in `$x.($name)`, `$x.$($name)`, or `$x.{ $name }`. The tree keeps the
        // whole group as the name.
        if (scan < text.Length && (text[scan] is '(' or '{' || (text[scan] is '$' or '@' && scan + 1 < text.Length && text[scan + 1] == '(')))
        {
            if (text[scan] is not ('(' or '{'))
            {
                scan++;
            }

            var end = SkipBalancedGroup(text, scan);
            if (end < 0)
            {
                // Taking the rest of the line keeps an unclosed group from swallowing the statements after it.
                AddDiagnostic(new TextSpan(start, 1), "SHELL0009", "Missing closing delimiter in member name.");
                end = scan;
                while (end < text.Length && SourceText.GetLineBreakLength(text, end) == 0)
                {
                    end++;
                }
            }

            return ReadOperatorToken(SyntaxKind.GenericToken, end - start);
        }

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
            var (trivia, fullStart) = TakeTrivia();

            return MissingToken(SyntaxKind.GenericToken, fullStart, trivia);
        }

        return ReadOperatorToken(SyntaxKind.GenericToken, scan - start);
    }

    /// <summary>
    /// Returns the index just past the <c>( )</c> or <c>{ }</c> group that starts at <paramref name="index"/>, skipping
    /// over quoted strings, or <c>-1</c> when the group is not closed.
    /// </summary>
    private static int SkipBalancedGroup(string text, int index)
    {
        var depth = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current is '\'' or '"')
            {
                var quote = current;
                index++;
                while (index < text.Length && text[index] != quote)
                {
                    index += text[index] == '`' && quote == '"' ? 2 : 1;
                }

                index++;
                continue;
            }

            if (current == '`')
            {
                index += 2;
                continue;
            }

            if (current is '(' or '{')
            {
                depth++;
            }
            else if (current is ')' or '}')
            {
                depth--;
                if (depth == 0)
                    return index + 1;
            }

            index++;
        }

        return -1;
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
            case '@' when _lexer.Peek(1) is '"' or '\'':
                return ParseMalformedHereStringHeader();
            case '@' when PowerShellLexer.IsVariableNameCharacter(_lexer.Peek(1)):
                // `@args` splats, which only an argument of a command can do.
                AddDiagnostic(new TextSpan(_lexer.Position, 1), "SHELL0002", "A splatted variable can only be used as a command argument.");
                return ParseVariableExpression();
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

        // Expression mode has no bare words: in `1 + abc` the operand is missing and `abc` is left for the statement
        // list, which reports it as unexpected. Not consuming it is what keeps `$x = 1 +` from swallowing the next
        // line's command.
        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0023", "Expected an expression.");
        var (trivia, fullStart) = TakeTrivia();

        return new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellBareWord, MissingToken(SyntaxKind.GenericToken, fullStart, trivia));
    }

    private PowerShellParenthesizedExpressionSyntax ParseParenthesizedExpression()
    {
        AccumulateStatementTrivia();
        var openParen = ReadOperatorToken(SyntaxKind.OpenParenToken, length: 1);
        var (statements, closeParen) = ParseParenthesizedPipeline();

        return new PowerShellParenthesizedExpressionSyntax(openParen, statements, closeParen);
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

            if (_lexer.Current is ';' or ')')
            {
                // A separator with no entry in front of it is an error in PowerShell too. Leaving it unread ends the
                // literal here and keeps the text, which consuming the token would drop.
                AddDiagnostic(new TextSpan(_lexer.Position, 1), "SHELL0002", $"Unexpected '{_lexer.Current}'.");
                break;
            }

            var positionBefore = _lexer.Position;
            var checkpoint = CreateCheckpoint();

            // A key may be a bare word or any simple expression, as in `@{ $parameter.Name = $parameter.Value }`, and
            // a value may be an array or a whole pipeline, as in `@{ Names = $items | Sort-Object }`.
            var key = PowerShellLexer.IsNameStart(_lexer.Current)
                ? new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellBareWord, ReadIdentifierToken())
                : ParsePostfixExpression();

            // The `=` has to be on the same line as the key.
            AccumulateInlineTrivia();
            if (_lexer.Current != '=')
            {
                // Without the `=` this is not an entry. Going back to where the key started and ending the literal
                // there leaves what follows, often the statements after a forgotten `}`, to be read as statements.
                Restore(checkpoint);
                AddDiagnostic(new TextSpan(positionBefore, 0), "SHELL0012", "Expected '=' after the key of a hash literal entry, or '}' to close the hash literal.");
                break;
            }

            var equalsToken = ReadOperatorToken(SyntaxKind.EqualsToken, length: 1);
            AccumulateStatementTrivia();
            ShellSyntaxNode value;
            if (_lexer.IsAtEnd || _lexer.Current is ';' or '}' or ')')
            {
                AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0023", "Expected a value after '='.");
                var (trivia, fullStart) = TakeTrivia();
                value = new PowerShellLiteralExpressionSyntax(SyntaxKind.PowerShellBareWord, MissingToken(SyntaxKind.GenericToken, fullStart, trivia));
            }
            else if (IsAtStatementKeyword())
            {
                // Like an assignment, an entry takes a whole statement: `@{ a = if ($x) { 1 } else { 2 } }`.
                value = ParseStatement();
            }
            else
            {
                value = ParseClause(ParseArrayLiteralExpression);
            }

            // Entries are separated by `;` or by line breaks, and a `;` may start the next line.
            AccumulateInlineTrivia();
            var separator = default(ScannedToken);
            if (_lexer.Current == ';' || IsAtSemicolonOnNextLines())
            {
                AccumulateStatementTrivia();
                separator = ReadSemicolonRun();
            }
            else if (!_lexer.IsAtEnd && _lexer.Current != '}' && SourceText.GetLineBreakLength(_lexer.Text, _lexer.Position) == 0)
            {
                AddUnexpectedTokenDiagnostic();
                entries.Add(new PowerShellHashEntrySyntax(key, equalsToken, value, separator));
                break;
            }

            entries.Add(new PowerShellHashEntrySyntax(key, equalsToken, value, separator));

            if (_lexer.Position == positionBefore)
                break;
        }

        return new PowerShellHashLiteralSyntax(openToken, ParserHelpers.List(entries), ExpectCharacter('}', SyntaxKind.CloseBraceToken));
    }

    /// <summary>Returns whether a <c>;</c> comes next once whitespace, line breaks, and comments are skipped.</summary>
    private bool IsAtSemicolonOnNextLines()
    {
        var text = _lexer.Text;
        var scan = _lexer.Position;
        while (scan < text.Length)
        {
            if (char.IsWhiteSpace(text[scan]))
            {
                scan++;
            }
            else if (text[scan] == '<' && scan + 1 < text.Length && text[scan + 1] == '#')
            {
                var end = text.IndexOf("#>", scan + 2, StringComparison.Ordinal);
                if (end < 0)
                    return false;

                scan = end + 2;
            }
            else if (text[scan] == '#')
            {
                while (scan < text.Length && SourceText.GetLineBreakLength(text, scan) == 0)
                {
                    scan++;
                }
            }
            else
            {
                return text[scan] == ';';
            }
        }

        return false;
    }

    /// <summary>Reads an identifier, which unlike a command word stops at <c>-</c>, <c>.</c>, and <c>:</c>.</summary>
    private ScannedToken ReadIdentifierToken()
    {
        var length = 0;
        while (PowerShellLexer.IsNameCharacter(_lexer.Peek(length)))
        {
            length++;
        }

        return ReadOperatorToken(SyntaxKind.GenericToken, length);
    }

    /// <summary>
    /// Reads a run of <c>;</c> as one separator, since an empty entry between two of them is allowed. The slot holds a
    /// single token, so whitespace between the semicolons, as in <c>; ;</c>, becomes part of its text.
    /// </summary>
    private ScannedToken ReadSemicolonRun()
    {
        var length = 1;
        var scan = 1;
        while (true)
        {
            while (_lexer.Peek(scan) is ' ' or '\t' or '\r' or '\n')
            {
                scan++;
            }

            if (_lexer.Peek(scan) != ';')
                break;

            scan++;
            length = scan;
        }

        return ReadOperatorToken(SyntaxKind.SemicolonToken, length);
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
                if (_lexer.Current == '`' && _lexer.Peek(1) != '\0')
                {
                    // A backtick escapes the next character, which is how `${a`}b}` names `a}b`.
                    _lexer.Position += 2;
                    continue;
                }

                if (_lexer.Current == '{')
                {
                    AddDiagnostic(new TextSpan(_lexer.Position, 1), "SHELL0002", "A '{' in a braced variable name must be escaped with a backtick.");
                }

                _lexer.Position++;
            }

            _lexer.Position = Math.Min(_lexer.Position, _lexer.Text.Length);

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
            // `::` is the static member operator, not part of a scope-qualified name: `$type::Name`. A `?` continues
            // the name, so `$a?.b` reads the member `b` of the variable `a?`, and `$global:?` keeps its scope prefix;
            // the null-conditional operators need the braced form, `${a}?.b`. `$global:^` is an error, unlike `$^`.
            while (!_lexer.IsAtEnd && (PowerShellLexer.IsVariableNameCharacter(_lexer.Current) || _lexer.Current == '?') && !(_lexer.Current == ':' && _lexer.Peek(1) == ':'))
            {
                _lexer.Position++;
            }

            if (_lexer.Peek(-1) == ':')
            {
                // `"$name: value"` names a drive and then nothing; `${name}:` is how to write the variable.
                AddDiagnostic(TextSpan.FromBounds(sigilStart, _lexer.Position), "SHELL0013", "Expected a variable name after the scope or drive qualifier.");
            }
        }
        else if (!_lexer.IsAtEnd && _lexer.Current is '?' or '^' or '$' or '_')
        {
            _lexer.Position++;
        }

        var rawName = _lexer.Text[nameStart..Math.Clamp(_lexer.Position, nameStart, _lexer.Text.Length)];
        if (rawName.Length == 0 && sigilToken.Text == "@")
        {
            // `@` splats a variable, so on its own, as in `@-Recurse`, it is not a token PowerShell knows.
            AddDiagnostic(sigilToken.Span, "SHELL0002", "Unexpected '@'.");
        }

        var nameToken = _lexer.CreateToken(SyntaxKind.VariableNameToken, nameStart, null, nameStart, rawName.Trim('{', '}'));

        return new PowerShellVariableExpressionSyntax(sigilToken, nameToken);
    }

    private PowerShellTypeLiteralSyntax ParseTypeLiteral()
    {
        var openBracket = ExpectCharacter('[', SyntaxKind.OpenBracketToken);
        var nameToken = ReadTypeNameToken(includeArgumentList: true, insideBrackets: true);
        if (openBracket.Green is { IsMissing: false } && (nameToken.Green is { IsMissing: true } || !PowerShellLexer.IsNameStart(nameToken.Text[0])))
        {
            // A type name is an identifier, so `[0]` names no type.
            AddDiagnostic(new TextSpan(nameToken.Start, 0), "SHELL0013", "Expected a type name.");
        }

        // A type name cannot continue on the next line, so neither can its `]`.
        AccumulateInlineTrivia();
        if (_lexer.Current == ']')
            return new PowerShellTypeLiteralSyntax(openBracket, nameToken, ReadOperatorToken(SyntaxKind.CloseBracketToken, length: 1));

        AddDiagnostic(new TextSpan(_lexer.Position, 0), "SHELL0012", "Expected ']'.");
        var (trivia, fullStart) = TakeTrivia();

        return new PowerShellTypeLiteralSyntax(openBracket, nameToken, MissingToken(SyntaxKind.CloseBracketToken, fullStart, trivia));
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

            // A dot only continues the number when it is not the range operator or a member access. A trailing dot
            // with nothing after it, as in `1.`, still belongs to the number.
            if (scan < text.Length && text[scan] == '.' && scan + 1 < text.Length && char.IsAsciiDigit(text[scan + 1]))
            {
                scan++;
                while (scan < text.Length && char.IsAsciiDigit(text[scan]))
                {
                    scan++;
                }
            }
            else if (scan < text.Length && text[scan] == '.' && (scan + 1 >= text.Length || char.IsWhiteSpace(text[scan + 1]) || text[scan + 1] is ';' or ')' or '}' or ']' or ',' or '|'))
            {
                scan++;
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
    /// Reports an <c>@"</c> or <c>@'</c> followed by more text on its line. PowerShell reads it as a here-string
    /// header, which has to end its line. The rest of the line is kept as one bad token, so that it does not become
    /// the body of a here-string that never ends and the next line still parses.
    /// </summary>
    private ShellRawExpressionSyntax ParseMalformedHereStringHeader()
    {
        AccumulateInlineTrivia();
        AddDiagnostic(new TextSpan(_lexer.Position, 2), "SHELL0022", "A here-string header must be the last thing on its line.");

        var text = _lexer.Text;
        var end = _lexer.Position;
        while (end < text.Length && SourceText.GetLineBreakLength(text, end) == 0)
        {
            end++;
        }

        return new ShellRawExpressionSyntax(ReadOperatorToken(SyntaxKind.BadToken, end - _lexer.Position));
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
