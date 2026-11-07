namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a binary operation expression (addition, comparison, logical operations, etc.).</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="BinaryExpression_Example" ref:dedent="true">
/// var addition = new BinaryExpression(BinaryOperator.Add, 10, 20);
/// var comparison = new BinaryExpression(BinaryOperator.GreaterThan, new VariableReferenceExpression("x"), 0);
/// var logicalAnd = new BinaryExpression(
///     BinaryOperator.And,
///     new BinaryExpression(BinaryOperator.GreaterThan, new VariableReferenceExpression("x"), 0),
///     new BinaryExpression(BinaryOperator.LessThan, new VariableReferenceExpression("x"), 100));
///
/// Assert.Equal("(10 + 20)", addition.ToCsharpString());
/// Assert.Equal("(x &gt; 0)", comparison.ToCsharpString());
/// Assert.Equal("((x &gt; 0) &amp;&amp; (x &lt; 100))", logicalAnd.ToCsharpString());
/// </code>
/// </example>
public class BinaryExpression : Expression
{
    public BinaryExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BinaryExpression"/> class with the specified operator and operands.</summary>
    /// <param name="op">The binary operator.</param>
    /// <param name="leftExpression">The left operand.</param>
    /// <param name="rightExpression">The right operand.</param>
    public BinaryExpression(BinaryOperator op, Expression? leftExpression, Expression? rightExpression)
    {
        Operator = op;
        LeftExpression = leftExpression;
        RightExpression = rightExpression;
    }

    public BinaryOperator Operator { get; set; }

    public Expression? LeftExpression
    {
        get;
        set => SetParent(ref field, value);
    }

    public Expression? RightExpression
    {
        get;
        set => SetParent(ref field, value);
    }
}
