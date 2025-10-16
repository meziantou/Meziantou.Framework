namespace Meziantou.Framework.CodeDom;

public class IsInstanceOfTypeExpression : Expression
{
    public IsInstanceOfTypeExpression()
    {
    }

    public IsInstanceOfTypeExpression(Expression? expression, TypeReference? type)
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
