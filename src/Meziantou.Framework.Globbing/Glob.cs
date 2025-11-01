using Meziantou.Framework.Globbing.Internals;

#if NET472
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

/// <summary>
///     Glob patterns specify sets of filenames with wildcard characters. Supported syntaxes:
///     <list type="table">
///         <listheader>
///             <term>Wildcard</term>
///             <description>Description</description>
///         </listheader>
///         <item>
///             <term>*</term>
///             <description>matches any number of characters including none, excluding directory separator</description>
///         </item>
///         <item>
///             <term>?</term>
///             <description>matches a single character</description>
///         </item>
///         <item>
///             <term>[abc]</term>
///             <description>matches one character in the brackets</description>
///         </item>
///         <item>
///             <term>[!abc]</term>
///             <description>matches any character not in the brackets</description>
///         </item>
///         <item>
///             <term>[a-z]</term>
///             <description>matches one character from the range given in the brackets. <c>GlobOptions.IgnoreCase</c> is only supported for ASCII letters.</description>
///         </item>
///         <item>
///             <term>[!a-z]</term>
///             <description>matches one character that is not from the range given in the brackets. <c>GlobOptions.IgnoreCase</c> is only supported for ASCII letters.</description>
///         </item>
///         <item>
///             <term>{abc,123}</term>
///             <description>comma-delimited set of literals, matches 'abc' or '123'</description>
///         </item>
///         <item>
///             <term>**</term>
///             <description>matches zero or more directories</description>
///         </item>
///         <item>
///             <term>!pattern</term>
///             <description>leading '!' negates the pattern</description>
///         </item>
///         <item>
///             <term>\x</term>
///             <description>escapes the following character. For instance, '\*' matches the literal character '*' instead of being a wildcard.</description>
///         </item>
///     </list>
///     <para>If the pattern ends with a <c>/</c>, only directories are matched. Otherwise, only files are matched.</para>
/// </summary>
/// <example>
/// Parse a glob pattern and check if a path matches:
/// <code>
/// var glob = Glob.Parse("src/**/*.txt", GlobOptions.None);
/// if (glob.IsMatch("src/folder/file.txt"))
/// {
///     Console.WriteLine("File matches!");
/// }
/// </code>
/// Enumerate files that match a glob pattern:
/// <code>
/// var glob = Glob.Parse("**/*.cs", GlobOptions.IgnoreCase);
/// foreach (var file in glob.EnumerateFiles("C:/MyProject"))
/// {
///     Console.WriteLine(file);
/// }
/// </code>
/// </example>
/// <seealso href="https://en.wikipedia.org/wiki/Glob_(programming)"/>
/// <seealso href="https://www.meziantou.net/enumerating-files-using-globbing-and-system-io-enumeration.htm"/>
public sealed class Glob : IGlobEvaluatable
{
    private readonly GlobMatchType _matchType;
    internal readonly Segment[] _segments;

    /// <summary>Gets the glob mode indicating whether this pattern includes or excludes matches.</summary>
    public GlobMode Mode { get; }

    bool IGlobEvaluatable.CanMatchFiles => _matchType is GlobMatchType.File or GlobMatchType.Any;
    bool IGlobEvaluatable.CanMatchDirectories => _matchType is GlobMatchType.Directory or GlobMatchType.Any;
    bool IGlobEvaluatable.TraverseDirectories => _segments.Length > 1 || ShouldRecurse(_segments[0]);

    internal Glob(Segment[] segments, GlobMode mode, GlobMatchType matchType)
    {
        _segments = segments;
        _matchType = matchType;
        Mode = mode;
    }

    /// <summary>Parses a glob pattern string.</summary>
    /// <param name="pattern">The glob pattern to parse.</param>
    /// <param name="options">Options for controlling pattern parsing behavior.</param>
    /// <returns>A <see cref="Glob"/> object representing the parsed pattern.</returns>
    /// <exception cref="ArgumentException">The pattern is invalid.</exception>
    public static Glob Parse(string pattern, GlobOptions options)
    {
        return Parse(pattern.AsSpan(), options);
    }

    /// <summary>Parses a glob pattern string.</summary>
    /// <param name="pattern">The glob pattern to parse.</param>
    /// <param name="options">Options for controlling pattern parsing behavior.</param>
    /// <returns>A <see cref="Glob"/> object representing the parsed pattern.</returns>
    /// <exception cref="ArgumentException">The pattern is invalid.</exception>
    public static Glob Parse(ReadOnlySpan<char> pattern, GlobOptions options)
    {
        if (TryParse(pattern, options, out var result, out var errorMessage))
            return result;

        throw new ArgumentException($"The pattern '{pattern.ToString()}' is invalid: {errorMessage}", nameof(pattern));
    }

    /// <summary>Attempts to parse a glob pattern string.</summary>
    /// <param name="pattern">The glob pattern to parse.</param>
    /// <param name="options">Options for controlling pattern parsing behavior.</param>
    /// <param name="result">When this method returns, contains the parsed <see cref="Glob"/> if parsing succeeded, or <see langword="null"/> if parsing failed.</param>
    /// <returns><see langword="true"/> if the pattern was parsed successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string pattern, GlobOptions options, [NotNullWhen(true)] out Glob? result)
    {
        return TryParse(pattern.AsSpan(), options, out result);
    }

    /// <summary>Attempts to parse a glob pattern string.</summary>
    /// <param name="pattern">The glob pattern to parse.</param>
    /// <param name="options">Options for controlling pattern parsing behavior.</param>
    /// <param name="result">When this method returns, contains the parsed <see cref="Glob"/> if parsing succeeded, or <see langword="null"/> if parsing failed.</param>
    /// <returns><see langword="true"/> if the pattern was parsed successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> pattern, GlobOptions options, [NotNullWhen(true)] out Glob? result)
    {
        return TryParse(pattern, options, out result, out _);
    }

    private static bool TryParse(ReadOnlySpan<char> pattern, GlobOptions options, [NotNullWhen(true)] out Glob? result, [NotNullWhen(false)] out string? errorMessage)
    {
        return GlobParser.TryParse(pattern, options, out result, out errorMessage);
    }

    /// <summary>Determines whether the specified path matches this glob pattern.</summary>
    /// <param name="directory">The directory part of the path to match.</param>
    /// <param name="filename">The filename part of the path to match.</param>
    /// <param name="itemType">The type of the path item (file or directory), or <see langword="null"/> if unknown.</param>
    /// <returns><see langword="true"/> if the path matches the pattern; otherwise, <see langword="false"/>.</returns>
    public bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        return IsMatchCore(directory, filename, itemType);
    }

    internal bool IsMatchCore(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        var pathEnumerator = new PathReader(directory, filename, itemType);
        return IsMatchCore(pathEnumerator, _segments);
    }

    private bool IsMatchCore(PathReader pathReader, ReadOnlySpan<Segment> patternSegments)
    {
        if (_matchType is not GlobMatchType.Any)
        {
            if (_matchType is GlobMatchType.File && pathReader.IsDirectory)
                return false;

            if (_matchType is GlobMatchType.Directory && !pathReader.IsDirectory)
                return false;
        }

        for (var i = 0; i < patternSegments.Length; i++)
        {
            var patternSegment = patternSegments[i];
            if (patternSegment is RecursiveMatchAllSegment)
            {
                var remainingPatternSegments = patternSegments[(i + 1)..];
                if (remainingPatternSegments.IsEmpty) // Last segment
                    return false; // Match only files

                if (IsMatchCore(pathReader, remainingPatternSegments))
                    return true;

                pathReader.ConsumeSegment();
                while (!pathReader.IsEndOfPath)
                {
                    if (IsMatchCore(pathReader, remainingPatternSegments))
                        return true;

                    pathReader.ConsumeSegment();
                }

                return false;
            }

            if (pathReader.IsEndOfPath)
                return false;

            if (!patternSegment.IsMatch(ref pathReader))
                return false;

            if (!pathReader.IsEndOfCurrentSegment)
                return false;

            pathReader.ConsumeEndOfSegment();
        }

        // Ensure the path is fully parsed
        return pathReader.IsEndOfPath;
    }

    /// <summary>Determines whether a directory should be recursed into when enumerating files.</summary>
    /// <param name="folderPath">The folder path to check.</param>
    /// <param name="filename">The filename part of the path to check.</param>
    /// <returns><see langword="true"/> if the directory could contain matches; otherwise, <see langword="false"/>.</returns>
    public bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
    {
        return IsPartialMatchCore(folderPath, filename);
    }

    internal bool IsPartialMatchCore(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
    {
        return IsPartialMatchCore(new PathReader(folderPath, filename, itemType: null), _segments);
    }

    private static bool IsPartialMatchCore(PathReader pathReader, ReadOnlySpan<Segment> patternSegments)
    {
        foreach (var patternSegment in patternSegments)
        {
            if (ShouldRecurse(patternSegment))
                return true;

            if (pathReader.IsEndOfPath)
                return true;

            if (!patternSegment.IsMatch(ref pathReader))
                return false;

            pathReader.ConsumeSegment();
            patternSegments = patternSegments[1..];
        }

        return true;
    }

    private static bool ShouldRecurse(Segment patternSegment)
    {
        return patternSegment.IsRecursiveMatchAll;
    }

    public override string ToString()
    {
        using var sb = new ValueStringBuilder();
        if (Mode is GlobMode.Exclude)
        {
            sb.Append('!');
        }

        var first = true;
        foreach (var segment in _segments)
        {
            if (!first)
            {
                sb.Append('/');
            }

            sb.Append(segment.ToString());
            first = false;
        }

        return sb.ToString();
    }

    internal static ReadOnlySpan<char> GetRelativeDirectory(ref FileSystemEntry entry)
    {
        if (entry.Directory.Length == entry.RootDirectory.Length)
            return [];

        return entry.Directory[(entry.RootDirectory.Length + 1)..];
    }
}
