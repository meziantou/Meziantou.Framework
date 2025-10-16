namespace Meziantou.Framework.CodeDom;

public class ReturnStatement : Statement
{
    public ReturnStatement()
    {
    }

    public ReturnStatement(Expression? expression)
    {
        Expression = expression;
    }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }
}
