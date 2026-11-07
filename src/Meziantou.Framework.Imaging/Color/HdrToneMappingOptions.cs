namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of the conversion of high dynamic range samples (the PQ and HLG transfer characteristics of a CICP
/// color description) to the standard dynamic range that an ICC profile describes
/// (<see cref="IccColorTransformOptions.ToneMapping"/>).
/// </summary>
/// <remarks>
/// <para>
/// PQ samples (SMPTE ST 2084, Rec. ITU-R BT.2100) are decoded to the light of a display, in candelas per square meter.
/// HLG samples (Rec. ITU-R BT.2100) are decoded to the light of a scene, which the opto-optical transfer function of
/// the recommendation turns into the light of a display whose nominal peak is the peak of the source, with the system
/// gamma of that peak (1.2 at 1,000 cd/m2).
/// </para>
/// <para>
/// The light is divided by <see cref="TargetWhiteLuminance"/>, which becomes the white of the converted image, and
/// <see cref="Operator"/> compresses what is above it. The result is linear light in the color primaries of the
/// source, which is then converted to the destination profile like any other color: a color outside of the gamut of
/// the destination is clipped per channel.
/// </para>
/// </remarks>
public sealed class HdrToneMappingOptions
{
    /// <summary>Gets the default options: the curve of Report ITU-R BT.2390, a peak derived from the image, and a white of 203 cd/m2.</summary>
    public static HdrToneMappingOptions Default { get; } = new();

    /// <summary>Gets the curve that compresses the highlights. Defaults to <see cref="HdrToneMappingOperator.Bt2390"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="HdrToneMappingOperator"/>.</exception>
    public HdrToneMappingOperator Operator
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The tone mapping operator is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the luminance of the brightest light of the source, in candelas per square meter, which the curve maps to
    /// the white; or <see langword="null"/> (the default) to derive it from the image.
    /// </summary>
    /// <remarks>
    /// When it is not set, the peak of PQ samples is the maximum content light level of the image
    /// (<see cref="Metadata.ImageMetadata.ContentLightLevel"/>), else the maximum luminance of its mastering display
    /// (<see cref="Metadata.ImageMetadata.MasteringDisplay"/>), else 10,000 cd/m2, the largest value that PQ encodes. The
    /// peak of HLG samples, which is the peak of the display they are shown on, is the maximum luminance of the
    /// mastering display, else 1,000 cd/m2. Light above the peak is clipped to the white. A peak below
    /// <see cref="TargetWhiteLuminance"/> is raised to it: nothing is compressed, and nothing is brightened.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number, or is above 10,000.</exception>
    public double? SourcePeakLuminance
    {
        get;
        init
        {
            if (value is { } peak && !(peak > 0 && peak <= MaxLuminance))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The peak luminance must be greater than 0 and at most 10,000 cd/m2.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the luminance that becomes the white of the converted image, in candelas per square meter. Defaults to 203,
    /// the reference white of high dynamic range programs (Report ITU-R BT.2408).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number, or is above 10,000.</exception>
    public double TargetWhiteLuminance
    {
        get;
        init
        {
            if (!(value > 0 && value <= MaxLuminance))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The white luminance must be greater than 0 and at most 10,000 cd/m2.");

            field = value;
        }
    } = 203;

    // The largest luminance that PQ encodes
    private const double MaxLuminance = 10_000;
}
