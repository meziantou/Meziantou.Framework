namespace Meziantou.Framework.CodeDom;

public class TypeParameter : CodeObject
{
    public TypeParameter()
    {
        Constraints = new TypeParameterConstraintCollection();
    }

    public TypeParameter(string? name)
        : this()
    {
        Name = name;
    }

    public string? Name { get; set; }
    public TypeParameterConstraintCollection Constraints { get; }
}
