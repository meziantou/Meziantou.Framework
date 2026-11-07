namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tables of the encoder that are the inverse of tables of the specification: the specification maps the value of a
/// syntax element to its meaning, the encoder needs the value that codes a meaning. They are derived from the tables of the
/// decoder when the type is initialized.
/// </summary>
internal static class Av1EncoderTables
{
    private const byte NotInSet = byte.MaxValue;

    private static readonly byte[] IntraTxTypeSet1 = Invert(Av1Tables.TxTypeIntraInvSet1);
    private static readonly byte[] IntraTxTypeSet2 = Invert(Av1Tables.TxTypeIntraInvSet2);
    private static readonly byte[] InterTxTypeSet1 = Invert(Av1Tables.TxTypeInterInvSet1);
    private static readonly byte[] InterTxTypeSet2 = Invert(Av1Tables.TxTypeInterInvSet2);
    private static readonly byte[] InterTxTypeSet3 = Invert(Av1Tables.TxTypeInterInvSet3);

    /// <summary>
    /// Returns the value of <c>intra_tx_type</c> or <c>inter_tx_type</c> that codes <paramref name="txType"/> in a transform
    /// set (the inverse of the <c>Tx_Type_*_Inv_Set*</c> tables), or -1 when the set does not hold the transform type.
    /// </summary>
    public static int GetTxTypeSymbol(bool isInter, int set, int txType)
    {
        var table = (isInter, set) switch
        {
            (false, Av1Constants.TxSetIntra1) => IntraTxTypeSet1,
            (false, _) => IntraTxTypeSet2,
            (true, Av1Constants.TxSetInter1) => InterTxTypeSet1,
            (true, Av1Constants.TxSetInter2) => InterTxTypeSet2,
            (true, _) => InterTxTypeSet3,
        };

        var symbol = table[txType];
        return symbol is NotInSet ? -1 : symbol;
    }

    private static byte[] Invert(ReadOnlySpan<byte> symbolToTxType)
    {
        var result = new byte[Av1Constants.TxTypes];
        result.AsSpan().Fill(NotInSet);
        for (var symbol = 0; symbol < symbolToTxType.Length; symbol++)
        {
            result[symbolToTxType[symbol]] = (byte)symbol;
        }

        return result;
    }
}
