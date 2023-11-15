namespace Meziantou.Framework.CodeDom;

public class TypeParameterConstraintCollection : CodeObjectCollection<TypeParameterConstraint>
{
    public static implicit operator TypeParameterConstraintCollection(TypeParameterConstraint codeConstraint) => [codeConstraint];
}
