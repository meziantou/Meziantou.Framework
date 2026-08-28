using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Internal;

/// <summary>
/// Writes a PNG container: signature, IHDR, PLTE, IDAT and IEND, with the CRC32 each chunk needs.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrChunkType = [73, 72, 68, 82];
    private static readonly byte[] PlteChunkType = [80, 76, 84, 69];
    private static readonly byte[] TrnsChunkType = [116, 82, 78, 83];
    private static readonly byte[] IdatChunkType = [73, 68, 65, 84];
    private static readonly byte[] IendChunkType = [73, 69, 78, 68];
    private static readonly uint[] Crc32Table = InitializeCrc32Table();

    /// <summary>
    /// Writes a two-colour indexed PNG at one bit per pixel.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <param name="lightColor">The colour for a clear bit.</param>
    /// <param name="darkColor">The colour for a set bit.</param>
    /// <param name="renderRow">
    /// Fills one packed scanline. The span is cleared before each call and holds one bit per
    /// pixel, most significant bit leftmost; set a bit to make that pixel dark.
    /// </param>
    /// <remarks>
    /// QR codes and barcodes are two-colour images, so an indexed palette stores them at a
    /// thirty-second of the bytes truecolour-with-alpha needs. Rows are streamed into the
    /// compressor as they are produced rather than buffered, so peak memory does not scale with
    /// the image size.
    /// </remarks>
    public static void WritePalette(Stream stream, int width, int height, Color lightColor, Color darkColor, PngRowRenderer renderRow)
    {
        var compressedImageData = CompressRows(width, height, renderRow);

        stream.Write(Signature);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 1;  // Bit depth
        ihdrData[9] = 3;  // Colour type: indexed
        ihdrData[10] = 0; // Compression method
        ihdrData[11] = 0; // Filter method
        ihdrData[12] = 0; // Interlace method
        WriteChunk(stream, IhdrChunkType, ihdrData);

        Span<byte> palette = [lightColor.Red, lightColor.Green, lightColor.Blue, darkColor.Red, darkColor.Green, darkColor.Blue];
        WriteChunk(stream, PlteChunkType, palette);

        if (lightColor.Alpha is not byte.MaxValue || darkColor.Alpha is not byte.MaxValue)
        {
            Span<byte> transparency = [lightColor.Alpha, darkColor.Alpha];
            WriteChunk(stream, TrnsChunkType, transparency);
        }

        WriteChunk(stream, IdatChunkType, compressedImageData);
        WriteChunk(stream, IendChunkType, ReadOnlySpan<byte>.Empty);
    }

    private static byte[] CompressRows(int width, int height, PngRowRenderer renderRow)
    {
        var bytesPerRow = (width + 7) / 8;
        var current = new byte[bytesPerRow];
        var previous = new byte[bytesPerRow];
        var filtered = new byte[bytesPerRow + 1];

        using var output = new MemoryStream();
        using (var compressionStream = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var row = 0; row < height; row++)
            {
                Array.Clear(current);
                renderRow(current, row);

                // The Up filter turns a row identical to the one above it into zeroes, which is
                // the common case here because every module spans ModuleSize rows.
                if (row is 0)
                {
                    filtered[0] = 0; // None
                    current.CopyTo(filtered.AsSpan(1));
                }
                else
                {
                    filtered[0] = 2; // Up
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        filtered[i + 1] = (byte)(current[i] - previous[i]);
                    }
                }

                compressionStream.Write(filtered);
                (previous, current) = (current, previous);
            }
        }

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> uintBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, (uint)data.Length);
        stream.Write(uintBuffer);
        stream.Write(chunkType);
        stream.Write(data);

        var crc = ComputeCrc32(chunkType, data);
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, crc);
        stream.Write(uintBuffer);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        crc = UpdateCrc32(crc, chunkType);
        crc = UpdateCrc32(crc, data);

        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = Crc32Table[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] InitializeCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? (crc >> 1) : (0xEDB88320u ^ (crc >> 1));
            }

            table[index] = crc;
        }

        return table;
    }
}
