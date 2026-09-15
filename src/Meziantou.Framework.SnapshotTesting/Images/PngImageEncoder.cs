using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace Meziantou.Framework.SnapshotTesting;

internal static class PngImageEncoder
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static byte[] Encode(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // A 16-bit source (e.g. a PNG entry of an ICO file) is written back with 16 bits per sample, so the
        // snapshot keeps the precision that exact comparisons use.
        var highPrecisionSamples = image.HighPrecisionSamples.Span;
        var bytesPerSample = highPrecisionSamples.IsEmpty ? 1 : 2;
        var rowStride = checked(image.Width * 4 * bytesPerSample + 1);
        var imageData = new byte[checked(rowStride * image.Height)];
        var pixels = image.Pixels.Span;

        for (var y = 0; y < image.Height; y++)
        {
            var rowOffset = y * rowStride;
            imageData[rowOffset] = 0; // No filter
            for (var x = 0; x < image.Width; x++)
            {
                var pixelIndex = y * image.Width + x;
                var destinationOffset = rowOffset + 1 + x * 4 * bytesPerSample;
                if (highPrecisionSamples.IsEmpty)
                {
                    var pixel = pixels[pixelIndex];
                    imageData[destinationOffset] = pixel.R;
                    imageData[destinationOffset + 1] = pixel.G;
                    imageData[destinationOffset + 2] = pixel.B;
                    imageData[destinationOffset + 3] = pixel.A;
                }
                else
                {
                    var samples = highPrecisionSamples.Slice(pixelIndex * 4, 4); // A, R, G, B
                    var destination = imageData.AsSpan(destinationOffset, 8);
                    BinaryPrimitives.WriteUInt16BigEndian(destination, samples[1]);
                    BinaryPrimitives.WriteUInt16BigEndian(destination[2..], samples[2]);
                    BinaryPrimitives.WriteUInt16BigEndian(destination[4..], samples[3]);
                    BinaryPrimitives.WriteUInt16BigEndian(destination[6..], samples[0]);
                }
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
        ihdrData[8] = (byte)(8 * bytesPerSample); // Bit depth
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
        // PNG uses CRC-32/ISO-HDLC, which is what System.IO.Hashing.Crc32 computes.
        var crc = new Crc32();
        crc.Append(type);
        crc.Append(data);
        return crc.GetCurrentHashAsUInt32();
    }
}
