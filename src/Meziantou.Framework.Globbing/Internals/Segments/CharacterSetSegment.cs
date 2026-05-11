using Meziantou.Framework;

namespace Meziantou.Framework.Globbing.Internals;

internal sealed class CharacterSetSegment : Segment
{
    private readonly CharacterSetMatcher _matcher;

    public CharacterSetSegment(string set, bool ignoreCase)
    {
        Set = set;
        IgnoreCase = ignoreCase;
        _matcher = CharacterSetMatcher.Create(set, ignoreCase);
    }

    public bool IgnoreCase { get; }

    public string Set { get; }

    public override bool IsMatch(ref PathReader pathReader)
    {
        var c = pathReader.CurrentText[0];
        var result = _matcher.IsMatch(c);
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
        sb.Append(Set);
        sb.Append(']');
        return sb.ToString();
    }
}
