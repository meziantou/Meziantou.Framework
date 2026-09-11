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
        var segments = new List<Segment>();
        var matchLeadingDot = new List<bool>();
        List<Segment>? subSegments = null;
        List<string>? setSubsegment = null;
        List<CharacterRange>? rangeSubsegment = null;
        List<NamedCharacterClass>? classSubsegment = null;
        char? rangeStart = null;
        var rangeInverse = false;
        var currentSegmentMatchLeadingDot = settings.MatchLeadingDot;

        if (dialect is GlobDialect.Git)
        {
            // Check if there is a separator at start or middle of the string
            if (pattern[0..^1].IndexOf('/') < 0)
            {
                segments.Add(RecursiveMatchAllSegment.Instance);
                matchLeadingDot.Add(settings.MatchLeadingDot);
            }
        }

        var escape = false;
        var parserContext = GlobParserContext.Segment;
        var matchType = GlobMatchType.File;

        Span<char> sbSpan = stackalloc char[128];
        var currentLiteral = new ValueStringBuilder(sbSpan);
        try
        {
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (escape)
                {
                    AppendLiteral(ref currentLiteral, ref currentSegmentMatchLeadingDot, subSegments, c, settings.MatchLeadingDot);
                    escape = false;
                    continue;
                }

                if (c == '!' && i == 0 && settings.SupportsLeadingExclude)
                {
                    exclude = true;
                    continue;
                }

                if (dialect is GlobDialect.MSBuild && TryDecodeMsBuildEscape(pattern, i, out var escapedCharacter))
                {
                    AppendLiteral(ref currentLiteral, ref currentSegmentMatchLeadingDot, subSegments, escapedCharacter, settings.MatchLeadingDot);
                    i += 2;
                    continue;
                }

                if (parserContext == GlobParserContext.Segment)
                {
                    if (settings.IsPatternSeparator(c))
                    {
                        FinishSegment(segments, matchLeadingDot, ref subSegments, ref currentLiteral, settings.IgnoreCase, currentSegmentMatchLeadingDot, settings.PathSeparatorAware);
                        currentSegmentMatchLeadingDot = settings.MatchLeadingDot;
                        continue;
                    }
                    else if (c == '.')
                    {
                        if (settings.NormalizeDotSegments && subSegments is null && currentLiteral.Length == 0)
                        {
                            if (EndOfSegmentEqual(pattern[i..], "..", settings))
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

                                segments.RemoveAt(segments.Count - 1);
                                matchLeadingDot.RemoveAt(matchLeadingDot.Count - 1);
                                i += 2;
                                continue;
                            }

                            if (EndOfSegmentEqual(pattern[i..], ".", settings))
                            {
                                i += 1;
                                continue;
                            }
                        }
                    }
                    else if (c == '?')
                    {
                        AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, settings.AnyCharacterSegment);
                        continue;
                    }
                    else if (c == '*')
                    {
                        if (dialect is GlobDialect.MSBuild && i + 1 < pattern.Length && pattern[i + 1] == '*' &&
                            (subSegments is not null || currentLiteral.Length > 0 || !EndOfSegmentEqual(pattern[i..], "**", settings)))
                        {
                            errorMessage = "the recursive wildcard '**' must be its own path segment";
                            return false;
                        }

                        if (subSegments is null && currentLiteral.Length == 0)
                        {
                            if (settings.SupportsRecursiveWildcard && EndOfSegmentEqual(pattern[i..], "**", settings))
                            {
                                // Merge two consecutive '**' (**/**)
                                if (segments.Count == 0 || segments[^1] is not RecursiveMatchAllSegment)
                                {
                                    segments.Add(RecursiveMatchAllSegment.Instance);
                                    matchLeadingDot.Add(settings.MatchLeadingDot);
                                }

                                i += 2;
                                currentSegmentMatchLeadingDot = settings.MatchLeadingDot;
                                continue;
                            }

                            if (EndOfSegmentEqual(pattern[i..], "*", settings))
                            {
                                segments.Add(MatchAllSegment.Instance);
                                matchLeadingDot.Add(currentSegmentMatchLeadingDot);
                                i += 1;
                                currentSegmentMatchLeadingDot = settings.MatchLeadingDot;
                                continue;
                            }
                        }

                        // Merge 2 consecutive '*'
                        if (currentLiteral.Length == 0 && subSegments is not null && subSegments.Count > 0 && subSegments[^1] is MatchAllSubSegment)
                            continue;

                        AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, MatchAllSubSegment.Instance);
                        continue;
                    }
                    else if (c == '{' && settings.SupportsLiteralSet) // Start LiteralSet
                    {
                        Debug.Assert(setSubsegment is null);
                        AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, subSegment: null);
                        parserContext = GlobParserContext.LiteralSet;
                        setSubsegment = [];
                        continue;
                    }
                    else if (c == '[' && dialect is not GlobDialect.MSBuild) // Range
                    {
                        Debug.Assert(rangeSubsegment is null);
                        Debug.Assert(classSubsegment is null);
                        AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, subSegment: null);
                        parserContext = GlobParserContext.Range;
                        rangeSubsegment = [];
                        rangeInverse = i + 1 < pattern.Length && pattern[i + 1] == '!';
                        if (rangeInverse)
                        {
                            i++;
                        }

                        continue;
                    }
                }
                else if (parserContext == GlobParserContext.LiteralSet)
                {
                    Debug.Assert(setSubsegment is not null);
                    if (c == ',') // end of current value
                    {
                        setSubsegment.Add(currentLiteral.AsSpan().ToString());
                        currentLiteral.Clear();
                        continue;
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
                        continue;
                    }
                }
                else if (parserContext == GlobParserContext.Range)
                {
                    Debug.Assert(rangeSubsegment is not null);

                    // POSIX character class, for instance [[:digit:]]. A class cannot be a range bound, so an
                    // opened range keeps reading '[' as an ordinary character.
                    if (c == '[' && !rangeStart.HasValue && settings.SupportsNamedCharacterClass && TryReadNamedCharacterClass(pattern[i..], out var namedCharacterClass, out var namedCharacterClassLength))
                    {
                        classSubsegment ??= [];
                        classSubsegment.Add(namedCharacterClass);
                        i += namedCharacterClassLength - 1;
                        continue;
                    }

                    if (c == ']') // end of literal set, except if empty []] or [!]]
                    {
                        // [a-] => '-' is considered as a character
                        if (rangeStart.HasValue)
                        {
                            rangeSubsegment.Add(new CharacterRange(rangeStart.GetValueOrDefault()));
                            rangeSubsegment.Add(new CharacterRange('-'));
                            rangeStart = null;
                        }

                        if (rangeSubsegment.Count > 0 || classSubsegment is not null)
                        {
                            if (settings.PathSeparatorAware && rangeSubsegment.Exists(s => s.IsInRange(Path.DirectorySeparatorChar) || s.IsInRange(Path.AltDirectorySeparatorChar)))
                            {
                                errorMessage = "range contains a path separator";
                                return false;
                            }

                            AddSubsegment(ref subSegments, ref currentLiteral, settings.IgnoreCase, CreateRangeSubsegment(rangeSubsegment, classSubsegment, rangeInverse, settings.IgnoreCase));
                            rangeSubsegment = null;
                            classSubsegment = null;
                            parserContext = GlobParserContext.Segment;
                            continue;
                        }
                    }

                    if (rangeStart.HasValue)
                    {
                        var rangeStartValue = rangeStart.GetValueOrDefault();
                        if (rangeStartValue > c)
                        {
                            errorMessage = $"Invalid range '{rangeStartValue}' > '{c}'";
                            return false;
                        }

                        rangeSubsegment.Add(new CharacterRange(rangeStartValue, c));
                        rangeStart = null;
                    }
                    else
                    {
                        if (i + 1 < pattern.Length && pattern[i + 1] == '-')
                        {
                            rangeStart = c;
                            i++;
                        }
                        else
                        {
                            rangeSubsegment.Add(new CharacterRange(c));
                        }
                    }

                    continue;
                }

                switch (c)
                {
                    case '\\': // Escape next character
                        if (dialect is GlobDialect.MSBuild)
                        {
                            FinishSegment(segments, matchLeadingDot, ref subSegments, ref currentLiteral, settings.IgnoreCase, currentSegmentMatchLeadingDot, settings.PathSeparatorAware);
                            currentSegmentMatchLeadingDot = settings.MatchLeadingDot;
                        }
                        else
                        {
                            escape = true;
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

            // If the last character is a '\'
            if (escape)
            {
                errorMessage = "Expecting a character after '\\'";
                return false;
            }

            FinishSegment(segments, matchLeadingDot, ref subSegments, ref currentLiteral, settings.IgnoreCase, currentSegmentMatchLeadingDot, settings.PathSeparatorAware);

            if (dialect is GlobDialect.Git)
            {
                if (pattern[^1] == '/')
                {
                    // A gitignore entry ending with a '/' matches the directory itself. Excluding a directory also
                    // excludes its content, but re-including one does not re-include its content, so a negated
                    // entry never matches the paths below the directory.
                    segments.Add(matchGitDirectoryContent && !exclude ? DirectoryContentSegment.IncludingContent : DirectoryContentSegment.DirectoryOnly);
                    matchLeadingDot.Add(settings.MatchLeadingDot);
                }

                // A gitignore entry matches a file or a directory with that name. Whether the item must be a
                // directory is decided by DirectoryContentSegment, which knows where the path ends.
                matchType = GlobMatchType.Any;
            }
            else
            {
                if (settings.PathSeparatorAware && settings.IsPatternSeparator(pattern[^1]))
                {
                    matchType = GlobMatchType.Directory;
                }
            }

            // '.' and '..' normalization can remove every segment (".", "./", "a/.."). Such a pattern cannot match
            // anything, and a Glob without any segment is not usable, so reject it instead of returning it.
            if (segments.Count == 0)
            {
                errorMessage = "the pattern does not contain any segment";
                return false;
            }

            errorMessage = null;
            result = CreateGlob(segments, matchLeadingDot, exclude, settings.IgnoreCase, matchType, settings.MatchLeadingDot, settings.PathSeparatorAware);
            return true;
        }
        finally
        {
            currentLiteral.Dispose();
        }

        static void AddSubsegment(ref List<Segment>? subSegments, ref ValueStringBuilder currentLiteral, bool ignoreCase, Segment? subSegment)
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

        static void FinishSegment(List<Segment> segments, List<bool> matchLeadingDot, ref List<Segment>? subSegments, ref ValueStringBuilder currentLiteral, bool ignoreCase, bool currentSegmentMatchLeadingDot, bool pathSeparatorAware)
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

    private static bool EndOfSegmentEqual(ReadOnlySpan<char> rest, string expected, GlobParserSettings settings)
    {
        // Could be "{rest}/" or "{rest}"$
        if (rest.Length == expected.Length)
            return rest.SequenceEqual(expected.AsSpan());

        if (rest.Length > expected.Length)
            return rest.StartsWith(expected.AsSpan(), StringComparison.Ordinal) && settings.IsPatternSeparator(rest[expected.Length]);

        return false;
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

    private static bool TryReadNamedCharacterClass(ReadOnlySpan<char> pattern, out NamedCharacterClass result, out int length)
    {
        result = default;
        length = 0;

        // The shortest class is "[::]", and the caller already knows the first character is '['.
        if (pattern.Length < 4 || pattern[1] != ':')
            return false;

        var nameLength = pattern[2..].IndexOf(":]".AsSpan(), StringComparison.Ordinal);
        if (nameLength < 0)
            return false;

        var name = pattern.Slice(2, nameLength);
        switch (name)
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
            default: return false;
        }

        length = nameLength + 4;
        return true;
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
            var s1 = GetString(parts[i]);
            var s2 = GetString(parts[i + 1]);

            if (s1 is null || s2 is null)
                continue;

            // Merge
            var literal = new LiteralSegment(s1 + s2, ignoreCase);
            parts[i] = literal;
            parts.RemoveAt(i + 1);

            static string? GetString(Segment segment)
            {
                return segment switch
                {
                    LiteralSegment literal => literal.Value,
                    CharacterSetSegment set when set.Set.Length == 1 => set.Set,
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

    private readonly struct GlobParserSettings
    {
        private readonly GlobDialect _dialect;

        public GlobParserSettings(GlobDialect dialect, GlobOptions options)
        {
            _dialect = dialect;
            IgnoreCase = options.HasFlag(GlobOptions.IgnoreCase);
            MatchLeadingDot = dialect is GlobDialect.Git or GlobDialect.Posix or GlobDialect.PosixPath || options.HasFlag(GlobOptions.MatchLeadingDot);
        }

        public bool IgnoreCase { get; }
        public bool MatchLeadingDot { get; }
        public bool NormalizeDotSegments => _dialect is not (GlobDialect.Posix or GlobDialect.PosixPath);
        public bool PathSeparatorAware => _dialect is not GlobDialect.Posix;
        public bool SupportsLeadingExclude => _dialect is GlobDialect.Standard or GlobDialect.Git;

        // git treats '{' and '}' as ordinary characters.
        public bool SupportsLiteralSet => _dialect is GlobDialect.Standard;
        public bool SupportsNamedCharacterClass => _dialect is GlobDialect.Posix or GlobDialect.PosixPath;
        public bool SupportsRecursiveWildcard => _dialect is GlobDialect.Standard or GlobDialect.Git or GlobDialect.MSBuild;
        public Segment AnyCharacterSegment => _dialect is GlobDialect.Posix ? MatchAnyTextCharacterSegment.Instance : MatchAnyCharacterSegment.Instance;

        public bool IsPatternSeparator(char c)
        {
            return _dialect switch
            {
                GlobDialect.Posix => false,
                GlobDialect.MSBuild => c is '/' or '\\',
                _ => c == '/',
            };
        }
    }
}
