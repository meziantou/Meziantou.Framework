namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A method or a field used by an instruction. The member may be defined outside the architecture.</summary>
public sealed class MemberReference
{
    internal MemberReference(TypeReference declaringType, string name, MemberReferenceKind kind)
    {
        DeclaringType = declaringType;
        Name = name;
        Kind = kind;
    }

    /// <summary>Gets the type that declares the member, with its generic arguments.</summary>
    public TypeReference DeclaringType { get; }

    /// <summary>Gets the name of the member as written in metadata: <c>.ctor</c> for a constructor, <c>get_Now</c> for the getter of the <c>Now</c> property.</summary>
    public string Name { get; }

    /// <summary>Gets the kind of the member.</summary>
    public MemberReferenceKind Kind { get; }

    /// <summary>Returns the member as <c>DeclaringType.Name</c>.</summary>
    /// <returns>The member as <c>DeclaringType.Name</c>.</returns>
    public override string ToString() => DeclaringType + "." + Name;
}
