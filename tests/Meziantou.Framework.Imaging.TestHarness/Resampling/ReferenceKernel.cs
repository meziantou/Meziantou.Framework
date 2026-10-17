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
}
