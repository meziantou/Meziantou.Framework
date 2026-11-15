namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A sampled curve segment: N entries equally spaced after its first break-point (excluded), and the value of the previous
/// segment at that break-point, interpolated linearly (ICC.2:2023 Table 112).
/// </summary>
internal sealed class IccMpeSampledSegment : IccMpeSegment
{
    private readonly double _start;
    private readonly double _width;
    private readonly double _startValue;
    private readonly double[] _values;

    public IccMpeSampledSegment(double start, double end, double startValue, double[] values)
    {
        _start = start;
        _width = end - start;
        _startValue = startValue;
        _values = values;
    }

    public override double Evaluate(double value)
    {
        // Only called for values in (start, end], so the width is positive
        var position = Math.Clamp((value - _start) / _width, 0, 1) * _values.Length;
        var index = Math.Min((int)position, _values.Length - 1);
        var fraction = position - index;
        var low = index == 0 ? _startValue : _values[index - 1];
        return low + ((_values[index] - low) * fraction);
    }
}
