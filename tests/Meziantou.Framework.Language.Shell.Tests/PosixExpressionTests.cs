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
    // Text the grammar does not fit is kept verbatim rather than being forced into a shape it does not have.
    [InlineData("echo $((|))")]
    [InlineData("echo $((+))")]
    [InlineData("(( ))")]
    [InlineData("[[ ]]")]
    [InlineData("[[ && ]]")]
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
    public void ShIsUnaffectedBecauseItHasNoArithmeticExpansion()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("echo $((1 + 2))", ShellDialect.Sh);

        Assert.Empty(tree.Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>());
    }
}
