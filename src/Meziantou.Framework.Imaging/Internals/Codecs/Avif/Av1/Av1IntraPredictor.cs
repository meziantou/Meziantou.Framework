namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The intra prediction process of AV1 (AV1 specification, section 7.11.2): predicts a transform block of a plane from the
/// reconstructed samples above and to the left of it. The kernels follow the ordered steps of the specification; they are the
/// numerical reference of the decoder.
/// </summary>
internal static class Av1IntraPredictor
{
    // AboveRow and LeftCol are indexed from -16: the edge upsampling process writes entry -2 and reads entry -1
    private const int EdgeOrigin = 16;

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
    {
        var w = 1 << log2W;
        var h = 1 << log2H;
        var bitDepth = parameters.BitDepth;

        // The edges hold w + h samples, or twice as many once a short edge is upsampled, and the entries from -16
        var edgeLength = EdgeOrigin + Math.Max(w + h, 32) + 16;
        Span<int> aboveStorage = stackalloc int[edgeLength];
        Span<int> leftStorage = stackalloc int[edgeLength];
        var above = new Edge(aboveStorage);
        var left = new Edge(leftStorage);

        if (haveAbove)
        {
            var aboveLimit = Math.Min(maxX, x + (haveAboveRight ? 2 * w : w) - 1);
            var row = plane.Slice((y - 1) * stride, stride);
            for (var i = 0; i < w + h; i++)
            {
                above[i] = row[Math.Min(aboveLimit, x + i)];
            }
        }
        else
        {
            var value = haveLeft ? plane[(y * stride) + x - 1] : (1 << (bitDepth - 1)) - 1;
            for (var i = 0; i < w + h; i++)
            {
                above[i] = value;
            }
        }

        if (haveLeft)
        {
            var leftLimit = Math.Min(maxY, y + (haveBelowLeft ? 2 * h : h) - 1);
            for (var i = 0; i < w + h; i++)
            {
                left[i] = plane[(Math.Min(leftLimit, y + i) * stride) + x - 1];
            }
        }
        else
        {
            var value = haveAbove ? plane[((y - 1) * stride) + x] : (1 << (bitDepth - 1)) + 1;
            for (var i = 0; i < w + h; i++)
            {
                left[i] = value;
            }
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

        var destination = plane[((y * stride) + x)..];
        var mode = parameters.Mode;
        if (parameters.FilterIntraMode >= 0)
        {
            PredictRecursive(destination, stride, w, h, above, left, parameters.FilterIntraMode, bitDepth);
        }
        else if (mode is >= Av1Constants.VPred and <= Av1Constants.D67Pred)
        {
            PredictDirectional(destination, stride, x, y, haveLeft, haveAbove, w, h, maxX, maxY, above, left, parameters);
        }
        else if (mode is Av1Constants.SmoothPred or Av1Constants.SmoothVPred or Av1Constants.SmoothHPred)
        {
            PredictSmooth(destination, stride, mode, log2W, log2H, above, left);
        }
        else if (mode is Av1Constants.DcPred)
        {
            PredictDc(destination, stride, haveLeft, haveAbove, log2W, log2H, above, left, bitDepth);
        }
        else
        {
            PredictPaeth(destination, stride, w, h, above, left);
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

    // Section 7.11.2.4 "Directional intra prediction process"
    private static void PredictDirectional(Span<ushort> destination, int stride, int x, int y, bool haveLeft, bool haveAbove, int w, int h, int maxX, int maxY, Edge above, Edge left, in Parameters parameters)
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
                    FilterEdge(above, numPx, strength);
                }

                if (haveLeft)
                {
                    var strength = GetEdgeFilterStrength(w, h, filterType, pAngle - 180);
                    var numPx = Math.Min(h, maxY - y + 1) + (pAngle > 180 ? w : 0) + 1;
                    FilterEdge(left, numPx, strength);
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

        if (pAngle < 90)
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
    private static void FilterEdge(Edge buffer, int size, int strength)
    {
        if (strength is 0)
            return;

        Span<int> edge = stackalloc int[size];
        for (var i = 0; i < size; i++)
        {
            edge[i] = buffer[i - 1];
        }

        var kernel = Av1Tables.IntraEdgeKernel.Slice((strength - 1) * Av1Constants.IntraEdgeTaps, Av1Constants.IntraEdgeTaps);
        for (var i = 1; i < size; i++)
        {
            var s = 0;
            for (var j = 0; j < Av1Constants.IntraEdgeTaps; j++)
            {
                var k = Math.Clamp(i - 2 + j, 0, size - 1);
                s += kernel[j] * edge[k];
            }

            buffer[i - 1] = (s + 8) >> 4;
        }
    }

    private static int Round2(int value, int bits) => (value + (1 << (bits - 1))) >> bits;

    private static int Round2Signed(int value, int bits) => value >= 0 ? Round2(value, bits) : -Round2(-value, bits);

    /// <summary>The <c>AboveRow</c> or <c>LeftCol</c> array of the specification, whose index starts below zero.</summary>
    private readonly ref struct Edge
    {
        private readonly Span<int> _storage;

        public Edge(Span<int> storage) => _storage = storage;

        public ref int this[int index] => ref _storage[index + EdgeOrigin];
    }
}
