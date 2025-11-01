namespace Meziantou.Framework.Win32;

/// <summary>Represents a privilege entry in an access token.</summary>
/// <remarks>
/// This class contains information about a privilege held by an access token,
/// including the privilege name and its current state (enabled, disabled, etc.).
/// </remarks>
public sealed class TokenPrivilegeEntry
{
    internal TokenPrivilegeEntry(string name, PrivilegeAttribute attributes)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Attributes = attributes;
    }

    /// <summary>Gets the name of the privilege (e.g., "SeDebugPrivilege").</summary>
    public string Name { get; }

    /// <summary>Gets the attributes of the privilege.</summary>
    /// <value>A combination of <see cref="PrivilegeAttribute"/> flags indicating the privilege's state.</value>
    public PrivilegeAttribute Attributes { get; }

    /// <summary>Returns a string representation of the privilege entry.</summary>
    public override string ToString() => Name + ": " + Attributes;
}
