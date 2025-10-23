namespace Meziantou.Framework.CodeDom;

public class ExpressionStatement : Statement
{
    public ExpressionStatement()
    {
    }

    public ExpressionStatement(Expression? expression)
    {
        Expression = expression;
    }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }
}
