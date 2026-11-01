using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>The rules of a <c>.dockerignore</c> file, applied the way the docker CLI applies them to a build context.</summary>
/// <remarks>
/// A pattern is anchored at the root of the context and matched with the rules of Go's <c>filepath.Match</c>, plus
/// <c>**</c>, which matches any number of directories. A pattern that starts with <c>!</c> is an exception, and the last
/// pattern that matches a path decides. A path is excluded when it, or one of its parent directories, is excluded.
/// </remarks>
internal sealed class DockerIgnore
{
    private readonly Pattern[] _patterns;

    private DockerIgnore(Pattern[] patterns)
    {
        _patterns = patterns;
    }

    /// <summary>Whether the rules hold at least one exception, which can bring back a path below an excluded directory.</summary>
    public bool HasExceptions => _patterns.Any(static pattern => pattern.IsException);

    /// <summary>Parses the content of a <c>.dockerignore</c> file.</summary>
    /// <param name="content">The content of the file.</param>
    /// <param name="alwaysIncludedPaths">The paths, relative to the context, that are never excluded. The docker CLI always sends the Dockerfile and the ignore file.</param>
    public static DockerIgnore Parse(string content, IEnumerable<string> alwaysIncludedPaths)
    {
        var patterns = new List<Pattern>();
        using var reader = new StringReader(content);
        var isFirstLine = true;
        while (reader.ReadLine() is { } line)
        {
            if (isFirstLine)
            {
                line = line.TrimStart('\uFEFF');
                isFirstLine = false;
            }

            // A comment is only recognized at the very beginning of the line, before the whitespace is trimmed.
            if (line.StartsWith('#', StringComparison.Ordinal))
                continue;

            var text = line.Trim();
            var isException = false;
            if (text.StartsWith('!', StringComparison.Ordinal))
            {
                isException = true;
                text = text[1..].Trim();
            }

            text = Clean(text);
            if (text.Length is 0)
                continue;

            patterns.Add(new Pattern(text, isException));
        }

        foreach (var path in alwaysIncludedPaths)
        {
            var normalized = Clean(path);
            if (normalized.Length > 0)
                patterns.Add(new Pattern(EscapeLiteral(normalized), isException: true));
        }

        return new DockerIgnore([.. patterns]);
    }

    /// <summary>Loads the ignore file a build uses: <c>&lt;Dockerfile&gt;.dockerignore</c> next to the Dockerfile when it exists, <c>.dockerignore</c> at the root of the context otherwise.</summary>
    /// <param name="contextDirectory">The build context directory.</param>
    /// <param name="dockerfilePath">The full path of the Dockerfile.</param>
    /// <param name="dockerfileEntryName">The name of the Dockerfile in the context, which is never excluded.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The rules, or <see langword="null"/> when there is no ignore file.</returns>
    public static async Task<DockerIgnore?> LoadAsync(string contextDirectory, string dockerfilePath, string dockerfileEntryName, CancellationToken cancellationToken)
    {
        var dockerfileIgnorePath = dockerfilePath + ".dockerignore";
        var contextIgnorePath = Path.Combine(contextDirectory, ".dockerignore");
        var path = File.Exists(dockerfileIgnorePath) ? dockerfileIgnorePath : contextIgnorePath;
        if (!File.Exists(path))
            return null;

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        List<string> alwaysIncluded = [dockerfileEntryName, ".dockerignore"];
        var ignoreEntryName = Path.GetRelativePath(contextDirectory, path).Replace('\\', '/');
        if (!ignoreEntryName.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(ignoreEntryName))
            alwaysIncluded.Add(ignoreEntryName);

        return Parse(content, alwaysIncluded);
    }

    /// <summary>Determines whether a path of the context is excluded.</summary>
    /// <param name="relativePath">The path, relative to the context, with <c>/</c> as the separator.</param>
    public bool IsExcluded(string relativePath)
    {
        // Same algorithm as moby's PatternMatcher.MatchesOrParentMatches: every pattern is tried in order, so the last one
        // that matches the path or one of its parents wins.
        var excluded = false;
        foreach (var pattern in _patterns)
        {
            // An exception cannot change a path that is not excluded, nor a pattern a path that already is.
            if (pattern.IsException != excluded)
                continue;

            if (MatchesOrParentMatches(pattern, relativePath))
                excluded = !pattern.IsException;
        }

        return excluded;
    }

    /// <summary>Determines whether an exception could bring back a path below an excluded directory, in which case the directory must still be walked.</summary>
    /// <param name="relativeDirectoryPath">The directory, relative to the context, with <c>/</c> as the separator.</param>
    public bool CanIncludeBelow(string relativeDirectoryPath)
    {
        var directorySegments = relativeDirectoryPath.Split('/');
        foreach (var pattern in _patterns)
        {
            if (pattern.IsException && pattern.CanMatchBelow(directorySegments))
                return true;
        }

        return false;
    }

    private static bool MatchesOrParentMatches(Pattern pattern, string path)
    {
        if (pattern.IsMatch(path))
            return true;

        var index = path.IndexOf('/', StringComparison.Ordinal);
        while (index > 0)
        {
            if (pattern.IsMatch(path[..index]))
                return true;

            index = path.IndexOf('/', index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Normalizes a pattern like Go's <c>filepath.Clean</c> does, and removes the leading <c>/</c>: the patterns are relative to the context.</summary>
    internal static string Clean(string pattern)
    {
        if (OperatingSystem.IsWindows())
            pattern = pattern.Replace('\\', '/');

        if (pattern.Length is 0)
            return "";

        var segments = new List<string>();
        foreach (var segment in pattern.Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;

                case ".." when segments.Count > 0 && segments[^1] is not "..":
                    segments.RemoveAt(segments.Count - 1);
                    break;

                default:
                    segments.Add(segment);
                    break;
            }
        }

        return string.Join('/', segments);
    }

    private static string EscapeLiteral(string path)
    {
        var builder = new StringBuilder(path.Length);
        foreach (var c in path)
        {
            if (c is '*' or '?' or '[' or ']' or '\\')
                builder.Append('\\');

            builder.Append(c);
        }

        return builder.ToString();
    }

    private sealed class Pattern
    {
        private readonly Regex _regex;
        private readonly string[] _literalPrefix;
        private readonly bool _isLiteral;

        public Pattern(string text, bool isException)
        {
            IsException = isException;
            _regex = new Regex(ToRegex(text), RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

            // The segments before the first wildcard, which a path must start with to be matched.
            var prefix = new List<string>();
            _isLiteral = true;
            foreach (var segment in text.Split('/'))
            {
                if (segment.AsSpan().IndexOfAny("*?[\\") >= 0)
                {
                    _isLiteral = false;
                    break;
                }

                prefix.Add(segment);
            }

            _literalPrefix = [.. prefix];
        }

        public bool IsException { get; }

        public bool IsMatch(string path) => _regex.IsMatch(path);

        public bool CanMatchBelow(string[] directorySegments)
        {
            // A literal pattern names a single path: it is below the directory only when it is longer.
            if (_isLiteral && _literalPrefix.Length <= directorySegments.Length)
                return false;

            var length = Math.Min(_literalPrefix.Length, directorySegments.Length);
            for (var i = 0; i < length; i++)
            {
                if (!string.Equals(_literalPrefix[i], directorySegments[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        /// <summary>Translates a pattern to a regular expression the way moby's patternmatcher does.</summary>
        private static string ToRegex(string pattern)
        {
            var builder = new StringBuilder("^");
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                switch (c)
                {
                    case '*' when i + 1 < pattern.Length && pattern[i + 1] == '*':
                        i++;

                        // '**/' matches zero or more directories, a trailing '**' matches everything.
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                            i++;

                        builder.Append(i + 1 >= pattern.Length ? ".*" : "(?:.*/)?");
                        break;

                    case '*':
                        builder.Append("[^/]*");
                        break;

                    case '?':
                        builder.Append("[^/]");
                        break;

                    case '\\' when i + 1 < pattern.Length:
                        i++;
                        builder.Append(Regex.Escape(pattern[i].ToString()));
                        break;

                    case '[':
                        i = AppendCharacterClass(builder, pattern, i);
                        break;

                    default:
                        builder.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }

            builder.Append('$');
            return builder.ToString();
        }

        private static int AppendCharacterClass(StringBuilder builder, string pattern, int start)
        {
            var end = start + 1;
            if (end < pattern.Length && pattern[end] is '^')
                end++;

            if (end < pattern.Length && pattern[end] == ']')
                end++;

            while (end < pattern.Length && pattern[end] != ']')
            {
                if (pattern[end] == '\\')
                    end++;

                end++;
            }

            // An unterminated class matches nothing, like Go's filepath.Match, which reports it as an error.
            if (end >= pattern.Length)
            {
                builder.Append("(?!)");
                return pattern.Length - 1;
            }

            // A path separator is never matched by a class.
            builder.Append("(?!/)[");
            var i = start + 1;
            if (pattern[i] is '^')
            {
                builder.Append('^');
                i++;
            }

            for (; i < end; i++)
            {
                var c = pattern[i];
                if (c == '\\' && i + 1 < end)
                {
                    i++;
                    c = pattern[i];
                }

                if (c is '\\' or '[' or ']' or '^')
                    builder.Append('\\');

                builder.Append(c);
            }

            builder.Append(']');
            return end;
        }
    }
}
