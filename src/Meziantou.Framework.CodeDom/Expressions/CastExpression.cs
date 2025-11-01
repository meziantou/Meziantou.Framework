namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an explicit cast expression.</summary>
public class CastExpression : Expression
{
    public CastExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CastExpression"/> class with the specified expression and target type.</summary>
    /// <param name="expression">The expression to cast.</param>
    /// <param name="type">The target type.</param>
    public CastExpression(Expression? expression, TypeReference? type)
    {
        Expression = expression;
        Type = type;
    }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }

    public TypeReference? Type { get; set; }
}
