namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a member of an enumeration.</summary>
public class EnumerationMember : MemberDeclaration
{
    public EnumerationMember()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EnumerationMember"/> class with the specified name.</summary>
    /// <param name="name">The member name.</param>
    public EnumerationMember(string? name)
    {
        Name = name;
    }

    public EnumerationMember(string? name, Expression value)
    {
        Name = name;
        Value = value;
    }

    public Expression? Value
    {
        get;
        set => SetParent(ref field, value);
    }
}
