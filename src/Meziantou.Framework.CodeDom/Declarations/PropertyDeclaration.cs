namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a property declaration.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="PropertyDeclaration_Example" ref:dedent="true">
/// var prop = new PropertyDeclaration("Name", typeof(string));
/// prop.Modifiers = Modifiers.Public;
/// prop.Getter = new ReturnStatement(new MemberReferenceExpression(new ThisExpression(), "_name"));
/// prop.Setter = new AssignStatement(new MemberReferenceExpression(new ThisExpression(), "_name"), new ValueArgumentExpression());
///
/// Assert.Equal("""
///     public string Name
///     {
///         get
///         {
///             return this._name;
///         }
///         set
///         {
///             this._name = value;
///         }
///     }
///
///     """, prop.ToCsharpString());
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
