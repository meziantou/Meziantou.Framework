namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a yield return statement in an iterator.</summary>
public class YieldReturnStatement : Statement
{
    public YieldReturnStatement()
    {
    }

    public YieldReturnStatement(Expression? expression)
    {
        Expression = expression;
    }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }
}
