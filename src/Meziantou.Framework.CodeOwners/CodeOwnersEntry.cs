using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

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

    public int PatternIndex { get; }

    public string Pattern { get; }

    public CodeOwnersEntryType EntryType { get; }

    public string? Member { get; }

    public CodeOwnersSection? Section { get; }

    public bool IsOptional => Section?.IsOptional ?? false;

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

    public override int GetHashCode()
    {
        var hashCode = 1707150943;
        hashCode = (hashCode * -1521134295) + EntryType.GetHashCode();
        hashCode = (hashCode * -1521134295) + StringComparer.Ordinal.GetHashCode(Pattern);
        hashCode = (hashCode * -1521134295) + (Member is null ? 0 : StringComparer.Ordinal.GetHashCode(Member));
        hashCode = (hashCode * -1521134295) + (Section == null ? 0 : Section.GetHashCode());
        return hashCode;
    }

    public static bool operator ==(CodeOwnersEntry left, CodeOwnersEntry right) => left.Equals(right);
    public static bool operator !=(CodeOwnersEntry left, CodeOwnersEntry right) => !(left == right);
}
