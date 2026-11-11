using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.CodeOwners;

/// <summary>Matches the paths of a repository against the pattern of a <see cref="CodeOwnersEntry"/>, with the glob dialect of the host.</summary>
internal sealed class CodeOwnersPatternMatcher
{
    private readonly Glob? _glob;

    private CodeOwnersPatternMatcher(Glob? glob)
    {
        _glob = glob;
    }

    /// <summary>Gets a value indicating whether the pattern, as written in the file, is a GitLab exclusion pattern.</summary>
    public static bool IsExclusion(string escapedPattern, CodeOwnersDialect dialect) => dialect is CodeOwnersDialect.GitLab && escapedPattern.StartsWith('!', StringComparison.Ordinal);

    public static CodeOwnersPatternMatcher Create(string escapedPattern, CodeOwnersDialect dialect)
    {
        var pattern = escapedPattern;
        var globDialect = GlobDialect.GitHubCodeOwners;
        if (dialect is CodeOwnersDialect.GitLab)
        {
            globDialect = GlobDialect.GitLabCodeOwners;

            // The '!' of an exclusion is not part of the pattern. A line made of a single '!' leaves an empty pattern,
            // which GitLab normalizes to "/**/**/*": it excludes every path from the section. Globbing rejects an empty
            // pattern, so use "/", which GitLab normalizes to "/**/*" and matches the same paths.
            if (IsExclusion(pattern, dialect))
            {
                pattern = pattern.Length is 1 ? "/" : pattern[1..];
            }
        }

        // A pattern the dialect rejects, such as one holding an empty path segment, never matches
        return new CodeOwnersPatternMatcher(Glob.TryParse(pattern, globDialect, GlobOptions.None, out var glob) ? glob : null);
    }

    /// <summary>Determines whether the pattern matches the path of a file, relative to the root of the repository and without a leading '/'.</summary>
    public bool IsMatch(ReadOnlySpan<char> path)
    {
        if (_glob is null || path.IsEmpty)
            return false;

        var separatorIndex = path.LastIndexOf('/');
        return separatorIndex < 0
            ? _glob.IsMatch([], path, PathItemType.File)
            : _glob.IsMatch(path[..separatorIndex], path[(separatorIndex + 1)..], PathItemType.File);
    }
}
