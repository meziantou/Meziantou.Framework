using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies a <see cref="ColorMatrix"/> to the pixels of a row, and gives the colors they get.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> With <c>M</c> the largest sample value of the pixel format, and <c>R, G, B, A</c> the samples of a
/// pixel (<c>A = M</c> for a format without alpha, <c>R = G = B</c> the gray level of a grayscale format), each sample
/// of the result is <c>floor(((((m0 R + m1 G) + m2 B) + m3 A) + m4 M) + 1/2 + 2^-20)</c> in double precision, kept between
/// 0 and <c>M</c>; a value that is not a number is 0. The small term makes a value that is half way between two
/// samples, to the rounding of the sum, go up: with the coefficients of
/// <see cref="ColorMatrix.CreateGrayscale(double)"/>, the result is the luma of <see cref="PixelConverter.Luma"/>,
/// which is computed with integers.
/// </para>
/// <para>
/// The result is given as paint colors (<see cref="DrawingColor"/>: the color samples in storage order, then the
/// alpha; a grayscale format gets the Rec. 709 luma of the three colors). <see cref="Write"/> stores them: as they
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
        if (Vector128.IsHardwareAccelerated)
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
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

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
            for (var i = 0; i < count; i++)
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
            for (var i = 0; i < count; i++)
            {
                columns.Transform(pixels[i * 3], pixels[(i * 3) + 1], pixels[(i * 3) + 2], byte.MaxValue, destination.Slice(i * 4, 4), isBlueFirst: false);
            }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            var columns = new MatrixColumns(m, ushort.MaxValue);
            var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 4, count * 4);
            for (var i = 0; i < count; i++)
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
        var count = colors.Length / 4;
        var row = storage.GetRowSpanCore(y);
        if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
        {
            var samples = row.Slice(x * 4, count * 4);
            for (var i = 0; i < samples.Length; i++)
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
            for (var i = 0; i < count; i++)
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
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }

        static uint OverBlack(uint color, uint alpha, uint max) => ((color * alpha) + (max / 2)) / max;
    }

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

    // Written so that a value that is not a number is zero
    private static ushort Row(ReadOnlySpan<double> m, int row, double r, double g, double b, double a, double max)
    {
        var offset = row * ColorMatrix.ColumnCount;
        var value = Math.Floor((m[offset] * r) + (m[offset + 1] * g) + (m[offset + 2] * b) + (m[offset + 3] * a) + (m[offset + 4] * max) + RoundingOffset);
        return value >= max ? (ushort)max : value > 0 ? (ushort)value : (ushort)0;
    }
}
