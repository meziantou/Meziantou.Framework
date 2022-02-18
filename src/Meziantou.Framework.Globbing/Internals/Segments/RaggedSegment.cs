using System.Diagnostics;

namespace Meziantou.Framework.Globbing.Internals;

internal sealed class RaggedSegment : Segment
{
    internal readonly Segment[] _segments;

    public RaggedSegment(Segment[] segments)
    {
        _segments = segments;
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        ReadOnlySpan<Segment> patternSegments = _segments;
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
                        Debug.Fail("Shouldn't happen");
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
                        if (Match(ref pathReader, remainingPatternSegments))
                            return true;
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
