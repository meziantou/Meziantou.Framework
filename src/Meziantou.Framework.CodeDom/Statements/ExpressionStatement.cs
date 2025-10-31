namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a statement that consists of a single expression.</summary>
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
