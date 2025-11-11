namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a collection of type parameter constraints.</summary>
public class TypeParameterConstraintCollection : CodeObjectCollection<TypeParameterConstraint>
{
    public static implicit operator TypeParameterConstraintCollection(TypeParameterConstraint codeConstraint) => [codeConstraint];
}
