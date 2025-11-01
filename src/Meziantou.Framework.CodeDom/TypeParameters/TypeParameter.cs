namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a generic type parameter declaration.</summary>
public class TypeParameter : CodeObject
{
    public TypeParameter()
    {
    }

    public TypeParameter(string? name)
        : this()
    {
        Name = name;
    }

    public string? Name { get; set; }
    public TypeParameterConstraintCollection Constraints { get; } = [];
}
