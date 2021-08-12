namespace Meziantou.Framework.CodeDom;

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
