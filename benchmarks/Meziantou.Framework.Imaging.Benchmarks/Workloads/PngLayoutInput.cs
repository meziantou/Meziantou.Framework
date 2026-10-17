using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// Writes the PNG inputs whose layouts <c>PngEncoder</c> never writes (gray with alpha, 16-bit RGB without alpha): one
/// non-interlaced image, every scanline with the Up filter, compressed with the fastest zlib level.
/// </summary>
internal static class PngLayoutInput
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    /// <summary>Encodes big-endian samples (the bytes of the PNG scanlines without their filter byte).</summary>
    /// <param name="samples">The samples, row after row, as PNG stores them.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="colorType">The PNG color type (0, 2, 4 or 6).</param>
    /// <param name="bitDepth">8 or 16.</param>
    public static byte[] Encode(ReadOnlySpan<byte> samples, int width, int height, byte colorType, byte bitDepth)
    {
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            _ => 4,
        };

        var rowLength = width * channels * bitDepth / 8;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var filtered = new byte[rowLength + 1];
            filtered[0] = 2;
            for (var y = 0; y < height; y++)
            {
                var row = samples.Slice(y * rowLength, rowLength);
                for (var i = 0; i < rowLength; i++)
                {
                    filtered[i + 1] = (byte)(row[i] - (y == 0 ? 0 : samples[((y - 1) * rowLength) + i]));
                }

                zlib.Write(filtered);
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = bitDepth;
        header[9] = colorType;
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);
        var crc = Update(Update(uint.MaxValue, type), data) ^ uint.MaxValue;
        BinaryPrimitives.WriteUInt32BigEndian(length, crc);
        output.Write(length);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
