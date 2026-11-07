namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options controlling explicit conversions between pixel formats (typed loading and <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>).</summary>
/// <remarks>
/// <para>
/// Conversions never silently discard alpha: converting non-opaque pixels to a format without alpha throws an
/// <see cref="UnsupportedImageFeatureException"/> unless <see cref="BackgroundColor"/> is set. Converting fully opaque
/// pixels never requires a background. Requesting a narrower pixel type (for example <see cref="Rgba32"/> from 16-bit
/// content) is an explicit precision reduction and uses nearest rounding.
/// </para>
/// <para>
/// Integer samples convert to floating-point ones as <c>v / max</c>: exactly for <see cref="RgbaFloat"/>, rounded to nearest
/// for <see cref="RgbaHalf"/> (exactly from 8-bit samples). Floating-point samples convert to integer ones as
/// <c>round(v * max)</c>, to nearest; a sample that is not a number is always rejected, and a sample outside [0, 1] is
/// rejected unless <see cref="OutOfRangeSamples"/> clips it or <see cref="ToneMapping"/> compresses the highlights first.
/// Integer to <see cref="RgbaFloat"/> and back is exact for every integer pixel format, and so is 8-bit to <see cref="RgbaHalf"/> and back.
/// </para>
/// <para>
/// Layout conversion is not color management: no ICC transform is applied (use
/// <see cref="ImageProcessingExtensions.ConvertColorProfile"/> to convert colors). A retained ICC profile whose color space is
/// incompatible with the converted pixels (for example an RGB profile on grayscale pixels) causes an
/// <see cref="UnsupportedImageFeatureException"/> unless <see cref="DiscardIncompatibleColorProfile"/> is <see langword="true"/>.
/// </para>
/// </remarks>
public sealed class PixelConversionOptions
{
    /// <summary>Gets the default options: no background color, incompatible profiles are rejected.</summary>
    public static PixelConversionOptions Default { get; } = new();

    /// <summary>
    /// Gets the opaque color used to flatten non-opaque pixels when the target pixel type has no alpha component, or
    /// <see langword="null"/> to reject non-opaque pixels. The color is interpreted in the encoded color space of the
    /// source pixels (sRGB when untagged); flattening is performed in that encoded space.
    /// </summary>
    /// <exception cref="ArgumentException">The color is not fully opaque.</exception>
    public Rgba64? BackgroundColor
    {
        get;
        init
        {
            if (value is { A: not ushort.MaxValue })
                throw new ArgumentException("The background color must be fully opaque.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether an ICC profile incompatible with the converted pixels is removed instead of causing an error.</summary>
    public bool DiscardIncompatibleColorProfile { get; init; }

    /// <summary>
    /// Gets what happens to the floating-point samples that the destination pixel format cannot store: outside [0, 1] for
    /// an integer format, beyond ±65,504 for <see cref="RgbaHalf"/>. Defaults to <see cref="OutOfRangeSampleHandling.Reject"/>.
    /// </summary>
    /// <remarks>It applies after <see cref="ToneMapping"/>, to what the curve leaves out of range (negative components, alpha).</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="OutOfRangeSampleHandling"/>.</exception>
    public OutOfRangeSampleHandling OutOfRangeSamples
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The out-of-range sample handling is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the tone mapping that brings the colors of floating-point pixels into [0, 1] when they are converted to an integer
    /// pixel format, or <see langword="null"/> (the default) to keep the colors as they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The color components are linear light in units of the reference white (1 is the white), which is the meaning of the
    /// floating-point formats. The curve of <see cref="HdrToneMappingOptions.Operator"/> is applied to the largest of the
    /// three components and the three are scaled together, which keeps the hue. <see cref="HdrToneMappingOptions.TargetWhiteLuminance"/>
    /// gives the luminance of a component of 1 (the PQ values of the curve of Report ITU-R BT.2390 need it). The peak of
    /// the source is <see cref="HdrToneMappingOptions.SourcePeakLuminance"/> divided by that white when it is set, else the
    /// largest finite color component of the converted pixels (of every frame of an image), and at least 1. Light above the
    /// peak becomes the white, and so does an infinite component (the finite components of its pixel become 0).
    /// </para>
    /// <para>
    /// The conversion does not change the transfer function label of the image. Alpha is not tone mapped, and negative
    /// components stay negative: <see cref="OutOfRangeSamples"/> decides about what is still out of range. The option is
    /// ignored for the other conversions.
    /// </para>
    /// </remarks>
    public HdrToneMappingOptions? ToneMapping { get; init; }
}
