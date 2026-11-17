namespace Meziantou.Framework.Compression;

/// <summary>The format of a compressed stream, such as the compression of a tar archive (.tar.gz, .tar.xz, ...).</summary>
public enum CompressionFormat
{
    /// <summary>The data are not compressed.</summary>
    None,

    /// <summary>gzip (.gz).</summary>
    GZip,

    /// <summary>bzip2 (.bz2).</summary>
    BZip2,

    /// <summary>XZ (.xz).</summary>
    Xz,

    /// <summary>The LZMA alone format of the LZMA SDK (.lzma).</summary>
    Lzma,

    /// <summary>lzip (.lz).</summary>
    LZip,

    /// <summary>Zstandard (.zst).</summary>
    Zstandard,

    /// <summary>The LZW format of the Unix compress command (.Z).</summary>
    UnixCompress,
}
