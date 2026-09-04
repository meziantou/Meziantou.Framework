using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a single owner of a <see cref="CodeOwnersEntry"/>.
/// <example>
/// <code>
/// var entries = CodeOwnersParser.Parse("*.js @user1 docs@example.com");
/// // entries[0].Owners[0]: Type=Username, Name="user1"
/// // entries[0].Owners[1]: Type=EmailAddress, Name="docs@example.com"
/// </code>
/// </example>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersOwner : IEquatable<CodeOwnersOwner>
{
    private CodeOwnersOwner(CodeOwnersOwnerType type, string name)
    {
        Type = type;
        Name = name;
    }

    /// <summary>Gets how the owner is identified.</summary>
    public CodeOwnersOwnerType Type { get; }

    /// <summary>Gets the owner identifier: a username without its leading <c>@</c>, or an email address.</summary>
    public string Name { get; }

    internal static CodeOwnersOwner Username(string name) => new(CodeOwnersOwnerType.Username, name);

    internal static CodeOwnersOwner EmailAddress(string address) => new(CodeOwnersOwnerType.EmailAddress, address);

    /// <summary>Returns the owner as written in a CODEOWNERS file.</summary>
    public override string ToString() => Type is CodeOwnersOwnerType.Username ? "@" + Name : Name;

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is CodeOwnersOwner owner && Equals(owner);
    }

    public bool Equals(CodeOwnersOwner other)
    {
        return Type == other.Type &&
               string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Name);

    public static bool operator ==(CodeOwnersOwner left, CodeOwnersOwner right) => left.Equals(right);
    public static bool operator !=(CodeOwnersOwner left, CodeOwnersOwner right) => !(left == right);
}
