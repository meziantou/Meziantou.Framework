namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A curve made of segments between break-points (ICC.2:2023 section 11.2.2.4).</summary>
internal sealed class IccMpeSegmentedCurve : IccMpeCurve
{
    private readonly double[] _breakPoints;
    private readonly IccMpeSegment[] _segments;

    public IccMpeSegmentedCurve(double[] breakPoints, IccMpeSegment[] segments)
    {
        _breakPoints = breakPoints;
        _segments = segments;
    }

    public override double Evaluate(double value)
    {
        // The first segment whose interval contains the value: segment k covers (break-point k - 1, break-point k]
        var index = 0;
        while (index < _breakPoints.Length && !(value <= _breakPoints[index]))
        {
            index++;
        }

        return _segments[index].Evaluate(value);
    }
}
