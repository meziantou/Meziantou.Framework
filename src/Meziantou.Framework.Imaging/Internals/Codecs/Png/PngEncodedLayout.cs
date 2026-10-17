using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The PNG color type and bit depth written for each working pixel format, and the conversion of stored rows to PNG
/// samples.
/// </summary>
/// <remarks>
/// <para>
/// The default mapping is fixed (no data-dependent reduction, so a writer never needs a prepass): <c>Gray8</c> → gray 8,
/// <c>Gray16</c> → gray 16, <c>Rgb24</c> → RGB 8, <c>Rgba32</c>/<c>Bgra32</c> → RGBA 8 (B and R swapped for <c>Bgra32</c>),
/// <c>Rgba64</c> → RGBA 16. 16-bit samples are written big-endian from the native-endian <see cref="ushort"/> storage with all
/// 16 bits; no precision or alpha is ever lost. The bytes per pixel equal the storage bytes per pixel.
/// </para>
/// <para>
/// The indexed layout (<see cref="GetIndexed"/>, color type 3) depends on the pixels: its bit depth is the smallest that
/// addresses the palette of the file (<see cref="PngIndexedRows"/>), which also converts the stored rows to indexes. Its
/// scanlines pack one index per pixel, the leftmost pixel in the high-order bits of a byte.
/// </para>
/// </remarks>
internal sealed class PngEncodedLayout
{
    private PngEncodedLayout(PixelFormat pixelFormat, byte colorType, byte bitDepth)
    {
        PixelFormat = pixelFormat;
        ColorType = colorType;
        BitDepth = bitDepth;
        BytesPerPixel = colorType == 3 ? 1 : PixelFormats.GetBytesPerPixel(pixelFormat);
    }

    public PixelFormat PixelFormat { get; }

    /// <summary>Gets the IHDR color type: 0 gray, 2 RGB, 3 indexed, 6 RGBA.</summary>
    public byte ColorType { get; }

    /// <summary>Gets the IHDR bit depth: 8 or 16, or 1, 2, 4 or 8 for the indexed layout.</summary>
    public byte BitDepth { get; }

    /// <summary>Gets the bytes per complete pixel, used by the filters (1 for the indexed layout, whatever its bit depth).</summary>
    public int BytesPerPixel { get; }

    /// <summary>Gets a value indicating whether the samples are palette indexes.</summary>
    public bool IsIndexed => ColorType == 3;

    /// <summary>Gets the layout of a pixel format.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The pixel format is not a supported pixel format.</exception>
    public static PngEncodedLayout Get(PixelFormat pixelFormat) => pixelFormat switch
    {
        PixelFormat.Gray8 => new(pixelFormat, colorType: 0, bitDepth: 8),
        PixelFormat.Gray16 => new(pixelFormat, colorType: 0, bitDepth: 16),
        PixelFormat.Rgb24 => new(pixelFormat, colorType: 2, bitDepth: 8),
        PixelFormat.Rgba32 or PixelFormat.Bgra32 => new(pixelFormat, colorType: 6, bitDepth: 8),
        PixelFormat.Rgba64 => new(pixelFormat, colorType: 6, bitDepth: 16),
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "The pixel format is not supported."),
    };

    /// <summary>Gets the indexed layout of a bit depth.</summary>
    /// <param name="pixelFormat">The stored pixel format.</param>
    /// <param name="bitDepth">The bits per palette index: 1, 2, 4 or 8.</param>
    /// <exception cref="ArgumentOutOfRangeException">The bit depth is not a palette bit depth.</exception>
    public static PngEncodedLayout GetIndexed(PixelFormat pixelFormat, byte bitDepth)
    {
        if (bitDepth is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "The bit depth is not a palette bit depth.");

        return new(pixelFormat, colorType: 3, bitDepth);
    }

    /// <summary>
    /// Gets the pixel format whose color space is the one of the written samples, which is what an ICC profile of the
    /// file describes: a palette holds RGB colors, whatever the pixel format of the frames.
    /// </summary>
    /// <param name="layout">The layout setting of the encoder.</param>
    /// <param name="format">The pixel format of the frames.</param>
    /// <returns>The pixel format a profile must be able to label.</returns>
    public static PixelFormat GetLabeledPixelFormat(PngPixelLayout layout, PixelFormat format)
        => layout == PngPixelLayout.Indexed && PixelFormats.IsGrayscale(format) ? PixelFormat.Rgb24 : format;

    /// <summary>Gets the length of the scanline of a row of pixels, without its filter type byte.</summary>
    /// <param name="width">The number of pixels.</param>
    /// <returns>The length in bytes; the last byte of an indexed scanline may hold unused low-order bits.</returns>
    public int GetScanlineLength(int width) => IsIndexed ? (int)((((long)width * BitDepth) + 7) / 8) : width * BytesPerPixel;

    /// <summary>Packs palette indexes into a scanline of the indexed layout.</summary>
    /// <param name="indexes">One index per pixel, each below <c>2^BitDepth</c>.</param>
    /// <param name="destination">The scanline, <see cref="GetScanlineLength"/> bytes; the unused bits of its last byte are zero.</param>
    public void PackIndexes(ReadOnlySpan<byte> indexes, Span<byte> destination) => Pack(indexes, BitDepth, destination);

    /// <summary>Packs samples of one byte each into a scanline of 1-, 2-, 4- or 8-bit samples.</summary>
    /// <param name="indexes">One sample per byte, each below <c>2^bitDepth</c>.</param>
    /// <param name="bitDepth">The bits per sample: 1, 2, 4 or 8.</param>
    /// <param name="destination">The scanline; the unused bits of its last byte are zero.</param>
    public static void Pack(ReadOnlySpan<byte> indexes, int bitDepth, Span<byte> destination)
    {
        if (bitDepth == 8)
        {
            indexes.CopyTo(destination);
            return;
        }

        // The leftmost pixel is in the high-order bits of a byte (PNG specification, section 7.2)
        destination[..(int)((((long)indexes.Length * bitDepth) + 7) / 8)].Clear();
        for (var i = 0; i < indexes.Length; i++)
        {
            var bit = (long)i * bitDepth;
            destination[(int)(bit >> 3)] |= (byte)(indexes[i] << (8 - bitDepth - (int)(bit & 7)));
        }
    }

    /// <summary>Converts stored pixels (native byte layout of <see cref="PixelFormat"/>) to PNG samples.</summary>
    /// <param name="source">The stored pixel bytes.</param>
    /// <param name="destination">The PNG sample bytes, same length.</param>
    public void ToSamples(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        switch (PixelFormat)
        {
            case PixelFormat.Bgra32:
                destination = destination[..source.Length];
                for (var i = 0; i + 3 < source.Length; i += 4)
                {
                    destination[i] = source[i + 2];
                    destination[i + 1] = source[i + 1];
                    destination[i + 2] = source[i];
                    destination[i + 3] = source[i + 3];
                }

                break;

            case PixelFormat.Gray16 or PixelFormat.Rgba64:
                SampleEndianness.WriteBigEndian(unsafe(MemoryMarshal.Cast<byte, ushort>(source)), destination);
                break;

            default:
                source.CopyTo(destination);
                break;
        }
    }
}
