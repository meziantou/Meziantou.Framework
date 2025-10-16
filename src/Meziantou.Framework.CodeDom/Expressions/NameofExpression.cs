namespace Meziantou.Framework.CodeDom;

public class NameofExpression : Expression
{
    public NameofExpression()
    {
    }

    public NameofExpression(Expression? expression)
    {
        Expression = expression;
    }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }
}
