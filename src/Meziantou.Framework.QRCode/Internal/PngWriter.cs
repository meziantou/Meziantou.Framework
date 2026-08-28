using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Internal;

/// <summary>
/// Writes a PNG container: signature, IHDR, IDAT and IEND, with the CRC32 each chunk needs.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrChunkType = [73, 72, 68, 82];
    private static readonly byte[] IdatChunkType = [73, 68, 65, 84];
    private static readonly byte[] IendChunkType = [73, 69, 78, 68];
    private static readonly uint[] Crc32Table = InitializeCrc32Table();

    /// <summary>
    /// Writes a truecolour-with-alpha (8 bits per channel) PNG.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <param name="imageData">Scanlines, each prefixed with its filter type byte.</param>
    public static void WriteRgba(Stream stream, int width, int height, ReadOnlySpan<byte> imageData)
    {
        var compressedImageData = Compress(imageData);

        stream.Write(Signature);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 8;  // Bit depth
        ihdrData[9] = 6;  // Colour type: truecolour with alpha
        ihdrData[10] = 0; // Compression method
        ihdrData[11] = 0; // Filter method
        ihdrData[12] = 0; // Interlace method

        WriteChunk(stream, IhdrChunkType, ihdrData);
        WriteChunk(stream, IdatChunkType, compressedImageData);
        WriteChunk(stream, IendChunkType, ReadOnlySpan<byte>.Empty);
    }

    private static byte[] Compress(ReadOnlySpan<byte> imageData)
    {
        using var output = new MemoryStream();
        using (var compressionStream = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressionStream.Write(imageData);
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
