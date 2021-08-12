namespace Meziantou.Framework.CodeDom;

public class RecordDeclaration : ClassOrStructDeclaration, IInheritanceParameters
{
    public RecordDeclaration()
        : this(name: null)
    {
    }

    public RecordDeclaration(string? name)
    {
        Name = name;
    }

    public TypeReference? BaseType { get; set; }
}
