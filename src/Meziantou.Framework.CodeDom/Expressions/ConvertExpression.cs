namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a type conversion expression (as operator).</summary>
public class ConvertExpression : Expression
{
    public ConvertExpression()
    {
    }

    public ConvertExpression(Expression? expression, TypeReference? type)
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
