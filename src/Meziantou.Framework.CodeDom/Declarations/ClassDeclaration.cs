namespace Meziantou.Framework.CodeDom;

public class ClassDeclaration : ClassOrStructDeclaration, IInheritanceParameters
{
    public ClassDeclaration()
        : this(name: null)
    {
    }

    public ClassDeclaration(string? name)
    {
        Name = name;
    }

    public TypeReference? BaseType { get; set; }
}
