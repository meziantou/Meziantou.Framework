namespace Meziantou.Framework.Win32;

public sealed class TokenPrivilegeEntry
{
    internal TokenPrivilegeEntry(string name, PrivilegeAttribute attributes)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Attributes = attributes;
    }

    public string Name { get; }
    public PrivilegeAttribute Attributes { get; }

    public override string ToString() => Name + ": " + Attributes;
}
