namespace Meziantou.Framework.Globbing.Internals;

internal sealed class RaggedSegment : Segment
{
    internal readonly Segment[] _segments;
    private readonly int _minimumSegmentLength;
    private readonly char[]? _firstRequiredCharacters;
    private readonly int _matchAllSubSegmentCount;
    private readonly int _firstMatchAllSubSegmentIndex;
    private readonly bool _hasLiteralSet;

    public RaggedSegment(Segment[] segments)
    {
        _segments = segments;

        _firstMatchAllSubSegmentIndex = -1;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            _minimumSegmentLength += GetMinimumLength(segment);

            if (segment is MatchAllSubSegment)
            {
                if (_firstMatchAllSubSegmentIndex < 0)
                {
                    _firstMatchAllSubSegmentIndex = i;
                }

                _matchAllSubSegmentCount++;
            }
            else if (segment is LiteralSetSegment)
            {
                _hasLiteralSet = true;
            }
        }

        if (_segments.Length > 0)
        {
            _firstRequiredCharacters = GetLeadingCharacters(_segments[0]);
        }
    }

    /// <summary>The characters the first character of the path segment must be one of, or <see langword="null"/> when the segment does not require any.</summary>
    internal char[]? FirstRequiredCharacters => _firstRequiredCharacters;

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.CurrentSegmentLength < _minimumSegmentLength)
            return false;

        if (_firstRequiredCharacters is not null)
        {
            if (pathReader.CurrentSegmentLength == 0)
                return false;

            if (Array.IndexOf(_firstRequiredCharacters, pathReader.CurrentText[0]) < 0)
                return false;
        }

        ReadOnlySpan<Segment> patternSegments = _segments;

        // A literal set can be consumed in several ways, and the choice only pays off once the rest of the pattern
        // is matched, so it needs the backtracking matcher even when the segment holds no '*'.
        if (!_hasLiteralSet)
        {
            if (_matchAllSubSegmentCount == 0)
                return MatchCompleteNoStar(ref pathReader, patternSegments);

            if (_matchAllSubSegmentCount == 1)
                return MatchSingleStar(ref pathReader, patternSegments, _firstMatchAllSubSegmentIndex);
        }

        return Match(ref pathReader, patternSegments);

        static bool Match(ref PathReader pathReader, ReadOnlySpan<Segment> patternSegments)
        {
            for (var i = 0; i < patternSegments.Length; i++)
            {
                var patternSegment = patternSegments[i];
                if (patternSegment is MatchAllSubSegment)
                {
                    var remainingPatternSegments = patternSegments[(i + 1)..];
                    if (remainingPatternSegments.IsEmpty) // Last subsegment
                    {
                        return true;
                    }

                    var copyReader = pathReader;
                    if (Match(ref copyReader, remainingPatternSegments))
                    {
                        pathReader = copyReader;
                        return true;
                    }

                    while (!pathReader.IsEndOfCurrentSegment)
                    {
                        pathReader.ConsumeInSegment(1);
                        copyReader = pathReader;
                        if (Match(ref copyReader, remainingPatternSegments))
                        {
                            pathReader = copyReader;
                            return true;
                        }
                    }

                    return false;
                }
                else if (patternSegment is LiteralSetSegment literalSet)
                {
                    var remainingPatternSegments = patternSegments[(i + 1)..];
                    foreach (var value in literalSet.Values)
                    {
                        var copyReader = pathReader;
                        if (!TryConsumeLiteral(ref copyReader, value, literalSet.Comparison))
                            continue;

                        if (Match(ref copyReader, remainingPatternSegments))
                        {
                            pathReader = copyReader;
                            return true;
                        }
                    }

                    return false;
                }
                else
                {
                    if (!patternSegment.IsMatch(ref pathReader))
                        return false;
                }
            }

            return pathReader.IsEndOfPath || pathReader.IsPathSeparator();
        }

        static bool TryConsumeLiteral(ref PathReader pathReader, string value, StringComparison comparison)
        {
            // '{,a}' has an empty alternative, which consumes nothing.
            if (value.Length == 0)
                return true;

            if (!pathReader.CurrentSegment.StartsWith(value.AsSpan(), comparison))
                return false;

            pathReader.ConsumeInSegment(value.Length);
            return true;
        }

        static bool MatchSegmentsNoStar(ref PathReader pathReader, ReadOnlySpan<Segment> patternSegments)
        {
            foreach (var patternSegment in patternSegments)
            {
                if (!patternSegment.IsMatch(ref pathReader))
                    return false;
            }

            return true;
        }

        static bool MatchCompleteNoStar(ref PathReader pathReader, ReadOnlySpan<Segment> patternSegments)
        {
            if (!MatchSegmentsNoStar(ref pathReader, patternSegments))
                return false;

            return pathReader.IsEndOfPath || pathReader.IsPathSeparator();
        }

        static bool MatchSingleStar(ref PathReader pathReader, ReadOnlySpan<Segment> patternSegments, int starIndex)
        {
            var leadingPattern = patternSegments[..starIndex];
            var trailingPattern = patternSegments[(starIndex + 1)..];

            if (!MatchSegmentsNoStar(ref pathReader, leadingPattern))
                return false;

            if (trailingPattern.IsEmpty)
                return true;

            var copyReader = pathReader;
            if (MatchCompleteNoStar(ref copyReader, trailingPattern))
            {
                pathReader = copyReader;
                return true;
            }

            while (!pathReader.IsEndOfCurrentSegment)
            {
                pathReader.ConsumeInSegment(1);
                copyReader = pathReader;
                if (MatchCompleteNoStar(ref copyReader, trailingPattern))
                {
                    pathReader = copyReader;
                    return true;
                }
            }

            return false;
        }
    }

    private static int GetMinimumLength(Segment segment)
    {
        return segment switch
        {
            LiteralSegment literal => literal.Value.Length,
            LiteralSetSegment literalSet => GetMinimumLiteralSetLength(literalSet.Values),
            StartsWithSegment startsWith => startsWith.Value.Length,
            EndsWithSegment endsWith => endsWith.Value.Length,
            ContainsSegment contains => contains.Value.Length,

            MatchAnyCharacterSegment => 1,
            CharacterSetSegment => 1,
            CharacterSetInverseSegment => 1,
            CharacterRangeSegment => 1,
            CharacterRangeInverseSegment => 1,
            CharacterRangeIgnoreCaseSegment => 1,
            CharacterRangeIgnoreCaseInverseSegment => 1,
            CharacterClassSegment => 1,
            OrSegment => 1,

            _ => 0,
        };

        static int GetMinimumLiteralSetLength(string[] values)
        {
            if (values.Length == 0)
                return 0;

            var minLength = values[0].Length;
            for (var i = 1; i < values.Length; i++)
            {
                if (values[i].Length < minLength)
                {
                    minLength = values[i].Length;
                }
            }

            return minLength;
        }
    }

    private static char[]? GetLeadingCharacters(Segment segment)
    {
        switch (segment)
        {
            case LiteralSegment literal when literal.Value.Length > 0:
                return CreateCharacterSet([literal.Value[0]], literal.IgnoreCase);

            case CharacterSetSegment set:
                return CreateCharacterSet(set.Set.AsSpan(), set.IgnoreCase);

            case CharacterRangeSegment range when range.Range.Length <= 8:
                return range.Range.EnumerateCharacters();

            case LiteralSetSegment literalSet:
            {
                var result = new List<char>(literalSet.Values.Length);
                foreach (var value in literalSet.Values)
                {
                    // An empty alternative consumes nothing, so the segment does not require any first character.
                    if (value.Length == 0)
                        return null;

                    result.Add(value[0]);
                }

                if (result.Count == 0)
                    return null;

                return CreateCharacterSet([.. result], literalSet.IgnoreCase);
            }
        }

        return null;

        static char[]? CreateCharacterSet(ReadOnlySpan<char> characters, bool ignoreCase)
        {
            return IgnoreCaseExpansion.TryExpand(characters, ignoreCase, out var result) ? result : null;
        }
    }

    public override string ToString()
    {
        using var sb = new ValueStringBuilder();
        foreach (var item in _segments)
        {
            sb.Append(item.ToString());
        }

        return sb.ToString();
    }
}
