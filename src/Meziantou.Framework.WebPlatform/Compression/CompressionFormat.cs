namespace Meziantou.Framework.WebPlatform.Compression;

/// <summary>The compression formats of the Compression Standard (https://compression.spec.whatwg.org/#supported-formats).</summary>
internal enum CompressionFormat
{
    /// <summary><c>brotli</c>: the Brotli compressed data format (RFC 7932).</summary>
    Brotli,

    /// <summary><c>deflate</c>: the zlib compressed data format (RFC 1950).</summary>
    Deflate,

    /// <summary><c>deflate-raw</c>: the DEFLATE algorithm (RFC 1951), without header and trailer.</summary>
    DeflateRaw,

    /// <summary><c>gzip</c>: the gzip file format (RFC 1952).</summary>
    Gzip,
}

internal static class CompressionFormats
{
    /// <summary>Returns the format of a <c>CompressionFormat</c> enumeration value, or null when the value is not one of them.</summary>
    public static CompressionFormat? Parse(string value) => value switch
    {
        "brotli" => CompressionFormat.Brotli,
        "deflate" => CompressionFormat.Deflate,
        "deflate-raw" => CompressionFormat.DeflateRaw,
        "gzip" => CompressionFormat.Gzip,
        _ => null,
    };
}
