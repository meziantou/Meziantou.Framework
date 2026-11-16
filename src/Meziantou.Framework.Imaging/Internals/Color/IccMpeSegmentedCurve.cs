using System.Numerics;

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

    // The steps of the search of the segment, then the evaluation of the segment
    public override long Cost => 4 + BitOperations.Log2((uint)_breakPoints.Length + 1) + 1;

    public override double Evaluate(double value)
    {
        // The first segment whose interval contains the value: segment k covers (break-point k - 1, break-point k]. The
        // break-points do not decrease: a binary search finds the first one not less than the value (none for NaN)
        var low = 0;
        var high = _breakPoints.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (value <= _breakPoints[middle])
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return _segments[low].Evaluate(value);
    }
}
