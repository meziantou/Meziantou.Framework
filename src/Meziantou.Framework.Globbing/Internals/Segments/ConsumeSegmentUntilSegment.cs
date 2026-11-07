using System.Buffers;

namespace Meziantou.Framework.Globbing.Internals.Segments;

internal sealed class ConsumeSegmentUntilSegment : Segment
{
    private readonly SearchValues<char> _characters;

    // The characters are expanded by the parser through IgnoreCaseExpansion when the case is ignored: this segment
    // skips ahead to the next character the following subsegment could match, so a missing character would silently
    // reject a matching path.
    public ConsumeSegmentUntilSegment(char[] characters)
    {
        _characters = SearchValues.Create(characters);
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        var index = pathReader.CurrentText.IndexOfAny(_characters);
        if (index == -1)
            return false;

        if (index > 0)
        {
            pathReader.ConsumeInSegment(index);
        }

        return true;
    }

    public override string ToString()
    {
        // The segment is a pure prefilter that scans ahead to the next character the following subsegment could
        // match, so it doesn't contribute anything to the textual pattern.
        return "";
    }
}
