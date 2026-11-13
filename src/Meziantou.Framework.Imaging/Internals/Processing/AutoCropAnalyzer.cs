using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
/// Floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) are analyzed as they are stored, in double
/// precision, without clipping: the color of a border pixel is the nearest point of the 8-bit grid (<c>floor(255 v + 1/2)</c>
/// per sample, beyond [0, 1] too), the thresholds are fractions of 1 (<c>threshold / 255</c>), and the weights are
/// sums of doubles. The image checks that every sample is finite first (<see cref="FloatingPointSampleRange"/>).
/// </para>
/// <para>
/// The kernels are sequential, and scalar but for the sums of the weights of <see cref="Rgba32"/>, <see cref="Bgra32"/> and
/// <see cref="Rgba64"/> pixels (<see cref="BackgroundTest.SumMasses"/>: exact integers); cancellation is observed between row bands. The only allocations are the border
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

    // The number of pixels whose weights are summed in 64 bits (see AccumulateWeights)
    private const int WeightChunkLength = 512;

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

    // The floating-point formats: the border colors are keyed by their point of the 8-bit grid, which can be outside
    // [0, 255], and the background is kept as it is stored (allocated by the first floating-point frame only)
    private bool _isFloatingPoint;
    private Dictionary<FloatKey, int>? _floatIndexes;
    private FloatKey[]? _floatKeys;
    private RgbaFloat[]? _floatExemplars;
    private double _floatMomentX;
    private double _floatMomentY;

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

    /// <summary>
    /// Gets the background selected by the last completed border pass. The background of floating-point frames is
    /// <see cref="FloatingPointBackgroundColor"/> clipped to [0, 1] and rounded to 16 bits.
    /// </summary>
    public Rgba64 BackgroundColor { get; private set; }

    /// <summary>Gets the background selected by the last completed border pass of floating-point frames, as stored (transparent black when fully transparent).</summary>
    public RgbaFloat? FloatingPointBackgroundColor { get; private set; }

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

        if (_isFloatingPoint)
        {
            var background = _floatExemplars![best];
            FloatingPointBackgroundColor = background;
            BackgroundColor = new Rgba64(ToUInt16(background.R), ToUInt16(background.G), ToUInt16(background.B), ToUInt16(background.A));
        }
        else
        {
            BackgroundColor = _exemplars[best];
        }

        if (_colorCount < _threshold)
            return true;

        var bucket = _isFloatingPoint ? GetBucket(_floatKeys![best]) : GetBucket(_keys[best]);
        return bucketThreshold is { } threshold && (double)_buckets[bucket] / _borderPixels >= threshold;

        static ushort ToUInt16(float value) => (ushort)PixelConverter.ToIntegerSample(value <= 0 ? 0 : value >= 1 ? 1 : value, ushort.MaxValue);
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
        _floatIndexes?.Clear();
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
    /// (respectively <c>H</c>). The moments of floating-point frames are sums of doubles, of masses whose samples are
    /// fractions (<c>max</c> is 1).
    /// </summary>
    public (double X, double Y) GetWeights()
    {
        Debug.Assert(_phase == Phase.Weights && _weighedFrames > 0);
        if (_isFloatingPoint)
        {
            var floatScale = 10000d * _canvas.Area * _weighedFrames;
            return (_floatMomentX / (floatScale * _canvas.Width), _floatMomentY / (floatScale * _canvas.Height));
        }

        var scale = 10000d * _max * _max * _canvas.Area * _weighedFrames;
        return ((double)_momentX / (scale * _canvas.Width), (double)_momentY / (scale * _canvas.Height));
    }

    /// <summary>Runs the current pass over one frame.</summary>
    public void Accumulate<TPixel>(scoped in PixelLease lease, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        Debug.Assert(lease.Width == _canvas.Width && lease.Height == _canvas.Height);
        _isFloatingPoint = PixelConverter.IsFloat<TPixel>();
        switch (_phase)
        {
            case Phase.Border:
                AccumulateBorder<TPixel>(lease, cancellationToken);
                break;
            case Phase.Bounds when _isFloatingPoint:
                AccumulateBounds<TPixel, FloatBackgroundTest>(lease, FloatBackgroundTest.Create(FloatingPointBackgroundColor!.Value, _threshold), cancellationToken);
                break;
            case Phase.Bounds:
                AccumulateBounds<TPixel, BackgroundTest>(lease, BackgroundTest.Create<TPixel>(BackgroundColor, _threshold), cancellationToken);
                break;
            case Phase.Weights when _isFloatingPoint:
                AccumulateFloatingPointWeights<TPixel>(lease, cancellationToken);
                break;
            default:
                AccumulateWeights<TPixel>(lease, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Gets the luma bucket of a floating-point border color, as <see cref="GetBucket(uint)"/> does for 8-bit colors: the
    /// luma of the point of the 8-bit grid flattened onto white, in 11 equal buckets of [0, 1] (a brighter luma is in the
    /// last bucket, a darker one in the first).
    /// </summary>
    private static int GetBucket(FloatKey key)
    {
        var r = key.R / byte.MaxValue;
        var g = key.G / byte.MaxValue;
        var b = key.B / byte.MaxValue;
        var a = key.A / byte.MaxValue;
        if (a != 1)
        {
            r = (r * a) + (1 - a);
            g = (g * a) + (1 - a);
            b = (b * a) + (1 - a);
        }

        // A luma that is not a number (infinite samples of both signs) is in the first bucket
        var bucket = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) * BucketCount;
        return !(bucket >= 1) ? 0 : bucket >= BucketCount - 1 ? BucketCount - 1 : (int)bucket;
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
        if (PixelConverter.IsFloat<TPixel>())
        {
            TallyFloatingPoint(ref pixel);
            return;
        }

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

    private void TallyFloatingPoint<TPixel>(ref TPixel pixel)
        where TPixel : unmanaged
    {
        ReadFloatingPointPixel(ref pixel, out var r, out var g, out var b, out var a);
        var key = new FloatKey(ToGrid(r), ToGrid(g), ToGrid(b), ToGrid(a));
        _borderPixels++;
        _buckets[GetBucket(key)]++;
        var indexes = _floatIndexes ??= [];
        if (indexes.TryGetValue(key, out var index))
        {
            _counts[index]++;
        }
        else if (_colorCount < _threshold)
        {
            index = _colorCount++;
            indexes.Add(key, index);
            (_floatKeys ??= new FloatKey[_keys.Length])[index] = key;
            _counts[index] = 1;
            (_floatExemplars ??= new RgbaFloat[_keys.Length])[index] = new RgbaFloat((float)r, (float)g, (float)b, (float)a);
        }

        // The nearest point of the 8-bit grid, not clipped; adding 0 turns -0 into +0
        static double ToGrid(double value) => Math.Floor((value * byte.MaxValue) + 0.5) + 0d;
    }

    // Rows inside the vertical extent found so far can only move the left and right bounds: only the columns outside the
    // horizontal extent are read. The result is the same as testing every pixel.
    private void AccumulateBounds<TPixel, TTest>(scoped in PixelLease lease, TTest background, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TTest : struct, IBackgroundTest
    {
        var region = _region;
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

            // Integer pixels skip whole vectors of background first
            var start = typeof(TTest) == typeof(BackgroundTest) ? ((BackgroundTest)(object)background).SkipBackground(row, region.Left, leftEnd) : region.Left;
            for (var x = start; x < leftEnd; x++)
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
            var end = typeof(TTest) == typeof(BackgroundTest) ? ((BackgroundTest)(object)background).SkipBackgroundBackward(row, region.Right - 1, rightEnd) : region.Right - 1;
            for (var x = end; x > rightEnd; x--)
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

            // The moment of the row, sum of mass * (2x + 1 - width), is 2 * sum(mass * x) + (1 - width) * sum(mass): the same
            // integer. Both sums are accumulated in 64 bits over chunks of pixels whose sums cannot overflow (a mass is
            // below 2^46: sum(mass) < 2^55 and sum(mass * (x - chunk start)) < 2^46 * 512 * 511 / 2 < 2^64), then in
            // 128 bits once per chunk
            var row = lease.GetRow<TPixel>(y);
            UInt128 rowMass = 0;
            UInt128 rowWeightedX = 0;
            for (var chunkStart = 0; chunkStart < row.Length; chunkStart += WeightChunkLength)
            {
                var chunk = row.Slice(chunkStart, Math.Min(WeightChunkLength, row.Length - chunkStart));
                background.SumMasses(chunk, out var chunkMass, out var chunkWeightedX);
                rowMass += chunkMass;
                rowWeightedX += chunkWeightedX + ((UInt128)chunkMass * (ulong)chunkStart);
            }

            _momentX += (2 * (Int128)rowWeightedX) + ((1 - width) * (Int128)rowMass);
            _momentY += (Int128)rowMass * ((2L * y) + 1 - height);
        }
    }

    /// <summary>Gets the sums of <see cref="BackgroundTest.SumMasses"/> of a chunk for a background, or of its scalar reference (tests).</summary>
    internal static (ulong Mass, ulong WeightedX) SumMasses<TPixel>(Span<TPixel> chunk, Rgba64 background, bool scalar)
        where TPixel : unmanaged
    {
        var test = BackgroundTest.Create<TPixel>(background, threshold: 0);
        ulong mass = 0;
        ulong weightedX = 0;
        if (scalar)
        {
            test.SumMassesScalar(chunk, 0, ref mass, ref weightedX);
        }
        else
        {
            test.SumMasses(chunk, out mass, out weightedX);
        }

        return (mass, weightedX);
    }

    /// <summary>Gets the first and the last pixels of a row that are not background, or -1, skipping vectors of background first or not (tests).</summary>
    internal static (int First, int Last) FindContent<TPixel>(Span<TPixel> row, Rgba64 background, int threshold, bool scalar)
        where TPixel : unmanaged
    {
        var test = BackgroundTest.Create<TPixel>(background, threshold);
        var first = scalar ? 0 : test.SkipBackground(row, 0, row.Length);
        while (first < row.Length && test.Matches(ref row[first]))
        {
            first++;
        }

        var last = scalar ? row.Length - 1 : test.SkipBackgroundBackward(row, row.Length - 1, -1);
        while (last >= 0 && test.Matches(ref row[last]))
        {
            last--;
        }

        return (first == row.Length ? -1 : first, last);
    }

    // The weights of floating-point frames: the moments of AccumulateWeights, as sums of doubles
    private void AccumulateFloatingPointWeights<TPixel>(scoped in PixelLease lease, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var background = FloatBackgroundTest.Create(FloatingPointBackgroundColor!.Value, _threshold);
        var width = (double)_canvas.Width;
        var height = (double)_canvas.Height;
        _weighedFrames++;
        for (var y = 0; y < lease.Height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRow<TPixel>(y);
            var rowMass = 0d;
            var rowWeightedX = 0d;
            for (var x = 0; x < row.Length; x++)
            {
                var mass = background.GetMass(ref row[x]);
                rowMass += mass;
                rowWeightedX += mass * x;
            }

            _floatMomentX += (2 * rowWeightedX) + ((1 - width) * rowMass);
            _floatMomentY += rowMass * ((2d * y) + 1 - height);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Is16Bit<TPixel>() => typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(La32) || typeof(TPixel) == typeof(Rgb48) || PixelConverter.IsPacked<TPixel>();

    /// <summary>Reads a floating-point pixel as it is stored; a fully transparent pixel is transparent black.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadFloatingPointPixel<TPixel>(ref TPixel pixel, out double r, out double g, out double b, out double a)
        where TPixel : unmanaged
    {
        PixelConverter.ReadFloat(ref pixel, out r, out g, out b, out a);
        if (a == 0)
        {
            r = 0;
            g = 0;
            b = 0;
        }
    }

    /// <summary>The test of background pixels of a pass.</summary>
    private interface IBackgroundTest
    {
        /// <summary>Gets a value indicating whether a pixel is background: color difference at most the threshold, alpha difference below it.</summary>
        bool Matches<TPixel>(ref TPixel pixel)
            where TPixel : unmanaged;
    }

    /// <summary>The color of a floating-point border pixel on the 8-bit grid (the key of the table of border colors).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FloatKey(double R, double G, double B, double A);

    /// <summary>The background and the tolerances of floating-point pixels, as fractions of 1 (<c>threshold / 255</c>).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct FloatBackgroundTest : IBackgroundTest
    {
        private readonly double _r;
        private readonly double _g;
        private readonly double _b;
        private readonly double _a;
        private readonly double _colorLimit;
        private readonly double _alphaLimit;

        private FloatBackgroundTest(RgbaFloat background, int threshold)
        {
            _r = background.R;
            _g = background.G;
            _b = background.B;
            _a = background.A;
            _colorLimit = threshold * 10000d / byte.MaxValue;
            _alphaLimit = threshold / (double)byte.MaxValue;
        }

        public static FloatBackgroundTest Create(RgbaFloat background, int threshold) => new(background, threshold);

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches<TPixel>(ref TPixel pixel)
            where TPixel : unmanaged
        {
            ReadFloatingPointPixel(ref pixel, out var r, out var g, out var b, out var a);
            return GetColorDifference(r, g, b) <= _colorLimit && Math.Abs(a - _a) < _alphaLimit;
        }

        /// <summary>Gets the color difference of a pixel from the background multiplied by its alpha (at most 10000 for samples in [0, 1]).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetMass<TPixel>(ref TPixel pixel)
            where TPixel : unmanaged
        {
            ReadFloatingPointPixel(ref pixel, out var r, out var g, out var b, out var a);
            return GetColorDifference(r, g, b) * a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetColorDifference(double r, double g, double b)
            => (2126 * Math.Abs(r - _r)) + (7152 * Math.Abs(g - _g)) + (722 * Math.Abs(b - _b));
    }

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
    private readonly struct BackgroundTest : IBackgroundTest
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

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches<TPixel>(ref TPixel pixel)
            where TPixel : unmanaged
        {
            ReadPixel(ref pixel, out var r, out var g, out var b, out var a);
            return GetColorDifference(r, g, b) <= _colorLimit && AbsoluteDifference(a, _a) < _alphaLimit;
        }

        /// <summary>
        /// Gets the sum of the masses of the pixels of a chunk of at most <see cref="WeightChunkLength"/> pixels
        /// (<see cref="GetMass"/>), and the sum of the masses multiplied by the index of their pixel in the chunk.
        /// </summary>
        public void SumMasses<TPixel>(Span<TPixel> chunk, out ulong mass, out ulong weightedX)
            where TPixel : unmanaged
        {
            Debug.Assert(chunk.Length <= WeightChunkLength);
            mass = 0;
            weightedX = 0;
            var x = SumMassesVector(chunk, ref mass, ref weightedX);
            SumMassesScalar(chunk, x, ref mass, ref weightedX);
        }

        /// <summary>The reference of <see cref="SumMasses"/>: adds the masses of the pixels from <paramref name="start"/>.</summary>
        public void SumMassesScalar<TPixel>(Span<TPixel> chunk, int start, ref ulong mass, ref ulong weightedX)
            where TPixel : unmanaged
        {
            for (var x = start; x < chunk.Length; x++)
            {
                var pixelMass = GetMass(ref chunk[x]);
                mass += pixelMass;
                weightedX += pixelMass * (ulong)x;
            }
        }

        /// <summary>
        /// Adds the masses of the leading pixels of a chunk in vectors of 4 or 8 pixels (lane <c>l</c> of iteration <c>k</c>
        /// is pixel <c>L k + l</c>): every lane sums its masses <c>S</c> and the running sums <c>Q</c> of its masses, so that
        /// the sum of its masses multiplied by <c>k</c> is <c>K S - Q</c> after <c>K</c> iterations. Every sum is an exact
        /// integer in 64 bits: a mass is below 2^46 and a lane sums at most 128 of them. Returns the number of pixels summed
        /// (0 for the other pixel types).
        /// </summary>
        private int SumMassesVector<TPixel>(Span<TPixel> chunk, ref ulong mass, ref ulong weightedX)
            where TPixel : unmanaged
        {
            if (!IsVectorized<TPixel>())
                return 0;

            ref var start = ref unsafe(Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(chunk)));
            Span<ulong> sums = stackalloc ulong[16];
            int lanes;
            int k;
            if (Vector256.IsHardwareAccelerated && chunk.Length >= 8)
            {
                lanes = 8;
                var reference = new Lanes256<TPixel>(this);
                Vector256<ulong> lowSums = default, highSums = default, lowRunning = default, highRunning = default;
                for (k = 0; (k + 1) * 8 <= chunk.Length; k++)
                {
                    Lanes256<TPixel>.Load(ref start, 8 * k, out var c0, out var c1, out var c2, out var alpha);
                    var difference = reference.GetDifference(c0, c1, c2);
                    var masses = typeof(TPixel) == typeof(Rgba64) ? Lanes256<TPixel>.MultiplyWide(difference, alpha) : Vector256.Widen((difference * alpha).AsUInt32());
                    lowSums += masses.Lower;
                    highSums += masses.Upper;
                    lowRunning += lowSums;
                    highRunning += highSums;
                }

                lowSums.CopyTo(sums);
                highSums.CopyTo(sums[4..]);
                lowRunning.CopyTo(sums[8..]);
                highRunning.CopyTo(sums[12..]);
            }
            else if (chunk.Length >= 4)
            {
                lanes = 4;
                var reference = new Lanes128<TPixel>(this);
                Vector128<ulong> lowSums = default, highSums = default, lowRunning = default, highRunning = default;
                for (k = 0; (k + 1) * 4 <= chunk.Length; k++)
                {
                    Lanes128<TPixel>.Load(ref start, 4 * k, out var c0, out var c1, out var c2, out var alpha);
                    var difference = reference.GetDifference(c0, c1, c2);
                    var masses = typeof(TPixel) == typeof(Rgba64) ? Lanes128<TPixel>.MultiplyWide(difference, alpha) : Vector128.Widen((difference * alpha).AsUInt32());
                    lowSums += masses.Lower;
                    highSums += masses.Upper;
                    lowRunning += lowSums;
                    highRunning += highSums;
                }

                lowSums.CopyTo(sums);
                highSums.CopyTo(sums[2..]);
                lowRunning.CopyTo(sums[8..]);
                highRunning.CopyTo(sums[10..]);
            }
            else
            {
                return 0;
            }

            // Pixel L k + l of lane l: the sum of (L k + l) m is L (K S - Q) + l S
            for (var lane = 0; lane < lanes; lane++)
            {
                var laneMass = sums[lane];
                mass += laneMass;
                weightedX += ((ulong)lanes * (((ulong)k * laneMass) - sums[8 + lane])) + ((ulong)lane * laneMass);
            }

            return lanes * k;
        }

        /// <summary>
        /// Skips the background pixels at the start of <c>[<paramref name="x"/>, <paramref name="end"/>)</c> by whole vectors
        /// of 4 or 8 pixels (<see cref="Matches"/>): returns the first pixel of the first vector that holds content, or the first
        /// pixel after the last whole vector.
        /// </summary>
        public int SkipBackground<TPixel>(Span<TPixel> row, int x, int end)
            where TPixel : unmanaged
        {
            if (!IsVectorized<TPixel>())
                return x;

            ref var start = ref unsafe(Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(row)));
            if (Vector256.IsHardwareAccelerated)
            {
                var reference = new Lanes256<TPixel>(this);
                for (; x + 8 <= end; x += 8)
                {
                    Lanes256<TPixel>.Load(ref start, x, out var c0, out var c1, out var c2, out var alpha);
                    if (reference.Matches(c0, c1, c2, alpha) != Vector256<int>.AllBitsSet)
                        return x;
                }
            }

            var narrow = new Lanes128<TPixel>(this);
            for (; x + 4 <= end; x += 4)
            {
                Lanes128<TPixel>.Load(ref start, x, out var c0, out var c1, out var c2, out var alpha);
                if (narrow.Matches(c0, c1, c2, alpha) != Vector128<int>.AllBitsSet)
                    return x;
            }

            return x;
        }

        /// <summary>
        /// Skips the background pixels at the end of <c>(<paramref name="low"/>, <paramref name="x"/>]</c> by whole vectors
        /// of 4 or 8 pixels: returns the last pixel of the last vector that holds content, or the last pixel before the first
        /// whole vector.
        /// </summary>
        public int SkipBackgroundBackward<TPixel>(Span<TPixel> row, int x, int low)
            where TPixel : unmanaged
        {
            if (!IsVectorized<TPixel>())
                return x;

            ref var start = ref unsafe(Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(row)));
            if (Vector256.IsHardwareAccelerated)
            {
                var reference = new Lanes256<TPixel>(this);
                for (; x - 8 >= low; x -= 8)
                {
                    Lanes256<TPixel>.Load(ref start, x - 7, out var c0, out var c1, out var c2, out var alpha);
                    if (reference.Matches(c0, c1, c2, alpha) != Vector256<int>.AllBitsSet)
                        return x;
                }
            }

            var narrow = new Lanes128<TPixel>(this);
            for (; x - 4 >= low; x -= 4)
            {
                Lanes128<TPixel>.Load(ref start, x - 3, out var c0, out var c1, out var c2, out var alpha);
                if (narrow.Matches(c0, c1, c2, alpha) != Vector128<int>.AllBitsSet)
                    return x;
            }

            return x;
        }

        /// <summary>Gets a value indicating whether the pixels have vector paths (<see cref="Lanes128{TPixel}"/>), and vectors are used.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsVectorized<TPixel>()
            => (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgba64))
                && Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar;

        /// <summary>
        /// The background and the tolerances in the lanes of 4 pixels of <see cref="Rgba32"/>, <see cref="Bgra32"/> or
        /// <see cref="Rgba64"/>, and their samples: the first and third samples are those of the memory order (blue first
        /// for <see cref="Bgra32"/>).
        /// </summary>
        [StructLayout(LayoutKind.Auto)]
        private readonly struct Lanes128<TPixel>
            where TPixel : unmanaged
        {
            private readonly Vector128<int> _firstWeight;
            private readonly Vector128<int> _thirdWeight;
            private readonly Vector128<int> _first;
            private readonly Vector128<int> _second;
            private readonly Vector128<int> _third;
            private readonly Vector128<int> _alpha;
            private readonly Vector128<int> _colorLimit;
            private readonly Vector128<int> _alphaLimit;

            public Lanes128(BackgroundTest test)
            {
                var bgr = typeof(TPixel) == typeof(Bgra32);
                _firstWeight = Vector128.Create(bgr ? 722 : 2126);
                _thirdWeight = Vector128.Create(bgr ? 2126 : 722);
                _first = Vector128.Create((int)(bgr ? test._b : test._r));
                _second = Vector128.Create((int)test._g);
                _third = Vector128.Create((int)(bgr ? test._r : test._b));
                _alpha = Vector128.Create((int)test._a);
                _colorLimit = Vector128.Create((int)test._colorLimit);
                _alphaLimit = Vector128.Create((int)test._alphaLimit);
            }

            /// <summary>Reads the samples of the 4 pixels from pixel <paramref name="pixel"/>.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Load(ref byte start, int pixel, out Vector128<int> first, out Vector128<int> second, out Vector128<int> third, out Vector128<int> alpha)
            {
                if (typeof(TPixel) == typeof(Rgba64))
                {
                    var low = unsafe(Vector128.LoadUnsafe(ref start, (nuint)(8 * pixel))).AsUInt64();
                    var high = unsafe(Vector128.LoadUnsafe(ref start, (nuint)((8 * pixel) + 16))).AsUInt64();
                    var firstSecond = Vector128.Narrow(low, high).AsInt32();
                    var thirdAlpha = Vector128.Narrow(Vector128.ShiftRightLogical(low, 32), Vector128.ShiftRightLogical(high, 32)).AsInt32();
                    var mask = Vector128.Create(0xFFFF);
                    first = firstSecond & mask;
                    second = Vector128.ShiftRightLogical(firstSecond, 16);
                    third = thirdAlpha & mask;
                    alpha = Vector128.ShiftRightLogical(thirdAlpha, 16);
                }
                else
                {
                    var pixels = unsafe(Vector128.LoadUnsafe(ref start, (nuint)(4 * pixel))).AsInt32();
                    var mask = Vector128.Create(0xFF);
                    first = pixels & mask;
                    second = Vector128.ShiftRightLogical(pixels, 8) & mask;
                    third = Vector128.ShiftRightLogical(pixels, 16) & mask;
                    alpha = Vector128.ShiftRightLogical(pixels, 24);
                }
            }

            /// <summary>The color differences of <see cref="GetColorDifference"/>, below 2^30.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vector128<int> GetDifference(Vector128<int> first, Vector128<int> second, Vector128<int> third)
                => (Vector128.Abs(first - _first) * _firstWeight) + (Vector128.Abs(second - _second) * Vector128.Create(7152)) + (Vector128.Abs(third - _third) * _thirdWeight);

            /// <summary>The results of <see cref="Matches"/>, all bits set for background pixels: transparent pixels are transparent black.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vector128<int> Matches(Vector128<int> first, Vector128<int> second, Vector128<int> third, Vector128<int> alpha)
            {
                var transparent = Vector128.Equals(alpha, Vector128<int>.Zero);
                var difference = GetDifference(Vector128.AndNot(first, transparent), Vector128.AndNot(second, transparent), Vector128.AndNot(third, transparent));
                return Vector128.LessThanOrEqual(difference, _colorLimit) & Vector128.LessThan(Vector128.Abs(alpha - _alpha), _alphaLimit);
            }

            /// <summary>The masses of 16-bit pixels: a difference (below 2^30) times alpha (below 2^16), as two products below 2^31, of its low 15 bits and of the others.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static (Vector128<ulong> Lower, Vector128<ulong> Upper) MultiplyWide(Vector128<int> difference, Vector128<int> alpha)
            {
                var low = Vector128.Widen((difference & Vector128.Create(0x7FFF)).AsUInt32() * alpha.AsUInt32());
                var high = Vector128.Widen(Vector128.ShiftRightLogical(difference, 15).AsUInt32() * alpha.AsUInt32());
                return (low.Lower + Vector128.ShiftLeft(high.Lower, 15), low.Upper + Vector128.ShiftLeft(high.Upper, 15));
            }
        }

        /// <summary>The lanes of 8 pixels of <see cref="Lanes128{TPixel}"/>.</summary>
        [StructLayout(LayoutKind.Auto)]
        private readonly struct Lanes256<TPixel>
            where TPixel : unmanaged
        {
            private readonly Vector256<int> _firstWeight;
            private readonly Vector256<int> _thirdWeight;
            private readonly Vector256<int> _first;
            private readonly Vector256<int> _second;
            private readonly Vector256<int> _third;
            private readonly Vector256<int> _alpha;
            private readonly Vector256<int> _colorLimit;
            private readonly Vector256<int> _alphaLimit;

            public Lanes256(BackgroundTest test)
            {
                var bgr = typeof(TPixel) == typeof(Bgra32);
                _firstWeight = Vector256.Create(bgr ? 722 : 2126);
                _thirdWeight = Vector256.Create(bgr ? 2126 : 722);
                _first = Vector256.Create((int)(bgr ? test._b : test._r));
                _second = Vector256.Create((int)test._g);
                _third = Vector256.Create((int)(bgr ? test._r : test._b));
                _alpha = Vector256.Create((int)test._a);
                _colorLimit = Vector256.Create((int)test._colorLimit);
                _alphaLimit = Vector256.Create((int)test._alphaLimit);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Load(ref byte start, int pixel, out Vector256<int> first, out Vector256<int> second, out Vector256<int> third, out Vector256<int> alpha)
            {
                if (typeof(TPixel) == typeof(Rgba64))
                {
                    var low = unsafe(Vector256.LoadUnsafe(ref start, (nuint)(8 * pixel))).AsUInt64();
                    var high = unsafe(Vector256.LoadUnsafe(ref start, (nuint)((8 * pixel) + 32))).AsUInt64();
                    var firstSecond = Vector256.Narrow(low, high).AsInt32();
                    var thirdAlpha = Vector256.Narrow(Vector256.ShiftRightLogical(low, 32), Vector256.ShiftRightLogical(high, 32)).AsInt32();
                    var mask = Vector256.Create(0xFFFF);
                    first = firstSecond & mask;
                    second = Vector256.ShiftRightLogical(firstSecond, 16);
                    third = thirdAlpha & mask;
                    alpha = Vector256.ShiftRightLogical(thirdAlpha, 16);
                }
                else
                {
                    var pixels = unsafe(Vector256.LoadUnsafe(ref start, (nuint)(4 * pixel))).AsInt32();
                    var mask = Vector256.Create(0xFF);
                    first = pixels & mask;
                    second = Vector256.ShiftRightLogical(pixels, 8) & mask;
                    third = Vector256.ShiftRightLogical(pixels, 16) & mask;
                    alpha = Vector256.ShiftRightLogical(pixels, 24);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vector256<int> GetDifference(Vector256<int> first, Vector256<int> second, Vector256<int> third)
                => (Vector256.Abs(first - _first) * _firstWeight) + (Vector256.Abs(second - _second) * Vector256.Create(7152)) + (Vector256.Abs(third - _third) * _thirdWeight);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vector256<int> Matches(Vector256<int> first, Vector256<int> second, Vector256<int> third, Vector256<int> alpha)
            {
                var transparent = Vector256.Equals(alpha, Vector256<int>.Zero);
                var difference = GetDifference(Vector256.AndNot(first, transparent), Vector256.AndNot(second, transparent), Vector256.AndNot(third, transparent));
                return Vector256.LessThanOrEqual(difference, _colorLimit) & Vector256.LessThan(Vector256.Abs(alpha - _alpha), _alphaLimit);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static (Vector256<ulong> Lower, Vector256<ulong> Upper) MultiplyWide(Vector256<int> difference, Vector256<int> alpha)
            {
                var low = Vector256.Widen((difference & Vector256.Create(0x7FFF)).AsUInt32() * alpha.AsUInt32());
                var high = Vector256.Widen(Vector256.ShiftRightLogical(difference, 15).AsUInt32() * alpha.AsUInt32());
                return (low.Lower + Vector256.ShiftLeft(high.Lower, 15), low.Upper + Vector256.ShiftLeft(high.Upper, 15));
            }
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
