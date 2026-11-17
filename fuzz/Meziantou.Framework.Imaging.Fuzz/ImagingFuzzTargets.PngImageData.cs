#if NET11_0_OR_GREATER
using System.Buffers;
#endif
using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The image data of a PNG file (inflation of the zlib datastream, then reconstruction of the filtered scanlines): a
/// decoded image must have the pixels of a scalar reconstruction of the scanlines that a reference inflater gives, and on
/// .NET 11 the decoder must accept exactly the datastreams that <c>ZLibDecoder</c> decodes completely to the
/// scanlines of the image (no byte before, after or between them) with valid filter types.
/// </summary>
/// <remarks>
/// The input is a byte of format (8- or 16-bit RGB or RGBA, so that the pixels are the reconstructed bytes), a byte of
/// width (1 to 128, so that rows span several pieces of the vectorized Paeth reconstruction), a byte of height (1 to 8), a
/// byte of mode and the data. The mode says whether the data is the zlib datastream itself, or scanlines that the target
/// compresses (each filter byte taken modulo 5), and the size of the <c>IDAT</c> chunks the datastream is split into.
/// </remarks>
public static partial class ImagingFuzzTargets
{
    private static readonly (byte ColorType, byte BitDepth, int BytesPerPixel)[] PngImageDataFormats = [(6, 8, 4), (2, 8, 3), (6, 16, 8), (2, 16, 6)];

    // zlib headers, the end of a stored block, the filter types; a property: All, in another file of the class, reads it
    private static string[] PngImageDataTokens => ["x\u009C", "x\u0001", "xÚ", "\u0001\0\0ÿÿ", "\0", "\u0001", "\u0002", "\u0003", "\u0004", "ÿÿÿÿ"];

    private static void PngImageDataTarget(ReadOnlySpan<byte> data)
    {
        var format = PngImageDataFormats[FuzzInput.ReadByte(ref data) % PngImageDataFormats.Length];
        var width = 1 + (FuzzInput.ReadByte(ref data) % 128);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 8);
        var mode = FuzzInput.ReadByte(ref data);
        var stride = (width * format.BytesPerPixel) + 1;
        var length = stride * height;

        byte[] zlib;
        if ((mode & 1) != 0)
        {
            var scanlines = new byte[length];
            for (var i = 0; i < length && !data.IsEmpty; i++)
            {
                scanlines[i] = data[i % data.Length];
            }

            for (var row = 0; row < height; row++)
            {
                scanlines[row * stride] %= 5;
            }

            using var compressed = new MemoryStream();
            using (var stream = new ZLibStream(compressed, (mode & 2) != 0 ? CompressionLevel.NoCompression : CompressionLevel.Fastest, leaveOpen: true))
            {
                stream.Write(scanlines);
            }

            zlib = compressed.ToArray();
        }
        else
        {
            zlib = data.ToArray();
        }

        var chunkSize = (mode >> 2) is 0 ? int.MaxValue : mode >> 2;
        var file = CreatePng(width, height, format.ColorType, format.BitDepth, zlib, chunkSize);
        var expected = GetReferenceScanlines(zlib, length);
        Image? image = null;
        try
        {
            var loaded = Catch(() => image = format switch
            {
                (6, 8, _) => Image.Load<Rgba32>(file),
                (2, 8, _) => Image.Load<Rgb24>(file),
                (6, 16, _) => Image.Load<Rgba64>(file),
                _ => Image.Load<Rgb48>(file),
            });

            // Valid filter types are part of the validity of the data
            if (expected is not null)
            {
                for (var row = 0; row < height; row++)
                {
                    if (expected[row * stride] > 4)
                    {
                        expected = null;
                        break;
                    }
                }
            }

            if (image is null)
            {
                FuzzAssert.True(loaded.Exception is InvalidImageContentException, "The image data fails with an undocumented exception: " + loaded.Exception);

                // The datastreams compressed here are valid, with valid filter types
                FuzzAssert.True((mode & 1) == 0, "The decoder rejects the image data compressed by ZLibStream: " + loaded.Exception?.Message);
#if NET11_0_OR_GREATER
                FuzzAssert.True(expected is null, "The decoder rejects image data that the reference inflater decodes: " + loaded.Exception?.Message);
#endif
                return;
            }

#if NET11_0_OR_GREATER
            FuzzAssert.True(expected is not null, "The decoder accepts image data that the reference inflater rejects");
#else
            // ZLibStream does not report where the datastream ends: the decoded bytes are compared, and the datastream must
            // end with their Adler-32 checksum
            expected ??= GetStreamScanlines(zlib, length);
            FuzzAssert.True(expected is not null, "The decoder accepts image data that the reference inflater rejects");
            FuzzAssert.True(BinaryPrimitives.ReadUInt32BigEndian(zlib.AsSpan(zlib.Length - 4)) == ReferenceAdler32(expected), "The decoder accepts a datastream that does not end with the Adler-32 checksum of its data");
#endif

            var reconstructed = Unfilter(expected, width, height, format.BytesPerPixel);
            var pixels = new byte[reconstructed.Length];
            image.Frames[0].CopyPixelBytesTo(pixels);
            if (format.BitDepth == 16)
            {
                // The samples of the file are big-endian, the pixels little-endian
                for (var i = 0; i < pixels.Length; i += 2)
                {
                    (pixels[i], pixels[i + 1]) = (pixels[i + 1], pixels[i]);
                }
            }

            FuzzAssert.True(pixels.AsSpan().SequenceEqual(reconstructed), "The decoded pixels differ from the scalar reconstruction of the scanlines");
        }
        finally
        {
            image?.Dispose();
        }
    }

    /// <summary>Decodes a zlib datastream with the reference inflater.</summary>
    /// <returns>The scanlines, or <see langword="null"/> when the datastream is invalid, is not exactly <paramref name="length"/> bytes, or has bytes after its end.</returns>
    private static byte[]? GetReferenceScanlines(byte[] zlib, int length)
    {
#if NET11_0_OR_GREATER
        using var decoder = new ZLibDecoder();
        var output = new byte[length + 1];
        var status = decoder.Decompress(zlib, output, out var consumed, out var written);
        return status == OperationStatus.Done && consumed == zlib.Length && written == length ? output[..length] : null;
#else
        _ = zlib;
        _ = length;
        return null;
#endif
    }

#if !NET11_0_OR_GREATER
    /// <summary>Computes the Adler-32 checksum (RFC 1950, section 8.2) byte by byte.</summary>
    private static uint ReferenceAdler32(ReadOnlySpan<byte> data)
    {
        uint a = 1;
        uint b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static byte[]? GetStreamScanlines(byte[] zlib, int length)
    {
        try
        {
            using var stream = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress);
            var output = new byte[length + 1];
            var written = 0;
            int read;
            while (written < output.Length && (read = stream.Read(output, written, output.Length - written)) > 0)
            {
                written += read;
            }

            return written == length ? output[..length] : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
#endif

    /// <summary>Reconstructs the scanlines with the formulas of the PNG specification (section 9), byte by byte.</summary>
    private static byte[] Unfilter(byte[] scanlines, int width, int height, int bytesPerPixel)
    {
        var rowLength = width * bytesPerPixel;
        var result = new byte[rowLength * height];
        for (var row = 0; row < height; row++)
        {
            var filter = scanlines[row * (rowLength + 1)];
            for (var i = 0; i < rowLength; i++)
            {
                var x = scanlines[(row * (rowLength + 1)) + 1 + i];
                int a = i >= bytesPerPixel ? result[(row * rowLength) + i - bytesPerPixel] : 0;
                int b = row > 0 ? result[((row - 1) * rowLength) + i] : 0;
                int c = row > 0 && i >= bytesPerPixel ? result[((row - 1) * rowLength) + i - bytesPerPixel] : 0;
                var predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                result[(row * rowLength) + i] = (byte)(x + predictor);
            }
        }

        return result;

        static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static byte[] CreatePng(int width, int height, byte colorType, byte bitDepth, byte[] zlib, int chunkSize)
    {
        using var file = new MemoryStream();
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = bitDepth;
        header[9] = colorType;
        WriteChunk(file, "IHDR"u8, header);
        for (var offset = 0; offset < zlib.Length; offset += chunkSize)
        {
            WriteChunk(file, "IDAT"u8, zlib.AsSpan(offset, Math.Min(chunkSize, zlib.Length - offset)));
        }

        if (zlib.Length == 0)
        {
            WriteChunk(file, "IDAT"u8, []);
        }

        WriteChunk(file, "IEND"u8, []);
        return file.ToArray();
    }

    private static void WriteChunk(MemoryStream file, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(field, data.Length);
        file.Write(field);
        file.Write(type);
        file.Write(data);
        var crc = UpdateCrc(UpdateCrc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(field, crc);
        file.Write(field);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc;
    }

    private static IEnumerable<FuzzSeed> GetPngImageDataSeeds()
    {
        // Every format, Paeth and the other filters, rows within one piece and across pieces of the Paeth reconstruction,
        // stored and compressed datastreams, whole and split IDAT chunks, and datastreams given as they are
        for (var format = 0; format < PngImageDataFormats.Length; format++)
        {
            foreach (var width in new byte[] { 0, 7, 63, 127 })
            {
                foreach (var mode in new byte[] { 1, 3, 1 | (7 << 2) })
                {
                    var input = new byte[64];
                    input[0] = (byte)format;
                    input[1] = width;
                    input[2] = 3;
                    input[3] = mode;
                    for (var i = 4; i < input.Length; i++)
                    {
                        input[i] = (byte)((i * 37) ^ (i >> 2));
                    }

                    input[4] = 4;
                    yield return new FuzzSeed(FormattableString.Invariant($"scanlines-{format}-{width}-{mode}"), input);
                }
            }
        }

        // The zlib datastream of a 2x2 RGBA image (filter types 4 and 1), then the same datastream with a byte between the
        // deflate data and the Adler-32 checksum, and with the checksum repeated
        var scanlines = new byte[] { 4, 1, 2, 3, 4, 5, 6, 7, 8, 1, 9, 10, 11, 12, 13, 14, 15, 16 };
        using var compressed = new MemoryStream();
        using (var stream = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            stream.Write(scanlines);
        }

        var zlib = compressed.ToArray();
        byte[] prefix = [0, 1, 1, 0];
        yield return new FuzzSeed("zlib", (byte[])[.. prefix, .. zlib]);
        yield return new FuzzSeed("zlib-byte-before-checksum", (byte[])[.. prefix, .. zlib[..^4], 0, .. zlib[^4..]]);
        yield return new FuzzSeed("zlib-checksum-repeated", (byte[])[.. prefix, .. zlib, .. zlib[^4..]]);
        yield return new FuzzSeed("zlib-one-byte-chunks", (byte[])[.. prefix[..3], 1 << 2, .. zlib]);
    }
}
