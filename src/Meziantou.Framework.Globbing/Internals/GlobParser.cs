using System.Diagnostics;
using Meziantou.Framework.Globbing.Internals;
using Meziantou.Framework.Globbing.Internals.Segments;

namespace Meziantou.Framework.Globbing;

internal static class GlobParser
{
    private static readonly char[] DirectorySeparator = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static bool TryParse(ReadOnlySpan<char> pattern, GlobOptions options, [NotNullWhen(true)] out Glob? result, [NotNullWhen(false)] out string? errorMessage)
    {
        result = null;
        if (pattern.IsEmpty)
        {
            errorMessage = "The pattern is empty";
            return false;
        }

        var ignoreCase = options.HasFlag(GlobOptions.IgnoreCase);

        var exclude = false;
        var segments = new List<Segment>();
        List<Segment>? subSegments = null;
        List<string>? setSubsegment = null;
        List<CharacterRange>? rangeSubsegment = null;
        char? rangeStart = null;
        var rangeInverse = false;

        if (options.HasFlag(GlobOptions.Git))
        {
            // Check if there is a separator at start or middle of the string
            if (pattern[0..^1].IndexOf('/') < 0)
            {
                segments.Add(RecursiveMatchAllSegment.Instance);
            }
        }

        var escape = false;
        var parserContext = GlobParserContext.Segment;

        Span<char> sbSpan = stackalloc char[128];
        var currentLiteral = new ValueStringBuilder(sbSpan);
        try
        {
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (escape)
                {
                    currentLiteral.Append(c);
                    escape = false;
                    continue;
                }

                if (c == '!' && i == 0)
                {
                    exclude = true;
                    continue;
                }

                if (parserContext == GlobParserContext.Segment)
                {
                    if (c == '/')
                    {
                        FinishSegment(segments, ref subSegments, ref currentLiteral, ignoreCase);
                        continue;
                    }
                    else if (c == '.')
                    {
                        if (subSegments is null && currentLiteral.Length == 0)
                        {
                            if (EndOfSegmentEqual(pattern[i..], ".."))
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
                                i += 2;
                                continue;
                            }

                            if (EndOfSegmentEqual(pattern[i..], "."))
                            {
                                i += 1;
                                continue;
                            }
                        }
                    }
                    else if (c == '?')
                    {
                        AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, MatchAnyCharacterSegment.Instance);
                        continue;
                    }
                    else if (c == '*')
                    {
                        if (subSegments is null && currentLiteral.Length == 0)
                        {
                            if (EndOfSegmentEqual(pattern[i..], "**"))
                            {
                                // Merge two consecutive '**' (**/**)
                                if (segments.Count == 0 || segments[^1] is not RecursiveMatchAllSegment)
                                {
                                    segments.Add(RecursiveMatchAllSegment.Instance);
                                }

                                i += 2;
                                continue;
                            }

                            if (EndOfSegmentEqual(pattern[i..], "*"))
                            {
                                segments.Add(MatchAllSegment.Instance);
                                i += 1;
                                continue;
                            }
                        }

                        // Merge 2 consecutive '*'
                        if (currentLiteral.Length == 0 && subSegments is not null && subSegments.Count > 0 && subSegments[^1] is MatchAllSubSegment)
                            continue;

                        AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, MatchAllSubSegment.Instance);
                        continue;
                    }
                    else if (c == '{') // Start LiteralSet
                    {
                        Debug.Assert(setSubsegment is null);
                        AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, subSegment: null);
                        parserContext = GlobParserContext.LiteralSet;
                        setSubsegment = [];
                        continue;
                    }
                    else if (c == '[') // Range
                    {
                        Debug.Assert(rangeSubsegment is null);
                        AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, subSegment: null);
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

                        if (setSubsegment.Exists(s => s.IndexOfAny(DirectorySeparator) >= 0))
                        {
                            errorMessage = "set contains a path separator";
                            return false;
                        }

                        AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, new LiteralSetSegment(setSubsegment.ToArray(), ignoreCase));
                        setSubsegment = null;
                        parserContext = GlobParserContext.Segment;
                        continue;
                    }
                }
                else if (parserContext == GlobParserContext.Range)
                {
                    Debug.Assert(rangeSubsegment is not null);
                    if (c == ']') // end of literal set, except if empty []] or [!]]
                    {
                        // [a-] => '-' is considered as a character
                        if (rangeStart.HasValue)
                        {
                            rangeSubsegment.Add(new CharacterRange(rangeStart.GetValueOrDefault()));
                            rangeSubsegment.Add(new CharacterRange('-'));
                            rangeStart = null;
                        }

                        if (rangeSubsegment.Count > 0)
                        {
                            if (rangeSubsegment.Exists(s => s.IsInRange(Path.DirectorySeparatorChar) || s.IsInRange(Path.AltDirectorySeparatorChar)))
                            {
                                errorMessage = "range contains a path separator";
                                return false;
                            }

                            AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, CreateRangeSubsegment(rangeSubsegment, rangeInverse, ignoreCase));
                            rangeSubsegment = null;
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
                        escape = true;
                        break;

                    default:
                        currentLiteral.Append(c);
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

            FinishSegment(segments, ref subSegments, ref currentLiteral, ignoreCase);

            if (options.HasFlag(GlobOptions.Git))
            {
                if (pattern[^1] == '/')
                {
                    segments.Add(RecursiveMatchAllSegment.Instance);
                    segments.Add(MatchAllSegment.Instance);
                }
            }

            errorMessage = null;
            result = CreateGlob(segments, exclude, ignoreCase);
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

        static void FinishSegment(List<Segment> segments, ref List<Segment>? subSegments, ref ValueStringBuilder currentLiteral, bool ignoreCase)
        {
            if (subSegments is not null)
            {
                if (currentLiteral.Length > 0)
                {
                    subSegments.Add(new LiteralSegment(currentLiteral.AsSpan().ToString(), ignoreCase));
                    currentLiteral.Clear();
                }

                segments.Add(CreateSegment(subSegments, ignoreCase));
                subSegments = null;
            }
            else if (currentLiteral.Length > 0)
            {
                segments.Add(new LiteralSegment(currentLiteral.AsSpan().ToString(), ignoreCase));
                currentLiteral.Clear();
            }
        }
    }

    private static Glob CreateGlob(List<Segment> segments, bool exclude, bool ignoreCase)
    {
        // Optimize segments
        if (segments.Count >= 2)
        {
            if (segments[^2] is RecursiveMatchAllSegment && segments[^1] is MatchAllSegment) // **/*
            {
                var lastSegment = MatchNonEmptyTextSegment.Instance;
                segments.RemoveRange(segments.Count - 2, 2);
                segments.Add(lastSegment);
            }
            else if (segments[^2] is RecursiveMatchAllSegment && segments[^1] is EndsWithSegment endsWith) // **/*.txt
            {
                var lastSegment = new MatchAllEndsWithSegment(endsWith.Value, ignoreCase);
                segments.RemoveRange(segments.Count - 2, 2);
                segments.Add(lastSegment);
            }
            else if (segments[^2] is RecursiveMatchAllSegment) // **/segment
            {
                var lastSegment = new LastSegment(segments[^1]);
                segments.RemoveRange(segments.Count - 2, 2);
                segments.Add(lastSegment);
            }
        }

        return new Glob(segments.ToArray(), exclude ? GlobMode.Exclude : GlobMode.Include);
    }

    private static bool EndOfSegmentEqual(ReadOnlySpan<char> rest, string expected)
    {
        // Could be "{rest}/" or "{rest}"$
        if (rest.Length == expected.Length)
            return rest.SequenceEqual(expected.AsSpan());

        if (rest.Length > expected.Length)
            return rest.StartsWith(expected.AsSpan(), StringComparison.Ordinal) && rest[expected.Length] == '/';

        return false;
    }

    private static Segment CreateRangeSubsegment(List<CharacterRange> ranges, bool inverse, bool ignoreCase)
    {
        var singleCharRanges = ranges.Where(r => r.IsSingleCharacterRange).Select(r => r.Min).ToArray();
        var rangeCharRanges = ranges.Where(r => !r.IsSingleCharacterRange).ToArray();

        if (singleCharRanges.Length > 0)
        {
            if (rangeCharRanges.Length == 0)
                return CreateCharacterSet(singleCharRanges, inverse, ignoreCase);
        }
        else if (rangeCharRanges.Length == 1)
        {
            return CreateCharacterRange(rangeCharRanges[0], ignoreCase, inverse);
        }

        // Inverse flags is set on the combination
        var segments = rangeCharRanges.Select(r => CreateCharacterRange(r, ignoreCase, inverse: false));

        if (singleCharRanges.Length > 0)
        {
            segments = segments.Prepend(CreateCharacterSet(singleCharRanges, inverse: false, ignoreCase));
        }

        return new OrSegment(segments.ToArray(), inverse);
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

    private static Segment CreateSegment(List<Segment> parts, bool ignoreCase)
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
                    var nextCharacters = next switch
                    {
                        LiteralSegment literal => [literal.Value[0]],
                        CharacterSetSegment characterSet => characterSet.Set.ToList(),
                        CharacterRangeSegment characterRange when characterRange.Range.Length < 3 => characterRange.Range.EnumerateCharacters().ToList(),
                        LiteralSetSegment literalSet => literalSet.Values.Where(v => v.Length > 0).Select(v => v[0]).ToList(),
                        _ => null,
                    };

                    if (nextCharacters is not null)
                    {
                        nextCharacters.Add(Path.DirectorySeparatorChar);
                        if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar)
                        {
                            nextCharacters.Add(Path.AltDirectorySeparatorChar);
                        }

                        parts.Insert(i, new ConsumeSegmentUntilSegment(nextCharacters.ToArray()));
                        i++;
                    }
                }
            }
        }

        if (parts[^1] is MatchAllSubSegment)
        {
            parts[^1] = MatchAllEndOfSegment.Instance;
        }

        if (parts.Count == 1)
            return parts[0];

        return new RaggedSegment(parts.ToArray());
    }
}
