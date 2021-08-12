namespace Meziantou.Framework.CodeDom;

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
