using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Composites paints over the pixels of a frame of a floating-point format (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>,
/// <see cref="GrayFloat"/>, <see cref="GrayHalf"/>, <see cref="LaFloat"/>, <see cref="LaHalf"/>), and applies color
/// matrices to them: the formulas of <see cref="DrawingKernels"/> and <see cref="ColorMatrixKernel"/>
/// in double precision, on the values of the samples, without rounding them to a grid and without clipping the colors.
/// </summary>
/// <remarks>
/// <para>
/// The paints of a floating-point frame have 16-bit samples (<see cref="DrawingColor"/>, <c>M = 65535</c>), which are
/// fractions <c>s = v / M</c>; the pixels are read as they are stored. With a coverage <c>k = q / Q</c>, a paint
/// <c>(s, sa)</c> and a pixel <c>(d, da)</c>, the factors <c>fa</c> and <c>fb</c> of the operator as fractions of one,
/// <c>S = k fa sa</c> and <c>D = (1 - k) da + k fb da</c>: the alpha is <c>A = S + D</c> and each color
/// <c>(s S + d D) / A</c>, stored to the nearest value of the format. When <c>A</c> is not positive the pixel becomes
/// transparent black, or is not written if it was transparent. A full coverage with an opaque paint that replaces the
/// pixel writes the paint (<c>v / M</c>, and an alpha of 1).
/// </para>
/// <para>
/// A gray pixel is read as its level in the three colors, and stores the gray level of the result
/// (<see cref="PixelConverter.GetGrayLevel"/>: its level when the three colors are equal, else their Rec. 709 luma). A
/// pixel without alpha (<see cref="GrayFloat"/>, <see cref="GrayHalf"/>) is opaque, and stores the colors of the result
/// over black, <c>s S + d D</c>, as the integer formats without alpha do.
/// </para>
/// <para>
/// <see cref="CompositeMode.Plus"/> adds light: the alpha is <c>k min(sa + da, 1) + (1 - k) da</c>, and the colors
/// times their alpha are added without the clipping of the integer formats, so that a highlight stays a highlight.
/// A <see cref="BlendMode"/> other than normal replaces the color of the paint by <c>(1 - da) s + da B(s, d)</c> first,
/// with the functions of <see cref="BlendFunctions"/> applied to the values as they are.
/// </para>
/// <para>
/// In linear light (<see cref="DrawingSource.Transfer"/>), the samples of the paint and of the pixel are decoded by the
/// function of the transfer characteristics and the result is encoded again: only samples in [0, 1] have a linear
/// value, so a pixel outside that range is an <see cref="UnsupportedImageFeatureException"/>, and so are the tone curves
/// of an ICC profile (<see cref="DrawingSource.Curves"/>), which are tables of 16-bit samples. A floating-point image
/// labeled as linear light is combined as it is stored: its samples are its light.
/// </para>
/// </remarks>
internal static class FloatDrawingKernels
{
    private const double PaintMax = ushort.MaxValue;

    private static readonly AsyncLocal<bool> TestForceGeneralValue = new();

    /// <summary>
    /// Gets or sets a value indicating whether the current asynchronous flow composites every paint with the general loop of
    /// <see cref="Composite{TPixel}"/>. Tests compare it with the loop of a solid color bit for bit.
    /// </summary>
    internal static bool TestForceGeneral
    {
        get => TestForceGeneralValue.Value;
        set => TestForceGeneralValue.Value = value;
    }

    /// <summary>Composites a paint over a run of pixels of a row of a floating-point frame.</summary>
    /// <typeparam name="TPixel">A floating-point pixel type.</typeparam>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="coverage">The coverage of each pixel of the run.</param>
    /// <param name="source">The paint, with 16-bit samples.</param>
    /// <exception cref="UnsupportedImageFeatureException">The paint is combined in linear light with pixels that have no linear value.</exception>
    public static void Composite<TPixel>(PixelStorage storage, int y, int x, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        where TPixel : unmanaged
    {
        const double Full = CoverageRasterizer.FullCoverage;
        var pixels = unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y))).Slice(x, coverage.Length);
        var transfer = source.Transfer;
        var isLinear = source.IsLinearLight;
        if (source.Curves is not null)
            throw new UnsupportedImageFeatureException("A floating-point image is not combined in the linear light of the tone curves of its ICC profile: label it as linear light (ColorTransferFunction.Linear), or draw in its encoded values.", ImageFormat.Unknown, "Linear light of floating-point pixels with an ICC profile");

        var mode = source.Mode;
        var blend = source.Blend;
        if (typeof(TPixel) == typeof(RgbaFloat) && !isLinear && mode == CompositeMode.SourceOver && blend == BlendMode.Normal && source.Colors.IsEmpty && source.FloatColors.IsEmpty && !TestForceGeneral)
        {
            CompositeSolidOver(unsafe(MemoryMarshal.Cast<TPixel, RgbaFloat>(pixels)), coverage, source.Color);
            return;
        }

        var decode = isLinear ? CicpTransfer.GetDecode16(transfer) : default;
        var isInert = DrawingKernels.IsTransparentPaintInert(mode);
        var colors = source.Colors;
        var floatColors = source.FloatColors;
        var color = source.Color;
        Span<ushort> paint = [color.C0, color.C1, color.C2, color.Alpha];
        Span<double> s = stackalloc double[3];
        Span<double> d = stackalloc double[3];
        Span<double> b = stackalloc double[3];
        for (var i = 0; i < coverage.Length; i++)
        {
            var q = coverage[i];
            if (q == 0)
                continue;

            if (!colors.IsEmpty)
            {
                colors.Slice(i * 4, 4).CopyTo(paint);
            }

            var paintAlpha = floatColors.IsEmpty ? paint[3] / PaintMax : floatColors[(i * 4) + 3];
            if (paintAlpha == 0 && isInert)
                continue;

            ref var pixel = ref pixels[i];
            Read(ref pixel, out d[0], out d[1], out d[2], out var destinationAlpha);
            if (q == CoverageRasterizer.FullCoverage && paintAlpha == 1 && (mode == CompositeMode.Source || (mode == CompositeMode.SourceOver && (blend == BlendMode.Normal || destinationAlpha == 0))))
            {
                // The paint replaces the pixel: its samples, exactly
                if (floatColors.IsEmpty)
                    Write(ref pixel, paint[0] / PaintMax, paint[1] / PaintMax, paint[2] / PaintMax, 1);
                else
                    Write(ref pixel, floatColors[i * 4], floatColors[(i * 4) + 1], floatColors[(i * 4) + 2], 1);
                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                if (isLinear)
                {
                    s[c] = floatColors.IsEmpty ? decode[paint[c]] : DecodePixel(transfer, floatColors[(i * 4) + c]);
                    d[c] = DecodePixel(transfer, d[c]);
                }
                else
                {
                    s[c] = floatColors.IsEmpty ? paint[c] / PaintMax : floatColors[(i * 4) + c];
                }
            }

            var sourceAlpha = paintAlpha;
            var backdrop = destinationAlpha;
            if (blend != BlendMode.Normal && paintAlpha != 0 && destinationAlpha != 0)
            {
                BlendFunctions.Blend(blend, s, d, b);
                for (var c = 0; c < 3; c++)
                {
                    s[c] = ((1 - backdrop) * s[c]) + (backdrop * b[c]);
                }
            }

            var covered = q / Full;
            double total;
            if (mode == CompositeMode.Plus)
            {
                total = (covered * Math.Min(sourceAlpha + backdrop, 1)) + ((1 - covered) * backdrop);
                for (var c = 0; c < 3; c++)
                {
                    b[c] = (covered * ((s[c] * sourceAlpha) + (d[c] * backdrop))) + ((1 - covered) * d[c] * backdrop);
                }
            }
            else
            {
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
                total = sourceWeight + destinationWeight;
                for (var c = 0; c < 3; c++)
                {
                    b[c] = (s[c] * sourceWeight) + (d[c] * destinationWeight);
                }
            }

            if (!HasAlpha<TPixel>())
            {
                // No alpha to store: the colors of the result over black
                for (var c = 0; c < 3; c++)
                {
                    if (isLinear)
                    {
                        b[c] = CicpTransfer.Encode(transfer, b[c]);
                    }
                }

                Write(ref pixel, b[0], b[1], b[2], 1);
                continue;
            }

            if (!(total > 0))
            {
                if (destinationAlpha != 0)
                {
                    pixel = default;
                }

                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                b[c] /= total;
                if (isLinear)
                {
                    b[c] = CicpTransfer.Encode(transfer, b[c]);
                }
            }

            Write(ref pixel, b[0], b[1], b[2], Math.Min(total, 1));
        }
    }

    /// <summary>
    /// Composites one color with <see cref="CompositeMode.SourceOver"/> and <see cref="BlendMode.Normal"/> over
    /// <see cref="RgbaFloat"/> pixels in their encoded values: the operations of <see cref="Composite{TPixel}"/>, in the
    /// same order, without what the other paints, operators and blend modes need.
    /// </summary>
    private static void CompositeSolidOver(Span<RgbaFloat> pixels, ReadOnlySpan<uint> coverage, DrawingColor color)
    {
        const double Full = CoverageRasterizer.FullCoverage;
        var sourceAlpha = color.Alpha / PaintMax;
        if (sourceAlpha == 0)
            return;

        var s0 = color.C0 / PaintMax;
        var s1 = color.C1 / PaintMax;
        var s2 = color.C2 / PaintMax;
        var opaque = new RgbaFloat((float)s0, (float)s1, (float)s2, 1);
        var destinationFactor = 1 - sourceAlpha;
        for (var i = 0; i < coverage.Length; i++)
        {
            var q = coverage[i];
            if (q == 0)
                continue;

            ref var pixel = ref pixels[i];
            if (q == CoverageRasterizer.FullCoverage && sourceAlpha == 1)
            {
                pixel = opaque;
                continue;
            }

            double backdrop = pixel.A;
            var covered = q / Full;
            var sourceWeight = covered * sourceAlpha;
            var destinationWeight = ((1 - covered) * backdrop) + (covered * destinationFactor * backdrop);
            var total = sourceWeight + destinationWeight;
            if (!(total > 0))
            {
                if (backdrop != 0)
                {
                    pixel = default;
                }

                continue;
            }

            var r = ((s0 * sourceWeight) + (pixel.R * destinationWeight)) / total;
            var g = ((s1 * sourceWeight) + (pixel.G * destinationWeight)) / total;
            var b = ((s2 * sourceWeight) + (pixel.B * destinationWeight)) / total;
            pixel = new RgbaFloat((float)r, (float)g, (float)b, (float)Math.Min(total, 1));
        }
    }

    /// <summary>
    /// Applies a color matrix to a run of pixels of a floating-point frame, in place: each component is
    /// <c>(((m0 R + m1 G) + m2 B) + m3 A) + m4</c> in double precision, the colors are not clipped and the alpha is kept
    /// between 0 and 1 (a value that is not a number is 0). A pixel covered in part is replaced in proportion to its
    /// coverage, as <see cref="CompositeMode.Source"/> does.
    /// A pixel without alpha gets the colors of the result over black (multiplied by the alpha the matrix gives), and a gray
    /// pixel their gray level.
    /// </summary>
    /// <typeparam name="TPixel">A floating-point pixel type.</typeparam>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="matrix">The matrix.</param>
    /// <param name="coverage">The coverage of each pixel of the run.</param>
    public static void ApplyColorMatrix<TPixel>(PixelStorage storage, int y, int x, ColorMatrix matrix, ReadOnlySpan<uint> coverage)
        where TPixel : unmanaged
    {
        const double Full = CoverageRasterizer.FullCoverage;
        var m = matrix.Values;
        var pixels = unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y))).Slice(x, coverage.Length);
        for (var i = 0; i < pixels.Length; i++)
        {
            var q = coverage[i];
            if (q == 0)
                continue;

            ref var pixel = ref pixels[i];
            Read(ref pixel, out var r, out var g, out var b, out var a);
            var r2 = Row(m, 0, r, g, b, a);
            var g2 = Row(m, 1, r, g, b, a);
            var b2 = Row(m, 2, r, g, b, a);
            var a2 = Row(m, 3, r, g, b, a);
            a2 = a2 >= 1 ? 1 : a2 > 0 ? a2 : 0;
            if (!HasAlpha<TPixel>())
            {
                // No alpha to store: the colors of the result over black, where the clip covers, and the pixel elsewhere
                var resultWeight = q / Full * a2;
                var pixelWeight = 1 - (q / Full);
                Write(ref pixel, (r2 * resultWeight) + (r * pixelWeight), (g2 * resultWeight) + (g * pixelWeight), (b2 * resultWeight) + (b * pixelWeight), 1);
                continue;
            }

            if (q == CoverageRasterizer.FullCoverage)
            {
                Write(ref pixel, r2, g2, b2, a2);
                continue;
            }

            // The source operator: the result where the clip covers, the pixel elsewhere
            var covered = q / Full;
            var sourceWeight = covered * a2;
            var destinationWeight = (1 - covered) * a;
            var total = sourceWeight + destinationWeight;
            if (!(total > 0))
            {
                if (a != 0)
                {
                    pixel = default;
                }

                continue;
            }

            Write(ref pixel, ((r2 * sourceWeight) + (r * destinationWeight)) / total, ((g2 * sourceWeight) + (g * destinationWeight)) / total, ((b2 * sourceWeight) + (b * destinationWeight)) / total, Math.Min(total, 1));
        }

        static double Row(ReadOnlySpan<double> m, int row, double r, double g, double b, double a)
        {
            var offset = row * ColorMatrix.ColumnCount;
            return (m[offset] * r) + (m[offset + 1] * g) + (m[offset + 2] * b) + (m[offset + 3] * a) + m[offset + 4];
        }
    }

    // The linear value of a sample of a pixel: only a sample in [0, 1] has one
    private static double DecodePixel(byte transfer, double value)
    {
        if (value is >= 0 and <= 1)
            return CicpTransfer.Decode(transfer, value);

        throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The sample {value} of a floating-point pixel has no linear-light value for the transfer characteristics {transfer} of the image: only samples in [0, 1] are encoded light. Label the image as linear light (ColorTransferFunction.Linear), or draw in its encoded values."), ImageFormat.Unknown, "Linear light of out-of-range floating-point samples");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Read<TPixel>(ref TPixel pixel, out double r, out double g, out double b, out double a)
    {
        if (!PixelConverter.IsFloat<TPixel>())
            throw UnsupportedType.Create<TPixel>();

        PixelConverter.ReadFloat(ref pixel, out r, out g, out b, out a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Write<TPixel>(ref TPixel pixel, double r, double g, double b, double a)
        => PixelConverter.WriteFloat(ref pixel, r, g, b, a);

    /// <summary>Gets a value indicating whether a floating-point pixel type stores alpha.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasAlpha<TPixel>() => typeof(TPixel) == typeof(RgbaFloat) || typeof(TPixel) == typeof(RgbaHalf) || typeof(TPixel) == typeof(LaFloat) || typeof(TPixel) == typeof(LaHalf);
}
