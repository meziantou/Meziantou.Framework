using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents an entry in a CODEOWNERS file.
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

    /// <summary>
    /// Gets the index of the pattern in the CODEOWNERS file.
    /// </summary>
    public int PatternIndex { get; }

    /// <summary>
    /// Gets the file path pattern for this entry.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets the type of this entry.
    /// </summary>
    public CodeOwnersEntryType EntryType { get; }

    /// <summary>
    /// Gets the owner member (username or email address).
    /// </summary>
    public string? Member { get; }

    /// <summary>
    /// Gets the section this entry belongs to.
    /// </summary>
    public CodeOwnersSection? Section { get; }

    /// <summary>
    /// Gets a value indicating whether this entry is optional (does not require approval).
    /// </summary>
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
