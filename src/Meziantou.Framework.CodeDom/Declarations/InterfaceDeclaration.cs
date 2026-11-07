namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an interface declaration.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="InterfaceDeclaration_Example" ref:dedent="true">
/// var iface = new InterfaceDeclaration("IRepository");
/// var typeParameter = new TypeParameter("T");
/// iface.Parameters.Add(typeParameter);
/// iface.Members.Add(new MethodDeclaration("Get") { ReturnType = typeParameter });
/// var code = iface.ToCsharpString();
///
/// Assert.StartsWith("interface IRepository&lt;T&gt;", code);
/// Assert.Contains("Get();", code);
/// </code>
/// </example>
public class InterfaceDeclaration : TypeDeclaration, IParametrableType, IInheritanceParameters, ITypeDeclarationContainer, IMemberContainer
{
    public InterfaceDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InterfaceDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The interface name.</param>
    public InterfaceDeclaration(string? name)
    {
        Name = name;
        Implements = new List<TypeReference>();
        Parameters = new CodeObjectCollection<TypeParameter>(this);
        Members = new CodeObjectCollection<MemberDeclaration>(this);
        Types = new CodeObjectCollection<TypeDeclaration>(this);
    }

    public TypeReference? BaseType { get; set; }
    public IList<TypeReference> Implements { get; }
    public CodeObjectCollection<TypeParameter> Parameters { get; }
    public CodeObjectCollection<MemberDeclaration> Members { get; }
    public CodeObjectCollection<TypeDeclaration> Types { get; }
}
