namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a binary operation expression (addition, comparison, logical operations, etc.).</summary>
/// <example>
/// <code>
/// var addition = new BinaryExpression(BinaryOperator.Add, 10, 20);
/// var comparison = new BinaryExpression(BinaryOperator.GreaterThan, new VariableReferenceExpression("x"), 0);
/// var logicalAnd = new BinaryExpression(BinaryOperator.And, condition1, condition2);
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
