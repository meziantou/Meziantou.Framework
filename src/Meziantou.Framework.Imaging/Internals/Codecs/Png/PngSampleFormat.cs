using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoded sample layout of a PNG image (IHDR color type and bit depth, <c>PLTE</c>, <c>tRNS</c>) and its expansion
/// from unfiltered scanlines into the lossless <em>source</em> pixel layout handed to the frame sink.
/// Shared by static PNG decoding and APNG frames, which use the same IHDR layout.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Grayscale below 8 bits is scaled to 8 bits exactly (<c>v * 255 / (2^depth - 1)</c>: x255, x85, x17), 16-bit samples are kept (big-endian to native).</description></item>
/// <item><description>Source layouts: <see cref="PixelFormat.Gray8"/>/<see cref="PixelFormat.Gray16"/> (gray), <see cref="PixelFormat.Rgb24"/> (RGB 8, palette), <see cref="PixelFormat.Rgba32"/>/<see cref="PixelFormat.Rgba64"/> (alpha, <c>tRNS</c>, RGB 16, gray+alpha replicated to RGBA).</description></item>
/// <item><description>A <c>tRNS</c> key (gray or RGB) makes exactly the samples equal to the key fully transparent; their color is kept. The key is compared with the encoded sample value (before scaling), all 16 bits included.</description></item>
/// <item><description>Palette entries without a <c>tRNS</c> value are opaque. A palette index outside the <c>PLTE</c> entries is <see cref="InvalidImageContentException"/>.</description></item>
/// </list>
/// </remarks>
internal sealed class PngSampleFormat
{
    private readonly byte[]? _palette; // 256 RGBA entries
    private readonly int _paletteCount;
    private readonly bool _hasKey;
    private readonly ushort _keyRed;
    private readonly ushort _keyGreen;
    private readonly ushort _keyBlue;

    private PngSampleFormat(byte colorType, byte bitDepth, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> transparency, bool hasTransparency)
    {
        ColorType = colorType;
        BitDepth = bitDepth;
        Channels = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(colorType), colorType, "The PNG color type is not valid."),
        };

        BitsPerPixel = Channels * bitDepth;
        FilterBytesPerPixel = Math.Max(1, BitsPerPixel / 8);
        var is16Bit = bitDepth == 16;
        switch (colorType)
        {
            case 0:
                _hasKey = hasTransparency;
                if (_hasKey)
                {
                    _keyRed = _keyGreen = _keyBlue = BinaryPrimitives.ReadUInt16BigEndian(transparency);
                }

                SourcePixelFormat = (is16Bit, _hasKey) switch
                {
                    (false, false) => PixelFormat.Gray8,
                    (true, false) => PixelFormat.Gray16,
                    (false, true) => PixelFormat.Rgba32,
                    _ => PixelFormat.Rgba64,
                };
                break;

            case 2:
                _hasKey = hasTransparency;
                if (_hasKey)
                {
                    _keyRed = BinaryPrimitives.ReadUInt16BigEndian(transparency);
                    _keyGreen = BinaryPrimitives.ReadUInt16BigEndian(transparency[2..]);
                    _keyBlue = BinaryPrimitives.ReadUInt16BigEndian(transparency[4..]);
                }

                SourcePixelFormat = is16Bit ? PixelFormat.Rgba64 : _hasKey ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
                break;

            case 3:
                _paletteCount = palette.Length / 3;
                _palette = new byte[256 * 4];
                for (var i = 0; i < _paletteCount; i++)
                {
                    _palette[i * 4] = palette[i * 3];
                    _palette[(i * 4) + 1] = palette[(i * 3) + 1];
                    _palette[(i * 4) + 2] = palette[(i * 3) + 2];
                    _palette[(i * 4) + 3] = i < transparency.Length ? transparency[i] : (byte)0xFF;
                }

                SourcePixelFormat = hasTransparency ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
                break;

            default:
                SourcePixelFormat = is16Bit ? PixelFormat.Rgba64 : PixelFormat.Rgba32;
                break;
        }

        SourceBytesPerPixel = PixelFormats.GetBytesPerPixel(SourcePixelFormat);
    }

    /// <summary>Gets the IHDR color type: 0 gray, 2 RGB, 3 palette, 4 gray+alpha, 6 RGBA.</summary>
    public byte ColorType { get; }

    /// <summary>Gets the IHDR bit depth.</summary>
    public byte BitDepth { get; }

    /// <summary>Gets the number of samples per pixel.</summary>
    public int Channels { get; }

    /// <summary>Gets the number of encoded bits per pixel.</summary>
    public int BitsPerPixel { get; }

    /// <summary>Gets the byte distance used by the filters (bytes per complete pixel, at least 1).</summary>
    public int FilterBytesPerPixel { get; }

    /// <summary>Gets the layout of the expanded rows.</summary>
    public PixelFormat SourcePixelFormat { get; }

    /// <summary>Gets the number of bytes per expanded pixel.</summary>
    public int SourceBytesPerPixel { get; }

    /// <summary>
    /// Gets a value indicating whether an unfiltered scanline already is a row of source pixels (8-bit gray, RGB or RGBA
    /// without a <c>tRNS</c> key), so that <see cref="Expand"/> would only copy it.
    /// </summary>
    public bool IsIdentity => BitDepth == 8 && !_hasKey && ColorType is 0 or 2 or 6;

    /// <summary>Creates the sample format of the image walked by a structure parser (IHDR, PLTE, tRNS already validated).</summary>
    public static PngSampleFormat Create(PngStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var transparency = structure.Transparency;
        return new PngSampleFormat(structure.ColorType, structure.BitDepth, structure.Palette.Span, transparency is { } value ? value.Span : default, transparency is not null);
    }

    /// <summary>Creates a sample format from validated header values.</summary>
    /// <param name="colorType">The IHDR color type.</param>
    /// <param name="bitDepth">The IHDR bit depth (a legal combination with <paramref name="colorType"/>).</param>
    /// <param name="palette">The PLTE entries (RGB triplets), or empty.</param>
    /// <param name="transparency">The tRNS chunk data, or <see langword="null"/> when there is none.</param>
    public static PngSampleFormat Create(byte colorType, byte bitDepth, ReadOnlySpan<byte> palette, byte[]? transparency)
        => new(colorType, bitDepth, palette, transparency, transparency is not null);

    /// <summary>Gets the number of bytes of an encoded scanline of <paramref name="width"/> pixels, filter byte excluded.</summary>
    public long GetScanlineLength(int width) => (((long)width * BitsPerPixel) + 7) / 8;

    /// <summary>Expands an unfiltered scanline into source pixels.</summary>
    /// <param name="scanline">The unfiltered scanline (filter byte excluded).</param>
    /// <param name="width">The number of pixels.</param>
    /// <param name="destination">At least <c>width * SourceBytesPerPixel</c> bytes.</param>
    /// <exception cref="InvalidImageContentException">A palette index is outside the palette.</exception>
    public void Expand(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        switch (ColorType)
        {
            case 0:
                if (BitDepth == 16)
                {
                    ExpandGray16(scanline, width, destination);
                }
                else
                {
                    ExpandGray(scanline, width, destination);
                }

                break;

            case 2:
                if (BitDepth == 16)
                {
                    ExpandRgb16(scanline, width, destination);
                }
                else
                {
                    ExpandRgb8(scanline, width, destination);
                }

                break;

            case 3:
                ExpandPalette(scanline, width, destination);
                break;

            case 4:
                if (BitDepth == 16)
                {
                    ExpandGrayAlpha16(scanline, width, destination);
                }
                else
                {
                    ExpandGrayAlpha8(scanline, width, destination);
                }

                break;

            default:
                var length = width * SourceBytesPerPixel;
                if (BitDepth == 16)
                {
                    SampleEndianness.ReadBigEndian(scanline[..length], unsafe(MemoryMarshal.Cast<byte, ushort>(destination[..length])));
                }
                else
                {
                    scanline[..length].CopyTo(destination);
                }

                break;
        }
    }

    /// <summary>Reads the sub-byte or 8-bit sample <paramref name="index"/> of a packed scanline.</summary>
    private static int ReadPackedSample(ReadOnlySpan<byte> scanline, int index, int bitDepth)
    {
        if (bitDepth == 8)
            return scanline[index];

        var bit = index * bitDepth;
        var shift = 8 - bitDepth - (bit & 7);
        return (scanline[bit >> 3] >> shift) & ((1 << bitDepth) - 1);
    }

    private void ExpandGray(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        var depth = BitDepth;
        if (!_hasKey)
        {
            if (depth == 8)
            {
                scanline[..width].CopyTo(destination);
                return;
            }

            PackedGraySamples.Expand(scanline, depth, width, invert: false, destination);
            return;
        }

        var scale = 255 / ((1 << depth) - 1);
        for (var x = 0; x < width; x++)
        {
            var sample = ReadPackedSample(scanline, x, depth);
            var value = (byte)(sample * scale);
            var pixel = destination.Slice(x * 4, 4);
            pixel[0] = value;
            pixel[1] = value;
            pixel[2] = value;
            pixel[3] = sample == _keyRed ? (byte)0 : (byte)0xFF;
        }
    }

    private void ExpandGray16(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        if (!_hasKey)
        {
            SampleEndianness.ReadBigEndian(scanline[..(width * 2)], unsafe(MemoryMarshal.Cast<byte, ushort>(destination[..(width * 2)])));
            return;
        }

        var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(destination[..(width * 8)]));
        for (var x = 0; x < width; x++)
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(scanline[(x * 2)..]);
            pixels[x * 4] = value;
            pixels[(x * 4) + 1] = value;
            pixels[(x * 4) + 2] = value;
            pixels[(x * 4) + 3] = value == _keyRed ? (ushort)0 : ushort.MaxValue;
        }
    }

    private void ExpandRgb8(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        if (!_hasKey)
        {
            scanline[..(width * 3)].CopyTo(destination);
            return;
        }

        for (var x = 0; x < width; x++)
        {
            var source = scanline.Slice(x * 3, 3);
            var pixel = destination.Slice(x * 4, 4);
            pixel[0] = source[0];
            pixel[1] = source[1];
            pixel[2] = source[2];
            pixel[3] = source[0] == _keyRed && source[1] == _keyGreen && source[2] == _keyBlue ? (byte)0 : (byte)0xFF;
        }
    }

    private void ExpandRgb16(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        if (!_hasKey)
        {
            ExpandRgb16Opaque(scanline, width, destination);
            return;
        }

        var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(destination[..(width * 8)]));
        for (var x = 0; x < width; x++)
        {
            var source = scanline.Slice(x * 6, 6);
            var red = BinaryPrimitives.ReadUInt16BigEndian(source);
            var green = BinaryPrimitives.ReadUInt16BigEndian(source[2..]);
            var blue = BinaryPrimitives.ReadUInt16BigEndian(source[4..]);
            pixels[x * 4] = red;
            pixels[(x * 4) + 1] = green;
            pixels[(x * 4) + 2] = blue;
            pixels[(x * 4) + 3] = red == _keyRed && green == _keyGreen && blue == _keyBlue ? (ushort)0 : ushort.MaxValue;
        }
    }

    /// <summary>
    /// Expands big-endian 16-bit RGB samples to opaque native-endian <see cref="PixelFormat.Rgba64"/> pixels. With 128-bit
    /// hardware vectors on a little-endian machine, two pixels are shuffled at a time; the result equals
    /// <see cref="ExpandRgb16OpaqueScalar"/>.
    /// </summary>
    internal static void ExpandRgb16Opaque(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            var shuffle = Vector128.Create((byte)1, 0, 3, 2, 5, 4, 0xFF, 0xFF, 7, 6, 9, 8, 11, 10, 0xFF, 0xFF);
            var alpha = Vector128.Create(0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0xFF, (byte)0xFF);

            // 16 bytes are loaded for the 12 of two pixels
            for (; (x * 6) + Vector128<byte>.Count <= width * 6; x += 2)
            {
                (Vector128.Shuffle(Vector128.Create(scanline[(x * 6)..]), shuffle) | alpha).CopyTo(destination[(x * 8)..]);
            }
        }

        ExpandRgb16OpaqueScalar(scanline[(x * 6)..(width * 6)], width - x, destination[(x * 8)..]);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="ExpandRgb16Opaque"/>.</summary>
    internal static void ExpandRgb16OpaqueScalar(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(destination[..(width * 8)]));
        for (var x = 0; x < width; x++)
        {
            var source = scanline.Slice(x * 6, 6);
            pixels[x * 4] = BinaryPrimitives.ReadUInt16BigEndian(source);
            pixels[(x * 4) + 1] = BinaryPrimitives.ReadUInt16BigEndian(source[2..]);
            pixels[(x * 4) + 2] = BinaryPrimitives.ReadUInt16BigEndian(source[4..]);
            pixels[(x * 4) + 3] = ushort.MaxValue;
        }
    }

    private void ExpandPalette(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        var depth = BitDepth;
        var invalid = IndexedPixels.FindIndexOutside(scanline, depth, width, _paletteCount);
        if (invalid >= 0)
            throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The palette index {IndexedPixels.ReadIndex(scanline, invalid, depth)} is outside the {_paletteCount} PLTE entries."), ImageFormat.Png);

        // The RGBA entries are packed Rgba32 pixels
        IndexedPixels.Expand(scanline, depth, width, unsafe(MemoryMarshal.Cast<byte, uint>(_palette.AsSpan())), destination, rgba: SourceBytesPerPixel == 4);
    }

    /// <summary>
    /// Expands 8-bit gray+alpha samples to <see cref="PixelFormat.Rgba32"/> (the gray replicated). With 128-bit hardware
    /// vectors, eight pixels are shuffled at a time; the result equals <see cref="ExpandGrayAlpha8Scalar"/>.
    /// </summary>
    internal static void ExpandGrayAlpha8(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var low = Vector128.Create((byte)0, 0, 0, 1, 2, 2, 2, 3, 4, 4, 4, 5, 6, 6, 6, 7);
            var high = Vector128.Create((byte)8, 8, 8, 9, 10, 10, 10, 11, 12, 12, 12, 13, 14, 14, 14, 15);
            for (; x + 8 <= width; x += 8)
            {
                var samples = Vector128.Create(scanline[(x * 2)..]);
                Vector128.Shuffle(samples, low).CopyTo(destination[(x * 4)..]);
                Vector128.Shuffle(samples, high).CopyTo(destination[((x * 4) + 16)..]);
            }
        }

        ExpandGrayAlpha8Scalar(scanline[(x * 2)..(width * 2)], width - x, destination[(x * 4)..]);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="ExpandGrayAlpha8"/>.</summary>
    internal static void ExpandGrayAlpha8Scalar(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        for (var x = 0; x < width; x++)
        {
            var gray = scanline[x * 2];
            var pixel = destination.Slice(x * 4, 4);
            pixel[0] = gray;
            pixel[1] = gray;
            pixel[2] = gray;
            pixel[3] = scanline[(x * 2) + 1];
        }
    }

    /// <summary>
    /// Expands big-endian 16-bit gray+alpha samples to native-endian <see cref="PixelFormat.Rgba64"/> pixels. With 128-bit
    /// hardware vectors on a little-endian machine, four pixels are shuffled at a time; the result equals
    /// <see cref="ExpandGrayAlpha16Scalar"/>.
    /// </summary>
    internal static void ExpandGrayAlpha16(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            var low = Vector128.Create((byte)1, 0, 1, 0, 1, 0, 3, 2, 5, 4, 5, 4, 5, 4, 7, 6);
            var high = Vector128.Create((byte)9, 8, 9, 8, 9, 8, 11, 10, 13, 12, 13, 12, 13, 12, 15, 14);
            for (; x + 4 <= width; x += 4)
            {
                var samples = Vector128.Create(scanline[(x * 4)..]);
                Vector128.Shuffle(samples, low).CopyTo(destination[(x * 8)..]);
                Vector128.Shuffle(samples, high).CopyTo(destination[((x * 8) + 16)..]);
            }
        }

        ExpandGrayAlpha16Scalar(scanline[(x * 4)..(width * 4)], width - x, destination[(x * 8)..]);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="ExpandGrayAlpha16"/>.</summary>
    internal static void ExpandGrayAlpha16Scalar(ReadOnlySpan<byte> scanline, int width, Span<byte> destination)
    {
        var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(destination[..(width * 8)]));
        for (var x = 0; x < width; x++)
        {
            var gray = BinaryPrimitives.ReadUInt16BigEndian(scanline[(x * 4)..]);
            pixels[x * 4] = gray;
            pixels[(x * 4) + 1] = gray;
            pixels[(x * 4) + 2] = gray;
            pixels[(x * 4) + 3] = BinaryPrimitives.ReadUInt16BigEndian(scanline[((x * 4) + 2)..]);
        }
    }
}
