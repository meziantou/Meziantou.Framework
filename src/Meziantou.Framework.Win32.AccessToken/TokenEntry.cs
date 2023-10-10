namespace Meziantou.Framework.Win32;

public sealed class TokenEntry
{
    internal TokenEntry(SecurityIdentifier sid)
    {
        Sid = sid ?? throw new ArgumentNullException(nameof(sid));
    }

    public SecurityIdentifier Sid { get; }
}
