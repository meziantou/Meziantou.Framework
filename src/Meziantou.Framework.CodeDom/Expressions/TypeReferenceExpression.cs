namespace Meziantou.Framework.CodeDom;

public class TypeReferenceExpression : Expression
{
    public TypeReferenceExpression()
    {
    }

    public TypeReferenceExpression(TypeReference? type)
    {
        Type = type;
    }

    public TypeReference? Type { get; set; }
}
