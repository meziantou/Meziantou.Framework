using System.Buffers;

namespace Meziantou.Framework.Globbing.Internals.Segments;

internal sealed class ConsumeSegmentUntilSegment : Segment
{
    private readonly SearchValues<char> _characters;

    public ConsumeSegmentUntilSegment(char[] characters, bool ignoreCase)
    {
        if (!ignoreCase)
        {
            _characters = SearchValues.Create(characters);
            return;
        }

        var expandedCharacters = new HashSet<char>();
        foreach (var character in characters)
        {
            expandedCharacters.Add(character);
            expandedCharacters.Add(char.ToLowerInvariant(character));
            expandedCharacters.Add(char.ToUpperInvariant(character));
        }

        _characters = SearchValues.Create([.. expandedCharacters]);
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
