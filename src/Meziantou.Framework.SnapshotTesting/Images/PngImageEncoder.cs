using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.SnapshotTesting;

internal static class PngImageEncoder
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static byte[] Encode(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var rowStride = checked(image.Width * 4 + 1);
        var imageData = new byte[checked(rowStride * image.Height)];
        var pixels = image.Pixels.Span;

        for (var y = 0; y < image.Height; y++)
        {
            var rowOffset = y * rowStride;
            imageData[rowOffset] = 0; // No filter
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = pixels[y * image.Width + x];
                var destinationOffset = rowOffset + 1 + x * 4;
                imageData[destinationOffset] = pixel.R;
                imageData[destinationOffset + 1] = pixel.G;
                imageData[destinationOffset + 2] = pixel.B;
                imageData[destinationOffset + 3] = pixel.A;
            }
        }

        byte[] compressedData;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(imageData);
            }

            compressedData = compressedStream.ToArray();
        }

        using var stream = new MemoryStream();
        stream.Write(PngSignature);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], checked((uint)image.Height));
        ihdrData[8] = 8;  // Bit depth
        ihdrData[9] = 6;  // RGBA
        ihdrData[10] = 0; // Compression method
        ihdrData[11] = 0; // Filter method
        ihdrData[12] = 0; // Interlace method

        WriteChunk(stream, "IHDR"u8, ihdrData);
        WriteChunk(stream, "IDAT"u8, compressedData);
        WriteChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> uintBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, checked((uint)data.Length));
        stream.Write(uintBuffer);
        stream.Write(type);
        stream.Write(data);

        var crc = ComputeCrc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, crc);
        stream.Write(uintBuffer);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        crc = UpdateCrc32(crc, type);
        crc = UpdateCrc32(crc, data);
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320u ^ (crc >> 1);
            }
        }

        return crc;
    }
}
