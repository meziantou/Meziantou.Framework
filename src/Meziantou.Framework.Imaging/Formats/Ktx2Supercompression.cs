namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The supercompression scheme of the mip levels of a KTX 2.0 file written by <see cref="Ktx2Encoder"/>.</summary>
public enum Ktx2Supercompression
{
    /// <summary>The texels are stored as they are (scheme 0). This is the default; the file can be uploaded to a GPU without decompression.</summary>
    None = 0,

    /// <summary>
    /// Each mip level is compressed with ZLIB (scheme 3, RFC 1950), which every KTX 2.0 reader of the specification
    /// decompresses. Zstandard (scheme 2) is read but not written.
    /// </summary>
    Zlib = 3,
}
