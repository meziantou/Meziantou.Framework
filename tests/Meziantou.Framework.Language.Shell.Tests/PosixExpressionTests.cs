namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// The two POSIX expression grammars: arithmetic, behind <c>$(( ))</c> and <c>(( ))</c>, and the conditional behind
/// <c>[[ ]]</c>. Precedence is asserted through the shape of the tree rather than through the text.
/// </summary>
public sealed class PosixExpressionTests
{
    /// <summary>Renders an expression so precedence is visible: <c>1 + 2 * 3</c> becomes <c>(1 + (2 * 3))</c>.</summary>
    private static string Shape(ShellSyntaxNode node) => node switch
    {
        ShellBinaryExpressionSyntax binary => $"({Shape(binary.Left)} {binary.OperatorText} {Shape(binary.Right)})",
        ShellUnaryExpressionSyntax unary when unary.IsPostfix => $"({Shape(unary.Operand)}{unary.OperatorText})",
        ShellUnaryExpressionSyntax unary => $"({unary.OperatorText} {Shape(unary.Operand)})",
        ShellGroupedExpressionSyntax grouped => $"[{Shape(grouped.Expression)}]",
        ShellConditionalExpressionSyntax conditional => $"({Shape(conditional.Condition)} ? {Shape(conditional.WhenTrue)} : {Shape(conditional.WhenFalse)})",
        ShellOperandExpressionSyntax operand => operand.Word.ToFullString().Trim(),
        ShellRawExpressionSyntax raw => $"RAW<{raw.Text.Trim()}>",
        _ => node.Kind.ToString(),
    };

    private static ShellExpressionSyntax Arithmetic(string text)
    {
        var source = $"echo $(({text}))";
        var tree = ShellSyntaxAssert.TextIsFaithful(source, ShellDialect.Bash);

        return Assert.Single(tree.Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>()).Expression;
    }

    private static ShellExpressionSyntax Conditional(string text)
    {
        var source = $"[[ {text} ]]";
        var tree = ShellSyntaxAssert.TextIsFaithful(source, ShellDialect.Bash);

        return Assert.Single(tree.Root.DescendantNodes().OfType<PosixDelimitedExpressionStatementSyntax>()).Expression;
    }

    [Theory]
    [InlineData("1 + 2", "(1 + 2)")]
    [InlineData("1 + 2 * 3", "(1 + (2 * 3))")]
    [InlineData("1 * 2 + 3", "((1 * 2) + 3)")]
    [InlineData("(1 + 2) * 3", "([(1 + 2)] * 3)")]
    [InlineData("1 - 2 - 3", "((1 - 2) - 3)")]
    [InlineData("a = b = 3", "(a = (b = 3))")]
    [InlineData("x < y && z > 0", "((x < y) && (z > 0))")]
    [InlineData("a || b && c", "(a || (b && c))")]
    [InlineData("1 | 2 ^ 3 & 4", "(1 | (2 ^ (3 & 4)))")]
    [InlineData("1 << 2 >> 3", "((1 << 2) >> 3)")]
    [InlineData("a == b != c", "((a == b) != c)")]
    [InlineData("a ? b : c", "(a ? b : c)")]
    [InlineData("a ? b : c ? d : e", "(a ? b : (c ? d : e))")]
    [InlineData("a += 2", "(a += 2)")]
    // `**` binds tighter than `*` and is right-associative, and unary minus binds tighter still: bash reads
    // `-2**2` as `(-2)**2`, which is 4.
    [InlineData("2 ** 3", "(2 ** 3)")]
    [InlineData("2**3**2", "(2 ** (3 ** 2))")]
    [InlineData("2**2*3", "((2 ** 2) * 3)")]
    [InlineData("1+2**2", "(1 + (2 ** 2))")]
    [InlineData("-2**2", "((- 2) ** 2)")]
    [InlineData("a *= b", "(a *= b)")]
    public void Arithmetic_PrecedenceAndAssociativityAreInTheTree(string text, string expected)
    {
        Assert.Equal(expected, Shape(Arithmetic(text)));
    }

    [Theory]
    [InlineData("i++", "(i++)")]
    [InlineData("i--", "(i--)")]
    [InlineData("++i", "(++ i)")]
    [InlineData("--i", "(-- i)")]
    [InlineData("-x", "(- x)")]
    [InlineData("!a", "(! a)")]
    [InlineData("~bits", "(~ bits)")]
    public void Arithmetic_UnaryOperatorsAreDistinguishedByPosition(string text, string expected)
    {
        Assert.Equal(expected, Shape(Arithmetic(text)));
    }

    [Fact]
    public void Arithmetic_OperandsKeepTheirWordStructure()
    {
        var expression = Assert.IsType<ShellBinaryExpressionSyntax>(Arithmetic("$x + ${y}"));

        Assert.Single(Assert.IsType<ShellOperandExpressionSyntax>(expression.Left).Word.Parts.OfType<ShellVariableReferenceSyntax>());
        Assert.Single(Assert.IsType<ShellOperandExpressionSyntax>(expression.Right).Word.Parts.OfType<ShellVariableReferenceSyntax>());
    }

    [Fact]
    public void ArithmeticCommand_UsesTheSameGrammar()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("(( i = (a + b) * 2 ))", ShellDialect.Bash);
        var statement = Assert.IsType<PosixDelimitedExpressionStatementSyntax>(tree.Root.Statements.Statements[0]);

        Assert.True(statement.IsArithmetic);
        Assert.Equal("(i = ([(a + b)] * 2))", Shape(statement.Expression));
    }

    [Theory]
    [InlineData("-f $file", "(-f $file)")]
    [InlineData("-z $x", "(-z $x)")]
    [InlineData("! -f x", "(! (-f x))")]
    [InlineData("$a == b", "($a == b)")]
    [InlineData("$a != $b", "($a != $b)")]
    [InlineData("$x =~ ^a.*z$", "($x =~ ^a.*z$)")]
    [InlineData("$n -eq 0", "($n -eq 0)")]
    [InlineData("$a -lt $b", "($a -lt $b)")]
    [InlineData("-f a && -d b", "((-f a) && (-d b))")]
    [InlineData("-f a || -d b", "((-f a) || (-d b))")]
    [InlineData("-f a && -d b || -x c", "(((-f a) && (-d b)) || (-x c))")]
    [InlineData("( -f a || -d b ) && -x c", "([((-f a) || (-d b))] && (-x c))")]
    // The right side of `=~` is a regular expression, so `(`, `|`, and the `)` that closes a group belong to it.
    [InlineData("$x =~ (a|b)", "($x =~ (a|b))")]
    [InlineData("$x =~ ^(a|b)c$", "($x =~ ^(a|b)c$)")]
    [InlineData("$x =~ a|b", "($x =~ a|b)")]
    [InlineData("$x =~ (a|b) && -f c", "(($x =~ (a|b)) && (-f c))")]
    [InlineData("( $x =~ (a|b) )", "[($x =~ (a|b))]")]
    // A `)` that closes nothing ends the pattern, so the enclosing group still gets its closing parenthesis.
    [InlineData("($x =~ a)", "[($x =~ a)]")]
    public void Conditional_BuildsTheExpectedShape(string text, string expected)
    {
        Assert.Equal(expected, Shape(Conditional(text)));
    }

    [Fact]
    public void Conditional_OperandsKeepQuotingAndExpansions()
    {
        var expression = Assert.IsType<ShellBinaryExpressionSyntax>(Conditional("$a == \"quoted value\""));
        var right = Assert.IsType<ShellOperandExpressionSyntax>(expression.Right);

        Assert.Equal("quoted value", right.Value);
        Assert.Single(right.Word.Parts.OfType<ShellQuotedStringSyntax>());
    }

    [Fact]
    public void Conditional_ClosingBracketsInsideQuotesDoNotEndIt()
    {
        var expression = Conditional("$x == \"a]]b\"");

        Assert.Equal("($x == \"a]]b\")", Shape(expression));
    }

    [Theory]
    // `**` is a bash and zsh extension; sh has no exponentiation, so there the text is not arithmetic.
    [InlineData("2 ** 3", false)]
    [InlineData("a ** b", false)]
    [InlineData("a * b", true)]
    [InlineData("a *= b", true)]
    public void Arithmetic_ExponentiationIsBashAndZshOnly(string text, bool expectedInSh)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful($"echo $(({text}))", ShellDialect.Sh);
        var expansion = Assert.Single(tree.Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>());

        Assert.Equal(expectedInSh, expansion.Expression is not ShellRawExpressionSyntax);
        Assert.IsNotType<ShellRawExpressionSyntax>(Arithmetic(text));
    }

    [Theory]
    // A line break is ordinary space in arithmetic; it may sit anywhere an operator or an operand may.
    [InlineData("1 +\n2", "(1 + 2)")]
    [InlineData("1\n+\n2", "(1 + 2)")]
    [InlineData("\n1 + 2\n", "(1 + 2)")]
    [InlineData("(1\n+ 2)\n* 3", "([(1 + 2)] * 3)")]
    [InlineData("a\n? b\n: c", "(a ? b : c)")]
    public void Arithmetic_LineBreaksAreJustSpace(string text, string expected)
    {
        Assert.Equal(expected, Shape(Arithmetic(text)));
    }

    [Theory]
    // In a conditional a line break separates the operands of `&&` and `||`, and may follow `[[`, `!`, and `(`.
    [InlineData("-f a\n&& -d b", "((-f a) && (-d b))")]
    [InlineData("-f a &&\n-d b", "((-f a) && (-d b))")]
    [InlineData("\n-f a\n", "(-f a)")]
    [InlineData("!\n-f a", "(! (-f a))")]
    [InlineData("(\n-f a\n)", "[(-f a)]")]
    public void Conditional_LineBreaksSeparateOperands(string text, string expected)
    {
        Assert.Equal(expected, Shape(Conditional(text)));
    }

    [Theory]
    // Text the grammar does not fit is kept verbatim rather than being forced into a shape it does not have.
    [InlineData("echo $((|))")]
    [InlineData("echo $((+))")]
    [InlineData("(( ))")]
    [InlineData("[[ ]]")]
    [InlineData("[[ && ]]")]
    // Bash rejects each of these too: `**=` is not an operator, `-a` and `-o` are not `[[ ]]` operators, a
    // regular expression may not leave a group open, and a line break may not split an operator from its operand.
    [InlineData("echo $((a **= b))")]
    [InlineData("[[ -f a -o -f b ]]")]
    [InlineData("[[ -f a -a -f b ]]")]
    [InlineData("[[ $x =~ (a ]]")]
    [InlineData("[[ $x =~ ) ]]")]
    [InlineData("[[ -d\n/tmp ]]")]
    [InlineData("[[ $x ==\ny ]]")]
    public void TextThatIsNotAnExpression_FallsBackToRawAndStillRoundTrips(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Bash);

        Assert.All(
            tree.Root.DescendantNodes().OfType<PosixDelimitedExpressionStatementSyntax>(),
            node => Assert.IsType<ShellRawExpressionSyntax>(node.Expression));
        Assert.All(
            tree.Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>(),
            node => Assert.IsType<ShellRawExpressionSyntax>(node.Expression));
    }

    [Fact]
    public void NestedArithmeticExpansionsAreParsedIndependently()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("echo $(( $((1 + 2)) * 3 ))", ShellDialect.Bash);

        Assert.HasCount(2, tree.Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>());
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void ExpressionsAreVisitedAndRewritable()
    {
        var tree = ShellSyntaxTree.ParseText("echo $((a + b))", ShellDialect.Bash);
        var operands = tree.Root.DescendantNodes().OfType<ShellOperandExpressionSyntax>().ToArray();

        Assert.HasCount(2, operands);
        Assert.Equal(["a", "b"], operands.Select(operand => operand.Word.Value?.Trim()));
    }

    [Fact]
    public void ShHasArithmeticExpansionBecausePosixDefinesIt()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("echo $((1 + 2))", ShellDialect.Sh);

        var expansion = Assert.Single(tree.Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>());
        Assert.Equal("1 + 2", expansion.ExpressionText);
    }

    [Fact]
    public void ShHasNoArithmeticCommandBecausePosixDefinesNone()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("((1 + 2))", ShellDialect.Sh);

        Assert.DoesNotContain(tree.Root.DescendantNodes(), node => node.Kind == ShellSyntaxKind.PosixArithmeticCommand);
        Assert.Contains(ShellSyntaxTree.ParseText("((1 + 2))", ShellDialect.Bash).Root.DescendantNodes(), node => node.Kind == ShellSyntaxKind.PosixArithmeticCommand);
    }
}
