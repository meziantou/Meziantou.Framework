namespace Meziantou.Framework.CodeDom;

public abstract class EventHandlerStatement : Statement
{
    protected EventHandlerStatement()
    {
    }

    protected EventHandlerStatement(Expression? leftExpression, Expression? rightExpression)
    {
        LeftExpression = leftExpression;
        RightExpression = rightExpression;
    }

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
