namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an enumeration declaration.</summary>
/// <example>
/// <code>
/// var enumDecl = new EnumerationDeclaration("Status");
/// enumDecl.BaseType = typeof(int);
/// enumDecl.Members.Add(new EnumerationMember("Active", 1));
/// enumDecl.Members.Add(new EnumerationMember("Inactive", 2));
/// </code>
/// </example>
public class EnumerationDeclaration : TypeDeclaration
{
    public EnumerationDeclaration()
    {
        Members = new CodeObjectCollection<EnumerationMember>(this);
    }

    /// <summary>Initializes a new instance of the <see cref="EnumerationDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The enumeration name.</param>
    public EnumerationDeclaration(string? name)
    {
        Members = new CodeObjectCollection<EnumerationMember>(this);
        Name = name;
    }

    public TypeReference? BaseType { get; set; }
    public CodeObjectCollection<EnumerationMember> Members { get; }
}
