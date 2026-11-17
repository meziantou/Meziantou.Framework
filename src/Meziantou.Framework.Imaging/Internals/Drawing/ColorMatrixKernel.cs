using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies a <see cref="ColorMatrix"/> to the pixels of a row, and gives the colors they get.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> With <c>M</c> the largest sample value of the pixel format, and <c>R, G, B, A</c> the samples of a
/// pixel (<c>A = M</c> for a format without alpha, <c>R = G = B</c> the gray level of a grayscale format, and
/// <c>R = G = B = 0</c> for <see cref="A8"/>; the samples of the format of its kernels for an interoperability format,
/// <see cref="DrawingKernels.IsBridgedToColorFormat{TPixel}"/>), each sample
/// of the result is <c>floor(((((m0 R + m1 G) + m2 B) + m3 A) + m4 M) + 1/2 + 2^-20)</c> in double precision, kept between
/// 0 and <c>M</c>; a value that is not a number is 0. The small term makes a value that is half way between two
/// samples, to the rounding of the sum, go up: with the coefficients of
/// <see cref="ColorMatrix.CreateGrayscale(double)"/>, the result is the luma of <see cref="PixelConverter.Luma"/>,
/// which is computed with integers.
/// </para>
/// <para>
/// The result is given as paint colors (<see cref="DrawingColor"/>: the color samples in storage order, then the
/// alpha; a grayscale format gets the Rec. 709 luma of the three colors, three times for a gray format with alpha, and
/// <see cref="A8"/> keeps the alpha only). <see cref="Write"/> stores them: as they
/// are in a format with alpha, hidden colors included (a pixel that is or becomes fully transparent has the color
/// the matrix gives it, as <see cref="ProcessingKernels.Grayscale"/> leaves it its gray level), and multiplied by the
/// alpha in a format without alpha, <c>round(c a / M)</c> to nearest with ties upward: the color of the result over
/// black. A pixel that is replaced in part goes through <see cref="DrawingKernels"/> with
/// <see cref="Drawing.CompositeMode.Source"/> instead, which gives the same samples to a pixel it replaces entirely,
/// except the color of a pixel without alpha.
/// </para>
/// </remarks>
internal static class ColorMatrixKernel
{
    private const double RoundingOffset = 0.5 + (1.0 / 1048576);

    /// <summary>Computes the colors a matrix gives to the pixels of a run.</summary>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="matrix">The matrix.</param>
    /// <param name="destination">The colors: four samples per pixel, for as many pixels as it holds.</param>
    public static void Apply<TPixel>(PixelStorage storage, int y, int x, ColorMatrix matrix, Span<ushort> destination)
        where TPixel : unmanaged
    {
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        var row = storage.GetRowSpanCore(y);
        if (DrawingKernels.IsBridgedToColorFormat<TPixel>())
        {
            ApplyBridged<TPixel>(row, x, matrix, destination);
        }
        else if (Vector128.IsHardwareAccelerated && !HasScalarKernelOnly<TPixel>())
        {
            ApplyVector<TPixel>(row, x, matrix, destination);
        }
        else
        {
            ApplyScalar<TPixel>(row, x, matrix, destination);
        }
    }

    /// <summary>The scalar reference of <see cref="Apply{TPixel}(PixelStorage, int, int, ColorMatrix, Span{ushort})"/>, on the bytes of a row.</summary>
    internal static void ApplyScalar<TPixel>(ReadOnlySpan<byte> row, int x, ColorMatrix matrix, Span<ushort> destination)
        where TPixel : unmanaged
    {
        var count = destination.Length / 4;
        var m = matrix.Values;
        if (typeof(TPixel) == typeof(Rgba32))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba32>(row)).Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                Transform(m, pixel.R, pixel.G, pixel.B, pixel.A, byte.MaxValue, destination.Slice(i * 4, 4), isBlueFirst: false);
            }
        }
        else if (typeof(TPixel) == typeof(Bgra32))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, Bgra32>(row)).Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                Transform(m, pixel.R, pixel.G, pixel.B, pixel.A, byte.MaxValue, destination.Slice(i * 4, 4), isBlueFirst: true);
            }
        }
        else if (typeof(TPixel) == typeof(Rgb24))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, Rgb24>(row)).Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                Transform(m, pixel.R, pixel.G, pixel.B, byte.MaxValue, byte.MaxValue, destination.Slice(i * 4, 4), isBlueFirst: false);
            }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba64>(row)).Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                Transform(m, pixel.R, pixel.G, pixel.B, pixel.A, ushort.MaxValue, destination.Slice(i * 4, 4), isBlueFirst: false);
            }
        }
        else if (typeof(TPixel) == typeof(Gray8))
        {
            var pixels = row.Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                TransformGray(m, pixels[i], byte.MaxValue, destination.Slice(i * 4, 4));
            }
        }
        else if (typeof(TPixel) == typeof(Gray16))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                TransformGray(m, pixels[i], ushort.MaxValue, destination.Slice(i * 4, 4));
            }
        }
        else if (typeof(TPixel) == typeof(Rgb48))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, Rgb48>(row)).Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                Transform(m, pixel.R, pixel.G, pixel.B, ushort.MaxValue, ushort.MaxValue, destination.Slice(i * 4, 4), isBlueFirst: false);
            }
        }
        else if (typeof(TPixel) == typeof(La16))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, La16>(row)).Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                TransformGrayAlpha(m, pixels[i].L, pixels[i].A, byte.MaxValue, destination.Slice(i * 4, 4));
            }
        }
        else if (typeof(TPixel) == typeof(La32))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, La32>(row)).Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                TransformGrayAlpha(m, pixels[i].L, pixels[i].A, ushort.MaxValue, destination.Slice(i * 4, 4));
            }
        }
        else if (typeof(TPixel) == typeof(A8))
        {
            var pixels = row.Slice(x, count);
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = destination.Slice(i * 4, 4);
                color[0] = color[1] = color[2] = 0;
                color[3] = Row(m, 3, 0, 0, 0, pixels[i], byte.MaxValue);
            }
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    /// <summary>
    /// Applies a matrix to pixels of an interoperability format (<see cref="DrawingKernels.IsBridgedToColorFormat{TPixel}"/>):
    /// the pixels converted exactly to the format of their kernels, with the colors of that format.
    /// </summary>
    private static void ApplyBridged<TPixel>(ReadOnlySpan<byte> row, int x, ColorMatrix matrix, Span<ushort> destination)
        where TPixel : unmanaged
    {
        var pixels = unsafe(MemoryMarshal.Cast<byte, TPixel>(row)).Slice(x, destination.Length / 4);
        switch (ProcessingBridge.GetKernelFormat(PixelFormats.GetPixelFormat<TPixel>()))
        {
            case PixelFormat.Rgba32:
                ApplyBridged<TPixel, Rgba32>(pixels, matrix, destination);
                break;
            case PixelFormat.Rgb24:
                ApplyBridged<TPixel, Rgb24>(pixels, matrix, destination);
                break;
            case PixelFormat.Rgba64:
                ApplyBridged<TPixel, Rgba64>(pixels, matrix, destination);
                break;
            default:
                throw new UnreachableException();
        }
    }

    private static void ApplyBridged<TPixel, TWorking>(ReadOnlySpan<TPixel> pixels, ColorMatrix matrix, Span<ushort> destination)
        where TPixel : unmanaged
        where TWorking : unmanaged
    {
        const int ChunkSize = 64;
        Span<TWorking> working = stackalloc TWorking[ChunkSize];
        for (var start = 0; start < pixels.Length; start += ChunkSize)
        {
            var count = Math.Min(ChunkSize, pixels.Length - start);
            PixelConverter.ConvertRow(pixels.Slice(start, count), working);
            ApplyScalar<TWorking>(unsafe(MemoryMarshal.AsBytes(working)), 0, matrix, destination.Slice(start * 4, count * 4));
        }
    }

    /// <summary>Gets a value indicating whether the pixel type has no vector kernel (<see cref="ApplyVector"/>).</summary>
    private static bool HasScalarKernelOnly<TPixel>() => typeof(TPixel) == typeof(Rgb48) || typeof(TPixel) == typeof(La16) || typeof(TPixel) == typeof(La32) || typeof(TPixel) == typeof(A8);

    /// <summary>
    /// Computes the colors a matrix gives to the pixels of a run, the four rows of the matrix two at a time: each lane
    /// computes the sum of its row with the same operations, in the same order, as <see cref="ApplyScalar"/>.
    /// </summary>
    internal static void ApplyVector<TPixel>(ReadOnlySpan<byte> row, int x, ColorMatrix matrix, Span<ushort> destination)
        where TPixel : unmanaged
    {
        var count = destination.Length / 4;
        var m = matrix.Values;
        if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
        {
            var columns = new MatrixColumns(m, byte.MaxValue);
            var isBlueFirst = typeof(TPixel) == typeof(Bgra32);
            var pixels = row.Slice(x * 4, count * 4);
            var done = WideKernel.Apply(matrix, byte.MaxValue, pixels, isRgb24: false, destination, isBlueFirst);
            for (var i = done; i < count; i++)
            {
                var pixel = pixels.Slice(i * 4, 4);
                var (red, blue) = isBlueFirst ? (pixel[2], pixel[0]) : (pixel[0], pixel[2]);
                columns.Transform(red, pixel[1], blue, pixel[3], destination.Slice(i * 4, 4), isBlueFirst);
            }
        }
        else if (typeof(TPixel) == typeof(Rgb24))
        {
            var columns = new MatrixColumns(m, byte.MaxValue);
            var pixels = row.Slice(x * 3, count * 3);
            var done = WideKernel.Apply(matrix, byte.MaxValue, pixels, isRgb24: true, destination, isBlueFirst: false);
            for (var i = done; i < count; i++)
            {
                columns.Transform(pixels[i * 3], pixels[(i * 3) + 1], pixels[(i * 3) + 2], byte.MaxValue, destination.Slice(i * 4, 4), isBlueFirst: false);
            }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            var columns = new MatrixColumns(m, ushort.MaxValue);
            var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 4, count * 4);
            var done = WideKernel.Apply64(m, ushort.MaxValue, pixels, destination);
            for (var i = done; i < count; i++)
            {
                var pixel = pixels.Slice(i * 4, 4);
                columns.Transform(pixel[0], pixel[1], pixel[2], pixel[3], destination.Slice(i * 4, 4), isBlueFirst: false);
            }
        }
        else if (typeof(TPixel) == typeof(Gray8))
        {
            var columns = new MatrixColumns(m, byte.MaxValue);
            var pixels = row.Slice(x, count);
            for (var i = 0; i < count; i++)
            {
                columns.TransformGray(pixels[i], destination.Slice(i * 4, 4));
            }
        }
        else if (typeof(TPixel) == typeof(Gray16))
        {
            var columns = new MatrixColumns(m, ushort.MaxValue);
            var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x, count);
            for (var i = 0; i < count; i++)
            {
                columns.TransformGray(pixels[i], destination.Slice(i * 4, 4));
            }
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    /// <summary>Stores the colors a matrix gave to the pixels of a run.</summary>
    /// <param name="storage">The storage of the frame, leased by the caller.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The first column.</param>
    /// <param name="colors">The colors: four samples per pixel.</param>
    public static void Write<TPixel>(PixelStorage storage, int y, int x, ReadOnlySpan<ushort> colors)
        where TPixel : unmanaged
    {
        var row = storage.GetRowSpanCore(y);
        if (DrawingKernels.IsBridgedToColorFormat<TPixel>())
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, TPixel>(row)).Slice(x, colors.Length / 4);
            switch (ProcessingBridge.GetKernelFormat(PixelFormats.GetPixelFormat<TPixel>()))
            {
                case PixelFormat.Rgba32:
                    WriteBridged<TPixel, Rgba32>(pixels, colors);
                    break;
                case PixelFormat.Rgb24:
                    WriteBridged<TPixel, Rgb24>(pixels, colors);
                    break;
                case PixelFormat.Rgba64:
                    WriteBridged<TPixel, Rgba64>(pixels, colors);
                    break;
                default:
                    throw new UnreachableException();
            }

            return;
        }

        WriteCore<TPixel>(row, x, colors);
    }

    /// <summary>Stores colors of the format of the kernels of an interoperability format: written to that format, then converted to the pixels (to the nearest components).</summary>
    private static void WriteBridged<TPixel, TWorking>(Span<TPixel> pixels, ReadOnlySpan<ushort> colors)
        where TPixel : unmanaged
        where TWorking : unmanaged
    {
        const int ChunkSize = 64;
        Span<TWorking> working = stackalloc TWorking[ChunkSize];
        for (var start = 0; start < pixels.Length; start += ChunkSize)
        {
            var count = Math.Min(ChunkSize, pixels.Length - start);
            WriteCore<TWorking>(unsafe(MemoryMarshal.AsBytes(working)), 0, colors.Slice(start * 4, count * 4));
            PixelConverter.ConvertRow<TWorking, TPixel>(working[..count], pixels.Slice(start, count));
        }
    }

    private static void WriteCore<TPixel>(Span<byte> row, int x, ReadOnlySpan<ushort> colors)
        where TPixel : unmanaged
    {
        var count = colors.Length / 4;
        if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
        {
            // The colors are 8-bit samples: narrowing them keeps their value
            var samples = row.Slice(x * 4, count * 4);
            var i = 0;
            if (Vector512.IsHardwareAccelerated)
            {
                for (; i + 64 <= samples.Length; i += 64)
                {
                    Vector512.Narrow(Vector512.Create(colors.Slice(i, 32)), Vector512.Create(colors.Slice(i + 32, 32))).CopyTo(samples.Slice(i, 64));
                }
            }

            if (Vector256.IsHardwareAccelerated)
            {
                for (; i + 32 <= samples.Length; i += 32)
                {
                    Vector256.Narrow(Vector256.Create(colors.Slice(i, 16)), Vector256.Create(colors.Slice(i + 16, 16))).CopyTo(samples.Slice(i, 32));
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                // The colors hold at least as many samples as the run has bytes: the run is sliced to four bytes per color
                ref var source = ref unsafe(MemoryMarshal.GetReference(colors));
                ref var target = ref unsafe(MemoryMarshal.GetReference(samples));
                for (; i + 32 <= samples.Length; i += 32)
                {
                    var offset = (nuint)i;
                    var first = Vector128.Narrow(unsafe(Vector128.LoadUnsafe(ref source, offset)), unsafe(Vector128.LoadUnsafe(ref source, offset + 8)));
                    var second = Vector128.Narrow(unsafe(Vector128.LoadUnsafe(ref source, offset + 16)), unsafe(Vector128.LoadUnsafe(ref source, offset + 24)));
                    unsafe { first.StoreUnsafe(ref target, offset); }
                    unsafe { second.StoreUnsafe(ref target, offset + 16); }
                }

                for (; i + 16 <= samples.Length; i += 16)
                {
                    Vector128.Narrow(Vector128.Create(colors.Slice(i, 8)), Vector128.Create(colors.Slice(i + 8, 8))).CopyTo(samples.Slice(i, 16));
                }
            }

            for (; i < samples.Length; i++)
            {
                samples[i] = (byte)colors[i];
            }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            colors.CopyTo(unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 4, count * 4));
        }
        else if (typeof(TPixel) == typeof(Rgb24))
        {
            var samples = row.Slice(x * 3, count * 3);
            for (var i = AdvSimd.Arm64.IsSupported ? WriteOverBlack24(samples, colors) : 0; i < count; i++)
            {
                var color = colors.Slice(i * 4, 4);
                samples[i * 3] = (byte)OverBlack(color[0], color[3], byte.MaxValue);
                samples[(i * 3) + 1] = (byte)OverBlack(color[1], color[3], byte.MaxValue);
                samples[(i * 3) + 2] = (byte)OverBlack(color[2], color[3], byte.MaxValue);
            }
        }
        else if (typeof(TPixel) == typeof(Gray8))
        {
            var samples = row.Slice(x, count);
            for (var i = 0; i < count; i++)
            {
                samples[i] = (byte)OverBlack(colors[i * 4], colors[(i * 4) + 3], byte.MaxValue);
            }
        }
        else if (typeof(TPixel) == typeof(Gray16))
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x, count);
            for (var i = 0; i < count; i++)
            {
                samples[i] = (ushort)OverBlack(colors[i * 4], colors[(i * 4) + 3], ushort.MaxValue);
            }
        }
        else if (typeof(TPixel) == typeof(Rgb48))
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 3, count * 3);
            for (var i = 0; i < count; i++)
            {
                var color = colors.Slice(i * 4, 4);
                samples[i * 3] = (ushort)OverBlack(color[0], color[3], ushort.MaxValue);
                samples[(i * 3) + 1] = (ushort)OverBlack(color[1], color[3], ushort.MaxValue);
                samples[(i * 3) + 2] = (ushort)OverBlack(color[2], color[3], ushort.MaxValue);
            }
        }
        else if (typeof(TPixel) == typeof(La16))
        {
            var samples = row.Slice(x * 2, count * 2);
            for (var i = 0; i < count; i++)
            {
                samples[i * 2] = (byte)colors[i * 4];
                samples[(i * 2) + 1] = (byte)colors[(i * 4) + 3];
            }
        }
        else if (typeof(TPixel) == typeof(La32))
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 2, count * 2);
            for (var i = 0; i < count; i++)
            {
                samples[i * 2] = colors[i * 4];
                samples[(i * 2) + 1] = colors[(i * 4) + 3];
            }
        }
        else if (typeof(TPixel) == typeof(A8))
        {
            var samples = row.Slice(x, count);
            for (var i = 0; i < count; i++)
            {
                samples[i] = (byte)colors[(i * 4) + 3];
            }
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }

        static uint OverBlack(uint color, uint alpha, uint max) => ((color * alpha) + (max / 2)) / max;
    }

    /// <summary>
    /// Writes the colors of 8-bit pixels without alpha over black, as <see cref="WriteCore"/> does, sixteen at a time
    /// on Arm64: LD4 separates the samples of the colors, ST3 interleaves the results. Returns the number of pixels done.
    /// </summary>
    private static int WriteOverBlack24(Span<byte> samples, ReadOnlySpan<ushort> colors)
    {
        var blocks = samples.Length / 3 / 16;
        unsafe
        {
            fixed (byte* target = samples)
            fixed (ushort* source = colors)
            {
                for (var i = 0; i < blocks; i++)
                {
                    var (red0, green0, blue0, alpha0) = AdvSimd.Arm64.Load4xVector128AndUnzip(source + (64 * i));
                    var (red1, green1, blue1, alpha1) = AdvSimd.Arm64.Load4xVector128AndUnzip(source + (64 * i) + 32);
                    AdvSimd.Arm64.StoreVectorAndZip(target + (48 * i), (OverBlack(red0, alpha0, red1, alpha1), OverBlack(green0, alpha0, green1, alpha1), OverBlack(blue0, alpha0, blue1, alpha1)));
                }
            }
        }

        return blocks * 16;

        // round(c a / 255) with ties upward, (c a + 127) / 255, for 8-bit samples: the products fit in 16 bits
        static Vector128<byte> OverBlack(Vector128<ushort> color0, Vector128<ushort> alpha0, Vector128<ushort> color1, Vector128<ushort> alpha1)
        {
            var half = Vector128.Create((ushort)(byte.MaxValue / 2));
            return Vector128.Narrow(DivideBy255((color0 * alpha0) + half), DivideBy255((color1 * alpha1) + half));
        }
    }

    /// <summary>Divides by 255, rounding down, the whole numbers from 0 to 65152: <c>(x + 1 + (x >> 8)) >> 8</c>, whose sum stays below 65536.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<ushort> DivideBy255(Vector128<ushort> value)
        => (value + Vector128<ushort>.One + (value >>> 8)) >>> 8;

    /// <summary>Gets a value indicating whether a matrix gives transparent black to a pixel of four samples (color then alpha) that is transparent black.</summary>
    public static bool KeepsTransparentBlack(ColorMatrix matrix, double max)
    {
        var m = matrix.Values;
        return Row(m, 0, 0, 0, 0, 0, max) == 0 && Row(m, 1, 0, 0, 0, 0, max) == 0 && Row(m, 2, 0, 0, 0, 0, max) == 0 && Row(m, 3, 0, 0, 0, 0, max) == 0;
    }

    private static void Transform(ReadOnlySpan<double> m, double r, double g, double b, double a, double max, Span<ushort> color, bool isBlueFirst)
    {
        var red = Row(m, 0, r, g, b, a, max);
        var blue = Row(m, 2, r, g, b, a, max);
        color[0] = isBlueFirst ? blue : red;
        color[1] = Row(m, 1, r, g, b, a, max);
        color[2] = isBlueFirst ? red : blue;
        color[3] = Row(m, 3, r, g, b, a, max);
    }

    /// <summary>Transforms a gray pixel with alpha: the colors are the luma of the three rows, three times (the paint layout of the formats composited as RGBA).</summary>
    private static void TransformGrayAlpha(ReadOnlySpan<double> m, double gray, double alpha, double max, Span<ushort> color)
    {
        var red = Row(m, 0, gray, gray, gray, alpha, max);
        var green = Row(m, 1, gray, gray, gray, alpha, max);
        var blue = Row(m, 2, gray, gray, gray, alpha, max);
        color[0] = color[1] = color[2] = (ushort)PixelConverter.Luma(red, green, blue, (uint)max);
        color[3] = Row(m, 3, gray, gray, gray, alpha, max);
    }

    private static void TransformGray(ReadOnlySpan<double> m, double gray, double max, Span<ushort> color)
    {
        var red = Row(m, 0, gray, gray, gray, max, max);
        var green = Row(m, 1, gray, gray, gray, max, max);
        var blue = Row(m, 2, gray, gray, gray, max, max);
        color[0] = (ushort)PixelConverter.Luma(red, green, blue, (uint)max);
        color[1] = 0;
        color[2] = 0;
        color[3] = Row(m, 3, gray, gray, gray, max, max);
    }

    /// <summary>The columns of a matrix, for the rows 0 and 1 and for the rows 2 and 3; the last one is multiplied by the largest sample.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct MatrixColumns
    {
        private readonly Vector128<double> _red01;
        private readonly Vector128<double> _red23;
        private readonly Vector128<double> _green01;
        private readonly Vector128<double> _green23;
        private readonly Vector128<double> _blue01;
        private readonly Vector128<double> _blue23;
        private readonly Vector128<double> _alpha01;
        private readonly Vector128<double> _alpha23;
        private readonly Vector128<double> _offset01;
        private readonly Vector128<double> _offset23;
        private readonly Vector128<double> _max;
        private readonly double _maxValue;

        // The four rows in one vector, when the hardware has 256-bit vectors
        private readonly Vector256<double> _red;
        private readonly Vector256<double> _green;
        private readonly Vector256<double> _blue;
        private readonly Vector256<double> _alpha;
        private readonly Vector256<double> _offset;

        public MatrixColumns(ReadOnlySpan<double> m, double max)
        {
            const int Columns = ColorMatrix.ColumnCount;
            _red01 = Vector128.Create(m[0], m[Columns]);
            _red23 = Vector128.Create(m[2 * Columns], m[3 * Columns]);
            _green01 = Vector128.Create(m[1], m[Columns + 1]);
            _green23 = Vector128.Create(m[(2 * Columns) + 1], m[(3 * Columns) + 1]);
            _blue01 = Vector128.Create(m[2], m[Columns + 2]);
            _blue23 = Vector128.Create(m[(2 * Columns) + 2], m[(3 * Columns) + 2]);
            _alpha01 = Vector128.Create(m[3], m[Columns + 3]);
            _alpha23 = Vector128.Create(m[(2 * Columns) + 3], m[(3 * Columns) + 3]);
            _offset01 = Vector128.Create(m[4] * max, m[Columns + 4] * max);
            _offset23 = Vector128.Create(m[(2 * Columns) + 4] * max, m[(3 * Columns) + 4] * max);
            _max = Vector128.Create(max);
            _maxValue = max;
            _red = Vector256.Create(_red01, _red23);
            _green = Vector256.Create(_green01, _green23);
            _blue = Vector256.Create(_blue01, _blue23);
            _alpha = Vector256.Create(_alpha01, _alpha23);
            _offset = Vector256.Create(_offset01, _offset23);
        }

        public void Transform(double r, double g, double b, double a, Span<ushort> color, bool isBlueFirst)
        {
            var (rows01, rows23) = Rows(r, g, b, a);
            var red = (ushort)rows01.GetElement(0);
            var blue = (ushort)rows23.GetElement(0);
            color[0] = isBlueFirst ? blue : red;
            color[1] = (ushort)rows01.GetElement(1);
            color[2] = isBlueFirst ? red : blue;
            color[3] = (ushort)rows23.GetElement(1);
        }

        public void TransformGray(double gray, Span<ushort> color)
        {
            var (rows01, rows23) = Rows(gray, gray, gray, _maxValue);
            color[0] = (ushort)PixelConverter.Luma((uint)rows01.GetElement(0), (uint)rows01.GetElement(1), (uint)rows23.GetElement(0), (uint)_maxValue);
            color[1] = 0;
            color[2] = 0;
            color[3] = (ushort)rows23.GetElement(1);
        }

        /// <summary>Gets the samples of the four rows, as <see cref="Row"/> computes them, as whole numbers.</summary>
        private (Vector128<long> Rows01, Vector128<long> Rows23) Rows(double r, double g, double b, double a)
        {
            if (Vector256.IsHardwareAccelerated)
            {
                var rows = (((((_red * Vector256.Create(r)) + (_green * Vector256.Create(g))) + (_blue * Vector256.Create(b))) + (_alpha * Vector256.Create(a))) + _offset) + Vector256.Create(RoundingOffset);
                var max = Vector256.Create(_maxValue);
                var floor = Vector256.Floor(rows);
                var clamped = Vector256.ConditionalSelect(Vector256.GreaterThanOrEqual(floor, max), max, Vector256.ConditionalSelect(Vector256.GreaterThan(floor, Vector256<double>.Zero), floor, Vector256<double>.Zero));
                var whole = ToInt64(clamped);
                return (whole.GetLower(), whole.GetUpper());
            }

            var red = Vector128.Create(r);
            var green = Vector128.Create(g);
            var blue = Vector128.Create(b);
            var alpha = Vector128.Create(a);
            var rows01 = (((((_red01 * red) + (_green01 * green)) + (_blue01 * blue)) + (_alpha01 * alpha)) + _offset01) + Vector128.Create(RoundingOffset);
            var rows23 = (((((_red23 * red) + (_green23 * green)) + (_blue23 * blue)) + (_alpha23 * alpha)) + _offset23) + Vector128.Create(RoundingOffset);
            return (ToInt64(Clamp(Vector128.Floor(rows01), _max)), ToInt64(Clamp(Vector128.Floor(rows23), _max)));
        }

        // Written so that a value that is not a number is zero, as in Row
        private static Vector128<double> Clamp(Vector128<double> value, Vector128<double> max)
            => Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(value, max), max, Vector128.ConditionalSelect(Vector128.GreaterThan(value, Vector128<double>.Zero), value, Vector128<double>.Zero));
    }

    /// <summary>
    /// Computes the colors of several pixels at once, one pixel per lane (structure of arrays): each lane computes the
    /// sum of a row with the same operations, in the same order, as <see cref="Row"/>, so the results are the same.
    /// </summary>
    private static class WideKernel
    {
        /// <summary>Computes the colors of 8-bit RGBA, BGRA or RGB pixels, as many as fit in whole groups; returns the number of pixels done.</summary>
        public static int Apply(ColorMatrix matrix, double max, ReadOnlySpan<byte> pixels, bool isRgb24, Span<ushort> destination, bool isBlueFirst)
        {
            if (AdvSimd.Arm64.IsSupported)
                return Arm64Kernel.Apply(matrix, pixels, isRgb24, destination, isBlueFirst);

            var m = matrix.Values;

            var count = destination.Length / 4;
            var bytesPerPixel = isRgb24 ? 3 : 4;
            var done = 0;
            if (Vector512.IsHardwareAccelerated && Avx512F.IsSupported && Avx2.IsSupported)
            {
                var c = new Coefficients512(m, max);
                var mask = Vector256.Create(0xFFu);
                for (; done + 8 <= count && (done * bytesPerPixel) + 32 <= pixels.Length; done += 8)
                {
                    var packed = isRgb24
                        ? Vector256.Create(ExpandRgb24(Vector128.Create(pixels.Slice(done * 3, 16))), ExpandRgb24(Vector128.Create(pixels.Slice((done + 4) * 3, 16))))
                        : Vector256.Create(pixels.Slice(done * 4, 32)).AsUInt32();
                    var first = (packed & mask).AsInt32();
                    var second = ((packed >> 8) & mask).AsInt32();
                    var third = ((packed >> 16) & mask).AsInt32();
                    var alpha = isRgb24 ? Vector256.Create((int)byte.MaxValue) : (packed >> 24).AsInt32();
                    var (red, blue) = isBlueFirst ? (third, first) : (first, third);
                    c.Compute(red, second, blue, alpha, out var o0, out var o1, out var o2, out var o3);
                    Store(isBlueFirst ? o2 : o0, o1, isBlueFirst ? o0 : o2, o3, destination.Slice(done * 4, 32));
                }
            }

            if (Avx.IsSupported)
            {
                var c = new Coefficients256(m, max);
                var mask = Vector128.Create(0xFFu);
                for (; done + 4 <= count && (done * bytesPerPixel) + 16 <= pixels.Length; done += 4)
                {
                    var bytes = Vector128.Create(pixels.Slice(done * bytesPerPixel, 16));
                    var packed = isRgb24 ? ExpandRgb24(bytes) : bytes.AsUInt32();
                    var first = (packed & mask).AsInt32();
                    var second = ((packed >> 8) & mask).AsInt32();
                    var third = ((packed >> 16) & mask).AsInt32();
                    var alpha = isRgb24 ? Vector128.Create((int)byte.MaxValue) : (packed >> 24).AsInt32();
                    var (red, blue) = isBlueFirst ? (third, first) : (first, third);
                    c.Compute(red, second, blue, alpha, out var o0, out var o1, out var o2, out var o3);
                    Store(isBlueFirst ? o2 : o0, o1, isBlueFirst ? o0 : o2, o3, destination.Slice(done * 4, 16));
                }
            }

            return done;
        }

        /// <summary>Computes the colors of 16-bit RGBA pixels (four samples each), as many as fit in whole groups of four; returns the number of pixels done.</summary>
        public static int Apply64(ReadOnlySpan<double> m, double max, ReadOnlySpan<ushort> samples, Span<ushort> destination)
        {
            if (AdvSimd.Arm64.IsSupported)
                return Arm64Kernel.Apply64(m, max, samples, destination);

            var count = destination.Length / 4;
            var done = 0;
            if (Avx.IsSupported)
            {
                var c = new Coefficients256(m, max);
                var mask = Vector128.Create(0xFFFFUL);
                for (; done + 4 <= count; done += 4)
                {
                    var low = Vector128.Create(samples.Slice(done * 4, 8)).AsUInt64();
                    var high = Vector128.Create(samples.Slice((done * 4) + 8, 8)).AsUInt64();
                    var red = Vector128.Narrow(low & mask, high & mask).AsInt32();
                    var green = Vector128.Narrow((low >> 16) & mask, (high >> 16) & mask).AsInt32();
                    var blue = Vector128.Narrow((low >> 32) & mask, (high >> 32) & mask).AsInt32();
                    var alpha = Vector128.Narrow(low >> 48, high >> 48).AsInt32();
                    c.Compute(red, green, blue, alpha, out var o0, out var o1, out var o2, out var o3);
                    Store(o0, o1, o2, o3, destination.Slice(done * 4, 16));
                }
            }

            return done;
        }

        /// <summary>Spreads four RGB pixels (the first 12 bytes) to four 32-bit lanes; the fourth byte of each lane is to be ignored.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<uint> ExpandRgb24(Vector128<byte> bytes)
            => Vector128.Shuffle(bytes, Vector128.Create((byte)0, 1, 2, 0, 3, 4, 5, 0, 6, 7, 8, 0, 9, 10, 11, 0)).AsUInt32();

        /// <summary>Writes the four samples of four pixels (from 0 to 65535) in pixel order.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Store(Vector128<int> s0, Vector128<int> s1, Vector128<int> s2, Vector128<int> s3, Span<ushort> destination)
        {
            var first = (s0 | (s1 << 16)).AsUInt32();
            var second = (s2 | (s3 << 16)).AsUInt32();
            Vector128<uint> low;
            Vector128<uint> high;
            if (AdvSimd.Arm64.IsSupported)
            {
                low = AdvSimd.Arm64.ZipLow(first, second);
                high = AdvSimd.Arm64.ZipHigh(first, second);
            }
            else
            {
                low = Sse2.UnpackLow(first, second);
                high = Sse2.UnpackHigh(first, second);
            }

            low.AsUInt16().CopyTo(destination);
            high.AsUInt16().CopyTo(destination[8..]);
        }

        /// <summary>Writes the four samples of eight pixels (from 0 to 65535) in pixel order.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Store(Vector256<int> s0, Vector256<int> s1, Vector256<int> s2, Vector256<int> s3, Span<ushort> destination)
        {
            var first = (s0 | (s1 << 16)).AsUInt32();
            var second = (s2 | (s3 << 16)).AsUInt32();

            // Interleaved within each half: the pixels 0, 1, 4, 5, then 2, 3, 6, 7
            var low = Avx2.UnpackLow(first, second);
            var high = Avx2.UnpackHigh(first, second);
            Avx2.Permute2x128(low, high, 0x20).AsUInt16().CopyTo(destination);
            Avx2.Permute2x128(low, high, 0x31).AsUInt16().CopyTo(destination[16..]);
        }

        /// <summary>
        /// The kernel of Arm64: four pixels at once, in two halves of 128 bits. A table lookup moves each sample to a
        /// 64-bit lane. Each sum is rounded down and converted by one instruction (<c>fcvtms</c>: a value that is not a
        /// number gives 0, and a value out of the range of 64-bit integers saturates), then the saturating narrowing
        /// keeps a negative value at 0 and the minimum with the largest sample keeps the others below it: the samples of
        /// <see cref="Row"/>. The terms that are known to be zero (the alpha of a matrix that keeps it, a constant column of
        /// zeros) are not added: adding a zero to a sum does not change its value.
        /// </summary>
        private static class Arm64Kernel
        {
            /// <summary>Computes the colors of 8-bit RGBA, BGRA or RGB pixels, as many as fit in whole groups of four; returns the number of pixels done.</summary>
            public static int Apply(ColorMatrix matrix, ReadOnlySpan<byte> pixels, bool isRgb24, Span<ushort> destination, bool isBlueFirst)
            {
                var m = matrix.Values;
                var done = 0;

                // A run shorter than one block of the tables does not build them
                if (Math.Min(destination.Length / 4, pixels.Length / (isRgb24 ? 3 : 4)) >= DiagonalBlock && GetDiagonalTables(matrix) is { } tables)
                {
                    done = ApplyDiagonal(tables, KeepsAlpha(m), pixels, isRgb24, destination, isBlueFirst);
                    pixels = pixels[(done * (isRgb24 ? 3 : 4))..];
                    destination = destination[(done * 4)..];
                }

                const double Max = byte.MaxValue;
                return done + ((KeepsAlpha(m), HasOffset(m)) switch
                {
                    (true, false) => Apply<NotSet, NotSet>(m, Max, pixels, isRgb24, destination, isBlueFirst),
                    (true, true) => Apply<NotSet, Set>(m, Max, pixels, isRgb24, destination, isBlueFirst),
                    (false, false) => Apply<Set, NotSet>(m, Max, pixels, isRgb24, destination, isBlueFirst),
                    (false, true) => Apply<Set, Set>(m, Max, pixels, isRgb24, destination, isBlueFirst),
                });
            }

            /// <summary>
            /// Gets the lookup tables of a matrix whose rows each read one sample of 8 bits (the other coefficients of the
            /// red, the green, the blue and the alpha are zero), or <see langword="null"/>: four tables of 256 samples, for
            /// the red, the green, the blue and the alpha of the result. Each sample is computed by <see cref="Row"/> with
            /// the other samples of the pixel at 0: their terms are zero whatever they are (the coefficients are finite),
            /// and adding a zero does not change a sum.
            /// </summary>
            private static byte[]? GetDiagonalTables(ColorMatrix matrix)
            {
                const int Columns = ColorMatrix.ColumnCount;
                var m = matrix.Values;
                for (var row = 0; row < ColorMatrix.RowCount; row++)
                {
                    for (var column = 0; column < ColorMatrix.RowCount; column++)
                    {
                        if (column != row && m[(row * Columns) + column] != 0)
                            return null;
                    }
                }

                var tables = Volatile.Read(ref matrix._diagonalTables8);
                if (tables is null)
                {
                    tables = new byte[4 * 256];
                    for (var value = 0; value < 256; value++)
                    {
                        tables[value] = (byte)ColorMatrixKernel.Row(m, 0, value, 0, 0, 0, byte.MaxValue);
                        tables[256 + value] = (byte)ColorMatrixKernel.Row(m, 1, 0, value, 0, 0, byte.MaxValue);
                        tables[512 + value] = (byte)ColorMatrixKernel.Row(m, 2, 0, 0, value, 0, byte.MaxValue);
                        tables[768 + value] = (byte)ColorMatrixKernel.Row(m, 3, 0, 0, 0, value, byte.MaxValue);
                    }

                    Volatile.Write(ref matrix._diagonalTables8, tables);
                }

                return tables;
            }

            /// <summary>The number of pixels <see cref="ApplyDiagonal"/> computes at once.</summary>
            private const int DiagonalBlock = 16;

            /// <summary>
            /// Computes the colors of 8-bit pixels with the tables of <see cref="GetDiagonalTables"/>, sixteen pixels at
            /// a time; returns the number of pixels done. The samples are separated by LD3 or LD4, each one is looked up in
            /// its table of 256 bytes by four lookups of 64 bytes, and ST4 interleaves the colors.
            /// </summary>
            private static int ApplyDiagonal(byte[] tables, bool keepsAlpha, ReadOnlySpan<byte> pixels, bool isRgb24, Span<ushort> destination, bool isBlueFirst)
            {
                var bytesPerPixel = isRgb24 ? 3 : 4;
                var blocks = Math.Min(destination.Length / 4, pixels.Length / bytesPerPixel) / DiagonalBlock;
                var firstTable = isBlueFirst ? 512 : 0;
                var thirdTable = isBlueFirst ? 0 : 512;
                unsafe
                {
                    fixed (byte* table = tables)
                    fixed (byte* source = pixels)
                    fixed (ushort* target = destination)
                    {
                        // The alpha of a pixel without alpha is the largest sample
                        var opaque = Vector128.Create(table[768 + byte.MaxValue]);
                        for (var i = 0; i < blocks; i++)
                        {
                            Vector128<byte> first, second, third, alpha;
                            if (isRgb24)
                            {
                                (first, second, third) = AdvSimd.Arm64.Load3xVector128AndUnzip(source + (48 * i));
                                alpha = opaque;
                            }
                            else
                            {
                                (first, second, third, alpha) = AdvSimd.Arm64.Load4xVector128AndUnzip(source + (64 * i));
                                if (!keepsAlpha)
                                {
                                    alpha = Lookup(table + 768, alpha);
                                }
                            }

                            first = Lookup(table + firstTable, first);
                            second = Lookup(table + 256, second);
                            third = Lookup(table + thirdTable, third);
                            var output = target + (64 * i);
                            AdvSimd.Arm64.StoreVectorAndZip(output, (AdvSimd.ZeroExtendWideningLower(first.GetLower()), AdvSimd.ZeroExtendWideningLower(second.GetLower()), AdvSimd.ZeroExtendWideningLower(third.GetLower()), AdvSimd.ZeroExtendWideningLower(alpha.GetLower())));
                            AdvSimd.Arm64.StoreVectorAndZip(output + 32, (AdvSimd.ZeroExtendWideningUpper(first), AdvSimd.ZeroExtendWideningUpper(second), AdvSimd.ZeroExtendWideningUpper(third), AdvSimd.ZeroExtendWideningUpper(alpha)));
                        }
                    }
                }

                return blocks * DiagonalBlock;
            }

            /// <summary>Looks up sixteen bytes in a table of 256 bytes: TBL on the first 64 bytes, then TBX on each next 64 bytes, with the indexes moved down by 64 (an index out of the 64 bytes leaves its result as it is).</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe Vector128<byte> Lookup(byte* table, Vector128<byte> indexes)
            {
                var step = Vector128.Create((byte)64);
                unsafe
                {
                    var result = AdvSimd.Arm64.VectorTableLookup(Quarter(table), indexes);
                    indexes -= step;
                    result = AdvSimd.Arm64.VectorTableLookupExtension(result, Quarter(table + 64), indexes);
                    indexes -= step;
                    result = AdvSimd.Arm64.VectorTableLookupExtension(result, Quarter(table + 128), indexes);
                    indexes -= step;
                    return AdvSimd.Arm64.VectorTableLookupExtension(result, Quarter(table + 192), indexes);
                }
            }

            // Four loads: the JIT spills the result of LD1 with four registers to the stack before a table lookup
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe (Vector128<byte>, Vector128<byte>, Vector128<byte>, Vector128<byte>) Quarter(byte* table)
            {
                unsafe
                {
                    return (AdvSimd.LoadVector128(table), AdvSimd.LoadVector128(table + 16), AdvSimd.LoadVector128(table + 32), AdvSimd.LoadVector128(table + 48));
                }
            }

            /// <summary>Computes the colors of 16-bit RGBA pixels, as many as fit in whole groups of four; returns the number of pixels done.</summary>
            public static int Apply64(ReadOnlySpan<double> m, double max, ReadOnlySpan<ushort> samples, Span<ushort> destination)
                => (KeepsAlpha(m), HasOffset(m)) switch
                {
                    (true, false) => Apply64<NotSet, NotSet>(m, max, samples, destination),
                    (true, true) => Apply64<NotSet, Set>(m, max, samples, destination),
                    (false, false) => Apply64<Set, NotSet>(m, max, samples, destination),
                    (false, true) => Apply64<Set, Set>(m, max, samples, destination),
                };

            /// <typeparam name="TAlpha">Whether the alpha is computed (the matrix does not keep it, <see cref="KeepsAlpha"/>).</typeparam>
            /// <typeparam name="TOffset">Whether the constant column is added (it has a value that is not zero).</typeparam>
            private static int Apply<TAlpha, TOffset>(ReadOnlySpan<double> m, double max, ReadOnlySpan<byte> pixels, bool isRgb24, Span<ushort> destination, bool isBlueFirst)
                where TAlpha : struct, IFlag
                where TOffset : struct, IFlag
            {
                const int Columns = ColorMatrix.ColumnCount;
                var r0 = Vector128.Create(m[0]);
                var g0 = Vector128.Create(m[1]);
                var b0 = Vector128.Create(m[2]);
                var a0 = Vector128.Create(m[3]);
                var c0 = Vector128.Create(m[4] * max);
                var r1 = Vector128.Create(m[Columns]);
                var g1 = Vector128.Create(m[Columns + 1]);
                var b1 = Vector128.Create(m[Columns + 2]);
                var a1 = Vector128.Create(m[Columns + 3]);
                var c1 = Vector128.Create(m[Columns + 4] * max);
                var r2 = Vector128.Create(m[(2 * Columns) + 0]);
                var g2 = Vector128.Create(m[(2 * Columns) + 1]);
                var b2 = Vector128.Create(m[(2 * Columns) + 2]);
                var a2 = Vector128.Create(m[(2 * Columns) + 3]);
                var c2 = Vector128.Create(m[(2 * Columns) + 4] * max);
                var r3 = Vector128.Create(m[(3 * Columns) + 0]);
                var g3 = Vector128.Create(m[(3 * Columns) + 1]);
                var b3 = Vector128.Create(m[(3 * Columns) + 2]);
                var a3 = Vector128.Create(m[(3 * Columns) + 3]);
                var c3 = Vector128.Create(m[(3 * Columns) + 4] * max);
                var largest = Vector128.Create((uint)max);

                var count = destination.Length / 4;
                var bytesPerPixel = isRgb24 ? 3 : 4;
                var redOffset = isBlueFirst ? 2 : 0;
                var blueOffset = isBlueFirst ? 0 : 2;
                var redLow = ByteLanes(redOffset, bytesPerPixel + redOffset);
                var redHigh = ByteLanes((2 * bytesPerPixel) + redOffset, (3 * bytesPerPixel) + redOffset);
                var greenLow = ByteLanes(1, bytesPerPixel + 1);
                var greenHigh = ByteLanes((2 * bytesPerPixel) + 1, (3 * bytesPerPixel) + 1);
                var blueLow = ByteLanes(blueOffset, bytesPerPixel + blueOffset);
                var blueHigh = ByteLanes((2 * bytesPerPixel) + blueOffset, (3 * bytesPerPixel) + blueOffset);
                var alphaLow = ByteLanes(3, 7);
                var alphaHigh = ByteLanes(11, 15);
                var done = 0;
                for (; done + 4 <= count && (done * bytesPerPixel) + 16 <= pixels.Length; done += 4)
                {
                    var bytes = Vector128.Create(pixels.Slice(done * bytesPerPixel, 16));
                    var rl = ToDouble(bytes, redLow);
                    var rh = ToDouble(bytes, redHigh);
                    var gl = ToDouble(bytes, greenLow);
                    var gh = ToDouble(bytes, greenHigh);
                    var bl = ToDouble(bytes, blueLow);
                    var bh = ToDouble(bytes, blueHigh);
                    Vector128<double> al = default, ah = default;
                    Vector128<uint> o3;
                    if (TAlpha.IsSet)
                    {
                        if (isRgb24)
                        {
                            al = ah = Vector128.Create(max);
                        }
                        else
                        {
                            al = ToDouble(bytes, alphaLow);
                            ah = ToDouble(bytes, alphaHigh);
                        }

                        o3 = Row<TAlpha, TOffset>(rl, gl, bl, al, rh, gh, bh, ah, r3, g3, b3, a3, c3, largest);
                    }
                    else
                    {
                        o3 = isRgb24 ? largest : bytes.AsUInt32() >>> 24;
                    }

                    var o0 = Row<TAlpha, TOffset>(rl, gl, bl, al, rh, gh, bh, ah, r0, g0, b0, a0, c0, largest);
                    var o1 = Row<TAlpha, TOffset>(rl, gl, bl, al, rh, gh, bh, ah, r1, g1, b1, a1, c1, largest);
                    var o2 = Row<TAlpha, TOffset>(rl, gl, bl, al, rh, gh, bh, ah, r2, g2, b2, a2, c2, largest);
                    Store(isBlueFirst ? o2 : o0, o1, isBlueFirst ? o0 : o2, o3, destination.Slice(done * 4, 16));
                }

                return done;
            }

            /// <inheritdoc cref="Apply{TAlpha, TOffset}"/>
            private static int Apply64<TAlpha, TOffset>(ReadOnlySpan<double> m, double max, ReadOnlySpan<ushort> samples, Span<ushort> destination)
                where TAlpha : struct, IFlag
                where TOffset : struct, IFlag
            {
                const int Columns = ColorMatrix.ColumnCount;
                var r0 = Vector128.Create(m[0]);
                var g0 = Vector128.Create(m[1]);
                var b0 = Vector128.Create(m[2]);
                var a0 = Vector128.Create(m[3]);
                var c0 = Vector128.Create(m[4] * max);
                var r1 = Vector128.Create(m[Columns]);
                var g1 = Vector128.Create(m[Columns + 1]);
                var b1 = Vector128.Create(m[Columns + 2]);
                var a1 = Vector128.Create(m[Columns + 3]);
                var c1 = Vector128.Create(m[Columns + 4] * max);
                var r2 = Vector128.Create(m[(2 * Columns) + 0]);
                var g2 = Vector128.Create(m[(2 * Columns) + 1]);
                var b2 = Vector128.Create(m[(2 * Columns) + 2]);
                var a2 = Vector128.Create(m[(2 * Columns) + 3]);
                var c2 = Vector128.Create(m[(2 * Columns) + 4] * max);
                var r3 = Vector128.Create(m[(3 * Columns) + 0]);
                var g3 = Vector128.Create(m[(3 * Columns) + 1]);
                var b3 = Vector128.Create(m[(3 * Columns) + 2]);
                var a3 = Vector128.Create(m[(3 * Columns) + 3]);
                var c3 = Vector128.Create(m[(3 * Columns) + 4] * max);
                var largest = Vector128.Create((uint)max);

                // Each half holds two pixels: the same lanes serve both
                var red = UInt16Lanes(0);
                var green = UInt16Lanes(1);
                var blue = UInt16Lanes(2);
                var alpha = UInt16Lanes(3);
                var alphas = Vector128.Create(6, 7, 0xFF, 0xFF, 14, 15, 0xFF, 0xFF, 22, 23, 0xFF, 0xFF, 30, 31, 0xFF, (byte)0xFF);
                var count = destination.Length / 4;
                var done = 0;
                for (; done + 4 <= count; done += 4)
                {
                    var low = Vector128.Create(samples.Slice(done * 4, 8)).AsByte();
                    var high = Vector128.Create(samples.Slice((done * 4) + 8, 8)).AsByte();
                    var rl = ToDouble(low, red);
                    var rh = ToDouble(high, red);
                    var gl = ToDouble(low, green);
                    var gh = ToDouble(high, green);
                    var bl = ToDouble(low, blue);
                    var bh = ToDouble(high, blue);
                    Vector128<double> al = default, ah = default;
                    Vector128<uint> o3;
                    if (TAlpha.IsSet)
                    {
                        al = ToDouble(low, alpha);
                        ah = ToDouble(high, alpha);
                        o3 = Row<TAlpha, TOffset>(rl, gl, bl, al, rh, gh, bh, ah, r3, g3, b3, a3, c3, largest);
                    }
                    else
                    {
                        o3 = AdvSimd.Arm64.VectorTableLookup((low, high), alphas).AsUInt32();
                    }

                    var o0 = Row<TAlpha, TOffset>(rl, gl, bl, al, rh, gh, bh, ah, r0, g0, b0, a0, c0, largest);
                    var o1 = Row<TAlpha, TOffset>(rl, gl, bl, al, rh, gh, bh, ah, r1, g1, b1, a1, c1, largest);
                    var o2 = Row<TAlpha, TOffset>(rl, gl, bl, al, rh, gh, bh, ah, r2, g2, b2, a2, c2, largest);
                    Store(o0, o1, o2, o3, destination.Slice(done * 4, 16));
                }

                return done;
            }

            /// <summary>Gets a value indicating whether the constant column of a matrix has a value that is not zero.</summary>
            private static bool HasOffset(ReadOnlySpan<double> m)
            {
                const int Columns = ColorMatrix.ColumnCount;
                return m[4] != 0 || m[Columns + 4] != 0 || m[(2 * Columns) + 4] != 0 || m[(3 * Columns) + 4] != 0;
            }

            /// <summary>The indexes of a table lookup that moves two bytes to the low byte of two 64-bit lanes (the other bytes get 0).</summary>
            private static Vector128<byte> ByteLanes(int first, int second)
                => Vector128.Create((uint)first | 0xFFFF_FFFF_FFFF_FF00, (uint)second | 0xFFFF_FFFF_FFFF_FF00).AsByte();

            /// <summary>The indexes of a table lookup that moves a sample of each of the two 16-bit RGBA pixels of a vector to a 64-bit lane.</summary>
            private static Vector128<byte> UInt16Lanes(int sample)
            {
                var first = (uint)(2 * sample) | ((ulong)(uint)((2 * sample) + 1) << 8) | 0xFFFF_FFFF_FFFF_0000;
                return Vector128.Create(first, first + 0x0808).AsByte();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Vector128<double> ToDouble(Vector128<byte> bytes, Vector128<byte> lanes)
                => Vector128.ConvertToDouble(AdvSimd.Arm64.VectorTableLookup(bytes, lanes).AsUInt64());

            /// <summary>Computes a row of the matrix for four pixels (two halves), as <see cref="Row"/> does.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Vector128<uint> Row<TAlpha, TOffset>(
                Vector128<double> rl, Vector128<double> gl, Vector128<double> bl, Vector128<double> al,
                Vector128<double> rh, Vector128<double> gh, Vector128<double> bh, Vector128<double> ah,
                Vector128<double> mr, Vector128<double> mg, Vector128<double> mb, Vector128<double> ma, Vector128<double> mc, Vector128<uint> largest)
                where TAlpha : struct, IFlag
                where TOffset : struct, IFlag
            {
                var low = ((mr * rl) + (mg * gl)) + (mb * bl);
                var high = ((mr * rh) + (mg * gh)) + (mb * bh);
                if (TAlpha.IsSet)
                {
                    low += ma * al;
                    high += ma * ah;
                }

                if (TOffset.IsSet)
                {
                    low += mc;
                    high += mc;
                }

                var rounding = Vector128.Create(RoundingOffset);
                var lowWhole = AdvSimd.Arm64.ConvertToInt64RoundToNegativeInfinity(low + rounding);
                var highWhole = AdvSimd.Arm64.ConvertToInt64RoundToNegativeInfinity(high + rounding);
                var narrowed = AdvSimd.ExtractNarrowingSaturateUnsignedUpper(AdvSimd.ExtractNarrowingSaturateUnsignedLower(lowWhole), highWhole);
                return AdvSimd.Min(narrowed, largest);
            }

            /// <summary>Writes the four samples of four pixels (from 0 to 65535) in pixel order.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Store(Vector128<uint> s0, Vector128<uint> s1, Vector128<uint> s2, Vector128<uint> s3, Span<ushort> destination)
            {
                // The samples fit in 16 bits: the second sample of each pair goes to the high half of the lane of the first
                var first = AdvSimd.ShiftLeftAndInsert(s0, s1, 16);
                var second = AdvSimd.ShiftLeftAndInsert(s2, s3, 16);
                AdvSimd.Arm64.ZipLow(first, second).AsUInt16().CopyTo(destination);
                AdvSimd.Arm64.ZipHigh(first, second).AsUInt16().CopyTo(destination[8..]);
            }
        }

        /// <summary>The coefficients of four pixels at once, in 256-bit vectors of doubles (x64).</summary>
        [StructLayout(LayoutKind.Auto)]
        private readonly struct Coefficients256
        {
            private readonly Vector256<double> _r0, _g0, _b0, _a0, _c0, _r1, _g1, _b1, _a1, _c1, _r2, _g2, _b2, _a2, _c2, _r3, _g3, _b3, _a3, _c3;
            private readonly Vector256<double> _max;
            private readonly bool _keepsAlpha;

            public Coefficients256(ReadOnlySpan<double> m, double max)
            {
                _keepsAlpha = KeepsAlpha(m);
                _r0 = Vector256.Create(m[0]);
                _g0 = Vector256.Create(m[1]);
                _b0 = Vector256.Create(m[2]);
                _a0 = Vector256.Create(m[3]);
                _c0 = Vector256.Create(m[4] * max);
                _r1 = Vector256.Create(m[ColorMatrix.ColumnCount]);
                _g1 = Vector256.Create(m[ColorMatrix.ColumnCount + 1]);
                _b1 = Vector256.Create(m[ColorMatrix.ColumnCount + 2]);
                _a1 = Vector256.Create(m[ColorMatrix.ColumnCount + 3]);
                _c1 = Vector256.Create(m[ColorMatrix.ColumnCount + 4] * max);
                _r2 = Vector256.Create(m[(2 * ColorMatrix.ColumnCount) + 0]);
                _g2 = Vector256.Create(m[(2 * ColorMatrix.ColumnCount) + 1]);
                _b2 = Vector256.Create(m[(2 * ColorMatrix.ColumnCount) + 2]);
                _a2 = Vector256.Create(m[(2 * ColorMatrix.ColumnCount) + 3]);
                _c2 = Vector256.Create(m[(2 * ColorMatrix.ColumnCount) + 4] * max);
                _r3 = Vector256.Create(m[(3 * ColorMatrix.ColumnCount) + 0]);
                _g3 = Vector256.Create(m[(3 * ColorMatrix.ColumnCount) + 1]);
                _b3 = Vector256.Create(m[(3 * ColorMatrix.ColumnCount) + 2]);
                _a3 = Vector256.Create(m[(3 * ColorMatrix.ColumnCount) + 3]);
                _c3 = Vector256.Create(m[(3 * ColorMatrix.ColumnCount) + 4] * max);
                _max = Vector256.Create(max);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Compute(Vector128<int> red, Vector128<int> green, Vector128<int> blue, Vector128<int> alpha, out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3)
            {
                var r = Avx.ConvertToVector256Double(red);
                var g = Avx.ConvertToVector256Double(green);
                var b = Avx.ConvertToVector256Double(blue);
                if (_keepsAlpha)
                {
                    o0 = Avx.ConvertToVector128Int32WithTruncation(Sum(r, g, b, _r0, _g0, _b0, _c0, _max));
                    o1 = Avx.ConvertToVector128Int32WithTruncation(Sum(r, g, b, _r1, _g1, _b1, _c1, _max));
                    o2 = Avx.ConvertToVector128Int32WithTruncation(Sum(r, g, b, _r2, _g2, _b2, _c2, _max));
                    o3 = alpha;
                    return;
                }

                var a = Avx.ConvertToVector256Double(alpha);
                o0 = Avx.ConvertToVector128Int32WithTruncation(Sum(r, g, b, a, _r0, _g0, _b0, _a0, _c0, _max));
                o1 = Avx.ConvertToVector128Int32WithTruncation(Sum(r, g, b, a, _r1, _g1, _b1, _a1, _c1, _max));
                o2 = Avx.ConvertToVector128Int32WithTruncation(Sum(r, g, b, a, _r2, _g2, _b2, _a2, _c2, _max));
                o3 = Avx.ConvertToVector128Int32WithTruncation(Sum(r, g, b, a, _r3, _g3, _b3, _a3, _c3, _max));
            }
        }

        /// <summary>The coefficients of eight pixels at once, in 512-bit vectors of doubles.</summary>
        [StructLayout(LayoutKind.Auto)]
        private readonly struct Coefficients512
        {
            private readonly Vector512<double> _r0, _g0, _b0, _a0, _c0, _r1, _g1, _b1, _a1, _c1, _r2, _g2, _b2, _a2, _c2, _r3, _g3, _b3, _a3, _c3;
            private readonly Vector512<double> _max;
            private readonly bool _keepsAlpha;

            public Coefficients512(ReadOnlySpan<double> m, double max)
            {
                _keepsAlpha = KeepsAlpha(m);
                _r0 = Vector512.Create(m[0]);
                _g0 = Vector512.Create(m[1]);
                _b0 = Vector512.Create(m[2]);
                _a0 = Vector512.Create(m[3]);
                _c0 = Vector512.Create(m[4] * max);
                _r1 = Vector512.Create(m[ColorMatrix.ColumnCount]);
                _g1 = Vector512.Create(m[ColorMatrix.ColumnCount + 1]);
                _b1 = Vector512.Create(m[ColorMatrix.ColumnCount + 2]);
                _a1 = Vector512.Create(m[ColorMatrix.ColumnCount + 3]);
                _c1 = Vector512.Create(m[ColorMatrix.ColumnCount + 4] * max);
                _r2 = Vector512.Create(m[(2 * ColorMatrix.ColumnCount) + 0]);
                _g2 = Vector512.Create(m[(2 * ColorMatrix.ColumnCount) + 1]);
                _b2 = Vector512.Create(m[(2 * ColorMatrix.ColumnCount) + 2]);
                _a2 = Vector512.Create(m[(2 * ColorMatrix.ColumnCount) + 3]);
                _c2 = Vector512.Create(m[(2 * ColorMatrix.ColumnCount) + 4] * max);
                _r3 = Vector512.Create(m[(3 * ColorMatrix.ColumnCount) + 0]);
                _g3 = Vector512.Create(m[(3 * ColorMatrix.ColumnCount) + 1]);
                _b3 = Vector512.Create(m[(3 * ColorMatrix.ColumnCount) + 2]);
                _a3 = Vector512.Create(m[(3 * ColorMatrix.ColumnCount) + 3]);
                _c3 = Vector512.Create(m[(3 * ColorMatrix.ColumnCount) + 4] * max);
                _max = Vector512.Create(max);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Compute(Vector256<int> red, Vector256<int> green, Vector256<int> blue, Vector256<int> alpha, out Vector256<int> o0, out Vector256<int> o1, out Vector256<int> o2, out Vector256<int> o3)
            {
                var r = Avx512F.ConvertToVector512Double(red);
                var g = Avx512F.ConvertToVector512Double(green);
                var b = Avx512F.ConvertToVector512Double(blue);
                if (_keepsAlpha)
                {
                    o0 = Avx512F.ConvertToVector256Int32WithTruncation(Sum(r, g, b, _r0, _g0, _b0, _c0, _max));
                    o1 = Avx512F.ConvertToVector256Int32WithTruncation(Sum(r, g, b, _r1, _g1, _b1, _c1, _max));
                    o2 = Avx512F.ConvertToVector256Int32WithTruncation(Sum(r, g, b, _r2, _g2, _b2, _c2, _max));
                    o3 = alpha;
                    return;
                }

                var a = Avx512F.ConvertToVector512Double(alpha);
                o0 = Avx512F.ConvertToVector256Int32WithTruncation(Sum(r, g, b, a, _r0, _g0, _b0, _a0, _c0, _max));
                o1 = Avx512F.ConvertToVector256Int32WithTruncation(Sum(r, g, b, a, _r1, _g1, _b1, _a1, _c1, _max));
                o2 = Avx512F.ConvertToVector256Int32WithTruncation(Sum(r, g, b, a, _r2, _g2, _b2, _a2, _c2, _max));
                o3 = Avx512F.ConvertToVector256Int32WithTruncation(Sum(r, g, b, a, _r3, _g3, _b3, _a3, _c3, _max));
            }
        }

        /// <summary>
        /// Gets a value indicating whether a matrix keeps the alpha of every pixel and gives colors that do not depend on
        /// it: the alpha row is (0, 0, 0, 1, 0) and the alpha column of the color rows is 0. The alpha of a pixel is then
        /// its own (<c>floor(A + 1/2 + 2^-20)</c> is <c>A</c>), and the sums of the colors are those of
        /// <see cref="Row"/> without the term of the alpha: it is 0 (the alpha is finite), and adding 0 to a sum does
        /// not change its value (only the sign of a zero, which the rounding down removes).
        /// </summary>
        private static bool KeepsAlpha(ReadOnlySpan<double> m)
        {
            const int Columns = ColorMatrix.ColumnCount;
            return m[3] == 0 && m[Columns + 3] == 0 && m[(2 * Columns) + 3] == 0
                && m[3 * Columns] == 0 && m[(3 * Columns) + 1] == 0 && m[(3 * Columns) + 2] == 0 && m[(3 * Columns) + 3] == 1 && m[(3 * Columns) + 4] == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<double> Sum(Vector256<double> r, Vector256<double> g, Vector256<double> b, Vector256<double> mr, Vector256<double> mg, Vector256<double> mb, Vector256<double> mc, Vector256<double> max)
            => Clamp(Vector256.Floor(((((mr * r) + (mg * g)) + (mb * b)) + mc) + Vector256.Create(RoundingOffset)), max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<double> Sum(Vector512<double> r, Vector512<double> g, Vector512<double> b, Vector512<double> mr, Vector512<double> mg, Vector512<double> mb, Vector512<double> mc, Vector512<double> max)
            => Clamp(Vector512.Floor(((((mr * r) + (mg * g)) + (mb * b)) + mc) + Vector512.Create(RoundingOffset)), max);

        // The sum of Row, rounded down and kept between 0 and the largest sample; a value that is not a number gives 0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<double> Sum(Vector256<double> r, Vector256<double> g, Vector256<double> b, Vector256<double> a, Vector256<double> mr, Vector256<double> mg, Vector256<double> mb, Vector256<double> ma, Vector256<double> mc, Vector256<double> max)
            => Clamp(Vector256.Floor((((((mr * r) + (mg * g)) + (mb * b)) + (ma * a)) + mc) + Vector256.Create(RoundingOffset)), max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<double> Sum(Vector512<double> r, Vector512<double> g, Vector512<double> b, Vector512<double> a, Vector512<double> mr, Vector512<double> mg, Vector512<double> mb, Vector512<double> ma, Vector512<double> mc, Vector512<double> max)
            => Clamp(Vector512.Floor((((((mr * r) + (mg * g)) + (mb * b)) + (ma * a)) + mc) + Vector512.Create(RoundingOffset)), max);

        // Keeps a whole number between 0 and the largest sample; a value that is not a number gives 0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<double> Clamp(Vector256<double> floor, Vector256<double> max)
        {
            if (Avx.IsSupported)
            {
                // maxpd gives its second operand when one is not a number
                return Avx.Min(Avx.Max(floor, Vector256<double>.Zero), max);
            }

            return Vector256.ConditionalSelect(Vector256.GreaterThanOrEqual(floor, max), max, Vector256.ConditionalSelect(Vector256.GreaterThan(floor, Vector256<double>.Zero), floor, Vector256<double>.Zero));
        }

        // vmaxpd gives its second operand when one is not a number
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<double> Clamp(Vector512<double> floor, Vector512<double> max)
            => Avx512F.Min(Avx512F.Max(floor, Vector512<double>.Zero), max);
    }

    /// <summary>Converts whole numbers from 0 to 2^52, as <see cref="Vector128.ConvertToInt64(Vector128{double})"/> does.</summary>
    /// <remarks>Arm64 converts them with one instruction; x64 emulates that conversion without AVX-512 and takes <see cref="ToInt64WithBias"/>.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<long> ToInt64(Vector128<double> value)
        => AdvSimd.Arm64.IsSupported ? Vector128.ConvertToInt64(value) : ToInt64WithBias(value);

    /// <summary>Converts whole numbers from 0 to 2^52 with an addition and a subtraction.</summary>
    /// <remarks>
    /// Adding 2^52 to such a number is exact whatever the rounding mode (from 2^52 to 2^53, the doubles are the whole
    /// numbers), and the sum minus the bits of 2^52 is the number.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<long> ToInt64WithBias(Vector128<double> value)
    {
        var bias = Vector128.Create(4503599627370496.0);
        return (value + bias).AsInt64() - bias.AsInt64();
    }

    /// <summary>Converts whole numbers from 0 to 2^52, with an addition and a subtraction (see <see cref="ToInt64WithBias"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector256<long> ToInt64(Vector256<double> value)
    {
        var bias = Vector256.Create(4503599627370496.0);
        return (value + bias).AsInt64() - bias.AsInt64();
    }

    // Written so that a value that is not a number is zero
    private static ushort Row(ReadOnlySpan<double> m, int row, double r, double g, double b, double a, double max)
    {
        var offset = row * ColorMatrix.ColumnCount;
        var value = Math.Floor((m[offset] * r) + (m[offset + 1] * g) + (m[offset + 2] * b) + (m[offset + 3] * a) + (m[offset + 4] * max) + RoundingOffset);
        return value >= max ? (ushort)max : value > 0 ? (ushort)value : (ushort)0;
    }

    /// <summary>A choice that is made when a generic method is compiled, so that its loops do not test it.</summary>
    private interface IFlag
    {
        static abstract bool IsSet { get; }
    }

    private readonly struct Set : IFlag
    {
        public static bool IsSet => true;
    }

    private readonly struct NotSet : IFlag
    {
        public static bool IsSet => false;
    }
}
