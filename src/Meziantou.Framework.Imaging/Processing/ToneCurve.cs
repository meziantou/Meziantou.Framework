namespace Meziantou.Framework.Imaging;

/// <summary>
/// An immutable tone curve: a function that gives a new value to a sample value, for
/// <see cref="ImageProcessingExtensions.ApplyToneCurve(Image, ToneCurve, CancellationToken)"/> and
/// <see cref="ImageProcessingExtensions.ApplyToneCurves(Image, ToneCurve, ToneCurve, ToneCurve, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Values are fractions of the largest sample value: 0 is black and 1 is full intensity, for every pixel format (a level
/// <c>v</c> of an 8-bit sample is <c>v / 255</c>). A curve is made of a gamma correction (<see cref="CreateGamma"/>), a
/// levels adjustment (<see cref="CreateLevels"/>), a smooth curve through control points (<see cref="CreateFromPoints"/>),
/// a one-dimensional lookup table (<see cref="CreateFromTable"/>), or a sequence of them (<see cref="Then"/>), and
/// <see cref="Evaluate"/> gives its value anywhere.
/// </para>
/// <para>
/// Every function is defined for every number, so that floating-point samples outside [0, 1] (highlights, negative
/// out-of-gamut colors) have a value too: see each factory method. A value that is not a number stays one.
/// </para>
/// </remarks>
public sealed class ToneCurve
{
    private readonly ToneStage[] _stages;

    private ToneCurve(ToneStage[] stages)
    {
        _stages = stages;
    }

    /// <summary>Gets the curve that keeps every value.</summary>
    public static ToneCurve Identity { get; } = new([]);

    /// <summary>Gets a value indicating whether the curve keeps every value: it is <see cref="Identity"/>, or a sequence of curves that keep every value.</summary>
    public bool IsIdentity => _stages.Length == 0;

    /// <summary>
    /// Creates a gamma correction: a value <c>v</c> becomes <c>v^(1 / gamma)</c>, so a gamma above 1 brightens the midtones
    /// and a gamma below 1 darkens them, while 0 and 1 are kept. It is the gamma of ImageMagick's <c>-gamma</c> and of the
    /// middle slider of the levels of Photoshop and GIMP.
    /// </summary>
    /// <remarks>A negative value <c>v</c> becomes <c>-(-v)^(1 / gamma)</c>, so the curve is odd and keeps the sign.</remarks>
    /// <param name="gamma">The gamma. Must be a positive finite number; 1 gives <see cref="Identity"/>.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gamma"/> is not a positive finite number.</exception>
    public static ToneCurve CreateGamma(double gamma)
    {
        ValidateGamma(gamma, nameof(gamma));
        return gamma == 1 ? Identity : new([new GammaStage(1 / gamma)]);
    }

    /// <summary>
    /// Creates a levels adjustment: the values from <paramref name="inputBlack"/> to <paramref name="inputWhite"/> are
    /// stretched to [0, 1], clipped, corrected by <paramref name="gamma"/>, and stretched to the range from
    /// <paramref name="outputBlack"/> to <paramref name="outputWhite"/>.
    /// </summary>
    /// <remarks>
    /// A value <c>v</c> becomes <c>outputBlack + (outputWhite - outputBlack) * t^(1 / gamma)</c>, where
    /// <c>t = clamp((v - inputBlack) / (inputWhite - inputBlack), 0, 1)</c>: the levels of Photoshop and GIMP, and
    /// ImageMagick's <c>-level</c> followed by <c>+level</c>. Every value below <paramref name="inputBlack"/> becomes
    /// <paramref name="outputBlack"/> and every value above <paramref name="inputWhite"/> becomes
    /// <paramref name="outputWhite"/>, floating-point values outside [0, 1] included: the input range clips. An output
    /// black above the output white inverts the values.
    /// </remarks>
    /// <param name="inputBlack">The value that becomes <paramref name="outputBlack"/>. Must be finite.</param>
    /// <param name="inputWhite">The value that becomes <paramref name="outputWhite"/>. Must be finite and greater than <paramref name="inputBlack"/>.</param>
    /// <param name="gamma">The gamma correction of the midtones, as <see cref="CreateGamma"/>. Must be a positive finite number.</param>
    /// <param name="outputBlack">The darkest value of the result. Must be finite.</param>
    /// <param name="outputWhite">The brightest value of the result. Must be finite.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentException">A value is not finite, or <paramref name="inputWhite"/> is not greater than <paramref name="inputBlack"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gamma"/> is not a positive finite number.</exception>
    public static ToneCurve CreateLevels(double inputBlack, double inputWhite, double gamma = 1, double outputBlack = 0, double outputWhite = 1)
    {
        ValidateFinite(inputBlack, nameof(inputBlack));
        ValidateFinite(inputWhite, nameof(inputWhite));
        ValidateGamma(gamma, nameof(gamma));
        ValidateFinite(outputBlack, nameof(outputBlack));
        ValidateFinite(outputWhite, nameof(outputWhite));
        if (!(inputWhite > inputBlack))
            throw new ArgumentException("The input white must be greater than the input black.", nameof(inputWhite));

        return new([new LevelsStage(inputBlack, inputWhite, 1 / gamma, outputBlack, outputWhite)]);
    }

    /// <summary>
    /// Creates a smooth curve through control points: the curves of photo editors. Between two points, the curve is the
    /// monotone piecewise cubic Hermite interpolation of Fritsch and Carlson (PCHIP): it goes through every point, never
    /// overshoots them, and rises (or falls) wherever the points do, so that it never inverts the tones between two points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slopes at the points are those of SciPy's <c>PchipInterpolator</c> (and of MATLAB's <c>pchip</c>): 0 at a point
    /// where the curve changes direction, the weighted harmonic mean of the slopes of the two segments elsewhere, and a
    /// three-point estimate at the first and the last point. Two points give a straight line.
    /// </para>
    /// <para>
    /// Before the first point and after the last one, the curve is flat: it keeps the value of the nearest point, as in
    /// Photoshop and GIMP. To clip the shadows, start at <c>(0.1, 0)</c>; to keep the whole range, start at <c>(0, 0)</c>
    /// and end at <c>(1, 1)</c>.
    /// </para>
    /// </remarks>
    /// <param name="points">At least two points, ordered by increasing <see cref="PointD.X"/>, with finite coordinates; <see cref="PointD.X"/> is the input value and <see cref="PointD.Y"/> the output value. They are copied.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentException">There are fewer than two points, a coordinate is not finite, or the <see cref="PointD.X"/> coordinates are not strictly increasing.</exception>
    public static ToneCurve CreateFromPoints(params ReadOnlySpan<PointD> points)
    {
        if (points.Length < 2)
            throw new ArgumentException("A curve needs at least two points.", nameof(points));

        var x = new double[points.Length];
        var y = new double[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Y))
                throw new ArgumentException("The coordinates of the points must be finite.", nameof(points));

            if (i > 0 && !(points[i].X > points[i - 1].X))
                throw new ArgumentException("The X coordinates of the points must be strictly increasing.", nameof(points));

            x[i] = points[i].X;
            y[i] = points[i].Y;
        }

        return new([new MonotoneCubicStage(x, y)]);
    }

    /// <summary>
    /// Creates a curve from a one-dimensional lookup table: the values of the curve at evenly spaced inputs from 0 to 1,
    /// interpolated linearly in between. A table of <c>n</c> values gives value <c>i</c> to the input <c>i / (n - 1)</c>.
    /// </summary>
    /// <remarks>
    /// Before 0 and after 1, the curve keeps the first and the last value of the table. A table of 256 values applied to an
    /// 8-bit sample reads exactly one value per level, without interpolation.
    /// </remarks>
    /// <param name="values">At least two finite values. They are copied.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentException">There are fewer than two values, or a value is not finite.</exception>
    public static ToneCurve CreateFromTable(params ReadOnlySpan<double> values)
    {
        if (values.Length < 2)
            throw new ArgumentException("A table needs at least two values.", nameof(values));

        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The values of the table must be finite.", nameof(values));
        }

        return new([new TableStage(values.ToArray())]);
    }

    /// <summary>Gets the curve that applies this one, then another one to its result.</summary>
    /// <param name="next">The curve applied to the values of this one.</param>
    /// <returns>The combined curve: <c>next.Evaluate(this.Evaluate(v))</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> is <see langword="null"/>.</exception>
    public ToneCurve Then(ToneCurve next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.IsIdentity)
            return this;

        if (IsIdentity)
            return next;

        return new([.. _stages, .. next._stages]);
    }

    /// <summary>Gets the value of the curve at a value.</summary>
    /// <param name="value">The value, a fraction of the largest sample value.</param>
    /// <returns>The value of the curve; <see cref="double.NaN"/> when <paramref name="value"/> is not a number.</returns>
    public double Evaluate(double value)
    {
        foreach (var stage in _stages)
        {
            value = stage.Evaluate(value);
        }

        return value;
    }

    private static void ValidateGamma(double gamma, string paramName)
    {
        if (!(gamma > 0 && double.IsFinite(gamma)))
            throw new ArgumentOutOfRangeException(paramName, gamma, "The gamma must be a positive finite number.");
    }

    private static void ValidateFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The value must be finite.", paramName);
    }

    private abstract class ToneStage
    {
        public abstract double Evaluate(double value);
    }

    private sealed class GammaStage(double exponent) : ToneStage
    {
        public override double Evaluate(double value) => value < 0 ? -Math.Pow(-value, exponent) : Math.Pow(value, exponent);
    }

    private sealed class LevelsStage(double inputBlack, double inputWhite, double exponent, double outputBlack, double outputWhite) : ToneStage
    {
        public override double Evaluate(double value)
        {
            if (double.IsNaN(value))
                return value;

            var t = Math.Clamp((value - inputBlack) / (inputWhite - inputBlack), 0, 1);
            if (exponent != 1)
            {
                t = Math.Pow(t, exponent);
            }

            return outputBlack + ((outputWhite - outputBlack) * t);
        }
    }

    private sealed class TableStage(double[] values) : ToneStage
    {
        public override double Evaluate(double value)
        {
            if (double.IsNaN(value))
                return value;

            if (value <= 0)
                return values[0];

            var last = values.Length - 1;
            if (value >= 1)
                return values[last];

            var position = value * last;
            var index = Math.Min((int)position, last - 1);
            var fraction = position - index;
            return values[index] + (fraction * (values[index + 1] - values[index]));
        }
    }

    /// <summary>The monotone piecewise cubic Hermite interpolation of Fritsch and Carlson, with the slopes of SciPy's <c>PchipInterpolator</c>.</summary>
    private sealed class MonotoneCubicStage : ToneStage
    {
        private readonly double[] _x;
        private readonly double[] _y;

        // The coefficients of each segment, from the cubic term: y = ((c0 s + c1) s + c2) s + y[k], s = v - x[k]
        private readonly double[] _coefficients;

        public MonotoneCubicStage(double[] x, double[] y)
        {
            _x = x;
            _y = y;
            var n = x.Length;
            var h = new double[n - 1];
            var m = new double[n - 1];
            for (var k = 0; k < n - 1; k++)
            {
                h[k] = x[k + 1] - x[k];
                m[k] = (y[k + 1] - y[k]) / h[k];
            }

            var d = new double[n];
            if (n == 2)
            {
                d[0] = m[0];
                d[1] = m[0];
            }
            else
            {
                for (var k = 1; k < n - 1; k++)
                {
                    if (m[k - 1] == 0 || m[k] == 0 || Math.Sign(m[k - 1]) != Math.Sign(m[k]))
                    {
                        d[k] = 0;
                    }
                    else
                    {
                        var w1 = (2 * h[k]) + h[k - 1];
                        var w2 = h[k] + (2 * h[k - 1]);
                        d[k] = 1 / (((w1 / m[k - 1]) + (w2 / m[k])) / (w1 + w2));
                    }
                }

                d[0] = GetEndSlope(h[0], h[1], m[0], m[1]);
                d[n - 1] = GetEndSlope(h[n - 2], h[n - 3], m[n - 2], m[n - 3]);
            }

            _coefficients = new double[3 * (n - 1)];
            for (var k = 0; k < n - 1; k++)
            {
                _coefficients[3 * k] = (d[k] + d[k + 1] - (2 * m[k])) / (h[k] * h[k]);
                _coefficients[(3 * k) + 1] = ((3 * m[k]) - (2 * d[k]) - d[k + 1]) / h[k];
                _coefficients[(3 * k) + 2] = d[k];
            }
        }

        public override double Evaluate(double value)
        {
            if (double.IsNaN(value))
                return value;

            if (value <= _x[0])
                return _y[0];

            var last = _x.Length - 1;
            if (value >= _x[last])
                return _y[last];

            // The segment whose start is the last point at or before the value
            var index = Array.BinarySearch(_x, value);
            var k = index >= 0 ? index : ~index - 1;
            var s = value - _x[k];
            return (((((_coefficients[3 * k] * s) + _coefficients[(3 * k) + 1]) * s) + _coefficients[(3 * k) + 2]) * s) + _y[k];
        }

        /// <summary>The slope at an end point: the three-point estimate, made 0 against the slope of its segment and limited to three times that slope at a change of direction.</summary>
        private static double GetEndSlope(double h0, double h1, double m0, double m1)
        {
            var d = ((((2 * h0) + h1) * m0) - (h0 * m1)) / (h0 + h1);
            if (Math.Sign(m0) != Math.Sign(m1) && Math.Abs(d) > 3 * Math.Abs(m0))
                return 3 * m0;

            if (Math.Sign(d) != Math.Sign(m0))
                return 0;

            return d;
        }
    }
}
