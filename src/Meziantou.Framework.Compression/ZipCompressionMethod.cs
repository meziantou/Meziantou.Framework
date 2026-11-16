namespace Meziantou.Framework.Compression;

/// <summary>
/// The compression method of a zip entry (APPNOTE 4.4.5). A value that is not a named member is a method that the
/// library does not know. The content of an entry whose method is not supported cannot be read.
/// </summary>
public enum ZipCompressionMethod
{
    /// <summary>The content is not compressed.</summary>
    Stored = 0,

    /// <summary>Shrink (LZW), of PKZIP 1.</summary>
    Shrink = 1,

    /// <summary>Reduce with a compression factor of 1, of PKZIP 0.9.</summary>
    Reduce1 = 2,

    /// <summary>Reduce with a compression factor of 2, of PKZIP 0.9.</summary>
    Reduce2 = 3,

    /// <summary>Reduce with a compression factor of 3, of PKZIP 0.9.</summary>
    Reduce3 = 4,

    /// <summary>Reduce with a compression factor of 4, of PKZIP 0.9.</summary>
    Reduce4 = 5,

    /// <summary>Implode, of PKZIP 1.</summary>
    Implode = 6,

    /// <summary>Deflate.</summary>
    Deflate = 8,

    /// <summary>Deflate64 (enhanced deflate).</summary>
    Deflate64 = 9,

    /// <summary>The PKWARE Data Compression Library Implode (not supported).</summary>
    PkwareDclImplode = 10,

    /// <summary>BZip2.</summary>
    BZip2 = 12,

    /// <summary>LZMA.</summary>
    Lzma = 14,

    /// <summary>IBM TERSE (not supported).</summary>
    Terse = 18,

    /// <summary>IBM LZ77 z Architecture (not supported).</summary>
    Lz77 = 19,

    /// <summary>Zstandard.</summary>
    Zstandard = 93,

    /// <summary>MP3 (not supported).</summary>
    Mp3 = 94,

    /// <summary>XZ.</summary>
    Xz = 95,

    /// <summary>JPEG variant (not supported).</summary>
    Jpeg = 96,

    /// <summary>WavPack (not supported).</summary>
    WavPack = 97,

    /// <summary>PPMd version I, revision 1.</summary>
    Ppmd = 98,
}
