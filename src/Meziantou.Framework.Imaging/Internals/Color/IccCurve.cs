namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A one-dimensional ICC curve on normalized values: a <c>curveType</c> (ICC.1:2022 section 10.6: identity, gamma or
/// sampled with linear interpolation), a <c>parametricCurveType</c> (section 10.18, function types 0 to 4), or a table of
/// a <c>lut8Type</c>/<c>lut16Type</c>. Immutable.
/// </summary>
/// <remarks>
/// <see cref="Evaluate"/> clips its input and its output to [0, 1] and maps a result that is not a number to 0 (section
/// 10.18: values outside the range are clipped). <see cref="TryCreateInverse"/> gives the generalized inverse: the
/// smallest input whose output reaches a value, which is the ordinary inverse where the curve is strictly monotonic and
/// picks the start of a flat segment otherwise. Small reversals of a sampled curve are ignored by inverting its running
/// extremum.
/// </remarks>
internal sealed class IccCurve
{
    /// <summary>The number of samples used to invert a parametric curve that has no usable closed-form inverse.</summary>
    private const int FallbackInverseSamples = 4096;

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
    private readonly double[]? _table;

    private IccCurve(int function, ReadOnlySpan<double> parameters)
    {
        _function = function;
        Span<double> p = stackalloc double[7];
        parameters.CopyTo(p);
        (_g, _a, _b, _c, _d, _e, _f) = (p[0], p[1], p[2], p[3], p[4], p[5], p[6]);
    }

    private IccCurve(double[] table)
    {
        _function = FunctionSampled;
        _table = table;
    }

    /// <summary>Gets the identity curve.</summary>
    public static IccCurve Identity { get; } = new(FunctionIdentity, []);

    /// <summary>Gets a value indicating whether the curve is the identity by construction (a <c>curveType</c> without entry).</summary>
    public bool IsIdentity => _function == FunctionIdentity;

    /// <summary>Creates a sampled curve from normalized entries, interpolated linearly over [0, 1].</summary>
    /// <param name="table">The entries (at least two), owned by the curve.</param>
    public static IccCurve FromTable(double[] table) => new(table);

    /// <summary>Creates the curve <c>y = x^gamma</c>.</summary>
    public static IccCurve FromGamma(double gamma) => new(0, [gamma]);

    /// <summary>Gets the number of parameters of a parametric function type, or 0 for an undefined type.</summary>
    public static int GetParameterCount(int function) => function switch { 0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 7, _ => 0 };

    /// <summary>Parses a <c>curveType</c> or <c>parametricCurveType</c> element.</summary>
    /// <param name="data">The data starting at the type signature; it may extend past the element.</param>
    /// <param name="curve">The curve.</param>
    /// <param name="length">The number of bytes of the element (not padded).</param>
    /// <returns><see langword="false"/> if the element is truncated, of another type, or uses an undefined function type.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out IccCurve? curve, out int length)
    {
        curve = null;
        length = 0;
        if (data.Length < 12)
            return false;

        var type = IccReader.ReadUInt32(data);
        if (type == IccReader.TypeCurve)
        {
            var count = IccReader.ReadUInt32(data[8..]);
            if (count > (uint)(data.Length - 12) / 2)
                return false;

            length = 12 + (2 * (int)count);
            if (count == 0)
            {
                curve = Identity;
            }
            else if (count == 1)
            {
                curve = FromGamma(IccReader.ReadU8Fixed8(data[12..]));
            }
            else
            {
                var table = new double[count];
                for (var i = 0; i < table.Length; i++)
                {
                    table[i] = IccReader.ReadUInt16(data[(12 + (2 * i))..]) / 65535.0;
                }

                curve = new IccCurve(table);
            }

            return true;
        }

        if (type == IccReader.TypeParametricCurve)
        {
            int function = IccReader.ReadUInt16(data[8..]);
            var parameterCount = GetParameterCount(function);
            if (parameterCount == 0 || data.Length < 12 + (4 * parameterCount))
                return false;

            Span<double> parameters = stackalloc double[7];
            for (var i = 0; i < parameterCount; i++)
            {
                parameters[i] = IccReader.ReadS15Fixed16(data[(12 + (4 * i))..]);
            }

            length = 12 + (4 * parameterCount);
            curve = new IccCurve(function, parameters[..parameterCount]);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Evaluates an ICC parametric curve (ICC.1:2022 section 10.18, Table 68) with parameters g, a, b, c, d, e, f, without
    /// clipping the result.
    /// </summary>
    public static double EvaluateParametric(int function, ReadOnlySpan<double> p, double x)
    {
        var (g, a, b, c, d, e, f) = (p[0], p[1], p[2], p[3], p[4], p[5], p[6]);
        return EvaluateParametric(function, g, a, b, c, d, e, f, x);
    }

    /// <summary>Evaluates the curve.</summary>
    /// <param name="x">The input, clipped to [0, 1].</param>
    /// <returns>The output in [0, 1].</returns>
    public double Evaluate(double x)
    {
        x = Clip(x);
        if (_function == FunctionIdentity)
            return x;

        if (_table is { } table)
            return Interpolate(table, x);

        return Clip(EvaluateParametric(_function, _g, _a, _b, _c, _d, _e, _f, x));
    }

    /// <summary>Creates the generalized inverse of the curve.</summary>
    /// <param name="inverse">The inverse curve.</param>
    /// <returns><see langword="false"/> if the curve is constant over [0, 1] and therefore cannot be inverted.</returns>
    public bool TryCreateInverse([NotNullWhen(true)] out IccInverseCurve? inverse)
    {
        if (_function == FunctionIdentity)
        {
            inverse = IccInverseCurve.Identity;
            return true;
        }

        if (_table is null && HasClosedFormInverse())
        {
            inverse = IccInverseCurve.CreateParametric(_function, _g, _a, _b, _c, _d, _e, _f);
            return true;
        }

        var table = _table;
        if (table is null)
        {
            table = new double[FallbackInverseSamples];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = Evaluate((double)i / (table.Length - 1));
            }
        }

        return IccInverseCurve.TryCreateSampled(table, out inverse);
    }

    /// <summary>
    /// Determines whether the parametric curve is increasing with well-formed parameters, so that each of its segments
    /// can be inverted in closed form. Any other parametric curve is inverted through samples.
    /// </summary>
    private bool HasClosedFormInverse()
    {
        if (!(double.IsFinite(_g) && _g > 0))
            return false;

        if (_function == 0)
            return true;

        if (!(_a > 0 && double.IsFinite(_b) && double.IsFinite(_c) && double.IsFinite(_e) && double.IsFinite(_f)))
            return false;

        // Types 3 and 4 have a linear segment below d: it must be increasing or flat, and d must be inside the power segment domain
        return _function < 3 || (_c >= 0 && double.IsFinite(_d) && (_a * _d) + _b >= 0);
    }

    private static double EvaluateParametric(int function, double g, double a, double b, double c, double d, double e, double f, double x)
        => function switch
        {
            0 => Math.Pow(x, g),
            1 => x >= -b / a ? Math.Pow((a * x) + b, g) : 0,
            2 => x >= -b / a ? Math.Pow((a * x) + b, g) + c : c,
            3 => x >= d ? Math.Pow((a * x) + b, g) : c * x,
            _ => x >= d ? Math.Pow((a * x) + b, g) + e : (c * x) + f,
        };

    /// <summary>Interpolates a table linearly; the entries are spread evenly over [0, 1].</summary>
    internal static double Interpolate(double[] table, double x)
    {
        var position = x * (table.Length - 1);
        var index = (int)position;
        if (index >= table.Length - 1)
            return table[^1];

        var low = table[index];
        return low + ((table[index + 1] - low) * (position - index));
    }

    /// <summary>Clips a value to [0, 1]; a value that is not a number becomes 0.</summary>
    internal static double Clip(double value) => value >= 1 ? 1 : value > 0 ? value : 0;
}
