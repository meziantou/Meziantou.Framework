namespace Meziantou.Framework.CodeDom;

public class ThrowStatement : Statement
{
    public ThrowStatement()
    {
    }

    public ThrowStatement(Expression? expression)
    {
        Expression = expression;
    }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }
}
