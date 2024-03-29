namespace Meziantou.Framework.CodeDom;

public class InterfaceDeclaration : TypeDeclaration, IParametrableType, IInheritanceParameters, ITypeDeclarationContainer, IMemberContainer
{
    public InterfaceDeclaration()
        : this(name: null)
    {
    }

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
