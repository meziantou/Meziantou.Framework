namespace Meziantou.Framework.CodeDom;

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
