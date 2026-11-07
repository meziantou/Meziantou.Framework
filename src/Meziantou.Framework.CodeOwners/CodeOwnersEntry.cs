using System.Text;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a single line of a CODEOWNERS file, associating a file pattern with its owners.
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs" ref:region="CodeOwnersEntry_Example" ref:dedent="true">
/// // Parse a CODEOWNERS file
/// var file = CodeOwnersFile.Parse("*.js @user1 @user2", CodeOwnersDialect.GitLab);
/// var entry = file.Entries[0];
/// Assert.Equal("*.js", entry.Pattern);
/// Assert.Equal(["@user1", "@user2"], entry.Owners.Select(owner =&gt; owner.ToString()));
/// </code>
/// </example>
/// </summary>
/// <remarks>Entries are returned in the order they appear in the file. CODEOWNERS resolution is last-match-wins, so the last entry whose <see cref="Pattern"/> matches a path owns it.</remarks>
public sealed class CodeOwnersEntry : IEquatable<CodeOwnersEntry>
{
    private readonly CodeOwnersDialect _dialect;

    internal CodeOwnersEntry(string pattern, IReadOnlyList<CodeOwner> owners, CodeOwnersSection? section, CodeOwnersDialect dialect)
    {
        Pattern = pattern;
        Owners = owners;
        Section = section;
        _dialect = dialect;
    }

    /// <summary>Gets the file pattern (e.g., "*.js", "/docs/*", or "*") that this entry applies to.</summary>
    public string Pattern { get; }

    /// <summary>Gets the owners of the pattern. Empty when the entry explicitly leaves the pattern unowned.</summary>
    public IReadOnlyList<CodeOwner> Owners { get; }

    /// <summary>Gets the section this entry belongs to, or null if not part of a section.</summary>
    public CodeOwnersSection? Section { get; }

    /// <summary>Gets a value indicating whether this entry belongs to an optional section.</summary>
    public bool IsOptional => Section?.IsOptional ?? false;

    /// <summary>Returns the entry as written in a CODEOWNERS file of the dialect it was parsed with: the escaped pattern followed by the owners.</summary>
    /// <remarks>The section header is not part of the entry: parse the line after <see cref="CodeOwnersSection.ToString"/> to read the entry back.</remarks>
    public override string ToString()
    {
        var sb = new StringBuilder(Pattern.Length + (Owners.Count * 16));
        for (var i = 0; i < Pattern.Length; i++)
        {
            var c = Pattern[i];
            if (MustEscape(c, i))
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        foreach (var owner in Owners)
        {
            sb.Append(' ').Append(owner.ToString());
        }

        return sb.ToString();
    }

    /// <summary>Gets a value indicating whether the character of the pattern at the index is read as something else than text of the pattern when written as is.</summary>
    private bool MustEscape(char c, int index) => c switch
    {
        // A backslash escapes the next character, and spaces separate the pattern from the owners
        '\\' or ' ' or '\t' => true,

        // A line starting with '#' is a comment
        '#' => index is 0,

        // In the GitLab syntax, a line starting with '[' or '^[' is a section header
        '[' => index is 0 && _dialect is CodeOwnersDialect.GitLab,
        '^' => index is 0 && _dialect is CodeOwnersDialect.GitLab && Pattern.Length > 1 && Pattern[1] is '[',
        _ => false,
    };

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
