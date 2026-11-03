namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The positions of the coefficients of the transform blocks in the level map of the decoder, with the context offsets of
/// their <c>coeff_base</c> symbols in the 2D class: the <c>Coeff_Base_Ctx_Offset[ txSz ][ Min( row, 4 ) ][ Min( col, 4 ) ]</c>
/// of the coefficient contexts (AV1 specification, section 8.3.2).
/// </summary>
internal static class Av1CoefficientContexts
{
    /// <summary>The number of entries of a row of the level map: the widest coefficients (32) and the padding of their neighbors.</summary>
    public const int LevelStride = 32 + Av1TileScratch.LevelMapPadding;

    /// <summary>The shift of the index in the map of an entry of <see cref="GetPositions"/>.</summary>
    public const int IndexShift = 5;

    /// <summary>The mask of the context offset of an entry of <see cref="GetPositions"/>.</summary>
    public const int OffsetMask = (1 << IndexShift) - 1;

    private static readonly ushort[][] Positions = CreatePositions();

    /// <summary>
    /// Gets the entries of the positions of a transform size (Min(32, width) per row): the index of the position in the level
    /// map (row * <see cref="LevelStride"/> + column) shifted by <see cref="IndexShift"/>, and the context offset.
    /// </summary>
    public static ReadOnlySpan<ushort> GetPositions(int txSz) => Positions[txSz];

    private static ushort[][] CreatePositions()
    {
        var result = new ushort[Av1Constants.TxSizesAll][];
        for (var txSz = 0; txSz < result.Length; txSz++)
        {
            var adjTxSz = Av1Tables.AdjustedTxSize[txSz];
            var bwl = Av1Tables.TxWidthLog2[adjTxSz];
            var height = Av1Tables.TxHeight[adjTxSz];
            var positions = new ushort[height << bwl];
            for (var pos = 0; pos < positions.Length; pos++)
            {
                var row = pos >> bwl;
                var col = pos & ((1 << bwl) - 1);
                var offset = Av1Tables.CoeffBaseCtxOffset[(((txSz * 5) + Math.Min(row, 4)) * 5) + Math.Min(col, 4)];
                positions[pos] = (ushort)((((row * LevelStride) + col) << IndexShift) | offset);
            }

            result[txSz] = positions;
        }

        return result;
    }
}
