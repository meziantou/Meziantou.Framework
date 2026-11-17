using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Creates the zlib streams of the PNG encoder for a <see cref="CompressionLevel"/>.</summary>
internal static class PngZlib
{
    /// <summary>The zlib level used for <see cref="CompressionLevel.Fastest"/>.</summary>
    /// <remarks>
    /// zlib-ng, the zlib of .NET, maps level 1 to a "quick" strategy that writes fixed Huffman codes: filtered scanlines
    /// compress 25 to 35% worse than with level 2, which builds codes from the data for little more time.
    /// </remarks>
    public const int FastestLevel = 2;

    /// <summary>Creates a zlib compression stream over <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream receiving the compressed data.</param>
    /// <param name="level">The compression level.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the zlib stream is disposed.</param>
    /// <returns>The zlib stream.</returns>
    public static PngZlibStream Create(Stream stream, CompressionLevel level, bool leaveOpen)
    {
        var zlibLevel = level switch
        {
            CompressionLevel.NoCompression => 0,
            CompressionLevel.Fastest => FastestLevel,
            CompressionLevel.SmallestSize => 9,
            _ => 6, // Optimal: the default level of zlib
        };

        return new PngZlibStream(stream, zlibLevel, leaveOpen);
    }
}
