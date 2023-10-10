namespace Meziantou.Framework.Win32;

public sealed class TokenGroupEntry
{
    internal TokenGroupEntry(SecurityIdentifier sid, GroupSidAttributes attributes)
    {
        Sid = sid ?? throw new ArgumentNullException(nameof(sid));
        Attributes = attributes;
    }

    public SecurityIdentifier Sid { get; }
    public GroupSidAttributes Attributes { get; }
}
