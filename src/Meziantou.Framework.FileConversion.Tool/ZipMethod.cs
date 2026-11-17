namespace Meziantou.Framework.FileConversionTool;

/// <summary>The compression methods of the entries of a zip output.</summary>
internal enum ZipMethod
{
    Deflate,
    Stored,
    BZip2,
    Lzma,
    Ppmd,
    Xz,

    /// <summary>Zstandard, which needs .NET 11.</summary>
    Zstandard,
}
