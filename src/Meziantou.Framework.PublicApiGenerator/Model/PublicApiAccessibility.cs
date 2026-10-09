namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The declared accessibility of a symbol. Values are ordered from the least to the most accessible.</summary>
public enum PublicApiAccessibility
{
    Private,
    PrivateProtected,
    Internal,
    Protected,
    ProtectedInternal,
    Public,
}
