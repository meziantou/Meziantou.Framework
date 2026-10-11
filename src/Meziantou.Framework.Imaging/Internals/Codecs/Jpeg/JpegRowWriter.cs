using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns decoded component planes into output rows: chroma upsampling to the frame size,
/// color conversion to the source layout of the sink (<see cref="PixelFormat.Gray8"/> or <see cref="PixelFormat.Rgb24"/>), and
/// the row writes. When the sink's plan is an identity, rows are produced directly in the frame storage.
/// </summary>
/// <remarks>
/// <para>Upsampling (centered chroma siting, edges replicated at the component's real width and height):</para>
/// <list type="bullet">
/// <item><description>factor 1: the samples are used as is;</description></item>
/// <item><description>
/// horizontal factor 2 (triangle filter): <c>out[2j] = (3 s[j] + s[j - 1] + 1) &gt;&gt; 2</c>, <c>out[2j + 1] = (3 s[j] + s[j + 1] + 2) &gt;&gt; 2</c>;
/// </description></item>
/// <item><description>
/// vertical factor 2 (triangle filter): <c>out = (3 near + far + 1) &gt;&gt; 2</c> for the upper output row of each sample row
/// (far = the row above) and <c>(3 near + far + 2) &gt;&gt; 2</c> for the lower one (far = the row below);
/// </description></item>
/// <item><description>
/// both factors 2: column sums <c>c[j] = 3 near[j] + far[j]</c>, then <c>out[2j] = (3 c[j] + c[j - 1] + 8) &gt;&gt; 4</c> and
/// <c>out[2j + 1] = (3 c[j] + c[j + 1] + 7) &gt;&gt; 4</c>;
/// </description></item>
/// <item><description>any other integral factor (3 or 4, or 2 combined with 3 or 4): sample replication;</description></item>
/// <item><description>
/// a horizontal factor of 2 on a plane of at most 2 samples (frames of at most 4 pixels wide): sample replication in both
/// directions, as libjpeg-turbo does (its triangle filters require wider planes), so that tiny frames decode like the
/// reference decoder and the browsers built on it.
/// </description></item>
/// </list>
/// <para>
/// Color conversion (JFIF 1.02, full range): with <c>cb = Cb - 128</c> and <c>cr = Cr - 128</c>,
/// <c>R = Y + ((91881 cr + 32768) &gt;&gt; 16)</c>, <c>G = Y + ((-22554 cb - 46802 cr + 32768) &gt;&gt; 16)</c>,
/// <c>B = Y + ((116130 cb + 32768) &gt;&gt; 16)</c> (the coefficients 1.402, 0.34414, 0.71414 and 1.772 in 16-bit fixed point,
/// arithmetic shifts, i.e. rounding to nearest with ties upward), each clamped to 0..255. RGB samples (Adobe transform 0,
/// or component identifiers R, G, B) are copied; grayscale samples are the luma samples.
/// </para>
/// <para>
/// With 128-bit hardware vector support (and little-endian byte order), the triangle filters and the color conversion process
/// 16 samples at a time with the same integer arithmetic (16-bit lanes for the filters, 32-bit lanes for the conversion);
/// the edges and the remainders use the scalar code. The output is identical to the scalar reference.
/// </para>
/// </remarks>
internal sealed class JpegRowWriter : IDisposable
{
    // Per-sample color terms: complete for R and B, unshifted (with the rounding half added once) for G
    private static readonly int[] CrToR = CreateTable(static v => ((91881 * v) + 32768) >> 16);
    private static readonly int[] CbToB = CreateTable(static v => ((116130 * v) + 32768) >> 16);
    private static readonly int[] CbToG = CreateTable(static v => -22554 * v);
    private static readonly int[] CrToG = CreateTable(static v => (-46802 * v) + 32768);

    private readonly JpegFrameComponent[] _components;
    private readonly DecodedFrameSink _sink;
    private readonly ImageColorModel _colorModel;
    private readonly int _width;
    private readonly int _height;
    private readonly int _bandRows;
    private readonly PooledBuffer?[] _upsampled;
    private PooledBuffer? _sourceRow;

    public JpegRowWriter(JpegFrameComponent[] components, DecodedFrameSink sink, ImageColorModel colorModel, int width, int height, int maxVertical, AllocationScope scope)
    {
        _components = components;
        _sink = sink;
        _colorModel = colorModel;
        _width = width;
        _height = height;
        _bandRows = 8 * maxVertical;
        _upsampled = new PooledBuffer?[components.Length];
        try
        {
            foreach (var component in components)
            {
                if (component.HorizontalRatio != 1 || component.VerticalRatio != 1)
                {
                    _upsampled[component.Index] = scope.Rent(width, AllocationKind.DecoderState, clear: false);
                }
            }

            if (!sink.Plan.IsIdentity && components.Length > 1)
            {
                _sourceRow = scope.Rent(width * 3, AllocationKind.DecoderState, clear: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the number of bands written so far.</summary>
    public int BandsWritten { get; private set; }

    /// <summary>Writes the output rows of the next band (<c>8 * Vmax</c> rows, fewer for the last band).</summary>
    /// <remarks>The planes must hold the band's rows and their context rows (the last row of the previous band and the first row of the next one, when they exist).</remarks>
    public void WriteNextBand(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = BandsWritten * _bandRows;
        var last = Math.Min(_height, first + _bandRows);
        using var lease = _sink.LeaseCurrentFrame();
        for (var y = first; y < last; y++)
        {
            WriteRow(lease, y);
        }

        BandsWritten++;
    }

    public void Dispose()
    {
        for (var i = 0; i < _upsampled.Length; i++)
        {
            _upsampled[i]?.Dispose();
            _upsampled[i] = null;
        }

        _sourceRow?.Dispose();
        _sourceRow = null;
    }

    private static int[] CreateTable(Func<int, int> term)
    {
        var table = new int[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = term(i - 128);
        }

        return table;
    }

    private void WriteRow(scoped in PixelLease lease, int y)
    {
        if (_components.Length == 1)
        {
            // Grayscale: the luma samples are the output (a single-component frame is never subsampled)
            var samples = _components[0].GetRow(y)[.._width];
            if (_sink.Plan.IsIdentity)
            {
                samples.CopyTo(lease.GetRowBytes(y));
            }
            else
            {
                _sink.WriteRow(lease, y, samples);
            }

            return;
        }

        var c0 = GetSamples(_components[0], y);
        var c1 = GetSamples(_components[1], y);
        var c2 = GetSamples(_components[2], y);
        var identity = _sink.Plan.IsIdentity;
        var row = identity ? lease.GetRowBytes(y)[..(_width * 3)] : _sourceRow!.RawBuffer.AsSpan(0, _width * 3);
        if (_colorModel == ImageColorModel.Rgb)
        {
            for (var x = 0; x < c0.Length; x++)
            {
                row[3 * x] = c0[x];
                row[(3 * x) + 1] = c1[x];
                row[(3 * x) + 2] = c2[x];
            }
        }
        else
        {
            ConvertYCbCr(c0, c1, c2, row);
        }

        if (!identity)
        {
            _sink.WriteRow(lease, y, row);
        }
    }

    /// <summary>Converts full-range YCbCr samples to interleaved RGB samples.</summary>
    internal static void ConvertYCbCr(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        var done = UseVectors ? ConvertYCbCrVector(luma, blue, red, destination) : 0;
        ConvertYCbCrScalar(luma[done..], blue[done..], red[done..], destination[(3 * done)..]);
    }

    /// <summary>The scalar reference of <see cref="ConvertYCbCr"/>.</summary>
    internal static void ConvertYCbCrScalar(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        var crToR = CrToR;
        var cbToB = CbToB;
        var cbToG = CbToG;
        var crToG = CrToG;
        for (var x = 0; x < luma.Length; x++)
        {
            int yValue = luma[x];
            int cb = blue[x];
            int cr = red[x];
            destination[3 * x] = Clamp(yValue + crToR[cr]);
            destination[(3 * x) + 1] = Clamp(yValue + ((cbToG[cb] + crToG[cr]) >> 16));
            destination[(3 * x) + 2] = Clamp(yValue + cbToB[cb]);
        }
    }

    /// <summary>Gets a value indicating whether the vector paths are used (128-bit hardware vectors, little-endian lanes).</summary>
    private static bool UseVectors => Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    /// <summary>Converts the pixels of whole 16-pixel groups that leave 4 bytes of slack in <paramref name="destination"/>.</summary>
    /// <returns>The number of pixels converted.</returns>
    private static int ConvertYCbCrVector(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        // Each group of 4 pixels is packed as R | G << 8 | B << 16 in 32-bit lanes, compacted to 12 bytes and stored as 16
        // bytes (the 4 extra bytes are overwritten by the next group, hence the slack)
        var compact = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0xFF, 0xFF, 0xFF, 0xFF);
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
        var length = Math.Min(luma.Length, Math.Min(blue.Length, red.Length));
        var x = 0;
        for (; x + 16 <= length && (3 * (x + 16)) + 4 <= destination.Length; x += 16)
        {
            var (y0, y1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref lumaStart, (nuint)x)));
            var (b0, b1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref blueStart, (nuint)x)));
            var (r0, r1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref redStart, (nuint)x)));
            ref var output = ref unsafe(Unsafe.Add(ref destinationStart, 3 * x));
            unsafe { Convert4(Vector128.WidenLower(y0), Vector128.WidenLower(b0), Vector128.WidenLower(r0), compact).StoreUnsafe(ref output); }
            unsafe { Convert4(Vector128.WidenUpper(y0), Vector128.WidenUpper(b0), Vector128.WidenUpper(r0), compact).StoreUnsafe(ref Unsafe.Add(ref output, 12)); }
            unsafe { Convert4(Vector128.WidenLower(y1), Vector128.WidenLower(b1), Vector128.WidenLower(r1), compact).StoreUnsafe(ref Unsafe.Add(ref output, 24)); }
            unsafe { Convert4(Vector128.WidenUpper(y1), Vector128.WidenUpper(b1), Vector128.WidenUpper(r1), compact).StoreUnsafe(ref Unsafe.Add(ref output, 36)); }
        }

        return x;

        static Vector128<byte> Convert4(Vector128<uint> yValues, Vector128<uint> blueValues, Vector128<uint> redValues, Vector128<byte> compact)
        {
            var luma = yValues.AsInt32();
            var cb = blueValues.AsInt32() - Vector128.Create(128);
            var cr = redValues.AsInt32() - Vector128.Create(128);
            var half = Vector128.Create(32768);
            var r = luma + Vector128.ShiftRightArithmetic((Vector128.Create(91881) * cr) + half, 16);
            var g = luma + Vector128.ShiftRightArithmetic((Vector128.Create(-22554) * cb) + (Vector128.Create(-46802) * cr) + half, 16);
            var b = luma + Vector128.ShiftRightArithmetic((Vector128.Create(116130) * cb) + half, 16);
            var zero = Vector128<int>.Zero;
            var max = Vector128.Create(255);
            r = Vector128.Min(Vector128.Max(r, zero), max);
            g = Vector128.Min(Vector128.Max(g, zero), max);
            b = Vector128.Min(Vector128.Max(b, zero), max);
            var packed = r | Vector128.ShiftLeft(g, 8) | Vector128.ShiftLeft(b, 16);
            return Vector128.Shuffle(packed.AsByte(), compact);
        }
    }

    private static byte Clamp(int value) => (uint)value <= 255 ? (byte)value : value < 0 ? (byte)0 : (byte)255;

    /// <summary>Gets the samples of a component for an output row, upsampled to the frame width.</summary>
    private ReadOnlySpan<byte> GetSamples(JpegFrameComponent component, int y)
    {
        var horizontal = component.HorizontalRatio;
        var vertical = component.VerticalRatio;
        if (horizontal == 1 && vertical == 1)
            return component.GetRow(y)[.._width];

        var output = _upsampled[component.Index]!.RawBuffer.AsSpan(0, _width);
        if (horizontal == 2 && component.Width <= 2)
        {
            // Planes of at most two samples with a horizontal factor of 2 (frames of at most 4 pixels) are replicated in both
            // directions, like libjpeg-turbo
            Replicate(component.GetRow(y / vertical), horizontal, output);
        }
        else if (vertical == 2 && horizontal <= 2)
        {
            // Triangle filter: the nearer sample row weighs 3/4, the other neighbor row (replicated at the edges) 1/4
            var row = y >> 1;
            var upper = (y & 1) == 0;
            var other = Math.Clamp(upper ? row - 1 : row + 1, 0, component.Height - 1);
            var near = component.GetRow(row);
            var far = component.GetRow(other);
            if (horizontal == 2)
            {
                UpsampleBoth(near, far, output);
            }
            else
            {
                UpsampleVertical(near, far, upper ? 1 : 2, output);
            }
        }
        else if (vertical == 1 && horizontal == 2)
        {
            UpsampleHorizontal(component.GetRow(y), output);
        }
        else
        {
            // Replication for the other integral factors
            Replicate(component.GetRow(y / vertical), horizontal, output);
        }

        return output;
    }

    private static void Replicate(ReadOnlySpan<byte> samples, int factor, Span<byte> output)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = samples[x / factor];
        }
    }

    /// <summary>Horizontal triangle filter (factor 2).</summary>
    internal static void UpsampleHorizontal(ReadOnlySpan<byte> samples, Span<byte> output)
    {
        if (!UseVectors)
        {
            UpsampleHorizontalScalar(samples, output);
            return;
        }

        // Inner samples by groups of 16 (their neighbors exist); the edges and the remainder use the scalar formula
        var end = UpsampleInner(samples, samples, output, horizontalOnly: true);
        UpsampleHorizontalRange(samples, output, 0, 1);
        UpsampleHorizontalRange(samples, output, end, samples.Length);
    }

    /// <summary>The scalar reference of <see cref="UpsampleHorizontal"/>.</summary>
    internal static void UpsampleHorizontalScalar(ReadOnlySpan<byte> samples, Span<byte> output) => UpsampleHorizontalRange(samples, output, 0, samples.Length);

    private static void UpsampleHorizontalRange(ReadOnlySpan<byte> samples, Span<byte> output, int start, int end)
    {
        var last = samples.Length - 1;
        for (var j = start; j < end; j++)
        {
            var center = 3 * samples[j];
            var x = 2 * j;
            output[x] = (byte)((center + samples[Math.Max(j - 1, 0)] + 1) >> 2);
            if (x + 1 < output.Length)
            {
                output[x + 1] = (byte)((center + samples[Math.Min(j + 1, last)] + 2) >> 2);
            }
        }
    }

    /// <summary>Vertical triangle filter (factor 2): <c>(3 near + far + bias) &gt;&gt; 2</c>.</summary>
    internal static void UpsampleVertical(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int bias, Span<byte> output)
    {
        var x = 0;
        if (UseVectors)
        {
            ref var nearStart = ref unsafe(MemoryMarshal.GetReference(near));
            ref var farStart = ref unsafe(MemoryMarshal.GetReference(far));
            ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
            var biasVector = Vector128.Create((ushort)bias);
            var three = Vector128.Create((ushort)3);
            var length = Math.Min(output.Length, Math.Min(near.Length, far.Length));
            for (; x + 16 <= length; x += 16)
            {
                var (n0, n1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref nearStart, (nuint)x)));
                var (f0, f1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref farStart, (nuint)x)));
                var low = Vector128.ShiftRightLogical((three * n0) + f0 + biasVector, 2);
                var high = Vector128.ShiftRightLogical((three * n1) + f1 + biasVector, 2);
                unsafe { Vector128.Narrow(low, high).StoreUnsafe(ref outputStart, (nuint)x); }
            }
        }

        UpsampleVerticalScalar(near[x..], far[x..], bias, output[x..]);
    }

    /// <summary>The scalar reference of <see cref="UpsampleVertical"/>.</summary>
    internal static void UpsampleVerticalScalar(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int bias, Span<byte> output)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = (byte)(((3 * near[x]) + far[x] + bias) >> 2);
        }
    }

    /// <summary>Both factors 2: column sums <c>c = 3 near + far</c>, then the horizontal triangle filter of the sums.</summary>
    internal static void UpsampleBoth(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output)
    {
        if (!UseVectors)
        {
            UpsampleBothScalar(near, far, output);
            return;
        }

        var end = UpsampleInner(near, far, output, horizontalOnly: false);
        UpsampleBothRange(near, far, output, 0, 1);
        UpsampleBothRange(near, far, output, end, near.Length);
    }

    /// <summary>The scalar reference of <see cref="UpsampleBoth"/>.</summary>
    internal static void UpsampleBothScalar(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output)
    {
        var last = near.Length - 1;
        var previous = (3 * near[0]) + far[0];
        var current = previous;
        for (var j = 0; j <= last; j++)
        {
            var next = j < last ? (3 * near[j + 1]) + far[j + 1] : current;
            var x = 2 * j;
            output[x] = (byte)(((3 * current) + previous + 8) >> 4);
            if (x + 1 < output.Length)
            {
                output[x + 1] = (byte)(((3 * current) + next + 7) >> 4);
            }

            previous = current;
            current = next;
        }
    }

    private static void UpsampleBothRange(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output, int start, int end)
    {
        var last = near.Length - 1;
        for (var j = start; j < end; j++)
        {
            var current = (3 * near[j]) + far[j];
            var previous = (3 * near[Math.Max(j - 1, 0)]) + far[Math.Max(j - 1, 0)];
            var next = (3 * near[Math.Min(j + 1, last)]) + far[Math.Min(j + 1, last)];
            var x = 2 * j;
            output[x] = (byte)(((3 * current) + previous + 8) >> 4);
            if (x + 1 < output.Length)
            {
                output[x + 1] = (byte)(((3 * current) + next + 7) >> 4);
            }
        }
    }

    /// <summary>
    /// Filters the inner samples <c>j</c> (from 1, by groups of 16 whose right neighbors exist and whose 32 outputs fit) in
    /// 16-bit lanes. Each output pair is formed as <c>even | odd &lt;&lt; 8</c> in a 16-bit lane, which is the interleaved byte
    /// order in little-endian memory. Horizontal only: <c>(3 s + left + 1) &gt;&gt; 2</c> and <c>(3 s + right + 2) &gt;&gt; 2</c>; both
    /// factors: the same on column sums <c>c = 3 near + far</c> with <c>(... + 8) &gt;&gt; 4</c> and <c>(... + 7) &gt;&gt; 4</c>.
    /// </summary>
    /// <returns>The first sample not processed (at least 1).</returns>
    private static int UpsampleInner(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output, bool horizontalOnly)
    {
        ref var nearStart = ref unsafe(MemoryMarshal.GetReference(near));
        ref var farStart = ref unsafe(MemoryMarshal.GetReference(far));
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
        var three = Vector128.Create((ushort)3);
        var evenBias = Vector128.Create((ushort)(horizontalOnly ? 1 : 8));
        var oddBias = Vector128.Create((ushort)(horizontalOnly ? 2 : 7));
        var shift = horizontalOnly ? 2 : 4;
        // Every load is in bounds: 16 samples from j - 1 to j + 1 of both rows
        var length = horizontalOnly ? near.Length : Math.Min(near.Length, far.Length);
        var j = 1;
        for (; j + 17 <= length && (2 * j) + 32 <= output.Length; j += 16)
        {
            var (current0, current1) = Sums(ref nearStart, ref farStart, j - 0, horizontalOnly, three);
            var (previous0, previous1) = Sums(ref nearStart, ref farStart, j - 1, horizontalOnly, three);
            var (next0, next1) = Sums(ref nearStart, ref farStart, j + 1, horizontalOnly, three);
            var even0 = Vector128.ShiftRightLogical((three * current0) + previous0 + evenBias, shift);
            var odd0 = Vector128.ShiftRightLogical((three * current0) + next0 + oddBias, shift);
            var even1 = Vector128.ShiftRightLogical((three * current1) + previous1 + evenBias, shift);
            var odd1 = Vector128.ShiftRightLogical((three * current1) + next1 + oddBias, shift);
            ref var destination = ref unsafe(Unsafe.Add(ref outputStart, 2 * j));
            unsafe { (even0 | Vector128.ShiftLeft(odd0, 8)).AsByte().StoreUnsafe(ref destination); }
            unsafe { (even1 | Vector128.ShiftLeft(odd1, 8)).AsByte().StoreUnsafe(ref Unsafe.Add(ref destination, 16)); }
        }

        return j;

        static (Vector128<ushort> Low, Vector128<ushort> High) Sums(ref byte near, ref byte far, int offset, bool horizontalOnly, Vector128<ushort> three)
        {
            var (n0, n1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref near, (nuint)offset)));
            if (horizontalOnly)
                return (n0, n1);

            var (f0, f1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref far, (nuint)offset)));
            return ((three * n0) + f0, (three * n1) + f1);
        }
    }
}
