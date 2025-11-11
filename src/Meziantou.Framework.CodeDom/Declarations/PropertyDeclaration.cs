namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a property declaration.</summary>
/// <example>
/// <code>
/// var prop = new PropertyDeclaration("Name", typeof(string));
/// prop.Modifiers = Modifiers.Public;
/// prop.Getter = new PropertyAccessorDeclaration();
/// prop.Setter = new PropertyAccessorDeclaration();
/// </code>
/// </example>
public class PropertyDeclaration : MemberDeclaration, IModifiers
{
    public PropertyDeclaration()
        : this(name: null, type: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PropertyDeclaration"/> class with the specified name and type.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="type">The property type.</param>
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
