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
public readonly struct CodeOwner : IEquatable<CodeOwner>
{
    private CodeOwner(CodeOwnerType type, string name)
    {
        Type = type;
        Name = name;
    }

    /// <summary>Gets how the owner is identified.</summary>
    public CodeOwnerType Type { get; }

    /// <summary>Gets the owner identifier: a username without its leading <c>@</c>, or an email address.</summary>
    public string Name { get; }

    internal static CodeOwner Username(string name) => new(CodeOwnerType.Username, name);

    internal static CodeOwner EmailAddress(string address) => new(CodeOwnerType.EmailAddress, address);

    /// <summary>Returns the owner as written in a CODEOWNERS file.</summary>
    public override string ToString() => Type is CodeOwnerType.Username ? "@" + Name : Name;

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is CodeOwner owner && Equals(owner);
    }

    public bool Equals(CodeOwner other)
    {
        return Type == other.Type &&
               string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    public override int GetHashCode() => HashCode.Combine(Type, Name);

    public static bool operator ==(CodeOwner left, CodeOwner right) => left.Equals(right);
    public static bool operator !=(CodeOwner left, CodeOwner right) => !(left == right);
}
