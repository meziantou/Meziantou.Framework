namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a default value expression (default(T) or default).</summary>
public class DefaultValueExpression : Expression
{
    public DefaultValueExpression()
    {
    }

    public DefaultValueExpression(TypeReference? type)
    {
        Type = type;
    }

    public TypeReference? Type { get; set; }
}
