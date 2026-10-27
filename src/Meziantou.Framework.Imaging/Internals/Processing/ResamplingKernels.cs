namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The interpolation kernels of <see cref="ResamplingFilter"/>, as functions of the distance
/// <c>x</c> between a source pixel center and the sampling position, in source pixels (divided by the filter scale when
/// downsampling). Every kernel is zero outside its radius. The interpolating kernels (all but the Mitchell-Netravali,
/// Robidoux and B-spline cubics) equal 1 at 0 and are exactly 0 at every non-zero integer, so they reproduce an axis
/// resampled at scale 1 exactly.
/// </summary>
/// <remarks>
/// The cubics are the two-parameter family of D. P. Mitchell and A. N. Netravali, "Reconstruction Filters in Computer
/// Graphics", SIGGRAPH 1988; <c>C = (1 - B) / 2</c> gives the cubics of R. G. Keys, "Cubic Convolution Interpolation for
/// Digital Image Processing", 1981. The Robidoux parameters are those published by N. Robidoux (B of Robidoux:
/// <c>12 / (19 + 9 sqrt(2))</c>; of the sharp variant: <c>6 / (13 + 7 sqrt(2))</c>).
/// </remarks>
internal static class ResamplingKernels
{
    private static readonly double RobidouxB = 12 / (19 + (9 * Math.Sqrt(2)));
    private static readonly double RobidouxC = (1 - RobidouxB) / 2;
    private static readonly double RobidouxSharpB = 6 / (13 + (7 * Math.Sqrt(2)));
    private static readonly double RobidouxSharpC = (1 - RobidouxSharpB) / 2;

    /// <summary>Gets the radius of the kernel support (the kernel is zero for <c>|x| &gt;= radius</c>, <c>x &gt; radius</c> for <see cref="ResamplingFilter.Box"/>).</summary>
    public static double GetRadius(ResamplingFilter filter) => filter switch
    {
        ResamplingFilter.Box => 0.5,
        ResamplingFilter.Bilinear or ResamplingFilter.Hermite => 1,
        ResamplingFilter.Bicubic or ResamplingFilter.MitchellNetravali or ResamplingFilter.Robidoux or ResamplingFilter.RobidouxSharp or ResamplingFilter.Spline or ResamplingFilter.Lanczos2 => 2,
        ResamplingFilter.Lanczos3 => 3,
        ResamplingFilter.Lanczos5 => 5,
        ResamplingFilter.Lanczos8 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "The filter has no kernel."),
    };

    /// <summary>Evaluates the kernel.</summary>
    public static double Evaluate(ResamplingFilter filter, double x) => filter switch
    {
        ResamplingFilter.Box => Box(x),
        ResamplingFilter.Bilinear => Triangle(x),
        ResamplingFilter.Hermite => Cubic(x, 0, 0),
        ResamplingFilter.Bicubic => CatmullRom(x),
        ResamplingFilter.MitchellNetravali => Cubic(x, 1.0 / 3, 1.0 / 3),
        ResamplingFilter.Robidoux => Cubic(x, RobidouxB, RobidouxC),
        ResamplingFilter.RobidouxSharp => Cubic(x, RobidouxSharpB, RobidouxSharpC),
        ResamplingFilter.Spline => Cubic(x, 1, 0),
        ResamplingFilter.Lanczos2 => Lanczos(x, 2),
        ResamplingFilter.Lanczos3 => Lanczos3(x),
        ResamplingFilter.Lanczos5 => Lanczos(x, 5),
        ResamplingFilter.Lanczos8 => Lanczos(x, 8),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "The filter has no kernel."),
    };

    /// <summary>
    /// The box kernel: 1 for <c>-1/2 &lt; x &lt;= 1/2</c>, else 0. The interval is half-open like the contributors of a box
    /// resize (<see cref="ResampleAxis.GetBoxContributors"/>): a center on the boundary between two pixels selects the higher one.
    /// </summary>
    public static double Box(double x) => x > -0.5 && x <= 0.5 ? 1 : 0;

    /// <summary>The triangle (tent) kernel: <c>1 - |x|</c> for <c>|x| &lt; 1</c>, else 0.</summary>
    public static double Triangle(double x)
    {
        x = Math.Abs(x);
        return x < 1 ? 1 - x : 0;
    }

    /// <summary>
    /// The Catmull-Rom cubic (Keys cubic convolution with a = -0.5, Mitchell-Netravali B = 0, C = 0.5):
    /// <c>1.5|x|^3 - 2.5|x|^2 + 1</c> for <c>|x| &lt; 1</c>, <c>-0.5|x|^3 + 2.5|x|^2 - 4|x| + 2</c> for
    /// <c>1 &lt;= |x| &lt; 2</c>, else 0 (evaluated in Horner form).
    /// </summary>
    public static double CatmullRom(double x)
    {
        x = Math.Abs(x);
        if (x < 1)
            return (((1.5 * x) - 2.5) * x * x) + 1;

        if (x < 2)
            return (((((-0.5 * x) + 2.5) * x) - 4) * x) + 2;

        return 0;
    }

    /// <summary>
    /// The cubic of Mitchell and Netravali with parameters <paramref name="b"/> and <paramref name="c"/>:
    /// <c>((12 - 9B - 6C)|x|^3 + (-18 + 12B + 6C)|x|^2 + (6 - 2B)) / 6</c> for <c>|x| &lt; 1</c>,
    /// <c>((-B - 6C)|x|^3 + (6B + 30C)|x|^2 + (-12B - 48C)|x| + (8B + 24C)) / 6</c> for <c>1 &lt;= |x| &lt; 2</c>, else 0
    /// (evaluated in Horner form).
    /// </summary>
    public static double Cubic(double x, double b, double c)
    {
        x = Math.Abs(x);
        if (x < 1)
            return (((((12 - (9 * b) - (6 * c)) * x) + (-18 + (12 * b) + (6 * c))) * x * x) + (6 - (2 * b))) / 6;

        if (x < 2)
            return (((((((-b - (6 * c)) * x) + ((6 * b) + (30 * c))) * x) + ((-12 * b) - (48 * c))) * x) + (8 * b) + (24 * c)) / 6;

        return 0;
    }

    /// <summary>
    /// The three-lobe Lanczos kernel: <c>sinc(x) * sinc(x / 3)</c> with <c>sinc(x) = sin(pi x) / (pi x)</c> (see
    /// <see cref="Lanczos(double, int)"/>).
    /// </summary>
    public static double Lanczos3(double x) => Lanczos(x, 3);

    /// <summary>
    /// The Lanczos kernel of <paramref name="lobes"/> lobes: <c>sinc(x) * sinc(x / n)</c> with
    /// <c>sinc(x) = sin(pi x) / (pi x)</c>, evaluated as <c>n sin(pi x) sin(pi x / n) / (pi^2 x^2)</c> for
    /// <c>0 &lt; |x| &lt; n</c>; 1 at 0; exactly 0 at the other integers and for <c>|x| &gt;= n</c>.
    /// </summary>
    public static double Lanczos(double x, int lobes)
    {
        x = Math.Abs(x);
        if (x == 0)
            return 1;

        if (x >= lobes || x == Math.Floor(x))
            return 0;

        var px = Math.PI * x;
        return lobes * Math.Sin(px) * Math.Sin(px / lobes) / (px * px);
    }
}
