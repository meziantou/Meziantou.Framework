namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a single line of a CODEOWNERS file, associating a file pattern with its owners.
/// <example>
/// <code>
/// // Parse a CODEOWNERS file
/// var entries = CodeOwnersParser.Parse("*.js @user1 @user2");
/// var entry = entries[0];
/// // entry.Pattern: "*.js"
/// // entry.Owners: [ @user1, @user2 ]
/// </code>
/// </example>
/// </summary>
/// <remarks>Entries are returned in the order they appear in the file. CODEOWNERS resolution is last-match-wins, so the last entry whose <see cref="Pattern"/> matches a path owns it.</remarks>
public sealed class CodeOwnersEntry : IEquatable<CodeOwnersEntry>
{
    internal CodeOwnersEntry(string pattern, IReadOnlyList<CodeOwnersOwner> owners, CodeOwnersSection? section)
    {
        Pattern = pattern;
        Owners = owners;
        Section = section;
    }

    /// <summary>Gets the file pattern (e.g., "*.js", "/docs/*", or "*") that this entry applies to.</summary>
    public string Pattern { get; }

    /// <summary>Gets the owners of the pattern. Empty when the entry explicitly leaves the pattern unowned.</summary>
    public IReadOnlyList<CodeOwnersOwner> Owners { get; }

    /// <summary>Gets the section this entry belongs to, or null if not part of a section.</summary>
    public CodeOwnersSection? Section { get; }

    /// <summary>Gets a value indicating whether this entry belongs to an optional section.</summary>
    public bool IsOptional => Section?.IsOptional ?? false;

    /// <summary>Returns the entry as written in a CODEOWNERS file.</summary>
    public override string ToString()
    {
        var result = Owners.Count is 0 ? Pattern : Pattern + " " + string.Join(' ', Owners);
        if (IsOptional)
        {
            result += " (optional)";
        }

        return result;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as CodeOwnersEntry);

    public bool Equals([NotNullWhen(true)] CodeOwnersEntry? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
               Section == other.Section &&
               Owners.SequenceEqual(other.Owners);
    }

    public override int GetHashCode() => HashCode.Combine(Pattern, Owners.Count, Section);

    public static bool operator ==(CodeOwnersEntry? left, CodeOwnersEntry? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(CodeOwnersEntry? left, CodeOwnersEntry? right) => !(left == right);
}
