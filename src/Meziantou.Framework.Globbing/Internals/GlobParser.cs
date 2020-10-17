using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using Meziantou.Framework.Globbing.Internals;

namespace Meziantou.Framework.Globbing
{
    internal ref struct GlobParser
    {
        public static bool TryParse(ReadOnlySpan<char> pattern, GlobOptions options, [NotNullWhen(true)] out Glob? result, [NotNullWhen(false)] out string? errorMessage)
        {
            result = null;
            if (pattern.IsEmpty)
            {
                errorMessage = "The pattern is empty";
                return false;
            }

            var ignoreCase = options.HasFlag(GlobOptions.CaseInsensitive);

            var exclude = false;
            var segments = new List<Segment>();
            List<SubSegment>? subSegments = null;
            List<string>? setSubsegment = null;
            List<CharacterRange>? rangeSubsegment = null;
            char? rangeStart = null;
            var rangeInverse = false;

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
                            if (subSegments == null && currentLiteral.Length == 0)
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
                            AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, MatchAnySubSegment.Instance);
                            continue;
                        }
                        else if (c == '*')
                        {
                            if (subSegments == null && currentLiteral.Length == 0)
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
                            if (subSegments != null && subSegments.Count > 0 && subSegments[^1] is MatchAllSubSegment)
                                continue;

                            AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, MatchAllSubSegment.Instance);
                            continue;
                        }
                        else if (c == '{') // Start LiteralSet
                        {
                            Debug.Assert(setSubsegment == null);
                            AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, subSegment: null);
                            parserContext = GlobParserContext.LiteralSet;
                            setSubsegment = new List<string>();
                            continue;
                        }
                        else if (c == '[') // Range
                        {
                            Debug.Assert(rangeSubsegment == null);
                            AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, subSegment: null);
                            parserContext = GlobParserContext.Range;
                            rangeSubsegment = new List<CharacterRange>();
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
                        Debug.Assert(setSubsegment != null);
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
                            AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, new LiteralSetSubSegment(setSubsegment.ToArray(), ignoreCase));
                            setSubsegment = null;
                            parserContext = GlobParserContext.Segment;
                            continue;
                        }
                    }
                    else if (parserContext == GlobParserContext.Range)
                    {
                        Debug.Assert(rangeSubsegment != null);
                        if (c == ']') // end of literal set
                        {
                            if (rangeStart.HasValue)
                            {
                                rangeSubsegment.Add(new CharacterRange(rangeStart.GetValueOrDefault()));
                                rangeSubsegment.Add(new CharacterRange('-'));
                                rangeStart = null;
                            }

                            AddSubsegment(ref subSegments, ref currentLiteral, ignoreCase, CreateRangeSubsegment(rangeSubsegment, rangeInverse, ignoreCase));
                            rangeSubsegment = null;
                            parserContext = GlobParserContext.Segment;
                            continue;
                        }
                        else
                        {
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

                errorMessage = null;
                result = new Glob(segments.ToArray(), exclude ? GlobMode.Exclude : GlobMode.Include);
                return true;
            }
            finally
            {
                currentLiteral.Dispose();
            }

            static void AddSubsegment(ref List<SubSegment>? subSegments, ref ValueStringBuilder currentLiteral, bool ignoreCase, SubSegment? subSegment)
            {
                if (subSegments == null)
                {
                    subSegments = new List<SubSegment>();
                }

                if (currentLiteral.Length > 0)
                {
                    subSegments.Add(new LiteralSubSegment(currentLiteral.AsSpan().ToString(), ignoreCase));
                    currentLiteral.Clear();
                }

                if (subSegment != null)
                {
                    subSegments.Add(subSegment);
                }
            }

            static void FinishSegment(List<Segment> segments, ref List<SubSegment>? subSegments, ref ValueStringBuilder currentLiteral, bool ignoreCase)
            {
                if (subSegments != null)
                {
                    if (currentLiteral.Length > 0)
                    {
                        subSegments.Add(new LiteralSubSegment(currentLiteral.AsSpan().ToString(), ignoreCase));
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

        private static bool EndOfSegmentEqual(ReadOnlySpan<char> rest, string expected)
        {
            // Could be "{rest}/" or "{rest}"$
            if (rest.Length == expected.Length)
                return rest.SequenceEqual(expected.AsSpan());

            if (rest.Length > expected.Length)
                return rest.StartsWith(expected.AsSpan(), StringComparison.Ordinal) && rest[expected.Length] == '/';

            return false;
        }

        private static SubSegment CreateRangeSubsegment(List<CharacterRange> ranges, bool inverse, bool ignoreCase)
        {
            var singleCharRanges = ranges.Where(r => r.IsSingleCharacterRange).Select(r => r.Min).ToArray();
            var rangeCharRanges = ranges.Where(r => !r.IsSingleCharacterRange).ToArray();

            if (singleCharRanges.Length > 0)
            {
                if (rangeCharRanges.Length == 0)
                    return new CharacterSetSubSegment(new string(singleCharRanges), inverse, ignoreCase);
            }
            else if (rangeCharRanges.Length == 1)
            {
                return new RangeSubSegment(rangeCharRanges[0], inverse, ignoreCase);
            }

            // Inverse flags is set on the combination
            var segments = rangeCharRanges.Select(r => (SubSegment)new RangeSubSegment(r, inverse: false, ignoreCase));

            if (singleCharRanges.Length > 0)
            {
                segments = segments.Prepend(new CharacterSetSubSegment(new string(singleCharRanges), inverse: false, ignoreCase));
            }

            return new CombinedSubSegment(segments.ToArray(), inverse);
        }

        private static Segment CreateSegment(List<SubSegment> parts, bool ignoreCase)
        {
            if (parts.Count == 1)
                return new SingleSubSegment(parts[0]);

            // Try to optimize common cases
            if (parts.Count == 2)
            {
                // Ends with: *.txt
                if (parts[0] is MatchAllSubSegment && parts[1] is LiteralSubSegment endsWithLiteral)
                    return new EndsWithSegment(endsWithLiteral.Value, ignoreCase);

                // Starts with: test.*
                if (parts[1] is MatchAllSubSegment && parts[0] is LiteralSubSegment startsWithLiteral)
                    return new StartsWithSegment(startsWithLiteral.Value, ignoreCase);
            }
            else if (parts.Count == 3)
            {
                // Contains: *test*
                if (parts[0] is MatchAllSubSegment && parts[2] is MatchAllSubSegment && parts[1] is LiteralSubSegment containsLiteral)
                    return new ContainsSegment(containsLiteral.Value, ignoreCase);

                // Starts and Ends with: a*b
                if (parts[1] is MatchAllSubSegment && parts[2] is MatchAllSubSegment && parts[0] is LiteralSubSegment startsLiteral && parts[2] is LiteralSubSegment endsLiteral)
                    return new StartsEndsWithSegment(startsLiteral.Value, endsLiteral.Value, ignoreCase);
            }

            return new RaggedSegment(parts.ToArray());
        }

        [StructLayout(LayoutKind.Auto)]
        private ref struct SplitEnumerator
        {
            private ReadOnlySpan<char> _str;
            private readonly char _separator;

            public SplitEnumerator(ReadOnlySpan<char> str, char separator)
            {
                _str = str;
                _separator = separator;
                Current = default;
            }

            // Needed to be compatible with the foreach operator
            public SplitEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                var span = _str;
                if (span.Length == 0) // Reach the end of the string
                    return false;

                var index = span.IndexOf(_separator);
                if (index == -1) // The string is composed of only one line
                {
                    _str = ReadOnlySpan<char>.Empty; // The remaining string is an empty string
                    Current = span;
                    return true;
                }

                Current = span[..index];
                _str = span[(index + 1)..];
                return true;
            }

            public ReadOnlySpan<char> Current { get; private set; }
        }
    }
}
