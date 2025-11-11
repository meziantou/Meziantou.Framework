namespace Meziantou.Framework.Win32;

/// <summary>Represents a group membership entry in an access token.</summary>
/// <remarks>
/// This class contains information about a security group that is associated with an access token,
/// including the group's SID and its attributes (enabled, mandatory, etc.).
/// </remarks>
public sealed class TokenGroupEntry
{
    internal TokenGroupEntry(SecurityIdentifier sid, GroupSidAttributes attributes)
    {
        Sid = sid ?? throw new ArgumentNullException(nameof(sid));
        Attributes = attributes;
    }

    /// <summary>Gets the security identifier (SID) of the group.</summary>
    public SecurityIdentifier Sid { get; }

    /// <summary>Gets the attributes of the group membership.</summary>
    /// <value>A combination of <see cref="GroupSidAttributes"/> flags indicating the group's attributes.</value>
    public GroupSidAttributes Attributes { get; }
}
