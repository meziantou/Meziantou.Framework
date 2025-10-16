namespace Meziantou.Framework.CodeDom;

public class BinaryExpression : Expression
{
    public BinaryExpression()
    {
    }

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
