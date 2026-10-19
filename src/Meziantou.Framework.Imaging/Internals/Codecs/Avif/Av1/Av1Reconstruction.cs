using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The dequantization and reconstruction of a transform block (AV1 specification, section 7.12) and the chroma from luma
/// prediction (section 7.11.5).
/// </summary>
internal static class Av1Reconstruction
{
    /// <summary>Returns <c>dc_q( b )</c> (section 7.12.2).</summary>
    public static int GetDcQuantizer(int qindex, int bitDepth) => Av1Tables.DcQlookup[(((bitDepth - 8) >> 1) * Av1Tables.DcQlookupLength1) + Math.Clamp(qindex, 0, 255)];

    /// <summary>Returns <c>ac_q( b )</c> (section 7.12.2).</summary>
    public static int GetAcQuantizer(int qindex, int bitDepth) => Av1Tables.AcQlookup[(((bitDepth - 8) >> 1) * Av1Tables.AcQlookupLength1) + Math.Clamp(qindex, 0, 255)];

    /// <summary>Returns <c>dqDenom</c>: what the dequantized coefficients of a transform size are divided by (section 7.12.3).</summary>
    public static int GetDequantizationDenominator(int txSz) => txSz switch
    {
        Av1Constants.Tx32x32 or Av1Constants.Tx16x32 or Av1Constants.Tx32x16 or Av1Constants.Tx16x64 or Av1Constants.Tx64x16 => 2,
        Av1Constants.Tx64x64 or Av1Constants.Tx32x64 or Av1Constants.Tx64x32 => 4,
        _ => 1,
    };

    /// <summary>
    /// Dequantizes the coefficients of a transform block, applies the inverse transform and adds the residual to the predicted
    /// samples (<c>reconstruct</c>, section 7.12.3).
    /// </summary>
    /// <param name="plane">The samples of the plane.</param>
    /// <param name="stride">The number of samples of a row of <paramref name="plane"/>.</param>
    /// <param name="x">The column of the top left sample of the transform block.</param>
    /// <param name="y">The row of the top left sample of the transform block.</param>
    /// <param name="quant"><c>Quant</c>: the coefficients, Min(32, width) per row. Only the positions of the <paramref name="eob"/> first entries of <paramref name="scan"/> are read: the other coefficients are zero.</param>
    /// <param name="scan">The scan order of the coefficients.</param>
    /// <param name="eob">The number of coefficients of the scan order that may not be zero.</param>
    /// <param name="scratch">Scratch for the dequantized coefficients (1024 entries) followed by the residual (4096 entries).</param>
    /// <param name="txSz">The transform size.</param>
    /// <param name="txType">The transform type (<c>PlaneTxType</c>).</param>
    /// <param name="lossless">Whether the block is lossless.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="dcQuantizer"><c>get_dc_quant( plane )</c>.</param>
    /// <param name="acQuantizer"><c>get_ac_quant( plane )</c>.</param>
    /// <param name="quantizerMatrix">
    /// The quantizer matrix of the transform size (<c>Quantizer_Matrix[ level ][ plane &gt; 0 ]</c> from <c>Qm_Offset[ txSz ]</c>), or
    /// an empty span when no matrix applies.
    /// </param>
    public static void Reconstruct(Span<ushort> plane, int stride, int x, int y, ReadOnlySpan<int> quant, Av1Scan scan, int eob, Span<int> scratch, int txSz, int txType, bool lossless, int bitDepth, int dcQuantizer, int acQuantizer, ReadOnlySpan<byte> quantizerMatrix)
    {
        var log2W = Av1Tables.TxWidthLog2[txSz];
        var log2H = Av1Tables.TxHeightLog2[txSz];
        var w = 1 << log2W;
        var h = 1 << log2H;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);
        var dqDenom = GetDequantizationDenominator(txSz);

        var dequant = scratch[..(tw * th)];
        var residual = scratch.Slice(1024, w * h);
        var min = -(1 << (7 + bitDepth));
        var max = (1 << (7 + bitDepth)) - 1;
        var useMatrix = !quantizerMatrix.IsEmpty && txType < Av1Constants.Idtx;
        var maxSample = (1 << bitDepth) - 1;
        if (eob is 1 && txType is Av1Constants.DctDct && !lossless && scan[0] is 0)
        {
            // Only the DC coefficient may not be zero: the residual is one value
            long dcQ = dcQuantizer;
            if (useMatrix)
                dcQ = ((dcQ * quantizerMatrix[0]) + 16) >> 5;

            AddConstant(plane, stride, x, y, w, h, GetDcOnlyResidual(Dequantize(quant[0], dcQ, dqDenom, min, max), txSz, bitDepth), maxSample);
            return;
        }

        dequant.Clear();
        for (var c = 0; c < eob; c++)
        {
            var i = scan[c];
            var coefficient = quant[i];
            if (coefficient is 0)
                continue;

            long q = i is 0 ? dcQuantizer : acQuantizer;
            if (useMatrix)
                q = ((q * quantizerMatrix[i]) + 16) >> 5;

            dequant[i] = Dequantize(coefficient, q, dqDenom, min, max);
        }

        Av1InverseTransform.Transform(dequant, residual, txSz, txType, lossless, bitDepth);

        var flipUD = txType is Av1Constants.FlipadstDct or Av1Constants.FlipadstAdst or Av1Constants.VFlipadst or Av1Constants.FlipadstFlipadst;
        var flipLR = txType is Av1Constants.DctFlipadst or Av1Constants.AdstFlipadst or Av1Constants.HFlipadst or Av1Constants.FlipadstFlipadst;
        for (var i = 0; i < h; i++)
        {
            var yy = flipUD ? h - i - 1 : i;
            var row = plane.Slice(((y + yy) * stride) + x, w);
            var residualRow = residual.Slice(i * w, w);
            if (flipLR)
            {
                for (var j = 0; j < w; j++)
                {
                    row[w - j - 1] = (ushort)Math.Clamp(row[w - j - 1] + residualRow[j], 0, maxSample);
                }
            }
            else
            {
                AddResidual(row, residualRow, maxSample);
            }
        }
    }

    /// <summary>
    /// Returns the residual of a DCT_DCT transform block whose only coefficient that is not zero is the DC coefficient:
    /// every sample of the residual of <see cref="Av1InverseTransform.Transform"/> is this value.
    /// </summary>
    /// <param name="dc">The dequantized DC coefficient.</param>
    /// <param name="txSz">The transform size.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    // The steps of the transform for this input: the input of the row transform is scaled for a 2:1 block; the butterfly of
    // the first two elements of the row DCT gives Round2( dc * cos128( 32 ), 12 ) to every element of the first row, the
    // other rotations combine it with zeros, and the Hadamard steps clamp it to the row range. Then the row shift and the
    // clamp to the column range, the same DCT for the columns (clamped to the column range) and the column shift
    internal static int GetDcOnlyResidual(int dc, int txSz, int bitDepth)
    {
        const int Cos128Of32 = 2896;
        var rowLimit = 1 << (bitDepth + 7);
        var colLimit = 1 << (Math.Max(bitDepth + 6, 16) - 1);
        var value = dc;
        if (Math.Abs(Av1Tables.TxWidthLog2[txSz] - Av1Tables.TxHeightLog2[txSz]) is 1)
            value = Round2(value * (long)Cos128Of32, 12);

        value = Math.Clamp(Round2(value * (long)Cos128Of32, 12), -rowLimit, rowLimit - 1);
        value = Math.Clamp(Round2(value, Av1Tables.TransformRowShift[txSz]), -colLimit, colLimit - 1);
        value = Math.Clamp(Round2(value * (long)Cos128Of32, 12), -colLimit, colLimit - 1);
        return Round2(value, 4);
    }

    // The dequantization of a coefficient (section 7.12.3)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Dequantize(int coefficient, long q, int dqDenom, int min, int max)
    {
        var dq = coefficient * q;
        var dq2 = (Math.Abs(dq) & 0xFFFFFF) / dqDenom;
        if (dq < 0)
            dq2 = -dq2;

        return (int)Math.Clamp(dq2, min, max);
    }

    private static int Round2(long value, int bits) => bits is 0 ? (int)value : (int)((value + (1L << (bits - 1))) >> bits);

    /// <summary>Adds <paramref name="value"/> to the samples of a block, clamped to 0 to <paramref name="maxSample"/>.</summary>
    internal static void AddConstant(Span<ushort> plane, int stride, int x, int y, int w, int h, int value, int maxSample)
    {
        if (value is 0)
            return;

        // The samples are 0 to maxSample, at most 4095: Clip( sample + value ) is Clip( sample + Clip( value, -maxSample,
        // maxSample ) ), whose sums fit 16-bit lanes
        var addend = (short)Math.Clamp(value, -maxSample, maxSample);
        for (var i = 0; i < h; i++)
        {
            var row = plane.Slice(((y + i) * stride) + x, w);
            var j = 0;
            if (Vector128.IsHardwareAccelerated && w >= Vector128<ushort>.Count)
            {
                var add = Vector128.Create(addend);
                var max = Vector128.Create((short)maxSample);
                for (; j < w; j += Vector128<ushort>.Count)
                {
                    var sum = Vector128.Create(row[j..]).AsInt16() + add;
                    Vector128.Min(Vector128.Max(sum, Vector128<short>.Zero), max).AsUInt16().CopyTo(row[j..]);
                }
            }
            else if (Vector64.IsHardwareAccelerated && w == Vector64<ushort>.Count)
            {
                var sum = Vector64.Create(row).AsInt16() + Vector64.Create(addend);
                Vector64.Min(Vector64.Max(sum, Vector64<short>.Zero), Vector64.Create((short)maxSample)).AsUInt16().CopyTo(row);
                j = w;
            }

            for (; j < w; j++)
            {
                row[j] = (ushort)Math.Clamp(row[j] + addend, 0, maxSample);
            }
        }
    }

    /// <summary>Adds a row of residual samples to a row of predicted samples, clamped to 0 to <paramref name="maxSample"/>.</summary>
    internal static void AddResidual(Span<ushort> row, ReadOnlySpan<int> residual, int maxSample)
    {
        var j = 0;
        if (Vector128.IsHardwareAccelerated && row.Length >= Vector128<ushort>.Count)
        {
            var max = Vector128.Create(maxSample);
            for (; j <= row.Length - Vector128<ushort>.Count; j += Vector128<ushort>.Count)
            {
                var (low, high) = Vector128.Widen(Vector128.Create(row[j..]));
                var lowSum = Vector128.Min(Vector128.Max(low.AsInt32() + Vector128.Create(residual[j..]), Vector128<int>.Zero), max);
                var highSum = Vector128.Min(Vector128.Max(high.AsInt32() + Vector128.Create(residual[(j + Vector128<int>.Count)..]), Vector128<int>.Zero), max);
                Vector128.Narrow(lowSum.AsUInt32(), highSum.AsUInt32()).CopyTo(row[j..]);
            }
        }

        AddResidualScalar(row[j..], residual[j..row.Length], maxSample);
    }

    /// <summary>The scalar reference of <see cref="AddResidual"/>.</summary>
    internal static void AddResidualScalar(Span<ushort> row, ReadOnlySpan<int> residual, int maxSample)
    {
        for (var j = 0; j < row.Length; j++)
        {
            row[j] = (ushort)Math.Clamp(row[j] + residual[j], 0, maxSample);
        }
    }

    /// <summary>
    /// Adds the scaled alternating part of the reconstructed luma samples to the DC predicted chroma samples
    /// (<c>predict_chroma_from_luma</c>, section 7.11.5).
    /// </summary>
    /// <param name="luma">The samples of the luma plane.</param>
    /// <param name="lumaStride">The number of samples of a row of <paramref name="luma"/>.</param>
    /// <param name="chroma">The samples of the chroma plane being predicted.</param>
    /// <param name="chromaStride">The number of samples of a row of <paramref name="chroma"/>.</param>
    /// <param name="startX">The column of the top left chroma sample of the transform block.</param>
    /// <param name="startY">The row of the top left chroma sample of the transform block.</param>
    /// <param name="txSz">The transform size.</param>
    /// <param name="alpha"><c>CflAlphaU</c> or <c>CflAlphaV</c>.</param>
    /// <param name="subX">The horizontal subsampling of the chroma planes.</param>
    /// <param name="subY">The vertical subsampling of the chroma planes.</param>
    /// <param name="maxLumaW"><c>MaxLumaW</c>: the end of the reconstructed luma samples of the block.</param>
    /// <param name="maxLumaH"><c>MaxLumaH</c>.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="scratch">Scratch for the subsampled luma samples (1024 entries).</param>
    public static void PredictChromaFromLuma(ReadOnlySpan<ushort> luma, int lumaStride, Span<ushort> chroma, int chromaStride, int startX, int startY, int txSz, int alpha, int subX, int subY, int maxLumaW, int maxLumaH, int bitDepth, Span<int> scratch)
    {
        if (AdvSimd.IsSupported)
        {
            PredictChromaFromLumaAdvSimd(luma, lumaStride, chroma, chromaStride, startX, startY, txSz, alpha, subX, subY, maxLumaW, maxLumaH, bitDepth, scratch);
        }
        else
        {
            PredictChromaFromLumaScalar(luma, lumaStride, chroma, chromaStride, startX, startY, txSz, alpha, subX, subY, maxLumaW, maxLumaH, bitDepth, scratch);
        }
    }

    // PredictChromaFromLumaScalar with 4 samples at once. In a row, the luma columns of the chroma samples are not clamped
    // up to the last one inside MaxLumaW, then they are all the clamped column: the samples up to it are subsampled 4 at a
    // time (pairwise additions for a horizontal subsampling), the others take the value of the clamped column. The
    // integer expressions are the scalar ones
    private static void PredictChromaFromLumaAdvSimd(ReadOnlySpan<ushort> luma, int lumaStride, Span<ushort> chroma, int chromaStride, int startX, int startY, int txSz, int alpha, int subX, int subY, int maxLumaW, int maxLumaH, int bitDepth, Span<int> scratch)
    {
        var w = Av1Tables.TxWidth[txSz];
        var h = Av1Tables.TxHeight[txSz];
        var lastX = maxLumaW - (1 << subX);
        var count = Math.Clamp((lastX >> subX) - startX + 1, 0, w);
        var scale = 3 - subX - subY;
        var total = Vector128<int>.Zero;
        var lumaAverage = 0;
        for (var i = 0; i < h; i++)
        {
            var lumaY = Math.Min((startY + i) << subY, maxLumaH - (1 << subY));
            var row0 = luma[(lumaY * lumaStride)..];
            var row1 = subY is 0 ? row0 : luma[((lumaY + 1) * lumaStride)..];
            var values = scratch.Slice(i * w, w);
            var j = 0;
            for (; j <= count - 4; j += 4)
            {
                var x = (startX + j) << subX;
                Vector128<int> sum;
                if (subX is 0)
                {
                    sum = Vector128.WidenLower(Vector64.Create(row0.Slice(x, 4)).ToVector128()).AsInt32();
                    if (subY is not 0)
                        sum += Vector128.WidenLower(Vector64.Create(row1.Slice(x, 4)).ToVector128()).AsInt32();
                }
                else
                {
                    var samples = Vector128.Create(row0.Slice(x, 8));
                    if (subY is not 0)
                        samples += Vector128.Create(row1.Slice(x, 8));

                    sum = AdvSimd.AddPairwiseWidening(samples).AsInt32();
                }

                var v = sum << scale;
                v.CopyTo(values[j..]);
                total += v;
            }

            for (; j < w; j++)
            {
                var lumaX = Math.Min((startX + j) << subX, lastX);
                var t = 0;
                for (var dy = 0; dy <= subY; dy++)
                {
                    for (var dx = 0; dx <= subX; dx++)
                    {
                        t += luma[((lumaY + dy) * lumaStride) + lumaX + dx];
                    }
                }

                var v = t << scale;
                values[j] = v;
                lumaAverage += v;
            }
        }

        lumaAverage += Vector128.Sum(total);
        var shift = Av1Tables.TxWidthLog2[txSz] + Av1Tables.TxHeightLog2[txSz];
        lumaAverage = (lumaAverage + (1 << (shift - 1))) >> shift;
        var average = Vector128.Create(lumaAverage);
        var max = Vector128.Create((1 << bitDepth) - 1);
        var rounding = Vector128.Create(32);
        for (var i = 0; i < h; i++)
        {
            var row = chroma.Slice(((startY + i) * chromaStride) + startX, w);
            var values = scratch.Slice(i * w, w);
            for (var j = 0; j < w; j += 4)
            {
                // (product + 32) >> 6 of the magnitude, with the sign of the product
                var product = (Vector128.Create(values[j..]) - average) * alpha;
                var sign = product >> 31;
                var magnitude = (Vector128.Abs(product) + rounding) >> 6;
                var scaled = (magnitude ^ sign) - sign;
                var predicted = Vector128.WidenLower(Vector64.Create(row[j..]).ToVector128()).AsInt32();
                var value = Vector128.Min(Vector128.Max(predicted + scaled, Vector128<int>.Zero), max);
                Vector128.Narrow(value.AsUInt32(), value.AsUInt32()).GetLower().CopyTo(row[j..]);
            }
        }
    }

    /// <summary>The scalar reference of <see cref="PredictChromaFromLuma"/>.</summary>
    internal static void PredictChromaFromLumaScalar(ReadOnlySpan<ushort> luma, int lumaStride, Span<ushort> chroma, int chromaStride, int startX, int startY, int txSz, int alpha, int subX, int subY, int maxLumaW, int maxLumaH, int bitDepth, Span<int> scratch)
    {
        var w = Av1Tables.TxWidth[txSz];
        var h = Av1Tables.TxHeight[txSz];
        var lumaAverage = 0;
        for (var i = 0; i < h; i++)
        {
            var lumaY = Math.Min((startY + i) << subY, maxLumaH - (1 << subY));
            for (var j = 0; j < w; j++)
            {
                var lumaX = Math.Min((startX + j) << subX, maxLumaW - (1 << subX));
                var t = 0;
                for (var dy = 0; dy <= subY; dy++)
                {
                    for (var dx = 0; dx <= subX; dx++)
                    {
                        t += luma[((lumaY + dy) * lumaStride) + lumaX + dx];
                    }
                }

                var v = t << (3 - subX - subY);
                scratch[(i * w) + j] = v;
                lumaAverage += v;
            }
        }

        var shift = Av1Tables.TxWidthLog2[txSz] + Av1Tables.TxHeightLog2[txSz];
        lumaAverage = (lumaAverage + (1 << (shift - 1))) >> shift;
        var maxSample = (1 << bitDepth) - 1;
        for (var i = 0; i < h; i++)
        {
            var row = chroma.Slice(((startY + i) * chromaStride) + startX, w);
            for (var j = 0; j < w; j++)
            {
                var product = alpha * (scratch[(i * w) + j] - lumaAverage);
                var scaledLuma = product >= 0 ? (product + 32) >> 6 : -((-product + 32) >> 6);
                row[j] = (ushort)Math.Clamp(row[j] + scaledLuma, 0, maxSample);
            }
        }
    }
}
