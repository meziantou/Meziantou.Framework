using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The accumulator of an auto-crop analysis. The image runs it over every displayed frame and the poster, in that order,
/// once per pass (<see cref="Accumulate{TPixel}"/>, typed dispatch through the frames): one or two border passes, then a
/// bounds pass and, on request, a weights pass. Nothing is written to the pixels.
/// </summary>
/// <remarks>
/// <para>
/// All decisions use integer arithmetic at the storage precision; thresholds are given on the 8-bit scale and multiplied
/// by 257 for 16-bit samples. A fully transparent pixel is read as transparent black, so that hidden colors never count.
/// The luma weights are the Rec. 709 integers of <see cref="PixelConverter.Luma(uint, uint, uint, uint)"/>.
/// </para>
/// <para>
/// The kernels are scalar and sequential; cancellation is observed between row bands. The only allocations are the border
/// color table (at most 255 entries) and the 11 luma buckets, whatever the image size.
/// </para>
/// </remarks>
internal sealed class AutoCropAnalyzer
{
    /// <summary>The smallest width and height of a content box worth cropping to.</summary>
    internal const int MinimumContentSize = 3;

    /// <summary>The fraction of the width and of the height removed from each side of the analysis rectangle by the retry.</summary>
    internal const int RetryInsetDivisor = 20;

    private const int BucketCount = 11;

    private readonly Size _canvas;
    private readonly Dictionary<uint, int> _indexes = [];
    private readonly uint[] _keys;
    private readonly long[] _counts;
    private readonly Rgba64[] _exemplars;
    private readonly long[] _buckets = new long[BucketCount];
    private Phase _phase = Phase.Border;
    private Rectangle _region;
    private int _threshold;
    private int _colorCount;
    private long _borderPixels;
    private int _minX;
    private int _minY;
    private int _maxX;
    private int _maxY;
    private Int128 _momentX;
    private Int128 _momentY;
    private long _weighedFrames;
    private uint _max;

    /// <summary>Starts the first border pass, over the whole canvas.</summary>
    /// <param name="canvas">The canvas size.</param>
    /// <param name="colorThreshold">The color threshold, from 1 to 255.</param>
    public AutoCropAnalyzer(Size canvas, int colorThreshold)
    {
        Debug.Assert(!canvas.IsEmpty);
        Debug.Assert(colorThreshold is >= 1 and <= byte.MaxValue);
        _canvas = canvas;
        _region = new Rectangle(0, 0, canvas.Width, canvas.Height);
        _threshold = colorThreshold;
        _keys = new uint[colorThreshold];
        _counts = new long[colorThreshold];
        _exemplars = new Rgba64[colorThreshold];
    }

    private enum Phase
    {
        Border,
        Bounds,
        Weights,
    }

    /// <summary>Gets the background selected by the last completed border pass.</summary>
    public Rgba64 BackgroundColor { get; private set; }

    /// <summary>
    /// Completes a border pass: the background is the most frequent tracked color (the first encountered among equals).
    /// </summary>
    /// <param name="bucketThreshold">The minimum share of border pixels in the luma bucket of the background, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the border is uniform enough to be a background.</returns>
    public bool CompleteBorderPass(double? bucketThreshold)
    {
        Debug.Assert(_phase == Phase.Border && _colorCount > 0);
        var best = 0;
        for (var i = 1; i < _colorCount; i++)
        {
            if (_counts[i] > _counts[best])
            {
                best = i;
            }
        }

        BackgroundColor = _exemplars[best];
        if (_colorCount < _threshold)
            return true;

        return bucketThreshold is { } threshold && (double)_buckets[GetBucket(_keys[best])] / _borderPixels >= threshold;
    }

    /// <summary>
    /// Starts the retry border pass: half the threshold (rounded up), on the canvas without 1/20 of its width and height on
    /// each side. The threshold and the rectangle stay in effect for the following passes.
    /// </summary>
    public void BeginRetryBorderPass()
    {
        Debug.Assert(_phase == Phase.Border);
        _threshold = (_threshold + 1) / 2;
        var insetX = _canvas.Width / RetryInsetDivisor;
        var insetY = _canvas.Height / RetryInsetDivisor;
        _region = new Rectangle(insetX, insetY, _canvas.Width - (2 * insetX), _canvas.Height - (2 * insetY));
        _indexes.Clear();
        _buckets.AsSpan().Clear();
        _colorCount = 0;
        _borderPixels = 0;
    }

    /// <summary>Starts the bounds pass: the union, over all frames, of the pixels of the analysis rectangle that are not background.</summary>
    public void BeginBoundsPass()
    {
        Debug.Assert(_phase == Phase.Border);
        _phase = Phase.Bounds;
        _minX = int.MaxValue;
        _minY = int.MaxValue;
        _maxX = -1;
        _maxY = -1;
    }

    /// <summary>Gets the content box found by the bounds pass.</summary>
    /// <param name="bounds">The content box.</param>
    /// <returns><see langword="true"/> if there is content and its box is at least <see cref="MinimumContentSize"/> pixels wide and high.</returns>
    public bool TryGetBounds(out Rectangle bounds)
    {
        Debug.Assert(_phase == Phase.Bounds);
        if (_maxX < 0)
        {
            bounds = default;
            return false;
        }

        bounds = Rectangle.FromLTRB(_minX, _minY, _maxX + 1, _maxY + 1);
        return bounds.Width >= MinimumContentSize && bounds.Height >= MinimumContentSize;
    }

    /// <summary>Starts the weights pass, over every pixel of the canvas.</summary>
    public void BeginWeightsPass()
    {
        Debug.Assert(_phase == Phase.Bounds);
        _phase = Phase.Weights;
    }

    /// <summary>
    /// Gets the weights: the exact integer moments <c>sum(m * (2x + 1 - W))</c> and <c>sum(m * (2y + 1 - H))</c>, where
    /// <c>m = (2126 |dR| + 7152 |dG| + 722 |dB|) * alpha</c>, divided once by <c>W * 10000 * max * max * pixels</c>
    /// (respectively <c>H</c>).
    /// </summary>
    public (double X, double Y) GetWeights()
    {
        Debug.Assert(_phase == Phase.Weights && _weighedFrames > 0);
        var scale = 10000d * _max * _max * _canvas.Area * _weighedFrames;
        return ((double)_momentX / (scale * _canvas.Width), (double)_momentY / (scale * _canvas.Height));
    }

    /// <summary>Runs the current pass over one frame.</summary>
    public void Accumulate<TPixel>(scoped in PixelLease lease, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        Debug.Assert(lease.Width == _canvas.Width && lease.Height == _canvas.Height);
        switch (_phase)
        {
            case Phase.Border:
                AccumulateBorder<TPixel>(lease, cancellationToken);
                break;
            case Phase.Bounds:
                AccumulateBounds<TPixel>(lease, cancellationToken);
                break;
            default:
                AccumulateWeights<TPixel>(lease, cancellationToken);
                break;
        }
    }

    /// <summary>Gets the luma bucket of a border color: 11 equal buckets of the 8-bit luma of the color flattened onto white.</summary>
    internal static int GetBucket(uint key)
    {
        var r = key & 0xFF;
        var g = (key >> 8) & 0xFF;
        var b = (key >> 16) & 0xFF;
        var a = key >> 24;
        if (a != byte.MaxValue)
        {
            r = PixelConverter.Flatten(r, byte.MaxValue, a, byte.MaxValue);
            g = PixelConverter.Flatten(g, byte.MaxValue, a, byte.MaxValue);
            b = PixelConverter.Flatten(b, byte.MaxValue, a, byte.MaxValue);
        }

        var luma = PixelConverter.Luma(r, g, b, byte.MaxValue);
        return (int)Math.Min(BucketCount - 1, luma * BucketCount / byte.MaxValue);
    }

    // The one-pixel frame of the analysis rectangle in row-major order, every pixel once
    private void AccumulateBorder<TPixel>(scoped in PixelLease lease, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var region = _region;
        var last = region.Bottom - 1;
        for (var y = region.Top; y <= last; y++)
        {
            if ((y - region.Top) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRow<TPixel>(y).Slice(region.X, region.Width);
            if (y == region.Top || y == last)
            {
                foreach (ref var pixel in row)
                {
                    Tally(ref pixel);
                }
            }
            else
            {
                Tally(ref row[0]);
                if (row.Length > 1)
                {
                    Tally(ref row[^1]);
                }
            }
        }
    }

    private void Tally<TPixel>(ref TPixel pixel)
        where TPixel : unmanaged
    {
        ReadPixel(ref pixel, out var r, out var g, out var b, out var a);
        var key = Is16Bit<TPixel>()
            ? PixelConverter.To8Bit(r) | ((uint)PixelConverter.To8Bit(g) << 8) | ((uint)PixelConverter.To8Bit(b) << 16) | ((uint)PixelConverter.To8Bit(a) << 24)
            : r | (g << 8) | (b << 16) | (a << 24);

        _borderPixels++;
        _buckets[GetBucket(key)]++;
        if (_indexes.TryGetValue(key, out var index))
        {
            _counts[index]++;
        }
        else if (_colorCount < _threshold)
        {
            // Once the table is full, new colors are ignored; the tracked ones keep counting
            index = _colorCount++;
            _indexes.Add(key, index);
            _keys[index] = key;
            _counts[index] = 1;
            _exemplars[index] = Is16Bit<TPixel>()
                ? new Rgba64((ushort)r, (ushort)g, (ushort)b, (ushort)a)
                : new Rgba64(PixelConverter.To16Bit(r), PixelConverter.To16Bit(g), PixelConverter.To16Bit(b), PixelConverter.To16Bit(a));
        }
    }

    // Rows inside the vertical extent found so far can only move the left and right bounds: only the columns outside the
    // horizontal extent are read. The result is the same as testing every pixel.
    private void AccumulateBounds<TPixel>(scoped in PixelLease lease, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var region = _region;
        var background = BackgroundTest.Create<TPixel>(BackgroundColor, _threshold);
        for (var y = region.Top; y < region.Bottom; y++)
        {
            if ((y - region.Top) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRow<TPixel>(y);
            var inside = y >= _minY && y <= _maxY;
            var leftEnd = inside ? _minX : region.Right;
            var first = -1;
            for (var x = region.Left; x < leftEnd; x++)
            {
                if (!background.Matches(ref row[x]))
                {
                    first = x;
                    break;
                }
            }

            if (first < 0 && !inside)
                continue;

            var rightEnd = inside ? _maxX : first;
            var lastContent = first;
            for (var x = region.Right - 1; x > rightEnd; x--)
            {
                if (!background.Matches(ref row[x]))
                {
                    lastContent = x;
                    break;
                }
            }

            if (first >= 0)
            {
                _minX = Math.Min(_minX, first);
            }

            if (lastContent >= 0)
            {
                _maxX = Math.Max(_maxX, lastContent);
                _minY = Math.Min(_minY, y);
                _maxY = Math.Max(_maxY, y);
            }
        }
    }

    private void AccumulateWeights<TPixel>(scoped in PixelLease lease, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var background = BackgroundTest.Create<TPixel>(BackgroundColor, _threshold);
        var width = (long)_canvas.Width;
        var height = (long)_canvas.Height;
        _max = Is16Bit<TPixel>() ? ushort.MaxValue : byte.MaxValue;
        _weighedFrames++;
        for (var y = 0; y < lease.Height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRow<TPixel>(y);
            UInt128 rowMass = 0;
            Int128 rowMoment = 0;
            for (var x = 0; x < row.Length; x++)
            {
                var mass = background.GetMass(ref row[x]);
                if (mass != 0)
                {
                    rowMass += mass;
                    rowMoment += (Int128)mass * ((2L * x) + 1 - width);
                }
            }

            _momentX += rowMoment;
            _momentY += (Int128)rowMass * ((2L * y) + 1 - height);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Is16Bit<TPixel>() => typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16);

    /// <summary>Reads a pixel at the storage precision; a fully transparent pixel is transparent black.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadPixel<TPixel>(ref TPixel pixel, out uint r, out uint g, out uint b, out uint a)
        where TPixel : unmanaged
    {
        PixelConverter.Read(ref pixel, out r, out g, out b, out a);
        if (a == 0)
        {
            r = 0;
            g = 0;
            b = 0;
        }
    }

    /// <summary>The background and the tolerances, at the storage precision of one pixel type.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct BackgroundTest
    {
        private readonly uint _r;
        private readonly uint _g;
        private readonly uint _b;
        private readonly uint _a;
        private readonly uint _colorLimit;
        private readonly uint _alphaLimit;

        private BackgroundTest(uint r, uint g, uint b, uint a, uint colorLimit, uint alphaLimit)
        {
            _r = r;
            _g = g;
            _b = b;
            _a = a;
            _colorLimit = colorLimit;
            _alphaLimit = alphaLimit;
        }

        public static BackgroundTest Create<TPixel>(Rgba64 background, int threshold)
            where TPixel : unmanaged
        {
            // At most 255 * 10000 * 257 = 655,350,000: no overflow in 32 bits
            if (Is16Bit<TPixel>())
                return new(background.R, background.G, background.B, background.A, (uint)threshold * 10000 * 257, (uint)threshold * 257);

            // The background was widened exactly from the same 8-bit format
            return new(PixelConverter.To8Bit(background.R), PixelConverter.To8Bit(background.G), PixelConverter.To8Bit(background.B), PixelConverter.To8Bit(background.A), (uint)threshold * 10000, (uint)threshold);
        }

        /// <summary>Gets a value indicating whether a pixel is background: color difference at most the threshold, alpha difference below it.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches<TPixel>(ref TPixel pixel)
            where TPixel : unmanaged
        {
            ReadPixel(ref pixel, out var r, out var g, out var b, out var a);
            return GetColorDifference(r, g, b) <= _colorLimit && AbsoluteDifference(a, _a) < _alphaLimit;
        }

        /// <summary>Gets the color difference of a pixel from the background multiplied by its alpha (at most 10000 * max * max).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetMass<TPixel>(ref TPixel pixel)
            where TPixel : unmanaged
        {
            ReadPixel(ref pixel, out var r, out var g, out var b, out var a);
            return (ulong)GetColorDifference(r, g, b) * a;
        }

        // At most 10000 * 65535 = 655,350,000: no overflow in 32 bits
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint GetColorDifference(uint r, uint g, uint b)
            => (2126 * AbsoluteDifference(r, _r)) + (7152 * AbsoluteDifference(g, _g)) + (722 * AbsoluteDifference(b, _b));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint AbsoluteDifference(uint a, uint b) => a > b ? a - b : b - a;
    }
}
