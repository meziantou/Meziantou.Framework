namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a base type constraint for a type parameter.</summary>
public class BaseTypeParameterConstraint : TypeParameterConstraint
{
    public BaseTypeParameterConstraint()
    {
    }

    public BaseTypeParameterConstraint(TypeReference? type)
    {
        Type = type;
    }

    public TypeReference? Type { get; set; }
}
