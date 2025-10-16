namespace Meziantou.Framework.CodeDom;

public class PropertyDeclaration : MemberDeclaration, IModifiers
{
    public PropertyDeclaration()
        : this(name: null, type: null)
    {
    }

    public PropertyDeclaration(string? name, TypeReference? type)
    {
        Name = name;
        Type = type;
    }

    public Modifiers Modifiers { get; set; }

    public TypeReference? Type { get; set; }

    public PropertyAccessorDeclaration? Getter
    {
        get;
        set => SetParent(ref field, value);
    }

    public PropertyAccessorDeclaration? Setter
    {
        get;
        set => SetParent(ref field, value);
    }

    public TypeReference? PrivateImplementationType { get; set; }
}
