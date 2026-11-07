using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A color type and a bit depth chosen by <see cref="Formats.PngOptimization.MinimumSize"/> for the pixels of a file, and the
/// conversion of wide rows to its scanlines.
/// </summary>
/// <remarks>
/// <para>
/// The optimized encoder works on wide rows: <see cref="PixelFormat.Rgba32"/> for the 8-bit pixel formats and
/// <see cref="PixelFormat.Rgba64"/> for the 16-bit ones, whatever the stored pixel format, so that the transparent black
/// of an APNG canvas is representable. A layout stores the wide pixels exactly: the analysis that chose it
/// (<see cref="PngLayoutAnalysis"/>) checked every pixel, and <see cref="FindMismatch"/> checks the frames that come after
/// the header in a sequential writer.
/// </para>
/// <list type="bullet">
/// <item><description>Grayscale (color type 0) at 1, 2, 4, 8 or 16 bits: the samples are multiples of <c>max / (2^depth - 1)</c>.</description></item>
/// <item><description>RGB (2), grayscale with alpha (4) and RGBA (6) at 8 or 16 bits: 16-bit samples are written at 8 bits only when both of their bytes are equal.</description></item>
/// <item><description>
/// A <c>tRNS</c> color key on grayscale or RGB: a pixel whose color is the key is fully transparent, and every other
/// pixel is opaque. It is either the color of every transparent pixel of the file, or a color that no pixel has, added to
/// an opaque animation so that the unchanged pixels of a frame can be transparent.
/// </description></item>
/// <item><description>A palette (3) at 1, 2, 4 or 8 bits, with the entries of <see cref="PngIndexedRows"/> (frozen).</description></item>
/// </list>
/// </remarks>
internal sealed class PngOptimizedLayout
{
    private readonly uint _step;

    private PngOptimizedLayout(byte colorType, byte bitDepth, bool isWide16, PngWideColor? key, PngIndexedRows? palette)
    {
        ColorType = colorType;
        BitDepth = bitDepth;
        IsWide16 = isWide16;
        Key = key;
        Palette = palette;
        Channels = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            _ => 4,
        };

        _step = bitDepth < 8 ? (isWide16 ? 65535u : 255u) / ((1u << bitDepth) - 1) : 1;
    }

    /// <summary>Gets the IHDR color type: 0 gray, 2 RGB, 3 indexed, 4 gray and alpha, 6 RGBA.</summary>
    public byte ColorType { get; }

    /// <summary>Gets the IHDR bit depth.</summary>
    public byte BitDepth { get; }

    /// <summary>Gets a value indicating whether the wide rows are <see cref="PixelFormat.Rgba64"/> (otherwise <see cref="PixelFormat.Rgba32"/>).</summary>
    public bool IsWide16 { get; }

    /// <summary>Gets the <c>tRNS</c> color key, in the units of the wide rows, or <see langword="null"/>.</summary>
    public PngWideColor? Key { get; }

    /// <summary>Gets the palette of an indexed layout (borrowed, frozen), or <see langword="null"/>.</summary>
    public PngIndexedRows? Palette { get; }

    /// <summary>Gets the number of samples per pixel.</summary>
    public int Channels { get; }

    /// <summary>Gets a value indicating whether the samples are palette indexes.</summary>
    public bool IsIndexed => ColorType == 3;

    /// <summary>Gets the bytes per wide pixel: 4 or 8.</summary>
    public int WideBytesPerPixel => IsWide16 ? 8 : 4;

    /// <summary>Gets the byte distance of the filters: the bytes per complete pixel, at least one.</summary>
    public int FilterDistance => Math.Max(1, Channels * BitDepth / 8);

    /// <summary>
    /// Gets a value indicating whether a sample value is fully transparent: an alpha channel, a color key, or a palette
    /// entry of alpha 0 (the first entries of a frozen palette are the transparent ones).
    /// </summary>
    public bool HasTransparentValue => ColorType is 4 or 6 || Key is not null || (Palette is { Count: > 0 } palette && (palette.Colors[0] >> 24) == 0);

    /// <summary>Gets a value indicating whether the layout has no sample below 8 bits and no palette, the layouts the adaptive filter heuristic suits.</summary>
    public bool SuitsAdaptiveFilter => !IsIndexed && BitDepth >= 8;

    /// <summary>Creates a grayscale, RGB, grayscale-with-alpha or RGBA layout.</summary>
    /// <param name="colorType">0, 2, 4 or 6.</param>
    /// <param name="bitDepth">1, 2, 4, 8 or 16 for grayscale; 8 or 16 otherwise; 16 only for 16-bit wide rows.</param>
    /// <param name="isWide16">Whether the wide rows are 16-bit.</param>
    /// <param name="key">The color key of a grayscale or RGB layout (its three samples are equal for grayscale), or <see langword="null"/>.</param>
    /// <returns>The layout.</returns>
    public static PngOptimizedLayout Truecolor(byte colorType, byte bitDepth, bool isWide16, PngWideColor? key = null)
    {
        if (colorType is not (0 or 2 or 4 or 6))
            throw new ArgumentOutOfRangeException(nameof(colorType), colorType, "The color type is not a truecolor or grayscale color type.");

        if (bitDepth is not (1 or 2 or 4 or 8 or 16) || (bitDepth < 8 && colorType != 0) || (bitDepth == 16 && !isWide16))
            throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "The bit depth is not valid for the color type.");

        if (key is not null && colorType is 4 or 6)
            throw new ArgumentException("A color type with an alpha channel has no color key.", nameof(key));

        return new(colorType, bitDepth, isWide16, key, palette: null);
    }

    /// <summary>Creates an indexed layout.</summary>
    /// <param name="palette">The palette, frozen, built from wide rows.</param>
    /// <param name="bitDepth">1, 2, 4 or 8; it must address every entry.</param>
    /// <param name="isWide16">Whether the wide rows are 16-bit.</param>
    /// <returns>The layout.</returns>
    public static PngOptimizedLayout Indexed(PngIndexedRows palette, byte bitDepth, bool isWide16)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (!palette.IsFrozen)
            throw new ArgumentException("The palette is not final.", nameof(palette));

        if (bitDepth is not (1 or 2 or 4 or 8) || palette.Count > 1 << bitDepth)
            throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "The bit depth does not address the palette.");

        return new(colorType: 3, bitDepth, isWide16, key: null, palette);
    }

    /// <summary>Gets the layout of <see cref="Formats.PngPixelLayout.Auto"/> without optimization: the one of the pixel format.</summary>
    /// <param name="pixelFormat">The stored pixel format.</param>
    /// <returns>The layout.</returns>
    public static PngOptimizedLayout ForPixelFormat(PixelFormat pixelFormat)
    {
        var encoded = PngEncodedLayout.Get(pixelFormat);
        return Truecolor(encoded.ColorType, encoded.BitDepth, isWide16: encoded.BitDepth == 16);
    }

    /// <summary>Gets a value indicating whether two layouts write the same samples.</summary>
    /// <param name="other">The other layout.</param>
    /// <returns><see langword="true"/> when the color type, the bit depth, the color key and the palette are the same.</returns>
    public bool IsSameAs(PngOptimizedLayout other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return ColorType == other.ColorType && BitDepth == other.BitDepth && Key == other.Key && ReferenceEquals(Palette, other.Palette);
    }

    /// <summary>Gets the length of the scanline of a row of pixels, without its filter type byte.</summary>
    /// <param name="width">The number of pixels.</param>
    /// <returns>The length in bytes.</returns>
    public int GetScanlineLength(int width) => (int)((((long)width * Channels * BitDepth) + 7) / 8);

    /// <summary>Gets the bytes of the chunks the layout adds before the image data: <c>PLTE</c> and <c>tRNS</c>.</summary>
    /// <returns>The length of the chunks, their 12 bytes of length, type and CRC included.</returns>
    public long GetAncillaryBytes()
    {
        if (Palette is { } palette)
        {
            var transparent = 0;
            foreach (var color in palette.Colors)
            {
                if ((color >> 24) != byte.MaxValue)
                {
                    transparent++;
                }
            }

            return 12 + (3L * palette.Count) + (transparent > 0 ? 12 + transparent : 0);
        }

        return Key is null ? 0 : 12 + (ColorType == 0 ? 2 : 6);
    }

    /// <summary>Writes the chunks the layout adds before the image data: <c>PLTE</c> and <c>tRNS</c>, as needed.</summary>
    /// <param name="output">The output buffer.</param>
    public void WriteAncillaryChunks(ImageOutputBuffer output)
    {
        if (Palette is { } palette)
        {
            PngEncoderCodec.WritePalette(output, palette.Colors);
            return;
        }

        if (Key is not { } key)
            return;

        // tRNS of grayscale and RGB: one 16-bit field per sample, holding the sample value at the bit depth of the image
        Span<byte> data = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)ToSample(key.R));
        if (ColorType == 0)
        {
            PngChunkWriter.Write(output, PngChunkWriter.Trns, data[..2]);
            return;
        }

        BinaryPrimitives.WriteUInt16BigEndian(data[2..], (ushort)ToSample(key.G));
        BinaryPrimitives.WriteUInt16BigEndian(data[4..], (ushort)ToSample(key.B));
        PngChunkWriter.Write(output, PngChunkWriter.Trns, data);
    }

    /// <summary>Writes the scanline of pixels of a wide row.</summary>
    /// <param name="row">The wide row.</param>
    /// <param name="start">The column of the first pixel.</param>
    /// <param name="step">The distance between two pixels.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="mask">
    /// Empty, or one byte per pixel: a non-zero byte writes the transparent value of the layout instead of the pixel
    /// (<see cref="HasTransparentValue"/> must be <see langword="true"/>).
    /// </param>
    /// <param name="unpacked">Scratch for one byte per pixel.</param>
    /// <param name="scanline">The scanline, <see cref="GetScanlineLength"/> bytes.</param>
    public void WriteSamples(ReadOnlySpan<byte> row, int start, int step, int count, ReadOnlySpan<byte> mask, Span<byte> unpacked, Span<byte> scanline)
    {
        if (Palette is { } palette)
        {
            var indexes = BitDepth == 8 ? scanline[..count] : unpacked[..count];
            palette.Map(row, start, step, indexes);
            if (!mask.IsEmpty)
            {
                // The transparent entries are the first ones of the palette
                for (var i = 0; i < count; i++)
                {
                    if (mask[i] != 0)
                    {
                        indexes[i] = 0;
                    }
                }
            }

            if (BitDepth < 8)
            {
                PngEncodedLayout.Pack(indexes, BitDepth, scanline);
            }

            return;
        }

        var wide16 = IsWide16;
        var samples16 = wide16 ? unsafe(MemoryMarshal.Cast<byte, ushort>(row)) : default;
        var key = Key ?? default;
        if (BitDepth < 8)
        {
            var levels = unpacked[..count];
            var keyLevel = (byte)ToSample(key.R);
            for (int i = 0, x = start; i < count; i++, x += step)
            {
                levels[i] = !mask.IsEmpty && mask[i] != 0 ? keyLevel : (byte)((wide16 ? samples16[x * 4] : row[x * 4]) / _step);
            }

            PngEncodedLayout.Pack(levels, BitDepth, scanline);
            return;
        }

        var bytesPerSample = BitDepth / 8;
        var offset = 0;
        for (int i = 0, x = start; i < count; i++, x += step)
        {
            uint r, g, b, a;
            if (!mask.IsEmpty && mask[i] != 0)
            {
                (r, g, b, a) = (key.R, key.G, key.B, 0u);
            }
            else if (wide16)
            {
                var pixel = samples16.Slice(x * 4, 4);
                (r, g, b, a) = (pixel[0], pixel[1], pixel[2], pixel[3]);
            }
            else
            {
                var pixel = row.Slice(x * 4, 4);
                (r, g, b, a) = (pixel[0], pixel[1], pixel[2], pixel[3]);
            }

            switch (ColorType)
            {
                case 0:
                    Write(scanline, ref offset, r);
                    break;
                case 2:
                    Write(scanline, ref offset, r);
                    Write(scanline, ref offset, g);
                    Write(scanline, ref offset, b);
                    break;
                case 4:
                    Write(scanline, ref offset, r);
                    Write(scanline, ref offset, a);
                    break;
                default:
                    Write(scanline, ref offset, r);
                    Write(scanline, ref offset, g);
                    Write(scanline, ref offset, b);
                    Write(scanline, ref offset, a);
                    break;
            }
        }

        void Write(Span<byte> destination, ref int position, uint value)
        {
            if (bytesPerSample == 2)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination[position..], (ushort)value);
                position += 2;
            }
            else
            {
                destination[position++] = (byte)(wide16 ? value >> 8 : value);
            }
        }
    }

    /// <summary>Finds the first pixel of a wide row that the layout cannot store exactly.</summary>
    /// <param name="row">The whole wide row.</param>
    /// <param name="x">The column of the pixel, or -1.</param>
    /// <returns>Why the pixel cannot be stored, or <see cref="PngLayoutMismatch.None"/>.</returns>
    public PngLayoutMismatch FindMismatch(ReadOnlySpan<byte> row, out int x)
    {
        if (Palette is { } palette)
        {
            var status = palette.Check(row, out x);
            return status switch
            {
                PngIndexedRowStatus.Ok => PngLayoutMismatch.None,
                PngIndexedRowStatus.NotEightBit => PngLayoutMismatch.BitDepth,
                _ => PngLayoutMismatch.Color,
            };
        }

        var wide16 = IsWide16;
        var max = wide16 ? 65535u : 255u;
        var samples16 = wide16 ? unsafe(MemoryMarshal.Cast<byte, ushort>(row)) : default;
        var width = row.Length / WideBytesPerPixel;
        for (x = 0; x < width; x++)
        {
            uint r, g, b, a;
            if (wide16)
            {
                var pixel = samples16.Slice(x * 4, 4);
                (r, g, b, a) = (pixel[0], pixel[1], pixel[2], pixel[3]);
            }
            else
            {
                var pixel = row.Slice(x * 4, 4);
                (r, g, b, a) = (pixel[0], pixel[1], pixel[2], pixel[3]);
            }

            if (ColorType is 0 or 4 && (r != g || r != b))
                return PngLayoutMismatch.Color;

            if (!IsExact(r) || !IsExact(g) || !IsExact(b) || (ColorType is 4 or 6 && !IsExact(a)))
                return PngLayoutMismatch.BitDepth;

            if (ColorType is 0 or 2)
            {
                var isKey = Key is { } key && r == key.R && g == key.G && b == key.B;
                if (a == 0 ? !isKey : a != max || isKey)
                    return Key is null ? PngLayoutMismatch.Alpha : PngLayoutMismatch.ColorKey;
            }
        }

        x = -1;
        return PngLayoutMismatch.None;
    }

    /// <summary>Gets a description of the layout for messages, for example "grayscale 4-bit".</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        var name = ColorType switch
        {
            0 => Key is null ? "grayscale" : "grayscale with a tRNS color key",
            2 => Key is null ? "RGB" : "RGB with a tRNS color key",
            3 => "indexed",
            4 => "grayscale with alpha",
            _ => "RGBA",
        };

        return string.Create(CultureInfo.InvariantCulture, $"{name} {BitDepth}-bit");
    }

    /// <summary>Converts a wide sample to a sample of the bit depth (the sample is exact at that depth).</summary>
    private uint ToSample(uint value) => BitDepth switch
    {
        16 => value,
        8 => IsWide16 ? value >> 8 : value,
        _ => value / _step,
    };

    private bool IsExact(uint value) => BitDepth switch
    {
        16 => true,
        8 => !IsWide16 || (value >> 8) == (value & 0xFF),
        _ => value % _step == 0,
    };
}
