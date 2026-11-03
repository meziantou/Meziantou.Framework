namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The context offsets of the <c>coeff_base</c> symbols of the transform blocks of the 2D class, by position: the
/// <c>Coeff_Base_Ctx_Offset[ txSz ][ Min( row, 4 ) ][ Min( col, 4 ) ]</c> of the coefficient contexts (AV1 specification,
/// section 8.3.2), for the positions of the coefficients of each transform size.
/// </summary>
internal static class Av1CoefficientContexts
{
    private static readonly byte[][] BaseOffsets = CreateBaseOffsets();

    /// <summary>Gets the offsets of the positions of a transform size: Min(32, width) per row.</summary>
    public static ReadOnlySpan<byte> GetBaseOffsets(int txSz) => BaseOffsets[txSz];

    private static byte[][] CreateBaseOffsets()
    {
        var result = new byte[Av1Constants.TxSizesAll][];
        for (var txSz = 0; txSz < result.Length; txSz++)
        {
            var adjTxSz = Av1Tables.AdjustedTxSize[txSz];
            var bwl = Av1Tables.TxWidthLog2[adjTxSz];
            var height = Av1Tables.TxHeight[adjTxSz];
            var offsets = new byte[height << bwl];
            for (var pos = 0; pos < offsets.Length; pos++)
            {
                var row = pos >> bwl;
                var col = pos & ((1 << bwl) - 1);
                offsets[pos] = Av1Tables.CoeffBaseCtxOffset[(((txSz * 5) + Math.Min(row, 4)) * 5) + Math.Min(col, 4)];
            }

            result[txSz] = offsets;
        }

        return result;
    }
}
