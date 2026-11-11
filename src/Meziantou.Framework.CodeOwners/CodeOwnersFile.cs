using System.Collections.ObjectModel;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a parsed CODEOWNERS file.
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs" ref:region="CodeOwnersFile_Parse" ref:dedent="true">
/// var file = CodeOwnersFile.Parse("* @user1\n*.js @js-owner", CodeOwnersDialect.GitHub);
///
/// Assert.Equal("*", file.Entries[0].Pattern);
/// Assert.Equal("@user1", file.Entries[0].Owners[0].ToString());
///
/// Assert.Equal("*.js", file.Entries[1].Pattern);
/// Assert.Equal("@js-owner", file.Entries[1].Owners[0].ToString());
/// </code>
/// </example>
/// </summary>
/// <remarks>An instance only exists for a valid file: <see cref="Parse(string, CodeOwnersDialect)"/> throws and <see cref="TryParse(string, CodeOwnersDialect, out CodeOwnersFile)"/> returns <see langword="false"/> rather than returning a partially parsed file.</remarks>
public sealed class CodeOwnersFile
{
    private readonly CodeOwnersDialect _dialect;

    internal CodeOwnersFile(IReadOnlyList<CodeOwnersEntry> entries, CodeOwnersDialect dialect)
    {
        Entries = entries;
        _dialect = dialect;
    }

    /// <summary>Gets the entries of the file, in the order they appear in it.</summary>
    /// <remarks>CODEOWNERS resolution is last-match-wins, so the last entry whose <see cref="CodeOwnersEntry.Pattern"/> matches a path owns it.</remarks>
    public IReadOnlyList<CodeOwnersEntry> Entries { get; }

    /// <summary>Finds the entries that own the path of a file, as the host of the dialect the file was parsed with does.</summary>
    /// <param name="path">The path of a file, relative to the root of the repository, with <c>/</c> as the separator. A leading <c>/</c> is ignored. The comparison is case-sensitive.</param>
    /// <returns>
    /// The entries that own <paramref name="path"/>, in the order of the file, or an empty list when no entry matches it.
    /// With <see cref="CodeOwnersDialect.GitHub"/>, the last entry whose pattern matches the path.
    /// With <see cref="CodeOwnersDialect.GitLab"/>, the last matching entry of each section, except the sections where an exclusion matches the path.
    /// </returns>
    /// <remarks>
    /// An entry without owners is returned too: it means that the path is explicitly left unowned. Use <see cref="GetOwners(string)"/> to get the owners.
    /// GitLab merges the sections that have the same name, ignoring case: they count as one section.
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs" ref:region="CodeOwnersFile_FindEntries" ref:dedent="true">
    /// var file = CodeOwnersFile.Parse("""
    ///     * @maintainers
    ///
    ///     [Backend]
    ///     *.cs @backend-team
    ///
    ///     [Docs]
    ///     *.md @docs-team
    ///     /src/ @writers
    ///     """, CodeOwnersDialect.GitLab);
    ///
    /// // GitLab applies the last matching entry of each section
    /// IReadOnlyList&lt;CodeOwnersEntry&gt; entries = file.FindEntries("src/Program.cs");
    /// Assert.Equal(["*", "*.cs", "/src/"], entries.Select(entry =&gt; entry.Pattern));
    /// Assert.Equal([null, "Backend", "Docs"], entries.Select(entry =&gt; entry.Section?.Name));
    /// </code>
    /// </example>
    public IReadOnlyList<CodeOwnersEntry> FindEntries(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return _dialect is CodeOwnersDialect.GitLab ? FindGitLabEntries(path) : FindGitHubEntry(path);
    }

    /// <summary>Gets the owners of the path of a file, as the host of the dialect the file was parsed with does.</summary>
    /// <param name="path">The path of a file, relative to the root of the repository, with <c>/</c> as the separator. A leading <c>/</c> is ignored. The comparison is case-sensitive.</param>
    /// <returns>The owners of the entries returned by <see cref="FindEntries(string)"/>, without duplicates. Empty when no entry matches the path, or when the matching entries leave it unowned.</returns>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs" ref:region="CodeOwnersFile_GetOwners" ref:dedent="true">
    /// var file = CodeOwnersFile.Parse("""
    ///     * @maintainers
    ///     *.md @docs-team
    ///     /docs/internal/
    ///     """, CodeOwnersDialect.GitHub);
    ///
    /// Assert.Equal(["maintainers"], file.GetOwners("src/Program.cs").Select(owner =&gt; owner.Name));
    /// Assert.Equal(["docs-team"], file.GetOwners("docs/README.md").Select(owner =&gt; owner.Name));
    ///
    /// // The last matching entry has no owners: the path is unowned
    /// Assert.Empty(file.GetOwners("docs/internal/notes.md"));
    /// </code>
    /// </example>
    public IReadOnlyList<CodeOwner> GetOwners(string path)
    {
        var entries = FindEntries(path);
        if (entries.Count is 0)
            return [];

        var owners = new List<CodeOwner>();
        foreach (var entry in entries)
        {
            foreach (var owner in entry.Owners)
            {
                if (!owners.Contains(owner))
                {
                    owners.Add(owner);
                }
            }
        }

        return owners.AsReadOnly();
    }

    private CodeOwnersEntry[] FindGitHubEntry(string path)
    {
        // Last match wins
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];
            if (entry.IsMatch(path))
                return [entry];
        }

        return [];
    }

    private ReadOnlyCollection<CodeOwnersEntry> FindGitLabEntries(string path)
    {
        // One result per section, in the order the sections first appear in the file
        var sections = new List<GitLabSectionMatch>();
        foreach (var entry in Entries)
        {
            var sectionIndex = sections.FindIndex(section => IsSameGitLabSection(section.Name, entry.Section?.Name));
            if (sectionIndex < 0)
            {
                sections.Add(new GitLabSectionMatch(entry.Section?.Name));
                sectionIndex = sections.Count - 1;
            }

            var match = sections[sectionIndex];
            if (match.IsExcluded || !entry.IsMatch(path))
                continue;

            // An exclusion removes the section, even when a later entry of the section matches the path
            sections[sectionIndex] = entry.IsExclusion ? match with { Entry = null, IsExcluded = true } : match with { Entry = entry };
        }

        List<CodeOwnersEntry>? result = null;
        foreach (var section in sections)
        {
            if (section.Entry is not null)
            {
                result ??= [];
                result.Add(section.Entry);
            }
        }

        return result is null ? ReadOnlyCollection<CodeOwnersEntry>.Empty : result.AsReadOnly();
    }

    private static bool IsSameGitLabSection(string? name, string? otherName)
    {
        // The entries written before the first section are in the default section
        if (name is null || otherName is null)
            return name is null && otherName is null;

        return string.Equals(name, otherName, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct GitLabSectionMatch(string? Name)
    {
        public CodeOwnersEntry? Entry { get; init; }

        public bool IsExcluded { get; init; }
    }

    /// <summary>Parses the content of a CODEOWNERS file.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <returns>The parsed <see cref="CodeOwnersFile"/>.</returns>
    /// <exception cref="CodeOwnersParseException"><paramref name="content"/> is not a valid CODEOWNERS file. Parsing stops at the first error.</exception>
    public static CodeOwnersFile Parse(string content, CodeOwnersDialect dialect)
    {
        return Parse(content.AsSpan(), dialect);
    }

    /// <summary>Parses the content of a CODEOWNERS file.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <returns>The parsed <see cref="CodeOwnersFile"/>.</returns>
    /// <exception cref="CodeOwnersParseException"><paramref name="content"/> is not a valid CODEOWNERS file. Parsing stops at the first error.</exception>
    /// <remarks>The returned <see cref="CodeOwnersFile"/> does not reference <paramref name="content"/>: every value it exposes is copied out of it.</remarks>
    public static CodeOwnersFile Parse(ReadOnlySpan<char> content, CodeOwnersDialect dialect)
    {
        var context = new CodeOwnersParserContext(content, dialect);
        var entries = context.Parse();
        if (context.HasError)
            throw new CodeOwnersParseException(context.CreateError());

        return new CodeOwnersFile(entries, dialect);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    /// <remarks>Use the <see cref="TryParse(string, CodeOwnersDialect, out CodeOwnersFile, out CodeOwnersParseError)"/> overload to know why the file is invalid.</remarks>
    public static bool TryParse(string content, CodeOwnersDialect dialect, [NotNullWhen(true)] out CodeOwnersFile? file)
    {
        return TryParse(content.AsSpan(), dialect, out file, out _);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    /// <remarks>Use the <see cref="TryParse(ReadOnlySpan{char}, CodeOwnersDialect, out CodeOwnersFile, out CodeOwnersParseError)"/> overload to know why the file is invalid.</remarks>
    public static bool TryParse(ReadOnlySpan<char> content, CodeOwnersDialect dialect, [NotNullWhen(true)] out CodeOwnersFile? file)
    {
        return TryParse(content, dialect, out file, out _);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <param name="error">When this method returns <see langword="false"/>, contains the first error found in <paramref name="content"/>; otherwise, <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string content, CodeOwnersDialect dialect, [NotNullWhen(true)] out CodeOwnersFile? file, out CodeOwnersParseError error)
    {
        return TryParse(content.AsSpan(), dialect, out file, out error);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <param name="error">When this method returns <see langword="false"/>, contains the first error found in <paramref name="content"/>; otherwise, <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The parsed <see cref="CodeOwnersFile"/> does not reference <paramref name="content"/>: every value it exposes is copied out of it.</remarks>
    public static bool TryParse(ReadOnlySpan<char> content, CodeOwnersDialect dialect, [NotNullWhen(true)] out CodeOwnersFile? file, out CodeOwnersParseError error)
    {
        var context = new CodeOwnersParserContext(content, dialect);
        var entries = context.Parse();
        if (context.HasError)
        {
            file = null;
            error = context.CreateError();
            return false;
        }

        file = new CodeOwnersFile(entries, dialect);
        error = default;
        return true;
    }
}
