namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>An immutable EXIF profile: the TIFF-structured EXIF data, starting with the TIFF byte-order header (<c>II*\0</c> or <c>MM\0*</c>).</summary>
/// <remarks>
/// <para>
/// The data never includes container-specific prefixes such as the JPEG <c>Exif\0\0</c> APP1 identifier. The typed
/// <see cref="ImageMetadata.Orientation"/> is authoritative: when the profile is serialized, its orientation tag is
/// rewritten from the typed value, dimension tags are reconciled with the image, and thumbnails made stale by geometry
/// changes are removed.
/// </para>
/// </remarks>
public sealed class ExifProfile
{
    /// <summary>Initializes a new instance of the <see cref="ExifProfile"/> class.</summary>
    /// <param name="data">The TIFF-structured EXIF data. It is validated when serialized.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public ExifProfile(MetadataBlob data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    /// <summary>Gets the raw EXIF data.</summary>
    public MetadataBlob Data { get; }
}
