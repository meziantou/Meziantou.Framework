namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>An immutable, uncompressed ICC color profile.</summary>
/// <remarks>
/// <para>
/// Profiles are preserved and labeled, never applied: the library does not perform ICC color conversion in this version.
/// A profile is only kept when its <see cref="ColorSpace"/> is compatible with the pixels it describes (a grayscale profile
/// for grayscale pixels, an RGB profile for color pixels); incompatible combinations are rejected unless the caller
/// explicitly discards the profile.
/// </para>
/// </remarks>
public sealed class IccProfile
{
    private const int HeaderSize = 128;

    /// <summary>Initializes a new instance of the <see cref="IccProfile"/> class.</summary>
    /// <param name="data">The uncompressed ICC profile bytes. The full structure is validated when serialized.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public IccProfile(MetadataBlob data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        ColorSpace = ParseColorSpace(data.Span);
    }

    /// <summary>Gets the raw profile bytes.</summary>
    public MetadataBlob Data { get; }

    /// <summary>Gets the data color space declared in the profile header.</summary>
    public IccProfileColorSpace ColorSpace { get; }

    private static IccProfileColorSpace ParseColorSpace(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            return IccProfileColorSpace.Unknown;

        var signature = data.Slice(16, 4);
        if (signature.SequenceEqual("GRAY"u8))
            return IccProfileColorSpace.Gray;

        if (signature.SequenceEqual("RGB "u8))
            return IccProfileColorSpace.Rgb;

        if (signature.SequenceEqual("CMYK"u8))
            return IccProfileColorSpace.Cmyk;

        return IccProfileColorSpace.Other;
    }
}
