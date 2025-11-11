using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a single code owner entry in a CODEOWNERS file, associating a file pattern with an owner.
/// <example>
/// <code>
/// // Parse a CODEOWNERS file
/// var entries = CodeOwnersParser.Parse("*.js @user1").ToArray();
/// var entry = entries[0];
/// // entry.Pattern: "*.js"
/// // entry.Member: "user1"
/// // entry.EntryType: CodeOwnersEntryType.Username
/// </code>
/// </example>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersEntry : IEquatable<CodeOwnersEntry>
{
    private CodeOwnersEntry(int patternIndex, string pattern, CodeOwnersEntryType entryType, string? member, CodeOwnersSection? section)
    {
        Pattern = pattern;
        PatternIndex = patternIndex;
        Member = member;
        Section = section;
        EntryType = entryType;
    }

    /// <summary>Gets the zero-based index of the pattern in the CODEOWNERS file.</summary>
    public int PatternIndex { get; }

    /// <summary>Gets the file pattern (e.g., "*.js", "/docs/*", or "*") that this entry applies to.</summary>
    public string Pattern { get; }

    /// <summary>Gets the type of the owner (username, email address, or none).</summary>
    public CodeOwnersEntryType EntryType { get; }

    /// <summary>Gets the owner identifier (username without @ or email address), or null if no owner is assigned.</summary>
    public string? Member { get; }

    /// <summary>Gets the section this entry belongs to, or null if not part of a section.</summary>
    public CodeOwnersSection? Section { get; }

    /// <summary>Gets a value indicating whether this entry belongs to an optional section.</summary>
    public bool IsOptional => Section?.IsOptional ?? false;

    public override string ToString()
    {
        var result = $"{Pattern} {Member}";
        if (IsOptional)
        {
            result += " (optional)";
        }

        return result;
    }

    internal static CodeOwnersEntry FromUsername(int patternIndex, string pattern, string username, CodeOwnersSection? section)
    {
        return new CodeOwnersEntry(patternIndex, pattern, CodeOwnersEntryType.Username, username, section);
    }

    internal static CodeOwnersEntry FromEmailAddress(int patternIndex, string pattern, string emailAddress, CodeOwnersSection? section)
    {
        return new CodeOwnersEntry(patternIndex, pattern, CodeOwnersEntryType.EmailAddress, emailAddress, section);
    }

    internal static CodeOwnersEntry FromNone(int patternIndex, string pattern, CodeOwnersSection? section)
    {
        return new CodeOwnersEntry(patternIndex, pattern, CodeOwnersEntryType.None, member: null, section);
    }

    public override bool Equals(object? obj)
    {
        return obj is CodeOwnersEntry entry && Equals(entry);

    }

    public bool Equals(CodeOwnersEntry other)
    {
        return EntryType == other.EntryType &&
               string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
               string.Equals(Member, other.Member, StringComparison.Ordinal) &&
               Section == other.Section;
    }

    public override int GetHashCode() => HashCode.Combine(Pattern, EntryType, Member, Section);

    public static bool operator ==(CodeOwnersEntry left, CodeOwnersEntry right) => left.Equals(right);
    public static bool operator !=(CodeOwnersEntry left, CodeOwnersEntry right) => !(left == right);
}
