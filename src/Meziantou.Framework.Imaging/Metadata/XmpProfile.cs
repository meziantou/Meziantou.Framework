namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>An immutable XMP packet, stored as UTF-8 bytes without container-specific prefixes.</summary>
/// <remarks>
/// The packet is preserved as-is; the library does not parse or rewrite XMP properties in this version, so dimension
/// or orientation properties inside the packet are not reconciled after geometry changes.
/// </remarks>
public sealed class XmpProfile
{
    /// <summary>Initializes a new instance of the <see cref="XmpProfile"/> class.</summary>
    /// <param name="data">The UTF-8 XMP packet. It is validated when serialized.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public XmpProfile(MetadataBlob data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    /// <summary>Gets the raw XMP packet.</summary>
    public MetadataBlob Data { get; }
}
