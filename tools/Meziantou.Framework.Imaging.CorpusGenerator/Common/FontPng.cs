using System.IO.Compression;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes a PNG image of 8-bit red, green, blue and alpha samples, for the glyphs of a bitmap font under construction:
/// "Portable Network Graphics (PNG) Specification". The writer has no dependency on the library.
/// </summary>
internal static class FontPng
{
    /// <summary>Writes an image.</summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="rgba">The pixels, row by row from the top: four bytes each, with a color that is not multiplied by its opacity.</param>
    public static byte[] Write(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("The pixels are four bytes each", nameof(rgba));

        var writer = new FontWriter();
        writer.Bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR: the size, 8 bits per sample, truecolor with alpha, no interlacing.
        var header = new FontWriter();
        header.UInt32((uint)width);
        header.UInt32((uint)height);
        header.Bytes([8, 6, 0, 0, 0]);
        WriteChunk(writer, "IHDR", header.ToArray());

        // IDAT: each row starts with its filter, none.
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba.Slice(y * width * 4, width * 4));
            }
        }

        WriteChunk(writer, "IDAT", compressed.ToArray());
        WriteChunk(writer, "IEND", []);
        return writer.ToArray();
    }

    private static void WriteChunk(FontWriter writer, string type, byte[] data)
    {
        writer.UInt32((uint)data.Length);
        byte[] body = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        writer.Bytes(body);

        // The cyclic redundancy check of the type and of the data: the polynomial of the specification, bit by bit.
        var crc = 0xFFFFFFFFu;
        foreach (var value in body)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        writer.UInt32(~crc);
    }
}
