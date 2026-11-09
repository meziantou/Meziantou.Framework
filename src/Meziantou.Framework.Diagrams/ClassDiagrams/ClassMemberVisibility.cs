namespace Meziantou.Framework.Diagrams;

/// <summary>The visibility of a member of a class, shown before its name.</summary>
public enum ClassMemberVisibility
{
    /// <summary>No visibility is shown.</summary>
    None,

    /// <summary>Public: <c>+</c>.</summary>
    Public,

    /// <summary>Private: <c>-</c>.</summary>
    Private,

    /// <summary>Protected: <c>#</c>.</summary>
    Protected,

    /// <summary>Package or internal: <c>~</c>.</summary>
    Package,
}
