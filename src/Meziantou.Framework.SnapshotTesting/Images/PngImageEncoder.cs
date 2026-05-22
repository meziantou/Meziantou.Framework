using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.SnapshotTesting;

internal static class PngImageEncoder
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] PngCrc32Table = InitializePngCrc32Table();

    internal static byte[] Encode(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var width = image.Width;
        var height = image.Height;
        var rawData = new byte[checked(height * (checked(width * 4) + 1))];
        var pixels = image.Pixels.Span;
        for (var y = 0; y < height; y++)
        {
            var rawRowOffset = y * (width * 4 + 1);
            rawData[rawRowOffset] = 0;
            var pixelRowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[pixelRowOffset + x];
                var destinationOffset = rawRowOffset + 1 + x * 4;
                rawData[destinationOffset] = pixel.R;
                rawData[destinationOffset + 1] = pixel.G;
                rawData[destinationOffset + 2] = pixel.B;
                rawData[destinationOffset + 3] = pixel.A;
            }
        }

        byte[] compressedData;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlibStream = new ZLibStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlibStream.Write(rawData);
            }

            compressedData = compressedStream.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(PngSignature);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[0..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..8], (uint)height);
        ihdrData[8] = 8;
        ihdrData[9] = 6;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WritePngChunk(output, "IHDR"u8, ihdrData);
        WritePngChunk(output, "IDAT"u8, compressedData);
        WritePngChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> uintBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, (uint)data.Length);
        stream.Write(uintBuffer);
        stream.Write(type);
        stream.Write(data);

        var crc = ComputePngCrc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, crc);
        stream.Write(uintBuffer);
    }

    private static uint ComputePngCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        crc = UpdatePngCrc32(crc, type);
        crc = UpdatePngCrc32(crc, data);
        return ~crc;
    }

    private static uint UpdatePngCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = PngCrc32Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] InitializePngCrc32Table()
    {
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            var c = (uint)i;
            for (var bit = 0; bit < 8; bit++)
            {
                c = (c & 1) == 0 ? c >> 1 : 0xEDB88320u ^ (c >> 1);
            }

            table[i] = c;
        }

        return table;
    }
}
