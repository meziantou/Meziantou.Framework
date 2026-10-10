namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The interpolation kernels of <see cref="ResamplingFilter"/>, as functions of the distance
/// <c>x</c> between a source pixel center and the sampling position, in source pixels (divided by the filter scale when
/// downsampling). Every kernel is even, equals 1 at 0, and is exactly 0 at every non-zero integer and outside its radius,
/// so a resize at scale 1 reproduces its source exactly.
/// </summary>
internal static class ResamplingKernels
{
    /// <summary>Gets the radius of the kernel support (the kernel is zero for <c>|x| &gt;= radius</c>).</summary>
    public static double GetRadius(ResamplingFilter filter) => filter switch
    {
        ResamplingFilter.Bilinear => 1,
        ResamplingFilter.Bicubic => 2,
        ResamplingFilter.Lanczos3 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "The filter has no kernel."),
    };

    /// <summary>Evaluates the kernel.</summary>
    public static double Evaluate(ResamplingFilter filter, double x) => filter switch
    {
        ResamplingFilter.Bilinear => Triangle(x),
        ResamplingFilter.Bicubic => CatmullRom(x),
        ResamplingFilter.Lanczos3 => Lanczos3(x),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "The filter has no kernel."),
    };

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
    /// The three-lobe Lanczos kernel: <c>sinc(x) * sinc(x / 3)</c> with <c>sinc(x) = sin(pi x) / (pi x)</c>, evaluated as
    /// <c>3 sin(pi x) sin(pi x / 3) / (pi^2 x^2)</c> for <c>0 &lt; |x| &lt; 3</c>; 1 at 0; exactly 0 at the other integers
    /// and for <c>|x| &gt;= 3</c>.
    /// </summary>
    public static double Lanczos3(double x)
    {
        x = Math.Abs(x);
        if (x == 0)
            return 1;

        if (x >= 3 || x == Math.Floor(x))
            return 0;

        var px = Math.PI * x;
        return 3 * Math.Sin(px) * Math.Sin(px / 3) / (px * px);
    }
}
