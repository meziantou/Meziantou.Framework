namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a throw statement to raise an exception.</summary>
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
