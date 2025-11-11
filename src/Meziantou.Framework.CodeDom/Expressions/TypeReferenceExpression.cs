namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a reference to a type in an expression context.</summary>
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
