using System;

namespace Meziantou.Framework.Win32;

public sealed class TokenPrivilegeEntry
{
    public TokenPrivilegeEntry(string name, PrivilegeAttribute attributes)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Attributes = attributes;
    }

    public string Name { get; }
    public PrivilegeAttribute Attributes { get; }
}
