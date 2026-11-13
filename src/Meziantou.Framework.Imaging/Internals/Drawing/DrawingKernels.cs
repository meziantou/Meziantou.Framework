using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Composites a paint over the pixels of a frame, weighted by the coverage of each pixel.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> Source-over on the stored (encoded) sample values, with straight alpha and exact integer
/// arithmetic: this is <see cref="AnimationCompositor.BlendOver8"/> with a source alpha multiplied by a coverage. With
/// <c>M</c> the largest sample value, <c>Q</c> = <see cref="CoverageRasterizer.FullCoverage"/>, a paint <c>(sc, sa)</c>,
/// a coverage <c>q</c> and a pixel <c>(dc, da)</c>, the effective source alpha is <c>E / (M Q)</c> with <c>E = sa q</c>:
/// </para>
/// <list type="bullet">
/// <item><description><c>E = 0</c>: the pixel is not written.</description></item>
/// <item><description>
/// Otherwise <c>A = E M + da (M Q - E)</c> is the exact output alpha times <c>M^2 Q</c>:
/// <c>a' = round(A / (M Q))</c> and <c>c' = round((sc E M + dc da (M Q - E)) / A)</c>, both to nearest with ties upward.
/// When <c>a'</c> is 0 the pixel is not written (it was transparent and stays so, with its hidden color).
/// </description></item>
/// <item><description>
/// A pixel format without alpha is an opaque destination (<c>da = M</c>), for which the color is
/// <c>round((sc E + dc (M Q - E)) / (M Q))</c> and the pixel stays opaque: the same samples as an opaque pixel of a format
/// with alpha.
/// </description></item>
/// </list>
/// <para>
/// Consequences: a full coverage with an opaque paint writes the paint exactly; a full coverage is exactly the
/// compositing of the animation decoders. Every intermediate value fits in 64 bits: the largest one,
/// <c>M^3 Q + M^2 Q / 2</c> for 16-bit samples, is just below 2^64.
/// </para>
/// <para>
/// <b>Other operators.</b> An operator of <see cref="CompositeMode"/> has two factors, whose numerators over <c>M</c> are
/// <c>fa</c> (0, <c>M</c>, <c>da</c> or <c>M - da</c>) and <c>fb</c> (0, <c>M</c>, <c>sa</c> or <c>M - sa</c>). The pixel is,
/// for the part <c>q / Q</c> the shape covers, the result of the operator, and for the rest what it was. With
/// <c>S = q fa sa</c> and <c>D = (Q - q) da M + q fb da</c>, the exact output alpha times <c>M^2 Q</c> is <c>A = S + D</c>:
/// <c>a' = round(A / (M Q))</c> and <c>c' = round((sc S + dc D) / A)</c>. When <c>a'</c> is 0, the pixel becomes
/// transparent black, or is not written if it was fully transparent. A pixel format without alpha (<c>da = M</c>) gets
/// the color of the result over black, <c>round((sc S + dc D) / (M^2 Q))</c>. For source-over, these are the formulas
/// above; <c>S + D</c> never exceeds <c>M^2 Q</c>, so nothing overflows either.
/// </para>
/// <para>
/// <see cref="CompositeMode.Plus"/> clips the sums: the alpha times <c>M Q</c> is
/// <c>A = q min(sa + da, M) + (Q - q) da</c>, and each color times its alpha, times <c>M^2 Q</c>, is
/// <c>P = q min(sc sa + dc da, M^2) + (Q - q) dc da</c>; <c>a' = round(A / Q)</c> and <c>c' = round(P / A)</c>, or
/// <c>round(P / (M Q))</c> for a pixel format without alpha.
/// </para>
/// <para>
/// <b>Blend modes.</b> With a <see cref="BlendMode"/> other than normal, the color of the paint is replaced, pixel by
/// pixel, before any of the above: with the colors as fractions of one, <c>s' = (1 - da) s + da B(s, d)</c>, where
/// <c>B</c> is the function of the mode (<see cref="BlendFunctions"/>), computed in double precision and rounded to a
/// sample as <c>floor(M s' + 1/2 + 2^-20)</c>. A paint without opacity and a fully transparent pixel are left as they
/// are. The functions that take a color as a whole get red, green and blue, whatever the order of the samples, and a
/// gray level three times.
/// </para>
/// <para>
/// <b>Linear light.</b> When the paint is combined in linear light (<see cref="DrawingSource.Transfer"/>), every
/// color sample of the paint and of the pixel is decoded by the function of those transfer characteristics
/// (<see cref="CicpTransfer"/>; sRGB for an image that declares no function), or by the tone curve of its channel in
/// the ICC profile of the image (<see cref="DrawingSource.Curves"/>), the
/// blend mode and the operator are applied to the decoded colors with the formulas above, in double precision, and
/// the color of the result is encoded and rounded to a sample as <c>floor(M v + 1/2 + 2^-20)</c>. The alpha is not a
/// color: with <c>k = q / Q</c> and the factors as fractions of one, <c>S = k fa sa</c>,
/// <c>D = (1 - k) da + k fb da</c> and <c>A = S + D</c>, it is <c>floor(M A + 1/2)</c>, and each color is
/// <c>encode((sc S + dc D) / A)</c> (<c>encode(sc S + dc D)</c> for a pixel format without alpha). The pixels that are
/// not written are the same as on the stored values, and a full coverage with an opaque paint that replaces the pixel
/// writes the paint exactly.
/// </para>
/// </remarks>
internal static class DrawingKernels
{
    /// <summary>The number of pixels from which a run with a single paint is searched for pixels covered entirely.</summary>
    private const int MinFullRunSearchLength = 16;

    // The thresholds of the encoding of linear light to 8-bit samples, by code point of the transfer characteristics
    private static readonly QuantizationThresholds?[] LinearThresholds8 = new QuantizationThresholds?[19];

    // The functions those thresholds are created from, by code point of the transfer characteristics
    private static readonly Func<double, int>?[] LinearEncoders8 = new Func<double, int>?[19];

    /// <summary>Gets the largest sample of the paints of a frame: 255 for the 8-bit formats, 65535 for the 16-bit and the floating-point formats.</summary>
    /// <param name="format">The pixel format of the frame.</param>
    public static int GetPaintMax(PixelFormat format) => PixelFormats.GetBitsPerComponent(format) > 8 ? ushort.MaxValue : byte.MaxValue;

    /// <summary>Converts a paint color to the pixel format of a frame, with the rules of pixel conversion.</summary>
    /// <remarks>
    /// The color samples are those of the opaque color converted by <see cref="PixelConverter"/> (Rec. 709 luma for
    /// grayscale formats, 16-to-8-bit rounding last); the opacity is the alpha of the color at the precision of the format.
    /// The paints of a floating-point format keep the 16-bit color (<see cref="FloatDrawingKernels"/>).
    /// </remarks>
    public static DrawingColor ConvertColor<TPixel>(Rgba64 color)
        where TPixel : unmanaged
    {
        if (PixelConverter.IsFloat<TPixel>())
            return new DrawingColor(color.R, color.G, color.B, color.A);

        // The formats composited in the format of their kernels (CompositeBridged) get its paints: the result is rounded
        // to their components once, when it is stored
        if (IsBridgedToColorFormat<TPixel>())
        {
            return ProcessingBridge.GetKernelFormat(PixelFormats.GetPixelFormat<TPixel>()) switch
            {
                PixelFormat.Rgba32 => ConvertColor<Rgba32>(color),
                PixelFormat.Rgb24 => ConvertColor<Rgb24>(color),
                PixelFormat.Rgba64 => ConvertColor<Rgba64>(color),
                _ => throw new UnreachableException(),
            };
        }

        var opaque = new Rgba64(color.R, color.G, color.B);
        TPixel pixel = default;
        PixelConverter.ConvertRow(new ReadOnlySpan<Rgba64>(in opaque), new Span<TPixel>(ref pixel));
        if (typeof(TPixel) == typeof(Rgba64))
        {
            var value = unsafe(Unsafe.BitCast<TPixel, Rgba64>(pixel));
            return new DrawingColor(value.R, value.G, value.B, color.A);
        }

        if (typeof(TPixel) == typeof(Rgb48))
        {
            var value = unsafe(Unsafe.BitCast<TPixel, Rgb48>(pixel));
            return new DrawingColor(value.R, value.G, value.B, color.A);
        }

        if (typeof(TPixel) == typeof(Gray16))
            return new DrawingColor(unsafe(Unsafe.BitCast<TPixel, Gray16>(pixel)).Value, 0, 0, color.A);

        // The formats composited as RGBA (CompositeBridged) get their gray level in the three colors
        if (typeof(TPixel) == typeof(La32))
        {
            var level = unsafe(Unsafe.BitCast<TPixel, La32>(pixel)).L;
            return new DrawingColor(level, level, level, color.A);
        }

        var alpha = PixelConverter.To8Bit(color.A);
        if (typeof(TPixel) == typeof(La16))
        {
            var level = unsafe(Unsafe.BitCast<TPixel, La16>(pixel)).L;
            return new DrawingColor(level, level, level, alpha);
        }

        if (typeof(TPixel) == typeof(A8))
            return new DrawingColor(0, 0, 0, alpha);

        if (typeof(TPixel) == typeof(Rgba32))
        {
            var value = unsafe(Unsafe.BitCast<TPixel, Rgba32>(pixel));
            return new DrawingColor(value.R, value.G, value.B, alpha);
        }

        if (typeof(TPixel) == typeof(Bgra32))
        {
            var value = unsafe(Unsafe.BitCast<TPixel, Bgra32>(pixel));
            return new DrawingColor(value.B, value.G, value.R, alpha);
        }

        if (typeof(TPixel) == typeof(Rgb24))
        {
            var value = unsafe(Unsafe.BitCast<TPixel, Rgb24>(pixel));
            return new DrawingColor(value.R, value.G, value.B, alpha);
        }

        if (typeof(TPixel) == typeof(Gray8))
            return new DrawingColor(unsafe(Unsafe.BitCast<TPixel, Gray8>(pixel)).Value, 0, 0, alpha);

        throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
    }

    /// <summary>Converts paint colors to the pixel format of a frame: four samples per color, laid out as <see cref="DrawingColor"/>.</summary>
    /// <remarks>
    /// The colors are those of <see cref="ConvertColor{TPixel}(Rgba64)"/>: the opaque colors of a chunk are converted
    /// by one call of <see cref="PixelConverter"/>, which converts each pixel on its own.
    /// </remarks>
    public static void ConvertColors<TPixel>(ReadOnlySpan<Rgba64> colors, Span<ushort> destination)
        where TPixel : unmanaged
    {
        const int ChunkSize = 64;
        Span<Rgba64> opaque = stackalloc Rgba64[ChunkSize];
        Span<TPixel> pixels = stackalloc TPixel[ChunkSize];
        for (var start = 0; start < colors.Length; start += ChunkSize)
        {
            var chunk = colors.Slice(start, Math.Min(ChunkSize, colors.Length - start));
            var count = chunk.Length;
            var done = 0;
            if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
            {
                // The alpha is the last 16 bits of the 64 bits of a color
                var colorBits = unsafe(MemoryMarshal.Cast<Rgba64, ulong>(chunk));
                var opaqueBits = unsafe(MemoryMarshal.Cast<Rgba64, ulong>(opaque));
                if (Vector256.IsHardwareAccelerated)
                {
                    for (; done + 4 <= count; done += 4)
                    {
                        (Vector256.Create(colorBits.Slice(done, 4)) | Vector256.Create(0xFFFF_0000_0000_0000UL)).CopyTo(opaqueBits.Slice(done, 4));
                    }
                }

                for (; done + 2 <= count; done += 2)
                {
                    (Vector128.Create(colorBits.Slice(done, 2)) | Vector128.Create(0xFFFF_0000_0000_0000UL)).CopyTo(opaqueBits.Slice(done, 2));
                }
            }

            for (var i = done; i < count; i++)
            {
                var color = chunk[i];
                opaque[i] = new Rgba64(color.R, color.G, color.B);
            }

            var entries = destination.Slice(start * 4, count * 4);
            if (PixelConverter.IsFloat<TPixel>())
            {
                // The paints of a floating-point format keep the 16-bit colors
                unsafe { MemoryMarshal.Cast<Rgba64, ushort>(chunk).CopyTo(entries); }
                continue;
            }

            if (IsBridgedToColorFormat<TPixel>())
            {
                // The paints of the format of the kernels (see ConvertColor)
                switch (ProcessingBridge.GetKernelFormat(PixelFormats.GetPixelFormat<TPixel>()))
                {
                    case PixelFormat.Rgba32:
                        ConvertColors<Rgba32>(chunk, entries);
                        break;
                    case PixelFormat.Rgb24:
                        ConvertColors<Rgb24>(chunk, entries);
                        break;
                    case PixelFormat.Rgba64:
                        ConvertColors<Rgba64>(chunk, entries);
                        break;
                    default:
                        throw new UnreachableException();
                }

                continue;
            }

            PixelConverter.ConvertRow<Rgba64, TPixel>(opaque[..count], pixels);
            if (typeof(TPixel) == typeof(Rgba64))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, Rgba64>(pixels));
                var i = 0;
                if (Vector128.IsHardwareAccelerated)
                {
                    // The converted color with the alpha of the paint, two colors per vector
                    var paintSamples = unsafe(MemoryMarshal.Cast<Rgba64, ushort>(chunk));
                    var convertedSamples = unsafe(MemoryMarshal.Cast<Rgba64, ushort>(values));
                    for (; i + 2 <= count; i += 2)
                    {
                        Vector128.ConditionalSelect(AlphaLanes, Vector128.Create(paintSamples.Slice(i * 4, 8)), Vector128.Create(convertedSamples.Slice(i * 4, 8))).CopyTo(entries.Slice(i * 4, 8));
                    }
                }

                for (; i < count; i++)
                {
                    var value = values[i];
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = value.R;
                    entry[1] = value.G;
                    entry[2] = value.B;
                    entry[3] = chunk[i].A;
                }
            }
            else if (typeof(TPixel) == typeof(Gray16))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, Gray16>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = values[i].Value;
                    entry[1] = 0;
                    entry[2] = 0;
                    entry[3] = chunk[i].A;
                }
            }
            else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
            {
                // The bytes of the converted pixels in their stored order, with the alpha of the paint
                var i = ConvertColors32Vector(unsafe(MemoryMarshal.AsBytes(pixels[..count])), chunk, entries);
                for (; i < count; i++)
                {
                    var value = unsafe(MemoryMarshal.AsBytes(pixels.Slice(i, 1)));
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = value[0];
                    entry[1] = value[1];
                    entry[2] = value[2];
                    entry[3] = PixelConverter.To8Bit(chunk[i].A);
                }
            }
            else if (typeof(TPixel) == typeof(Rgb24))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, Rgb24>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var value = values[i];
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = value.R;
                    entry[1] = value.G;
                    entry[2] = value.B;
                    entry[3] = PixelConverter.To8Bit(chunk[i].A);
                }
            }
            else if (typeof(TPixel) == typeof(Gray8))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, Gray8>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = values[i].Value;
                    entry[1] = 0;
                    entry[2] = 0;
                    entry[3] = PixelConverter.To8Bit(chunk[i].A);
                }
            }
            else if (typeof(TPixel) == typeof(La16))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, La16>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = entry[1] = entry[2] = values[i].L;
                    entry[3] = PixelConverter.To8Bit(chunk[i].A);
                }
            }
            else if (typeof(TPixel) == typeof(La32))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, La32>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = entry[1] = entry[2] = values[i].L;
                    entry[3] = chunk[i].A;
                }
            }
            else if (typeof(TPixel) == typeof(A8))
            {
                for (var i = 0; i < count; i++)
                {
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = entry[1] = entry[2] = 0;
                    entry[3] = PixelConverter.To8Bit(chunk[i].A);
                }
            }
            else if (typeof(TPixel) == typeof(Rgb48))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, Rgb48>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var value = values[i];
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = value.R;
                    entry[1] = value.G;
                    entry[2] = value.B;
                    entry[3] = chunk[i].A;
                }
            }
            else
            {
                throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
            }
        }
    }

    /// <summary>The fourth 16-bit lane of each 64 bits: the alpha of a color of four samples.</summary>
    private static Vector128<ushort> AlphaLanes => Vector128.Create((ushort)0, 0, 0, ushort.MaxValue, 0, 0, 0, ushort.MaxValue);

    /// <summary>
    /// Writes the paint colors of the first pixels of four 8-bit samples: their bytes, widened, with the 8-bit alpha
    /// (<see cref="PixelConverter.To8Bit"/>) of the paint; returns the number of colors written.
    /// </summary>
    private static int ConvertColors32Vector(ReadOnlySpan<byte> pixels, ReadOnlySpan<Rgba64> paints, Span<ushort> entries)
    {
        var count = pixels.Length / 4;
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var paintSamples = unsafe(MemoryMarshal.Cast<Rgba64, ushort>(paints));
            for (; i + 4 <= count; i += 4)
            {
                var bytes = Vector128.Create(pixels.Slice(i * 4, 16));
                var alphaLow = PixelConverter.To8BitVector(Vector128.Create(paintSamples.Slice(i * 4, 8)));
                var alphaHigh = PixelConverter.To8BitVector(Vector128.Create(paintSamples.Slice((i * 4) + 8, 8)));
                Vector128.ConditionalSelect(AlphaLanes, alphaLow, Vector128.WidenLower(bytes)).CopyTo(entries.Slice(i * 4, 8));
                Vector128.ConditionalSelect(AlphaLanes, alphaHigh, Vector128.WidenUpper(bytes)).CopyTo(entries.Slice((i * 4) + 8, 8));
            }
        }

        return i;
    }

    /// <summary>Reads pixels of a row as 16-bit colors, with the rules of pixel conversion (a floating-point sample outside [0, 1] is rejected).</summary>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="destination">The colors; as many pixels are read as it holds.</param>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static void ReadColors<TPixel>(PixelStorage storage, int y, int x, Span<Rgba64> destination)
        where TPixel : unmanaged
    {
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var row = unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y))).Slice(x, destination.Length);
        if (PixelConverter.IsFloat<TPixel>())
        {
            ReadFloatColors(row, destination);
            return;
        }

        PixelConverter.ConvertRow<TPixel, Rgba64>(row, destination);
    }

    /// <summary>Reads pixels of a row as 16-bit colors outside of a drawing, with the rules and the failures of pixel conversion.</summary>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="destination">The colors; as many pixels are read as it holds.</param>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static void ReadConvertedColors<TPixel>(PixelStorage storage, int y, int x, Span<Rgba64> destination)
        where TPixel : unmanaged
    {
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var row = unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y))).Slice(x, destination.Length);
        PixelConverter.ConvertRow<TPixel, Rgba64>(row, destination);
    }

    /// <summary>
    /// Reads floating-point pixels as 16-bit colors for a drawing, which reads the canvas (its backdrop) and the images it
    /// draws that way; its layers have 16-bit samples. A sample outside [0, 1] or that is not a number has no 16-bit color: it is rejected, never clipped,
    /// with a message that says what to do in a drawing (pixel conversion options cannot be passed to it).
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">A sample is outside [0, 1] or is not a number.</exception>
    private static void ReadFloatColors<TPixel>(Span<TPixel> pixels, Span<Rgba64> destination)
        where TPixel : unmanaged
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            PixelConverter.ReadFloat(ref pixels[i], out var r, out var g, out var b, out var a);
            if (!IsInUnitRange(r) || !IsInUnitRange(g) || !IsInUnitRange(b) || !IsInUnitRange(a))
                throw CreateOutOfRangeColorException(typeof(TPixel) == typeof(RgbaFloat) ? PixelFormat.RgbaFloat : PixelFormat.RgbaHalf, r, g, b, a);
        }

        PixelConverter.ConvertRow<TPixel, Rgba64>(pixels, destination);

        // Not a number fails both comparisons
        static bool IsInUnitRange(double value) => value is >= 0 and <= 1;
    }

    /// <summary>The failure of a drawing that reads a floating-point pixel without a 16-bit color.</summary>
    internal static UnsupportedImageFeatureException CreateOutOfRangeColorException(PixelFormat format, double r, double g, double b, double a)
        => new(string.Create(CultureInfo.InvariantCulture, $"Drawing reads the {format} pixels of the canvas (for a backdrop layer, a backdrop blur or a group that is not isolated) or of a drawn image as 16-bit colors, and a pixel is ({r}, {g}, {b}, {a}): a sample outside [0, 1] or that is not a number has none, and is never clipped. Keep those samples in [0, 1], or draw on an Rgba64 copy (Image.CloneAs<Rgba64> with PixelConversionOptions that clip or tone map them)."), ImageFormat.Unknown, OutOfRangeColorFeature);

    /// <summary>The <see cref="UnsupportedImageFeatureException.Feature"/> of <see cref="CreateOutOfRangeColorException"/>.</summary>
    internal const string OutOfRangeColorFeature = "Floating-point samples outside [0, 1] read as 16-bit colors";

    /// <summary>
    /// Reads pixels of a row as the paint colors of a frame of the same pixel type: four samples per pixel, laid out as
    /// <see cref="DrawingColor"/>. These are the colors <see cref="ConvertColors{TPixel}"/> gives for those
    /// <see cref="ReadColors{TPixel}"/> reads, without the 16-bit colors in between: each sample is written as it is
    /// stored, a format without alpha has an opaque one, a gray format has its level first then two zeros, a gray format
    /// with alpha has its level three times (<see cref="A8"/> zeros), as the paints of these formats are composited as RGBA. Every
    /// sample goes back to itself through 16 bits, and the luma of a gray is that gray (<c>VectorizedKernelTests</c>
    /// checks every 8-bit sample and every 16-bit gray).
    /// </summary>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="destination">The colors; as many pixels are read as it holds.</param>
    public static void ReadDrawingColors<TPixel>(PixelStorage storage, int y, int x, Span<ushort> destination)
        where TPixel : unmanaged
    {
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var count = destination.Length / 4;
        var row = storage.GetRowSpanCore(y);
        if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
        {
            var samples = row.Slice(x * 4, count * 4);
            var i = 0;
            if (Vector512.IsHardwareAccelerated)
            {
                for (; i + 64 <= samples.Length; i += 64)
                {
                    var block = Vector512.Create(samples.Slice(i, 64));
                    Vector512.WidenLower(block).CopyTo(destination.Slice(i, 32));
                    Vector512.WidenUpper(block).CopyTo(destination.Slice(i + 32, 32));
                }
            }

            if (Vector256.IsHardwareAccelerated)
            {
                for (; i + 32 <= samples.Length; i += 32)
                {
                    var block = Vector256.Create(samples.Slice(i, 32));
                    Vector256.WidenLower(block).CopyTo(destination.Slice(i, 16));
                    Vector256.WidenUpper(block).CopyTo(destination.Slice(i + 16, 16));
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                for (; i + 16 <= samples.Length; i += 16)
                {
                    var block = Vector128.Create(samples.Slice(i, 16));
                    Vector128.WidenLower(block).CopyTo(destination.Slice(i, 8));
                    Vector128.WidenUpper(block).CopyTo(destination.Slice(i + 8, 8));
                }
            }

            for (; i < samples.Length; i++)
            {
                destination[i] = samples[i];
            }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 4, count * 4);
            samples.CopyTo(destination);
        }
        else if (typeof(TPixel) == typeof(Rgb24))
        {
            var samples = row.Slice(x * 3, count * 3);
            var i = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                // Four pixels of each 16 bytes read (the last 4 bytes are not used), an opaque alpha in their fourth byte; the
                // indices stay in range (a constant shuffle on every platform), the alpha replaces the repeated byte
                var expand = Vector128.Create((byte)0, 1, 2, 2, 3, 4, 5, 5, 6, 7, 8, 8, 9, 10, 11, 11);
                var opaque = Vector128.Create(0xFF000000u).AsByte();
                for (; (i * 3) + 16 <= samples.Length; i += 4)
                {
                    var block = Vector128.Shuffle(Vector128.Create(samples.Slice(i * 3, 16)), expand) | opaque;
                    Vector128.WidenLower(block).CopyTo(destination.Slice(i * 4, 8));
                    Vector128.WidenUpper(block).CopyTo(destination.Slice((i * 4) + 8, 8));
                }
            }

            for (; i < count; i++)
            {
                var entry = destination.Slice(i * 4, 4);
                entry[0] = samples[i * 3];
                entry[1] = samples[(i * 3) + 1];
                entry[2] = samples[(i * 3) + 2];
                entry[3] = byte.MaxValue;
            }
        }
        else if (typeof(TPixel) == typeof(Gray8))
        {
            var samples = row.Slice(x, count);
            var i = 0;
            if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
            {
                // Each gray sample in the first 16 bits of the 64 bits of its color, with an opaque alpha in the last 16 bits
                var opaque = Vector128.Create(0x00FF_0000_0000_0000UL);
                var colors = unsafe(MemoryMarshal.Cast<ushort, ulong>(destination));
                for (; i + 8 <= count; i += 8)
                {
                    var words = Vector128.WidenLower(Vector128.CreateScalar(unsafe(MemoryMarshal.Read<ulong>(samples.Slice(i, 8)))).AsByte());
                    var (first, second) = Vector128.Widen(words);
                    (Vector128.WidenLower(first) | opaque).CopyTo(colors.Slice(i, 2));
                    (Vector128.WidenUpper(first) | opaque).CopyTo(colors.Slice(i + 2, 2));
                    (Vector128.WidenLower(second) | opaque).CopyTo(colors.Slice(i + 4, 2));
                    (Vector128.WidenUpper(second) | opaque).CopyTo(colors.Slice(i + 6, 2));
                }
            }

            for (; i < count; i++)
            {
                var entry = destination.Slice(i * 4, 4);
                entry[0] = samples[i];
                entry[1] = 0;
                entry[2] = 0;
                entry[3] = byte.MaxValue;
            }
        }
        else if (typeof(TPixel) == typeof(Gray16))
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x, count);
            for (var i = 0; i < count; i++)
            {
                var entry = destination.Slice(i * 4, 4);
                entry[0] = samples[i];
                entry[1] = 0;
                entry[2] = 0;
                entry[3] = ushort.MaxValue;
            }
        }
        else if (typeof(TPixel) == typeof(Rgb48))
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 3, count * 3);
            for (var i = 0; i < count; i++)
            {
                var entry = destination.Slice(i * 4, 4);
                entry[0] = samples[i * 3];
                entry[1] = samples[(i * 3) + 1];
                entry[2] = samples[(i * 3) + 2];
                entry[3] = ushort.MaxValue;
            }
        }
        else if (typeof(TPixel) == typeof(La16))
        {
            var samples = row.Slice(x * 2, count * 2);
            for (var i = 0; i < count; i++)
            {
                var entry = destination.Slice(i * 4, 4);
                entry[0] = entry[1] = entry[2] = samples[i * 2];
                entry[3] = samples[(i * 2) + 1];
            }
        }
        else if (typeof(TPixel) == typeof(La32))
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 2, count * 2);
            for (var i = 0; i < count; i++)
            {
                var entry = destination.Slice(i * 4, 4);
                entry[0] = entry[1] = entry[2] = samples[i * 2];
                entry[3] = samples[(i * 2) + 1];
            }
        }
        else if (typeof(TPixel) == typeof(A8))
        {
            var samples = row.Slice(x, count);
            for (var i = 0; i < count; i++)
            {
                var entry = destination.Slice(i * 4, 4);
                entry[0] = entry[1] = entry[2] = 0;
                entry[3] = samples[i];
            }
        }
        else if (IsBridgedToColorFormat<TPixel>())
        {
            ReadBridgedDrawingColors(unsafe(MemoryMarshal.Cast<byte, TPixel>(row)).Slice(x, count), destination);
        }
        else if (PixelConverter.IsFloat<TPixel>())
        {
            // The 16-bit colors of the pixels: a sample outside [0, 1] has none, and is rejected
            var pixels = unsafe(MemoryMarshal.Cast<byte, TPixel>(row)).Slice(x, count);
            ReadFloatColors(pixels, unsafe(MemoryMarshal.Cast<ushort, Rgba64>(destination)));
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    /// <summary>Composites a paint over a run of pixels of a row.</summary>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="coverage">The coverage of each pixel of the run, from 0 to <see cref="CoverageRasterizer.FullCoverage"/>.</param>
    /// <param name="source">The paint, converted to the pixel format of the frame.</param>
    public static void Composite<TPixel>(PixelStorage storage, int y, int x, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TPixel : unmanaged
    {
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var row = storage.GetRowSpanCore(y);
        if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
        {
            Composite(row.Slice(x * 4, coverage.Length * 4), channels: 4, coverage, source, isBlueFirst: typeof(TPixel) == typeof(Bgra32));
        }
        else if (typeof(TPixel) == typeof(Rgb24))
        {
            Composite(row.Slice(x * 3, coverage.Length * 3), channels: 3, coverage, source);
        }
        else if (typeof(TPixel) == typeof(Gray8))
        {
            Composite(row.Slice(x, coverage.Length), channels: 1, coverage, source);
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            Composite(unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 4, coverage.Length * 4), channels: 4, coverage, source);
        }
        else if (typeof(TPixel) == typeof(Gray16))
        {
            Composite(unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x, coverage.Length), channels: 1, coverage, source);
        }
        else if (typeof(TPixel) == typeof(Rgb48))
        {
            Composite(unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 3, coverage.Length * 3), channels: 3, coverage, source);
        }
        else if (typeof(TPixel) == typeof(La16) || typeof(TPixel) == typeof(A8) || typeof(TPixel) == typeof(Argb32) || typeof(TPixel) == typeof(Abgr32))
        {
            CompositeBridged<TPixel, Rgba32, byte>(unsafe(MemoryMarshal.Cast<byte, TPixel>(row)).Slice(x, coverage.Length), channels: 4, coverage, source);
        }
        else if (typeof(TPixel) == typeof(Bgr565))
        {
            CompositeBridged<TPixel, Rgb24, byte>(unsafe(MemoryMarshal.Cast<byte, TPixel>(row)).Slice(x, coverage.Length), channels: 3, coverage, source);
        }
        else if (typeof(TPixel) == typeof(La32) || typeof(TPixel) == typeof(Rgba1010102))
        {
            CompositeBridged<TPixel, Rgba64, ushort>(unsafe(MemoryMarshal.Cast<byte, TPixel>(row)).Slice(x, coverage.Length), channels: 4, coverage, source);
        }
        else if (PixelConverter.IsFloat<TPixel>())
        {
            FloatDrawingKernels.Composite<TPixel>(storage, y, x, coverage, source);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    /// <summary>
    /// Composites a paint over pixels of a format that has no kernel of its own (gray with alpha, alpha only, the
    /// interoperability formats): each chunk of the run is converted to the format of its kernels
    /// (<see cref="ProcessingBridge.GetKernelFormat"/>), composited, and converted back. The paints of the gray formats
    /// have the gray level in the three colors (black for <see cref="A8"/>), so the three colors of the result are the
    /// same and the conversion back is exact: the result is the one of a gray kernel with alpha, and an alpha mask gets
    /// the alpha of the result. <see cref="Argb32"/> and <see cref="Abgr32"/> get the result of <see cref="Rgba32"/>, and
    /// a packed format the result of its wider format rounded to its components.
    /// </summary>
    private static void CompositeBridged<TPixel, TWorking, TSample>(Span<TPixel> pixels, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TPixel : unmanaged
        where TWorking : unmanaged
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        const int ChunkSize = 128;
        Span<TWorking> working = stackalloc TWorking[ChunkSize];
        for (var start = 0; start < pixels.Length; start += ChunkSize)
        {
            var count = Math.Min(ChunkSize, pixels.Length - start);
            var chunk = pixels.Slice(start, count);
            var rgba = working[..count];
            PixelConverter.ConvertRow<TPixel, TWorking>(chunk, rgba);
            Composite(unsafe(MemoryMarshal.Cast<TWorking, TSample>(rgba)), channels, coverage.Slice(start, count), source.Slice(start, count));
            PixelConverter.ConvertRow<TWorking, TPixel>(rgba, chunk);
        }
    }

    /// <summary>
    /// Gets a value indicating whether a pixel format is drawn in the format of its kernels with the paints of that format:
    /// the interoperability formats (<see cref="Argb32"/>, <see cref="Abgr32"/>, <see cref="Bgr565"/>,
    /// <see cref="Rgba1010102"/>), whose colors are those of <see cref="Rgba32"/>, <see cref="Rgb24"/> or <see cref="Rgba64"/>:
    /// the result of that format is rounded to their components when it is stored.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsBridgedToColorFormat<TPixel>() => typeof(TPixel) == typeof(Argb32) || typeof(TPixel) == typeof(Abgr32) || typeof(TPixel) == typeof(Bgr565) || typeof(TPixel) == typeof(Rgba1010102);

    /// <summary>
    /// Gets a value indicating whether the layers, the projections and the canvas pattern tiles of a frame are 8-bit
    /// (<see cref="Rgba32"/>) rather than 16-bit (<see cref="Rgba64"/>) images: the 8-bit color formats, and those drawn
    /// as one of them.
    /// </summary>
    /// <param name="format">The pixel format of the frame.</param>
    public static bool HasNarrowWorkingImages(PixelFormat format) => format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Argb32 or PixelFormat.Abgr32 or PixelFormat.Bgr565;

    /// <summary>
    /// Reads pixels of an interoperability format as the paint colors of the format of its kernels: the pixels converted to
    /// that format (exactly), laid out as <see cref="DrawingColor"/>.
    /// </summary>
    private static void ReadBridgedDrawingColors<TPixel>(ReadOnlySpan<TPixel> pixels, Span<ushort> destination)
        where TPixel : unmanaged
    {
        const int ChunkSize = 64;
        switch (ProcessingBridge.GetKernelFormat(PixelFormats.GetPixelFormat<TPixel>()))
        {
            case PixelFormat.Rgba32:
                {
                    Span<Rgba32> working = stackalloc Rgba32[ChunkSize];
                    for (var start = 0; start < pixels.Length; start += ChunkSize)
                    {
                        var count = Math.Min(ChunkSize, pixels.Length - start);
                        PixelConverter.ConvertRow(pixels.Slice(start, count), working);
                        for (var i = 0; i < count; i++)
                        {
                            var entry = destination.Slice((start + i) * 4, 4);
                            (entry[0], entry[1], entry[2], entry[3]) = (working[i].R, working[i].G, working[i].B, working[i].A);
                        }
                    }

                    break;
                }

            case PixelFormat.Rgb24:
                {
                    Span<Rgb24> working = stackalloc Rgb24[ChunkSize];
                    for (var start = 0; start < pixels.Length; start += ChunkSize)
                    {
                        var count = Math.Min(ChunkSize, pixels.Length - start);
                        PixelConverter.ConvertRow(pixels.Slice(start, count), working);
                        for (var i = 0; i < count; i++)
                        {
                            var entry = destination.Slice((start + i) * 4, 4);
                            (entry[0], entry[1], entry[2], entry[3]) = (working[i].R, working[i].G, working[i].B, byte.MaxValue);
                        }
                    }

                    break;
                }

            case PixelFormat.Rgba64:
                PixelConverter.ConvertRow(pixels, unsafe(MemoryMarshal.Cast<ushort, Rgba64>(destination)));
                break;

            default:
                throw new UnreachableException();
        }
    }

    /// <summary>
    /// Gets a value indicating whether a paint without opacity changes nothing with an operator: its factor of the
    /// destination is then 1. With the other operators, a transparent paint erases.
    /// </summary>
    public static bool IsTransparentPaintInert(CompositeMode mode)
        => mode is CompositeMode.SourceOver or CompositeMode.DestinationOver or CompositeMode.DestinationOut or CompositeMode.SourceAtop or CompositeMode.Xor or CompositeMode.Plus;

    /// <summary>Composites a paint over interleaved samples: 1 (gray), 3 (color) or 4 (color then alpha) per pixel.</summary>
    /// <param name="samples">The samples of the pixels.</param>
    /// <param name="channels">The number of samples per pixel.</param>
    /// <param name="coverage">The coverage of each pixel.</param>
    /// <param name="source">The paint.</param>
    /// <param name="isBlueFirst"><see langword="true"/> when the first color sample is blue and the third one red: only the blend modes that take a color as a whole need to know.</param>
    internal static void Composite<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, bool isBlueFirst = false)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        if (typeof(TSample) == typeof(byte) && source.Colors.IsEmpty && SampleTable.IsSeparable(source) && TryCompositeWithTable(unsafe(MemoryMarshal.Cast<TSample, byte>(samples)), channels, coverage, source, isBlueFirst))
            return;

        CompositeWithFormulas(samples, channels, coverage, source, isBlueFirst);
    }

    /// <summary>
    /// Composites a single paint over 8-bit samples with a <see cref="SampleTable"/>: the pixels covered entirely whose
    /// alpha is the one of the table get its samples, the others go through <see cref="CompositeWithFormulas"/>.
    /// </summary>
    /// <returns><see langword="false"/> when no pixel is covered entirely, or the paint has no table yet.</returns>
    internal static bool TryCompositeWithTable(Span<byte> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, bool isBlueFirst)
    {
        const uint Full = CoverageRasterizer.FullCoverage;
        var first = coverage.IndexOf(Full);
        if (first < 0)
            return false;

        // The table of the alpha of the first pixel covered entirely: the pixels inside a shape usually share it
        var destinationAlpha = channels == 4 ? samples[(first * 4) + 3] : byte.MaxValue;
        var table = SampleTable.Get(source, channels, isBlueFirst, destinationAlpha, coverage.Length);
        if (table is null)
            return false;

        var lookup = table.Samples;
        var alpha = table.Alpha;
        var start = 0;
        var i = first;
        while (true)
        {
            if (i > start)
            {
                CompositeWithFormulas(samples[(start * channels)..(i * channels)], channels, coverage[start..i], source.Slice(start, i - start), isBlueFirst);
            }

            // The pixels the table gives
            switch (channels)
            {
                case 4:
                    {
                        // A pixel read as one little-endian integer: the alpha is its high byte
                        var pixels = unsafe(MemoryMarshal.Cast<byte, uint>(samples));
                        var pixelAlpha = (uint)destinationAlpha << 24;
                        var resultAlpha = (uint)alpha << 24;
                        for (; i < coverage.Length && coverage[i] == Full; i++)
                        {
                            var pixel = pixels[i];
                            if ((pixel & 0xFF000000) != pixelAlpha)
                                break;

                            pixels[i] = lookup[(int)(pixel & 0xFF)] | ((uint)lookup[256 + (int)((pixel >> 8) & 0xFF)] << 8) | ((uint)lookup[512 + (int)((pixel >> 16) & 0xFF)] << 16) | resultAlpha;
                        }
                    }

                    break;

                case 3:
                    for (; i < coverage.Length && coverage[i] == Full; i++)
                    {
                        var pixel = samples.Slice(i * 3, 3);
                        pixel[0] = lookup[pixel[0]];
                        pixel[1] = lookup[256 + pixel[1]];
                        pixel[2] = lookup[512 + pixel[2]];
                    }

                    break;

                default:
                    for (; i < coverage.Length && coverage[i] == Full; i++)
                    {
                        samples[i] = lookup[samples[i]];
                    }

                    break;
            }

            if (i == coverage.Length)
                return true;

            // The pixels that go through the formulas, up to the next one the table gives
            start = i;
            for (i++; i < coverage.Length && (coverage[i] != Full || (channels == 4 && samples[(i * 4) + 3] != destinationAlpha)); i++)
            {
            }
        }
    }

    /// <summary>Composites a paint over interleaved samples with the formulas of the contract: the reference of <see cref="TryCompositeWithTable"/>.</summary>
    internal static void CompositeWithFormulas<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, bool isBlueFirst = false)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        if (source.IsLinearLight)
        {
            CompositeLinear(samples, channels, coverage, source, isBlueFirst);
            return;
        }

        if (source.Blend != BlendMode.Normal)
        {
            CompositeBlended(samples, channels, coverage, source, isBlueFirst);
            return;
        }

        // A short run is cheaper pixel by pixel than searched for runs
        if (coverage.Length >= MinFullRunSearchLength)
        {
            if (source.Colors.IsEmpty)
            {
                if (TryCompositeFullRuns(samples, channels, coverage, source))
                    return;
            }
            else if (source.Mode == CompositeMode.Source && channels == 4 && TryReplaceFullRuns(samples, coverage, source.Colors))
            {
                return;
            }
        }

        CompositeCore(samples, channels, coverage, source);
    }

    /// <summary>Composites a paint with the formulas of the stored samples, pixel by pixel.</summary>
    private static void CompositeCore<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        if (source.Mode != CompositeMode.SourceOver)
        {
            CompositeWithOperator(samples, channels, coverage, source);
        }
        else
        {
            CompositeOver(samples, channels, coverage, source);
        }
    }

    /// <summary>
    /// Composites a single paint whose result on a pixel covered entirely does not depend on the pixel: an opaque paint
    /// with source-over, or any paint that replaces the pixel (<see cref="CompositeMode.Source"/>). The runs of full
    /// coverage get the samples the formulas give one of their pixels; the other pixels go through the formulas.
    /// </summary>
    /// <returns><see langword="false"/> when the paint has no such result, or no pixel is covered entirely.</returns>
    /// <remarks>
    /// With <c>q = Q</c>, source-over and <c>sa = M</c> write the paint (<c>c' = sc</c>, <c>a' = M</c>). The operator
    /// <see cref="CompositeMode.Source"/> has <c>D = 0</c>: the color is <c>round(sc S / S) = sc</c> and the alpha
    /// <c>round(S / (M Q)) = sa</c>, or the color over black without alpha, whatever the pixel was. A paint without
    /// opacity then makes a pixel with alpha transparent black, or leaves it as it is when it was transparent.
    /// </remarks>
    private static bool TryCompositeFullRuns<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        const uint Full = CoverageRasterizer.FullCoverage;
        var paintAlpha = source.Color.Alpha;
        var isConstant = source.Mode switch
        {
            CompositeMode.SourceOver => paintAlpha == ushort.CreateTruncating(TSample.MaxValue),
            CompositeMode.Source => paintAlpha != 0 || channels != 4,
            _ => false,
        };

        var erases = !isConstant && source.Mode == CompositeMode.Source;
        if (!isConstant && !erases)
            return false;

        var first = coverage.IndexOf(Full);
        if (first < 0)
            return false;

        // The samples of a pixel covered entirely, from the formulas
        Span<TSample> pixel = stackalloc TSample[4];
        pixel = pixel[..channels];
        if (isConstant)
        {
            var full = Full;
            CompositeCore(pixel, channels, new ReadOnlySpan<uint>(in full), source);
        }

        var start = 0;
        while (true)
        {
            if (first > start)
            {
                CompositeCore(samples[(start * channels)..(first * channels)], channels, coverage[start..first], source);
            }

            if (first == coverage.Length)
                return true;

            var length = coverage[first..].IndexOfAnyExcept(Full);
            if (length < 0)
            {
                length = coverage.Length - first;
            }

            var run = samples.Slice(first * channels, length * channels);
            if (isConstant)
            {
                FillPixels(run, pixel);
            }
            else
            {
                ErasePixels(run);
            }

            start = first + length;
            if (start == coverage.Length)
                return true;

            var next = coverage[start..].IndexOf(Full);
            first = next < 0 ? coverage.Length : start + next;
        }
    }

    /// <summary>
    /// Replaces pixels with a color of their own (<see cref="CompositeMode.Source"/>, color then alpha): the runs of full
    /// coverage get their colors as they are; the other pixels go through the formulas.
    /// </summary>
    /// <returns><see langword="false"/> when no pixel is covered entirely.</returns>
    /// <remarks>
    /// With <c>q = Q</c>, the operator has <c>S = M Q sa</c> and <c>D = 0</c>: the alpha is <c>round(S / (M Q)) = sa</c>
    /// and the color <c>round(sc S / S) = sc</c>, as the scalar path computes them (<c>S / 2 &lt; S</c>). A paint without
    /// opacity makes the pixel transparent black, or leaves it as it is when it was transparent.
    /// </remarks>
    private static bool TryReplaceFullRuns<TSample>(Span<TSample> samples, ReadOnlySpan<uint> coverage, ReadOnlySpan<ushort> colors)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        const uint Full = CoverageRasterizer.FullCoverage;
        var first = coverage.IndexOf(Full);
        if (first < 0)
            return false;

        var start = 0;
        while (true)
        {
            if (first > start)
            {
                CompositeWithOperator(samples[(start * 4)..(first * 4)], 4, coverage[start..first], new DrawingSource(colors[(start * 4)..(first * 4)], CompositeMode.Source));
            }

            if (first == coverage.Length)
                return true;

            var length = coverage[first..].IndexOfAnyExcept(Full);
            if (length < 0)
            {
                length = coverage.Length - first;
            }

            ReplacePixels(samples.Slice(first * 4, length * 4), colors.Slice(first * 4, length * 4));
            start = first + length;
            if (start == coverage.Length)
                return true;

            var next = coverage[start..].IndexOf(Full);
            first = next < 0 ? coverage.Length : start + next;
        }
    }

    /// <summary>Writes a color to each pixel of a run (color then alpha): a color without opacity erases the pixel, unless it is transparent already.</summary>
    private static void ReplacePixels<TSample>(Span<TSample> pixels, ReadOnlySpan<ushort> colors)
        where TSample : unmanaged, IBinaryInteger<TSample>
    {
        for (var i = 0; i + 4 <= pixels.Length; i += 4)
        {
            var color = colors.Slice(i, 4);
            var pixel = pixels.Slice(i, 4);
            if (color[3] != 0)
            {
                pixel[0] = TSample.CreateTruncating(color[0]);
                pixel[1] = TSample.CreateTruncating(color[1]);
                pixel[2] = TSample.CreateTruncating(color[2]);
                pixel[3] = TSample.CreateTruncating(color[3]);
            }
            else if (pixel[3] != TSample.Zero)
            {
                pixel.Clear();
            }
        }
    }

    /// <summary>Writes the samples of one pixel to every pixel of a run.</summary>
    private static void FillPixels<TSample>(Span<TSample> run, ReadOnlySpan<TSample> pixel)
        where TSample : unmanaged
    {
        // The packed paths fill whole pixels only: a partial pixel at the end of the run would be left as it was
        Debug.Assert(run.Length % pixel.Length == 0);
        if (pixel.Length == 1)
        {
            run.Fill(pixel[0]);
            return;
        }

        if (pixel.Length == 4 && typeof(TSample) == typeof(byte))
        {
            var packed = unsafe(MemoryMarshal.Read<uint>(MemoryMarshal.AsBytes(pixel)));
            var pixels = unsafe(MemoryMarshal.Cast<TSample, uint>(run));
            pixels.Fill(packed);
            return;
        }

        if (pixel.Length == 4 && typeof(TSample) == typeof(ushort))
        {
            var packed = unsafe(MemoryMarshal.Read<ulong>(MemoryMarshal.AsBytes(pixel)));
            var pixels = unsafe(MemoryMarshal.Cast<TSample, ulong>(run));
            pixels.Fill(packed);
            return;
        }

        // The pixels written so far are copied after themselves
        pixel.CopyTo(run);
        var filled = pixel.Length;
        while (filled < run.Length)
        {
            var count = Math.Min(filled, run.Length - filled);
            run[..count].CopyTo(run[filled..]);
            filled += count;
        }
    }

    /// <summary>Makes the pixels of a run (color then alpha) that are not transparent transparent black.</summary>
    private static void ErasePixels<TSample>(Span<TSample> run)
        where TSample : unmanaged, IBinaryInteger<TSample>
    {
        for (var i = 0; i < run.Length; i += 4)
        {
            if (run[i + 3] != TSample.Zero)
            {
                run.Slice(i, 4).Clear();
            }
        }
    }

    /// <summary>Composites a paint over interleaved samples with source-over.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CompositeOver<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        if (typeof(TSample) == typeof(byte) && channels == 4 && coverage.Length >= 4 && Vector128.IsHardwareAccelerated)
        {
            if (Vector512.IsHardwareAccelerated && Avx512BW.IsSupported && coverage.Length >= 16)
            {
                CompositeOver8Avx512(unsafe(MemoryMarshal.Cast<TSample, byte>(samples)), coverage, source);
            }
            else if (Avx2.IsSupported && coverage.Length >= 8)
            {
                CompositeOver8Avx2(unsafe(MemoryMarshal.Cast<TSample, byte>(samples)), coverage, source);
            }
            else
            {
                CompositeOver8(unsafe(MemoryMarshal.Cast<TSample, byte>(samples)), coverage, source);
            }

            return;
        }

        if (typeof(TSample) == typeof(ushort) && channels is 4 or 1 && coverage.Length >= 2 && Vector128.IsHardwareAccelerated)
        {
            CompositeOver16(unsafe(MemoryMarshal.Cast<TSample, ushort>(samples)), channels, coverage, source);
            return;
        }

        CompositeOverScalar(samples, channels, coverage, source);
    }

    /// <summary>
    /// Composites a paint over pixels of four 8-bit samples (color then alpha) with source-over: four pixels at a time
    /// when they are all opaque or all transparent, the others with <see cref="CompositeOverScalar"/>. The blocks of
    /// four pixels without coverage are skipped, as the scalar path leaves such pixels as they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On an opaque pixel the color is <c>floor(x / S)</c> with <c>x = sc E + dc (S - E) + S / 2</c>, <c>S = 255 Q</c>
    /// and <c>E = sa q</c>, and the alpha stays 255. That formula also leaves the pixel as it is when <c>E</c> is 0 and
    /// writes the paint when <c>E</c> is <c>S</c>, as the scalar path does. <c>x</c> is below <c>255.5 S &lt; 2^32</c>;
    /// <c>floor(x / S) = floor(floor(x / 2^16) / 255)</c>, and <c>floor(y / 255) = (y 32897) >> 23</c> for every
    /// <c>y &lt; 2^16</c>, whose product is below 2^32.
    /// </para>
    /// <para>
    /// A transparent pixel gets the color of the paint and the alpha <c>(E + Q / 2) >> 16</c> (<c>E + Q / 2</c> is
    /// below 2^24), or stays as it is when that alpha is 0.
    /// </para>
    /// </remarks>
    internal static void CompositeOver8(Span<byte> samples, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
    {
        const uint Scale = byte.MaxValue * CoverageRasterizer.FullCoverage;
        var colors = source.Colors;
        var color = source.Color;
        var transpose = Vector128.Create((byte)0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15);
        var colorMask = Vector128.Create((byte)255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 0);
        var half = Vector128.Create(Scale / 2);
        var halfCoverage = Vector128.Create(CoverageRasterizer.FullCoverage / 2);
        var scale = Vector128.Create(Scale);
        var reciprocal = Vector128.Create(32897u);
        var red = Vector128.Create((uint)color.C0);
        var green = Vector128.Create((uint)color.C1);
        var blue = Vector128.Create((uint)color.C2);
        var alpha = Vector128.Create((uint)color.Alpha);

        // The pixels from 'pending' on are not composited yet
        var pending = 0;
        for (var i = 0; i + 4 <= coverage.Length; i += 4)
        {
            // Pixels without coverage stay as they are: the run of them is skipped up to the block of the next covered pixel
            var blockCoverage = Vector128.Create(coverage.Slice(i, 4));
            if (blockCoverage == Vector128<uint>.Zero)
            {
                if (pending < i)
                {
                    CompositeOverScalarRange(samples, coverage, source, pending, i);
                }

                var next = coverage[(i + 4)..].IndexOfAnyExcept(0u);
                i += (next < 0 ? coverage.Length - i - 4 : next) & ~3;
                pending = i + 4;
                continue;
            }

            var block = samples.Slice(i * 4, 16);
            var pixels = Vector128.Create(block);
            if ((pixels | colorMask) == Vector128<byte>.AllBitsSet)
            {
                if (pending < i)
                {
                    CompositeOverScalarRange(samples, coverage, source, pending, i);
                }

                if (!colors.IsEmpty)
                {
                    LoadPaint(colors.Slice(i * 4, 16), transpose, out red, out green, out blue, out alpha);
                }

                var effective = blockCoverage * alpha;
                var inverse = scale - effective;
                var planar = Vector128.Shuffle(pixels, transpose);
                var low = Vector128.WidenLower(planar);
                var high = Vector128.WidenUpper(planar);
                var r = Blend(red, Vector128.WidenLower(low), effective, inverse, half, reciprocal);
                var g = Blend(green, Vector128.WidenUpper(low), effective, inverse, half, reciprocal);
                var b = Blend(blue, Vector128.WidenLower(high), effective, inverse, half, reciprocal);
                var result = Vector128.Narrow(Vector128.Narrow(r, g), Vector128.Narrow(b, Vector128.WidenUpper(high)));
                Vector128.Shuffle(result, transpose).CopyTo(block);
                pending = i + 4;
            }
            else if (Vector128.AndNot(pixels, colorMask) == Vector128<byte>.Zero)
            {
                if (pending < i)
                {
                    CompositeOverScalarRange(samples, coverage, source, pending, i);
                }

                if (!colors.IsEmpty)
                {
                    LoadPaint(colors.Slice(i * 4, 16), transpose, out red, out green, out blue, out alpha);
                }

                // The paint with the alpha of its coverage, where that alpha is not 0
                var resultAlpha = Vector128.ShiftRightLogical((blockCoverage * alpha) + halfCoverage, 16);
                var painted = Vector128.Shuffle(Vector128.Narrow(Vector128.Narrow(red, green), Vector128.Narrow(blue, resultAlpha)), transpose);
                var unchanged = Vector128.Equals(resultAlpha, Vector128<uint>.Zero);
                var keep = Vector128.Shuffle(Vector128.Narrow(Vector128.Narrow(unchanged, unchanged), Vector128.Narrow(unchanged, unchanged)), transpose);
                Vector128.ConditionalSelect(keep, pixels, painted).CopyTo(block);
                pending = i + 4;
            }
        }

        if (pending < coverage.Length)
        {
            CompositeOverScalarRange(samples, coverage, source, pending, coverage.Length);
        }

        static Vector128<uint> Blend(Vector128<uint> paint, Vector128<uint> pixel, Vector128<uint> effective, Vector128<uint> inverse, Vector128<uint> half, Vector128<uint> reciprocal)
        {
            var x = (paint * effective) + (pixel * inverse) + half;
            return Vector128.ShiftRightLogical(Vector128.ShiftRightLogical(x, 16) * reciprocal, 23);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void LoadPaint(ReadOnlySpan<ushort> paint, Vector128<byte> transpose, out Vector128<uint> red, out Vector128<uint> green, out Vector128<uint> blue, out Vector128<uint> alpha)
        {
            var paintBytes = Vector128.Shuffle(Vector128.Narrow(Vector128.Create(paint[..8]), Vector128.Create(paint[8..])), transpose);
            var paintLow = Vector128.WidenLower(paintBytes);
            var paintHigh = Vector128.WidenUpper(paintBytes);
            red = Vector128.WidenLower(paintLow);
            green = Vector128.WidenUpper(paintLow);
            blue = Vector128.WidenLower(paintHigh);
            alpha = Vector128.WidenUpper(paintHigh);
        }
    }

    /// <summary>
    /// Composites a paint over pixels of four 8-bit samples (color then alpha) with source-over, with the formulas of
    /// <see cref="CompositeOver8"/>, eight pixels at a time with AVX2 when they are all opaque or all transparent; the
    /// other pixels go through <see cref="CompositeOver8"/>. Each half of a vector holds four pixels, as a vector of
    /// <see cref="CompositeOver8"/> does: the shuffles and the packs stay within the halves.
    /// </summary>
    internal static void CompositeOver8Avx2(Span<byte> samples, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
    {
        const uint Scale = byte.MaxValue * CoverageRasterizer.FullCoverage;
        var colors = source.Colors;
        var color = source.Color;
        var transpose = Vector256.Create((byte)0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15, 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15);
        var colorMask = Vector256.Create(0x00FFFFFFu).AsByte();
        var half = Vector256.Create(Scale / 2);
        var halfCoverage = Vector256.Create(CoverageRasterizer.FullCoverage / 2);
        var scale = Vector256.Create(Scale);
        var reciprocal = Vector256.Create(32897u);
        var red = Vector256.Create((uint)color.C0);
        var green = Vector256.Create((uint)color.C1);
        var blue = Vector256.Create((uint)color.C2);
        var alpha = Vector256.Create((uint)color.Alpha);

        // The pixels from 'pending' on are not composited yet
        var pending = 0;
        for (var i = 0; i + 8 <= coverage.Length; i += 8)
        {
            // Pixels without coverage stay as they are: the run of them is skipped up to the block of the next covered pixel
            var blockCoverage = Vector256.Create(coverage.Slice(i, 8));
            if (blockCoverage == Vector256<uint>.Zero)
            {
                if (pending < i)
                {
                    CompositeOver8Range(samples, coverage, source, pending, i);
                }

                var next = coverage[(i + 8)..].IndexOfAnyExcept(0u);
                i += (next < 0 ? coverage.Length - i - 8 : next) & ~7;
                pending = i + 8;
                continue;
            }

            var block = samples.Slice(i * 4, 32);
            var pixels = Vector256.Create(block);
            var isOpaque = (pixels | colorMask) == Vector256<byte>.AllBitsSet;
            if (!isOpaque && Vector256.AndNot(pixels, colorMask) != Vector256<byte>.Zero)
                continue;

            if (pending < i)
            {
                CompositeOver8Range(samples, coverage, source, pending, i);
            }

            if (!colors.IsEmpty)
            {
                // The paint of the eight pixels, four in each half, then as the pixels
                var paint = Avx2.PackUnsignedSaturate(Vector256.Create(colors.Slice(i * 4, 16)).AsInt16(), Vector256.Create(colors.Slice((i * 4) + 16, 16)).AsInt16());
                Split(Avx2.Permute4x64(paint.AsUInt64(), 0b11_01_10_00).AsByte(), transpose, out red, out green, out blue, out alpha);
            }

            Split(pixels, transpose, out var r, out var g, out var b, out var a);
            if (isOpaque)
            {
                var effective = blockCoverage * alpha;
                var inverse = scale - effective;
                r = Blend(red, r, effective, inverse, half, reciprocal);
                g = Blend(green, g, effective, inverse, half, reciprocal);
                b = Blend(blue, b, effective, inverse, half, reciprocal);
                Join(r, g, b, a, transpose).CopyTo(block);
            }
            else
            {
                // The paint with the alpha of its coverage, where that alpha is not 0
                var resultAlpha = Vector256.ShiftRightLogical((blockCoverage * alpha) + halfCoverage, 16);
                var painted = Join(red, green, blue, resultAlpha, transpose);
                var unchanged = Vector256.Equals(resultAlpha, Vector256<uint>.Zero).AsInt32();
                var keep = Avx2.Shuffle(Avx2.PackSignedSaturate(Avx2.PackSignedSaturate(unchanged, unchanged), Avx2.PackSignedSaturate(unchanged, unchanged)).AsByte(), transpose);
                Vector256.ConditionalSelect(keep, pixels, painted).CopyTo(block);
            }

            pending = i + 8;
        }

        if (pending < coverage.Length)
        {
            CompositeOver8Range(samples, coverage, source, pending, coverage.Length);
        }

        static Vector256<uint> Blend(Vector256<uint> paint, Vector256<uint> pixel, Vector256<uint> effective, Vector256<uint> inverse, Vector256<uint> half, Vector256<uint> reciprocal)
        {
            var x = (paint * effective) + (pixel * inverse) + half;
            return Vector256.ShiftRightLogical(Vector256.ShiftRightLogical(x, 16) * reciprocal, 23);
        }

        // The samples of the pixels of each half, one channel per vector: the pixels 0 to 3, then 4 to 7
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Split(Vector256<byte> pixels, Vector256<byte> transpose, out Vector256<uint> red, out Vector256<uint> green, out Vector256<uint> blue, out Vector256<uint> alpha)
        {
            var planar = Avx2.Shuffle(pixels, transpose);
            var low = Avx2.UnpackLow(planar, Vector256<byte>.Zero).AsUInt16();
            var high = Avx2.UnpackHigh(planar, Vector256<byte>.Zero).AsUInt16();
            red = Avx2.UnpackLow(low, Vector256<ushort>.Zero).AsUInt32();
            green = Avx2.UnpackHigh(low, Vector256<ushort>.Zero).AsUInt32();
            blue = Avx2.UnpackLow(high, Vector256<ushort>.Zero).AsUInt32();
            alpha = Avx2.UnpackHigh(high, Vector256<ushort>.Zero).AsUInt32();
        }

        // The inverse of Split, for samples up to 255
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<byte> Join(Vector256<uint> red, Vector256<uint> green, Vector256<uint> blue, Vector256<uint> alpha, Vector256<byte> transpose)
        {
            var low = Avx2.PackUnsignedSaturate(red.AsInt32(), green.AsInt32()).AsInt16();
            var high = Avx2.PackUnsignedSaturate(blue.AsInt32(), alpha.AsInt32()).AsInt16();
            return Avx2.Shuffle(Avx2.PackUnsignedSaturate(low, high), transpose);
        }
    }

    /// <summary>
    /// Composites a paint over pixels of four 8-bit samples (color then alpha) with source-over, with the formulas of
    /// <see cref="CompositeOver8"/>, sixteen pixels at a time with AVX-512 when they are all opaque or all transparent; the
    /// other pixels go through <see cref="CompositeOver8Avx2"/>. Each 128-bit lane of a vector holds four pixels, as a
    /// vector of <see cref="CompositeOver8"/> does: the shuffles and the packs stay within the lanes.
    /// </summary>
    internal static void CompositeOver8Avx512(Span<byte> samples, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
    {
        const uint Scale = byte.MaxValue * CoverageRasterizer.FullCoverage;
        var colors = source.Colors;
        var color = source.Color;
        var transpose128 = Vector128.Create((byte)0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15);
        var transpose = Vector512.Create(Vector256.Create(transpose128, transpose128), Vector256.Create(transpose128, transpose128));
        var colorMask = Vector512.Create(0x00FFFFFFu).AsByte();
        var half = Vector512.Create(Scale / 2);
        var halfCoverage = Vector512.Create(CoverageRasterizer.FullCoverage / 2);
        var scale = Vector512.Create(Scale);
        var reciprocal = Vector512.Create(32897u);
        var red = Vector512.Create((uint)color.C0);
        var green = Vector512.Create((uint)color.C1);
        var blue = Vector512.Create((uint)color.C2);
        var alpha = Vector512.Create((uint)color.Alpha);

        // The pixels from 'pending' on are not composited yet
        var pending = 0;
        for (var i = 0; i + 16 <= coverage.Length; i += 16)
        {
            // Pixels without coverage stay as they are: the run of them is skipped up to the block of the next covered pixel
            var blockCoverage = Vector512.Create(coverage.Slice(i, 16));
            if (blockCoverage == Vector512<uint>.Zero)
            {
                if (pending < i)
                {
                    CompositeOver8Avx2Range(samples, coverage, source, pending, i);
                }

                var next = coverage[(i + 16)..].IndexOfAnyExcept(0u);
                i += (next < 0 ? coverage.Length - i - 16 : next) & ~15;
                pending = i + 16;
                continue;
            }

            var block = samples.Slice(i * 4, 64);
            var pixels = Vector512.Create(block);
            var isOpaque = (pixels | colorMask) == Vector512<byte>.AllBitsSet;
            if (!isOpaque && Vector512.AndNot(pixels, colorMask) != Vector512<byte>.Zero)
                continue;

            if (pending < i)
            {
                CompositeOver8Avx2Range(samples, coverage, source, pending, i);
            }

            if (!colors.IsEmpty)
            {
                // The paint of the sixteen pixels, in the order of the pixels (its samples are 8-bit)
                var paint = Vector512.Narrow(Vector512.Create(colors.Slice(i * 4, 32)), Vector512.Create(colors.Slice((i * 4) + 32, 32)));
                Split(paint, transpose, out red, out green, out blue, out alpha);
            }

            Split(pixels, transpose, out var r, out var g, out var b, out var a);
            if (isOpaque)
            {
                var effective = blockCoverage * alpha;
                var inverse = scale - effective;
                r = Blend(red, r, effective, inverse, half, reciprocal);
                g = Blend(green, g, effective, inverse, half, reciprocal);
                b = Blend(blue, b, effective, inverse, half, reciprocal);
                Join(r, g, b, a, transpose).CopyTo(block);
            }
            else
            {
                // The paint with the alpha of its coverage, where that alpha is not 0
                var resultAlpha = Vector512.ShiftRightLogical((blockCoverage * alpha) + halfCoverage, 16);
                var painted = Join(red, green, blue, resultAlpha, transpose);
                var unchanged = Vector512.Equals(resultAlpha, Vector512<uint>.Zero).AsInt32();
                var words = Avx512BW.PackSignedSaturate(unchanged, unchanged);
                var keep = Avx512BW.Shuffle(Avx512BW.PackSignedSaturate(words, words).AsByte(), transpose);
                Vector512.ConditionalSelect(keep, pixels, painted).CopyTo(block);
            }

            pending = i + 16;
        }

        if (pending < coverage.Length)
        {
            CompositeOver8Avx2Range(samples, coverage, source, pending, coverage.Length);
        }

        static Vector512<uint> Blend(Vector512<uint> paint, Vector512<uint> pixel, Vector512<uint> effective, Vector512<uint> inverse, Vector512<uint> half, Vector512<uint> reciprocal)
        {
            var x = (paint * effective) + (pixel * inverse) + half;
            return Vector512.ShiftRightLogical(Vector512.ShiftRightLogical(x, 16) * reciprocal, 23);
        }

        // The samples of the pixels of each lane, one channel per vector
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Split(Vector512<byte> pixels, Vector512<byte> transpose, out Vector512<uint> red, out Vector512<uint> green, out Vector512<uint> blue, out Vector512<uint> alpha)
        {
            var planar = Avx512BW.Shuffle(pixels, transpose);
            var low = Avx512BW.UnpackLow(planar, Vector512<byte>.Zero).AsUInt16();
            var high = Avx512BW.UnpackHigh(planar, Vector512<byte>.Zero).AsUInt16();
            red = Avx512BW.UnpackLow(low, Vector512<ushort>.Zero).AsUInt32();
            green = Avx512BW.UnpackHigh(low, Vector512<ushort>.Zero).AsUInt32();
            blue = Avx512BW.UnpackLow(high, Vector512<ushort>.Zero).AsUInt32();
            alpha = Avx512BW.UnpackHigh(high, Vector512<ushort>.Zero).AsUInt32();
        }

        // The inverse of Split, for samples up to 255
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector512<byte> Join(Vector512<uint> red, Vector512<uint> green, Vector512<uint> blue, Vector512<uint> alpha, Vector512<byte> transpose)
        {
            var low = Avx512BW.PackUnsignedSaturate(red.AsInt32(), green.AsInt32()).AsInt16();
            var high = Avx512BW.PackUnsignedSaturate(blue.AsInt32(), alpha.AsInt32()).AsInt16();
            return Avx512BW.Shuffle(Avx512BW.PackUnsignedSaturate(low, high), transpose);
        }
    }

    private static void CompositeOver8Avx2Range(Span<byte> samples, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, int start, int end)
    {
        var range = samples[(start * 4)..(end * 4)];
        if (source.Colors.IsEmpty)
        {
            CompositeOver8Avx2(range, coverage[start..end], source);
        }
        else
        {
            CompositeOver8Avx2(range, coverage[start..end], new DrawingSource(source.Colors[(start * 4)..(end * 4)]));
        }
    }

    private static void CompositeOver8Range(Span<byte> samples, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, int start, int end)
    {
        var range = samples[(start * 4)..(end * 4)];
        if (source.Colors.IsEmpty)
        {
            CompositeOver8(range, coverage[start..end], source);
        }
        else
        {
            CompositeOver8(range, coverage[start..end], new DrawingSource(source.Colors[(start * 4)..(end * 4)]));
        }
    }

    private static void CompositeOverScalarRange(Span<byte> samples, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, int start, int end)
    {
        var range = samples[(start * 4)..(end * 4)];
        if (source.Colors.IsEmpty)
        {
            CompositeOverScalar(range, 4, coverage[start..end], source);
        }
        else
        {
            CompositeOverScalar(range, 4, coverage[start..end], new DrawingSource(source.Colors[(start * 4)..(end * 4)]));
        }
    }

    private static void CompositeOverScalarRange16(Span<ushort> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, int start, int end)
    {
        var range = samples[(start * channels)..(end * channels)];
        if (source.Colors.IsEmpty)
        {
            CompositeOverScalar(range, channels, coverage[start..end], source);
        }
        else
        {
            CompositeOverScalar(range, channels, coverage[start..end], new DrawingSource(source.Colors[(start * 4)..(end * 4)]));
        }
    }

    /// <summary>
    /// Composites a paint over pixels of 16-bit samples with source-over, four samples (color then alpha) or one (gray)
    /// per pixel: two pixels at a time when they are opaque, in double precision, the others with
    /// <see cref="CompositeOverScalar"/>. The pairs of pixels without coverage are skipped, as the scalar path leaves
    /// such pixels as they are.
    /// </summary>
    /// <remarks>
    /// On an opaque pixel the color is <c>floor(x / S)</c> with <c>x = sc E + dc (S - E) + S / 2</c>, <c>S = 65535 Q</c>
    /// and <c>E = sa q &lt;= S &lt; 2^32</c>, and the alpha stays 65535; that formula also leaves the pixel as it is when
    /// <c>E</c> is 0 and writes the paint when <c>E</c> is <c>S</c>, as the scalar path does. Each product is below 2^48
    /// and <c>x</c> below 2^49: a double holds them exactly. <c>x / S = k + m / S</c> with integers <c>k &lt; 65536</c> and
    /// <c>0 &lt;= m &lt; S</c>, so that its fraction is 0 or between <c>1 / S &gt; 2^-32</c> and <c>1 - 1 / S</c>. The
    /// product of <c>x</c> by the reciprocal of <c>S</c> (both rounded) is within <c>2^-36</c> of <c>x / S</c>, and adding
    /// <c>2^-34 - 1/2</c> rounds within <c>2^-37</c>: the sum is strictly between <c>k - 1/2</c> and <c>k + 1/2</c>, which
    /// adding 2^52 rounds to <c>k</c>, in the low bits of the double.
    /// </remarks>
    internal static void CompositeOver16(Span<ushort> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
    {
        const double Scale = (double)ushort.MaxValue * CoverageRasterizer.FullCoverage;
        var colors = source.Colors;
        var color = source.Color;
        var half = Vector128.Create(Scale / 2);
        var reciprocal = Vector128.Create(1 / Scale);
        var offset = Vector128.Create(0.5 - (1.0 / (1L << 34)));
        var magic = Vector128.Create(4503599627370496.0);
        var magicBits = Vector128.Create(0x4330000000000000UL);
        double paintAlpha = color.Alpha;

        // The pixels from 'pending' on are not composited yet
        var pending = 0;
        if (channels == 4)
        {
            var alphaMask = Vector128.Create((ushort)0, 0, 0, ushort.MaxValue, 0, 0, 0, ushort.MaxValue);
            var paintLow = Vector128.Create((double)color.C0, color.C1);
            var paintHigh = Vector128.Create((double)color.C2, color.Alpha);
            for (var i = 0; i + 2 <= coverage.Length; i += 2)
            {
                var q0 = coverage[i];
                var q1 = coverage[i + 1];
                if ((q0 | q1) == 0)
                {
                    // Pixels without coverage stay as they are: the run of them is skipped up to the pair of the next covered pixel
                    if (pending < i)
                    {
                        CompositeOverScalarRange16(samples, 4, coverage, source, pending, i);
                    }

                    var next = coverage[(i + 2)..].IndexOfAnyExcept(0u);
                    i += (next < 0 ? coverage.Length - i - 2 : next) & ~1;
                    pending = i + 2;
                    continue;
                }

                var block = samples.Slice(i * 4, 8);
                var pixels = Vector128.Create(block);
                if ((pixels & alphaMask) != alphaMask)
                    continue;

                if (pending < i)
                {
                    CompositeOverScalarRange16(samples, 4, coverage, source, pending, i);
                }

                if (Vector512.IsHardwareAccelerated)
                {
                    // AVX-512: the two pixels in one vector (a vector of 256 bits per pixel is slower than two of 128 bits with AVX2)
                    var pairEffective0 = colors.IsEmpty ? paintAlpha * q0 : colors[(i * 4) + 3] * (double)q0;
                    var pairEffective1 = colors.IsEmpty ? paintAlpha * q1 : colors[(i * 4) + 7] * (double)q1;
                    var paintSamples = colors.IsEmpty ? Vector128.Create(color.C0, color.C1, color.C2, color.Alpha, color.C0, color.C1, color.C2, color.Alpha) : Vector128.Create(colors.Slice(i * 4, 8));
                    var effective = Vector512.Create(Vector256.Create(pairEffective0), Vector256.Create(pairEffective1));
                    var inverse = Vector512.Create(Vector256.Create(Scale - pairEffective0), Vector256.Create(Scale - pairEffective1));
                    var bits = Blend512(ToDouble512(paintSamples), ToDouble512(pixels), effective, inverse);
                    var narrowed = Vector256.Narrow(bits.GetLower(), bits.GetUpper());
                    (Vector128.Narrow(narrowed.GetLower(), narrowed.GetUpper()) | alphaMask).CopyTo(block);
                    pending = i + 2;
                    continue;
                }

                var alpha0 = paintAlpha;
                var alpha1 = paintAlpha;
                var paint0Low = paintLow;
                var paint0High = paintHigh;
                var paint1Low = paintLow;
                var paint1High = paintHigh;
                if (!colors.IsEmpty)
                {
                    var paint = Vector128.Create(colors.Slice(i * 4, 8));
                    var paint0 = Vector128.WidenLower(paint);
                    var paint1 = Vector128.WidenUpper(paint);
                    paint0Low = ToDouble(Vector128.WidenLower(paint0), magicBits, magic);
                    paint0High = ToDouble(Vector128.WidenUpper(paint0), magicBits, magic);
                    paint1Low = ToDouble(Vector128.WidenLower(paint1), magicBits, magic);
                    paint1High = ToDouble(Vector128.WidenUpper(paint1), magicBits, magic);
                    alpha0 = colors[(i * 4) + 3];
                    alpha1 = colors[(i * 4) + 7];
                }

                var pixel0 = Vector128.WidenLower(pixels);
                var pixel1 = Vector128.WidenUpper(pixels);
                var effective0 = alpha0 * q0;
                var effective1 = alpha1 * q1;
                var e0 = Vector128.Create(effective0);
                var e1 = Vector128.Create(effective1);
                var inverse0 = Vector128.Create(Scale - effective0);
                var inverse1 = Vector128.Create(Scale - effective1);
                var low0 = Blend(paint0Low, ToDouble(Vector128.WidenLower(pixel0), magicBits, magic), e0, inverse0, half, reciprocal, offset, magic);
                var high0 = Blend(paint0High, ToDouble(Vector128.WidenUpper(pixel0), magicBits, magic), e0, inverse0, half, reciprocal, offset, magic);
                var low1 = Blend(paint1Low, ToDouble(Vector128.WidenLower(pixel1), magicBits, magic), e1, inverse1, half, reciprocal, offset, magic);
                var high1 = Blend(paint1High, ToDouble(Vector128.WidenUpper(pixel1), magicBits, magic), e1, inverse1, half, reciprocal, offset, magic);
                var result = Vector128.Narrow(Vector128.Narrow(low0, high0), Vector128.Narrow(low1, high1));
                (result | alphaMask).CopyTo(block);
                pending = i + 2;
            }
        }
        else
        {
            // Gray pixels are opaque: two of them per vector
            var gray = Vector128.Create((double)color.C0);
            var scale = Vector128.Create(Scale);
            for (var i = 0; i + 2 <= coverage.Length; i += 2)
            {
                var q0 = coverage[i];
                var q1 = coverage[i + 1];
                if ((q0 | q1) == 0)
                    continue;

                var paint = gray;
                var alpha0 = paintAlpha;
                var alpha1 = paintAlpha;
                if (!colors.IsEmpty)
                {
                    paint = Vector128.Create((double)colors[i * 4], colors[(i * 4) + 4]);
                    alpha0 = colors[(i * 4) + 3];
                    alpha1 = colors[(i * 4) + 7];
                }

                var effective = Vector128.Create(alpha0 * q0, alpha1 * q1);
                var pixels = Vector128.Create((double)samples[i], samples[i + 1]);
                var result = Blend(paint, pixels, effective, scale - effective, half, reciprocal, offset, magic);
                samples[i] = (ushort)result.GetElement(0);
                samples[i + 1] = (ushort)result.GetElement(1);
            }

            pending = coverage.Length & ~1;
        }

        if (pending < coverage.Length)
        {
            CompositeOverScalarRange16(samples, channels, coverage, source, pending, coverage.Length);
        }

        // The samples of two pixels as doubles, with the bits of 2^52 (see ToDouble)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector512<double> ToDouble512(Vector128<ushort> value)
        {
            var magic = Vector512.Create(4503599627370496.0);
            var words = Vector256.WidenLower(value.ToVector256());
            return (Vector512.WidenLower(words.ToVector512()) | magic.AsUInt64()).AsDouble() - magic;
        }

        // floor(x / S) in the low bits of the result, with the operations of Blend
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector512<ulong> Blend512(Vector512<double> paint, Vector512<double> pixel, Vector512<double> effective, Vector512<double> inverse)
        {
            var x = (paint * effective) + (pixel * inverse) + Vector512.Create(Scale / 2);
            return (((x * Vector512.Create(1 / Scale)) - Vector512.Create(0.5 - (1.0 / (1L << 34)))) + Vector512.Create(4503599627370496.0)).AsUInt64();
        }

        // Integers below 2^32 widened to 64 bits, as doubles: their bits under the exponent of 2^52
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<double> ToDouble(Vector128<ulong> value, Vector128<ulong> magicBits, Vector128<double> magic)
            => System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported ? Vector128.ConvertToDouble(value) : (value | magicBits).AsDouble() - magic;

        // floor(x / S) in the low bits of the result (see the remarks)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<ulong> Blend(Vector128<double> paint, Vector128<double> pixel, Vector128<double> effective, Vector128<double> inverse, Vector128<double> half, Vector128<double> reciprocal, Vector128<double> offset, Vector128<double> magic)
        {
            var x = (paint * effective) + (pixel * inverse) + half;
            return (((x * reciprocal) - offset) + magic).AsUInt64();
        }
    }

    /// <summary>Composites a paint over interleaved samples with source-over, pixel by pixel: the reference of the vectorized paths.</summary>
    internal static void CompositeOverScalar<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        var max = ulong.CreateTruncating(TSample.MaxValue);
        var scale = max * CoverageRasterizer.FullCoverage;
        var half = scale / 2;
        var colorChannels = channels == 4 ? 3 : channels;
        var colors = source.Colors;
        var color = source.Color;
        Span<ulong> paint = [color.C0, color.C1, color.C2];
        ulong paintAlpha = color.Alpha;
        for (var i = 0; i < coverage.Length; i++)
        {
            ulong q = coverage[i];
            if (q == 0)
                continue;

            if (!colors.IsEmpty)
            {
                var entry = colors.Slice(i * 4, 4);
                paint[0] = entry[0];
                paint[1] = entry[1];
                paint[2] = entry[2];
                paintAlpha = entry[3];
            }

            if (paintAlpha == 0)
                continue;

            var pixel = samples.Slice(i * channels, channels);
            if (q == CoverageRasterizer.FullCoverage && paintAlpha == max)
            {
                for (var c = 0; c < colorChannels; c++)
                {
                    pixel[c] = TSample.CreateTruncating(paint[c]);
                }

                if (channels == 4)
                {
                    pixel[3] = TSample.MaxValue;
                }

                continue;
            }

            var effective = paintAlpha * q;
            var inverse = scale - effective;
            var destinationAlpha = channels == 4 ? ulong.CreateTruncating(pixel[3]) : max;
            if (destinationAlpha == max)
            {
                for (var c = 0; c < colorChannels; c++)
                {
                    pixel[c] = TSample.CreateTruncating(((paint[c] * effective) + (ulong.CreateTruncating(pixel[c]) * inverse) + half) / scale);
                }

                continue;
            }

            if (destinationAlpha == 0)
            {
                // The formulas below without the pixel: the color is the paint (the rounding E M / 2 is below the
                // total E M), and the alpha (E M + S / 2) / S is (E + Q / 2) / Q since S = M Q with Q even
                var coverageAlpha = (effective + (CoverageRasterizer.FullCoverage / 2)) / CoverageRasterizer.FullCoverage;
                if (coverageAlpha == 0)
                    continue;

                for (var c = 0; c < 3; c++)
                {
                    pixel[c] = TSample.CreateTruncating(paint[c]);
                }

                pixel[3] = TSample.CreateTruncating(coverageAlpha);
                continue;
            }

            var weight = destinationAlpha * inverse;
            var total = (effective * max) + weight;
            var alpha = (total + half) / scale;
            if (alpha == 0)
                continue;

            // Half of an odd total rounds down, which is still "to nearest": no quotient is half way then
            var rounding = total / 2;
            var sourceWeight = effective * max;
            for (var c = 0; c < 3; c++)
            {
                pixel[c] = TSample.CreateTruncating(((paint[c] * sourceWeight) + (ulong.CreateTruncating(pixel[c]) * weight) + rounding) / total);
            }

            pixel[3] = TSample.CreateTruncating(alpha);
        }
    }

    /// <summary>
    /// Composites a paint whose color is first mixed with the color of each pixel by a blend mode: the paint of each
    /// pixel becomes a color of its own, composited as any paint.
    /// </summary>
    private static void CompositeBlended<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, bool isBlueFirst)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        var max = double.CreateTruncating(TSample.MaxValue);
        var colorChannels = channels == 4 ? 3 : channels;
        var colors = source.Colors;
        var color = source.Color;
        Span<ushort> paint = [color.C0, color.C1, color.C2, color.Alpha];
        Span<double> s = stackalloc double[3];
        Span<double> d = stackalloc double[3];
        Span<double> b = stackalloc double[3];

        // The paint of each pixel of a chunk, composited at once: a pixel only depends on its own samples
        const int ChunkSize = 64;
        Span<ushort> chunkColors = stackalloc ushort[ChunkSize * 4];

        // Where red is among the color samples of a pixel, and where blue is
        var red = isBlueFirst ? 2 : 0;
        var blue = 2 - red;

        // The color of a single paint, computed once
        var isSingle = colors.IsEmpty;
        if (isSingle)
        {
            SetSourceColor(s, paint, colorChannels, red, blue, max);
        }

        for (var start = 0; start < coverage.Length; start += ChunkSize)
        {
            var count = Math.Min(ChunkSize, coverage.Length - start);
            for (var k = 0; k < count; k++)
            {
                var i = start + k;
                if (coverage[i] == 0)
                    continue;

                if (!isSingle)
                {
                    colors.Slice(i * 4, 4).CopyTo(paint);
                }

                var blended = chunkColors.Slice(k * 4, 4);
                paint.CopyTo(blended);
                var pixel = samples.Slice(i * channels, channels);
                var backdrop = channels == 4 ? double.CreateTruncating(pixel[3]) / max : 1;
                if (paint[3] != 0 && backdrop != 0)
                {
                    if (!isSingle)
                    {
                        SetSourceColor(s, paint, colorChannels, red, blue, max);
                    }

                    if (colorChannels == 1)
                    {
                        d[0] = d[1] = d[2] = double.CreateTruncating(pixel[0]) / max;
                    }
                    else
                    {
                        d[0] = double.CreateTruncating(pixel[red]) / max;
                        d[1] = double.CreateTruncating(pixel[1]) / max;
                        d[2] = double.CreateTruncating(pixel[blue]) / max;
                    }

                    BlendFunctions.Blend(source.Blend, s, d, b);
                    if (colorChannels == 1)
                    {
                        blended[0] = ToSample(((1 - backdrop) * s[0]) + (backdrop * b[0]), max);
                    }
                    else
                    {
                        blended[red] = ToSample(((1 - backdrop) * s[0]) + (backdrop * b[0]), max);
                        blended[1] = ToSample(((1 - backdrop) * s[1]) + (backdrop * b[1]), max);
                        blended[blue] = ToSample(((1 - backdrop) * s[2]) + (backdrop * b[2]), max);
                    }
                }
            }

            // The pixels without coverage are not written: their entry is not read
            CompositeCore(samples.Slice(start * channels, count * channels), channels, coverage.Slice(start, count), new DrawingSource(chunkColors[..(count * 4)], source.Mode));
        }

        // Written so that a value that is not a number is zero
        static ushort ToSample(double value, double max)
        {
            var rounded = Math.Floor((value * max) + 0.5 + (1.0 / 1048576));
            return rounded >= max ? (ushort)max : rounded > 0 ? (ushort)rounded : (ushort)0;
        }

        // The color of a paint, from 0 to 1: gray in the three components
        static void SetSourceColor(Span<double> s, ReadOnlySpan<ushort> paint, int colorChannels, int red, int blue, double max)
        {
            if (colorChannels == 1)
            {
                s[0] = s[1] = s[2] = paint[0] / max;
            }
            else
            {
                s[0] = paint[red] / max;
                s[1] = paint[1] / max;
                s[2] = paint[blue] / max;
            }
        }
    }

    /// <summary>Combines a paint with interleaved samples with an operator other than source-over.</summary>
    private static void CompositeWithOperator<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        const ulong Full = CoverageRasterizer.FullCoverage;
        var max = ulong.CreateTruncating(TSample.MaxValue);
        var alphaScale = max * Full;
        var colorScale = max * max * Full;
        var colorChannels = channels == 4 ? 3 : channels;
        var mode = source.Mode;
        var isInert = IsTransparentPaintInert(mode);
        var colors = source.Colors;
        var color = source.Color;
        Span<ulong> paint = [color.C0, color.C1, color.C2];
        ulong paintAlpha = color.Alpha;
        for (var i = 0; i < coverage.Length; i++)
        {
            ulong q = coverage[i];
            if (q == 0)
                continue;

            if (!colors.IsEmpty)
            {
                var entry = colors.Slice(i * 4, 4);
                paint[0] = entry[0];
                paint[1] = entry[1];
                paint[2] = entry[2];
                paintAlpha = entry[3];
            }

            if (paintAlpha == 0 && isInert)
                continue;

            var pixel = samples.Slice(i * channels, channels);
            var destinationAlpha = channels == 4 ? ulong.CreateTruncating(pixel[3]) : max;
            if (mode == CompositeMode.SourceAtop && q == Full && channels >= 3)
            {
                // The weights below, with Q = 1, are Da Pa and Da (M - Pa), and their total Da M: the alpha is kept, and
                // each color is round((P Pa + D (M - Pa)) / M), ties upward, the factor Da (a transparent pixel is kept)
                if (destinationAlpha != 0)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        pixel[c] = TSample.CreateTruncating(((2 * ((paint[c] * paintAlpha) + (ulong.CreateTruncating(pixel[c]) * (max - paintAlpha)))) + max) / (2 * max));
                    }
                }

                continue;
            }

            if (mode == CompositeMode.Plus)
            {
                Add(pixel, channels, colorChannels, q, paint, paintAlpha, destinationAlpha, max);
                continue;
            }

            var (sourceFactor, destinationFactor) = mode switch
            {
                CompositeMode.Source => (max, 0UL),
                CompositeMode.DestinationOver => (max - destinationAlpha, max),
                CompositeMode.SourceIn => (destinationAlpha, 0UL),
                CompositeMode.DestinationIn => (0UL, paintAlpha),
                CompositeMode.SourceOut => (max - destinationAlpha, 0UL),
                CompositeMode.DestinationOut => (0UL, max - paintAlpha),
                CompositeMode.SourceAtop => (destinationAlpha, max - paintAlpha),
                CompositeMode.DestinationAtop => (max - destinationAlpha, paintAlpha),
                CompositeMode.Xor => (max - destinationAlpha, max - paintAlpha),
                CompositeMode.Clear => (0UL, 0UL),
                _ => (max, max - paintAlpha),
            };

            // The weights of the color of the paint and of the color of the pixel in the result, times M^2 Q
            var sourceWeight = q * sourceFactor * paintAlpha;
            var destinationWeight = ((Full - q) * destinationAlpha * max) + (q * destinationFactor * destinationAlpha);
            if (channels != 4)
            {
                // No alpha to store: the color of the result over black
                for (var c = 0; c < colorChannels; c++)
                {
                    pixel[c] = TSample.CreateTruncating(((paint[c] * sourceWeight) + (ulong.CreateTruncating(pixel[c]) * destinationWeight) + (colorScale / 2)) / colorScale);
                }

                continue;
            }

            var total = sourceWeight + destinationWeight;
            var alpha = (total + (alphaScale / 2)) / alphaScale;
            if (alpha == 0)
            {
                if (destinationAlpha != 0)
                {
                    pixel.Clear();
                }

                continue;
            }

            // Half of an odd total rounds down, which is still "to nearest": no quotient is half way then
            var rounding = total / 2;
            for (var c = 0; c < 3; c++)
            {
                pixel[c] = TSample.CreateTruncating(((paint[c] * sourceWeight) + (ulong.CreateTruncating(pixel[c]) * destinationWeight) + rounding) / total);
            }

            pixel[3] = TSample.CreateTruncating(alpha);
        }
    }

    /// <summary>Adds a paint to a pixel, both multiplied by their alpha, the sums clipped to one (<see cref="CompositeMode.Plus"/>).</summary>
    private static void Add<TSample>(Span<TSample> pixel, int channels, int colorChannels, ulong q, ReadOnlySpan<ulong> paint, ulong paintAlpha, ulong destinationAlpha, ulong max)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        const ulong Full = CoverageRasterizer.FullCoverage;
        var kept = Full - q;
        if (channels != 4)
        {
            // An opaque pixel: the color of the result over black
            for (var c = 0; c < colorChannels; c++)
            {
                var destination = ulong.CreateTruncating(pixel[c]);
                var premultiplied = (q * Math.Min((paint[c] * paintAlpha) + (destination * max), max * max)) + (kept * destination * max);
                pixel[c] = TSample.CreateTruncating((premultiplied + (max * Full / 2)) / (max * Full));
            }

            return;
        }

        var total = (q * Math.Min(paintAlpha + destinationAlpha, max)) + (kept * destinationAlpha);
        var alpha = (total + (Full / 2)) / Full;
        if (alpha == 0)
        {
            if (destinationAlpha != 0)
            {
                pixel.Clear();
            }

            return;
        }

        // Each color times the alpha, times M Q; divided by the alpha times M Q, the color itself
        var rounding = total / 2;
        for (var c = 0; c < 3; c++)
        {
            var destination = ulong.CreateTruncating(pixel[c]);
            var premultiplied = (q * Math.Min((paint[c] * paintAlpha) + (destination * destinationAlpha), max * max)) + (kept * destination * destinationAlpha);
            pixel[c] = TSample.CreateTruncating((premultiplied + rounding) / total);
        }

        pixel[3] = TSample.CreateTruncating(alpha);
    }

    /// <summary>Combines a paint with interleaved samples in the linear light their transfer function decodes them to: any blend mode, any operator.</summary>
    private static void CompositeLinear<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source, bool isBlueFirst)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        const double Full = CoverageRasterizer.FullCoverage;
        var maxSample = int.CreateTruncating(TSample.MaxValue);
        var max = (double)maxSample;
        var transfer = source.Transfer;
        var curves = source.Curves;

        // The light of every sample, for red (or gray), green and blue: one function of the transfer characteristics,
        // or the tone curve of each channel of an ICC profile
        ReadOnlySpan<double> decodeRed;
        ReadOnlySpan<double> decodeGreen;
        ReadOnlySpan<double> decodeBlue;
        if (curves is null)
        {
            decodeRed = decodeGreen = decodeBlue = maxSample == byte.MaxValue ? CicpTransfer.GetDecode8(transfer) : CicpTransfer.GetDecode16(transfer);
        }
        else if (maxSample == byte.MaxValue)
        {
            decodeRed = curves.GetDecode8(0);
            decodeGreen = curves.GetDecode8(1);
            decodeBlue = curves.GetDecode8(2);
        }
        else
        {
            decodeRed = curves.GetDecode16(0);
            decodeGreen = curves.GetDecode16(1);
            decodeBlue = curves.GetDecode16(2);
        }

        // The samples of 8-bit light come from the thresholds of its encoding, which give the samples of EncodeLight
        var thresholds = curves is null && maxSample == byte.MaxValue && transfer is not CicpTransfer.Linear && transfer < LinearThresholds8.Length ? GetLinearThresholds8(transfer) : null;

        var colorChannels = channels == 4 ? 3 : channels;
        var mode = source.Mode;
        var blend = source.Blend;
        var isInert = IsTransparentPaintInert(mode);
        var colors = source.Colors;
        var color = source.Color;
        Span<ushort> paint = [color.C0, color.C1, color.C2, color.Alpha];
        Span<double> s = stackalloc double[3];
        Span<double> d = stackalloc double[3];
        Span<double> b = stackalloc double[3];

        // Where red is among the color samples of a pixel, and where blue is
        var red = isBlueFirst ? 2 : 0;
        var blue = 2 - red;
        for (var i = 0; i < coverage.Length; i++)
        {
            var q = coverage[i];
            if (q == 0)
                continue;

            if (!colors.IsEmpty)
            {
                colors.Slice(i * 4, 4).CopyTo(paint);
            }

            int paintAlpha = paint[3];
            if (paintAlpha == 0 && isInert)
                continue;

            var pixel = samples.Slice(i * channels, channels);
            var destinationAlpha = channels == 4 ? int.CreateTruncating(pixel[3]) : maxSample;
            if (q == CoverageRasterizer.FullCoverage && paintAlpha == maxSample && (mode == CompositeMode.Source || (mode == CompositeMode.SourceOver && (blend == BlendMode.Normal || destinationAlpha == 0))))
            {
                // The paint replaces the pixel: its samples, exactly
                for (var c = 0; c < colorChannels; c++)
                {
                    pixel[c] = TSample.CreateTruncating(paint[c]);
                }

                if (channels == 4)
                {
                    pixel[3] = TSample.MaxValue;
                }

                continue;
            }

            if (colorChannels == 1)
            {
                s[0] = s[1] = s[2] = decodeRed[paint[0]];
                d[0] = d[1] = d[2] = decodeRed[int.CreateTruncating(pixel[0])];
            }
            else
            {
                s[0] = decodeRed[paint[red]];
                s[1] = decodeGreen[paint[1]];
                s[2] = decodeBlue[paint[blue]];
                d[0] = decodeRed[int.CreateTruncating(pixel[red])];
                d[1] = decodeGreen[int.CreateTruncating(pixel[1])];
                d[2] = decodeBlue[int.CreateTruncating(pixel[blue])];
            }

            var sourceAlpha = paintAlpha / max;
            var backdrop = destinationAlpha / max;
            if (blend != BlendMode.Normal && paintAlpha != 0 && destinationAlpha != 0)
            {
                BlendFunctions.Blend(blend, s, d, b);
                for (var c = 0; c < 3; c++)
                {
                    s[c] = ((1 - backdrop) * s[c]) + (backdrop * b[c]);
                }
            }

            // The weights of the color of the paint and of the color of the pixel in the result
            var covered = q / Full;
            if (mode == CompositeMode.Plus)
            {
                // The sums are clipped to one, where the shape is
                var sum = Math.Min(sourceAlpha + backdrop, 1);
                var plusAlpha = (covered * sum) + ((1 - covered) * backdrop);
                for (var c = 0; c < 3; c++)
                {
                    b[c] = (covered * Math.Min((s[c] * sourceAlpha) + (d[c] * backdrop), 1)) + ((1 - covered) * d[c] * backdrop);
                }

                if (channels != 4)
                {
                    if (colorChannels == 1)
                    {
                        pixel[0] = ToSample(b[0], max, transfer, curves, 0, thresholds);
                    }
                    else
                    {
                        pixel[red] = ToSample(b[0], max, transfer, curves, 0, thresholds);
                        pixel[1] = ToSample(b[1], max, transfer, curves, 1, thresholds);
                        pixel[blue] = ToSample(b[2], max, transfer, curves, 2, thresholds);
                    }

                    continue;
                }

                var plusRounded = Math.Floor((plusAlpha * max) + 0.5);
                if (!(plusRounded > 0))
                {
                    if (destinationAlpha != 0)
                    {
                        pixel.Clear();
                    }

                    continue;
                }

                pixel[red] = ToSample(b[0] / plusAlpha, max, transfer, curves, 0, thresholds);
                pixel[1] = ToSample(b[1] / plusAlpha, max, transfer, curves, 1, thresholds);
                pixel[blue] = ToSample(b[2] / plusAlpha, max, transfer, curves, 2, thresholds);
                pixel[3] = TSample.CreateTruncating(plusRounded >= max ? max : plusRounded);
                continue;
            }

            var (sourceFactor, destinationFactor) = mode switch
            {
                CompositeMode.Source => (1d, 0d),
                CompositeMode.DestinationOver => (1 - backdrop, 1d),
                CompositeMode.SourceIn => (backdrop, 0d),
                CompositeMode.DestinationIn => (0d, sourceAlpha),
                CompositeMode.SourceOut => (1 - backdrop, 0d),
                CompositeMode.DestinationOut => (0d, 1 - sourceAlpha),
                CompositeMode.SourceAtop => (backdrop, 1 - sourceAlpha),
                CompositeMode.DestinationAtop => (1 - backdrop, sourceAlpha),
                CompositeMode.Xor => (1 - backdrop, 1 - sourceAlpha),
                CompositeMode.Clear => (0d, 0d),
                _ => (1d, 1 - sourceAlpha),
            };

            var sourceWeight = covered * sourceFactor * sourceAlpha;
            var destinationWeight = ((1 - covered) * backdrop) + (covered * destinationFactor * backdrop);
            if (channels != 4)
            {
                // No alpha to store: the color of the result over black
                if (colorChannels == 1)
                {
                    pixel[0] = ToSample((s[0] * sourceWeight) + (d[0] * destinationWeight), max, transfer, curves, 0, thresholds);
                }
                else
                {
                    pixel[red] = ToSample((s[0] * sourceWeight) + (d[0] * destinationWeight), max, transfer, curves, 0, thresholds);
                    pixel[1] = ToSample((s[1] * sourceWeight) + (d[1] * destinationWeight), max, transfer, curves, 1, thresholds);
                    pixel[blue] = ToSample((s[2] * sourceWeight) + (d[2] * destinationWeight), max, transfer, curves, 2, thresholds);
                }

                continue;
            }

            var total = sourceWeight + destinationWeight;
            var alpha = Math.Floor((total * max) + 0.5);
            if (!(alpha > 0))
            {
                if (destinationAlpha != 0)
                {
                    pixel.Clear();
                }

                continue;
            }

            pixel[red] = ToSample(((s[0] * sourceWeight) + (d[0] * destinationWeight)) / total, max, transfer, curves, 0, thresholds);
            pixel[1] = ToSample(((s[1] * sourceWeight) + (d[1] * destinationWeight)) / total, max, transfer, curves, 1, thresholds);
            pixel[blue] = ToSample(((s[2] * sourceWeight) + (d[2] * destinationWeight)) / total, max, transfer, curves, 2, thresholds);
            pixel[3] = TSample.CreateTruncating(alpha >= max ? max : alpha);
        }

        static TSample ToSample(double linear, double max, byte transfer, LinearLightCurves? curves, int channel, QuantizationThresholds? thresholds)
            => thresholds is not null ? TSample.CreateTruncating(thresholds.Quantize(linear)) : TSample.CreateTruncating(EncodeLight(linear, max, transfer, curves, channel));
    }

    /// <summary>Gets the thresholds of <see cref="EncodeLight"/> for 8-bit samples and a function of the transfer characteristics, or <see langword="null"/> when they do not give its samples.</summary>
    internal static QuantizationThresholds? GetLinearThresholds8(byte transfer)
    {
        // The function is created once per transfer characteristics: the thresholds are asked for at every run of pixels
        var encode = Volatile.Read(ref LinearEncoders8[transfer]);
        if (encode is null)
        {
            encode = value => (int)EncodeLight(value, byte.MaxValue, transfer, curves: null, channel: 0);
            encode = Interlocked.CompareExchange(ref LinearEncoders8[transfer], encode, null) ?? encode;
        }

        return QuantizationThresholds.GetOrCreate(LinearThresholds8, transfer, encode, byte.MaxValue);
    }

    /// <summary>Encodes linear light to a sample, from 0 to <paramref name="max"/>: the encoding is clipped to the samples, and a value that is not a number is zero.</summary>
    internal static double EncodeLight(double linear, double max, byte transfer, LinearLightCurves? curves, int channel)
    {
        var encoded = curves is null ? CicpTransfer.Encode(transfer, linear) : curves.Encode(channel, linear);
        var rounded = Math.Floor((encoded * max) + 0.5 + (1.0 / 1048576));
        return rounded >= max ? max : rounded > 0 ? rounded : 0;
    }

    /// <summary>
    /// The samples that a single paint gives the 8-bit pixels covered entirely that have one alpha (or no alpha), when
    /// each color sample of the result only depends on the same sample of the pixel: the 256 values of each color,
    /// computed once with the formulas (<see cref="CompositeWithFormulas"/>), instead of the formulas for every pixel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With a single paint, a full coverage and the alpha of the pixel fixed, every formula of the contract computes a
    /// color of the result from the paint, the alpha of the pixel and the same color of the pixel only, and the alpha of
    /// the result without its colors: the operators, the separable blend modes and linear light alike. The table is the
    /// result of the formulas on a pixel of each value (the three colors of that value), so it holds the samples the
    /// formulas give, exactly. The blend modes that take a color as a whole (<see cref="BlendMode.Hue"/> and after) mix the
    /// colors and get no table.
    /// </para>
    /// <para>
    /// The paths that have no table already cost little per pixel: source-over of the stored samples has vector kernels, and
    /// an operator that replaces the pixel writes the paint. The table of a thread is kept between the calls, for the rows
    /// of a shape and the next shapes with that paint, and built once its paint has been composited over
    /// <see cref="MinPixels"/> pixels, so a paint that changes at every short run does not pay for it.
    /// </para>
    /// </remarks>
    private sealed class SampleTable
    {
        /// <summary>The number of pixels composited with a paint from which its table is built: the table costs about as many pixels.</summary>
        private const int MinPixels = 256;

        [ThreadStatic]
        private static SampleTable? s_current;

        private readonly byte[] _samples = new byte[3 * 256];
        private DrawingColor _color;
        private CompositeMode _mode;
        private BlendMode _blend;
        private byte _transfer;
        private LinearLightCurves? _curves;
        private int _channels;
        private bool _isBlueFirst;
        private byte _destinationAlpha;
        private int _pixels;
        private bool _isBuilt;
        private bool _isUsable;

        /// <summary>Gets the samples of the result for each value of the first, second and third color sample of a pixel, 256 each.</summary>
        public ReadOnlySpan<byte> Samples => _samples;

        /// <summary>Gets the alpha of the result.</summary>
        public byte Alpha { get; private set; }

        /// <summary>Gets a value indicating whether a paint is composited with formulas that give each color of the result from the same color of the pixel, and costly enough to be worth a table.</summary>
        public static bool IsSeparable(scoped in DrawingSource source)
            => source.IsLinearLight || source.Blend != BlendMode.Normal ? source.Blend < BlendMode.Hue : source.Mode is not (CompositeMode.SourceOver or CompositeMode.Source);

        /// <summary>Gets the table of a single paint for the pixels of an alpha, or <see langword="null"/> when it is not built yet or the result has no table.</summary>
        /// <param name="source">The paint.</param>
        /// <param name="channels">The number of samples per pixel.</param>
        /// <param name="isBlueFirst"><see langword="true"/> when the first color sample is blue.</param>
        /// <param name="destinationAlpha">The alpha of the pixels, 255 without alpha.</param>
        /// <param name="length">The number of pixels about to be composited with the paint.</param>
        public static SampleTable? Get(scoped in DrawingSource source, int channels, bool isBlueFirst, byte destinationAlpha, int length)
        {
            var table = s_current ??= new SampleTable();
            if (table._color != source.Color || table._mode != source.Mode || table._blend != source.Blend || table._transfer != source.Transfer || !ReferenceEquals(table._curves, source.Curves) ||
                table._channels != channels || table._isBlueFirst != isBlueFirst || table._destinationAlpha != destinationAlpha)
            {
                table._color = source.Color;
                table._mode = source.Mode;
                table._blend = source.Blend;
                table._transfer = source.Transfer;
                table._curves = source.Curves;
                table._channels = channels;
                table._isBlueFirst = isBlueFirst;
                table._destinationAlpha = destinationAlpha;
                table._pixels = 0;
                table._isBuilt = false;
            }

            if (!table._isBuilt)
            {
                table._pixels += length;
                if (table._pixels < MinPixels)
                    return null;

                table.Build(source);
            }

            return table._isUsable ? table : null;
        }

        /// <summary>Composites the paint over a pixel of each value with the formulas.</summary>
        private void Build(scoped in DrawingSource source)
        {
            const int Values = 256;
            var channels = _channels;
            Span<byte> pixels = stackalloc byte[Values * 4];
            pixels = pixels[..(Values * channels)];
            Span<uint> coverage = stackalloc uint[Values];
            coverage.Fill(CoverageRasterizer.FullCoverage);
            for (var value = 0; value < Values; value++)
            {
                var pixel = pixels.Slice(value * channels, channels);
                pixel.Fill((byte)value);
                if (channels == 4)
                {
                    pixel[3] = _destinationAlpha;
                }
            }

            CompositeWithFormulas(pixels, channels, coverage, source, _isBlueFirst);
            _isBuilt = true;
            Alpha = channels == 4 ? pixels[3] : byte.MaxValue;

            // The alpha of the result does not depend on the colors of the pixel: the table is not used otherwise
            _isUsable = true;
            for (var value = 0; value < Values; value++)
            {
                var pixel = pixels.Slice(value * channels, channels);
                if (channels == 4 && pixel[3] != Alpha)
                {
                    _isUsable = false;
                    return;
                }

                for (var c = 0; c < 3; c++)
                {
                    _samples[(c * Values) + value] = pixel[Math.Min(c, channels - 1)];
                }
            }
        }
    }
}
