namespace Meziantou.Framework.Globbing.Internals;

internal sealed class CharacterRangeIgnoreCaseInverseSegment : Segment
{
    private readonly CharacterRange _range;

    public CharacterRangeIgnoreCaseInverseSegment(CharacterRange range)
    {
        if (CharacterRangeSegment.IsAsciiUpper(range.Min) && CharacterRangeSegment.IsAsciiUpper(range.Max))
        {
            _range = new CharacterRange(char.ToLowerInvariant(range.Min), char.ToLowerInvariant(range.Max));
        }
        else
        {
            _range = range;
        }
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        var c = pathReader.CurrentText[0];
        if (CharacterRangeSegment.IsAsciiUpper(c))
        {
            c = char.ToLowerInvariant(c);
        }

        var result = !_range.IsInRange(c);
        if (result)
        {
            pathReader.ConsumeInSegment(1);
        }

        return result;
    }

    public override string ToString()
    {
        using var sb = new ValueStringBuilder();
        sb.Append("[!");
        sb.Append(_range.Min);
        sb.Append('-');
        sb.Append(_range.Max);
        sb.Append(']');
        return sb.ToString();
    }
}
