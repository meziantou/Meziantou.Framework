using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Binarizes frames of one pixel format and size (<see cref="ThresholdOptions"/>): every pixel becomes the upper or the
/// lower color depending on whether its luminance is greater than its threshold. The working memory is rented when the
/// instance is created, before any pixel changes.
/// </summary>
internal sealed class Thresholder : IDisposable
{
    private readonly ThresholdOptions _options;
    private readonly PixelFormat _format;
    private readonly int _width;
    private readonly int _height;
    private readonly int _bytesPerPixel;
    private readonly uint _max;
    private readonly byte[] _colors;
    private readonly ulong _upper;
    private readonly ulong _lower;
    private readonly PooledBuffer _row;
    private readonly PooledBuffer? _luma;
    private readonly PooledBuffer? _sums;

    /// <summary>Rents the working memory: one row, and for the adaptive methods the luminance of a frame and two sums per column.</summary>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    public Thresholder(AllocationScope scope, PixelFormat format, Size size, ThresholdOptions options)
    {
        _options = options;
        _format = format;
        _width = size.Width;
        _height = size.Height;
        _bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        _max = (uint)(ToneSamples.GetLevelCount(format) - 1);
        _colors = new byte[2 * _bytesPerPixel];
        Store(options.UpperColor, format, _colors.AsSpan(0, _bytesPerPixel));
        Store(options.LowerColor, format, _colors.AsSpan(_bytesPerPixel));
        Span<byte> bits = stackalloc byte[sizeof(ulong)];
        bits.Clear();
        _colors.AsSpan(0, _bytesPerPixel).CopyTo(bits);
        _upper = BitConverter.ToUInt64(bits);
        _colors.AsSpan(_bytesPerPixel).CopyTo(bits);
        _lower = BitConverter.ToUInt64(bits);

        _row = ToneSamples.RentRow(scope, _width);
        try
        {
            if (IsAdaptive(options.Method))
            {
                var lumaBytes = (long)_width * _height * sizeof(ushort);
                var sumBytes = (long)_width * 2 * sizeof(long);
                if (lumaBytes > CheckedSizes.MaxBufferLength || sumBytes > CheckedSizes.MaxBufferLength)
                    throw CheckedSizes.CreateOverflowException(scope.Limits);

                _luma = scope.Rent((int)lumaBytes, AllocationKind.Temporary, clear: false);
                _sums = scope.Rent((int)sumBytes, AllocationKind.Temporary, clear: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Checks that the pixels have discrete levels and that the pixel format stores both colors exactly, so that nothing is
    /// lost silently.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    /// <exception cref="ArgumentException">A color cannot be stored exactly.</exception>
    public static void Validate(PixelFormat format, ThresholdOptions options)
    {
        if (PixelFormats.IsFloatingPoint(format))
            throw new UnsupportedImageFeatureException(
                $"A threshold compares the levels of integer samples, but {format} samples are floating-point values. Convert the image to Rgba64 with Image.CloneAs first to threshold it.",
                ImageFormat.Unknown,
                "Threshold of floating-point samples");

        if (!PixelConverter.IsExactlyRepresentable(options.UpperColor, format))
            throw new ArgumentException(CreateColorMessage("upper", options.UpperColor, format), nameof(options));

        if (!PixelConverter.IsExactlyRepresentable(options.LowerColor, format))
            throw new ArgumentException(CreateColorMessage("lower", options.LowerColor, format), nameof(options));
    }

    /// <summary>Gets a value indicating whether a method needs the neighborhood of each pixel.</summary>
    public static bool IsAdaptive(ThresholdMethod method) => method is ThresholdMethod.Mean or ThresholdMethod.Sauvola;

    /// <summary>Binarizes the pixels of a frame in place.</summary>
    /// <param name="lease">The lease of the frame.</param>
    /// <param name="level">The threshold of the global methods, on the scale of the samples; ignored by the adaptive ones.</param>
    /// <param name="cancellationToken">The token checked between row bands.</param>
    public void Apply(scoped in PixelLease lease, double level, CancellationToken cancellationToken)
    {
        if (_luma is null)
        {
            ApplyGlobal(lease, level, cancellationToken);
        }
        else
        {
            ApplyAdaptive(lease, cancellationToken);
        }
    }

    public void Dispose()
    {
        _row.Dispose();
        _luma?.Dispose();
        _sums?.Dispose();
    }

    private static string CreateColorMessage(string name, Rgba64 color, PixelFormat format)
    {
        return string.Create(CultureInfo.InvariantCulture, $"The {name} color ({color.R}, {color.G}, {color.B}, {color.A}) cannot be stored exactly in {format} pixels: a pixel format without alpha needs an opaque color, a gray one a gray color, and an 8-bit one samples that are multiples of 257.");
    }

    private static void Store(Rgba64 color, PixelFormat format, Span<byte> destination)
    {
        ReadOnlySpan<Rgba64> source = [color];
        PixelConverter.ConvertRow(PixelFormat.Rgba64, unsafe(MemoryMarshal.AsBytes(source)), format, destination);
    }

    /// <summary>
    /// Gets the lowest luma above a threshold: <c>luma &gt; level</c> is <c>luma &gt;= floor(level) + 1</c> for integer lumas. A
    /// threshold that is not a number has no luma above it.
    /// </summary>
    internal static int GetLowestLumaAbove(double level, uint max)
    {
        if (double.IsNaN(level) || level >= max)
            return (int)max + 1;

        return level < 0 ? 0 : (int)Math.Floor(level) + 1;
    }

    /// <summary>Replaces each luma of a row by a mask: <c>0xFFFF</c> when it is at least <paramref name="lowest"/>, 0 otherwise.</summary>
    internal static void CompareLuma(Span<ushort> luma, int lowest)
    {
        if (lowest <= 0)
        {
            luma.Fill(ushort.MaxValue);
            return;
        }

        if (lowest > ushort.MaxValue)
        {
            luma.Clear();
            return;
        }

        var i = 0;
        if (Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar)
        {
            ref var start = ref unsafe(MemoryMarshal.GetReference(luma));
            var threshold = Vector128.Create((ushort)lowest);
            for (; i + Vector128<ushort>.Count <= luma.Length; i += Vector128<ushort>.Count)
            {
                unsafe { Vector128.GreaterThanOrEqual(Vector128.LoadUnsafe(ref start, (nuint)i), threshold).StoreUnsafe(ref start, (nuint)i); }
            }
        }

        for (; i < luma.Length; i++)
        {
            luma[i] = luma[i] >= lowest ? ushort.MaxValue : (ushort)0;
        }
    }

    /// <summary>Stores the upper color in the pixels whose mask is <c>0xFFFF</c>, and the lower color in the others (mask 0).</summary>
    internal void StoreColors(ReadOnlySpan<ushort> mask, Span<byte> row)
    {
        var x = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar ? StoreColorsVector(mask, row) : 0;
        StoreColorsScalar(mask[x..], row[(x * _bytesPerPixel)..]);
    }

    /// <summary>The scalar reference of <see cref="StoreColors"/>.</summary>
    internal void StoreColorsScalar(ReadOnlySpan<ushort> mask, Span<byte> row)
    {
        switch (_bytesPerPixel)
        {
            case 1:
                for (var x = 0; x < mask.Length; x++)
                {
                    row[x] = (byte)(mask[x] != 0 ? _upper : _lower);
                }

                break;

            case 2:
                var pixels16 = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                for (var x = 0; x < mask.Length; x++)
                {
                    pixels16[x] = (ushort)(mask[x] != 0 ? _upper : _lower);
                }

                break;

            case 4:
                var pixels32 = unsafe(MemoryMarshal.Cast<byte, uint>(row));
                for (var x = 0; x < mask.Length; x++)
                {
                    pixels32[x] = (uint)(mask[x] != 0 ? _upper : _lower);
                }

                break;

            case 8:
                var pixels64 = unsafe(MemoryMarshal.Cast<byte, ulong>(row));
                for (var x = 0; x < mask.Length; x++)
                {
                    pixels64[x] = mask[x] != 0 ? _upper : _lower;
                }

                break;

            default:
                // 3 and 6 bytes: every pixel but the last is written with the bytes that follow it, which the next one overwrites
                var size = _bytesPerPixel;
                var last = mask.Length - 1;
                for (var x = 0; x < last; x++)
                {
                    var color = mask[x] != 0 ? _upper : _lower;
                    if (size == 3)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(row[(3 * x)..], (uint)color);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt64LittleEndian(row[(6 * x)..], color);
                    }
                }

                if (last >= 0)
                {
                    var color = mask[last] != 0 ? _upper : _lower;
                    for (var b = 0; b < size; b++)
                    {
                        row[(last * size) + b] = (byte)(color >> (8 * b));
                    }
                }

                break;
        }
    }

    /// <summary>Adds the lumas of a row to the sums and the sums of squares of each column.</summary>
    private static void AddRow(ReadOnlySpan<ushort> row, Span<long> columnSums, Span<long> columnSquares)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar)
        {
            ref var values = ref unsafe(MemoryMarshal.GetReference(row));
            ref var sums = ref unsafe(MemoryMarshal.GetReference(columnSums[..row.Length]));
            ref var squares = ref unsafe(MemoryMarshal.GetReference(columnSquares[..row.Length]));
            for (; x + 4 <= row.Length; x += 4)
            {
                // A square of a 16-bit value fits in 32 bits
                var value = Vector128.WidenLower(unsafe(Vector128.CreateScalarUnsafe(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<ushort, byte>(ref Unsafe.Add(ref values, x)))))).AsUInt16());
                var (valueLow, valueHigh) = Vector128.Widen(value);
                var (squareLow, squareHigh) = Vector128.Widen(value * value);
                unsafe { (Vector128.LoadUnsafe(ref sums, (nuint)x) + valueLow.AsInt64()).StoreUnsafe(ref sums, (nuint)x); }
                unsafe { (Vector128.LoadUnsafe(ref sums, (nuint)(x + 2)) + valueHigh.AsInt64()).StoreUnsafe(ref sums, (nuint)(x + 2)); }
                unsafe { (Vector128.LoadUnsafe(ref squares, (nuint)x) + squareLow.AsInt64()).StoreUnsafe(ref squares, (nuint)x); }
                unsafe { (Vector128.LoadUnsafe(ref squares, (nuint)(x + 2)) + squareHigh.AsInt64()).StoreUnsafe(ref squares, (nuint)(x + 2)); }
            }
        }

        for (; x < row.Length; x++)
        {
            long value = row[x];
            columnSums[x] += value;
            columnSquares[x] += value * value;
        }
    }

    /// <summary>Removes the lumas of a row from the sums and the sums of squares of each column.</summary>
    private static void RemoveRow(ReadOnlySpan<ushort> row, Span<long> columnSums, Span<long> columnSquares)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar)
        {
            ref var values = ref unsafe(MemoryMarshal.GetReference(row));
            ref var sums = ref unsafe(MemoryMarshal.GetReference(columnSums[..row.Length]));
            ref var squares = ref unsafe(MemoryMarshal.GetReference(columnSquares[..row.Length]));
            for (; x + 4 <= row.Length; x += 4)
            {
                var value = Vector128.WidenLower(unsafe(Vector128.CreateScalarUnsafe(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<ushort, byte>(ref Unsafe.Add(ref values, x)))))).AsUInt16());
                var (valueLow, valueHigh) = Vector128.Widen(value);
                var (squareLow, squareHigh) = Vector128.Widen(value * value);
                unsafe { (Vector128.LoadUnsafe(ref sums, (nuint)x) - valueLow.AsInt64()).StoreUnsafe(ref sums, (nuint)x); }
                unsafe { (Vector128.LoadUnsafe(ref sums, (nuint)(x + 2)) - valueHigh.AsInt64()).StoreUnsafe(ref sums, (nuint)(x + 2)); }
                unsafe { (Vector128.LoadUnsafe(ref squares, (nuint)x) - squareLow.AsInt64()).StoreUnsafe(ref squares, (nuint)x); }
                unsafe { (Vector128.LoadUnsafe(ref squares, (nuint)(x + 2)) - squareHigh.AsInt64()).StoreUnsafe(ref squares, (nuint)(x + 2)); }
            }
        }

        for (; x < row.Length; x++)
        {
            long value = row[x];
            columnSums[x] -= value;
            columnSquares[x] -= value * value;
        }
    }

    /// <summary>Stores the colors of the first pixels of a row with vectors; returns the number of pixels stored.</summary>
    private int StoreColorsVector(ReadOnlySpan<ushort> mask, Span<byte> row)
    {
        ref var input = ref unsafe(MemoryMarshal.GetReference(mask));
        ref var output = ref unsafe(MemoryMarshal.GetReference(row));
        var x = 0;
        switch (_bytesPerPixel)
        {
            case 1:
            {
                var upper = Vector128.Create((byte)_upper);
                var lower = Vector128.Create((byte)_lower);
                for (; x + 16 <= mask.Length; x += 16)
                {
                    var selected = Vector128.Narrow(unsafe(Vector128.LoadUnsafe(ref input, (nuint)x)), unsafe(Vector128.LoadUnsafe(ref input, (nuint)(x + 8))));
                    unsafe { Vector128.ConditionalSelect(selected, upper, lower).StoreUnsafe(ref output, (nuint)x); }
                }

                break;
            }

            case 2:
            {
                var upper = Vector128.Create((ushort)_upper).AsByte();
                var lower = Vector128.Create((ushort)_lower).AsByte();
                for (; x + 8 <= mask.Length; x += 8)
                {
                    var selected = unsafe(Vector128.LoadUnsafe(ref input, (nuint)x)).AsByte();
                    unsafe { Vector128.ConditionalSelect(selected, upper, lower).StoreUnsafe(ref output, (nuint)(2 * x)); }
                }

                break;
            }

            case 4:
            {
                var upper = Vector128.Create((uint)_upper).AsByte();
                var lower = Vector128.Create((uint)_lower).AsByte();
                for (; x + 8 <= mask.Length; x += 8)
                {
                    var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref input, (nuint)x)).AsInt16());
                    unsafe { Vector128.ConditionalSelect(low.AsByte(), upper, lower).StoreUnsafe(ref output, (nuint)(4 * x)); }
                    unsafe { Vector128.ConditionalSelect(high.AsByte(), upper, lower).StoreUnsafe(ref output, (nuint)((4 * x) + 16)); }
                }

                break;
            }

            case 3:
            {
                // 4 pixels of 3 bytes per 16 bytes written: the last 4 bytes are written again with the next pixels
                var upper = Vector128.Create((byte)_upper, (byte)(_upper >> 8), (byte)(_upper >> 16), (byte)_upper, (byte)(_upper >> 8), (byte)(_upper >> 16), (byte)_upper, (byte)(_upper >> 8), (byte)(_upper >> 16), (byte)_upper, (byte)(_upper >> 8), (byte)(_upper >> 16), (byte)_upper, (byte)(_upper >> 8), (byte)(_upper >> 16), (byte)_upper);
                var lower = Vector128.Create((byte)_lower, (byte)(_lower >> 8), (byte)(_lower >> 16), (byte)_lower, (byte)(_lower >> 8), (byte)(_lower >> 16), (byte)_lower, (byte)(_lower >> 8), (byte)(_lower >> 16), (byte)_lower, (byte)(_lower >> 8), (byte)(_lower >> 16), (byte)_lower, (byte)(_lower >> 8), (byte)(_lower >> 16), (byte)_lower);
                var expand = Vector128.Create((byte)0, 0, 0, 2, 2, 2, 4, 4, 4, 6, 6, 6, 6, 6, 6, 6);
                for (; x + 6 <= mask.Length; x += 4)
                {
                    var selected = Vector128.Shuffle(Vector128.CreateScalar(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<ushort, byte>(ref Unsafe.Add(ref input, x))))).AsByte(), expand);
                    Vector128.ConditionalSelect(selected, upper, lower).CopyTo(row[(3 * x)..]);
                }

                break;
            }

            case 8:
            {
                var upper = Vector128.Create(_upper).AsByte();
                var lower = Vector128.Create(_lower).AsByte();
                for (; x + 4 <= mask.Length; x += 4)
                {
                    var (low, high) = Vector128.Widen(Vector128.WidenLower(unsafe(Vector128.CreateScalarUnsafe(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<ushort, byte>(ref Unsafe.Add(ref input, x)))))).AsInt16()));
                    unsafe { Vector128.ConditionalSelect(low.AsByte(), upper, lower).StoreUnsafe(ref output, (nuint)(8 * x)); }
                    unsafe { Vector128.ConditionalSelect(high.AsByte(), upper, lower).StoreUnsafe(ref output, (nuint)((8 * x) + 16)); }
                }

                break;
            }
        }

        return x;
    }

    private void ApplyGlobal(scoped in PixelLease lease, double level, CancellationToken cancellationToken)
    {
        var samples = ToneSamples.GetSamples(_row, _width);
        var luma = ToneSamples.GetLuma(_row, _width);
        var lowest = GetLowestLumaAbove(level, _max);
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRowBytes(y)[..(_width * _bytesPerPixel)];
            ToneSamples.ReadLuma(_format, row, samples, luma);
            CompareLuma(luma, lowest);
            StoreColors(luma, row);
        }
    }

    private void ApplyAdaptive(scoped in PixelLease lease, CancellationToken cancellationToken)
    {
        // The luminance of the whole frame is kept, since the windows read rows that are already binarized
        var samples = ToneSamples.GetSamples(_row, _width);
        var luma = unsafe(MemoryMarshal.Cast<byte, ushort>(_luma!.Span))[..(_width * _height)];
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ToneSamples.ReadLuma(_format, lease.GetRowBytes(y)[..(_width * _bytesPerPixel)], samples, luma.Slice(y * _width, _width));
        }

        // The sums and the sums of squares of the window rows, per column
        var sums = unsafe(MemoryMarshal.Cast<byte, long>(_sums!.Span))[..(2 * _width)];
        var columnSums = sums[.._width];
        var columnSquares = sums[_width..];
        sums.Clear();

        var options = _options;
        var edgeMode = options.EdgeMode;
        var radius = options.WindowSize / 2;
        var area = (long)options.WindowSize * options.WindowSize;

        // The numerator of the variance fits in 64 bits when area^2 max^2 does: the sums of squares are at most area max^2
        var window = new AdaptiveWindow
        {
            Radius = radius,
            EdgeMode = edgeMode,
            Area = area,
            Sauvola = options.Method == ThresholdMethod.Sauvola,
            Offset = options.Offset * _max,
            K = options.SauvolaK,
            Range = options.SauvolaR * _max,
            ExactInt64 = (UInt128)(ulong)area * (ulong)area * _max * _max <= long.MaxValue,
        };
        var mask = ToneSamples.GetLuma(_row, _width);
        for (var j = -radius; j <= radius; j++)
        {
            var index = ConvolutionPlan.MapIndex(j, _height, edgeMode);
            if (index >= 0)
            {
                AddRow(luma.Slice(index * _width, _width), columnSums, columnSquares);
            }
        }

        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (y > 0)
            {
                var leavingRow = ConvolutionPlan.MapIndex(y - 1 - radius, _height, edgeMode);
                if (leavingRow >= 0)
                {
                    RemoveRow(luma.Slice(leavingRow * _width, _width), columnSums, columnSquares);
                }

                var enteringRow = ConvolutionPlan.MapIndex(y + radius, _height, edgeMode);
                if (enteringRow >= 0)
                {
                    AddRow(luma.Slice(enteringRow * _width, _width), columnSums, columnSquares);
                }
            }

            long sum = 0;
            long squares = 0;
            for (var i = -radius; i <= radius; i++)
            {
                var column = ConvolutionPlan.MapIndex(i, _width, edgeMode);
                if (column >= 0)
                {
                    sum += columnSums[column];
                    squares += columnSquares[column];
                }
            }

            ComputeAdaptiveMask(luma.Slice(y * _width, _width), columnSums, columnSquares, mask, window, sum, squares);
            StoreColors(mask, lease.GetRowBytes(y)[..(_width * _bytesPerPixel)]);
        }
    }

    /// <summary>
    /// Compares each luma of a row to the threshold of its window: <c>0xFFFF</c> in <paramref name="mask"/> when it is greater,
    /// 0 otherwise. <paramref name="sum"/> and <paramref name="squares"/> are the sums of the window of the first pixel.
    /// </summary>
    private static void ComputeAdaptiveMask(ReadOnlySpan<ushort> lumaRow, ReadOnlySpan<long> columnSums, ReadOnlySpan<long> columnSquares, Span<ushort> mask, in AdaptiveWindow window, long sum, long squares)
    {
        var width = lumaRow.Length;
        var radius = window.Radius;

        // Inside the frame, the columns that leave and enter the window are themselves
        var interiorStart = Math.Min(radius, width);
        var interiorEnd = Math.Max(interiorStart, width - radius - 1);
        var x = 0;
        for (; x < interiorStart; x++)
        {
            mask[x] = Compare(lumaRow[x], sum, squares, window);
            MoveWindow(x, width, columnSums, columnSquares, window, ref sum, ref squares);
        }

        var sums = columnSums[..width];
        var squareSums = columnSquares[..width];
        if (!window.Sauvola)
        {
            var area = (double)window.Area;
            var offset = window.Offset;
            for (; x < interiorEnd; x++)
            {
                var threshold = (sum / area) - offset;
                mask[x] = (ushort)-unsafe(Unsafe.BitCast<bool, byte>(lumaRow[x] > threshold));
                sum += sums[x + radius + 1] - sums[x - radius];
            }
        }
        else
        {
            for (; x < interiorEnd; x++)
            {
                mask[x] = Compare(lumaRow[x], sum, squares, window);
                sum += sums[x + radius + 1] - sums[x - radius];
                squares += squareSums[x + radius + 1] - squareSums[x - radius];
            }
        }

        for (; x < width; x++)
        {
            mask[x] = Compare(lumaRow[x], sum, squares, window);
            MoveWindow(x, width, columnSums, columnSquares, window, ref sum, ref squares);
        }
    }

    /// <summary>Gets the mask of a luma: <c>0xFFFF</c> when it is greater than the threshold of its window, 0 otherwise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort Compare(ushort luma, long sum, long squares, in AdaptiveWindow window)
    {
        var area = window.Area;
        var mean = (double)sum / area;
        double threshold;
        if (window.Sauvola)
        {
            // Exact: the numerator of the variance is never negative, and computed in 64 bits only when it cannot overflow
            var numerator = window.ExactInt64 ? (double)((squares * area) - (sum * sum)) : (double)(((Int128)squares * area) - ((Int128)sum * sum));
            var variance = numerator / ((double)area * area);
            threshold = mean * (1 + (window.K * ((Math.Sqrt(variance) / window.Range) - 1)));
        }
        else
        {
            threshold = mean - window.Offset;
        }

        return (ushort)-unsafe(Unsafe.BitCast<bool, byte>(luma > threshold));
    }

    /// <summary>Moves the window of the pixel <paramref name="x"/> one column to the right, mapping the columns outside the frame.</summary>
    private static void MoveWindow(int x, int width, ReadOnlySpan<long> columnSums, ReadOnlySpan<long> columnSquares, in AdaptiveWindow window, ref long sum, ref long squares)
    {
        var leaving = x - window.Radius;
        leaving = (uint)leaving < (uint)width ? leaving : ConvolutionPlan.MapIndex(leaving, width, window.EdgeMode);
        if (leaving >= 0)
        {
            sum -= columnSums[leaving];
            squares -= columnSquares[leaving];
        }

        var entering = x + window.Radius + 1;
        entering = (uint)entering < (uint)width ? entering : ConvolutionPlan.MapIndex(entering, width, window.EdgeMode);
        if (entering >= 0)
        {
            sum += columnSums[entering];
            squares += columnSquares[entering];
        }
    }

    /// <summary>The parameters of the windows of the adaptive methods.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct AdaptiveWindow
    {
        public int Radius { get; init; }

        public ConvolutionEdgeMode EdgeMode { get; init; }

        public long Area { get; init; }

        public bool Sauvola { get; init; }

        public double Offset { get; init; }

        public double K { get; init; }

        public double Range { get; init; }

        /// <summary>Gets a value indicating whether the numerator of the variance always fits in 64 bits.</summary>
        public bool ExactInt64 { get; init; }
    }
}
