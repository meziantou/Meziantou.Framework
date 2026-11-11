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
    private readonly string _escapedPattern;
    private readonly CodeOwnersDialect _dialect;
    private CodeOwnersPatternMatcher? _matcher;

    internal CodeOwnersEntry(string pattern, string escapedPattern, IReadOnlyList<CodeOwner> owners, CodeOwnersSection? section, CodeOwnersDialect dialect)
    {
        Pattern = pattern;
        _escapedPattern = escapedPattern;
        Owners = owners;
        Section = section;
        _dialect = dialect;
    }

    /// <summary>Gets the file pattern (e.g., "*.js", "/docs/*", or "*") that this entry applies to, without its escape characters.</summary>
    public string Pattern { get; }

    /// <summary>Gets the owners of the pattern. Empty when the entry explicitly leaves the pattern unowned.</summary>
    public IReadOnlyList<CodeOwner> Owners { get; }

    /// <summary>Gets the section this entry belongs to, or null if not part of a section.</summary>
    public CodeOwnersSection? Section { get; }

    /// <summary>Gets a value indicating whether this entry belongs to an optional section.</summary>
    public bool IsOptional => Section?.IsOptional ?? false;

    /// <summary>Gets a value indicating whether the entry is a GitLab exclusion: its pattern starts with an unescaped <c>!</c>.</summary>
    /// <remarks>
    /// A path that matches an exclusion of a section has no owners in this section, whatever the order of the entries.
    /// The <see cref="Pattern"/> of an exclusion keeps its leading <c>!</c>. The GitHub dialect has no exclusions: a <c>!</c> is an ordinary character.
    /// </remarks>
    public bool IsExclusion => CodeOwnersPatternMatcher.IsExclusion(_escapedPattern, _dialect);

    /// <summary>Determines whether the pattern of the entry matches the path of a file, as the host of the dialect does.</summary>
    /// <param name="path">The path of a file, relative to the root of the repository, with <c>/</c> as the separator. A leading <c>/</c> is ignored. The comparison is case-sensitive.</param>
    /// <returns><see langword="true"/> when the pattern matches <paramref name="path"/>; otherwise, <see langword="false"/>. For an exclusion, the pattern is read without its leading <c>!</c>.</returns>
    /// <remarks>
    /// This method ignores the other entries of the file. Use <see cref="CodeOwnersFile.FindEntries(string)"/> to find the entries that own a path,
    /// which applies the last-match-wins rule, the sections and the exclusions.
    /// </remarks>
    public bool IsMatch(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var span = path.AsSpan();
        if (span.StartsWith('/'))
        {
            span = span[1..];
        }

        var matcher = _matcher ??= CodeOwnersPatternMatcher.Create(_escapedPattern, _dialect);
        return matcher.IsMatch(span);
    }

    /// <summary>Returns the entry as written in a CODEOWNERS file: the pattern, with its escape characters, followed by the owners.</summary>
    /// <remarks>The section header is not part of the entry: parse the line after <see cref="CodeOwnersSection.ToString"/> to read the entry back.</remarks>
    public override string ToString()
    {
        var sb = new StringBuilder(_escapedPattern.Length + (Owners.Count * 16));
        sb.Append(_escapedPattern);
        foreach (var owner in Owners)
        {
            sb.Append(' ').Append(owner.ToString());
        }

        return sb.ToString();
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
