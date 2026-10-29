using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The samples of the histogram-based tone operations: rows of stored pixels read as red, green, blue and alpha samples at
/// the storage precision (0 to 255 for 8-bit formats, 0 to 65,535 for 16-bit ones), and their colors written back.
/// </summary>
/// <remarks>
/// The packed formats have the precision of the format their kernels work in (<see cref="ProcessingBridge.GetKernelFormat"/>):
/// <see cref="PixelFormat.Bgr565"/> is read as 8-bit samples and <see cref="PixelFormat.Rgba1010102"/> as 16-bit ones, the
/// nearest of each component, and the colors are written back to the nearest component, which gives back every sample read.
/// </remarks>
internal static class ToneSamples
{
    /// <summary>Gets the number of levels of a sample of an integer pixel format: 256 or 65,536.</summary>
    public static int GetLevelCount(PixelFormat format) => format is PixelFormat.Rgba64 or PixelFormat.Gray16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.Rgba1010102 ? 65536 : 256;

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

    /// <summary>The number of 16-bit values of a working row per pixel: four samples (<see cref="GetSamples"/>) and a luma (<see cref="GetLuma"/>).</summary>
    private const int RowValuesPerPixel = 5;

    /// <summary>Rents a working row of <paramref name="width"/> pixels of four samples and a luma.</summary>
    /// <exception cref="ImageResourceLimitException">The row exceeds the allocation limit.</exception>
    public static PooledBuffer RentRow(AllocationScope scope, int width)
    {
        if ((long)width * RowValuesPerPixel * sizeof(ushort) > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        return scope.Rent(width * RowValuesPerPixel * sizeof(ushort), AllocationKind.Temporary, clear: false);
    }

    /// <summary>Gets the four samples per pixel of a row rented by <see cref="RentRow"/>.</summary>
    public static Span<ushort> GetSamples(PooledBuffer row, int width) => unsafe(MemoryMarshal.Cast<byte, ushort>(row.Span))[..(4 * width)];

    /// <summary>Gets the luma per pixel of a row rented by <see cref="RentRow"/>.</summary>
    public static Span<ushort> GetLuma(PooledBuffer row, int width) => unsafe(MemoryMarshal.Cast<byte, ushort>(row.Span)).Slice(4 * width, width);

    /// <summary>The number of partial counters of <see cref="CountBytes"/>: four histograms of 512 counters.</summary>
    public const int PartialCountLength = 4 * 512;

    /// <summary>
    /// Counts levels from 0 to 511 in four partial histograms of 512 counters used in turn, so that consecutive equal
    /// levels do not wait for each other; <see cref="FlushPartialCounts"/> adds up the counters of the levels 0 to 255, the
    /// others count the values set aside by <see cref="MarkTransparent"/>.
    /// </summary>
    /// <param name="values">The values; every <paramref name="stride"/>-th one is counted, from the first.</param>
    /// <param name="stride">The distance between two counted values.</param>
    /// <param name="partial">The <see cref="PartialCountLength"/> partial counters.</param>
    public static void CountBytes(ReadOnlySpan<ushort> values, int stride, Span<uint> partial)
    {
        var first = partial[..512];
        var second = partial.Slice(512, 512);
        var third = partial.Slice(1024, 512);
        var fourth = partial.Slice(1536, 512);
        var i = 0;
        for (; i + (3 * stride) < values.Length; i += 4 * stride)
        {
            first[values[i] & 511]++;
            second[values[i + stride] & 511]++;
            third[values[i + (2 * stride)] & 511]++;
            fourth[values[i + (3 * stride)] & 511]++;
        }

        for (; i < values.Length; i += stride)
        {
            first[values[i] & 511]++;
        }
    }

    /// <summary>
    /// Adds 256 to the values of the fully transparent pixels (alpha 0) of 8-bit samples, so that <see cref="CountBytes"/>
    /// sets them aside: the luma of each pixel, or its three colors.
    /// </summary>
    /// <param name="rgba">The samples, four per pixel.</param>
    /// <param name="luma">The luma of each pixel, or an empty span to mark the colors of <paramref name="rgba"/>.</param>
    public static void MarkTransparent(Span<ushort> rgba, Span<ushort> luma)
    {
        var count = rgba.Length / 4;
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar)
        {
            ref var samples = ref unsafe(Unsafe.As<ushort, ulong>(ref MemoryMarshal.GetReference(rgba)));
            var mark = Vector128.Create((ushort)256);
            if (luma.IsEmpty)
            {
                var colors = Vector128.Create(0x0000_0100_0100_0100ul);
                for (; x + 2 <= count; x += 2)
                {
                    var pixels = unsafe(Vector128.LoadUnsafe(ref samples, (nuint)x));
                    var transparent = Vector128.Equals(Vector128.ShiftRightLogical(pixels, 48), Vector128<ulong>.Zero);
                    unsafe { (pixels | (transparent & colors)).StoreUnsafe(ref samples, (nuint)x); }
                }
            }
            else
            {
                ref var levels = ref unsafe(MemoryMarshal.GetReference(luma[..count]));
                for (; x + 8 <= count; x += 8)
                {
                    var alpha = Vector128.Narrow(
                        Vector128.Narrow(Vector128.ShiftRightLogical(unsafe(Vector128.LoadUnsafe(ref samples, (nuint)x)), 48), Vector128.ShiftRightLogical(unsafe(Vector128.LoadUnsafe(ref samples, (nuint)(x + 2))), 48)),
                        Vector128.Narrow(Vector128.ShiftRightLogical(unsafe(Vector128.LoadUnsafe(ref samples, (nuint)(x + 4))), 48), Vector128.ShiftRightLogical(unsafe(Vector128.LoadUnsafe(ref samples, (nuint)(x + 6))), 48)));
                    var transparent = Vector128.Equals(alpha, Vector128<ushort>.Zero);
                    unsafe { (Vector128.LoadUnsafe(ref levels, (nuint)x) | (transparent & mark)).StoreUnsafe(ref levels, (nuint)x); }
                }
            }
        }

        MarkTransparentScalar(rgba[(4 * x)..], luma.IsEmpty ? luma : luma[x..count]);
    }

    /// <summary>
    /// Adds 256 to the luma of the fully transparent pixels (alpha 0) of <see cref="PixelFormat.Rgba32"/> or
    /// <see cref="PixelFormat.Bgra32"/> pixels, read from the stored bytes, like <see cref="MarkTransparent"/>.
    /// </summary>
    /// <param name="pixels">The stored pixels, at least <c>luma.Length</c> of them.</param>
    /// <param name="luma">The luma of each pixel.</param>
    public static void MarkTransparent32(ReadOnlySpan<byte> pixels, Span<ushort> luma)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar && pixels.Length >= 4 * luma.Length)
        {
            ref var input = ref unsafe(Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(pixels)));
            ref var levels = ref unsafe(MemoryMarshal.GetReference(luma));
            var mark = Vector128.Create((ushort)256);
            for (; x + 8 <= luma.Length; x += 8)
            {
                var alpha = Vector128.Narrow(Vector128.ShiftRightLogical(unsafe(Vector128.LoadUnsafe(ref input, (nuint)x)), 24), Vector128.ShiftRightLogical(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(x + 4))), 24));
                unsafe { (Vector128.LoadUnsafe(ref levels, (nuint)x) | (Vector128.Equals(alpha, Vector128<ushort>.Zero) & mark)).StoreUnsafe(ref levels, (nuint)x); }
            }
        }

        for (; x < luma.Length; x++)
        {
            if (pixels[(4 * x) + 3] == 0)
            {
                luma[x] |= 256;
            }
        }
    }

    /// <summary>The scalar reference of <see cref="MarkTransparent"/>.</summary>
    internal static void MarkTransparentScalar(Span<ushort> rgba, Span<ushort> luma)
    {
        var count = rgba.Length / 4;
        for (var x = 0; x < count; x++)
        {
            if (rgba[(4 * x) + 3] != 0)
                continue;

            if (luma.IsEmpty)
            {
                rgba[4 * x] |= 256;
                rgba[(4 * x) + 1] |= 256;
                rgba[(4 * x) + 2] |= 256;
            }
            else
            {
                luma[x] |= 256;
            }
        }
    }

    /// <summary>
    /// Adds the partial counters of the levels 0 to 255 of <see cref="CountBytes"/> to a histogram of 256 levels and clears
    /// every counter. The count of a level must stay below 2^32.
    /// </summary>
    public static void FlushPartialCounts(Span<uint> partial, Span<long> counts)
    {
        for (var level = 0; level < 256; level++)
        {
            counts[level] += unchecked(partial[level] + partial[512 + level] + partial[1024 + level] + partial[1536 + level]);
        }

        partial.Clear();
    }

    /// <summary>Reads stored pixels as red, green, blue and alpha samples; a gray pixel has its value as its three colors.</summary>
    /// <param name="format">The pixel format.</param>
    /// <param name="pixels">The stored pixels, at least <c>rgba.Length / 4</c> of them.</param>
    /// <param name="rgba">The samples, four per pixel.</param>
    public static void Read(PixelFormat format, ReadOnlySpan<byte> pixels, Span<ushort> rgba)
    {
        var done = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar ? ReadVector(format, pixels, rgba) : 0;
        if (done > 0)
        {
            pixels = pixels[(done * PixelFormats.GetBytesPerPixel(format))..];
            rgba = rgba[(4 * done)..];
        }

        ReadScalar(format, pixels, rgba);
    }

    /// <summary>The scalar reference of <see cref="Read"/>.</summary>
    internal static void ReadScalar(PixelFormat format, ReadOnlySpan<byte> pixels, Span<ushort> rgba)
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

            case PixelFormat.Argb32:
                for (var i = 0; i < 4 * count; i += 4)
                {
                    rgba[i] = pixels[i + 1];
                    rgba[i + 1] = pixels[i + 2];
                    rgba[i + 2] = pixels[i + 3];
                    rgba[i + 3] = pixels[i];
                }

                break;

            case PixelFormat.Abgr32:
                for (var i = 0; i < 4 * count; i += 4)
                {
                    rgba[i] = pixels[i + 3];
                    rgba[i + 1] = pixels[i + 2];
                    rgba[i + 2] = pixels[i + 1];
                    rgba[i + 3] = pixels[i];
                }

                break;

            case PixelFormat.Bgr565:
                var packed16 = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < count; i++)
                {
                    uint value = packed16[i];
                    rgba[4 * i] = (ushort)PixelConverter.ExpandTo8(value >> 11, 31);
                    rgba[(4 * i) + 1] = (ushort)PixelConverter.ExpandTo8((value >> 5) & 63, 63);
                    rgba[(4 * i) + 2] = (ushort)PixelConverter.ExpandTo8(value & 31, 31);
                    rgba[(4 * i) + 3] = byte.MaxValue;
                }

                break;

            case PixelFormat.Rgba1010102:
                var packed32 = unsafe(MemoryMarshal.Cast<byte, uint>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var value = packed32[i];
                    rgba[4 * i] = (ushort)PixelConverter.Expand(value & 1023, 1023);
                    rgba[(4 * i) + 1] = (ushort)PixelConverter.Expand((value >> 10) & 1023, 1023);
                    rgba[(4 * i) + 2] = (ushort)PixelConverter.Expand((value >> 20) & 1023, 1023);
                    rgba[(4 * i) + 3] = (ushort)PixelConverter.Expand(value >> 30, 3);
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
        var done = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar ? WriteColorsVector(format, rgba, pixels) : 0;
        if (done > 0)
        {
            rgba = rgba[(4 * done)..];
            pixels = pixels[(done * PixelFormats.GetBytesPerPixel(format))..];
        }

        WriteColorsScalar(format, rgba, pixels);
    }

    /// <summary>The scalar reference of <see cref="WriteColors"/>.</summary>
    internal static void WriteColorsScalar(PixelFormat format, ReadOnlySpan<ushort> rgba, Span<byte> pixels)
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

            case PixelFormat.Argb32:
                for (var i = 0; i < 4 * count; i += 4)
                {
                    pixels[i + 1] = (byte)rgba[i];
                    pixels[i + 2] = (byte)rgba[i + 1];
                    pixels[i + 3] = (byte)rgba[i + 2];
                }

                break;

            case PixelFormat.Abgr32:
                for (var i = 0; i < 4 * count; i += 4)
                {
                    pixels[i + 3] = (byte)rgba[i];
                    pixels[i + 2] = (byte)rgba[i + 1];
                    pixels[i + 1] = (byte)rgba[i + 2];
                }

                break;

            case PixelFormat.Bgr565:
                var packed16 = unsafe(MemoryMarshal.Cast<byte, ushort>(pixels));
                for (var i = 0; i < count; i++)
                {
                    packed16[i] = (ushort)((PixelConverter.Reduce(rgba[4 * i], 31, byte.MaxValue) << 11) | (PixelConverter.Reduce(rgba[(4 * i) + 1], 63, byte.MaxValue) << 5) | PixelConverter.Reduce(rgba[(4 * i) + 2], 31, byte.MaxValue));
                }

                break;

            case PixelFormat.Rgba1010102:
                var packed32 = unsafe(MemoryMarshal.Cast<byte, uint>(pixels));
                for (var i = 0; i < count; i++)
                {
                    // The alpha bits are kept
                    packed32[i] = (packed32[i] & 0xC0000000u) | PixelConverter.Reduce(rgba[4 * i], 1023, ushort.MaxValue) | (PixelConverter.Reduce(rgba[(4 * i) + 1], 1023, ushort.MaxValue) << 10) | (PixelConverter.Reduce(rgba[(4 * i) + 2], 1023, ushort.MaxValue) << 20);
                }

                break;

            default:
                throw new NotSupportedException($"The pixel format '{format}' has no integer samples.");
        }
    }

    /// <summary>
    /// Computes the Rec. 709 luma of samples read by <see cref="Read"/>, with the result of
    /// <see cref="PixelConverter.Luma(uint, uint, uint, uint)"/>: samples never exceed the largest level, so neither does their luma.
    /// </summary>
    /// <param name="rgba">The samples, four per pixel.</param>
    /// <param name="luma">The luma of each pixel, at least <c>rgba.Length / 4</c> of them.</param>
    public static void ComputeLuma(ReadOnlySpan<ushort> rgba, Span<ushort> luma)
    {
        var count = rgba.Length / 4;
        var i = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar)
        {
            ref var source = ref unsafe(MemoryMarshal.GetReference(rgba));
            ref var destination = ref unsafe(MemoryMarshal.GetReference(luma[..count]));
            for (; i + 8 <= count; i += 8)
            {
                var low = PixelConverter.Luma16Vector(unsafe(Vector128.LoadUnsafe(ref source, (nuint)(4 * i))), unsafe(Vector128.LoadUnsafe(ref source, (nuint)((4 * i) + 8))));
                var high = PixelConverter.Luma16Vector(unsafe(Vector128.LoadUnsafe(ref source, (nuint)((4 * i) + 16))), unsafe(Vector128.LoadUnsafe(ref source, (nuint)((4 * i) + 24))));
                unsafe { Vector128.Narrow(low, high).StoreUnsafe(ref destination, (nuint)i); }
            }
        }

        ComputeLumaScalar(rgba[(4 * i)..], luma[i..count]);
    }

    /// <summary>
    /// Computes the luma of stored pixels, with the result of <see cref="Read"/> followed by <see cref="ComputeLuma"/>; the
    /// samples are only written for the pixels whose luma is not computed from the stored bytes directly.
    /// </summary>
    /// <param name="format">The pixel format.</param>
    /// <param name="pixels">The stored pixels, at least <c>luma.Length</c> of them.</param>
    /// <param name="samples">The working samples, four per pixel of <paramref name="luma"/>.</param>
    /// <param name="luma">The luma of each pixel.</param>
    public static void ReadLuma(PixelFormat format, ReadOnlySpan<byte> pixels, Span<ushort> samples, Span<ushort> luma)
    {
        var done = 0;
        if ((format is PixelFormat.Rgba32 or PixelFormat.Bgra32) && Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar && pixels.Length >= 4 * luma.Length)
        {
            done = ComputeLuma32Vector(pixels, luma, format == PixelFormat.Bgra32);
        }
        else if (format == PixelFormat.Rgb24 && Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar && pixels.Length >= 3 * luma.Length)
        {
            done = ComputeLuma24Vector(pixels, luma);
        }

        if (done < luma.Length)
        {
            var rest = samples[..(4 * (luma.Length - done))];
            Read(format, pixels[(done * PixelFormats.GetBytesPerPixel(format))..], rest);
            ComputeLuma(rest, luma[done..]);
        }
    }

    /// <summary>
    /// Adds a change of luma to the three colors of <see cref="PixelFormat.Rgba32"/> or <see cref="PixelFormat.Bgra32"/>
    /// pixels, clamped from 0 to 255, as a saturated addition followed by a saturated subtraction: the change of a pixel is
    /// <c>increases[x] - decreases[x]</c>, given as the same byte in its three colors and 0 in its alpha, one of them 0.
    /// </summary>
    /// <param name="pixels">The stored pixels, changed in place.</param>
    /// <param name="increases">The increase of each pixel.</param>
    /// <param name="decreases">The decrease of each pixel.</param>
    public static void ShiftColors32(Span<byte> pixels, ReadOnlySpan<uint> increases, ReadOnlySpan<uint> decreases)
    {
        var count = increases.Length;
        var x = 0;
        if (Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar && pixels.Length >= 4 * count && decreases.Length >= count)
        {
            ref var output = ref unsafe(MemoryMarshal.GetReference(pixels));
            ref var up = ref unsafe(Unsafe.As<uint, byte>(ref MemoryMarshal.GetReference(increases)));
            ref var down = ref unsafe(Unsafe.As<uint, byte>(ref MemoryMarshal.GetReference(decreases)));
            for (; x + 4 <= count; x += 4)
            {
                var offset = (nuint)(4 * x);
                var shifted = Vector128.SubtractSaturate(Vector128.AddSaturate(unsafe(Vector128.LoadUnsafe(ref output, offset)), unsafe(Vector128.LoadUnsafe(ref up, offset))), unsafe(Vector128.LoadUnsafe(ref down, offset)));
                unsafe { shifted.StoreUnsafe(ref output, offset); }
            }
        }

        ShiftColors32Scalar(pixels[(4 * x)..], increases[x..], decreases[x..count]);
    }

    /// <summary>The scalar reference of <see cref="ShiftColors32"/>.</summary>
    internal static void ShiftColors32Scalar(Span<byte> pixels, ReadOnlySpan<uint> increases, ReadOnlySpan<uint> decreases)
    {
        for (var x = 0; x < increases.Length; x++)
        {
            for (var b = 0; b < 4; b++)
            {
                var value = pixels[(4 * x) + b] + (int)((increases[x] >> (8 * b)) & 0xFF) - (int)((decreases[x] >> (8 * b)) & 0xFF);
                pixels[(4 * x) + b] = (byte)Math.Clamp(value, 0, byte.MaxValue);
            }
        }
    }

    /// <summary>Computes the luma of the first <see cref="PixelFormat.Rgba32"/> or <see cref="PixelFormat.Bgra32"/> pixels of a row with vectors; returns the number of pixels done.</summary>
    private static int ComputeLuma32Vector(ReadOnlySpan<byte> pixels, Span<ushort> luma, bool bgr)
    {
        ref var input = ref unsafe(MemoryMarshal.GetReference(pixels));
        ref var output = ref unsafe(MemoryMarshal.GetReference(luma));
        var x = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            // x64: 8 pixels per vector
            for (; x + 16 <= luma.Length; x += 16)
            {
                var low = PixelConverter.Luma8Vector(unsafe(Vector256.LoadUnsafe(ref input, (nuint)(4 * x))), bgr);
                var high = PixelConverter.Luma8Vector(unsafe(Vector256.LoadUnsafe(ref input, (nuint)((4 * x) + 32))), bgr);
                unsafe { Vector256.Narrow(low, high).AsUInt16().StoreUnsafe(ref output, (nuint)x); }
            }
        }

        for (; x + 8 <= luma.Length; x += 8)
        {
            var low = PixelConverter.Luma8Vector(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * x))), bgr);
            var high = PixelConverter.Luma8Vector(unsafe(Vector128.LoadUnsafe(ref input, (nuint)((4 * x) + 16))), bgr);
            unsafe { Vector128.Narrow(low, high).AsUInt16().StoreUnsafe(ref output, (nuint)x); }
        }

        return x;
    }

    /// <summary>Computes the luma of the first <see cref="PixelFormat.Rgb24"/> pixels of a row with vectors; returns the number of pixels done.</summary>
    private static int ComputeLuma24Vector(ReadOnlySpan<byte> pixels, Span<ushort> luma)
    {
        // 4 pixels of 3 bytes in each 16 bytes read; the fourth byte of each lane is not part of the luma
        var expand = Vector128.Create((byte)0, 1, 2, 2, 3, 4, 5, 5, 6, 7, 8, 8, 9, 10, 11, 11);
        var x = 0;
        for (; x + 10 <= luma.Length; x += 8)
        {
            var low = PixelConverter.Luma8Vector(Vector128.Shuffle(Vector128.Create(pixels.Slice(3 * x, 16)), expand), bgr: false);
            var high = PixelConverter.Luma8Vector(Vector128.Shuffle(Vector128.Create(pixels.Slice((3 * x) + 12, 16)), expand), bgr: false);
            Vector128.Narrow(low, high).AsUInt16().CopyTo(luma[x..]);
        }

        return x;
    }

    /// <summary>The scalar reference of <see cref="ComputeLuma"/>.</summary>
    internal static void ComputeLumaScalar(ReadOnlySpan<ushort> rgba, Span<ushort> luma)
    {
        var count = rgba.Length / 4;
        for (var i = 0; i < count; i++)
        {
            luma[i] = (ushort)PixelConverter.Luma(rgba[4 * i], rgba[(4 * i) + 1], rgba[(4 * i) + 2], ushort.MaxValue);
        }
    }

    /// <summary>Reads the first pixels of a row of an 8-bit format with vectors; returns the number of pixels read.</summary>
    private static int ReadVector(PixelFormat format, ReadOnlySpan<byte> pixels, Span<ushort> rgba)
    {
        var count = rgba.Length / 4;
        ref var input = ref unsafe(MemoryMarshal.GetReference(pixels));
        ref var output = ref unsafe(MemoryMarshal.GetReference(rgba));
        var i = 0;
        switch (format)
        {
            case PixelFormat.Rgba32:
                if (pixels.Length < 4 * count)
                    return 0;

                for (; i + 4 <= count; i += 4)
                {
                    var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * i))));
                    unsafe { low.StoreUnsafe(ref output, (nuint)(4 * i)); }
                    unsafe { high.StoreUnsafe(ref output, (nuint)((4 * i) + 8)); }
                }

                break;

            case PixelFormat.Bgra32:
                if (pixels.Length < 4 * count)
                    return 0;

                var swap = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
                for (; i + 4 <= count; i += 4)
                {
                    var (low, high) = Vector128.Widen(Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * i))), swap));
                    unsafe { low.StoreUnsafe(ref output, (nuint)(4 * i)); }
                    unsafe { high.StoreUnsafe(ref output, (nuint)((4 * i) + 8)); }
                }

                break;

            case PixelFormat.Rgb24:
            {
                if (pixels.Length < 3 * count)
                    return 0;

                // 4 pixels of 3 bytes in 16 bytes read (the last 4 bytes belong to the next pixels); the alpha bytes are set by the
                // mask (constant indices in range keep the shuffle a single instruction)
                var expand = Vector128.Create((byte)0, 1, 2, 2, 3, 4, 5, 5, 6, 7, 8, 8, 9, 10, 11, 11);
                var opaque = Vector128.Create(0xFF000000u).AsByte();
                for (; i + 6 <= count; i += 4)
                {
                    var (low, high) = Vector128.Widen(Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(3 * i))), expand) | opaque);
                    unsafe { low.StoreUnsafe(ref output, (nuint)(4 * i)); }
                    unsafe { high.StoreUnsafe(ref output, (nuint)((4 * i) + 8)); }
                }

                break;
            }

            case PixelFormat.Gray8:
            {
                if (pixels.Length < count)
                    return 0;

                // Each value is repeated four times, the alpha bytes are then set by the mask
                var opaque = Vector128.Create(0xFF000000u).AsByte();
                var part0 = Vector128.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3);
                var part1 = Vector128.Create((byte)4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7);
                var part2 = Vector128.Create((byte)8, 8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 11, 11, 11, 11);
                var part3 = Vector128.Create((byte)12, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 14, 15, 15, 15, 15);
                for (; i + 16 <= count; i += 16)
                {
                    var values = unsafe(Vector128.LoadUnsafe(ref input, (nuint)i));
                    StoreWidened(Vector128.Shuffle(values, part0) | opaque, ref output, 4 * i);
                    StoreWidened(Vector128.Shuffle(values, part1) | opaque, ref output, (4 * i) + 16);
                    StoreWidened(Vector128.Shuffle(values, part2) | opaque, ref output, (4 * i) + 32);
                    StoreWidened(Vector128.Shuffle(values, part3) | opaque, ref output, (4 * i) + 48);
                }

                break;
            }
        }

        return i;
    }

    /// <summary>Stores 16 bytes as 16 samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreWidened(Vector128<byte> bytes, ref ushort output, int index)
    {
        var (low, high) = Vector128.Widen(bytes);
        unsafe { low.StoreUnsafe(ref output, (nuint)index); }
        unsafe { high.StoreUnsafe(ref output, (nuint)(index + 8)); }
    }

    /// <summary>Writes the colors of the first pixels of a row of an 8-bit format with 4 samples with vectors; returns the number of pixels written.</summary>
    private static int WriteColorsVector(PixelFormat format, ReadOnlySpan<ushort> rgba, Span<byte> pixels)
    {
        if (format is not (PixelFormat.Rgba32 or PixelFormat.Bgra32))
            return 0;

        var count = rgba.Length / 4;
        if (pixels.Length < 4 * count)
            return 0;

        ref var input = ref unsafe(MemoryMarshal.GetReference(rgba));
        ref var output = ref unsafe(MemoryMarshal.GetReference(pixels));
        var alpha = Vector128.Create(0xFF000000u).AsByte();
        var swap = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
        var i = 0;
        for (; i + 4 <= count; i += 4)
        {
            // The samples of the colors are at most 255; the alpha of the stored pixels is kept
            var colors = Vector128.Narrow(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * i))), unsafe(Vector128.LoadUnsafe(ref input, (nuint)((4 * i) + 8))));
            if (format == PixelFormat.Bgra32)
            {
                colors = Vector128.Shuffle(colors, swap);
            }

            var stored = unsafe(Vector128.LoadUnsafe(ref output, (nuint)(4 * i)));
            unsafe { Vector128.ConditionalSelect(alpha, stored, colors).StoreUnsafe(ref output, (nuint)(4 * i)); }
        }

        return i;
    }
}
