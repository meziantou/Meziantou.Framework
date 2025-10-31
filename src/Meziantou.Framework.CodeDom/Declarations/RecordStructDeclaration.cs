namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a record struct declaration.</summary>
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
