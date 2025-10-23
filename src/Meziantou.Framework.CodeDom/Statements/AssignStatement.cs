namespace Meziantou.Framework.CodeDom;

public class AssignStatement : Statement
{
    public AssignStatement()
    {
    }

    public AssignStatement(Expression? leftExpression, Expression? rightExpression)
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
