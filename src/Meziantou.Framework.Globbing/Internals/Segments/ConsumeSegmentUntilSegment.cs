namespace Meziantou.Framework.Globbing.Internals.Segments;

internal sealed class ConsumeSegmentUntilSegment : Segment
{
    private readonly char[] _characters;

    public ConsumeSegmentUntilSegment(char[] characters, bool ignoreCase)
    {
        if (!ignoreCase)
        {
            _characters = characters;
            return;
        }

        var expandedCharacters = new HashSet<char>();
        foreach (var character in characters)
        {
            expandedCharacters.Add(character);
            expandedCharacters.Add(char.ToLowerInvariant(character));
            expandedCharacters.Add(char.ToUpperInvariant(character));
        }

        _characters = [.. expandedCharacters];
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
}
