using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// Encodes pixels as a PNG image (https://w3c.github.io/png/): the serialization of the bitmaps as <c>image/png</c>
/// (<c>toDataURL()</c>, <c>toBlob()</c>, <c>convertToBlob()</c>), which works without a rendering backend. The image is a
/// truecolor image with alpha, 8 or 16 bits per channel (or a truecolor image without alpha for the opaque bitmaps), whose rows are
/// filtered with the Paeth or the Sub filter and compressed with zlib.
/// </summary>
internal static class PngEncoder
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Encodes pixels (4 bytes per pixel in the order R, G, B, A, not premultiplied, row by row).</summary>
    /// <param name="width">The width in pixels (at least 1).</param>
    /// <param name="height">The height in pixels (at least 1).</param>
    /// <param name="rgba">The pixels.</param>
    /// <param name="hasAlpha">Whether the alpha channel is kept (an opaque bitmap is encoded without it).</param>
    /// <param name="bitDepth">8, or 16 for samples stored in big-endian order.</param>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, bool hasAlpha = true, int bitDepth = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (bitDepth is not (8 or 16))
            throw new ArgumentOutOfRangeException(nameof(bitDepth));

        var bytesPerSample = bitDepth / 8;
        var channels = hasAlpha ? 4 : 3;
        var bytesPerPixel = channels * bytesPerSample;
        var stride = width * bytesPerPixel;

        using var output = new MemoryStream();
        output.Write(Signature);

        // IHDR: width, height, bit depth 8, color type 6 (truecolor with alpha) or 2 (truecolor), deflate, adaptive filtering, no interlace
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = (byte)bitDepth;
        header[9] = hasAlpha ? (byte)6 : (byte)2;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR"u8, header);

        // IDAT: each row is preceded by its filter type (https://w3c.github.io/png/#9Filters)
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                var previous = new byte[stride];
                var current = new byte[stride];
                var filtered = new byte[stride + 1];
                for (var y = 0; y < height; y++)
                {
                    var row = rgba.Slice(y * width * 4 * bytesPerSample, width * 4 * bytesPerSample);
                    if (hasAlpha)
                    {
                        row.CopyTo(current);
                    }
                    else
                    {
                        for (var x = 0; x < width; x++)
                        {
                            row.Slice(x * 4 * bytesPerSample, 3 * bytesPerSample).CopyTo(current.AsSpan(x * 3 * bytesPerSample));
                        }
                    }

                    // The Paeth filter (4), or the Sub filter (1) for the first row
                    filtered[0] = y == 0 ? (byte)1 : (byte)4;
                    for (var i = 0; i < stride; i++)
                    {
                        var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        if (y == 0)
                        {
                            filtered[i + 1] = (byte)(current[i] - left);
                        }
                        else
                        {
                            var up = previous[i];
                            var upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                            filtered[i + 1] = (byte)(current[i] - Paeth(left, up, upperLeft));
                        }
                    }

                    zlib.Write(filtered);
                    (previous, current) = (current, previous);
                }
            }

            WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        }

        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    /// <summary>The Paeth predictor (https://w3c.github.io/png/#9Filter-type-4-Paeth).</summary>
    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;

        return pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        output.Write(buffer);
        output.Write(type);
        output.Write(data);
        var crc = new Crc32();
        crc.Append(type);
        crc.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc.GetCurrentHashAsUInt32());
        output.Write(buffer);
    }
}
