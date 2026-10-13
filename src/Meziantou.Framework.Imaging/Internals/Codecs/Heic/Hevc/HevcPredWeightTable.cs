namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The weighted prediction parameters of a slice (Rec. ITU-T H.265, clauses 7.3.6.3 "Weighted prediction parameters
/// syntax" and 7.4.7.3 "Weighted prediction parameters semantics"): a weight and an offset for each reference picture of
/// each list, for the luma and for each chroma component.
/// </summary>
internal sealed class HevcPredWeightTable
{
    private const int MaxRefIdx = 15;

    // For each of the two lists, 15 reference pictures
    private readonly int[] _lumaWeight = new int[2 * MaxRefIdx];
    private readonly int[] _lumaOffset = new int[2 * MaxRefIdx];

    // For each of the two lists, 15 reference pictures of two chroma components
    private readonly int[] _chromaWeight = new int[2 * MaxRefIdx * 2];
    private readonly int[] _chromaOffset = new int[2 * MaxRefIdx * 2];

    private HevcPredWeightTable()
    {
    }

    public int LumaLog2WeightDenom { get; private set; }

    /// <summary>Gets <c>ChromaLog2WeightDenom</c>.</summary>
    public int ChromaLog2WeightDenom { get; private set; }

    /// <summary>Gets <c>LumaWeightL0[ i ]</c> (<paramref name="list"/> 0) or <c>LumaWeightL1[ i ]</c> (1).</summary>
    public int GetLumaWeight(int list, int refIdx) => _lumaWeight[(list * MaxRefIdx) + refIdx];

    /// <summary>Gets <c>luma_offset_l0[ i ]</c> or <c>luma_offset_l1[ i ]</c>.</summary>
    public int GetLumaOffset(int list, int refIdx) => _lumaOffset[(list * MaxRefIdx) + refIdx];

    /// <summary>Gets <c>ChromaWeightL0[ i ][ j ]</c> or <c>ChromaWeightL1[ i ][ j ]</c>.</summary>
    public int GetChromaWeight(int list, int refIdx, int component) => _chromaWeight[(((list * MaxRefIdx) + refIdx) * 2) + component];

    /// <summary>Gets <c>ChromaOffsetL0[ i ][ j ]</c> or <c>ChromaOffsetL1[ i ][ j ]</c>.</summary>
    public int GetChromaOffset(int list, int refIdx, int component) => _chromaOffset[(((list * MaxRefIdx) + refIdx) * 2) + component];

    /// <summary>Reads the syntax structure <c>pred_weight_table( )</c>.</summary>
    /// <exception cref="InvalidImageContentException">A value is out of range, or the data are truncated.</exception>
    public static HevcPredWeightTable Parse(ref HevcBitReader reader, HevcSequenceParameterSet sps, int sliceType, int numRefIdxL0ActiveMinus1, int numRefIdxL1ActiveMinus1)
    {
        var table = new HevcPredWeightTable();
        table.LumaLog2WeightDenom = reader.ReadUE(7, "luma_log2_weight_denom");
        table.ChromaLog2WeightDenom = table.LumaLog2WeightDenom;
        if (sps.ChromaArrayType is not 0)
        {
            table.ChromaLog2WeightDenom += reader.ReadSE(-7, 7, "delta_chroma_log2_weight_denom");
            if (table.ChromaLog2WeightDenom is < 0 or > 7)
                throw HevcErrors.Invalid("delta_chroma_log2_weight_denom is out of range.");
        }

        table.ParseList(ref reader, sps, 0, numRefIdxL0ActiveMinus1);
        if (sliceType is HevcConstants.SliceB)
        {
            table.ParseList(ref reader, sps, 1, numRefIdxL1ActiveMinus1);
        }

        return table;
    }

    private void ParseList(ref HevcBitReader reader, HevcSequenceParameterSet sps, int list, int numRefIdxActiveMinus1)
    {
        // Without the screen content coding extensions, no reference picture is the current picture: every reference
        // picture has its flags
        Span<bool> lumaWeightFlag = stackalloc bool[MaxRefIdx];
        Span<bool> chromaWeightFlag = stackalloc bool[MaxRefIdx];
        for (var i = 0; i <= numRefIdxActiveMinus1; i++)
        {
            lumaWeightFlag[i] = reader.ReadFlag();
        }

        if (sps.ChromaArrayType is not 0)
        {
            for (var i = 0; i <= numRefIdxActiveMinus1; i++)
            {
                chromaWeightFlag[i] = reader.ReadFlag();
            }
        }

        // WpOffsetHalfRangeY and WpOffsetHalfRangeC (Equations 7-57 and 7-58): both bit depths are the same
        var wpOffsetHalfRange = 1 << (sps.HighPrecisionOffsetsEnabledFlag ? sps.BitDepth - 1 : 7);
        for (var i = 0; i <= numRefIdxActiveMinus1; i++)
        {
            var index = (list * MaxRefIdx) + i;
            _lumaWeight[index] = 1 << LumaLog2WeightDenom;
            if (lumaWeightFlag[i])
            {
                _lumaWeight[index] += reader.ReadSE(-128, 127, "delta_luma_weight");
                _lumaOffset[index] = reader.ReadSE(-wpOffsetHalfRange, wpOffsetHalfRange - 1, "luma_offset");
            }

            for (var j = 0; j < 2; j++)
            {
                _chromaWeight[(index * 2) + j] = 1 << ChromaLog2WeightDenom;
                if (!chromaWeightFlag[i])
                    continue;

                var chromaWeight = (1 << ChromaLog2WeightDenom) + reader.ReadSE(-128, 127, "delta_chroma_weight");
                var deltaChromaOffset = reader.ReadSE(-4 * wpOffsetHalfRange, (4 * wpOffsetHalfRange) - 1, "delta_chroma_offset");
                _chromaWeight[(index * 2) + j] = chromaWeight;
                _chromaOffset[(index * 2) + j] = Math.Clamp(wpOffsetHalfRange + deltaChromaOffset - ((wpOffsetHalfRange * chromaWeight) >> ChromaLog2WeightDenom), -wpOffsetHalfRange, wpOffsetHalfRange - 1);
            }
        }
    }
}
