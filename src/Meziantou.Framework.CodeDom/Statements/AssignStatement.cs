namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an assignment statement (=).</summary>
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
