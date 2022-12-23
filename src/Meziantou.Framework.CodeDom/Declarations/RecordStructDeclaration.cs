namespace Meziantou.Framework.CodeDom;

public class RecordStructDeclaration : ClassOrStructDeclaration
{
    public RecordStructDeclaration()
        : this(name: null)
    {
    }

    public RecordStructDeclaration(string? name)
    {
        Name = name;
    }
}
