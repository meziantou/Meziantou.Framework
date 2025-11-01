namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a record declaration.</summary>
/// <example>
/// <code>
/// var record = new RecordDeclaration("Person");
/// record.Members.Add(new PropertyDeclaration("Name", typeof(string)));
/// record.Members.Add(new PropertyDeclaration("Age", typeof(int)));
/// </code>
/// </example>
public class RecordDeclaration : ClassOrStructDeclaration, IInheritanceParameters
{
    public RecordDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RecordDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The record name.</param>
    public RecordDeclaration(string? name)
    {
        Name = name;
    }

    public TypeReference? BaseType { get; set; }
}
