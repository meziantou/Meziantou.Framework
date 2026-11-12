using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The intra prediction process of AV1 (AV1 specification, section 7.11.2): predicts a transform block of a plane from the
/// reconstructed samples above and to the left of it. The kernels follow the ordered steps of the specification; they are the
/// numerical reference of the decoder.
/// </summary>
internal static class Av1IntraPredictor
{
    /// <summary>
    /// The position of the entry 0 of an edge in its storage: <c>AboveRow</c> and <c>LeftCol</c> are indexed from -16 (the edge
    /// upsampling process writes entry -2 and reads entry -1).
    /// </summary>
    public const int EdgeOrigin = 16;

    // The taps of the recursive intra prediction, for each mode and each of the 7 neighbors: the weights of the 8 samples of
    // a 4x2 unit
    private static readonly int[] RecursiveTapsByNeighbor = CreateRecursiveTapsByNeighbor();

    /// <summary>The inputs of <c>predict_intra</c> that do not depend on the position of the transform block.</summary>
    /// <param name="BitDepth">The bit depth of the samples.</param>
    /// <param name="Mode">The intra prediction mode (<c>DC_PRED</c> to <c>PAETH_PRED</c>).</param>
    /// <param name="AngleDelta">The angle delta of a directional mode (<c>AngleDeltaY</c> or <c>AngleDeltaUV</c>).</param>
    /// <param name="FilterIntraMode">The filter intra mode, or -1 when the recursive prediction is not used.</param>
    /// <param name="EnableIntraEdgeFilter">The <c>enable_intra_edge_filter</c> flag of the sequence.</param>
    /// <param name="SmoothNeighbor">The output of the intra filter type process (section 7.11.2.8).</param>
    public readonly record struct Parameters(int BitDepth, int Mode, int AngleDelta, int FilterIntraMode, bool EnableIntraEdgeFilter, bool SmoothNeighbor);

    /// <summary>
    /// Predicts the block of (1 &lt;&lt; <paramref name="log2W"/>) x (1 &lt;&lt; <paramref name="log2H"/>) samples at
    /// (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="plane"/> (<c>predict_intra</c>).
    /// </summary>
    /// <param name="plane">The samples of the plane being decoded.</param>
    /// <param name="stride">The number of samples of a row of <paramref name="plane"/>.</param>
    /// <param name="x">The column of the top left sample of the block.</param>
    /// <param name="y">The row of the top left sample of the block.</param>
    /// <param name="haveLeft">Whether the samples to the left of the block are available.</param>
    /// <param name="haveAbove">Whether the samples above the block are available.</param>
    /// <param name="haveAboveRight">Whether the samples above and to the right of the block are available.</param>
    /// <param name="haveBelowLeft">Whether the samples to the left and below the block are available.</param>
    /// <param name="log2W">The base 2 logarithm of the width of the block.</param>
    /// <param name="log2H">The base 2 logarithm of the height of the block.</param>
    /// <param name="maxX">The largest valid column of the plane.</param>
    /// <param name="maxY">The largest valid row of the plane.</param>
    /// <param name="parameters">The prediction mode and its parameters.</param>
    public static void Predict(Span<ushort> plane, int stride, int x, int y, bool haveLeft, bool haveAbove, bool haveAboveRight, bool haveBelowLeft, int log2W, int log2H, int maxX, int maxY, in Parameters parameters)
        => Predict(plane, stride, x, y, haveLeft, haveAbove, haveAboveRight, haveBelowLeft, log2W, log2H, maxX, maxY, parameters, Vector128.IsHardwareAccelerated);

    /// <summary>The scalar reference of <see cref="Predict(Span{ushort}, int, int, int, bool, bool, bool, bool, int, int, int, int, in Parameters)"/>.</summary>
    internal static void PredictScalar(Span<ushort> plane, int stride, int x, int y, bool haveLeft, bool haveAbove, bool haveAboveRight, bool haveBelowLeft, int log2W, int log2H, int maxX, int maxY, in Parameters parameters)
        => Predict(plane, stride, x, y, haveLeft, haveAbove, haveAboveRight, haveBelowLeft, log2W, log2H, maxX, maxY, parameters, vectorized: false);

    /// <summary>
    /// Returns the number of entries of the storage of an edge of a block for <see cref="PrepareEdges"/>: the edge holds w + h
    /// entries, or twice as many once a short edge is upsampled, from the entry -16, and the vector kernels read past them.
    /// </summary>
    /// <param name="log2W">The base 2 logarithm of the width of the block.</param>
    /// <param name="log2H">The base 2 logarithm of the height of the block.</param>
    public static int GetEdgeStorageLength(int log2W, int log2H) => EdgeOrigin + Math.Max((1 << log2W) + (1 << log2H), 32) + 16;

    // The entries of the edges are not cleared: the kernels read the entries that the steps of the specification write, and
    // the vector lanes that read other entries are discarded. Zeroing them costs 0.4% of the instructions of an AVIF decode
    // (AvifBenchmarks.DecodeStill420, 2026-11-11)
    [SkipLocalsInit]
    private static void Predict(Span<ushort> plane, int stride, int x, int y, bool haveLeft, bool haveAbove, bool haveAboveRight, bool haveBelowLeft, int log2W, int log2H, int maxX, int maxY, in Parameters parameters, bool vectorized)
    {
        var edgeLength = GetEdgeStorageLength(log2W, log2H);
        Span<int> aboveStorage = unsafe(stackalloc int[edgeLength]);
        Span<int> leftStorage = unsafe(stackalloc int[edgeLength]);
        PrepareEdges(plane, stride, x, y, haveLeft, haveAbove, haveAboveRight, haveBelowLeft, log2W, log2H, maxX, maxY, parameters.BitDepth, aboveStorage, leftStorage);
        PredictFromEdges(plane, stride, x, y, haveLeft, haveAbove, log2W, log2H, maxX, maxY, parameters, aboveStorage, leftStorage, vectorized);
    }

    /// <summary>
    /// Writes the edges of the block that <see cref="PredictFromEdges(Span{ushort}, int, int, int, bool, bool, int, int, int, int, in Parameters, Span{int}, Span{int})"/>
    /// predicts the block from (<c>AboveRow</c> and <c>LeftCol</c> before the edge filter), whatever the mode: the first steps of
    /// <c>predict_intra</c>. The parameters are the ones of <see cref="Predict(Span{ushort}, int, int, int, bool, bool, bool, bool, int, int, int, int, in Parameters)"/>.
    /// </summary>
    /// <param name="plane">The samples of the plane.</param>
    /// <param name="stride">The number of samples of a row of <paramref name="plane"/>.</param>
    /// <param name="x">The column of the top left sample of the block.</param>
    /// <param name="y">The row of the top left sample of the block.</param>
    /// <param name="haveLeft">Whether the samples to the left of the block are available.</param>
    /// <param name="haveAbove">Whether the samples above the block are available.</param>
    /// <param name="haveAboveRight">Whether the samples above and to the right of the block are available.</param>
    /// <param name="haveBelowLeft">Whether the samples to the left and below the block are available.</param>
    /// <param name="log2W">The base 2 logarithm of the width of the block.</param>
    /// <param name="log2H">The base 2 logarithm of the height of the block.</param>
    /// <param name="maxX">The largest valid column of the plane.</param>
    /// <param name="maxY">The largest valid row of the plane.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="aboveStorage">The storage of the edge above: <see cref="GetEdgeStorageLength"/> entries.</param>
    /// <param name="leftStorage">The storage of the edge to the left: <see cref="GetEdgeStorageLength"/> entries.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PrepareEdges(ReadOnlySpan<ushort> plane, int stride, int x, int y, bool haveLeft, bool haveAbove, bool haveAboveRight, bool haveBelowLeft, int log2W, int log2H, int maxX, int maxY, int bitDepth, Span<int> aboveStorage, Span<int> leftStorage)
    {
        var w = 1 << log2W;
        var h = 1 << log2H;
        var above = new Edge(aboveStorage);
        var left = new Edge(leftStorage);
        if (haveAbove)
        {
            // The samples up to the limit, then the sample at the limit
            var aboveLimit = Math.Min(maxX, x + (haveAboveRight ? 2 * w : w) - 1);
            var row = plane.Slice((y - 1) * stride, stride);
            var count = Math.Clamp(aboveLimit - x + 1, 0, w + h);
            var samples = above.Slice(0, w + h);
            Widen(row.Slice(x, count), samples[..count]);
            samples[count..].Fill(row[aboveLimit]);
        }
        else
        {
            var value = haveLeft ? plane[(y * stride) + x - 1] : (1 << (bitDepth - 1)) - 1;
            above.Slice(0, w + h).Fill(value);
        }

        if (haveLeft)
        {
            var leftLimit = Math.Min(maxY, y + (haveBelowLeft ? 2 * h : h) - 1);
            var count = Math.Clamp(leftLimit - y + 1, 0, w + h);
            var column = plane[(x - 1)..];
            var samples = left.Slice(0, w + h);
            if (count > 0)
            {
                var rows = column.Slice(y * stride, ((count - 1) * stride) + 1);
                for (var i = 0; i < count; i++)
                {
                    samples[i] = rows[i * stride];
                }
            }

            samples[count..].Fill(column[leftLimit * stride]);
        }
        else
        {
            var value = haveAbove ? plane[((y - 1) * stride) + x] : (1 << (bitDepth - 1)) + 1;
            left.Slice(0, w + h).Fill(value);
        }

        if (haveAbove && haveLeft)
        {
            above[-1] = plane[((y - 1) * stride) + x - 1];
        }
        else if (haveAbove)
        {
            above[-1] = plane[((y - 1) * stride) + x];
        }
        else if (haveLeft)
        {
            above[-1] = plane[(y * stride) + x - 1];
        }
        else
        {
            above[-1] = 1 << (bitDepth - 1);
        }

        left[-1] = above[-1];
    }

    /// <summary>
    /// Predicts the block from the edges that <see cref="PrepareEdges"/> wrote: the last steps of <c>predict_intra</c>. The
    /// edges of a directional mode are filtered in place; the other modes leave them as they are. The parameters are the ones
    /// of <see cref="Predict(Span{ushort}, int, int, int, bool, bool, bool, bool, int, int, int, int, in Parameters)"/>.
    /// </summary>
    /// <param name="plane">The samples of the plane.</param>
    /// <param name="stride">The number of samples of a row of <paramref name="plane"/>.</param>
    /// <param name="x">The column of the top left sample of the block.</param>
    /// <param name="y">The row of the top left sample of the block.</param>
    /// <param name="haveLeft">Whether the samples to the left of the block are available.</param>
    /// <param name="haveAbove">Whether the samples above the block are available.</param>
    /// <param name="log2W">The base 2 logarithm of the width of the block.</param>
    /// <param name="log2H">The base 2 logarithm of the height of the block.</param>
    /// <param name="maxX">The largest valid column of the plane.</param>
    /// <param name="maxY">The largest valid row of the plane.</param>
    /// <param name="parameters">The prediction mode and its parameters.</param>
    /// <param name="aboveStorage">The storage of the edge above.</param>
    /// <param name="leftStorage">The storage of the edge to the left.</param>
    public static void PredictFromEdges(Span<ushort> plane, int stride, int x, int y, bool haveLeft, bool haveAbove, int log2W, int log2H, int maxX, int maxY, in Parameters parameters, Span<int> aboveStorage, Span<int> leftStorage)
        => PredictFromEdges(plane, stride, x, y, haveLeft, haveAbove, log2W, log2H, maxX, maxY, parameters, aboveStorage, leftStorage, Vector128.IsHardwareAccelerated);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PredictFromEdges(Span<ushort> plane, int stride, int x, int y, bool haveLeft, bool haveAbove, int log2W, int log2H, int maxX, int maxY, in Parameters parameters, Span<int> aboveStorage, Span<int> leftStorage, bool vectorized)
    {
        var w = 1 << log2W;
        var h = 1 << log2H;
        var bitDepth = parameters.BitDepth;
        var above = new Edge(aboveStorage);
        var left = new Edge(leftStorage);
        var destination = plane[((y * stride) + x)..];
        var mode = parameters.Mode;
        if (parameters.FilterIntraMode >= 0)
        {
            if (vectorized)
            {
                PredictRecursiveVector(destination, stride, w, h, above, left, parameters.FilterIntraMode, bitDepth);
            }
            else
            {
                PredictRecursive(destination, stride, w, h, above, left, parameters.FilterIntraMode, bitDepth);
            }
        }
        else if (mode is >= Av1Constants.VPred and <= Av1Constants.D67Pred)
        {
            PredictDirectional(destination, stride, x, y, haveLeft, haveAbove, w, h, maxX, maxY, above, left, parameters, vectorized);
        }
        else if (mode is Av1Constants.SmoothPred or Av1Constants.SmoothVPred or Av1Constants.SmoothHPred)
        {
            if (vectorized && w >= 4)
            {
                PredictSmoothVector(destination, stride, mode, log2W, log2H, above, left);
            }
            else
            {
                PredictSmooth(destination, stride, mode, log2W, log2H, above, left);
            }
        }
        else if (mode is Av1Constants.DcPred)
        {
            PredictDc(destination, stride, haveLeft, haveAbove, log2W, log2H, above, left, bitDepth);
        }
        else if (vectorized)
        {
            PredictPaethVector(destination, stride, w, h, above, left);
        }
        else
        {
            PredictPaeth(destination, stride, w, h, above, left);
        }
    }

    // Widens samples to the entries of an edge
    private static void Widen(ReadOnlySpan<ushort> samples, Span<int> values)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            for (; i <= samples.Length - Vector128<ushort>.Count; i += Vector128<ushort>.Count)
            {
                var (low, high) = Vector128.Widen(Vector128.Create(samples[i..]));
                low.AsInt32().CopyTo(values[i..]);
                high.AsInt32().CopyTo(values[(i + Vector128<int>.Count)..]);
            }
        }

        for (; i < samples.Length; i++)
        {
            values[i] = samples[i];
        }
    }

    // Section 7.11.2.2 "Basic intra prediction process"
    private static void PredictPaeth(Span<ushort> destination, int stride, int w, int h, Edge above, Edge left)
    {
        var topLeft = above[-1];
        for (var i = 0; i < h; i++)
        {
            var row = destination.Slice(i * stride, w);
            var leftSample = left[i];
            for (var j = 0; j < w; j++)
            {
                var top = above[j];
                var b = top + leftSample - topLeft;
                var pLeft = Math.Abs(b - leftSample);
                var pTop = Math.Abs(b - top);
                var pTopLeft = Math.Abs(b - topLeft);
                if (pLeft <= pTop && pLeft <= pTopLeft)
                {
                    row[j] = (ushort)leftSample;
                }
                else if (pTop <= pTopLeft)
                {
                    row[j] = (ushort)top;
                }
                else
                {
                    row[j] = (ushort)topLeft;
                }
            }
        }
    }

    // PredictPaeth with the samples of 4 columns in vector lanes
    private static void PredictPaethVector(Span<ushort> destination, int stride, int w, int h, Edge above, Edge left)
    {
        var topLeft = Vector128.Create(above[-1]);
        var tops = above.Slice(0, w);
        for (var i = 0; i < h; i++)
        {
            var row = destination.Slice(i * stride, w);
            var leftSample = Vector128.Create(left[i]);
            for (var j = 0; j < w; j += 4)
            {
                var top = Vector128.Create(tops[j..]);
                var b = top + leftSample - topLeft;
                var pLeft = Vector128.Abs(b - leftSample);
                var pTop = Vector128.Abs(b - top);
                var pTopLeft = Vector128.Abs(b - topLeft);
                var useLeft = Vector128.LessThanOrEqual(pLeft, pTop) & Vector128.LessThanOrEqual(pLeft, pTopLeft);
                var useTop = Vector128.LessThanOrEqual(pTop, pTopLeft);
                var value = Vector128.ConditionalSelect(useLeft, leftSample, Vector128.ConditionalSelect(useTop, top, topLeft));
                Vector128.Narrow(value.AsUInt32(), value.AsUInt32()).GetLower().CopyTo(row[j..]);
            }
        }
    }

    // Section 7.11.2.3 "Recursive intra prediction process": the block is predicted by units of 4x2 samples, each from 7
    // neighboring samples that are either edge samples or samples predicted by a previous unit
    private static void PredictRecursive(Span<ushort> destination, int stride, int w, int h, Edge above, Edge left, int filterIntraMode, int bitDepth)
    {
        var taps = Av1Tables.IntraFilterTaps.Slice(filterIntraMode * Av1Tables.IntraFilterTapsLength1 * Av1Tables.IntraFilterTapsLength2, Av1Tables.IntraFilterTapsLength1 * Av1Tables.IntraFilterTapsLength2);
        var max = (1 << bitDepth) - 1;
        Span<int> p = stackalloc int[7];
        var w4 = w >> 2;
        var h2 = h >> 1;
        for (var i2 = 0; i2 < h2; i2++)
        {
            for (var j4 = 0; j4 < w4; j4++)
            {
                for (var i = 0; i < 5; i++)
                {
                    if (i2 is 0)
                    {
                        p[i] = above[(j4 << 2) + i - 1];
                    }
                    else if (j4 is 0 && i is 0)
                    {
                        p[i] = left[(i2 << 1) - 1];
                    }
                    else
                    {
                        p[i] = destination[(((i2 << 1) - 1) * stride) + (j4 << 2) + i - 1];
                    }
                }

                for (var i = 5; i < 7; i++)
                {
                    p[i] = j4 is 0 ? left[(i2 << 1) + i - 5] : destination[(((i2 << 1) + i - 5) * stride) + (j4 << 2) - 1];
                }

                for (var i1 = 0; i1 < 2; i1++)
                {
                    for (var j1 = 0; j1 < 4; j1++)
                    {
                        var filter = taps.Slice(((i1 << 2) + j1) * Av1Tables.IntraFilterTapsLength2, Av1Tables.IntraFilterTapsLength2);
                        var pr = 0;
                        for (var i = 0; i < 7; i++)
                        {
                            pr += filter[i] * p[i];
                        }

                        destination[(((i2 << 1) + i1) * stride) + (j4 << 2) + j1] = (ushort)Math.Clamp(Round2Signed(pr, Av1Constants.IntraFilterScaleBits), 0, max);
                    }
                }
            }
        }
    }

    // PredictRecursive with the 8 samples of a 4x2 unit in vector lanes: the sum of the 7 neighbors times their taps
    private static void PredictRecursiveVector(Span<ushort> destination, int stride, int w, int h, Edge above, Edge left, int filterIntraMode, int bitDepth)
    {
        var taps = RecursiveTapsByNeighbor.AsSpan(filterIntraMode * 7 * 8, 7 * 8);
        var tap0Low = Vector128.Create(taps);
        var tap0High = Vector128.Create(taps[4..]);
        var tap1Low = Vector128.Create(taps[8..]);
        var tap1High = Vector128.Create(taps[12..]);
        var tap2Low = Vector128.Create(taps[16..]);
        var tap2High = Vector128.Create(taps[20..]);
        var tap3Low = Vector128.Create(taps[24..]);
        var tap3High = Vector128.Create(taps[28..]);
        var tap4Low = Vector128.Create(taps[32..]);
        var tap4High = Vector128.Create(taps[36..]);
        var tap5Low = Vector128.Create(taps[40..]);
        var tap5High = Vector128.Create(taps[44..]);
        var tap6Low = Vector128.Create(taps[48..]);
        var tap6High = Vector128.Create(taps[52..]);
        var max = Vector128.Create(ushort.CreateTruncating((1 << bitDepth) - 1));
        var rounding = Vector128.Create(1 << (Av1Constants.IntraFilterScaleBits - 1));
        var w4 = w >> 2;
        var h2 = h >> 1;
        for (var i2 = 0; i2 < h2; i2++)
        {
            for (var j4 = 0; j4 < w4; j4++)
            {
                // The 5 neighbors above (from the top left one) and the 2 neighbors to the left
                int p0, p1, p2, p3, p4, p5, p6;
                if (i2 is 0)
                {
                    var row = above.Slice((j4 << 2) - 1, 5);
                    (p0, p1, p2, p3, p4) = (row[0], row[1], row[2], row[3], row[4]);
                }
                else
                {
                    var row = destination.Slice((((i2 << 1) - 1) * stride) + (j4 << 2) - 1 + (j4 is 0 ? 1 : 0), j4 is 0 ? 4 : 5);
                    p0 = j4 is 0 ? left[(i2 << 1) - 1] : row[0];
                    var offset = j4 is 0 ? -1 : 0;
                    (p1, p2, p3, p4) = (row[1 + offset], row[2 + offset], row[3 + offset], row[4 + offset]);
                }

                if (j4 is 0)
                {
                    (p5, p6) = (left[i2 << 1], left[(i2 << 1) + 1]);
                }
                else
                {
                    var column = (i2 << 1) * stride + (j4 << 2) - 1;
                    (p5, p6) = (destination[column], destination[column + stride]);
                }

                var low = (tap0Low * p0) + (tap1Low * p1) + (tap2Low * p2) + (tap3Low * p3) + (tap4Low * p4) + (tap5Low * p5) + (tap6Low * p6);
                var high = (tap0High * p0) + (tap1High * p1) + (tap2High * p2) + (tap3High * p3) + (tap4High * p4) + (tap5High * p5) + (tap6High * p6);

                // Round2Signed: the magnitude is rounded, then the sign is restored; negative values clamp to 0
                low = Vector128.ConditionalSelect(Vector128.LessThan(low, Vector128<int>.Zero), Vector128<int>.Zero, (low + rounding) >> Av1Constants.IntraFilterScaleBits);
                high = Vector128.ConditionalSelect(Vector128.LessThan(high, Vector128<int>.Zero), Vector128<int>.Zero, (high + rounding) >> Av1Constants.IntraFilterScaleBits);
                var result = Vector128.Min(Vector128.Narrow(Vector128.Min(low.AsUInt32(), Vector128.Create(65535u)), Vector128.Min(high.AsUInt32(), Vector128.Create(65535u))), max);
                var top = destination.Slice(((i2 << 1) * stride) + (j4 << 2), 4);
                result.GetLower().CopyTo(top);
                result.GetUpper().CopyTo(destination.Slice((((i2 << 1) + 1) * stride) + (j4 << 2), 4));
            }
        }
    }

    // Section 7.11.2.4 "Directional intra prediction process"
    private static void PredictDirectional(Span<ushort> destination, int stride, int x, int y, bool haveLeft, bool haveAbove, int w, int h, int maxX, int maxY, Edge above, Edge left, in Parameters parameters, bool vectorized)
    {
        var bitDepth = parameters.BitDepth;
        var pAngle = Av1Tables.ModeToAngle[parameters.Mode] + (parameters.AngleDelta * Av1Constants.AngleStep);
        var upsampleAbove = 0;
        var upsampleLeft = 0;
        if (parameters.EnableIntraEdgeFilter)
        {
            var filterType = parameters.SmoothNeighbor ? 1 : 0;
            if (pAngle is not 90 and not 180)
            {
                if (pAngle > 90 && pAngle < 180 && (w + h) >= 24)
                {
                    // Section 7.11.2.7 "Filter corner process"
                    var corner = Round2((left[0] * 5) + (above[-1] * 6) + (above[0] * 5), 4);
                    left[-1] = corner;
                    above[-1] = corner;
                }

                if (haveAbove)
                {
                    var strength = GetEdgeFilterStrength(w, h, filterType, pAngle - 90);
                    var numPx = Math.Min(w, maxX - x + 1) + (pAngle < 90 ? h : 0) + 1;
                    FilterEdge(above, numPx, strength, vectorized);
                }

                if (haveLeft)
                {
                    var strength = GetEdgeFilterStrength(w, h, filterType, pAngle - 180);
                    var numPx = Math.Min(h, maxY - y + 1) + (pAngle > 180 ? w : 0) + 1;
                    FilterEdge(left, numPx, strength, vectorized);
                }
            }

            if (UseUpsample(w, h, filterType, pAngle - 90))
            {
                upsampleAbove = 1;
                UpsampleEdge(above, w + (pAngle < 90 ? h : 0), bitDepth);
            }

            if (UseUpsample(w, h, filterType, pAngle - 180))
            {
                upsampleLeft = 1;
                UpsampleEdge(left, h + (pAngle > 180 ? w : 0), bitDepth);
            }
        }

        if (vectorized && Av1Simd.IsSupported && pAngle is not 90 and not 180)
        {
            PredictDirectionalVector(destination, stride, w, h, pAngle, upsampleAbove, upsampleLeft, above, left);
        }
        else if (pAngle < 90)
        {
            var dx = Av1Tables.DrIntraDerivative[pAngle];
            var maxBaseX = (w + h - 1) << upsampleAbove;
            for (var i = 0; i < h; i++)
            {
                var row = destination.Slice(i * stride, w);
                var idx = (i + 1) * dx;
                var shift = ((idx << upsampleAbove) >> 1) & 0x1F;
                for (var j = 0; j < w; j++)
                {
                    var b = (idx >> (6 - upsampleAbove)) + (j << upsampleAbove);
                    row[j] = (ushort)(b < maxBaseX ? Round2((above[b] * (32 - shift)) + (above[b + 1] * shift), 5) : above[maxBaseX]);
                }
            }
        }
        else if (pAngle > 90 && pAngle < 180)
        {
            var dx = Av1Tables.DrIntraDerivative[180 - pAngle];
            var dy = Av1Tables.DrIntraDerivative[pAngle - 90];
            for (var i = 0; i < h; i++)
            {
                var row = destination.Slice(i * stride, w);
                for (var j = 0; j < w; j++)
                {
                    var idx = (j << 6) - ((i + 1) * dx);
                    var b = idx >> (6 - upsampleAbove);
                    if (b >= -(1 << upsampleAbove))
                    {
                        var shift = ((idx << upsampleAbove) >> 1) & 0x1F;
                        row[j] = (ushort)Round2((above[b] * (32 - shift)) + (above[b + 1] * shift), 5);
                    }
                    else
                    {
                        idx = (i << 6) - ((j + 1) * dy);
                        b = idx >> (6 - upsampleLeft);
                        var shift = ((idx << upsampleLeft) >> 1) & 0x1F;
                        row[j] = (ushort)Round2((left[b] * (32 - shift)) + (left[b + 1] * shift), 5);
                    }
                }
            }
        }
        else if (pAngle > 180)
        {
            var dy = Av1Tables.DrIntraDerivative[270 - pAngle];
            for (var i = 0; i < h; i++)
            {
                var row = destination.Slice(i * stride, w);
                for (var j = 0; j < w; j++)
                {
                    var idx = (j + 1) * dy;
                    var b = (idx >> (6 - upsampleLeft)) + (i << upsampleLeft);
                    var shift = ((idx << upsampleLeft) >> 1) & 0x1F;
                    row[j] = (ushort)Round2((left[b] * (32 - shift)) + (left[b + 1] * shift), 5);
                }
            }
        }
        else if (pAngle is 90 && vectorized)
        {
            PredictVerticalVector(destination, stride, w, h, above);
        }
        else if (pAngle is 90)
        {
            for (var i = 0; i < h; i++)
            {
                var row = destination.Slice(i * stride, w);
                for (var j = 0; j < w; j++)
                {
                    row[j] = (ushort)above[j];
                }
            }
        }
        else
        {
            for (var i = 0; i < h; i++)
            {
                destination.Slice(i * stride, w).Fill((ushort)left[i]);
            }
        }
    }

    // The vertical prediction (an angle of 90) with the row above narrowed once
    private static void PredictVerticalVector(Span<ushort> destination, int stride, int w, int h, Edge above)
    {
        if (w is 4)
        {
            var samples = Vector128.Create(above.Slice(0, 4));
            var row = Vector128.Narrow(samples.AsUInt32(), samples.AsUInt32()).GetLower();
            for (var i = 0; i < h; i++)
            {
                row.CopyTo(destination[(i * stride)..]);
            }

            return;
        }

        Span<ushort> top = stackalloc ushort[64];
        top = top[..w];
        for (var j = 0; j < w; j += 8)
        {
            Vector128.Narrow(Vector128.Create(above.Slice(j, 4)).AsUInt32(), Vector128.Create(above.Slice(j + 4, 4)).AsUInt32()).CopyTo(top[j..]);
        }

        for (var i = 0; i < h; i++)
        {
            top.CopyTo(destination.Slice(i * stride, w));
        }
    }

    // PredictDirectional for the angles other than 90 and 180, by tiles of 4x4 samples. In a row, the samples that are
    // predicted from the edge above read entries of the edge whose positions step by 1 (2 for an upsampled edge), with the
    // same weights: ( j << 6 ) - ( i + 1 ) * dx is j * 64 plus a constant. In a column, the samples that are predicted from
    // the edge to the left do the same with the entries of that edge: their vectors are the columns of a tile, which are
    // transposed into its rows. A sample is predicted from the edge to the left when its position on the edge above is
    // before the first entry: the first samples of a row, as many as Firsts gives for the row
    private static void PredictDirectionalVector(Span<ushort> destination, int stride, int w, int h, int pAngle, int upsampleAbove, int upsampleLeft, Edge above, Edge left)
    {
        var aboveEntries = above.From(-EdgeOrigin);
        var leftEntries = left.From(-EdgeOrigin);
        var lanes = Vector128.Create(0, 1, 2, 3);
        if (pAngle < 90)
        {
            // A sample whose position is at or after the last entry of the edge is that entry
            var dx = Av1Tables.DrIntraDerivative[pAngle];
            var maxBaseX = (w + h - 1) << upsampleAbove;
            var last = Vector128.Create(above[maxBaseX]);
            var limit = Vector128.Create(maxBaseX);
            for (var i = 0; i < h; i++)
            {
                var idx = (i + 1) * dx;
                var start = idx >> (6 - upsampleAbove);
                var shift = ((idx << upsampleAbove) >> 1) & 0x1F;
                var weight0 = Vector128.Create(32 - shift);
                var weight1 = Vector128.Create(shift);
                var row = destination.Slice(i * stride, w);
                for (var j = 0; j < w; j += 4)
                {
                    var b = start + (j << upsampleAbove);
                    Vector128<int> value;
                    if (b >= maxBaseX)
                    {
                        value = last;
                    }
                    else
                    {
                        value = InterpolateLanes(aboveEntries, b, upsampleAbove, weight0, weight1);
                        if (b + (3 << upsampleAbove) >= maxBaseX)
                            value = Vector128.ConditionalSelect(Vector128.LessThan(Vector128.Create(b) + (lanes << upsampleAbove), limit), value, last);
                    }

                    StoreLanes(value, row[j..]);
                }
            }
        }
        else if (pAngle < 180)
        {
            var dx = Av1Tables.DrIntraDerivative[180 - pAngle];
            var dy = Av1Tables.DrIntraDerivative[pAngle - 90];
            Span<int> firsts = stackalloc int[h];
            for (var i = 0; i < h; i++)
            {
                // The sample j is predicted from the edge above when ( ( j << 6 ) - ( i + 1 ) * dx ) >> ( 6 - upsampleAbove ),
                // which is ( j << upsampleAbove ) + start, is at least -( 1 << upsampleAbove )
                var start = -((i + 1) * dx) >> (6 - upsampleAbove);
                firsts[i] = Math.Clamp((-1 - start) >> upsampleAbove, 0, w);
            }

            for (var i0 = 0; i0 < h; i0 += 4)
            {
                for (var j0 = 0; j0 < w; j0 += 4)
                {
                    // The columns of the samples that are predicted from the edge to the left. A column whose samples are all
                    // predicted from the edge above is not read: its positions on the edge to the left may be before its start
                    var maxFirst = firsts[i0 + 3];
                    Vector128<int> left0 = default, left1 = default, left2 = default, left3 = default;
                    if (maxFirst > j0)
                    {
                        var column0 = PredictLeftColumn(leftEntries, i0, j0, dy, upsampleLeft);
                        var column1 = maxFirst > j0 + 1 ? PredictLeftColumn(leftEntries, i0, j0 + 1, dy, upsampleLeft) : default;
                        var column2 = maxFirst > j0 + 2 ? PredictLeftColumn(leftEntries, i0, j0 + 2, dy, upsampleLeft) : default;
                        var column3 = maxFirst > j0 + 3 ? PredictLeftColumn(leftEntries, i0, j0 + 3, dy, upsampleLeft) : default;
                        Av1Simd.Transpose(column0, column1, column2, column3, out left0, out left1, out left2, out left3);
                    }

                    var row0 = SelectDirectionalRow(aboveEntries, firsts[i0], i0, j0, dx, upsampleAbove, left0, lanes);
                    var row1 = SelectDirectionalRow(aboveEntries, firsts[i0 + 1], i0 + 1, j0, dx, upsampleAbove, left1, lanes);
                    var row2 = SelectDirectionalRow(aboveEntries, firsts[i0 + 2], i0 + 2, j0, dx, upsampleAbove, left2, lanes);
                    var row3 = SelectDirectionalRow(aboveEntries, maxFirst, i0 + 3, j0, dx, upsampleAbove, left3, lanes);
                    StoreLanes(row0, row1, destination[((i0 * stride) + j0)..], destination[(((i0 + 1) * stride) + j0)..]);
                    StoreLanes(row2, row3, destination[(((i0 + 2) * stride) + j0)..], destination[(((i0 + 3) * stride) + j0)..]);
                }
            }
        }
        else
        {
            var dy = Av1Tables.DrIntraDerivative[270 - pAngle];
            for (var j0 = 0; j0 < w; j0 += 4)
            {
                // The position on the edge to the left of the sample ( i, j ) is ( ( ( j + 1 ) * dy ) >> ( 6 - upsampleLeft ) ) +
                // ( i << upsampleLeft )
                var idx0 = (j0 + 1) * dy;
                var idx1 = (j0 + 2) * dy;
                var idx2 = (j0 + 3) * dy;
                var idx3 = (j0 + 4) * dy;
                var shift0 = ((idx0 << upsampleLeft) >> 1) & 0x1F;
                var shift1 = ((idx1 << upsampleLeft) >> 1) & 0x1F;
                var shift2 = ((idx2 << upsampleLeft) >> 1) & 0x1F;
                var shift3 = ((idx3 << upsampleLeft) >> 1) & 0x1F;
                for (var i0 = 0; i0 < h; i0 += 4)
                {
                    var offset = i0 << upsampleLeft;
                    var column0 = InterpolateLanes(leftEntries, (idx0 >> (6 - upsampleLeft)) + offset, upsampleLeft, Vector128.Create(32 - shift0), Vector128.Create(shift0));
                    var column1 = InterpolateLanes(leftEntries, (idx1 >> (6 - upsampleLeft)) + offset, upsampleLeft, Vector128.Create(32 - shift1), Vector128.Create(shift1));
                    var column2 = InterpolateLanes(leftEntries, (idx2 >> (6 - upsampleLeft)) + offset, upsampleLeft, Vector128.Create(32 - shift2), Vector128.Create(shift2));
                    var column3 = InterpolateLanes(leftEntries, (idx3 >> (6 - upsampleLeft)) + offset, upsampleLeft, Vector128.Create(32 - shift3), Vector128.Create(shift3));
                    Av1Simd.Transpose(column0, column1, column2, column3, out var row0, out var row1, out var row2, out var row3);
                    StoreLanes(row0, row1, destination[((i0 * stride) + j0)..], destination[(((i0 + 1) * stride) + j0)..]);
                    StoreLanes(row2, row3, destination[(((i0 + 2) * stride) + j0)..], destination[(((i0 + 3) * stride) + j0)..]);
                }
            }
        }
    }

    // The 4 samples j0 to j0 + 3 of the row i of a directional prediction between 90 and 180: the ones before first are
    // the lanes of fromLeft, the others are predicted from the edge above, which is only read when one is
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> SelectDirectionalRow(ReadOnlySpan<int> aboveEntries, int first, int i, int j0, int dx, int upsampleAbove, Vector128<int> fromLeft, Vector128<int> lanes)
    {
        if (first >= j0 + 4)
            return fromLeft;

        var idx = -((i + 1) * dx);
        var shift = ((idx << upsampleAbove) >> 1) & 0x1F;
        var fromAbove = InterpolateLanes(aboveEntries, (idx >> (6 - upsampleAbove)) + (j0 << upsampleAbove), upsampleAbove, Vector128.Create(32 - shift), Vector128.Create(shift));
        if (first <= j0)
            return fromAbove;

        return Vector128.ConditionalSelect(Vector128.LessThan(lanes, Vector128.Create(first - j0)), fromLeft, fromAbove);
    }

    // The samples i0 to i0 + 3 of the column j of a directional prediction between 90 and 180, predicted from the edge to
    // the left: the position of the sample i is ( ( i << 6 ) - ( j + 1 ) * dy ) >> ( 6 - upsampleLeft )
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> PredictLeftColumn(ReadOnlySpan<int> leftEntries, int i0, int j, int dy, int upsampleLeft)
    {
        var idx = (i0 << 6) - ((j + 1) * dy);
        var shift = ((idx << upsampleLeft) >> 1) & 0x1F;
        return InterpolateLanes(leftEntries, idx >> (6 - upsampleLeft), upsampleLeft, Vector128.Create(32 - shift), Vector128.Create(shift));
    }

    // Round2( edge[ b + k * step ] * ( 32 - shift ) + edge[ b + k * step + 1 ] * shift, 5 ) for the lanes k = 0 to 3, step being
    // 2 for an upsampled edge and 1 otherwise: weight0 is 32 - shift, weight1 is shift, entries is the storage of the edge
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> InterpolateLanes(ReadOnlySpan<int> entries, int b, int upsample, Vector128<int> weight0, Vector128<int> weight1)
    {
        var index = b + EdgeOrigin;
        Vector128<int> low;
        Vector128<int> high;
        if (upsample is 0)
        {
            low = Vector128.Create(entries[index..]);
            high = Vector128.Create(entries[(index + 1)..]);
        }
        else
        {
            var first = Vector128.Create(entries[index..]);
            var second = Vector128.Create(entries[(index + 4)..]);
            low = Av1Simd.UnzipEven(first, second);
            high = Av1Simd.UnzipOdd(first, second);
        }

        return ((low * weight0) + (high * weight1) + Vector128.Create(16)) >> 5;
    }

    // Writes the 4 lanes of the samples of a row
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreLanes(Vector128<int> value, Span<ushort> row) => Vector128.Narrow(value.AsUInt32(), value.AsUInt32()).GetLower().CopyTo(row);

    // Writes the 4 lanes of the samples of two rows
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreLanes(Vector128<int> first, Vector128<int> second, Span<ushort> firstRow, Span<ushort> secondRow)
    {
        var samples = Vector128.Narrow(first.AsUInt32(), second.AsUInt32());
        samples.GetLower().CopyTo(firstRow);
        samples.GetUpper().CopyTo(secondRow);
    }

    // Section 7.11.2.5 "DC intra prediction process"
    private static void PredictDc(Span<ushort> destination, int stride, bool haveLeft, bool haveAbove, int log2W, int log2H, Edge above, Edge left, int bitDepth)
    {
        var w = 1 << log2W;
        var h = 1 << log2H;
        int value;
        if (haveLeft && haveAbove)
        {
            var sum = 0;
            for (var k = 0; k < h; k++)
            {
                sum += left[k];
            }

            for (var k = 0; k < w; k++)
            {
                sum += above[k];
            }

            sum += (w + h) >> 1;
            value = sum / (w + h);
        }
        else if (haveLeft)
        {
            var sum = 0;
            for (var k = 0; k < h; k++)
            {
                sum += left[k];
            }

            value = Math.Clamp((sum + (h >> 1)) >> log2H, 0, (1 << bitDepth) - 1);
        }
        else if (haveAbove)
        {
            var sum = 0;
            for (var k = 0; k < w; k++)
            {
                sum += above[k];
            }

            value = Math.Clamp((sum + (w >> 1)) >> log2W, 0, (1 << bitDepth) - 1);
        }
        else
        {
            value = 1 << (bitDepth - 1);
        }

        for (var i = 0; i < h; i++)
        {
            destination.Slice(i * stride, w).Fill((ushort)value);
        }
    }

    // Section 7.11.2.6 "Smooth intra prediction process"
    private static void PredictSmooth(Span<ushort> destination, int stride, int mode, int log2W, int log2H, Edge above, Edge left)
    {
        var w = 1 << log2W;
        var h = 1 << log2H;
        var weightsX = GetSmoothWeights(log2W);
        var weightsY = GetSmoothWeights(log2H);
        var bottomLeft = left[h - 1];
        var topRight = above[w - 1];
        for (var i = 0; i < h; i++)
        {
            var row = destination.Slice(i * stride, w);
            for (var j = 0; j < w; j++)
            {
                if (mode is Av1Constants.SmoothPred)
                {
                    var smoothPred = (weightsY[i] * above[j]) + ((256 - weightsY[i]) * bottomLeft) + (weightsX[j] * left[i]) + ((256 - weightsX[j]) * topRight);
                    row[j] = (ushort)Round2(smoothPred, 9);
                }
                else if (mode is Av1Constants.SmoothVPred)
                {
                    var smoothPred = (weightsY[i] * above[j]) + ((256 - weightsY[i]) * bottomLeft);
                    row[j] = (ushort)Round2(smoothPred, 8);
                }
                else
                {
                    var smoothPred = (weightsX[j] * left[i]) + ((256 - weightsX[j]) * topRight);
                    row[j] = (ushort)Round2(smoothPred, 8);
                }
            }
        }
    }

    // PredictSmooth with 4 samples of a row in vector lanes: the terms of the columns, which do not depend on the row, are
    // computed once
    [SkipLocalsInit]
    private static void PredictSmoothVector(Span<ushort> destination, int stride, int mode, int log2W, int log2H, Edge above, Edge left)
    {
        var w = 1 << log2W;
        var h = 1 << log2H;
        var weightsX = GetSmoothWeights(log2W);
        var weightsY = GetSmoothWeights(log2H);
        var bottomLeft = left[h - 1];
        var topRight = above[w - 1];
        var chunks = w >> 2;

        // The samples above and, for the horizontal terms, the weights of the columns and the term of the sample above and to
        // the right: every element is written before it is read
        Span<Vector128<int>> tops = unsafe(stackalloc Vector128<int>[16]);
        Span<Vector128<int>> weights = unsafe(stackalloc Vector128<int>[16]);
        Span<Vector128<int>> rights = unsafe(stackalloc Vector128<int>[16]);
        var all = Vector128.Create(256);
        for (var c = 0; c < chunks; c++)
        {
            var j = 4 * c;
            tops[c] = Vector128.Create(above.Slice(j, 4));
            var weightX = Vector128.Create((int)weightsX[j], weightsX[j + 1], weightsX[j + 2], weightsX[j + 3]);
            weights[c] = weightX;
            rights[c] = (all - weightX) * topRight;
        }

        for (var i = 0; i < h; i++)
        {
            var row = destination.Slice(i * stride, w);
            var weightY = weightsY[i];
            var vertical = Vector128.Create((256 - weightY) * bottomLeft);
            var horizontal = Vector128.Create(left[i]);
            for (var c = 0; c < chunks; c++)
            {
                var value = mode switch
                {
                    Av1Constants.SmoothPred => ((tops[c] * weightY) + vertical + (weights[c] * horizontal) + rights[c] + Vector128.Create(256)) >> 9,
                    Av1Constants.SmoothVPred => ((tops[c] * weightY) + vertical + Vector128.Create(128)) >> 8,
                    _ => ((weights[c] * horizontal) + rights[c] + Vector128.Create(128)) >> 8,
                };
                StoreLanes(value, row[(4 * c)..]);
            }
        }
    }

    private static int[] CreateRecursiveTapsByNeighbor()
    {
        var values = new int[Av1Tables.IntraFilterTapsLength0 * 7 * 8];
        var taps = Av1Tables.IntraFilterTaps;
        for (var mode = 0; mode < Av1Tables.IntraFilterTapsLength0; mode++)
        {
            for (var output = 0; output < 8; output++)
            {
                for (var neighbor = 0; neighbor < 7; neighbor++)
                {
                    values[(((mode * 7) + neighbor) * 8) + output] = taps[(((mode * 8) + output) * 7) + neighbor];
                }
            }
        }

        return values;
    }

    private static ReadOnlySpan<byte> GetSmoothWeights(int log2) => log2 switch
    {
        2 => Av1Tables.SmWeightsTx4x4,
        3 => Av1Tables.SmWeightsTx8x8,
        4 => Av1Tables.SmWeightsTx16x16,
        5 => Av1Tables.SmWeightsTx32x32,
        _ => Av1Tables.SmWeightsTx64x64,
    };

    // Section 7.11.2.9 "Intra edge filter strength selection process"
    private static int GetEdgeFilterStrength(int w, int h, int filterType, int delta)
    {
        var d = Math.Abs(delta);
        var blkWh = w + h;
        var strength = 0;
        if (filterType is 0)
        {
            if (blkWh <= 8)
            {
                if (d >= 56)
                    strength = 1;
            }
            else if (blkWh <= 16)
            {
                if (d >= 40)
                    strength = 1;
            }
            else if (blkWh <= 24)
            {
                if (d >= 8)
                    strength = 1;

                if (d >= 16)
                    strength = 2;

                if (d >= 32)
                    strength = 3;
            }
            else if (blkWh <= 32)
            {
                strength = 1;
                if (d >= 4)
                    strength = 2;

                if (d >= 32)
                    strength = 3;
            }
            else
            {
                strength = 3;
            }
        }
        else if (blkWh <= 8)
        {
            if (d >= 40)
                strength = 1;

            if (d >= 64)
                strength = 2;
        }
        else if (blkWh <= 16)
        {
            if (d >= 20)
                strength = 1;

            if (d >= 48)
                strength = 2;
        }
        else if (blkWh <= 24)
        {
            if (d >= 4)
                strength = 3;
        }
        else
        {
            strength = 3;
        }

        return strength;
    }

    // Section 7.11.2.10 "Intra edge upsample selection process"
    private static bool UseUpsample(int w, int h, int filterType, int delta)
    {
        var d = Math.Abs(delta);
        var blkWh = w + h;
        if (d <= 0 || d >= 40)
            return false;

        return filterType is 0 ? blkWh <= 16 : blkWh <= 8;
    }

    // Section 7.11.2.11 "Intra edge upsample process": entries -1 to numPx - 1 become entries -2 to 2 * numPx - 2
    private static void UpsampleEdge(Edge buffer, int numPx, int bitDepth)
    {
        Span<int> dup = stackalloc int[numPx + 3];
        dup[0] = buffer[-1];
        for (var i = -1; i < numPx; i++)
        {
            dup[i + 2] = buffer[i];
        }

        dup[numPx + 2] = buffer[numPx - 1];
        var max = (1 << bitDepth) - 1;
        buffer[-2] = dup[0];
        for (var i = 0; i < numPx; i++)
        {
            var s = -dup[i] + (9 * dup[i + 1]) + (9 * dup[i + 2]) - dup[i + 3];
            buffer[(2 * i) - 1] = Math.Clamp(Round2Signed(s, 4), 0, max);
            buffer[2 * i] = dup[i + 2];
        }
    }

    // Section 7.11.2.12 "Intra edge filter process": filters entries -1 to size - 2 of the edge
    private static void FilterEdge(Edge buffer, int size, int strength, bool vectorized)
    {
        if (strength is 0)
            return;

        var kernel = Av1Tables.IntraEdgeKernel.Slice((strength - 1) * Av1Constants.IntraEdgeTaps, Av1Constants.IntraEdgeTaps);
        if (vectorized)
        {
            FilterEdgeVector(buffer, size, kernel);
            return;
        }

        Span<int> edge = stackalloc int[size];
        for (var i = 0; i < size; i++)
        {
            edge[i] = buffer[i - 1];
        }

        for (var i = 1; i < size; i++)
        {
            buffer[i - 1] = FilterEdgeSample(edge, kernel, i);
        }
    }

    // FilterEdge with 4 outputs at a time. The entries are copied with 2 copies of the first and of the last one on each
    // side, which replace the clamping of the positions: the output i is the sum of the copies i to i + 4 times the taps. The
    // lanes after the last output read copies that are not written, and are not stored
    [SkipLocalsInit]
    private static void FilterEdgeVector(Edge buffer, int size, ReadOnlySpan<byte> kernel)
    {
        var chunks = (size + 2) >> 2;
        Span<int> padded = unsafe(stackalloc int[(4 * chunks) + 8]);
        Span<int> filtered = unsafe(stackalloc int[4 * chunks]);
        buffer.Slice(-1, size).CopyTo(padded[2..]);
        padded[0] = padded[2];
        padded[1] = padded[2];
        padded[size + 2] = padded[size + 1];
        padded[size + 3] = padded[size + 1];
        var k0 = Vector128.Create((int)kernel[0]);
        var k1 = Vector128.Create((int)kernel[1]);
        var k2 = Vector128.Create((int)kernel[2]);
        var k3 = Vector128.Create((int)kernel[3]);
        var k4 = Vector128.Create((int)kernel[4]);
        var rounding = Vector128.Create(8);
        for (var c = 0; c < chunks; c++)
        {
            // The outputs 4c + 1 to 4c + 4
            var i = (4 * c) + 1;
            var sum = (k0 * Vector128.Create(padded[i..])) + (k1 * Vector128.Create(padded[(i + 1)..])) + (k2 * Vector128.Create(padded[(i + 2)..]))
                + (k3 * Vector128.Create(padded[(i + 3)..])) + (k4 * Vector128.Create(padded[(i + 4)..]));
            ((sum + rounding) >> 4).CopyTo(filtered[(4 * c)..]);
        }

        filtered[..(size - 1)].CopyTo(buffer.Slice(0, size - 1));
    }

    // The output i of the intra edge filter, from the unfiltered entries
    private static int FilterEdgeSample(ReadOnlySpan<int> edge, ReadOnlySpan<byte> kernel, int i)
    {
        var s = 0;
        for (var j = 0; j < Av1Constants.IntraEdgeTaps; j++)
        {
            var k = Math.Clamp(i - 2 + j, 0, edge.Length - 1);
            s += kernel[j] * edge[k];
        }

        return (s + 8) >> 4;
    }

    private static int Round2(int value, int bits) => (value + (1 << (bits - 1))) >> bits;

    private static int Round2Signed(int value, int bits) => value >= 0 ? Round2(value, bits) : -Round2(-value, bits);

    /// <summary>The <c>AboveRow</c> or <c>LeftCol</c> array of the specification, whose index starts below zero.</summary>
    private readonly ref struct Edge
    {
        private readonly Span<int> _storage;

        public Edge(Span<int> storage) => _storage = storage;

        public ref int this[int index] => ref _storage[index + EdgeOrigin];

        public Span<int> Slice(int index, int length) => _storage.Slice(index + EdgeOrigin, length);

        public Span<int> From(int index) => _storage[(index + EdgeOrigin)..];
    }
}
