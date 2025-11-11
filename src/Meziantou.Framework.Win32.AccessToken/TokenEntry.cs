namespace Meziantou.Framework.Win32;

/// <summary>Represents a security identifier (SID) entry in a token.</summary>
public sealed class TokenEntry
{
    internal TokenEntry(SecurityIdentifier sid)
    {
        Sid = sid ?? throw new ArgumentNullException(nameof(sid));
    }

    /// <summary>Gets the security identifier (SID) for this entry.</summary>
    public SecurityIdentifier Sid { get; }
}
