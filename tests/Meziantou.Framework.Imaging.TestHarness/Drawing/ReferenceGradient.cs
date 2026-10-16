using System.Numerics;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>
/// The colors of a linear or radial gradient at the centers of the pixels, computed with rational numbers from the
/// definition: exact parameter (the square root of a radial gradient to 80 fractional bits), exact stop lookup, exact
/// interpolation of alpha-premultiplied values.
/// </summary>
/// <remarks>
/// A gradient is not continuous at a hard stop (two stops with the same offset) or where a repeated gradient starts
/// over, and the library evaluates the parameter in double precision: the samples accepted for a pixel are those of the
/// parameter and of the parameter moved by <see cref="ParameterWindow"/> on each side, each rounded to nearest with ties
/// upward (either neighbor within <see cref="RoundingWindow"/> of a rounding boundary).
/// </remarks>
public sealed class ReferenceGradient
{
    private static readonly Fraction Half = new(1, 2);

    private readonly bool _radial;
    private readonly Fraction _x0;
    private readonly Fraction _y0;
    private readonly Fraction _x1;
    private readonly Fraction _y1;
    private readonly Fraction _radius;
    private readonly IReadOnlyList<(Fraction Offset, ReferencePaint Paint)> _stops;
    private readonly ReferenceSpreadMode _spreadMode;
    private readonly ReferencePixelLayout _layout;

    private ReferenceGradient(bool radial, double x0, double y0, double x1, double y1, double radius, IReadOnlyList<(double Offset, ReferencePaint Paint)> stops, ReferenceSpreadMode spreadMode, ReferencePixelLayout layout)
    {
        _radial = radial;
        _x0 = Fraction.FromDouble(x0);
        _y0 = Fraction.FromDouble(y0);
        _x1 = Fraction.FromDouble(x1);
        _y1 = Fraction.FromDouble(y1);
        _radius = Fraction.FromDouble(radius);
        _stops = [.. stops.Select(stop => (Fraction.FromDouble(stop.Offset), stop.Paint))];
        _spreadMode = spreadMode;
        _layout = layout;
    }

    /// <summary>The distance by which the parameter is moved to find the samples accepted near a discontinuity.</summary>
    public static Fraction ParameterWindow { get; } = new(1, 1_000_000_000);

    /// <summary>The distance to a rounding boundary under which either neighbor is accepted, in sample units.</summary>
    public static Fraction RoundingWindow { get; } = new(1, 10_000_000);

    /// <summary>The tie bias of the rounding contract: a sample is <c>floor(v + 1/2 + 2^-20)</c>.</summary>
    public static Fraction TieBias { get; } = new(1, 1 << 20);

    /// <summary>Creates a gradient along the line from a point (offset 0) to another (offset 1).</summary>
    /// <param name="startX">The horizontal coordinate of offset 0.</param>
    /// <param name="startY">The vertical coordinate of offset 0.</param>
    /// <param name="endX">The horizontal coordinate of offset 1.</param>
    /// <param name="endY">The vertical coordinate of offset 1.</param>
    /// <param name="stops">The stops, by increasing offset, already converted to the layout.</param>
    /// <param name="spreadMode">The spread mode.</param>
    /// <param name="layout">The layout of the pixels.</param>
    /// <returns>The gradient.</returns>
    public static ReferenceGradient Linear(double startX, double startY, double endX, double endY, IReadOnlyList<(double Offset, ReferencePaint Paint)> stops, ReferenceSpreadMode spreadMode, ReferencePixelLayout layout)
        => new(radial: false, startX, startY, endX, endY, radius: 0, stops, spreadMode, layout);

    /// <summary>Creates a gradient around a center (offset 0), reaching offset 1 at a radius.</summary>
    /// <param name="centerX">The horizontal coordinate of the center.</param>
    /// <param name="centerY">The vertical coordinate of the center.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="stops">The stops, by increasing offset, already converted to the layout.</param>
    /// <param name="spreadMode">The spread mode.</param>
    /// <param name="layout">The layout of the pixels.</param>
    /// <returns>The gradient.</returns>
    public static ReferenceGradient Radial(double centerX, double centerY, double radius, IReadOnlyList<(double Offset, ReferencePaint Paint)> stops, ReferenceSpreadMode spreadMode, ReferencePixelLayout layout)
        => new(radial: true, centerX, centerY, centerX, centerY, radius, stops, spreadMode, layout);

    /// <summary>Gets the paints accepted for a pixel.</summary>
    /// <param name="x">The column of the pixel.</param>
    /// <param name="y">The row of the pixel.</param>
    /// <returns>The accepted paints (several near a discontinuity or a rounding boundary).</returns>
    public IReadOnlyList<ReferencePaint> GetAcceptedPaints(int x, int y)
    {
        var last = _stops[^1].Paint;
        var px = new Fraction((2 * (BigInteger)x) + 1, 2) - _x0;
        var py = new Fraction((2 * (BigInteger)y) + 1, 2) - _y0;
        Fraction parameter;
        if (_radial)
        {
            if (_radius == 0)
                return [last];

            parameter = Sqrt((px * px) + (py * py)) / _radius;
        }
        else
        {
            var ax = _x1 - _x0;
            var ay = _y1 - _y0;
            var lengthSquared = (ax * ax) + (ay * ay);
            if (lengthSquared == 0)
                return [last];

            parameter = ((px * ax) + (py * ay)) / lengthSquared;
        }

        var paints = new List<ReferencePaint>();
        foreach (var candidate in new[] { parameter, parameter - ParameterWindow, parameter + ParameterWindow })
        {
            foreach (var paint in Evaluate(candidate))
            {
                if (!paints.Any(existing => existing.Alpha == paint.Alpha && existing.Color.SequenceEqual(paint.Color)))
                {
                    paints.Add(paint);
                }
            }
        }

        return paints;
    }

    private List<ReferencePaint> Evaluate(Fraction parameter)
    {
        // Outside the range of a transparent spread, the gradient paints nothing
        if (_spreadMode == ReferenceSpreadMode.Transparent && (parameter < 0 || parameter > 1))
            return [new ReferencePaint(new int[_layout.ColorChannels], 0)];

        var t = _spreadMode switch
        {
            ReferenceSpreadMode.Repeat => parameter - new Fraction(parameter.Floor(), 1),
            ReferenceSpreadMode.Reflect => Reflect(parameter),
            _ => parameter < 0 ? 0 : parameter > 1 ? 1 : parameter,
        };

        if (t < _stops[0].Offset)
            return [_stops[0].Paint];

        var index = 0;
        for (var i = 1; i < _stops.Count; i++)
        {
            if (_stops[i].Offset <= t)
            {
                index = i;
            }
        }

        if (index == _stops.Count - 1)
            return [_stops[index].Paint];

        var from = _stops[index];
        var to = _stops[index + 1];
        var u = (t - from.Offset) / (to.Offset - from.Offset);
        Fraction fromAlpha = from.Paint.Alpha;
        Fraction toAlpha = to.Paint.Alpha;
        var alpha = fromAlpha + (u * (toAlpha - fromAlpha));
        var alphas = Round(alpha);
        var colors = new List<int[]>();
        if (alpha > 0)
        {
            for (var c = 0; c < _layout.ColorChannels; c++)
            {
                var start = (Fraction)from.Paint.Color[c] * fromAlpha;
                var end = (Fraction)to.Paint.Color[c] * toAlpha;
                colors.Add(Round((start + (u * (end - start))) / alpha));
            }
        }

        var result = new List<ReferencePaint>();
        foreach (var a in alphas)
        {
            if (a == 0 || colors.Count == 0)
            {
                // A paint without opacity paints nothing, whatever its color
                result.Add(new ReferencePaint(new int[_layout.ColorChannels], 0));
                continue;
            }

            foreach (var color in Combine(colors))
            {
                result.Add(new ReferencePaint(color, a));
            }
        }

        return result;
    }

    private static Fraction Reflect(Fraction parameter)
    {
        var u = parameter - new Fraction(2 * (parameter / 2).Floor(), 1);
        return u <= 1 ? u : (Fraction)2 - u;
    }

    /// <summary>Gets the samples accepted for an exact value: its rounding, and the other neighbor near the rounding boundary.</summary>
    private int[] Round(Fraction value)
    {
        var biased = value + TieBias;
        var floor = biased.Floor();
        var distance = biased - new Fraction(floor, 1) - Half;
        if (distance < 0)
        {
            distance = (Fraction)0 - distance;
        }

        int Clamp(BigInteger v) => (int)BigInteger.Clamp(v, 0, _layout.MaxValue);
        if (distance <= RoundingWindow)
            return [.. new[] { Clamp(floor), Clamp(floor + 1) }.Distinct()];

        return [Clamp((value + Half + TieBias).Floor())];
    }

    private static IEnumerable<int[]> Combine(List<int[]> channels)
    {
        IEnumerable<int[]> result = [[]];
        foreach (var candidates in channels)
        {
            result = [.. result.SelectMany(prefix => candidates.Select(candidate => (int[])[.. prefix, candidate]))];
        }

        return result;
    }

    /// <summary>Gets the square root of a non-negative rational, rounded down to 80 fractional bits.</summary>
    private static Fraction Sqrt(Fraction value)
    {
        const int Bits = 80;
        var scaled = (value.Numerator << (2 * Bits)) / value.Denominator;
        if (scaled.IsZero)
            return 0;

        // Newton iteration on integers, from above
        var root = BigInteger.One << (int)((scaled.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (root + (scaled / root)) >> 1;
            if (next >= root)
                break;

            root = next;
        }

        return new Fraction(root, BigInteger.One << Bits);
    }
}
