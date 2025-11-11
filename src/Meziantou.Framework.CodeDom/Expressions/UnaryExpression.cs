namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a unary operation expression (negation, logical not, etc.).</summary>
public class UnaryExpression : Expression
{
    public UnaryExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnaryExpression"/> class with the specified operator and operand.</summary>
    /// <param name="op">The unary operator.</param>
    /// <param name="expression">The operand expression.</param>
    public UnaryExpression(UnaryOperator op, Expression? expression)
    {
        Operator = op;
        Expression = expression;
    }

    public UnaryOperator Operator { get; set; }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }

    /// <summary>Creates a logical NOT expression.</summary>
    /// <param name="expression">The expression to negate.</param>
    public static UnaryExpression Not(Expression expression) => new(UnaryOperator.Not, expression);

    /// <summary>Creates a numeric negation expression.</summary>
    /// <param name="expression">The expression to negate.</param>
    public static UnaryExpression Minus(Expression expression) => new(UnaryOperator.Minus, expression);
}
