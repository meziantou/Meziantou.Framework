using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Imaging.TestHarness.Bmp;

/// <summary>
/// An independent, deliberately simple reading of the Windows BMP subset the library supports, used to verify the library's
/// encoder output without the library's decoder. It keeps the whole input in memory, expands every pixel to straight RGBA8,
/// and is strict: an unsupported DIB header, an illegal field, a malformed mask, a palette index outside the palette, a
/// pixel-data offset inside the header and truncation throw <see cref="InvalidDataException"/>.
/// </summary>
public sealed class ReferenceBmp
{
    private ReferenceBmp(int width, int height, int bitsPerPixel, uint compression, bool topDown, bool hasAlphaMask, int paletteEntries, int headerLength, (int X, int Y) pixelsPerMeter, byte[] rgba)
    {
        Width = width;
        Height = height;
        BitsPerPixel = bitsPerPixel;
        Compression = compression;
        IsTopDown = topDown;
        HasAlphaMask = hasAlphaMask;
        PaletteEntries = paletteEntries;
        HeaderLength = headerLength;
        PixelsPerMeter = pixelsPerMeter;
        Rgba = rgba;
    }

    /// <summary>Gets the width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height, in pixels (the absolute value of <c>biHeight</c>).</summary>
    public int Height { get; }

    /// <summary>Gets the number of bits per pixel.</summary>
    public int BitsPerPixel { get; }

    /// <summary>Gets <c>biCompression</c>.</summary>
    public uint Compression { get; }

    /// <summary>Gets a value indicating whether the first stored row is the top row.</summary>
    public bool IsTopDown { get; }

    /// <summary>Gets a value indicating whether a non-zero alpha mask is declared.</summary>
    public bool HasAlphaMask { get; }

    /// <summary>Gets the number of palette entries.</summary>
    public int PaletteEntries { get; }

    /// <summary>Gets the length of the DIB header.</summary>
    public int HeaderLength { get; }

    /// <summary>Gets <c>biXPelsPerMeter</c> and <c>biYPelsPerMeter</c>.</summary>
    public (int X, int Y) PixelsPerMeter { get; }

    /// <summary>Gets the decoded pixels as straight RGBA8, row-major, top-down.</summary>
    public ReadOnlyMemory<byte> Rgba { get; }

    /// <summary>Reads a BMP file.</summary>
    /// <param name="data">The whole file.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidDataException">The input is malformed or uses an unsupported variant.</exception>
    public static ReferenceBmp Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18 || data[0] != (byte)'B' || data[1] != (byte)'M')
            throw new InvalidDataException("Missing BMP file header.");

        var offsetBits = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        var headerLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        if (headerLength is not (40 or 52 or 56 or 108 or 124))
            throw new InvalidDataException($"Unsupported DIB header of {headerLength} bytes.");

        Require(data, 14 + headerLength);
        var dib = data.Slice(14, headerLength);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var storedHeight = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]);
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var pixelsPerMeter = (BinaryPrimitives.ReadInt32LittleEndian(dib[24..]), BinaryPrimitives.ReadInt32LittleEndian(dib[28..]));
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (width <= 0 || storedHeight is 0 or int.MinValue || planes != 1)
            throw new InvalidDataException("Illegal BMP dimensions or plane count.");

        if (compression is not (0 or 3 or 6))
            throw new InvalidDataException($"Unsupported BMP compression {compression}.");

        if (bitsPerPixel is not (1 or 4 or 8 or 16 or 24 or 32))
            throw new InvalidDataException($"Unsupported BMP bit count {bitsPerPixel}.");

        if (compression is 3 or 6 && bitsPerPixel is not (16 or 32))
            throw new InvalidDataException("Explicit masks are only defined for 16 and 32 bits per pixel.");

        var height = Math.Abs(storedHeight);
        var indexed = bitsPerPixel <= 8;
        var maximumEntries = indexed ? 1 << bitsPerPixel : 256;
        if (colorsUsed > (uint)maximumEntries)
            throw new InvalidDataException($"The BMP declares {colorsUsed} palette entries.");

        var entries = colorsUsed != 0 ? (int)colorsUsed : (indexed ? 1 << bitsPerPixel : 0);
        Span<uint> masks = [0, 0, 0, 0];
        var position = 14 + headerLength;
        if (headerLength >= 52)
        {
            masks[0] = BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]);
            masks[1] = BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]);
            masks[2] = BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]);
            if (headerLength >= 56)
            {
                masks[3] = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
            }

            if (compression is not (3 or 6))
            {
                masks.Clear();
            }
        }
        else if (compression is 3 or 6)
        {
            var count = compression == 6 ? 4 : 3;
            Require(data, position + (4 * count));
            for (var i = 0; i < count; i++)
            {
                masks[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + (4 * i))..]);
            }

            position += 4 * count;
        }

        if (compression is 3 or 6)
        {
            if (masks[0] == 0 || masks[1] == 0 || masks[2] == 0)
                throw new InvalidDataException("A color mask is zero.");
        }
        else if (bitsPerPixel == 16)
        {
            masks[0] = 0x7C00;
            masks[1] = 0x03E0;
            masks[2] = 0x001F;
        }
        else if (bitsPerPixel == 32)
        {
            masks[0] = 0x00FF_0000;
            masks[1] = 0x0000_FF00;
            masks[2] = 0x0000_00FF;
        }

        var channels = new (uint Mask, int Shift, int Bits)[4];
        if (bitsPerPixel is 16 or 32)
        {
            uint used = 0;
            for (var i = 0; i < 4; i++)
            {
                var mask = masks[i];
                if (mask == 0)
                    continue;

                var shift = BitOperations.TrailingZeroCount(mask);
                var bits = BitOperations.PopCount(mask);
                if ((mask >> shift) != (1u << bits) - 1 || shift + bits > bitsPerPixel || bits > 8 || (used & mask) != 0)
                    throw new InvalidDataException($"Malformed BMP mask 0x{mask:X8}.");

                used |= mask;
                channels[i] = (mask, shift, bits);
            }
        }

        var palette = new byte[entries * 3];
        if (entries > 0)
        {
            Require(data, position + (4 * entries));
            for (var i = 0; i < entries; i++)
            {
                palette[(i * 3) + 0] = data[position + (4 * i) + 2];
                palette[(i * 3) + 1] = data[position + (4 * i) + 1];
                palette[(i * 3) + 2] = data[position + (4 * i)];
            }

            position += 4 * entries;
        }

        if (offsetBits < (uint)position)
            throw new InvalidDataException($"The BMP pixel data offset {offsetBits} is inside the header or palette.");

        position = (int)offsetBits;
        var rowLength = ((width * bitsPerPixel) + 31) / 32 * 4;
        Require(data, position + (rowLength * height));

        var rgba = new byte[width * height * 4];
        for (var index = 0; index < height; index++)
        {
            var row = data.Slice(position + (index * rowLength), rowLength);
            var y = storedHeight < 0 ? index : height - 1 - index;
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                if (bitsPerPixel == 24)
                {
                    rgba[offset] = row[(x * 3) + 2];
                    rgba[offset + 1] = row[(x * 3) + 1];
                    rgba[offset + 2] = row[x * 3];
                    rgba[offset + 3] = byte.MaxValue;
                }
                else if (indexed)
                {
                    var value = bitsPerPixel switch
                    {
                        1 => (row[x >> 3] >> (7 - (x & 7))) & 1,
                        4 => (x & 1) == 0 ? row[x >> 1] >> 4 : row[x >> 1] & 0x0F,
                        _ => row[x],
                    };

                    if (value >= entries)
                        throw new InvalidDataException($"The BMP palette index {value} is outside the {entries}-entry palette.");

                    rgba[offset] = palette[value * 3];
                    rgba[offset + 1] = palette[(value * 3) + 1];
                    rgba[offset + 2] = palette[(value * 3) + 2];
                    rgba[offset + 3] = byte.MaxValue;
                }
                else
                {
                    var size = bitsPerPixel / 8;
                    uint value = size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]) : BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]);
                    rgba[offset] = Scale(value, channels[0]);
                    rgba[offset + 1] = Scale(value, channels[1]);
                    rgba[offset + 2] = Scale(value, channels[2]);
                    rgba[offset + 3] = channels[3].Mask == 0 ? byte.MaxValue : Scale(value, channels[3]);
                }
            }
        }

        return new ReferenceBmp(width, height, bitsPerPixel, compression, storedHeight < 0, masks[3] != 0, entries, headerLength, pixelsPerMeter, rgba);
    }

    private static byte Scale(uint pixel, (uint Mask, int Shift, int Bits) channel)
    {
        var maximum = (1u << channel.Bits) - 1;
        var value = (pixel & channel.Mask) >> channel.Shift;
        return (byte)(((value * 255) + (maximum / 2)) / maximum);
    }

    private static void Require(ReadOnlySpan<byte> data, int length)
    {
        if (data.Length < length)
            throw new InvalidDataException($"The BMP data is truncated ({data.Length} bytes, {length} needed).");
    }
}
