using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The samples of the histogram-based tone operations: rows of stored pixels read as red, green, blue and alpha samples at
/// the storage precision (0 to 255 for 8-bit formats, 0 to 65,535 for 16-bit ones), and their colors written back.
/// </summary>
internal static class ToneSamples
{
    /// <summary>Gets the number of levels of a sample of an integer pixel format: 256 or 65,536.</summary>
    public static int GetLevelCount(PixelFormat format) => format is PixelFormat.Rgba64 or PixelFormat.Gray16 or PixelFormat.La32 or PixelFormat.Rgb48 ? 65536 : 256;

    /// <summary>
    /// Checks that the pixels have discrete levels: floating-point samples have none, and binning them would lose their
    /// precision silently.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    public static void EnsureIntegerSamples(PixelFormat format, string operation)
    {
        if (PixelFormats.IsFloatingPoint(format))
            throw new UnsupportedImageFeatureException(
                $"Histograms count the levels of integer samples, but {format} samples are floating-point values: binning them would lose their precision. Convert the image to Rgba64 with Image.CloneAs first to {operation}.",
                ImageFormat.Unknown,
                "Histogram of floating-point samples");
    }

    /// <summary>Rents a working row of <paramref name="width"/> pixels of four samples.</summary>
    /// <exception cref="ImageResourceLimitException">The row exceeds the allocation limit.</exception>
    public static PooledBuffer RentRow(AllocationScope scope, int width)
    {
        if ((long)width * 4 * sizeof(ushort) > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        return scope.Rent(width * 4 * sizeof(ushort), AllocationKind.Temporary, clear: false);
    }

    /// <summary>Reads stored pixels as red, green, blue and alpha samples; a gray pixel has its value as its three colors.</summary>
    /// <param name="format">The pixel format.</param>
    /// <param name="pixels">The stored pixels, at least <c>rgba.Length / 4</c> of them.</param>
    /// <param name="rgba">The samples, four per pixel.</param>
    public static void Read(PixelFormat format, ReadOnlySpan<byte> pixels, Span<ushort> rgba)
    {
        var count = rgba.Length / 4;
        switch (format)
        {
            case PixelFormat.Rgba32:
                for (var i = 0; i < 4 * count; i++)
                {
                    rgba[i] = pixels[i];
                }

                break;

            case PixelFormat.Bgra32:
                for (var i = 0; i < 4 * count; i += 4)
                {
                    rgba[i] = pixels[i + 2];
                    rgba[i + 1] = pixels[i + 1];
                    rgba[i + 2] = pixels[i];
                    rgba[i + 3] = pixels[i + 3];
                }

                break;

            case PixelFormat.Rgb24:
                for (var i = 0; i < count; i++)
                {
                    rgba[4 * i] = pixels[3 * i];
                    rgba[(4 * i) + 1] = pixels[(3 * i) + 1];
                    rgba[(4 * i) + 2] = pixels[(3 * i) + 2];
                    rgba[(4 * i) + 3] = byte.MaxValue;
                }

                break;

            case PixelFormat.Gray8:
                for (var i = 0; i < count; i++)
                {
                    var value = pixels[i];
                    rgba[4 * i] = value;
                    rgba[(4 * i) + 1] = value;
                    rgba[(4 * i) + 2] = value;
                    rgba[(4 * i) + 3] = byte.MaxValue;
                }

                break;

            case PixelFormat.Rgba64:
                unsafe { MemoryMarshal.Cast<byte, ushort>(pixels)[..(4 * count)].CopyTo(rgba); }
                break;

            case PixelFormat.Gray16:
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var value = samples[i];
                    rgba[4 * i] = value;
                    rgba[(4 * i) + 1] = value;
                    rgba[(4 * i) + 2] = value;
                    rgba[(4 * i) + 3] = ushort.MaxValue;
                }

                break;

            case PixelFormat.La16:
                for (var i = 0; i < count; i++)
                {
                    var value = pixels[2 * i];
                    rgba[4 * i] = value;
                    rgba[(4 * i) + 1] = value;
                    rgba[(4 * i) + 2] = value;
                    rgba[(4 * i) + 3] = pixels[(2 * i) + 1];
                }

                break;

            case PixelFormat.La32:
                var levels = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var value = levels[2 * i];
                    rgba[4 * i] = value;
                    rgba[(4 * i) + 1] = value;
                    rgba[(4 * i) + 2] = value;
                    rgba[(4 * i) + 3] = levels[(2 * i) + 1];
                }

                break;

            case PixelFormat.Rgb48:
                var rgb = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < count; i++)
                {
                    rgba[4 * i] = rgb[3 * i];
                    rgba[(4 * i) + 1] = rgb[(3 * i) + 1];
                    rgba[(4 * i) + 2] = rgb[(3 * i) + 2];
                    rgba[(4 * i) + 3] = ushort.MaxValue;
                }

                break;

            case PixelFormat.A8:
                // No color: black with the alpha
                for (var i = 0; i < count; i++)
                {
                    rgba[4 * i] = 0;
                    rgba[(4 * i) + 1] = 0;
                    rgba[(4 * i) + 2] = 0;
                    rgba[(4 * i) + 3] = pixels[i];
                }

                break;

            default:
                throw new NotSupportedException($"The pixel format '{format}' has no integer samples.");
        }
    }

    /// <summary>
    /// Writes the red, green and blue samples of <see cref="Read"/> back to stored pixels, whose alpha is left as it is; a
    /// gray pixel gets the red sample, which the tone operations keep equal to the other two, and an <see cref="PixelFormat.A8"/>
    /// pixel, which has no color, is unchanged.
    /// </summary>
    /// <param name="format">The pixel format.</param>
    /// <param name="rgba">The samples, four per pixel.</param>
    /// <param name="pixels">The stored pixels, at least <c>rgba.Length / 4</c> of them.</param>
    public static void WriteColors(PixelFormat format, ReadOnlySpan<ushort> rgba, Span<byte> pixels)
    {
        var count = rgba.Length / 4;
        switch (format)
        {
            case PixelFormat.Rgba32:
                for (var i = 0; i < 4 * count; i += 4)
                {
                    pixels[i] = (byte)rgba[i];
                    pixels[i + 1] = (byte)rgba[i + 1];
                    pixels[i + 2] = (byte)rgba[i + 2];
                }

                break;

            case PixelFormat.Bgra32:
                for (var i = 0; i < 4 * count; i += 4)
                {
                    pixels[i + 2] = (byte)rgba[i];
                    pixels[i + 1] = (byte)rgba[i + 1];
                    pixels[i] = (byte)rgba[i + 2];
                }

                break;

            case PixelFormat.Rgb24:
                for (var i = 0; i < count; i++)
                {
                    pixels[3 * i] = (byte)rgba[4 * i];
                    pixels[(3 * i) + 1] = (byte)rgba[(4 * i) + 1];
                    pixels[(3 * i) + 2] = (byte)rgba[(4 * i) + 2];
                }

                break;

            case PixelFormat.Gray8:
                for (var i = 0; i < count; i++)
                {
                    pixels[i] = (byte)rgba[4 * i];
                }

                break;

            case PixelFormat.Rgba64:
                var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < 4 * count; i += 4)
                {
                    colors[i] = rgba[i];
                    colors[i + 1] = rgba[i + 1];
                    colors[i + 2] = rgba[i + 2];
                }

                break;

            case PixelFormat.Gray16:
                var values = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < count; i++)
                {
                    values[i] = rgba[4 * i];
                }

                break;

            case PixelFormat.La16:
                for (var i = 0; i < count; i++)
                {
                    pixels[2 * i] = (byte)rgba[4 * i];
                }

                break;

            case PixelFormat.La32:
                var levels = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < count; i++)
                {
                    levels[2 * i] = rgba[4 * i];
                }

                break;

            case PixelFormat.Rgb48:
                var rgb = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < count; i++)
                {
                    rgb[3 * i] = rgba[4 * i];
                    rgb[(3 * i) + 1] = rgba[(4 * i) + 1];
                    rgb[(3 * i) + 2] = rgba[(4 * i) + 2];
                }

                break;

            case PixelFormat.A8:
                // No color to write: the tone operations leave an alpha mask unchanged
                break;

            default:
                throw new NotSupportedException($"The pixel format '{format}' has no integer samples.");
        }
    }
}
