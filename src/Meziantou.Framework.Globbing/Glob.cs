using Meziantou.Framework.Globbing.Internals;

using System.Buffers;
using System.IO.Enumeration;

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
///             <description>matches any character not in the brackets. <see cref="GlobDialect.Git"/>, <see cref="GlobDialect.Posix"/> and <see cref="GlobDialect.PosixPath"/> also accept <c>[^abc]</c>.</description>
///         </item>
///         <item>
///             <term>[a-z]</term>
///             <description>matches one character from the range given in the brackets. With <c>GlobOptions.IgnoreCase</c>, a character matches when it, its lowercase form or its uppercase form is in the range.</description>
///         </item>
///         <item>
///             <term>[!a-z]</term>
///             <description>matches one character that is not from the range given in the brackets, using the same rule as <c>[a-z]</c> for <c>GlobOptions.IgnoreCase</c>.</description>
///         </item>
///         <item>
///             <term>[[:alpha:]]</term>
///             <description>matches one character of a POSIX character class. Only supported by <see cref="GlobDialect.Git"/>, <see cref="GlobDialect.Posix"/> and <see cref="GlobDialect.PosixPath"/>. The supported classes are <c>alnum</c>, <c>alpha</c>, <c>blank</c>, <c>cntrl</c>, <c>digit</c>, <c>graph</c>, <c>lower</c>, <c>print</c>, <c>punct</c>, <c>space</c>, <c>upper</c> and <c>xdigit</c>. With <c>GlobOptions.IgnoreCase</c>, <c>upper</c> and <c>lower</c> match a letter of either case.</description>
///         </item>
///         <item>
///             <term>[[=a=]] [[.a.]]</term>
///             <description>an equivalence class and a collating symbol, which both stand for the character they hold. Only supported by <see cref="GlobDialect.Posix"/> and <see cref="GlobDialect.PosixPath"/>. A collating symbol can be a range bound: <c>[[.-.]-0]</c>.</description>
///         </item>
///         <item>
///             <term>{abc,123}</term>
///             <description>comma-delimited set of literals, matches 'abc' or '123'. Only supported by <see cref="GlobDialect.Standard"/>; the other dialects read the braces as ordinary characters.</description>
///         </item>
///         <item>
///             <term>**</term>
///             <description>matches zero or more directories when it makes a whole path segment. A trailing <c>**</c> matches at least one segment. The POSIX dialects read it as a <c>*</c>.</description>
///         </item>
///         <item>
///             <term>!pattern</term>
///             <description>leading '!' negates the pattern. Only supported by <see cref="GlobDialect.Standard"/> and <see cref="GlobDialect.Git"/>.</description>
///         </item>
///         <item>
///             <term>\x</term>
///             <description>escapes the following character. For instance, '\*' matches the literal character '*' instead of being a wildcard. <see cref="GlobDialect.Git"/> and the POSIX dialects also read an escape in a bracket expression. <see cref="GlobDialect.MSBuild"/> reads '\' as a path separator and escapes a character with <c>%XX</c> instead.</description>
///         </item>
///     </list>
///     <para>
///         If the pattern ends with a <c>/</c>, only directories are matched. Otherwise, only files are matched.
///         A <see cref="GlobDialect.Git"/> pattern matches both a file and a directory, and one ending with a
///         <c>/</c> matches the directory itself as well as everything below it. A <see cref="GlobDialect.Posix"/>
///         pattern matches a plain string, whatever the kind of item.
///     </para>
///     <para>
///         <see cref="GlobDialect.Git"/>, <see cref="GlobDialect.MSBuild"/>, <see cref="GlobDialect.Posix"/> and
///         <see cref="GlobDialect.PosixPath"/> follow the behavior of git, MSBuild and fnmatch(3), which the tests
///         check against corpora produced by those implementations. The exceptions are deliberate: characters are
///         compared as UTF-16 code units (git compares the bytes of their UTF-8 encoding), a path ending with a
///         <c>/</c> is a directory (fnmatch compares strings), and a pattern that can never match anything, such as
///         one holding an unknown character class, is rejected.
///     </para>
///     <para>
///         Matching backtracks, so the cost of a single <see cref="IsMatch(ReadOnlySpan{char}, ReadOnlySpan{char}, PathItemType?)"/>
///         call grows exponentially with the number of <c>*</c> wildcards in one path segment. Ordinary patterns and
///         file names are unaffected, but a pattern coming from an untrusted source can be made expensive on purpose.
///         Bound the length and the wildcard count of such patterns before parsing them.
///     </para>
/// </summary>
/// <example>
/// Parse a glob pattern and check if a path matches:
/// <code>
/// var glob = Glob.Parse("src/**/*.txt", GlobDialect.Standard);
/// if (glob.IsMatch("src/folder/file.txt"))
/// {
///     Console.WriteLine("File matches!");
/// }
/// </code>
/// Enumerate files that match a glob pattern:
/// <code>
/// var glob = Glob.Parse("**/*.cs", GlobDialect.Standard, GlobOptions.IgnoreCase);
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
    private readonly GlobDialect _dialect;
    internal readonly Segment[] _segments;
    private readonly bool[] _matchLeadingDot;
    private readonly bool _pathSeparatorAware;

    // Anchors are precomputed once per pattern. Only segments that immediately follow a
    // RecursiveMatchAllSegment ('**') can be tested as an anchor during matching, so we only
    // build (and allocate) anchors for those to avoid per-path allocations in the hot loop.
    private readonly SegmentAnchor?[] _segmentAnchors;

    /// <summary>Gets the glob mode indicating whether this pattern includes or excludes matches.</summary>
    public GlobMode Mode { get; }
    internal bool MatchLeadingDot => _matchLeadingDot.Contains(true);

    bool IGlobEvaluatable.CanMatchFiles => _matchType is GlobMatchType.File or GlobMatchType.Any;
    bool IGlobEvaluatable.CanMatchDirectories => _matchType is GlobMatchType.Directory or GlobMatchType.Any;
    bool IGlobEvaluatable.TraverseDirectories => !_pathSeparatorAware || _segments.Length > 1 || ShouldRecurse(_segments[0]);

    internal Glob(Segment[] segments, bool[] matchLeadingDot, bool pathSeparatorAware, GlobMode mode, GlobMatchType matchType, GlobDialect dialect)
    {
        _segments = segments;
        _matchLeadingDot = matchLeadingDot;
        _pathSeparatorAware = pathSeparatorAware;
        _matchType = matchType;
        _dialect = dialect;
        Mode = mode;

        _segmentAnchors = new SegmentAnchor?[segments.Length];
        for (var i = 1; i < segments.Length; i++)
        {
            if (segments[i - 1] is RecursiveMatchAllSegment && TryGetSegmentAnchor(segments[i], out var anchor))
            {
                _segmentAnchors[i] = anchor;
            }
        }
    }

    /// <summary>Parses a glob pattern string.</summary>
    /// <param name="pattern">The glob pattern to parse.</param>
    /// <param name="dialect">The glob pattern dialect to use.</param>
    /// <param name="options">Options for controlling pattern parsing behavior.</param>
    /// <returns>A <see cref="Glob"/> object representing the parsed pattern.</returns>
    /// <exception cref="ArgumentException">The pattern is invalid.</exception>
    public static Glob Parse(string pattern, GlobDialect dialect, GlobOptions options = GlobOptions.None)
    {
        return Parse(pattern.AsSpan(), dialect, options);
    }

    /// <summary>Parses a glob pattern string.</summary>
    /// <param name="pattern">The glob pattern to parse.</param>
    /// <param name="dialect">The glob pattern dialect to use.</param>
    /// <param name="options">Options for controlling pattern parsing behavior.</param>
    /// <returns>A <see cref="Glob"/> object representing the parsed pattern.</returns>
    /// <exception cref="ArgumentException">The pattern is invalid.</exception>
    public static Glob Parse(ReadOnlySpan<char> pattern, GlobDialect dialect, GlobOptions options = GlobOptions.None)
    {
        if (TryParse(pattern, dialect, options, out var result, out var errorMessage))
            return result;

        throw new ArgumentException($"The pattern '{pattern.ToString()}' is invalid: {errorMessage}", nameof(pattern));
    }

    /// <summary>Attempts to parse a glob pattern string.</summary>
    /// <param name="pattern">The glob pattern to parse.</param>
    /// <param name="dialect">The glob pattern dialect to use.</param>
    /// <param name="options">Options for controlling pattern parsing behavior.</param>
    /// <param name="result">When this method returns, contains the parsed <see cref="Glob"/> if parsing succeeded, or <see langword="null"/> if parsing failed.</param>
    /// <returns><see langword="true"/> if the pattern was parsed successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string pattern, GlobDialect dialect, GlobOptions options, [NotNullWhen(true)] out Glob? result)
    {
        return TryParse(pattern.AsSpan(), dialect, options, out result);
    }

    /// <summary>Attempts to parse a glob pattern string.</summary>
    /// <param name="pattern">The glob pattern to parse.</param>
    /// <param name="dialect">The glob pattern dialect to use.</param>
    /// <param name="options">Options for controlling pattern parsing behavior.</param>
    /// <param name="result">When this method returns, contains the parsed <see cref="Glob"/> if parsing succeeded, or <see langword="null"/> if parsing failed.</param>
    /// <returns><see langword="true"/> if the pattern was parsed successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> pattern, GlobDialect dialect, GlobOptions options, [NotNullWhen(true)] out Glob? result)
    {
        return TryParse(pattern, dialect, options, out result, out _);
    }

    private static bool TryParse(ReadOnlySpan<char> pattern, GlobDialect dialect, GlobOptions options, [NotNullWhen(true)] out Glob? result, [NotNullWhen(false)] out string? errorMessage)
    {
        return GlobParser.TryParse(pattern, dialect, options, out result, out errorMessage);
    }

    /// <summary>
    ///     Parses one line of gitignore content on behalf of <see cref="GlobCollection"/>, which excludes the whole
    ///     content of an excluded directory itself. An entry ending with a '/' therefore only has to match the
    ///     directory here, unlike the same pattern parsed on its own.
    /// </summary>
    internal static bool TryParseGitIgnoreEntry(ReadOnlySpan<char> pattern, [NotNullWhen(true)] out Glob? result)
    {
        return GlobParser.TryParse(pattern, GlobDialect.Git, GlobOptions.None, matchGitDirectoryContent: false, out result, out _);
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
        if (!_pathSeparatorAware && !filename.IsEmpty)
        {
            var path = directory.IsEmpty
                ? filename.ToString()
                : PathReader.IsPathSeparator(directory[^1]) ? directory.ToString() + filename.ToString() : directory.ToString() + '/' + filename.ToString();
            directory = path.AsSpan();
            filename = [];
        }

        var pathReader = new PathReader(directory, filename, itemType, _pathSeparatorAware);

        if (_matchType is not GlobMatchType.Any)
        {
            if (_matchType is GlobMatchType.File && pathReader.IsDirectory)
                return false;

            if (_matchType is GlobMatchType.Directory && !pathReader.IsDirectory)
                return false;
        }

        if (!_pathSeparatorAware)
        {
            // A pattern that is not path-separator aware matches a plain string, which may be empty, and it always
            // holds a single segment as no character separates segments. The path-oriented matcher below rejects an
            // empty path before reaching the segment, so match the segments directly. That dialect always allows
            // leading dots, so there is no per-segment check to run.
            foreach (var segment in _segments)
            {
                if (!segment.IsMatch(ref pathReader))
                    return false;
            }

            return pathReader.IsEndOfPath;
        }

        return IsMatchCore(pathReader, _segments, _segmentAnchors, _matchLeadingDot);
    }

    private static bool IsMatchCore(PathReader pathReader, ReadOnlySpan<Segment> patternSegments, ReadOnlySpan<SegmentAnchor?> anchors, ReadOnlySpan<bool> matchLeadingDot)
    {
        for (var i = 0; i < patternSegments.Length; i++)
        {
            var patternSegment = patternSegments[i];
            if (patternSegment is RecursiveMatchAllSegment)
            {
                var remainingPatternSegments = patternSegments[(i + 1)..];
                if (remainingPatternSegments.IsEmpty)
                {
                    // A trailing '**' matches the rest of the path. It must consume at least one segment, so 'a/**'
                    // matches the content of 'a' but not 'a' itself.
                    if (pathReader.IsEndOfPath)
                        return false;

                    while (!pathReader.IsEndOfPath)
                    {
                        if (!CanMatchLeadingDot(pathReader, matchLeadingDot[i]))
                            return false;

                        pathReader.ConsumeSegment();
                    }

                    return true;
                }

                var remainingAnchors = anchors[(i + 1)..];
                var remainingMatchLeadingDot = matchLeadingDot[(i + 1)..];

                // A gitignore entry such as 'abc/**/' ends with a '**' once its trailing '/' is set aside, and a
                // trailing '**' consumes at least one segment: the entry matches the directories inside 'abc'.
                if (remainingPatternSegments is [DirectoryContentSegment])
                {
                    if (pathReader.IsEndOfPath)
                        return false;
                }
                else if (IsMatchCore(pathReader, remainingPatternSegments, remainingAnchors, remainingMatchLeadingDot))
                {
                    return true;
                }

                var segmentAnchor = remainingAnchors[0];
                if (!CanMatchLeadingDot(pathReader, matchLeadingDot[i]))
                    return false;

                pathReader.ConsumeSegment();
                while (!pathReader.IsEndOfPath)
                {
                    if (segmentAnchor.HasValue && !segmentAnchor.GetValueOrDefault().Contains(pathReader.CurrentText))
                    {
                        if (!CanMatchLeadingDot(pathReader, matchLeadingDot[i]))
                            return false;

                        pathReader.ConsumeSegment();
                        continue;
                    }

                    if (IsMatchCore(pathReader, remainingPatternSegments, remainingAnchors, remainingMatchLeadingDot))
                        return true;

                    if (!CanMatchLeadingDot(pathReader, matchLeadingDot[i]))
                        return false;

                    pathReader.ConsumeSegment();
                }

                // The path is exhausted. Only a segment that can match an empty path is still worth trying: a
                // gitignore entry ending with a '/' matches the directory the path stops at.
                return IsMatchCore(pathReader, remainingPatternSegments, remainingAnchors, remainingMatchLeadingDot);
            }

            if (pathReader.IsEndOfPath && !patternSegment.CanMatchEmptyPath)
                return false;

            if (!CanMatchLeadingDot(pathReader, matchLeadingDot[i]))
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
        if (!_pathSeparatorAware)
            return true;

        return IsPartialMatchCore(new PathReader(folderPath, filename, itemType: null), _segments, _matchLeadingDot);
    }

    private static bool IsPartialMatchCore(PathReader pathReader, ReadOnlySpan<Segment> patternSegments, ReadOnlySpan<bool> matchLeadingDot)
    {
        for (var i = 0; i < patternSegments.Length; i++)
        {
            var patternSegment = patternSegments[i];
            if (ShouldRecurse(patternSegment))
            {
                if (IsPartialMatchCore(pathReader, patternSegments[(i + 1)..], matchLeadingDot[(i + 1)..]))
                    return true;

                return pathReader.IsEndOfPath || CanMatchLeadingDot(pathReader, matchLeadingDot[i]);
            }

            if (pathReader.IsEndOfPath)
                return true;

            if (!CanMatchLeadingDot(pathReader, matchLeadingDot[i]))
                return false;

            if (!patternSegment.IsMatch(ref pathReader))
                return false;

            // The segment must match the folder name as a whole: 'src' does not match the folder 'src2'.
            if (!pathReader.IsEndOfCurrentSegment)
                return false;

            pathReader.ConsumeEndOfSegment();
        }

        // The folder consumed every pattern segment, including the one that matches the file name, so no file below
        // it can match.
        return false;
    }

    private static bool CanMatchLeadingDot(PathReader pathReader, bool matchLeadingDot)
    {
        return matchLeadingDot || pathReader.CurrentSegment.IsEmpty || pathReader.CurrentSegment[0] != '.';
    }

    private static bool ShouldRecurse(Segment patternSegment)
    {
        return patternSegment.IsRecursiveMatchAll;
    }

    private static bool TryGetSegmentAnchor(Segment segment, out SegmentAnchor anchor)
    {
        var characters = GetAnchorCharacters(segment);
        if (characters is not null)
        {
            anchor = new SegmentAnchor(SearchValues.Create(characters));
            return true;
        }

        anchor = default;
        return false;

        // The anchor rejects a path segment before it is matched, so it must list every character the segment can
        // start with. Anything that cannot be enumerated exactly returns null and runs without an anchor.
        static char[]? GetAnchorCharacters(Segment segment)
        {
            return segment switch
            {
                LiteralSegment literal when literal.Value.Length > 0 => CreateCharacterSet([literal.Value[0]], literal.IgnoreCase),
                StartsWithSegment startsWith when startsWith.Value.Length > 0 => CreateCharacterSet([startsWith.Value[0]], startsWith.IgnoreCase),
                CharacterSetSegment set => CreateCharacterSet(set.Set.AsSpan(), set.IgnoreCase),
                CharacterRangeSegment range when range.Range.Length <= 8 => range.Range.EnumerateCharacters(),
                RaggedSegment ragged => ragged.FirstRequiredCharacters,
                _ => null,
            };
        }

        static char[]? CreateCharacterSet(ReadOnlySpan<char> characters, bool ignoreCase)
        {
            return IgnoreCaseExpansion.TryExpand(characters, ignoreCase, out var result) ? result : null;
        }
    }

    private readonly struct SegmentAnchor
    {
        private readonly SearchValues<char> _characters;

        public SegmentAnchor(SearchValues<char> characters)
        {
            _characters = characters;
        }

        public bool Contains(ReadOnlySpan<char> currentText)
        {
            return !currentText.IsEmpty && _characters.Contains(currentText[0]);
        }
    }

    /// <summary>Returns a pattern that parses back, with the same dialect and options, into a glob that matches the same paths.</summary>
    /// <returns>The pattern of this glob.</returns>
    public override string ToString()
    {
        var sb = new ValueStringBuilder(stackalloc char[128]);
        if (Mode is GlobMode.Exclude)
        {
            sb.Append('!');
        }

        var patternStart = sb.Length;
        for (var i = 0; i < _segments.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('/');
            }

            if (_segments[i] is LiteralSegment literal)
            {
                GlobPatternWriter.AppendPathSegmentLiteral(ref sb, literal.Value, _dialect);
            }
            else
            {
                _segments[i].AppendPattern(ref sb, _dialect);
            }
        }

        // A DirectoryContentSegment is written as an empty segment, which already ends the pattern with a '/'
        if (_matchType is GlobMatchType.Directory)
        {
            sb.Append('/');
        }

        if (_dialect is GlobDialect.Git && _segments[0] is not RecursiveMatchAllSegment && !ContainsSeparator(sb.AsSpan(patternStart), _segments[^1] is DirectoryContentSegment))
        {
            // gitignore matches an entry without a '/' at any depth, whereas this one is anchored to the root
            sb.Insert(patternStart, "/");
        }
        else if (_dialect is GlobDialect.Standard or GlobDialect.Git && Mode is GlobMode.Include && sb.Length > patternStart && sb[patternStart] == '!')
        {
            // A leading '!' would negate the pattern
            sb.Insert(patternStart, "\\");
        }

        return sb.ToString();

        static bool ContainsSeparator(ReadOnlySpan<char> pattern, bool endsWithDirectorySeparator)
        {
            if (endsWithDirectorySeparator)
            {
                pattern = pattern[..^1];
            }

            return pattern.Contains('/');
        }
    }

    internal static ReadOnlySpan<char> GetRelativeDirectory(ref FileSystemEntry entry)
    {
        if (entry.Directory.Length == entry.RootDirectory.Length)
            return [];

        return entry.Directory[(entry.RootDirectory.Length + 1)..];
    }
}
