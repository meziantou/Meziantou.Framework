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

    /// <summary>
    /// Dequantizes the coefficients of a transform block, applies the inverse transform and adds the residual to the predicted
    /// samples (<c>reconstruct</c>, section 7.12.3).
    /// </summary>
    /// <param name="plane">The samples of the plane.</param>
    /// <param name="stride">The number of samples of a row of <paramref name="plane"/>.</param>
    /// <param name="x">The column of the top left sample of the transform block.</param>
    /// <param name="y">The row of the top left sample of the transform block.</param>
    /// <param name="quant"><c>Quant</c>: the coefficients, Min(32, width) per row.</param>
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
    public static void Reconstruct(Span<ushort> plane, int stride, int x, int y, ReadOnlySpan<int> quant, Span<int> scratch, int txSz, int txType, bool lossless, int bitDepth, int dcQuantizer, int acQuantizer, ReadOnlySpan<byte> quantizerMatrix)
    {
        var log2W = Av1Tables.TxWidthLog2[txSz];
        var log2H = Av1Tables.TxHeightLog2[txSz];
        var w = 1 << log2W;
        var h = 1 << log2H;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);
        var dqDenom = txSz switch
        {
            Av1Constants.Tx32x32 or Av1Constants.Tx16x32 or Av1Constants.Tx32x16 or Av1Constants.Tx16x64 or Av1Constants.Tx64x16 => 2,
            Av1Constants.Tx64x64 or Av1Constants.Tx32x64 or Av1Constants.Tx64x32 => 4,
            _ => 1,
        };

        var dequant = scratch[..(tw * th)];
        var residual = scratch.Slice(1024, w * h);
        var min = -(1 << (7 + bitDepth));
        var max = (1 << (7 + bitDepth)) - 1;
        var useMatrix = !quantizerMatrix.IsEmpty && txType < Av1Constants.Idtx;
        for (var i = 0; i < tw * th; i++)
        {
            var coefficient = quant[i];
            if (coefficient is 0)
            {
                dequant[i] = 0;
                continue;
            }

            long q = i is 0 ? dcQuantizer : acQuantizer;
            if (useMatrix)
                q = ((q * quantizerMatrix[i]) + 16) >> 5;

            var dq = coefficient * q;
            var dq2 = (Math.Abs(dq) & 0xFFFFFF) / dqDenom;
            if (dq < 0)
                dq2 = -dq2;

            dequant[i] = (int)Math.Clamp(dq2, min, max);
        }

        Av1InverseTransform.Transform(dequant, residual, txSz, txType, lossless, bitDepth);

        var flipUD = txType is Av1Constants.FlipadstDct or Av1Constants.FlipadstAdst or Av1Constants.VFlipadst or Av1Constants.FlipadstFlipadst;
        var flipLR = txType is Av1Constants.DctFlipadst or Av1Constants.AdstFlipadst or Av1Constants.HFlipadst or Av1Constants.FlipadstFlipadst;
        var maxSample = (1 << bitDepth) - 1;
        for (var i = 0; i < h; i++)
        {
            var yy = flipUD ? h - i - 1 : i;
            var row = plane.Slice(((y + yy) * stride) + x, w);
            var residualRow = residual.Slice(i * w, w);
            for (var j = 0; j < w; j++)
            {
                var xx = flipLR ? w - j - 1 : j;
                row[xx] = (ushort)Math.Clamp(row[xx] + residualRow[j], 0, maxSample);
            }
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
