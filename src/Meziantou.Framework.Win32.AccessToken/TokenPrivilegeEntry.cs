namespace Meziantou.Framework.Win32;

public sealed class TokenPrivilegeEntry
{
    public TokenPrivilegeEntry(string name!!, PrivilegeAttribute attributes)
    {
        Name = name;
        Attributes = attributes;
    }

    public string Name { get; }
    public PrivilegeAttribute Attributes { get; }
}
