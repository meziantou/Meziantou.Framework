namespace Meziantou.Framework.Globbing.Internals;

internal sealed class CharacterSetInverseSegment : Segment
{
    private readonly StringComparison _stringComparison;

    public CharacterSetInverseSegment(string set, bool ignoreCase)
    {
        Set = set;
        _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public string Set { get; }

    public override bool IsMatch(ref PathReader pathReader)
    {
        bool result;
        var c = pathReader.CurrentText[0];
        result = !Set.Contains(c, _stringComparison);
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
        sb.Append('!');
        sb.Append(Set);
        sb.Append(']');
        return sb.ToString();
    }
}
