namespace Meziantou.Framework.Globbing.Internals;

internal sealed class CharacterRangeIgnoreCaseSegment : Segment
{
    private readonly CharacterRange _range;

    public CharacterRangeIgnoreCaseSegment(CharacterRange range)
    {
        _range = range;
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.IsEndOfCurrentSegment)
            return false;

        var result = _range.IsInRangeIgnoreCase(pathReader.CurrentText[0]);
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
        sb.Append(_range.Min);
        sb.Append('-');
        sb.Append(_range.Max);
        sb.Append(']');
        return sb.ToString();
    }
}
