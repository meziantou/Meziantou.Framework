namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>Specifies the declared accessibility of a type or a member.</summary>
public enum ElementAccessibility
{
    /// <summary><c>private</c>.</summary>
    Private,

    /// <summary><c>private protected</c>.</summary>
    PrivateProtected,

    /// <summary><c>internal</c>.</summary>
    Internal,

    /// <summary><c>protected</c>.</summary>
    Protected,

    /// <summary><c>protected internal</c>.</summary>
    ProtectedInternal,

    /// <summary><c>public</c>.</summary>
    Public,
}
