namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>The kernels of the reference resampler.</summary>
public enum ReferenceKernel
{
    /// <summary>The source pixel containing the output pixel center (the higher one on a boundary).</summary>
    Nearest,

    /// <summary>The triangle kernel <c>max(0, 1 - |x|)</c>.</summary>
    Triangle,

    /// <summary>The Catmull-Rom cubic (Keys a = -0.5).</summary>
    CatmullRom,

    /// <summary>The three-lobe Lanczos windowed sinc.</summary>
    Lanczos3,

    /// <summary>The average of the source pixels whose centers lie in <c>(u - h, u + h]</c>, <c>h = max(1, 1 / scale) / 2</c>.</summary>
    Box,

    /// <summary>The Mitchell-Netravali cubic with B = 0, C = 0: <c>2|x|^3 - 3|x|^2 + 1</c> on <c>[0, 1)</c>.</summary>
    Hermite,

    /// <summary>The Mitchell-Netravali cubic with B = C = 1/3.</summary>
    MitchellNetravali,

    /// <summary>The Keys cubic with B = 12 / (19 + 9 sqrt(2)).</summary>
    Robidoux,

    /// <summary>The Keys cubic with B = 6 / (13 + 7 sqrt(2)).</summary>
    RobidouxSharp,

    /// <summary>The cubic B-spline (Mitchell-Netravali B = 1, C = 0).</summary>
    Spline,

    /// <summary>The two-lobe Lanczos windowed sinc.</summary>
    Lanczos2,

    /// <summary>The five-lobe Lanczos windowed sinc.</summary>
    Lanczos5,

    /// <summary>The eight-lobe Lanczos windowed sinc.</summary>
    Lanczos8,
}
