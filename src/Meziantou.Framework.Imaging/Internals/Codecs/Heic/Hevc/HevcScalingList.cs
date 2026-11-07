namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The scaling lists of a sequence or picture parameter set (Rec. ITU-T H.265, clauses 7.3.4 "Scaling list data syntax"
/// and 7.4.5 "Scaling list data semantics"): <c>ScalingList[ sizeId ][ matrixId ][ i ]</c> in the order of the up-right
/// diagonal scan, and the values of the first coefficient of the 16x16 and 32x32 lists.
/// </summary>
internal sealed class HevcScalingList
{
    /// <summary>The number of values of <see cref="WriteScalingFactors"/>: 6 matrices of 4x4, 8x8, 16x16 and 32x32 values.</summary>
    public const int ScalingFactorLength = 6 * (16 + 64 + 256 + 1024);

    // 6 lists of 16 values, then 6 lists of 64 values for each of the three other sizes
    private readonly byte[] _lists = new byte[(6 * 16) + (3 * 6 * 64)];

    // scaling_list_dc_coef_minus8[ sizeId - 2 ][ matrixId ] + 8
    private readonly byte[] _dc = new byte[2 * 6];

    private HevcScalingList()
    {
    }

    /// <summary>Gets the lists that are used when no list is in the bitstream (Tables 7-5 and 7-6).</summary>
    public static HevcScalingList Default { get; } = CreateDefault();

    /// <summary>Gets <c>ScalingList[ sizeId ][ matrixId ]</c>: 16 values when <paramref name="sizeId"/> is 0, otherwise 64.</summary>
    public ReadOnlySpan<byte> GetList(int sizeId, int matrixId) => GetWritableList(sizeId, matrixId);

    /// <summary>Gets the first coefficient of the list <paramref name="matrixId"/> of the 16x16 (<paramref name="sizeId"/> 2) or 32x32 (3) size.</summary>
    public int GetDc(int sizeId, int matrixId) => _dc[((sizeId - 2) * 6) + matrixId];

    /// <summary>Gets the position in the values of <see cref="WriteScalingFactors"/> of the matrix of a size and of a list.</summary>
    public static int GetScalingFactorOffset(int sizeId, int matrixId)
    {
        var sizeOffset = sizeId switch
        {
            0 => 0,
            1 => 6 * 16,
            2 => 6 * (16 + 64),
            _ => 6 * (16 + 64 + 256),
        };
        return sizeOffset + (matrixId << (4 + (sizeId << 1)));
    }

    /// <summary>
    /// Derives <c>ScalingFactor[ sizeId ][ matrixId ][ x ][ y ]</c> (Equations 7-44 to 7-51) into
    /// <paramref name="scalingFactors"/>: for each size and each list, the rows of the matrix, the factor of the position
    /// <c>( x, y )</c> being the value <c>x</c> of the row <c>y</c>.
    /// </summary>
    /// <param name="scalingFactors"><see cref="ScalingFactorLength"/> values.</param>
    /// <param name="chromaArrayType"><c>ChromaArrayType</c>: the 32x32 chroma matrices only exist when it is 3.</param>
    public void WriteScalingFactors(Span<byte> scalingFactors, int chromaArrayType)
    {
        var scan4 = HevcScanOrder.Get(2, HevcScanOrder.Diagonal);
        var scan8 = HevcScanOrder.Get(3, HevcScanOrder.Diagonal);
        for (var matrixId = 0; matrixId < 6; matrixId++)
        {
            // Equation 7-44
            var list = GetList(0, matrixId);
            var factors = scalingFactors.Slice(GetScalingFactorOffset(0, matrixId), 16);
            for (var i = 0; i < 16; i++)
            {
                factors[(HevcScanOrder.Y(scan4[i]) * 4) + HevcScanOrder.X(scan4[i])] = list[i];
            }

            // Equations 7-45 to 7-49
            for (var sizeId = 1; sizeId <= 3; sizeId++)
            {
                if (sizeId is 3 && matrixId % 3 is not 0)
                {
                    if (chromaArrayType is not 3)
                        continue;

                    // Equations 7-50 and 7-51: the 32x32 chroma matrices are those of the 16x16 lists
                    Upsample(GetList(2, matrixId), GetDc(2, matrixId), scalingFactors.Slice(GetScalingFactorOffset(3, matrixId), 1024), 3, scan8);
                    continue;
                }

                var size = 4 << sizeId;
                Upsample(GetList(sizeId, matrixId), sizeId > 1 ? GetDc(sizeId, matrixId) : -1, scalingFactors.Slice(GetScalingFactorOffset(sizeId, matrixId), size * size), sizeId, scan8);
            }
        }
    }

    private static void Upsample(ReadOnlySpan<byte> list, int dc, Span<byte> factors, int sizeId, ReadOnlySpan<byte> scan8)
    {
        var shift = sizeId - 1;
        var size = 8 << shift;
        var ratio = 1 << shift;
        for (var i = 0; i < 64; i++)
        {
            var x = HevcScanOrder.X(scan8[i]) << shift;
            var y = HevcScanOrder.Y(scan8[i]) << shift;
            for (var j = 0; j < ratio; j++)
            {
                factors.Slice(((y + j) * size) + x, ratio).Fill(list[i]);
            }
        }

        if (dc >= 0)
        {
            factors[0] = (byte)dc;
        }
    }

    /// <summary>Reads the syntax structure <c>scaling_list_data( )</c>.</summary>
    /// <exception cref="InvalidImageContentException">A value is out of range, or the data are truncated.</exception>
    public static HevcScalingList Parse(ref HevcBitReader reader)
    {
        var result = new HevcScalingList();
        for (var sizeId = 0; sizeId < 4; sizeId++)
        {
            var coefNum = Math.Min(64, 1 << (4 + (sizeId << 1)));
            for (var matrixId = 0; matrixId < 6; matrixId += sizeId is 3 ? 3 : 1)
            {
                var list = result.GetWritableList(sizeId, matrixId);
                var scalingListPredModeFlag = reader.ReadFlag();
                if (!scalingListPredModeFlag)
                {
                    var delta = reader.ReadUE(sizeId is 3 ? matrixId / 3 : matrixId, "scaling_list_pred_matrix_id_delta");
                    if (delta is 0)
                    {
                        Default.GetList(sizeId, matrixId).CopyTo(list);
                        if (sizeId > 1)
                        {
                            result._dc[((sizeId - 2) * 6) + matrixId] = 16;
                        }
                    }
                    else
                    {
                        // Equation 7-42
                        var refMatrixId = matrixId - (delta * (sizeId is 3 ? 3 : 1));
                        result.GetList(sizeId, refMatrixId).CopyTo(list);
                        if (sizeId > 1)
                        {
                            result._dc[((sizeId - 2) * 6) + matrixId] = result._dc[((sizeId - 2) * 6) + refMatrixId];
                        }
                    }

                    continue;
                }

                var nextCoef = 8;
                if (sizeId > 1)
                {
                    var scalingListDcCoefMinus8 = reader.ReadSE(-7, 247, "scaling_list_dc_coef_minus8");
                    nextCoef = scalingListDcCoefMinus8 + 8;
                    result._dc[((sizeId - 2) * 6) + matrixId] = (byte)nextCoef;
                }

                for (var i = 0; i < coefNum; i++)
                {
                    var scalingListDeltaCoef = reader.ReadSE(-128, 127, "scaling_list_delta_coef");
                    nextCoef = (nextCoef + scalingListDeltaCoef + 256) % 256;
                    if (nextCoef is 0)
                        throw HevcErrors.Invalid("a scaling list has a value equal to 0.");

                    list[i] = (byte)nextCoef;
                }
            }
        }

        return result;
    }

    private Span<byte> GetWritableList(int sizeId, int matrixId)
    {
        return sizeId is 0
            ? _lists.AsSpan(matrixId * 16, 16)
            : _lists.AsSpan((6 * 16) + ((((sizeId - 1) * 6) + matrixId) * 64), 64);
    }

    private static HevcScalingList CreateDefault()
    {
        var result = new HevcScalingList();
        result._lists.AsSpan(0, 6 * 16).Fill(16);
        for (var sizeId = 1; sizeId <= 3; sizeId++)
        {
            for (var matrixId = 0; matrixId < 6; matrixId++)
            {
                var values = matrixId < 3 ? HevcQuantizationTables.DefaultScalingListIntra : HevcQuantizationTables.DefaultScalingListInter;
                values.CopyTo(result.GetWritableList(sizeId, matrixId));
            }
        }

        result._dc.AsSpan().Fill(16);
        return result;
    }
}
