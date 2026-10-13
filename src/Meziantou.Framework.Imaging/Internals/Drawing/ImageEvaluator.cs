using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the colors an image paints on the pixels of a row, in the pixel format of the frame that is drawn on.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> The color of a pixel is the color of the image at the position <c>(u, v)</c> its center
/// <c>(x + 1/2, y + 1/2)</c> comes from through the inverse of the transformation of the image. The pixel
/// <c>(i, j)</c> of the image covers the square from <c>(i, j)</c> to <c>(i + 1, j + 1)</c>.
/// </para>
/// <list type="bullet">
/// <item><description>Nearest: the pixel <c>(floor(u), floor(v))</c>.</description></item>
/// <item><description>
/// Bilinear: with <c>i = floor(u - 1/2)</c>, <c>fx = u - 1/2 - i</c>, and the same for <c>j</c> and <c>fy</c>, the pixels
/// <c>(i, j)</c>, <c>(i + 1, j)</c>, <c>(i, j + 1)</c> and <c>(i + 1, j + 1)</c> with the weights <c>(1 - fx)(1 - fy)</c>,
/// <c>fx (1 - fy)</c>, <c>(1 - fx) fy</c> and <c>fx fy</c>. The alpha is the weighted sum of the alphas, and each color the
/// weighted sum of the colors multiplied by their alpha, divided by that alpha, all at 16 bits and rounded as
/// <c>floor(v + 1/2 + 2^-20)</c>. A pixel whose weight is zero is not read: when <c>fx</c> and <c>fy</c> are zero, the
/// color is the pixel, exactly.
/// </description></item>
/// </list>
/// <para>
/// <b>Smooth.</b> With <c>(u, v)</c> = <c>(a x + c y + e, b x + d y + f)</c>, a pixel of the canvas covers
/// <c>sx = sqrt(a^2 + c^2)</c> columns and <c>sy = sqrt(b^2 + d^2)</c> rows of the image, each limited to the size of
/// the region. When neither exceeds 1, the image is not reduced and the color is the bilinear one, bit for bit.
/// Otherwise, with both brought up to 1, the pixel <c>(i, j)</c> has the weight <c>wx(i) wy(j)</c>, where
/// <c>wx(i) = 1 - |i + 1/2 - u| / sx</c> for the columns that make it positive (and the same for the rows): the
/// weights of the bilinear interpolation, stretched over what the pixel of the canvas covers. For each row in
/// ascending order, <c>Sa = sum wx(i) a(i, j)</c> and <c>Sc = sum (wx(i) a(i, j)) c(i, j)</c> over its columns in
/// ascending order, then <c>Ta += wy(j) Sa</c> and <c>Tc += wy(j) Sc</c>. The alpha is
/// <c>Ta / (sum wx sum wy)</c>, the sums being over every pixel of the support, in the region or not, and each color
/// is <c>Tc / Ta</c>, rounded as above.
/// </para>
/// <para>
/// A pixel outside the region of the image is, by wrap mode: transparent, the nearest pixel of the region, or the
/// pixel the repeated or mirrored region has there. The colors are converted to the pixel format of the frame last, as
/// any brush color is.
/// </para>
/// <para>
/// <b>Method.</b> When the rows of the canvas stay rows of the image (no rotation, no skew), the two rows of the image
/// around a row of the canvas are converted once and kept until another row needs their place: the image is read row
/// by row. Otherwise each pixel is read on its own. A reduced image is read the same two ways, one row of the image at
/// a time for every pixel of a run, with the same operations in the same order.
/// </para>
/// </remarks>
internal sealed class ImageEvaluator : PaintEvaluator, IDisposable
{
    private const double RoundingOffset = 0.5 + (1.0 / 1048576);

    /// <summary>The largest magnitude of a position in the image: beyond it, every position counts as that far.</summary>
    private const double MaxPosition = 1 << 30;

    /// <summary>The values kept for each pixel of a run of a reduced image: the sums of the alpha and of the three colors, and the sum of the weights of its columns.</summary>
    private const int ValuesPerSum = 5;

    private readonly AllocationScope _scope;
    private PooledBuffer? _colors;
    private PooledBuffer? _samples;
    private int _width;
    private PooledBuffer? _rows;
    private int _rowCapacity;
    private int _cachedRow0;
    private int _cachedRow1;

    private ImageFrame? _source;
    private ImageFrame? _destination;
    private PixelStorage? _storage;
    private long _token;
    private int _left;
    private int _top;
    private int _regionWidth;
    private int _regionHeight;
    private Affine2D _inverse;
    private bool _isEmpty;
    private bool _keepsRows;
    private ImageWrapMode _wrapMode;
    private bool _isNearest;
    private bool _isReduced;
    private double _spanX;
    private double _spanY;
    private PooledBuffer? _sums;
    private int _sumCapacity;

    public ImageEvaluator(AllocationScope scope)
    {
        _scope = scope;
    }

    private Span<Rgba64> Samples => unsafe(MemoryMarshal.Cast<byte, Rgba64>(_samples!.Span));

    /// <summary>Converts an opacity to a coverage: the nearest multiple of 1/65536.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The opacity is not between 0 and 1.</exception>
    public static uint GetOpacityCoverage(double opacity, string paramName)
    {
        if (!(opacity >= 0 && opacity <= 1))
            throw new ArgumentOutOfRangeException(paramName, opacity, "The opacity must be between 0 and 1.");

        return (uint)Math.Floor((opacity * CoverageRasterizer.FullCoverage) + 0.5);
    }

    /// <summary>Sets the image and its placement, and rents the rows the evaluation needs. The image is not read yet.</summary>
    /// <param name="source">The frame whose pixels are painted.</param>
    /// <param name="region">The pixels of the frame that are painted, or <see langword="null"/> for all of them.</param>
    /// <param name="transform">The transformation that places the image on the canvas.</param>
    /// <param name="wrapMode">What is painted outside the region.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="destination">A frame that is drawn on: the colors are converted to its pixel format.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="regionParameterName">The name of the parameter the region comes from, for the exception.</param>
    /// <exception cref="ObjectDisposedException">The frame no longer belongs to a live image.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The region is not inside the frame.</exception>
    /// <exception cref="ImageResourceLimitException">The rows exceed the allocation limit.</exception>
    public void Prepare(ImageFrame source, Rectangle? region, AffineTransform transform, ImageWrapMode wrapMode, ImageSampling sampling, ImageFrame destination, int width, string regionParameterName)
    {
        var storage = source.GetStorage();
        var area = region ?? new Rectangle(0, 0, storage.Width, storage.Height);
        if (area.Left < 0 || area.Top < 0 || area.Right > storage.Width || area.Bottom > storage.Height)
            throw new ArgumentOutOfRangeException(regionParameterName, area, "The region is not inside the frame.");

        _source = source;
        _destination = destination;
        _storage = storage;
        _left = area.Left;
        _top = area.Top;
        _regionWidth = area.Width;
        _regionHeight = area.Height;
        _wrapMode = wrapMode;
        _isNearest = sampling == ImageSampling.Nearest;
        _isEmpty = area.IsEmpty || !new Affine2D(transform.M11, transform.M12, transform.M21, transform.M22, transform.OffsetX, transform.OffsetY).TryInvert(out _inverse);

        // The row of the image a pixel reads does not depend on its column
        _keepsRows = _inverse.B == 0;

        // What a pixel of the canvas covers of the image: more than a pixel when the image is reduced
        _isReduced = false;
        if (sampling == ImageSampling.Smooth && !_isEmpty)
        {
            _spanX = Math.Min(Math.Sqrt((_inverse.A * _inverse.A) + (_inverse.C * _inverse.C)), _regionWidth);
            _spanY = Math.Min(Math.Sqrt((_inverse.B * _inverse.B) + (_inverse.D * _inverse.D)), _regionHeight);
            _isReduced = _spanX > 1 || _spanY > 1;
            _spanX = Math.Max(_spanX, 1);
            _spanY = Math.Max(_spanY, 1);
        }

        if (width > _width)
        {
            _colors?.Dispose();
            _samples?.Dispose();
            _colors = _samples = null;
            _width = 0;
            _colors = ResampleWeights.Rent<ushort>(_scope, 4L * width);
            _samples = ResampleWeights.Rent<Rgba64>(_scope, width);
            _width = width;
        }

        if (_isReduced && _keepsRows && width > _sumCapacity)
        {
            _sums?.Dispose();
            _sums = null;
            _sumCapacity = 0;
            _sums = ResampleWeights.Rent<double>(_scope, (long)ValuesPerSum * width);
            _sumCapacity = width;
        }

        if (_keepsRows && !_isEmpty && _regionWidth > _rowCapacity)
        {
            _rows?.Dispose();
            _rows = null;
            _rowCapacity = 0;
            _rows = ResampleWeights.Rent<Rgba64>(_scope, 2L * _regionWidth);
            _rowCapacity = _regionWidth;
        }
    }

    /// <summary>Takes the lease of the image: its pixels cannot change while a shape is painted with them.</summary>
    /// <exception cref="InvalidOperationException">The image is being drawn on, or its pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException">The frame no longer belongs to a live image.</exception>
    public override void Begin()
    {
        if (_isEmpty)
            return;

        _token = _storage!.AcquireLease().Token;
        _cachedRow0 = _cachedRow1 = int.MinValue;
    }

    public override void End()
    {
        if (_token != 0)
        {
            _storage!.Owner.ReleaseLease(_storage, _token);
            _token = 0;
        }
    }

    public override ReadOnlySpan<ushort> Evaluate(int y, int x, int count)
    {
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span))[..(count * 4)];
        if (_isEmpty)
        {
            colors.Clear();
            return colors;
        }

        var samples = Samples[..count];
        if (_isReduced)
        {
            if (_keepsRows)
            {
                ReduceRows(y, x, samples);
            }
            else
            {
                ReducePixels(y, x, samples);
            }
        }
        else if (_keepsRows)
        {
            EvaluateRows(y, x, samples);
        }
        else
        {
            EvaluatePixels(y, x, samples);
        }

        _destination!.ConvertDrawingColors(samples, colors);
        return colors;
    }

    public void Dispose()
    {
        End();
        _colors?.Dispose();
        _samples?.Dispose();
        _rows?.Dispose();
        _sums?.Dispose();
        _colors = _samples = _rows = _sums = null;
        _width = 0;
        _rowCapacity = 0;
        _sumCapacity = 0;
        _source = null;
        _destination = null;
        _storage = null;
    }

    /// <summary>Evaluates a run whose pixels all read the same two rows of the image.</summary>
    private void EvaluateRows(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        var v = _inverse.TransformY(x + 0.5, centerY);
        Split(v, out var row, out var fy);
        var hasRow0 = TryWrap(row, _top, _regionHeight, out var row0);
        var hasRow1 = false;
        var row1 = 0;
        if (fy != 0)
        {
            hasRow1 = TryWrap(row + 1, _top, _regionHeight, out row1);
        }

        if (!hasRow0 && !hasRow1)
        {
            samples.Clear();
            return;
        }

        // The rows of the image, converted once and kept
        var first = hasRow0 ? LoadRow(row0, hasRow1 ? row1 : row0) : default;
        var second = hasRow1 ? LoadRow(row1, hasRow0 ? row0 : row1) : default;
        for (var i = 0; i < samples.Length; i++)
        {
            var u = _inverse.TransformX(x + i + 0.5, centerY);
            Split(u, out var column, out var fx);
            var hasColumn0 = TryWrap(column, _left, _regionWidth, out var column0);
            var hasColumn1 = false;
            var column1 = 0;
            if (fx != 0)
            {
                hasColumn1 = TryWrap(column + 1, _left, _regionWidth, out column1);
            }

            if (fx == 0 && fy == 0)
            {
                // The center of a pixel of the image: that pixel, exactly
                samples[i] = hasRow0 && hasColumn0 ? first[column0] : default;
                continue;
            }

            var accumulator = default(Accumulator);
            if (hasRow0)
            {
                if (hasColumn0)
                {
                    accumulator.Add(first[column0], (1 - fx) * (1 - fy));
                }

                if (hasColumn1)
                {
                    accumulator.Add(first[column1], fx * (1 - fy));
                }
            }

            if (hasRow1)
            {
                if (hasColumn0)
                {
                    accumulator.Add(second[column0], (1 - fx) * fy);
                }

                if (hasColumn1)
                {
                    accumulator.Add(second[column1], fx * fy);
                }
            }

            samples[i] = accumulator.ToColor();
        }
    }

    /// <summary>Evaluates a run whose pixels read any pixel of the image.</summary>
    private void EvaluatePixels(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        for (var i = 0; i < samples.Length; i++)
        {
            var centerX = x + i + 0.5;
            Split(_inverse.TransformX(centerX, centerY), out var column, out var fx);
            Split(_inverse.TransformY(centerX, centerY), out var row, out var fy);
            if (fx == 0 && fy == 0)
            {
                samples[i] = TryRead(column, row, out var pixel) ? pixel : default;
                continue;
            }

            var accumulator = default(Accumulator);
            if (TryRead(column, row, out var color))
            {
                accumulator.Add(color, (1 - fx) * (1 - fy));
            }

            if (fx != 0 && TryRead(column + 1, row, out color))
            {
                accumulator.Add(color, fx * (1 - fy));
            }

            if (fy != 0)
            {
                if (TryRead(column, row + 1, out color))
                {
                    accumulator.Add(color, (1 - fx) * fy);
                }

                if (fx != 0 && TryRead(column + 1, row + 1, out color))
                {
                    accumulator.Add(color, fx * fy);
                }
            }

            samples[i] = accumulator.ToColor();
        }
    }

    /// <summary>Evaluates a run of a reduced image whose pixels all read the same rows of the image: one row at a time, for the whole run.</summary>
    private void ReduceRows(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        var v = Limit(_inverse.TransformY(x + 0.5, centerY));
        GetSupport(v, _spanY, out var firstRow, out var lastRow);
        var sums = unsafe(MemoryMarshal.Cast<byte, double>(_sums!.Span))[..(samples.Length * ValuesPerSum)];
        sums.Clear();
        var rows = unsafe(MemoryMarshal.Cast<byte, Rgba64>(_rows!.Span))[.._regionWidth];
        _cachedRow0 = _cachedRow1 = int.MinValue;
        var totalY = 0d;
        for (var row = firstRow; row <= lastRow; row++)
        {
            var weightY = GetWeight(row, v, _spanY);
            if (!(weightY > 0))
                continue;

            totalY += weightY;
            var hasRow = TryWrap(row, _top, _regionHeight, out var wrapped);
            if (hasRow)
            {
                _source!.ReadRgba64(_storage!, _top + wrapped, _left, rows);
            }

            for (var i = 0; i < samples.Length; i++)
            {
                var u = Limit(_inverse.TransformX(x + i + 0.5, centerY));
                GetSupport(u, _spanX, out var firstColumn, out var lastColumn);
                var sum = default(RowSum);
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var weightX = GetWeight(column, u, _spanX);
                    if (!(weightX > 0))
                        continue;

                    sum.Total += weightX;
                    if (hasRow && TryWrap(column, _left, _regionWidth, out var index))
                    {
                        sum.Add(rows[index], weightX);
                    }
                }

                var entry = sums.Slice(i * ValuesPerSum, ValuesPerSum);
                entry[0] += weightY * sum.Alpha;
                entry[1] += weightY * sum.Red;
                entry[2] += weightY * sum.Green;
                entry[3] += weightY * sum.Blue;
                entry[4] = sum.Total;
            }
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var entry = sums.Slice(i * ValuesPerSum, ValuesPerSum);
            samples[i] = ToColor(entry[0], entry[1], entry[2], entry[3], entry[4] * totalY);
        }
    }

    /// <summary>Evaluates a run of a reduced image whose pixels read any pixel of the image.</summary>
    private void ReducePixels(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        for (var i = 0; i < samples.Length; i++)
        {
            var centerX = x + i + 0.5;
            var u = Limit(_inverse.TransformX(centerX, centerY));
            var v = Limit(_inverse.TransformY(centerX, centerY));
            GetSupport(u, _spanX, out var firstColumn, out var lastColumn);
            GetSupport(v, _spanY, out var firstRow, out var lastRow);
            double alpha = 0, red = 0, green = 0, blue = 0, totalX = 0, totalY = 0;
            for (var row = firstRow; row <= lastRow; row++)
            {
                var weightY = GetWeight(row, v, _spanY);
                if (!(weightY > 0))
                    continue;

                totalY += weightY;
                var sum = default(RowSum);
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var weightX = GetWeight(column, u, _spanX);
                    if (!(weightX > 0))
                        continue;

                    sum.Total += weightX;
                    if (TryRead(column, row, out var color))
                    {
                        sum.Add(color, weightX);
                    }
                }

                alpha += weightY * sum.Alpha;
                red += weightY * sum.Red;
                green += weightY * sum.Green;
                blue += weightY * sum.Blue;
                totalX = sum.Total;
            }

            samples[i] = ToColor(alpha, red, green, blue, totalX * totalY);
        }
    }

    /// <summary>Gets the rows or the columns of the image a pixel of the canvas reads when the image is reduced: those whose center is within the span of its position.</summary>
    private static void GetSupport(double position, double span, out int first, out int last)
    {
        first = (int)Math.Floor(position - span - 0.5) + 1;
        last = (int)Math.Ceiling(position + span - 0.5) - 1;
    }

    /// <summary>Gets the weight of a row or of a column of the image for a position: 1 at its center, 0 a span away.</summary>
    private static double GetWeight(int index, double position, double span) => 1 - (Math.Abs(index + 0.5 - position) / span);

    /// <summary>Brings a coordinate of the image within the positions that count. Written so that a coordinate that is not a number is far away.</summary>
    private static double Limit(double coordinate) => Math.Abs(coordinate) < MaxPosition ? coordinate : coordinate < 0 ? -MaxPosition : MaxPosition;

    /// <summary>Makes a color of the sums of a reduced image: the alpha over the sum of every weight, the colors over the alpha.</summary>
    private static Rgba64 ToColor(double alpha, double red, double green, double blue, double total)
    {
        // Written so that sums that are not numbers give no color
        if (!(total > 0 && alpha > 0))
            return default;

        var roundedAlpha = Round(alpha / total);
        return roundedAlpha == 0 ? default : new Rgba64(Round(red / alpha), Round(green / alpha), Round(blue / alpha), roundedAlpha);
    }

    private static ushort Round(double value)
    {
        var rounded = Math.Floor(value + RoundingOffset);
        return rounded <= 0 ? (ushort)0 : rounded >= ushort.MaxValue ? ushort.MaxValue : (ushort)rounded;
    }

    /// <summary>
    /// Gets the first of the two pixels around a coordinate of the image, in the coordinates of the frame, and the weight
    /// of the second one. Nearest sampling has one pixel, the one that contains the coordinate.
    /// </summary>
    private void Split(double coordinate, out int index, out double fraction)
    {
        // Written so that a coordinate that is not a number is far away
        if (!(Math.Abs(coordinate) < MaxPosition))
        {
            coordinate = coordinate < 0 ? -MaxPosition : MaxPosition;
        }

        if (_isNearest)
        {
            index = (int)Math.Floor(coordinate);
            fraction = 0;
            return;
        }

        var position = coordinate - 0.5;
        var floor = Math.Floor(position);
        index = (int)floor;
        fraction = position - floor;
    }

    /// <summary>Brings a row or a column of the image into the region, by the wrap mode.</summary>
    /// <param name="index">The row or the column, in the coordinates of the frame.</param>
    /// <param name="origin">The first row or column of the region.</param>
    /// <param name="size">The height or the width of the region.</param>
    /// <param name="result">The row or the column, from the first one of the region.</param>
    /// <returns><see langword="false"/> when there is no pixel there: outside the region, without wrapping.</returns>
    private bool TryWrap(int index, int origin, int size, out int result)
    {
        var offset = (long)index - origin;
        if ((ulong)offset < (ulong)size)
        {
            result = (int)offset;
            return true;
        }

        switch (_wrapMode)
        {
            case ImageWrapMode.Pad:
                result = offset < 0 ? 0 : size - 1;
                return true;

            case ImageWrapMode.Repeat:
                offset %= size;
                result = (int)(offset < 0 ? offset + size : offset);
                return true;

            case ImageWrapMode.Reflect:
                var period = 2L * size;
                offset %= period;
                if (offset < 0)
                {
                    offset += period;
                }

                result = (int)(offset < size ? offset : period - 1 - offset);
                return true;

            default:
                result = 0;
                return false;
        }
    }

    /// <summary>Gets a row of the region converted to 16-bit colors, from the two rows that are kept.</summary>
    /// <param name="row">The row, from the first one of the region.</param>
    /// <param name="other">The other row the caller needs: it is not replaced.</param>
    private ReadOnlySpan<Rgba64> LoadRow(int row, int other)
    {
        var rows = unsafe(MemoryMarshal.Cast<byte, Rgba64>(_rows!.Span));
        if (_cachedRow0 == row)
            return rows[.._regionWidth];

        if (_cachedRow1 == row)
            return rows.Slice(_rowCapacity, _regionWidth);

        var useSecond = _cachedRow0 == other;
        var target = useSecond ? rows.Slice(_rowCapacity, _regionWidth) : rows[.._regionWidth];
        _source!.ReadRgba64(_storage!, _top + row, _left, target);
        if (useSecond)
        {
            _cachedRow1 = row;
        }
        else
        {
            _cachedRow0 = row;
        }

        return target;
    }

    /// <summary>Reads a pixel of the image.</summary>
    /// <returns><see langword="false"/> when there is no pixel there.</returns>
    private bool TryRead(int column, int row, out Rgba64 color)
    {
        color = default;
        if (!TryWrap(column, _left, _regionWidth, out var x) || !TryWrap(row, _top, _regionHeight, out var y))
            return false;

        _source!.ReadRgba64(_storage!, _top + y, _left + x, new Span<Rgba64>(ref color));
        return true;
    }

    /// <summary>The weighted sum of colors multiplied by their alpha.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Accumulator
    {
        private double _red;
        private double _green;
        private double _blue;
        private double _alpha;

        public void Add(Rgba64 color, double weight)
        {
            var alpha = weight * color.A;
            _red += alpha * color.R;
            _green += alpha * color.G;
            _blue += alpha * color.B;
            _alpha += alpha;
        }

        public readonly Rgba64 ToColor()
        {
            var alpha = Round(_alpha);
            if (alpha == 0)
                return default;

            return new Rgba64(Round(_red / _alpha), Round(_green / _alpha), Round(_blue / _alpha), alpha);
        }
    }

    /// <summary>The weighted sums of the pixels of a row of the image a pixel of the canvas reads, and the sum of the weights.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct RowSum
    {
        public double Alpha;
        public double Red;
        public double Green;
        public double Blue;
        public double Total;

        public void Add(Rgba64 color, double weight)
        {
            var alpha = weight * color.A;
            Alpha += alpha;
            Red += alpha * color.R;
            Green += alpha * color.G;
            Blue += alpha * color.B;
        }
    }
}
