namespace Meziantou.Framework.Win32;

public sealed class TokenEntry
{
    public TokenEntry(SecurityIdentifier sid!!)
    {
        Sid = sid;
    }

    public SecurityIdentifier Sid { get; }
}
