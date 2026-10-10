namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The generalized inverse of an <see cref="IccCurve"/> on [0, 1]: for an increasing curve, the smallest input whose
/// output reaches the requested value (0 when even the first output does, 1 when none does). Immutable.
/// </summary>
/// <remarks>
/// A sampled curve is inverted exactly as the piecewise-linear function it is, after replacing it by its running maximum
/// (running minimum for a decreasing curve) so that small reversals do not matter. An increasing parametric curve is
/// inverted in closed form, segment by segment.
/// </remarks>
internal sealed class IccInverseCurve
{
    private const int FunctionIdentity = -1;
    private const int FunctionSampled = -2;

    private readonly int _function;
    private readonly double _g;
    private readonly double _a;
    private readonly double _b;
    private readonly double _c;
    private readonly double _d;
    private readonly double _e;
    private readonly double _f;

    /// <summary>The value of the power segment at its first input (types 3 and 4).</summary>
    private readonly double _powerStart;
    private readonly double[]? _envelope;
    private readonly bool _decreasing;

    private IccInverseCurve(int function, double g, double a, double b, double c, double d, double e, double f)
    {
        (_function, _g, _a, _b, _c, _d, _e, _f) = (function, g, a, b, c, d, e, f);
        if (function >= 3)
        {
            _powerStart = Math.Pow((a * d) + b, g) + e;
        }
    }

    private IccInverseCurve(double[] envelope, bool decreasing)
    {
        _function = FunctionSampled;
        _envelope = envelope;
        _decreasing = decreasing;
    }

    /// <summary>Gets the inverse of the identity curve.</summary>
    public static IccInverseCurve Identity { get; } = new(FunctionIdentity, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Creates the closed-form inverse of an increasing parametric curve (validated by the caller).</summary>
    public static IccInverseCurve CreateParametric(int function, double g, double a, double b, double c, double d, double e, double f)
    {
        // Types 0 to 2 have no linear segment; type 3 has neither e nor f
        if (function < 3)
        {
            d = 0;
            e = function == 2 ? c : 0;
            f = 0;
            c = 0;
        }
        else if (function == 3)
        {
            e = 0;
            f = 0;
        }

        return new IccInverseCurve(function, g, a, b, c, d, e, f);
    }

    /// <summary>Creates the inverse of a sampled curve.</summary>
    /// <param name="table">The normalized entries of the curve (not retained).</param>
    /// <param name="inverse">The inverse.</param>
    /// <returns><see langword="false"/> if the curve is constant.</returns>
    public static bool TryCreateSampled(ReadOnlySpan<double> table, [NotNullWhen(true)] out IccInverseCurve? inverse)
    {
        var decreasing = table[^1] < table[0];
        var envelope = new double[table.Length];
        var extremum = table[0];
        for (var i = 0; i < table.Length; i++)
        {
            var value = table[i];
            if (decreasing ? value < extremum : value > extremum)
            {
                extremum = value;
            }

            envelope[i] = extremum;
        }

        if (envelope[0] == envelope[^1])
        {
            inverse = null;
            return false;
        }

        inverse = new IccInverseCurve(envelope, decreasing);
        return true;
    }

    /// <summary>Evaluates the inverse.</summary>
    /// <param name="y">The curve output, clipped to [0, 1].</param>
    /// <returns>The curve input in [0, 1].</returns>
    public double Evaluate(double y)
    {
        y = IccCurve.Clip(y);
        return _function switch
        {
            FunctionIdentity => y,
            FunctionSampled => EvaluateSampled(y),
            0 => IccCurve.Clip(Math.Pow(y, 1 / _g)),
            1 or 2 => y <= _e ? 0 : IccCurve.Clip(InvertPower(y)),
            _ => IccCurve.Clip(EvaluateSegmented(y)),
        };
    }

    private double InvertPower(double y) => (Math.Pow(y - _e, 1 / _g) - _b) / _a;

    /// <summary>Inverts a type 3 or 4 curve: a linear segment below d, a power segment from d.</summary>
    private double EvaluateSegmented(double y)
    {
        if (_d > 0)
        {
            if (y <= _f)
                return 0;

            if (_c > 0)
            {
                var x = (y - _f) / _c;
                if (x < _d)
                    return x;
            }

            if (y <= _powerStart)
                return _d;
        }
        else if (y <= Math.Pow(_b, _g) + _e)
        {
            // The power segment covers [0, 1] and already reaches the value at its first input
            return 0;
        }

        return InvertPower(y);
    }

    private double EvaluateSampled(double y)
    {
        var envelope = _envelope!;
        var last = envelope.Length - 1;
        if (_decreasing)
        {
            if (y >= envelope[0])
                return 0;

            if (y < envelope[last])
                return 1;
        }
        else
        {
            if (y <= envelope[0])
                return 0;

            if (y > envelope[last])
                return 1;
        }

        // The smallest index whose entry reaches the value; the entry before it does not
        var low = 0;
        var high = last;
        while (high - low > 1)
        {
            var middle = (low + high) >>> 1;
            if (_decreasing ? envelope[middle] <= y : envelope[middle] >= y)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        var before = envelope[high - 1];
        return (high - 1 + ((y - before) / (envelope[high] - before))) / last;
    }
}
