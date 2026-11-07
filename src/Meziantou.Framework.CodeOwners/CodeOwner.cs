using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a single owner of a <see cref="CodeOwnersEntry"/>.
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs" ref:region="CodeOwner_Example" ref:dedent="true">
/// var file = CodeOwnersFile.Parse("*.js @user1 docs@example.com", CodeOwnersDialect.GitLab);
/// var owners = file.Entries[0].Owners;
///
/// Assert.Equal(CodeOwnerType.Username, owners[0].Type);
/// Assert.Equal("user1", owners[0].Name);
///
/// Assert.Equal(CodeOwnerType.EmailAddress, owners[1].Type);
/// Assert.Equal("docs@example.com", owners[1].Name);
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

    /// <summary>Gets the owner identifier: a username without its leading <c>@</c>, an email address, or a role name without its leading <c>@@</c>.</summary>
    /// <remarks>The value is the one written in the file, so a role keeps the singular or plural form it was written with.</remarks>
    public string Name { get; }

    internal static CodeOwner Username(string name) => new(CodeOwnerType.Username, name);

    internal static CodeOwner EmailAddress(string address) => new(CodeOwnerType.EmailAddress, address);

    internal static CodeOwner Role(string name) => new(CodeOwnerType.Role, name);

    /// <summary>Returns the owner as written in a CODEOWNERS file.</summary>
    public override string ToString() => Type switch
    {
        CodeOwnerType.Username => "@" + Name,
        CodeOwnerType.Role => "@@" + Name,
        _ => Name,
    };

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
