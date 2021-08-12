namespace Meziantou.Framework.Globbing.Internals;

internal sealed class OrSegment : Segment
{
    private readonly Segment[] _subSegments;
    private readonly bool _inverse;

    public OrSegment(Segment[] subSegments, bool inverse)
    {
        _subSegments = subSegments;
        _inverse = inverse;
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        var copy = pathReader;
        var result = MatchCore(ref copy);
        if (_inverse)
        {
            result = !result;
            if (result)
                return true;
        }

        pathReader = copy;
        return result;
    }

    private bool MatchCore(ref PathReader pathReader)
    {
        foreach (var subsegment in _subSegments)
        {
            var copy = pathReader;
            if (subsegment.IsMatch(ref copy))
            {
                pathReader = copy;
                return true;
            }
        }

        return false;
    }

    public override string ToString()
    {
        using var sb = new ValueStringBuilder();
        sb.Append('(');
        if (_inverse)
        {
            sb.Append('!');
        }

        foreach (var segment in _subSegments)
        {
            sb.Append(segment.ToString());
        }

        sb.Append(')');
        return sb.ToString();
    }
}
