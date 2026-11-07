namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>Specifies the kind of a <see cref="MemberReference"/>.</summary>
public enum MemberReferenceKind
{
    /// <summary>A method, a constructor, or a property or event accessor.</summary>
    Method,

    /// <summary>A field.</summary>
    Field,
}
