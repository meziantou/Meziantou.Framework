using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Composites the glyphs of a text drawn with subpixel rendering: a coverage for each color of a pixel.</summary>
/// <remarks>
/// <para>
/// The coverage of the thirds of the pixels of a row is filtered with the default LCD filter of FreeType (the weights 8,
/// 77, 86, 77 and 8 over 256, <c>FT_LCD_FILTER_DEFAULT</c>): the coverage of a color of a pixel is the weighted sum of the
/// coverage of its third and of the two thirds on each side of it.
/// </para>
/// <para>
/// A color sample of an opaque pixel becomes <c>s w + d (1 - w)</c>, with <c>s</c> the sample of the paint, <c>d</c>
/// the one of the pixel and <c>w</c> the alpha of the paint times the coverage of the color, rounded to nearest, and
/// its alpha does not change. A pixel that is not opaque is composited with the mean of the three coverages, with the
/// source-over operator of straight alpha: its colors stay those of a pixel that is covered alike.
/// </para>
/// </remarks>
internal static class SubpixelTextKernels
{
    /// <summary>The weights of the filter, which add up to 256.</summary>
    private static ReadOnlySpan<byte> FilterWeights => [8, 77, 86, 77, 8];

    /// <summary>The number of thirds of a pixel the filter reads on each side of a third.</summary>
    public const int FilterReach = 2;

    /// <summary>Determines whether the pixels of a format can be composited a color at a time.</summary>
    /// <param name="format">The format.</param>
    /// <param name="isBlueFirst">Whether the first color sample of a pixel is blue.</param>
    public static bool IsSupported(PixelFormat format, out bool isBlueFirst)
    {
        isBlueFirst = format == PixelFormat.Bgra32;
        return format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Rgba64 or PixelFormat.Rgb48;
    }

    /// <summary>Filters the coverage of the thirds of the pixels of a row into the coverage of the colors of pixels.</summary>
    /// <param name="coverage">The coverage of the thirds of a run, from 0 to <see cref="CoverageRasterizer.FullCoverage"/>; the thirds around them have none.</param>
    /// <param name="start">The index of the first third of <paramref name="coverage"/>.</param>
    /// <param name="firstPixel">The first pixel to compute.</param>
    /// <param name="destination">The coverage of the three colors of the pixels, from left to right, from 0 to <see cref="CoverageRasterizer.FullCoverage"/>: three values per pixel.</param>
    public static void Filter(ReadOnlySpan<uint> coverage, int start, int firstPixel, Span<uint> destination)
    {
        var weights = FilterWeights;
        var first = firstPixel * 3;
        for (var i = 0; i < destination.Length; i++)
        {
            var center = first + i;
            ulong sum = 0;
            for (var k = 0; k < weights.Length; k++)
            {
                var index = center + k - FilterReach - start;
                if ((uint)index < (uint)coverage.Length)
                {
                    sum += (ulong)weights[k] * coverage[index];
                }
            }

            destination[i] = (uint)((sum + 128) >> 8);
        }
    }

    /// <summary>Composites a color over the pixels of a run of a row.</summary>
    /// <param name="row">The samples of the row.</param>
    /// <param name="format">The format of the pixels: one <see cref="IsSupported"/> accepts.</param>
    /// <param name="x">The first pixel of the run.</param>
    /// <param name="coverage">The coverage of the three color samples of each pixel, in the order they are stored.</param>
    /// <param name="color">The paint, in the order the samples are stored, at the precision of the format, with the opacity of the paint.</param>
    public static void Composite(Span<byte> row, PixelFormat format, int x, ReadOnlySpan<uint> coverage, DrawingColor color)
    {
        var count = coverage.Length / 3;
        switch (format)
        {
            case PixelFormat.Rgba32 or PixelFormat.Bgra32:
                Composite(row.Slice(x * 4, count * 4), channels: 4, byte.MaxValue, coverage, color);
                break;

            case PixelFormat.Rgb24:
                Composite(row.Slice(x * 3, count * 3), channels: 3, byte.MaxValue, coverage, color);
                break;

            case PixelFormat.Rgba64:
                Composite(unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 4, count * 4), channels: 4, ushort.MaxValue, coverage, color);
                break;

            case PixelFormat.Rgb48:
                Composite(unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 3, count * 3), channels: 3, ushort.MaxValue, coverage, color);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not composited a color at a time.");
        }
    }

    private static void Composite<T>(Span<T> samples, int channels, uint max, ReadOnlySpan<uint> coverage, DrawingColor color)
        where T : unmanaged
    {
        var source0 = (ulong)color.C0;
        var source1 = (ulong)color.C1;
        var source2 = (ulong)color.C2;
        var alpha = (ulong)color.Alpha;
        for (var i = 0; i < coverage.Length / 3; i++)
        {
            var pixel = samples.Slice(i * channels, channels);
            var coverage0 = coverage[i * 3];
            var coverage1 = coverage[(i * 3) + 1];
            var coverage2 = coverage[(i * 3) + 2];
            if ((coverage0 | coverage1 | coverage2) == 0)
                continue;

            var pixelAlpha = channels == 4 ? Read(pixel[3]) : max;
            if (pixelAlpha == max)
            {
                pixel[0] = Blend(pixel[0], source0, coverage0, alpha, max);
                pixel[1] = Blend(pixel[1], source1, coverage1, alpha, max);
                pixel[2] = Blend(pixel[2], source2, coverage2, alpha, max);
                continue;
            }

            // Source over in straight alpha, with the mean coverage
            var paintAlpha = (coverage0 + coverage1 + coverage2) / (3.0 * CoverageRasterizer.FullCoverage) * alpha / max;
            var destinationAlpha = (double)pixelAlpha / max;
            var resultAlpha = paintAlpha + (destinationAlpha * (1 - paintAlpha));
            if (resultAlpha <= 0)
                continue;

            pixel[0] = Over(pixel[0], source0, paintAlpha, destinationAlpha, resultAlpha);
            pixel[1] = Over(pixel[1], source1, paintAlpha, destinationAlpha, resultAlpha);
            pixel[2] = Over(pixel[2], source2, paintAlpha, destinationAlpha, resultAlpha);
            pixel[3] = Write<T>(Math.Min(max, (uint)Math.Round(resultAlpha * max, MidpointRounding.AwayFromZero)));
        }
    }

    private static T Blend<T>(T destination, ulong source, uint coverage, ulong alpha, uint max)
        where T : unmanaged
    {
        // The weight of the paint, from 0 to 65536
        var weight = coverage * alpha / max;
        var value = ((source * weight) + ((ulong)Read(destination) * (CoverageRasterizer.FullCoverage - weight)) + (CoverageRasterizer.FullCoverage / 2)) >> 16;
        return Write<T>((uint)value);
    }

    private static T Over<T>(T destination, ulong source, double paintAlpha, double destinationAlpha, double resultAlpha)
        where T : unmanaged
    {
        var value = ((source * paintAlpha) + (Read(destination) * destinationAlpha * (1 - paintAlpha))) / resultAlpha;
        return Write<T>((uint)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static uint Read<T>(T value)
        where T : unmanaged
        => typeof(T) == typeof(byte) ? unsafe(Unsafe.BitCast<T, byte>(value))
            : typeof(T) == typeof(ushort) ? unsafe(Unsafe.BitCast<T, ushort>(value))
            : UnsupportedType.Throw<T, uint>();

    private static T Write<T>(uint value)
        where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            var sample = (byte)value;
            return unsafe(Unsafe.BitCast<byte, T>(sample));
        }

        if (typeof(T) == typeof(ushort))
        {
            var wide = (ushort)value;
            return unsafe(Unsafe.BitCast<ushort, T>(wide));
        }

        return UnsupportedType.Throw<T, T>();
    }
}
