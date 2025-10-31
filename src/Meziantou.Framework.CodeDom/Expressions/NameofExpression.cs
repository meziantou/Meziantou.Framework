namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a nameof expression.</summary>
public class NameofExpression : Expression
{
    public NameofExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NameofExpression"/> class with the specified expression.</summary>
    /// <param name="expression">The expression to get the name of.</param>
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
