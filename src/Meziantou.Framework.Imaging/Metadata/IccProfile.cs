using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>An immutable, uncompressed ICC color profile.</summary>
/// <remarks>
/// <para>
/// A profile labels the pixels of an image: it is preserved by decoders, encoders and conversions, and never applied
/// implicitly. Colors are converted only on request, by <see cref="ImageProcessingExtensions.ConvertColorProfile"/> for an
/// image or by <see cref="IccColorTransform"/> for sample buffers. A profile is only kept on an image when its
/// <see cref="ColorSpace"/> is compatible with the pixels it describes (a grayscale profile for grayscale pixels, an RGB
/// profile for color pixels); incompatible combinations are rejected unless the caller explicitly discards the profile.
/// </para>
/// <para>
/// The properties expose the header fields as declared, without validating the profile: a profile shorter than an ICC
/// header (128 bytes) reports <see cref="IccProfileColorSpace.Unknown"/>, <see cref="IccProfileClass.Unknown"/>, version
/// 0.0.0 and no rendering intent.
/// </para>
/// </remarks>
public sealed class IccProfile
{
    private static readonly Version UnknownVersion = new(0, 0, 0);

    /// <summary>Initializes a new instance of the <see cref="IccProfile"/> class.</summary>
    /// <param name="data">The uncompressed ICC profile bytes. The full structure is validated when serialized.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public IccProfile(MetadataBlob data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        if (IccHeader.TryRead(data.Span, out var header))
        {
            ColorSpace = header.GetColorSpace();
            ProfileClass = header.GetProfileClass();
            Version = header.Version;
            RenderingIntent = header.GetRenderingIntent();
        }
        else
        {
            Version = UnknownVersion;
        }
    }

    /// <summary>
    /// Gets the sRGB profile (IEC 61966-2-1): the color space of untagged RGB pixels. It is a version 4 matrix-based
    /// display profile whose primaries are adapted to the D50 illuminant of the profile connection space.
    /// </summary>
    public static IccProfile Srgb => BuiltInIccProfiles.Srgb;

    /// <summary>
    /// Gets the grayscale counterpart of <see cref="Srgb"/>: the sRGB transfer function and white point. It is the color
    /// space of untagged grayscale pixels.
    /// </summary>
    public static IccProfile SrgbGray => BuiltInIccProfiles.SrgbGray;

    /// <summary>
    /// Gets a CMYK profile for web offset printing on coated paper, compatible with the CGATS TR 001 characterization
    /// data: the color space assumed by default for CMYK samples without profile (see
    /// <see cref="CmykConversionOptions.AssumedSourceProfile"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The profile is <c>CGATS001Compat-v2-micro.icc</c> of the Compact ICC Profiles collection by Clinton Ingram
    /// (https://github.com/saucecontrol/Compact-ICC-Profiles), released to the public domain (CC0-1.0) and stored
    /// unmodified: a version 2 input profile of 8,464 bytes.
    /// </para>
    /// <para>
    /// It only describes the conversion from CMYK (one perceptual table of 6 points per ink): it can be the source of a
    /// conversion with any rendering intent, never its destination. Its small table reproduces the overall appearance of
    /// the printing condition, not the exact colors of a full-size press profile.
    /// </para>
    /// </remarks>
    public static IccProfile Cgats001CompatibleCmyk => BuiltInIccProfiles.Cgats001CompatibleCmyk;

    /// <summary>Gets the raw profile bytes.</summary>
    public MetadataBlob Data { get; }

    /// <summary>Gets the data color space declared in the profile header.</summary>
    public IccProfileColorSpace ColorSpace { get; }

    /// <summary>Gets the profile/device class declared in the profile header.</summary>
    public IccProfileClass ProfileClass { get; }

    /// <summary>Gets the profile version declared in the profile header (major, minor and bug fix numbers), such as 2.4.0 or 4.4.0.</summary>
    public Version Version { get; }

    /// <summary>
    /// Gets the rendering intent declared in the profile header, or <see langword="null"/> when the header is missing or
    /// declares an undefined value. This is the intent the profile creator suggests; it does not restrict the intents the
    /// profile supports.
    /// </summary>
    public IccRenderingIntent? RenderingIntent { get; }
}
