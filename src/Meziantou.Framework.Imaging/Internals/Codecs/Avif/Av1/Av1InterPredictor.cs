using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The motion compensation kernels of the AV1 decoder (AV1 specification, sections 7.11.3.4 "Block inter prediction process"
/// and 7.11.3.5 "Block warp process"): they read a plane of a reference frame, whose samples outside the plane are the nearest
/// ones inside, and produce predictions with the intermediate precision that the rounding variables define.
/// </summary>
internal static class Av1InterPredictor
{
    /// <summary>The largest number of rows of the intermediate array of <see cref="PredictBlock"/>: a block of 128 rows from a reference of twice the height.</summary>
    public const int MaxIntermediateHeight = (((127 * 2048) + 1023) >> Av1Constants.ScaleSubpelBits) + 8;

    /// <summary>Predicts a block from a reference plane with the separable sub-sample interpolation filters.</summary>
    /// <param name="reference">The samples of the reference plane.</param>
    /// <param name="referenceStride">The number of samples of a row of <paramref name="reference"/>.</param>
    /// <param name="lastX">The column of the last sample of the reference plane.</param>
    /// <param name="lastY">The row of the last sample of the reference plane.</param>
    /// <param name="x">The column of the block in the reference plane, in units of 1/1024 sample.</param>
    /// <param name="y">The row of the block in the reference plane, in units of 1/1024 sample.</param>
    /// <param name="xStep">The horizontal distance between two samples of the block in the reference plane, in units of 1/1024 sample.</param>
    /// <param name="yStep">The vertical distance between two samples of the block in the reference plane, in units of 1/1024 sample.</param>
    /// <param name="w">The width of the block.</param>
    /// <param name="h">The height of the block.</param>
    /// <param name="horizontalFilter">The type of the horizontal filter (<c>InterpFilters[ candRow ][ candCol ][ 1 ]</c>).</param>
    /// <param name="verticalFilter">The type of the vertical filter (<c>InterpFilters[ candRow ][ candCol ][ 0 ]</c>).</param>
    /// <param name="interRound0">The number of bits rounded after the horizontal filter.</param>
    /// <param name="interRound1">The number of bits rounded after the vertical filter.</param>
    /// <param name="intermediate">A scratch of <see cref="MaxIntermediateHeight"/> rows of <paramref name="w"/> values.</param>
    /// <param name="pred">The prediction: <paramref name="h"/> rows of <paramref name="predStride"/> values.</param>
    /// <param name="predStride">The number of values of a row of <paramref name="pred"/>.</param>
    public static void PredictBlock(
        ReadOnlySpan<ushort> reference,
        int referenceStride,
        int lastX,
        int lastY,
        int x,
        int y,
        int xStep,
        int yStep,
        int w,
        int h,
        int horizontalFilter,
        int verticalFilter,
        int interRound0,
        int interRound1,
        Span<int> intermediate,
        Span<int> pred,
        int predStride)
    {
        if (Vector128.IsHardwareAccelerated && xStep is 1 << Av1Constants.ScaleSubpelBits && yStep is 1 << Av1Constants.ScaleSubpelBits && (w & 3) is 0)
        {
            PredictBlockUnscaled(reference, referenceStride, lastX, lastY, x, y, w, h, horizontalFilter, verticalFilter, interRound0, interRound1, intermediate, pred, predStride);
        }
        else
        {
            PredictBlockScalar(reference, referenceStride, lastX, lastY, x, y, xStep, yStep, w, h, horizontalFilter, verticalFilter, interRound0, interRound1, intermediate, pred, predStride);
        }
    }

    /// <summary>The scalar reference of <see cref="PredictBlock"/>.</summary>
    internal static void PredictBlockScalar(
        ReadOnlySpan<ushort> reference,
        int referenceStride,
        int lastX,
        int lastY,
        int x,
        int y,
        int xStep,
        int yStep,
        int w,
        int h,
        int horizontalFilter,
        int verticalFilter,
        int interRound0,
        int interRound1,
        Span<int> intermediate,
        Span<int> pred,
        int predStride)
    {
        var intermediateHeight = ((((h - 1) * yStep) + (1 << Av1Constants.ScaleSubpelBits) - 1) >> Av1Constants.ScaleSubpelBits) + 8;
        var filters = Av1Tables.SubpelFilters;
        var filterH = SelectFilter(horizontalFilter, w) * Av1Tables.SubpelFiltersLength1 * Av1Tables.SubpelFiltersLength2;
        var round0 = (1 << interRound0) >> 1;
        for (var r = 0; r < intermediateHeight; r++)
        {
            var source = reference.Slice(Math.Clamp((y >> Av1Constants.ScaleSubpelBits) + r - 3, 0, lastY) * referenceStride, lastX + 1);
            var destination = intermediate.Slice(r * w, w);
            for (var c = 0; c < w; c++)
            {
                var p = x + (xStep * c);
                var taps = filters.Slice(filterH + (((p >> 6) & Av1Constants.SubpelMask) * Av1Tables.SubpelFiltersLength2), 8);
                var first = (p >> Av1Constants.ScaleSubpelBits) - 3;
                var s = 0;
                if (first >= 0 && first + 7 <= lastX)
                {
                    var window = source.Slice(first, 8);
                    s = (taps[0] * window[0]) + (taps[1] * window[1]) + (taps[2] * window[2]) + (taps[3] * window[3])
                        + (taps[4] * window[4]) + (taps[5] * window[5]) + (taps[6] * window[6]) + (taps[7] * window[7]);
                }
                else
                {
                    for (var t = 0; t < 8; t++)
                    {
                        s += taps[t] * source[Math.Clamp(first + t, 0, lastX)];
                    }
                }

                destination[c] = (s + round0) >> interRound0;
            }
        }

        var filterV = SelectFilter(verticalFilter, h) * Av1Tables.SubpelFiltersLength1 * Av1Tables.SubpelFiltersLength2;
        var round1 = (1 << interRound1) >> 1;
        for (var r = 0; r < h; r++)
        {
            var p = (y & 1023) + (yStep * r);
            var taps = filters.Slice(filterV + (((p >> 6) & Av1Constants.SubpelMask) * Av1Tables.SubpelFiltersLength2), 8);
            var rows = intermediate[((p >> Av1Constants.ScaleSubpelBits) * w)..];
            var destination = pred.Slice(r * predStride, w);
            for (var c = 0; c < w; c++)
            {
                var s = (taps[0] * rows[c]) + (taps[1] * rows[w + c]) + (taps[2] * rows[(2 * w) + c]) + (taps[3] * rows[(3 * w) + c])
                    + (taps[4] * rows[(4 * w) + c]) + (taps[5] * rows[(5 * w) + c]) + (taps[6] * rows[(6 * w) + c]) + (taps[7] * rows[(7 * w) + c]);
                destination[c] = (s + round1) >> interRound1;
            }
        }
    }

    // PredictBlockScalar for a reference of the size of the frame (steps of one sample): every sample of the block uses the
    // same taps, and the taps of consecutive samples read consecutive samples. The rows whose taps are all inside the
    // reference are filtered 4 samples at a time with the integer expressions of the scalar code, the others like it
    private static void PredictBlockUnscaled(
        ReadOnlySpan<ushort> reference,
        int referenceStride,
        int lastX,
        int lastY,
        int x,
        int y,
        int w,
        int h,
        int horizontalFilter,
        int verticalFilter,
        int interRound0,
        int interRound1,
        Span<int> intermediate,
        Span<int> pred,
        int predStride)
    {
        var intermediateHeight = h + 7;
        var filters = Av1Tables.SubpelFilters;
        var tapsH = filters.Slice((SelectFilter(horizontalFilter, w) * Av1Tables.SubpelFiltersLength1 * Av1Tables.SubpelFiltersLength2) + (((x >> 6) & Av1Constants.SubpelMask) * Av1Tables.SubpelFiltersLength2), 8);
        var first = (x >> Av1Constants.ScaleSubpelBits) - 3;
        var round0 = Vector128.Create((1 << interRound0) >> 1);
        if (first >= 0 && first + w + 6 <= lastX)
        {
            var h0 = Vector128.Create((int)tapsH[0]);
            var h1 = Vector128.Create((int)tapsH[1]);
            var h2 = Vector128.Create((int)tapsH[2]);
            var h3 = Vector128.Create((int)tapsH[3]);
            var h4 = Vector128.Create((int)tapsH[4]);
            var h5 = Vector128.Create((int)tapsH[5]);
            var h6 = Vector128.Create((int)tapsH[6]);
            var h7 = Vector128.Create((int)tapsH[7]);
            for (var r = 0; r < intermediateHeight; r++)
            {
                var source = reference.Slice((Math.Clamp((y >> Av1Constants.ScaleSubpelBits) + r - 3, 0, lastY) * referenceStride) + first, w + 7);
                var destination = intermediate.Slice(r * w, w);
                for (var c = 0; c < w; c += 4)
                {
                    var s = (h0 * Load(source, c)) + (h1 * Load(source, c + 1)) + (h2 * Load(source, c + 2)) + (h3 * Load(source, c + 3))
                        + (h4 * Load(source, c + 4)) + (h5 * Load(source, c + 5)) + (h6 * Load(source, c + 6)) + (h7 * Load(source, c + 7));
                    ((s + round0) >> interRound0).CopyTo(destination[c..]);
                }
            }
        }
        else
        {
            for (var r = 0; r < intermediateHeight; r++)
            {
                var source = reference.Slice(Math.Clamp((y >> Av1Constants.ScaleSubpelBits) + r - 3, 0, lastY) * referenceStride, lastX + 1);
                var destination = intermediate.Slice(r * w, w);
                for (var c = 0; c < w; c++)
                {
                    var s = 0;
                    for (var t = 0; t < 8; t++)
                    {
                        s += tapsH[t] * source[Math.Clamp(first + c + t, 0, lastX)];
                    }

                    destination[c] = (s + round0.ToScalar()) >> interRound0;
                }
            }
        }

        // The rows of the vertical filter start at the row of the block: (y & 1023) is below one sample
        var tapsV = filters.Slice((SelectFilter(verticalFilter, h) * Av1Tables.SubpelFiltersLength1 * Av1Tables.SubpelFiltersLength2) + ((((y & 1023) >> 6) & Av1Constants.SubpelMask) * Av1Tables.SubpelFiltersLength2), 8);
        var v0 = Vector128.Create((int)tapsV[0]);
        var v1 = Vector128.Create((int)tapsV[1]);
        var v2 = Vector128.Create((int)tapsV[2]);
        var v3 = Vector128.Create((int)tapsV[3]);
        var v4 = Vector128.Create((int)tapsV[4]);
        var v5 = Vector128.Create((int)tapsV[5]);
        var v6 = Vector128.Create((int)tapsV[6]);
        var v7 = Vector128.Create((int)tapsV[7]);
        var round1 = Vector128.Create((1 << interRound1) >> 1);
        for (var r = 0; r < h; r++)
        {
            var rows = intermediate.Slice(r * w, 8 * w);
            var destination = pred.Slice(r * predStride, w);
            for (var c = 0; c < w; c += 4)
            {
                var s = (v0 * Vector128.Create(rows[c..])) + (v1 * Vector128.Create(rows[(w + c)..])) + (v2 * Vector128.Create(rows[((2 * w) + c)..])) + (v3 * Vector128.Create(rows[((3 * w) + c)..]))
                    + (v4 * Vector128.Create(rows[((4 * w) + c)..])) + (v5 * Vector128.Create(rows[((5 * w) + c)..])) + (v6 * Vector128.Create(rows[((6 * w) + c)..])) + (v7 * Vector128.Create(rows[((7 * w) + c)..]));
                ((s + round1) >> interRound1).CopyTo(destination[c..]);
            }
        }

        static Vector128<int> Load(ReadOnlySpan<ushort> samples, int index) => Vector128.WidenLower(Vector64.Create(samples.Slice(index, 4)).ToVector128()).AsInt32();
    }

    // The 4-tap variants of the filters are used for the blocks of 4 samples or less in the direction of the filter
    private static int SelectFilter(int interpFilter, int size)
    {
        if (size > 4)
            return interpFilter;

        return interpFilter switch
        {
            Av1Constants.Eighttap or Av1Constants.EighttapSharp => 4,
            Av1Constants.EighttapSmooth => 5,
            _ => interpFilter,
        };
    }

    /// <summary>
    /// Predicts the 8x8 section (<paramref name="i8"/>, <paramref name="j8"/>) of a block from a reference plane with a warp
    /// model, clipped to the size of the block.
    /// </summary>
    /// <param name="reference">The samples of the reference plane.</param>
    /// <param name="referenceStride">The number of samples of a row of <paramref name="reference"/>.</param>
    /// <param name="lastX">The column of the last sample of the reference plane.</param>
    /// <param name="lastY">The row of the last sample of the reference plane.</param>
    /// <param name="warpParams">The 6 parameters of the warp model.</param>
    /// <param name="shear">The outputs alpha, beta, gamma and delta of the setup shear process for <paramref name="warpParams"/>.</param>
    /// <param name="x">The column of the block in the plane being predicted.</param>
    /// <param name="y">The row of the block in the plane being predicted.</param>
    /// <param name="i8">The row of the section, in units of 8 samples.</param>
    /// <param name="j8">The column of the section, in units of 8 samples.</param>
    /// <param name="w">The width of the block.</param>
    /// <param name="h">The height of the block.</param>
    /// <param name="subX">The horizontal subsampling of the plane.</param>
    /// <param name="subY">The vertical subsampling of the plane.</param>
    /// <param name="interRound0">The number of bits rounded after the horizontal filter.</param>
    /// <param name="interRound1">The number of bits rounded after the vertical filter.</param>
    /// <param name="pred">The prediction of the block: <paramref name="h"/> rows of <paramref name="predStride"/> values.</param>
    /// <param name="predStride">The number of values of a row of <paramref name="pred"/>.</param>
    public static void WarpBlock(
        ReadOnlySpan<ushort> reference,
        int referenceStride,
        int lastX,
        int lastY,
        ReadOnlySpan<int> warpParams,
        ReadOnlySpan<int> shear,
        int x,
        int y,
        int i8,
        int j8,
        int w,
        int h,
        int subX,
        int subY,
        int interRound0,
        int interRound1,
        Span<int> pred,
        int predStride)
    {
        const int PrecisionMask = (1 << Av1Constants.WarpedmodelPrecBits) - 1;
        long srcX = (x + (j8 * 8) + 4) << subX;
        long srcY = (y + (i8 * 8) + 4) << subY;
        var dstX = (warpParams[2] * srcX) + (warpParams[3] * srcY) + warpParams[0];
        var dstY = (warpParams[4] * srcX) + (warpParams[5] * srcY) + warpParams[1];
        var x4 = dstX >> subX;
        var y4 = dstY >> subY;
        var ix4 = (int)(x4 >> Av1Constants.WarpedmodelPrecBits);
        var sx4 = (int)(x4 & PrecisionMask);
        var iy4 = (int)(y4 >> Av1Constants.WarpedmodelPrecBits);
        var sy4 = (int)(y4 & PrecisionMask);
        var alpha = shear[0];
        var beta = shear[1];
        var gamma = shear[2];
        var delta = shear[3];
        var filters = Av1Tables.WarpedFilters;
        var round0 = (1 << interRound0) >> 1;
        var round1 = (1 << interRound1) >> 1;

        // 15 rows of 8 values
        Span<int> intermediate = stackalloc int[15 * 8];
        for (var i1 = -7; i1 < 8; i1++)
        {
            var source = reference.Slice(Math.Clamp(iy4 + i1, 0, lastY) * referenceStride, lastX + 1);
            for (var i2 = -4; i2 < 4; i2++)
            {
                var sx = sx4 + (alpha * i2) + (beta * i1);
                var offs = ((sx + (1 << (Av1Constants.WarpeddiffPrecBits - 1))) >> Av1Constants.WarpeddiffPrecBits) + Av1Constants.WarpedpixelPrecShifts;
                var taps = filters.Slice(offs * Av1Tables.WarpedFiltersLength1, 8);
                var first = ix4 + i2 - 3;
                var s = 0;
                for (var i3 = 0; i3 < 8; i3++)
                {
                    s += taps[i3] * source[Math.Clamp(first + i3, 0, lastX)];
                }

                intermediate[((i1 + 7) * 8) + i2 + 4] = (s + round0) >> interRound0;
            }
        }

        for (var i1 = -4; i1 < Math.Min(4, h - (i8 * 8) - 4); i1++)
        {
            var destination = pred[((((i8 * 8) + i1 + 4) * predStride) + (j8 * 8))..];
            for (var i2 = -4; i2 < Math.Min(4, w - (j8 * 8) - 4); i2++)
            {
                var sy = sy4 + (gamma * i2) + (delta * i1);
                var offs = ((sy + (1 << (Av1Constants.WarpeddiffPrecBits - 1))) >> Av1Constants.WarpeddiffPrecBits) + Av1Constants.WarpedpixelPrecShifts;
                var taps = filters.Slice(offs * Av1Tables.WarpedFiltersLength1, 8);
                var s = 0;
                for (var i3 = 0; i3 < 8; i3++)
                {
                    s += taps[i3] * intermediate[((i1 + i3 + 4) * 8) + i2 + 4];
                }

                destination[i2 + 4] = (s + round1) >> interRound1;
            }
        }
    }
}
