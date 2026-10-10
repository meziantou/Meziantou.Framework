using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>An immutable, uncompressed ICC color profile.</summary>
/// <remarks>
/// <para>
/// Profiles are preserved and labeled, never applied: the library does not perform ICC color conversion in this version.
/// A profile is only kept when its <see cref="ColorSpace"/> is compatible with the pixels it describes (a grayscale profile
/// for grayscale pixels, an RGB profile for color pixels); incompatible combinations are rejected unless the caller
/// explicitly discards the profile.
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
