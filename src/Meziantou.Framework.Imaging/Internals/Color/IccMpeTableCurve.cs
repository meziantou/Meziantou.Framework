namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A curve sampled at equally spaced inputs between a first and a last input, interpolated linearly, clipped or extended
/// linearly outside them (ICC.2:2023 sections 11.2.2.2 and 11.2.2.3).
/// </summary>
internal sealed class IccMpeTableCurve : IccMpeCurve
{
    private readonly double _first;
    private readonly double _last;
    private readonly bool _extrapolate;
    private readonly double[] _values;

    public IccMpeTableCurve(double first, double last, bool extrapolate, double[] values)
    {
        _first = first;
        _last = last;
        _extrapolate = extrapolate;
        _values = values;
    }

    public override double Evaluate(double value)
    {
        var step = (_last - _first) / (_values.Length - 1);
        if (value < _first)
            return _extrapolate ? _values[0] + ((value - _first) * (_values[1] - _values[0]) / step) : _values[0];

        if (value > _last)
            return _extrapolate ? _values[^1] + ((value - _last) * (_values[^1] - _values[^2]) / step) : _values[^1];

        if (double.IsNaN(value))
            return value;

        var position = (value - _first) / step;
        var index = Math.Min((int)position, _values.Length - 2);
        var fraction = position - index;
        return _values[index] + ((_values[index + 1] - _values[index]) * fraction);
    }
}
