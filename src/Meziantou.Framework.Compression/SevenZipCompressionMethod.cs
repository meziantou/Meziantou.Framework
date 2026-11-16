namespace Meziantou.Framework.Compression;

/// <summary>The compression method of the content of a 7z archive.</summary>
public enum SevenZipCompressionMethod
{
    /// <summary>LZMA2, the default method of 7-Zip.</summary>
    Lzma2,

    /// <summary>LZMA.</summary>
    Lzma,

    /// <summary>BZip2.</summary>
    BZip2,

    /// <summary>Deflate.</summary>
    Deflate,

    /// <summary>PPMd (variant H), efficient on text.</summary>
    Ppmd,

    /// <summary>No compression.</summary>
    Copy,
}
