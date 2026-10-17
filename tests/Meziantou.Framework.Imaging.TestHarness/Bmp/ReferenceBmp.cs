using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Imaging.TestHarness.Bmp;

/// <summary>
/// An independent, deliberately simple reading of the BMP subset the library supports (the Windows headers, the OS/2 1.x and
/// 2.x headers, uncompressed rows and run-length streams), used to verify the library's encoder output without the
/// library's decoder. It keeps the whole input in memory, expands every pixel to straight RGBA8, and is strict: an
/// unsupported DIB header, an illegal field, a malformed mask, a palette index outside the palette, a pixel-data offset
/// inside the header, a run-length command that leaves the canvas and truncation throw <see cref="InvalidDataException"/>.
/// </summary>
public sealed class ReferenceBmp
{
    private ReferenceBmp(int width, int height, int bitsPerPixel, uint compression, bool topDown, bool hasAlphaMask, byte[] palette, int headerLength, (int X, int Y) pixelsPerMeter, int pixelDataLength, byte[] rgba)
    {
        Width = width;
        Height = height;
        BitsPerPixel = bitsPerPixel;
        Compression = compression;
        IsTopDown = topDown;
        HasAlphaMask = hasAlphaMask;
        Palette = palette;
        HeaderLength = headerLength;
        PixelsPerMeter = pixelsPerMeter;
        PixelDataLength = pixelDataLength;
        Rgba = rgba;
    }

    /// <summary>Gets the width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height, in pixels (the absolute value of <c>biHeight</c>).</summary>
    public int Height { get; }

    /// <summary>Gets the number of bits per pixel.</summary>
    public int BitsPerPixel { get; }

    /// <summary>
    /// Gets the stored compression value: <c>biCompression</c>, or <c>ulCompression</c> of an OS/2 2.x header, where 4 is
    /// 24-bit run-length encoding.
    /// </summary>
    public uint Compression { get; }

    /// <summary>Gets a value indicating whether the pixel data is a run-length stream.</summary>
    public bool IsRunLength => Compression is 1 or 2 or 4;

    /// <summary>Gets a value indicating whether the first stored row is the top row.</summary>
    public bool IsTopDown { get; }

    /// <summary>Gets a value indicating whether a non-zero alpha mask is declared.</summary>
    public bool HasAlphaMask { get; }

    /// <summary>Gets the number of palette entries.</summary>
    public int PaletteEntries => Palette.Length / 3;

    /// <summary>Gets the palette as red, green, blue triplets (empty for layouts without a palette).</summary>
    public ReadOnlyMemory<byte> Palette { get; }

    /// <summary>
    /// Gets the number of bytes the pixel data occupies from <c>bfOffBits</c>: the padded rows, or the run-length stream up
    /// to and including its end-of-bitmap marker.
    /// </summary>
    public int PixelDataLength { get; }

    /// <summary>Gets the length of the DIB header.</summary>
    public int HeaderLength { get; }

    /// <summary>Gets <c>biXPelsPerMeter</c> and <c>biYPelsPerMeter</c> (zero when the header stores no resolution in pixels per meter).</summary>
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
        var isCore = headerLength == 12;
        var isWindows = headerLength is 40 or 52 or 56 or 108 or 124;
        var isOs2 = !isWindows && headerLength is >= 16 and <= 64;
        if (!isCore && !isWindows && !isOs2)
            throw new InvalidDataException($"Unsupported DIB header of {headerLength} bytes.");

        Require(data, 14 + headerLength);

        // A short OS/2 2.x header leaves its last fields out: they are zero
        Span<byte> dib = stackalloc byte[124];
        dib.Clear();
        data.Slice(14, headerLength).CopyTo(dib);
        int width, storedHeight, planes, bitsPerPixel;
        uint compression = 0, colorsUsed = 0;
        (int X, int Y) pixelsPerMeter = (0, 0);
        if (isCore)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(dib[4..]);
            storedHeight = BinaryPrimitives.ReadUInt16LittleEndian(dib[6..]);
            planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[8..]);
            bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]);
        }
        else
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
            storedHeight = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
            planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]);
            bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
            compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
            pixelsPerMeter = (BinaryPrimitives.ReadInt32LittleEndian(dib[24..]), BinaryPrimitives.ReadInt32LittleEndian(dib[28..]));
            colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        }

        if (width <= 0 || storedHeight is 0 or int.MinValue || planes != 1)
            throw new InvalidDataException("Illegal BMP dimensions or plane count.");

        // Run-length encoding: 8-bit (1) and 4-bit (2) in every header, 24-bit (4) in an OS/2 2.x header only, where 3 is
        // Huffman 1D instead of explicit masks
        var hasMasks = isWindows && compression is 3 or 6;
        var runLengthBits = compression switch
        {
            1 => 8,
            2 => 4,
            4 when isOs2 => 24,
            _ => 0,
        };

        if (compression != 0 && !hasMasks && runLengthBits == 0)
            throw new InvalidDataException($"Unsupported BMP compression {compression}.");

        if (isWindows ? bitsPerPixel is not (1 or 4 or 8 or 16 or 24 or 32) : bitsPerPixel is not (1 or 4 or 8 or 24))
            throw new InvalidDataException($"Unsupported BMP bit count {bitsPerPixel}.");

        if (hasMasks && bitsPerPixel is not (16 or 32))
            throw new InvalidDataException("Explicit masks are only defined for 16 and 32 bits per pixel.");

        if (runLengthBits != 0 && (bitsPerPixel != runLengthBits || storedHeight < 0))
            throw new InvalidDataException("A run-length stream stores bottom-up pixels of its own bit count.");

        if (isOs2)
        {
            if (storedHeight < 0)
                throw new InvalidDataException("The height of an OS/2 header is unsigned.");

            // usRecording (offset 44) and ulColorEncoding (offset 56) only define the value 0
            if (BinaryPrimitives.ReadUInt16LittleEndian(dib[44..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(dib[56..]) != 0)
                throw new InvalidDataException("Unsupported OS/2 recording algorithm or color encoding.");

            // usUnits (offset 40): the resolution is in pixels per meter for the metric unit only
            if (BinaryPrimitives.ReadUInt16LittleEndian(dib[40..]) != 0)
            {
                pixelsPerMeter = (0, 0);
            }
        }

        var height = Math.Abs(storedHeight);
        var indexed = bitsPerPixel <= 8;
        var maximumEntries = indexed ? 1 << bitsPerPixel : 256;
        if (colorsUsed > (uint)maximumEntries)
            throw new InvalidDataException($"The BMP declares {colorsUsed} palette entries.");

        var entries = colorsUsed != 0 ? (int)colorsUsed : (indexed ? 1 << bitsPerPixel : 0);
        var entryLength = isCore ? 3 : 4;
        if (isCore && indexed)
        {
            // A BITMAPCOREHEADER has no color count: a short palette ends where the pixel data starts
            var available = ((long)offsetBits - 26) / entryLength;
            if (available < 1)
                throw new InvalidDataException($"The BMP pixel data offset {offsetBits} leaves no palette entry.");

            entries = (int)Math.Min(entries, available);
        }

        Span<uint> masks = [0, 0, 0, 0];
        var position = 14 + headerLength;
        if (isWindows && headerLength >= 52)
        {
            masks[0] = BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]);
            masks[1] = BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]);
            masks[2] = BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]);
            if (headerLength >= 56)
            {
                masks[3] = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
            }

            if (!hasMasks)
            {
                masks.Clear();
            }
        }
        else if (hasMasks)
        {
            var count = compression == 6 ? 4 : 3;
            Require(data, position + (4 * count));
            for (var i = 0; i < count; i++)
            {
                masks[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + (4 * i))..]);
            }

            position += 4 * count;
        }

        if (hasMasks)
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
            Require(data, position + (entryLength * entries));
            for (var i = 0; i < entries; i++)
            {
                palette[(i * 3) + 0] = data[position + (entryLength * i) + 2];
                palette[(i * 3) + 1] = data[position + (entryLength * i) + 1];
                palette[(i * 3) + 2] = data[position + (entryLength * i)];
            }

            position += entryLength * entries;
        }

        if (offsetBits < (uint)position)
            throw new InvalidDataException($"The BMP pixel data offset {offsetBits} is inside the header or palette.");

        position = (int)offsetBits;
        if (runLengthBits != 0)
            return ReadRunLength(data, position, width, height, runLengthBits, compression, palette, headerLength, pixelsPerMeter);

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

        return new ReferenceBmp(width, height, bitsPerPixel, compression, storedHeight < 0, masks[3] != 0, palette, headerLength, pixelsPerMeter, rowLength * height, rgba);
    }

    /// <summary>
    /// Reads a run-length stream command by command. The image starts filled with the pixels the stream never writes:
    /// palette index 0, or black for 24-bit pixels.
    /// </summary>
    private static ReferenceBmp ReadRunLength(ReadOnlySpan<byte> data, int start, int width, int height, int bits, uint compression, byte[] palette, int headerLength, (int X, int Y) pixelsPerMeter)
    {
        var entries = palette.Length / 3;
        var rgba = new byte[width * height * 4];
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            if (bits != 24)
            {
                palette.AsSpan(0, 3).CopyTo(rgba.AsSpan(offset));
            }

            rgba[offset + 3] = byte.MaxValue;
        }

        var unit = bits == 24 ? 3 : 1;
        var position = start;
        var x = 0;
        var storedRow = 0;
        while (true)
        {
            Require(data, position + 2);
            int first = data[position];
            int second = data[position + 1];
            if (first == 0 && second == 1)
                return new ReferenceBmp(width, height, bits, compression, topDown: false, hasAlphaMask: false, palette, headerLength, pixelsPerMeter, position + 2 - start, rgba);

            if (storedRow >= height)
                throw new InvalidDataException("The BMP run-length data continues after the last row.");

            if (first == 0 && second == 0)
            {
                storedRow++;
                x = 0;
                position += 2;
                continue;
            }

            if (first == 0 && second == 2)
            {
                Require(data, position + 4);
                x += data[position + 2];
                storedRow += data[position + 3];
                if (x > width || storedRow >= height)
                    throw new InvalidDataException("A BMP run-length delta leaves the canvas.");

                position += 4;
                continue;
            }

            // An encoded run repeats the pixel that follows its count; an absolute run stores literal pixels, word-aligned
            var isAbsolute = first == 0;
            var count = isAbsolute ? second : first;
            if (count > width - x)
                throw new InvalidDataException($"A BMP run of {count} pixels leaves its row.");

            var dataStart = isAbsolute ? position + 2 : position + 1;
            var dataLength = !isAbsolute ? unit : bits == 4 ? (count + 1) / 2 : count * unit;
            Require(data, dataStart + dataLength + (isAbsolute ? dataLength & 1 : 0));
            for (var i = 0; i < count; i++)
            {
                var target = rgba.AsSpan(((((height - 1 - storedRow) * width) + x + i) * 4), 3);
                if (bits == 24)
                {
                    var source = data.Slice(isAbsolute ? dataStart + (i * 3) : dataStart, 3);
                    target[0] = source[2];
                    target[1] = source[1];
                    target[2] = source[0];
                    continue;
                }

                int index;
                if (bits == 8)
                {
                    index = data[isAbsolute ? dataStart + i : dataStart];
                }
                else
                {
                    var packed = data[isAbsolute ? dataStart + (i / 2) : dataStart];
                    index = (i & 1) == 0 ? packed >> 4 : packed & 0x0F;
                }

                if (index >= entries)
                    throw new InvalidDataException($"The BMP palette index {index} is outside the {entries}-entry palette.");

                palette.AsSpan(index * 3, 3).CopyTo(target);
            }

            x += count;
            position = dataStart + dataLength + (isAbsolute ? dataLength & 1 : 0);
        }
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
