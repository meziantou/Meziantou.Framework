namespace Meziantou.Framework.CodeDom;

public class CastExpression : Expression
{
    private Expression? _expression;

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
        get => _expression;
        set => SetParent(ref _expression, value);
    }

    public TypeReference? Type { get; set; }
}
