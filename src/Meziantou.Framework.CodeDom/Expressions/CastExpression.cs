namespace Meziantou.Framework.CodeDom;

public class CastExpression : Expression
{
    public CastExpression()
    {
    }

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
