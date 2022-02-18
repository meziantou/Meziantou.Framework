using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersSection : IEquatable<CodeOwnersSection>
{
    public CodeOwnersSection(string name, bool isOptional)
    {
        Name = name;
        IsOptional = isOptional;
    }

    public string Name { get; }
    public bool IsOptional { get; }

    public override bool Equals(object? obj)
    {
        return obj is CodeOwnersSection section && Equals(section);
    }

    public bool Equals(CodeOwnersSection other)
    {
        return Name == other.Name &&
               IsOptional == other.IsOptional;
    }

    public override int GetHashCode()
    {
        var hashCode = 1707150943;
        hashCode = hashCode * -1521134295 + IsOptional.GetHashCode();
        hashCode = hashCode * -1521134295 + StringComparer.Ordinal.GetHashCode(Name);
        return hashCode;
    }

    public static bool operator ==(CodeOwnersSection left, CodeOwnersSection right) => left.Equals(right);
    public static bool operator !=(CodeOwnersSection left, CodeOwnersSection right) => !(left == right);
}
