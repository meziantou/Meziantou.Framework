namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a return statement with an optional value.</summary>
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
