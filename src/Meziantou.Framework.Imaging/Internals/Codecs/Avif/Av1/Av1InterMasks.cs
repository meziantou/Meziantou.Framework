namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The masks that blend two predictions of an AV1 block (AV1 specification, sections 7.11.3.11 "Wedge mask process",
/// 7.11.3.12 "Difference weight mask process" and 7.11.3.13 "Intra mode variant mask process"). A mask holds one weight from
/// 0 to 64 per sample.
/// </summary>
internal static class Av1InterMasks
{
    /// <summary>
    /// Fills the wedge mask of a block (<c>WedgeMasks[ miSize ][ wedgeSign ][ wedgeIndex ]</c>). The masks are computed from
    /// the master masks instead of being stored: a block that uses one has at most 1024 samples.
    /// </summary>
    /// <param name="mask">The mask: rows of <paramref name="maskStride"/> weights.</param>
    /// <param name="maskStride">The number of weights of a row of <paramref name="mask"/>.</param>
    /// <param name="miSize">The size of the block, which has a wedge codebook (<c>Wedge_Bits[ miSize ]</c> is not 0).</param>
    /// <param name="wedgeSign">The sign of the wedge.</param>
    /// <param name="wedgeIndex">The index of the wedge in the codebook.</param>
    public static void FillWedgeMask(Span<byte> mask, int maskStride, int miSize, int wedgeSign, int wedgeIndex)
    {
        int w4 = Av1Tables.Num4x4BlocksWide[miSize];
        int h4 = Av1Tables.Num4x4BlocksHigh[miSize];
        var w = w4 * 4;
        var h = h4 * 4;
        int shape;
        if (h4 > w4)
        {
            shape = 0;
        }
        else
        {
            shape = h4 < w4 ? 1 : 2;
        }

        var entry = ((shape * Av1Tables.WedgeCodebookLength1) + wedgeIndex) * Av1Tables.WedgeCodebookLength2;
        int direction = Av1Tables.WedgeCodebook[entry];
        var xoff = (Av1Constants.MaskMasterSize / 2) - ((Av1Tables.WedgeCodebook[entry + 1] * w) >> 3);
        var yoff = (Av1Constants.MaskMasterSize / 2) - ((Av1Tables.WedgeCodebook[entry + 2] * h) >> 3);
        var sum = 0;
        for (var i = 0; i < w; i++)
        {
            sum += GetMasterMask(direction, yoff, xoff + i);
        }

        for (var i = 1; i < h; i++)
        {
            sum += GetMasterMask(direction, yoff + i, xoff);
        }

        var avg = (sum + ((w + h - 1) / 2)) / (w + h - 1);
        var flipSign = avg < 32 ? 1 : 0;
        var invert = wedgeSign != flipSign;
        for (var i = 0; i < h; i++)
        {
            var row = mask.Slice(i * maskStride, w);
            for (var j = 0; j < w; j++)
            {
                var value = GetMasterMask(direction, yoff + i, xoff + j);
                row[j] = (byte)(invert ? 64 - value : value);
            }
        }
    }

    // MasterMask[ direction ][ row ][ col ] of initialise_wedge_mask_table
    private static int GetMasterMask(int direction, int row, int col)
    {
        const int Last = Av1Constants.MaskMasterSize - 1;
        return direction switch
        {
            Av1Constants.WedgeHorizontal => Av1Tables.WedgeMasterVertical[row],
            Av1Constants.WedgeVertical => Av1Tables.WedgeMasterVertical[col],
            Av1Constants.WedgeOblique27 => GetOblique63(col, row),
            Av1Constants.WedgeOblique63 => GetOblique63(row, col),
            Av1Constants.WedgeOblique117 => 64 - GetOblique63(row, Last - col),
            _ => 64 - GetOblique63(col, Last - row),
        };

        static int GetOblique63(int i, int j)
        {
            // The shift starts at a quarter of the size of the master mask and decreases by one every two rows, between the
            // even row and the odd row of the pair
            var shift = (Av1Constants.MaskMasterSize / 4) - (i >> 1);
            if ((i & 1) is 0)
                return Av1Tables.WedgeMasterObliqueEven[Math.Clamp(j - shift, 0, Last)];

            return Av1Tables.WedgeMasterObliqueOdd[Math.Clamp(j - shift + 1, 0, Last)];
        }
    }

    /// <summary>Fills the mask of a compound prediction whose weights depend on the difference between the two predictions.</summary>
    public static void FillDifferenceWeightMask(Span<byte> mask, int maskStride, ReadOnlySpan<int> pred0, ReadOnlySpan<int> pred1, int predStride, int w, int h, int bitDepth, int interPostRound, int maskType)
    {
        var bits = bitDepth - 8 + interPostRound;
        var half = (1 << bits) >> 1;
        for (var i = 0; i < h; i++)
        {
            var row = mask.Slice(i * maskStride, w);
            var row0 = pred0.Slice(i * predStride, w);
            var row1 = pred1.Slice(i * predStride, w);
            for (var j = 0; j < w; j++)
            {
                var diff = (Math.Abs(row0[j] - row1[j]) + half) >> bits;
                var m = Math.Clamp(38 + (diff / 16), 0, 64);
                row[j] = (byte)(maskType is not 0 ? 64 - m : m);
            }
        }
    }

    /// <summary>Fills the mask that blends an inter prediction with an intra prediction, from the mode of the intra prediction.</summary>
    public static void FillIntraVariantMask(Span<byte> mask, int maskStride, int w, int h, int interintraMode)
    {
        var sizeScale = Av1Constants.MaxSbSize / Math.Max(h, w);
        var weights = Av1Tables.IiWeights1d;
        for (var i = 0; i < h; i++)
        {
            var row = mask.Slice(i * maskStride, w);
            for (var j = 0; j < w; j++)
            {
                row[j] = interintraMode switch
                {
                    Av1Constants.IiVPred => weights[i * sizeScale],
                    Av1Constants.IiHPred => weights[j * sizeScale],
                    Av1Constants.IiSmoothPred => weights[Math.Min(i, j) * sizeScale],
                    _ => 32,
                };
            }
        }
    }
}
