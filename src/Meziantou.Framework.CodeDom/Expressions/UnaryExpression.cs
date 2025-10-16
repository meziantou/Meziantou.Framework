namespace Meziantou.Framework.CodeDom;

public class UnaryExpression : Expression
{
    public UnaryExpression()
    {
    }

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

    public static UnaryExpression Not(Expression expression) => new(UnaryOperator.Not, expression);
    public static UnaryExpression Minus(Expression expression) => new(UnaryOperator.Minus, expression);
}
