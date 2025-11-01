namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a struct declaration.</summary>
/// <example>
/// <code>
/// var str = new StructDeclaration("Point");
/// str.Members.Add(new FieldDeclaration("X", typeof(int)));
/// str.Members.Add(new FieldDeclaration("Y", typeof(int)));
/// </code>
/// </example>
public class StructDeclaration : ClassOrStructDeclaration
{
    public StructDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StructDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The struct name.</param>
    public StructDeclaration(string? name)
    {
        Name = name;
    }
}
