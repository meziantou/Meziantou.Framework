namespace Meziantou.Framework.Imaging;

/// <summary>Selects the interpolation kernel used to resample pixels.</summary>
/// <remarks>
/// <para>When downsampling, kernel support is widened proportionally to the scale factor and coefficients are normalized.</para>
/// <para>
/// The kernels of <see cref="NearestNeighbor"/>, <see cref="Box"/>, <see cref="Bilinear"/>, <see cref="Hermite"/>,
/// <see cref="Bicubic"/> and the Lanczos filters interpolate: an axis that keeps its size and position keeps its pixels.
/// <see cref="MitchellNetravali"/>, <see cref="Robidoux"/>, <see cref="RobidouxSharp"/> and <see cref="Spline"/> do not:
/// they also smooth such an axis, for example the width of an image whose height only changes. A resize that keeps the
/// whole image in place leaves it unchanged whatever the filter.
/// </para>
/// </remarks>
public enum ResamplingFilter
{
    /// <summary>Nearest-neighbor sampling (pixel centers, no blending).</summary>
    NearestNeighbor = 0,

    /// <summary>Bilinear (triangle) interpolation, with a support radius of 1.</summary>
    Bilinear = 1,

    /// <summary>
    /// Bicubic Catmull-Rom interpolation (the cubic of Mitchell and Netravali with <c>B = 0</c> and <c>C = 1/2</c>), with a
    /// support radius of 2. This is the default of <see cref="ResizeOptions.Filter"/>.
    /// </summary>
    Bicubic = 2,

    /// <summary>Three-lobe Lanczos windowed sinc interpolation, with a support radius of 3.</summary>
    Lanczos3 = 3,

    /// <summary>
    /// Box filter (area averaging): each output pixel is the average of the source pixels whose centers lie in its footprint,
    /// the boundary at its end included. The footprints of an integer downscale factor tile the source exactly, which makes
    /// this the filter of choice for such reductions; when upsampling, it selects the same pixels as
    /// <see cref="NearestNeighbor"/>.
    /// </summary>
    Box = 4,

    /// <summary>
    /// Hermite cubic interpolation (the cubic of Mitchell and Netravali with <c>B = 0</c> and <c>C = 0</c>):
    /// <c>2|x|^3 - 3|x|^2 + 1</c>, with a support radius of 1. Smoother than <see cref="Bilinear"/>, without overshoot.
    /// </summary>
    Hermite = 5,

    /// <summary>
    /// The cubic of Mitchell and Netravali with <c>B = C = 1/3</c>, with a support radius of 2: a compromise between blur,
    /// ringing and aliasing.
    /// </summary>
    MitchellNetravali = 6,

    /// <summary>
    /// The Robidoux cubic: the Keys cubic (<c>C = (1 - B) / 2</c>) with <c>B = 12 / (19 + 9 sqrt(2))</c>, about 0.3782, with a
    /// support radius of 2. Close to <see cref="MitchellNetravali"/>, slightly sharper.
    /// </summary>
    Robidoux = 7,

    /// <summary>
    /// The sharp Robidoux cubic: the Keys cubic (<c>C = (1 - B) / 2</c>) with <c>B = 6 / (13 + 7 sqrt(2))</c>, about 0.2620,
    /// with a support radius of 2. Between <see cref="Robidoux"/> and <see cref="Bicubic"/>.
    /// </summary>
    RobidouxSharp = 8,

    /// <summary>
    /// The cubic B-spline (the cubic of Mitchell and Netravali with <c>B = 1</c> and <c>C = 0</c>), with a support radius of 2:
    /// the smoothest cubic, without overshoot.
    /// </summary>
    Spline = 9,

    /// <summary>Two-lobe Lanczos windowed sinc interpolation, with a support radius of 2.</summary>
    Lanczos2 = 10,

    /// <summary>Five-lobe Lanczos windowed sinc interpolation, with a support radius of 5.</summary>
    Lanczos5 = 11,

    /// <summary>Eight-lobe Lanczos windowed sinc interpolation, with a support radius of 8.</summary>
    Lanczos8 = 12,
}
