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
}
