using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
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

    /// <summary>Converts a paint color to the pixel format of a frame, with the rules of pixel conversion.</summary>
    /// <remarks>
    /// The color samples are those of the opaque color converted by <see cref="PixelConverter"/> (Rec. 709 luma for
    /// grayscale formats, 16-to-8-bit rounding last); the opacity is the alpha of the color at the precision of the format.
    /// </remarks>
    public static DrawingColor ConvertColor<TPixel>(Rgba64 color)
        where TPixel : unmanaged
    {
        var opaque = new Rgba64(color.R, color.G, color.B);
        TPixel pixel = default;
        PixelConverter.ConvertRow(new ReadOnlySpan<Rgba64>(in opaque), new Span<TPixel>(ref pixel));
        if (typeof(TPixel) == typeof(Rgba64))
        {
            var value = unsafe(Unsafe.As<TPixel, Rgba64>(ref pixel));
            return new DrawingColor(value.R, value.G, value.B, color.A);
        }

        if (typeof(TPixel) == typeof(Gray16))
            return new DrawingColor(unsafe(Unsafe.As<TPixel, Gray16>(ref pixel)).Value, 0, 0, color.A);

        var alpha = PixelConverter.To8Bit(color.A);
        if (typeof(TPixel) == typeof(Rgba32))
        {
            var value = unsafe(Unsafe.As<TPixel, Rgba32>(ref pixel));
            return new DrawingColor(value.R, value.G, value.B, alpha);
        }

        if (typeof(TPixel) == typeof(Bgra32))
        {
            var value = unsafe(Unsafe.As<TPixel, Bgra32>(ref pixel));
            return new DrawingColor(value.B, value.G, value.R, alpha);
        }

        if (typeof(TPixel) == typeof(Rgb24))
        {
            var value = unsafe(Unsafe.As<TPixel, Rgb24>(ref pixel));
            return new DrawingColor(value.R, value.G, value.B, alpha);
        }

        if (typeof(TPixel) == typeof(Gray8))
            return new DrawingColor(unsafe(Unsafe.As<TPixel, Gray8>(ref pixel)).Value, 0, 0, alpha);

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
            for (var i = 0; i < count; i++)
            {
                var color = chunk[i];
                opaque[i] = new Rgba64(color.R, color.G, color.B);
            }

            PixelConverter.ConvertRow<Rgba64, TPixel>(opaque[..count], pixels);
            var entries = destination.Slice(start * 4, count * 4);
            if (typeof(TPixel) == typeof(Rgba64))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, Rgba64>(pixels));
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
            else if (typeof(TPixel) == typeof(Rgba32))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, Rgba32>(pixels));
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
            else if (typeof(TPixel) == typeof(Bgra32))
            {
                var values = unsafe(MemoryMarshal.Cast<TPixel, Bgra32>(pixels));
                for (var i = 0; i < count; i++)
                {
                    var value = values[i];
                    var entry = entries.Slice(i * 4, 4);
                    entry[0] = value.B;
                    entry[1] = value.G;
                    entry[2] = value.R;
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
            else
            {
                throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
            }
        }
    }

    /// <summary>Reads pixels of a row as 16-bit colors, with the rules of pixel conversion.</summary>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="destination">The colors; as many pixels are read as it holds.</param>
    public static void ReadColors<TPixel>(PixelStorage storage, int y, int x, Span<Rgba64> destination)
        where TPixel : unmanaged
    {
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var row = unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y))).Slice(x, destination.Length);
        PixelConverter.ConvertRow<TPixel, Rgba64>(row, destination);
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
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
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
    private static void CompositeOver<TSample>(Span<TSample> samples, int channels, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
    {
        if (typeof(TSample) == typeof(byte) && channels == 4 && coverage.Length >= 4 && Vector128.IsHardwareAccelerated)
        {
            CompositeOver8(unsafe(MemoryMarshal.Cast<TSample, byte>(samples)), coverage, source);
            return;
        }

        CompositeOverScalar(samples, channels, coverage, source);
    }

    /// <summary>
    /// Composites a paint over pixels of four 8-bit samples (color then alpha) with source-over: four pixels at a time
    /// when they are all opaque or all transparent, the others with <see cref="CompositeOverScalar"/>.
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

                var effective = Vector128.Create(coverage.Slice(i, 4)) * alpha;
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
                var resultAlpha = Vector128.ShiftRightLogical((Vector128.Create(coverage.Slice(i, 4)) * alpha) + halfCoverage, 16);
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
        for (var start = 0; start < coverage.Length; start += ChunkSize)
        {
            var count = Math.Min(ChunkSize, coverage.Length - start);
            for (var k = 0; k < count; k++)
            {
                var i = start + k;
                if (coverage[i] == 0)
                    continue;

                if (!colors.IsEmpty)
                {
                    colors.Slice(i * 4, 4).CopyTo(paint);
                }

                var blended = chunkColors.Slice(k * 4, 4);
                paint.CopyTo(blended);
                var pixel = samples.Slice(i * channels, channels);
                var backdrop = channels == 4 ? double.CreateTruncating(pixel[3]) / max : 1;
                if (paint[3] != 0 && backdrop != 0)
                {
                    if (colorChannels == 1)
                    {
                        s[0] = s[1] = s[2] = paint[0] / max;
                        d[0] = d[1] = d[2] = double.CreateTruncating(pixel[0]) / max;
                    }
                    else
                    {
                        s[0] = paint[red] / max;
                        s[1] = paint[1] / max;
                        s[2] = paint[blue] / max;
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
        => QuantizationThresholds.GetOrCreate(LinearThresholds8, transfer, value => (int)EncodeLight(value, byte.MaxValue, transfer, curves: null, channel: 0), byte.MaxValue);

    /// <summary>Encodes linear light to a sample, from 0 to <paramref name="max"/>: the encoding is clipped to the samples, and a value that is not a number is zero.</summary>
    internal static double EncodeLight(double linear, double max, byte transfer, LinearLightCurves? curves, int channel)
    {
        var encoded = curves is null ? CicpTransfer.Encode(transfer, linear) : curves.Encode(channel, linear);
        var rounded = Math.Floor((encoded * max) + 0.5 + (1.0 / 1048576));
        return rounded >= max ? max : rounded > 0 ? rounded : 0;
    }
}
