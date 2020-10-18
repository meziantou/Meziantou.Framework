using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class RaggedSegment : Segment
    {
        private readonly SubSegment[] _segments;

        public RaggedSegment(SubSegment[] segments)
        {
            _segments = segments;
        }

        public override bool Match(ReadOnlySpan<char> segment)
        {
            ReadOnlySpan<SubSegment> patternSegments = _segments;
            return Match(segment, patternSegments);

            static bool Match(ReadOnlySpan<char> pathSegment, ReadOnlySpan<SubSegment> patternSegments)
            {
                for (var i = 0; i < patternSegments.Length; i++)
                {
                    var patternSegment = patternSegments[i];
                    if (patternSegment is MatchAllSubSegment)
                    {
                        var remainingPatternSegments = patternSegments[(i + 1)..];
                        if (Match(pathSegment, remainingPatternSegments))
                            return true;

                        while (!pathSegment.IsEmpty)
                        {
                            pathSegment = pathSegment[1..];
                            if (Match(pathSegment, remainingPatternSegments))
                                return true;
                        }

                        return false;
                    }
                    else
                    {
                        if (!patternSegment.Match(pathSegment, out var readCharCount))
                            return false;

                        pathSegment = pathSegment[readCharCount..];
                    }
                }

                return pathSegment.IsEmpty;
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
}
