namespace Meziantou.Framework.Globbing.Internals;

internal sealed class CharacterRangeIgnoreCaseSegment : Segment
{
    private readonly IgnoreCaseCharacterRange _range;

    public CharacterRangeIgnoreCaseSegment(CharacterRange range)
    {
        _range = new IgnoreCaseCharacterRange(range);
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.IsEndOfCurrentSegment)
            return false;

        var result = _range.Contains(pathReader.CurrentText[0]);
        if (result)
        {
            pathReader.ConsumeInSegment(1);
        }

        return result;
    }

    public override string ToString()
    {
        using var sb = new ValueStringBuilder();
        sb.Append('[');
        sb.Append(_range.Range.Min);
        sb.Append('-');
        sb.Append(_range.Range.Max);
        sb.Append(']');
        return sb.ToString();
    }
}
