namespace Meziantou.Framework.Imaging;

/// <summary>
/// Selects what happens to the floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) that a narrower
/// pixel format cannot store: the samples outside [0, 1] of an integer format, and the finite samples beyond the range
/// of <see cref="Half"/> (±65,504).
/// </summary>
/// <remarks>Samples that are not numbers are always rejected: no policy gives them a value.</remarks>
public enum OutOfRangeSampleHandling
{
    /// <summary>The conversion throws an <see cref="UnsupportedImageFeatureException"/> before anything is written. This is the default.</summary>
    Reject = 0,

    /// <summary>
    /// Each sample is clipped to the range of the destination: [0, 1] for an integer format (infinities included), ±65,504
    /// for <see cref="RgbaHalf"/> (infinities are kept). Clipping a color component changes its hue: tone mapping
    /// (<see cref="PixelConversionOptions.ToneMapping"/>) keeps it.
    /// </summary>
    Clip = 1,
}
