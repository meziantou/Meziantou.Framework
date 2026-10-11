using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The PNG color type and bit depth written for each working pixel format, and the conversion of stored rows to PNG
/// samples. The mapping is fixed (no data-dependent reduction, so a writer never needs a prepass): <c>Gray8</c> → gray 8,
/// <c>Gray16</c> → gray 16, <c>Rgb24</c> → RGB 8, <c>Rgba32</c>/<c>Bgra32</c> → RGBA 8 (B and R swapped for <c>Bgra32</c>),
/// <c>Rgba64</c> → RGBA 16. 16-bit samples are written big-endian from the native-endian <see cref="ushort"/> storage with all
/// 16 bits; no precision or alpha is ever lost. The bytes per pixel equal the storage bytes per pixel.
/// </summary>
internal sealed class PngEncodedLayout
{
    private PngEncodedLayout(PixelFormat pixelFormat, byte colorType, byte bitDepth)
    {
        PixelFormat = pixelFormat;
        ColorType = colorType;
        BitDepth = bitDepth;
        BytesPerPixel = PixelFormats.GetBytesPerPixel(pixelFormat);
    }

    public PixelFormat PixelFormat { get; }

    /// <summary>Gets the IHDR color type: 0 gray, 2 RGB, 6 RGBA.</summary>
    public byte ColorType { get; }

    /// <summary>Gets the IHDR bit depth: 8 or 16.</summary>
    public byte BitDepth { get; }

    /// <summary>Gets the bytes per complete pixel, used by the filters.</summary>
    public int BytesPerPixel { get; }

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
