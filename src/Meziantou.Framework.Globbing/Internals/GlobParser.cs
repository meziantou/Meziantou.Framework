using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Globbing.Internals;
using Meziantou.Framework.Globbing.Internals.Segments;

namespace Meziantou.Framework.Globbing;

internal static class GlobParser
{
    private static readonly char[] DirectorySeparator = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static bool TryParse(ReadOnlySpan<char> pattern, GlobDialect dialect, GlobOptions options, [NotNullWhen(true)] out Glob? result, [NotNullWhen(false)] out string? errorMessage)
    {
        return TryParse(pattern, dialect, options, matchGitDirectoryContent: true, out result, out errorMessage);
    }

    /// <param name="matchGitDirectoryContent">
    ///     Whether a gitignore entry ending with a '/' also matches the paths below the directory. A standalone glob
    ///     has to, as it is the only rule the caller evaluates, but a <see cref="GlobCollection"/> built from
    ///     gitignore content resolves an excluded ancestor directory on its own and passes <see langword="false"/> so
    ///     that the same path is not excluded twice, which would let a directory entry outrank a later negation.
    /// </param>
    public static bool TryParse(ReadOnlySpan<char> pattern, GlobDialect dialect, GlobOptions options, bool matchGitDirectoryContent, [NotNullWhen(true)] out Glob? result, [NotNullWhen(false)] out string? errorMessage)
    {
        result = null;
        if (pattern.IsEmpty)
        {
            errorMessage = "The pattern is empty";
            return false;
        }

        var settings = new GlobParserSettings(dialect, options);

        var exclude = false;
        if (settings.SupportsLeadingExclude && pattern[0] == '!')
        {
            exclude = true;
            pattern = pattern[1..];
        }

        var segments = new List<Segment>();
        var matchLeadingDot = new List<bool>();
        var matchType = settings.PathSeparatorAware ? GlobMatchType.File : GlobMatchType.Any;
        var gitMustBeDirectory = false;

        if (dialect is GlobDialect.Git)
        {
            // gitignore(5) reads an entry in this order: a trailing '/' restricts it to directories, an entry without
            // any other '/' matches a name at any depth, and a leading '/' only anchors the entry to the root.
            if (!pattern.IsEmpty && pattern[^1] == '/')
            {
                gitMustBeDirectory = true;
                pattern = pattern[..^1];

                // What is left must match a whole name, and no name ends with a '/'
                if (!pattern.IsEmpty && pattern[^1] == '/')
                {
                    errorMessage = "the pattern contains an empty path segment, which no path can match";
                    return false;
                }
            }

            if (pattern.IndexOf('/') < 0)
            {
                segments.Add(RecursiveMatchAllSegment.Instance);
                matchLeadingDot.Add(settings.MatchLeadingDot);
            }
            else if (pattern[0] == '/')
            {
                pattern = pattern[1..];
            }

            // A gitignore entry matches a file or a directory with that name. Whether the item must be a directory
            // is decided by DirectoryContentSegment, which knows where the path ends.
            matchType = GlobMatchType.Any;
        }
        else if (settings.PathSeparatorAware && !pattern.IsEmpty && settings.IsPatternSeparator(pattern[^1]))
        {
            matchType = GlobMatchType.Directory;
        }

        if (pattern.IsEmpty)
        {
            errorMessage = "the pattern does not contain any segment";
            return false;
        }

        List<Segment>? subSegments = null;
        List<string>? setSubsegment = null;
        var currentSegmentMatchLeadingDot = settings.MatchLeadingDot;
        var parserContext = GlobParserContext.Segment;
        var isAtPatternStart = true;

        Span<char> sbSpan = stackalloc char[128];
        var currentLiteral = new ValueStringBuilder(sbSpan);
        try
        {
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (parserContext == GlobParserContext.LiteralSet)
                {
                    Debug.Assert(setSubsegment is not null);
                    if (c == '\\')
                    {
                        if (i + 1 >= pattern.Length)
                        {
                            errorMessage = "Expecting a character after '\\'";
                            return false;
                        }

                        i++;
                        currentLiteral.Append(pattern[i]);
                    }
                    else if (c == ',') // end of current value
                    {
                        setSubsegment.Add(currentLiteral.AsSpan().ToString());
                        currentLiteral.Clear();
                    }
                    else if (c == '}') // end of literal set
                    {
                        setSubsegment.Add(currentLiteral.AsSpan().ToString());
                        currentLiteral.Clear();

                        if (settings.PathSeparatorAware && setSubsegment.Exists(s => s.IndexOfAny(DirectorySeparator) >= 0))
                        {
                            errorMessage = "set contains a path separator";
                            return false;
                        }

                        AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, new LiteralSetSegment(setSubsegment.ToArray(), settings.IgnoreCase));
                        setSubsegment = null;
                        parserContext = GlobParserContext.Segment;
                    }
                    else
                    {
                        currentLiteral.Append(c);
                    }

                    continue;
                }

                // An escaped separator ('\/', or '%2F' for MSBuild) still separates two segments: escaping an
                // ordinary character yields the character itself.
                var separatorLength = GetSeparatorLength(pattern, i, settings);
                if (separatorLength > 0)
                {
                    if (!TryFinishSegmentAtSeparator(segments, matchLeadingDot, ref subSegments, ref currentLiteral, settings, currentSegmentMatchLeadingDot, isAtPatternStart, out errorMessage))
                        return false;

                    currentSegmentMatchLeadingDot = settings.MatchLeadingDot;
                    i += separatorLength - 1;
                    continue;
                }

                isAtPatternStart = false;
                if (dialect is GlobDialect.MSBuild && TryDecodeMsBuildEscape(pattern, i, out var escapedCharacter))
                {
                    AppendLiteral(ref currentLiteral, ref currentSegmentMatchLeadingDot, subSegments, escapedCharacter, settings.MatchLeadingDot);
                    i += 2;
                    continue;
                }

                if (c == '\\' && settings.SupportsEscape)
                {
                    if (i + 1 >= pattern.Length)
                    {
                        errorMessage = "Expecting a character after '\\'";
                        return false;
                    }

                    i++;
                    AppendLiteral(ref currentLiteral, ref currentSegmentMatchLeadingDot, subSegments, pattern[i], settings.MatchLeadingDot);
                    continue;
                }

                var isAtSegmentStart = subSegments is null && currentLiteral.Length == 0;
                if (c == '.' && isAtSegmentStart && settings.NormalizeDotSegments)
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '.' && IsEndOfSegment(pattern, i + 2, settings))
                    {
                        if (!TryApplyParentSegment(segments, matchLeadingDot, settings, out errorMessage))
                            return false;

                        // Skip the second '.' and the separator that follows it
                        i += 1 + GetSeparatorLength(pattern, i + 2, settings);
                        continue;
                    }

                    if (IsEndOfSegment(pattern, i + 1, settings))
                    {
                        // Skip the separator that follows the '.'
                        i += GetSeparatorLength(pattern, i + 1, settings);
                        continue;
                    }
                }

                switch (c)
                {
                    case '?':
                        AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, settings.AnyCharacterSegment);
                        break;

                    case '*':
                        var starCount = 1;
                        while (i + starCount < pattern.Length && pattern[i + starCount] == '*')
                        {
                            starCount++;
                        }

                        var isWholeSegment = isAtSegmentStart && IsEndOfSegment(pattern, i + starCount, settings);
                        if (dialect is GlobDialect.MSBuild && starCount > 1 && !(isWholeSegment && starCount == 2))
                        {
                            errorMessage = "the recursive wildcard '**' must be its own path segment";
                            return false;
                        }

                        if (isWholeSegment)
                        {
                            if (starCount > 1 && settings.SupportsRecursiveWildcard && (starCount == 2 || settings.ReadsStarRunAsRecursiveWildcard))
                            {
                                // Merge two consecutive '**' (**/**)
                                if (segments.Count == 0 || segments[^1] is not RecursiveMatchAllSegment)
                                {
                                    segments.Add(RecursiveMatchAllSegment.Instance);
                                    matchLeadingDot.Add(settings.MatchLeadingDot);
                                }
                            }
                            else
                            {
                                segments.Add(MatchAllSegment.Instance);
                                matchLeadingDot.Add(currentSegmentMatchLeadingDot);
                            }

                            // Skip the other stars and the separator that follows them
                            i += starCount - 1 + GetSeparatorLength(pattern, i + starCount, settings);
                            currentSegmentMatchLeadingDot = settings.MatchLeadingDot;
                            break;
                        }

                        // MSBuild reads a file name made of "*.*" as every file, including the ones without an extension
                        if (dialect is GlobDialect.MSBuild && isAtSegmentStart && pattern[i..].SequenceEqual("*.*"))
                        {
                            segments.Add(MatchAllSegment.Instance);
                            matchLeadingDot.Add(currentSegmentMatchLeadingDot);
                            i += 2;
                            break;
                        }

                        i += starCount - 1;

                        // Merge consecutive '*'
                        if (currentLiteral.Length == 0 && subSegments is not null && subSegments.Count > 0 && subSegments[^1] is MatchAllSubSegment)
                            break;

                        AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, MatchAllSubSegment.Instance);
                        break;

                    case '{' when settings.SupportsLiteralSet:
                        Debug.Assert(setSubsegment is null);
                        AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, subSegment: null);
                        parserContext = GlobParserContext.LiteralSet;
                        setSubsegment = [];
                        break;

                    case '[' when settings.SupportsBracketExpression:
                        switch (ParseBracketExpression(pattern, i, settings, out var bracketExpression, out var bracketExpressionEnd, out errorMessage))
                        {
                            case BracketExpressionParseResult.Parsed:
                                AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, bracketExpression);
                                i = bracketExpressionEnd - 1;
                                break;

                            case BracketExpressionParseResult.NotABracketExpression:
                                AppendLiteral(ref currentLiteral, ref currentSegmentMatchLeadingDot, subSegments, c, settings.MatchLeadingDot);
                                break;

                            default:
                                Debug.Assert(errorMessage is not null);
                                return false;
                        }

                        break;

                    default:
                        AppendLiteral(ref currentLiteral, ref currentSegmentMatchLeadingDot, subSegments, c, settings.MatchLeadingDot);
                        break;
                }
            }

            if (parserContext != GlobParserContext.Segment)
            {
                errorMessage = $"The '{parserContext}' is not complete";
                return false;
            }

            FinishSegment(segments, matchLeadingDot, ref subSegments, ref currentLiteral, settings.IgnoreCase, currentSegmentMatchLeadingDot, settings.PathSeparatorAware);

            // '.' and '..' normalization can remove every segment (".", "./", "a/.."). Such a pattern cannot match
            // anything, and a Glob without any segment is not usable, so reject it instead of returning it. A pattern
            // that is only made of separators is rejected as well.
            if (segments.TrueForAll(segment => segment is EmptySegment))
            {
                errorMessage = "the pattern does not contain any segment";
                return false;
            }

            if (gitMustBeDirectory)
            {
                // A gitignore entry ending with a '/' matches the directory itself. Excluding a directory also
                // excludes its content, but re-including one does not re-include its content, so a negated entry
                // never matches the paths below the directory.
                segments.Add(matchGitDirectoryContent && !exclude ? DirectoryContentSegment.IncludingContent : DirectoryContentSegment.DirectoryOnly);
                matchLeadingDot.Add(settings.MatchLeadingDot);
            }

            errorMessage = null;
            result = CreateGlob(segments, matchLeadingDot, exclude, settings.IgnoreCase, matchType, settings.MatchLeadingDot, settings.PathSeparatorAware);
            return true;
        }
        finally
        {
            currentLiteral.Dispose();
        }
    }

    private static void AddSubsegment(ref List<Segment>? subSegments, ref ValueStringBuilder currentLiteral, bool ignoreCase, Segment? subSegment)
    {
        subSegments ??= [];
        if (currentLiteral.Length > 0)
        {
            subSegments.Add(new LiteralSegment(currentLiteral.AsSpan().ToString(), ignoreCase));
            currentLiteral.Clear();
        }

        if (subSegment is not null)
        {
            subSegments.Add(subSegment);
        }
    }

    private static void FinishSegment(List<Segment> segments, List<bool> matchLeadingDot, ref List<Segment>? subSegments, ref ValueStringBuilder currentLiteral, bool ignoreCase, bool currentSegmentMatchLeadingDot, bool pathSeparatorAware)
    {
        if (subSegments is not null)
        {
            if (currentLiteral.Length > 0)
            {
                subSegments.Add(new LiteralSegment(currentLiteral.AsSpan().ToString(), ignoreCase));
                currentLiteral.Clear();
            }

            segments.Add(CreateSegment(subSegments, ignoreCase, pathSeparatorAware));
            matchLeadingDot.Add(currentSegmentMatchLeadingDot);
            subSegments = null;
        }
        else if (currentLiteral.Length > 0)
        {
            segments.Add(new LiteralSegment(currentLiteral.AsSpan().ToString(), ignoreCase));
            matchLeadingDot.Add(currentSegmentMatchLeadingDot);
            currentLiteral.Clear();
        }
    }

    /// <param name="isAtPatternStart">Whether only separators precede the separator, which makes the pattern an absolute path.</param>
    private static bool TryFinishSegmentAtSeparator(List<Segment> segments, List<bool> matchLeadingDot, ref List<Segment>? subSegments, ref ValueStringBuilder currentLiteral, GlobParserSettings settings, bool currentSegmentMatchLeadingDot, bool isAtPatternStart, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = null;
        if (subSegments is not null || currentLiteral.Length > 0)
        {
            FinishSegment(segments, matchLeadingDot, ref subSegments, ref currentLiteral, settings.IgnoreCase, currentSegmentMatchLeadingDot, settings.PathSeparatorAware);
            return true;
        }

        // The segment before the separator is empty: the pattern starts with a separator, or holds two consecutive ones
        switch (settings.EmptySegmentHandling)
        {
            case EmptySegmentHandling.Match:
                segments.Add(EmptySegment.Instance);
                matchLeadingDot.Add(true);
                break;

            case EmptySegmentHandling.MatchLeading when isAtPatternStart:
                segments.Add(EmptySegment.Instance);
                matchLeadingDot.Add(true);
                break;

            case EmptySegmentHandling.Reject:
                errorMessage = "the pattern contains an empty path segment, which no path can match";
                return false;
        }

        return true;
    }

    private static bool TryApplyParentSegment(List<Segment> segments, List<bool> matchLeadingDot, GlobParserSettings settings, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = null;
        if (settings.Dialect is GlobDialect.MSBuild)
        {
            // MSBuild does not accept a '..' after the first wildcard of a file spec
            if (segments.Exists(segment => segment is not (LiteralSegment or EmptySegment)))
            {
                errorMessage = "the pattern cannot contain '..' after a wildcard";
                return false;
            }

            // MSBuild resolves a leading '..' against the project directory: it cannot be normalized away, so it stays
            // a literal segment that matches a relative path starting with '..'.
            if (segments.Count == 0 || segments[^1] is EmptySegment or LiteralSegment { Value: ".." })
            {
                segments.Add(new LiteralSegment("..", settings.IgnoreCase));
                matchLeadingDot.Add(true);
                return true;
            }
        }
        else
        {
            if (segments.Count == 0)
            {
                errorMessage = "the pattern cannot start with '..'";
                return false;
            }

            if (segments[^1] is RecursiveMatchAllSegment)
            {
                errorMessage = "the pattern cannot contain '..' after a '**'";
                return false;
            }
        }

        segments.RemoveAt(segments.Count - 1);
        matchLeadingDot.RemoveAt(matchLeadingDot.Count - 1);
        return true;
    }

    /// <summary>
    ///     Returns the number of characters of the path separator at <paramref name="index"/>, or 0 when there is none.
    ///     An escaped separator ('\/', or '%2F' for MSBuild) is a separator too, as escaping an ordinary character
    ///     yields the character itself.
    /// </summary>
    private static int GetSeparatorLength(ReadOnlySpan<char> pattern, int index, GlobParserSettings settings)
    {
        if (index >= pattern.Length)
            return 0;

        var c = pattern[index];
        if (settings.IsPatternSeparator(c))
            return 1;

        if (c == '\\' && settings.SupportsEscape && index + 1 < pattern.Length && settings.IsPatternSeparator(pattern[index + 1]))
            return 2;

        if (settings.Dialect is GlobDialect.MSBuild && TryDecodeMsBuildEscape(pattern, index, out var decoded) && settings.IsPatternSeparator(decoded))
            return 3;

        return 0;
    }

    private static bool IsEndOfSegment(ReadOnlySpan<char> pattern, int index, GlobParserSettings settings)
    {
        return index >= pattern.Length || GetSeparatorLength(pattern, index, settings) > 0;
    }

    // canSkipLeadingDotChecks is settings.MatchLeadingDot. The rewrites below collapse a '**' and the segments that
    // follow it into a single segment that jumps to the end of the path, which skips the per-segment
    // CanMatchLeadingDot checks Glob.IsMatchCore would otherwise run - that is why each one records 'true' in
    // matchLeadingDot. They are only sound when leading dots are allowed everywhere, so do not loosen this gate
    // without giving the rewritten segments a way to reject a segment that starts with a dot.
    private static Glob CreateGlob(List<Segment> segments, List<bool> matchLeadingDot, bool exclude, bool ignoreCase, GlobMatchType matchType, bool canSkipLeadingDotChecks, bool pathSeparatorAware)
    {
        // Optimize segments
        if (canSkipLeadingDotChecks && segments.Count >= 3)
        {
            for (var i = segments.Count - 3; i >= 0; i--)
            {
                if (segments[i] is not RecursiveMatchAllSegment)
                    continue;

                var suffixLength = segments.Count - i - 1;
                if (suffixLength < 2)
                    continue;

                var suffix = new List<string>(suffixLength);
                var isFixedSuffix = true;
                for (var j = 0; j < suffixLength; j++)
                {
                    if (segments[i + j + 1] is LiteralSegment literal)
                    {
                        suffix.Add(literal.Value);
                    }
                    else
                    {
                        isFixedSuffix = false;
                        break;
                    }
                }

                if (isFixedSuffix)
                {
                    segments.RemoveRange(i, suffixLength + 1);
                    matchLeadingDot.RemoveRange(i, suffixLength + 1);
                    segments.Insert(i, new PathSuffixSegment([.. suffix], ignoreCase));
                    matchLeadingDot.Insert(i, true);
                    break;
                }
            }
        }

        if (canSkipLeadingDotChecks && segments.Count >= 2)
        {
            if (segments[^2] is RecursiveMatchAllSegment && segments[^1] is MatchAllSegment) // **/*
            {
                var lastSegment = MatchNonEmptyTextSegment.Instance;
                segments.RemoveRange(segments.Count - 2, 2);
                matchLeadingDot.RemoveRange(matchLeadingDot.Count - 2, 2);
                segments.Add(lastSegment);
                matchLeadingDot.Add(true);
            }
            else if (segments[^2] is RecursiveMatchAllSegment && segments[^1] is EndsWithSegment endsWith) // **/*.txt
            {
                var lastSegment = new MatchAllEndsWithSegment(endsWith.Value, ignoreCase);
                segments.RemoveRange(segments.Count - 2, 2);
                matchLeadingDot.RemoveRange(matchLeadingDot.Count - 2, 2);
                segments.Add(lastSegment);
                matchLeadingDot.Add(true);
            }
            else if (segments[^2] is RecursiveMatchAllSegment && !segments[^1].IsRecursiveMatchAll) // **/segment
            {
                var lastSegment = new LastSegment(segments[^1]);
                segments.RemoveRange(segments.Count - 2, 2);
                matchLeadingDot.RemoveRange(matchLeadingDot.Count - 2, 2);
                segments.Add(lastSegment);
                matchLeadingDot.Add(true);
            }
        }

        return new Glob([.. segments], [.. matchLeadingDot], pathSeparatorAware, exclude ? GlobMode.Exclude : GlobMode.Include, matchType);
    }

    private static void AppendLiteral(ref ValueStringBuilder currentLiteral, ref bool currentSegmentMatchLeadingDot, List<Segment>? subSegments, char c, bool defaultMatchLeadingDot)
    {
        if (!defaultMatchLeadingDot && c == '.' && subSegments is null && currentLiteral.Length == 0)
        {
            currentSegmentMatchLeadingDot = true;
        }

        currentLiteral.Append(c);
    }

    private static bool TryDecodeMsBuildEscape(ReadOnlySpan<char> pattern, int index, out char c)
    {
        if (pattern[index] == '%' && index + 2 < pattern.Length && TryGetHexValue(pattern[index + 1], out var high) && TryGetHexValue(pattern[index + 2], out var low))
        {
            c = (char)((high * 16) + low);
            return true;
        }

        c = '\0';
        return false;

        static bool TryGetHexValue(char c, out int result)
        {
            if (c is >= '0' and <= '9')
            {
                result = c - '0';
                return true;
            }

            if (c is >= 'a' and <= 'f')
            {
                result = c - 'a' + 10;
                return true;
            }

            if (c is >= 'A' and <= 'F')
            {
                result = c - 'A' + 10;
                return true;
            }

            result = 0;
            return false;
        }
    }

    /// <summary>Parses the bracket expression whose '[' is at <paramref name="start"/>.</summary>
    /// <param name="end">The index of the character that follows the closing ']'.</param>
    private static BracketExpressionParseResult ParseBracketExpression(ReadOnlySpan<char> pattern, int start, GlobParserSettings settings, out Segment? segment, out int end, out string? errorMessage)
    {
        segment = null;
        end = start;
        errorMessage = null;

        var ranges = new List<CharacterRange>();
        List<NamedCharacterClass>? classes = null;

        var i = start + 1;
        var inverse = i < pattern.Length && (pattern[i] == '!' || (pattern[i] == '^' && settings.SupportsCaretNegation));
        if (inverse)
        {
            i++;
        }

        // A ']' that comes first is an ordinary character: '[]a]' matches ']' or 'a'
        var isFirst = true;
        while (true)
        {
            if (i >= pattern.Length)
            {
                // POSIX reads a '[' that does not open a complete bracket expression as an ordinary character
                if (settings.ReadsUnterminatedBracketAsLiteral)
                    return BracketExpressionParseResult.NotABracketExpression;

                errorMessage = "The bracket expression is not complete";
                return BracketExpressionParseResult.Invalid;
            }

            var c = pattern[i];
            if (c == ']' && !isFirst)
            {
                i++;
                break;
            }

            isFirst = false;

            if (c == '[' && i + 1 < pattern.Length)
            {
                if (pattern[i + 1] == ':' && settings.SupportsNamedCharacterClass)
                {
                    var classResult = TryReadNamedCharacterClass(pattern, i, settings.Dialect, out var namedCharacterClass, out var length);
                    if (classResult is BracketExpressionParseResult.Parsed)
                    {
                        // A class cannot start a range, so a '-' that follows it is an ordinary character
                        classes ??= [];
                        classes.Add(namedCharacterClass);
                        i += length;
                        continue;
                    }

                    if (classResult is BracketExpressionParseResult.Invalid)
                    {
                        errorMessage = "The bracket expression contains an unknown character class";
                        return BracketExpressionParseResult.Invalid;
                    }
                }
                else if (pattern[i + 1] == '=' && settings.SupportsEquivalenceClass)
                {
                    // An equivalence class holds a single character in the POSIX locale. As for a class, it cannot
                    // start a range. Anything but "[=c=]" leaves the '[' as an ordinary character.
                    if (i + 4 < pattern.Length && pattern[i + 3] == '=' && pattern[i + 4] == ']')
                    {
                        ranges.Add(new CharacterRange(pattern[i + 2]));
                        i += 5;
                        continue;
                    }
                }
            }

            if (!TryReadBracketCharacter(pattern, ref i, settings, out var rangeStart, out errorMessage))
                return BracketExpressionParseResult.Invalid;

            // A '-' that comes last is an ordinary character: '[a-]' matches 'a' or '-'
            if (i + 1 < pattern.Length && pattern[i] == '-' && pattern[i + 1] != ']')
            {
                i++;
                if (!TryReadBracketCharacter(pattern, ref i, settings, out var rangeEnd, out errorMessage))
                    return BracketExpressionParseResult.Invalid;

                if (rangeStart > rangeEnd)
                {
                    switch (settings.ReversedRangeHandling)
                    {
                        case ReversedRangeHandling.Reject:
                            errorMessage = $"Invalid range '{rangeStart}' > '{rangeEnd}'";
                            return BracketExpressionParseResult.Invalid;

                        case ReversedRangeHandling.MatchStart:
                            // wildmatch compares the character before it sees the '-', so it still matches the start
                            ranges.Add(new CharacterRange(rangeStart));
                            break;
                    }

                    // glibc and the BSD libc read a reversed range as a range that contains no character
                    continue;
                }

                ranges.Add(new CharacterRange(rangeStart, rangeEnd));
            }
            else
            {
                ranges.Add(new CharacterRange(rangeStart));
            }
        }

        // The dialects that follow a specification accept a separator in a bracket expression, which simply never
        // matches as a path segment does not contain any separator.
        if (settings.RejectsSeparatorInBracketExpression && ranges.Exists(range => range.IsInRange(Path.DirectorySeparatorChar) || range.IsInRange(Path.AltDirectorySeparatorChar)))
        {
            errorMessage = "range contains a path separator";
            return BracketExpressionParseResult.Invalid;
        }

        segment = CreateRangeSubsegment(ranges, classes, inverse, settings.IgnoreCase);
        end = i;
        return BracketExpressionParseResult.Parsed;
    }

    /// <summary>Reads a character of a bracket expression that can be a range bound: an ordinary character, an escaped character or a collating symbol.</summary>
    private static bool TryReadBracketCharacter(ReadOnlySpan<char> pattern, ref int index, GlobParserSettings settings, out char value, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = null;
        var c = pattern[index];
        if (c == '\\' && settings.SupportsEscapeInBracketExpression)
        {
            if (index + 1 >= pattern.Length)
            {
                value = default;
                errorMessage = "Expecting a character after '\\'";
                return false;
            }

            value = pattern[index + 1];
            index += 2;
            return true;
        }

        if (c == '[' && settings.SupportsCollatingSymbol && index + 1 < pattern.Length && pattern[index + 1] == '.')
        {
            // A collating symbol, such as "[.-.]". Its content is read as is, without any escape sequence.
            var length = pattern[(index + 2)..].IndexOf(".]", StringComparison.Ordinal);
            if (length != 1)
            {
                value = default;
                errorMessage = length < 0 ? "The collating symbol is not complete" : "Only collating symbols made of a single character are supported";
                return false;
            }

            value = pattern[index + 2];
            index += 5;
            return true;
        }

        value = c;
        index++;
        return true;
    }

    /// <summary>Reads the character class, such as "[:alpha:]", whose '[' is at <paramref name="index"/> and followed by a ':'.</summary>
    /// <returns>
    ///     <see cref="BracketExpressionParseResult.NotABracketExpression"/> when the text is not a class, in which case
    ///     the '[' is an ordinary character, and <see cref="BracketExpressionParseResult.Invalid"/> when it names an
    ///     unknown class, which makes the pattern unable to match anything.
    /// </returns>
    private static BracketExpressionParseResult TryReadNamedCharacterClass(ReadOnlySpan<char> pattern, int index, GlobDialect dialect, out NamedCharacterClass result, out int length)
    {
        result = default;
        length = 0;

        var nameStart = index + 2;
        int nameEnd;
        if (dialect is GlobDialect.Git)
        {
            // wildmatch reads up to the next ']', and only sees a class when that ']' follows a ':'
            var closingBracket = pattern[nameStart..].IndexOf(']');
            if (closingBracket < 1 || pattern[nameStart + closingBracket - 1] != ':')
                return BracketExpressionParseResult.NotABracketExpression;

            nameEnd = nameStart + closingBracket - 1;
        }
        else
        {
            // glibc only reads lowercase letters as a class name
            nameEnd = nameStart;
            while (true)
            {
                if (nameEnd + 1 >= pattern.Length)
                    return BracketExpressionParseResult.NotABracketExpression;

                if (pattern[nameEnd] == ':' && pattern[nameEnd + 1] == ']')
                    break;

                if (pattern[nameEnd] is < 'a' or > 'z')
                    return BracketExpressionParseResult.NotABracketExpression;

                nameEnd++;
            }
        }

        switch (pattern[nameStart..nameEnd])
        {
            case "alnum": result = NamedCharacterClass.Alnum; break;
            case "alpha": result = NamedCharacterClass.Alpha; break;
            case "blank": result = NamedCharacterClass.Blank; break;
            case "cntrl": result = NamedCharacterClass.Cntrl; break;
            case "digit": result = NamedCharacterClass.Digit; break;
            case "graph": result = NamedCharacterClass.Graph; break;
            case "lower": result = NamedCharacterClass.Lower; break;
            case "print": result = NamedCharacterClass.Print; break;
            case "punct": result = NamedCharacterClass.Punct; break;
            case "space": result = NamedCharacterClass.Space; break;
            case "upper": result = NamedCharacterClass.Upper; break;
            case "xdigit": result = NamedCharacterClass.XDigit; break;
            default: return BracketExpressionParseResult.Invalid;
        }

        length = nameEnd + 2 - index;
        return BracketExpressionParseResult.Parsed;
    }

    private static Segment CreateRangeSubsegment(List<CharacterRange> ranges, List<NamedCharacterClass>? classes, bool inverse, bool ignoreCase)
    {
        List<char>? singleCharRanges = null;
        List<CharacterRange>? rangeCharRanges = null;
        foreach (var range in ranges)
        {
            if (range.IsSingleCharacterRange)
            {
                singleCharRanges ??= [];
                singleCharRanges.Add(range.Min);
            }
            else
            {
                rangeCharRanges ??= [];
                rangeCharRanges.Add(range);
            }
        }

        if (classes is null)
        {
            if (singleCharRanges is not null)
            {
                if (rangeCharRanges is null)
                    return CreateCharacterSet([.. singleCharRanges], inverse, ignoreCase);
            }
            else if (rangeCharRanges is not null && rangeCharRanges.Count == 1)
            {
                return CreateCharacterRange(rangeCharRanges[0], ignoreCase, inverse);
            }
        }
        else if (!inverse && singleCharRanges is null && rangeCharRanges is null && classes.Count == 1)
        {
            return new CharacterClassSegment(classes[0], ignoreCase);
        }

        // Inverse flags is set on the combination
        var segments = new List<Segment>(
            (rangeCharRanges?.Count ?? 0) + (singleCharRanges is null ? 0 : 1) + (classes?.Count ?? 0));
        if (singleCharRanges is not null)
        {
            segments.Add(CreateCharacterSet([.. singleCharRanges], inverse: false, ignoreCase));
        }

        if (rangeCharRanges is not null)
        {
            foreach (var range in rangeCharRanges)
            {
                segments.Add(CreateCharacterRange(range, ignoreCase, inverse: false));
            }
        }

        if (classes is not null)
        {
            foreach (var characterClass in classes)
            {
                segments.Add(new CharacterClassSegment(characterClass, ignoreCase));
            }
        }

        return new OrSegment([.. segments], inverse);
    }

    private static Segment CreateCharacterSet(char[] set, bool inverse, bool ignoreCase)
    {
        return inverse ? new CharacterSetInverseSegment(new string(set), ignoreCase) : new CharacterSetSegment(new string(set), ignoreCase);
    }

    private static Segment CreateCharacterRange(CharacterRange range, bool ignoreCase, bool inverse)
    {
        return (ignoreCase, inverse) switch
        {
            (ignoreCase: false, inverse: false) => new CharacterRangeSegment(range),
            (ignoreCase: false, inverse: true) => new CharacterRangeInverseSegment(range),
            (ignoreCase: true, inverse: false) => new CharacterRangeIgnoreCaseSegment(range),
            (ignoreCase: true, inverse: true) => new CharacterRangeIgnoreCaseInverseSegment(range),
        };
    }

    private static Segment CreateSegment(List<Segment> parts, bool ignoreCase, bool pathSeparatorAware)
    {
        Debug.Assert(parts.Count > 0);

        // Concat Literal and single character sets (abc[d])
        for (var i = parts.Count - 2; i >= 0; i--)
        {
            var s1 = GetString(parts[i], pathSeparatorAware);
            var s2 = GetString(parts[i + 1], pathSeparatorAware);

            if (s1 is null || s2 is null)
                continue;

            // Merge
            var literal = new LiteralSegment(s1 + s2, ignoreCase);
            parts[i] = literal;
            parts.RemoveAt(i + 1);

            // A path segment never contains a separator, so '[/]' never matches, whereas a literal holding a
            // separator would read past the end of the segment.
            static string? GetString(Segment segment, bool pathSeparatorAware)
            {
                return segment switch
                {
                    LiteralSegment literal => literal.Value,
                    CharacterSetSegment set when set.Set.Length == 1 && !(pathSeparatorAware && PathReader.IsPathSeparator(set.Set[0])) => set.Set,
                    _ => null,
                };
            }
        }

        // Try to optimize common cases
        if (parts.Count == 2)
        {
            // Starts with: test.*
            if (parts[1] is MatchAllSubSegment && parts[0] is LiteralSegment startsWithLiteral)
                return new StartsWithSegment(startsWithLiteral.Value, ignoreCase);
        }

        if (parts.Count > 2)
        {
            if (parts.Count > 2 && parts[^1] is MatchAllSubSegment && parts[^3] is MatchAllSubSegment && parts[^2] is LiteralSegment containsLiteral) // Contains: *test*
            {
                parts.RemoveRange(parts.Count - 3, 3);
                parts.Add(new ContainsSegment(containsLiteral.Value, ignoreCase));
            }
        }

        if (parts.Count >= 2)
        {
            if (parts[^2] is MatchAllSubSegment && parts[^1] is LiteralSegment endsWithLiteral) // Ends with: *.txt
            {
                parts.RemoveRange(parts.Count - 2, 2);
                parts.Add(new EndsWithSegment(endsWithLiteral.Value, ignoreCase));
            }

            // *(pattern) => Check if the first character is known and easily validatable
            // /, \, Literal[0], CharacterSet [abc], CharacterRange [a-z] if interval is small (<=5), LiteralSet {abc,def}
            for (var i = 0; i < parts.Count - 1; i++)
            {
                if (parts[i] is MatchAllSubSegment)
                {
                    var next = parts[i + 1];
                    List<char>? nextCharacters = null;
                    switch (next)
                    {
                        case LiteralSegment literal when literal.Value.Length > 0:
                            nextCharacters = [literal.Value[0]];
                            break;

                        case CharacterSetSegment characterSet:
                            nextCharacters = [];
                            foreach (var character in characterSet.Set)
                            {
                                nextCharacters.Add(character);
                            }

                            break;

                        case CharacterRangeSegment characterRange when characterRange.Range.Length < 3:
                            nextCharacters = [.. characterRange.Range.EnumerateCharacters()];
                            break;

                        case LiteralSetSegment literalSet:
                            nextCharacters = [];
                            foreach (var value in literalSet.Values)
                            {
                                // An empty alternative consumes nothing, so the next character is the one the rest
                                // of the pattern requires and no character can be required here.
                                if (value.Length == 0)
                                {
                                    nextCharacters = null;
                                    break;
                                }

                                nextCharacters.Add(value[0]);
                            }

                            break;
                    }

                    // The segment skips ahead to the next character the following subsegment could match, so it is
                    // only sound when every one of those characters is known.
                    if (nextCharacters is not null && IgnoreCaseExpansion.TryExpand(CollectionsMarshal.AsSpan(nextCharacters), ignoreCase, out var expandedCharacters))
                    {
                        var stopCharacters = new List<char>(expandedCharacters);
                        if (pathSeparatorAware)
                        {
                            stopCharacters.Add(Path.DirectorySeparatorChar);
                            if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar)
                            {
                                stopCharacters.Add(Path.AltDirectorySeparatorChar);
                            }
                        }

                        parts.Insert(i, new ConsumeSegmentUntilSegment([.. stopCharacters]));
                        i++;
                    }
                }
            }
        }

        if (parts[^1] is MatchAllSubSegment)
        {
            parts[^1] = MatchAllEndOfSegment.Instance;
        }

        // A literal set is a branch point that only the backtracking matcher in RaggedSegment can resolve, so it
        // never becomes a standalone segment.
        if (parts.Count == 1 && parts[0] is not LiteralSetSegment)
            return parts[0];

        return new RaggedSegment(parts.ToArray());
    }

    private enum BracketExpressionParseResult
    {
        Parsed,
        NotABracketExpression,
        Invalid,
    }

    /// <summary>How a bracket expression reads a range whose start comes after its end, such as "[z-a]".</summary>
    private enum ReversedRangeHandling
    {
        /// <summary>The pattern is invalid.</summary>
        Reject,

        /// <summary>The range contains no character.</summary>
        Empty,

        /// <summary>The range only contains its start.</summary>
        MatchStart,
    }

    /// <summary>How a pattern reads an empty path segment, which comes before a leading separator or between two consecutive ones.</summary>
    private enum EmptySegmentHandling
    {
        /// <summary>The separators are collapsed.</summary>
        Ignore,

        /// <summary>The segment only matches an empty path segment.</summary>
        Match,

        /// <summary>A leading separator makes the pattern absolute, and the other separators are collapsed.</summary>
        MatchLeading,

        /// <summary>No path can match the pattern.</summary>
        Reject,
    }

    private readonly struct GlobParserSettings
    {
        public GlobParserSettings(GlobDialect dialect, GlobOptions options)
        {
            Dialect = dialect;
            IgnoreCase = options.HasFlag(GlobOptions.IgnoreCase);

            // Only the Standard dialect hides the entries whose name starts with a dot: fnmatch without FNM_PERIOD,
            // gitignore and MSBuild all match them with a wildcard.
            MatchLeadingDot = dialect is not GlobDialect.Standard || options.HasFlag(GlobOptions.MatchLeadingDot);
        }

        public GlobDialect Dialect { get; }
        public bool IgnoreCase { get; }
        public bool MatchLeadingDot { get; }

        private bool IsPosix => Dialect is GlobDialect.Posix or GlobDialect.PosixPath;

        // fnmatch and gitignore read '.' and '..' as ordinary names
        public bool NormalizeDotSegments => Dialect is GlobDialect.Standard or GlobDialect.MSBuild;
        public bool PathSeparatorAware => Dialect is not GlobDialect.Posix;
        public bool SupportsLeadingExclude => Dialect is GlobDialect.Standard or GlobDialect.Git;

        // MSBuild escapes a character with '%XX', and '\' is a path separator
        public bool SupportsEscape => Dialect is not GlobDialect.MSBuild;

        // git treats '{' and '}' as ordinary characters.
        public bool SupportsLiteralSet => Dialect is GlobDialect.Standard;
        public bool SupportsRecursiveWildcard => Dialect is GlobDialect.Standard or GlobDialect.Git or GlobDialect.MSBuild;

        // gitignore reads any run of '*' that makes a whole path segment as '**'
        public bool ReadsStarRunAsRecursiveWildcard => Dialect is GlobDialect.Git;

        public EmptySegmentHandling EmptySegmentHandling => Dialect switch
        {
            GlobDialect.PosixPath => EmptySegmentHandling.Match,
            GlobDialect.MSBuild => EmptySegmentHandling.MatchLeading,
            GlobDialect.Git => EmptySegmentHandling.Reject,
            _ => EmptySegmentHandling.Ignore,
        };

        public bool SupportsBracketExpression => Dialect is not GlobDialect.MSBuild;
        public bool SupportsCaretNegation => Dialect is not GlobDialect.Standard;
        public bool SupportsEscapeInBracketExpression => Dialect is not GlobDialect.Standard;
        public bool SupportsNamedCharacterClass => Dialect is not GlobDialect.Standard;
        public bool SupportsEquivalenceClass => IsPosix;
        public bool SupportsCollatingSymbol => IsPosix;
        public bool ReadsUnterminatedBracketAsLiteral => IsPosix;
        public ReversedRangeHandling ReversedRangeHandling => Dialect switch
        {
            GlobDialect.Standard => ReversedRangeHandling.Reject,
            GlobDialect.Git => ReversedRangeHandling.MatchStart,
            _ => ReversedRangeHandling.Empty,
        };

        public bool RejectsSeparatorInBracketExpression => Dialect is GlobDialect.Standard;

        public Segment AnyCharacterSegment => Dialect is GlobDialect.Posix ? MatchAnyTextCharacterSegment.Instance : MatchAnyCharacterSegment.Instance;

        public bool IsPatternSeparator(char c)
        {
            return Dialect switch
            {
                GlobDialect.Posix => false,
                GlobDialect.MSBuild => c is '/' or '\\',
                _ => c == '/',
            };
        }
    }
}
