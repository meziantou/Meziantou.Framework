namespace Meziantou.Framework.Imaging.Formats;

/// <summary>How <see cref="TiffEncoder"/> compresses the strips of a page.</summary>
/// <remarks>
/// The encoder writes a deliberately conservative subset of TIFF 6.0. LZW, PackBits, CCITT fax and JPEG compression are
/// neither written nor decoded by this version.
/// </remarks>
public enum TiffCompression
{
    /// <summary>No compression: the samples are stored as they are (<c>Compression</c> 1).</summary>
    None = 0,

    /// <summary>Deflate, as Adobe's <c>Compression</c> 8 (a zlib datastream per strip). The default.</summary>
    Deflate = 1,
}
