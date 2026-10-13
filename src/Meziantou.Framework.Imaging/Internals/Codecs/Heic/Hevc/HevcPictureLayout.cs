namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What a sequence parameter set and a picture parameter set define together for the pictures that refer to them: the
/// constraints of one on the other, the variables derived from both, the tiles and the conversions between the scan orders
/// of the coding tree blocks (Rec. ITU-T H.265, clauses 6.5.1 "CTB raster and tile scanning conversion process" and 6.5.2
/// "Z-scan order array initialization process"), and the scaling factors.
/// </summary>
internal sealed class HevcPictureLayout : IDisposable
{
    private readonly int[] _colBd;
    private readonly int[] _rowBd;
    private Av1PooledArray<int>? _ctbAddrRsToTs;
    private Av1PooledArray<int>? _ctbAddrTsToRs;
    private Av1PooledArray<int>? _tileId;
    private Av1PooledArray<byte>? _scalingFactors;

    private HevcPictureLayout(HevcSequenceParameterSet sps, HevcPictureParameterSet pps)
    {
        Sps = sps;
        Pps = pps;
        _colBd = new int[pps.NumTileColumns + 1];
        _rowBd = new int[pps.NumTileRows + 1];
    }

    public HevcSequenceParameterSet Sps { get; }

    public HevcPictureParameterSet Pps { get; }

    /// <summary>Gets <c>Log2MinCuQpDeltaSize</c> (Equation 7-36).</summary>
    public int Log2MinCuQpDeltaSize => Sps.CtbLog2SizeY - Pps.DiffCuQpDeltaDepth;

    /// <summary>Gets <c>Log2MinCuChromaQpOffsetSize</c> (Equation 7-39).</summary>
    public int Log2MinCuChromaQpOffsetSize => Sps.CtbLog2SizeY - Pps.DiffCuChromaQpOffsetDepth;

    /// <summary>Gets <c>colBd[ i ]</c>: the first coding tree block column of each tile column, then the width of the picture.</summary>
    public ReadOnlySpan<int> ColBd => _colBd;

    /// <summary>Gets <c>rowBd[ j ]</c>: the first coding tree block row of each tile row, then the height of the picture.</summary>
    public ReadOnlySpan<int> RowBd => _rowBd;

    /// <summary>Gets <c>CtbAddrRsToTs[ ctbAddrRs ]</c>.</summary>
    public ReadOnlySpan<int> CtbAddrRsToTs => (_ctbAddrRsToTs ?? throw new ObjectDisposedException(nameof(HevcPictureLayout))).Span;

    /// <summary>Gets <c>CtbAddrTsToRs[ ctbAddrTs ]</c>.</summary>
    public ReadOnlySpan<int> CtbAddrTsToRs => (_ctbAddrTsToRs ?? throw new ObjectDisposedException(nameof(HevcPictureLayout))).Span;

    /// <summary>Gets <c>TileId[ ctbAddrTs ]</c>.</summary>
    public ReadOnlySpan<int> TileId => (_tileId ?? throw new ObjectDisposedException(nameof(HevcPictureLayout))).Span;

    /// <summary>
    /// Gets <c>ScalingFactor</c> in the layout of <see cref="HevcScalingList.WriteScalingFactors"/>, or an empty span when
    /// <c>scaling_list_enabled_flag</c> is 0.
    /// </summary>
    public ReadOnlySpan<byte> ScalingFactors => _scalingFactors is null ? default : _scalingFactors.Span;

    /// <summary>
    /// Gets <c>MinTbAddrZs[ x ][ y ]</c> (Equation 6-10): the address in z-scan order of the minimum transform block at
    /// the location <c>( x, y )</c>, in units of minimum transform blocks.
    /// </summary>
    public int GetMinTbAddrZs(int x, int y)
    {
        var shift = Sps.CtbLog2SizeY - Sps.MinTbLog2SizeY;
        var ctbAddrRs = (Sps.PicWidthInCtbsY * (y >> shift)) + (x >> shift);
        var mask = (1 << shift) - 1;
        return (CtbAddrRsToTs[ctbAddrRs] << (shift * 2)) + Interleave(x & mask) + (Interleave(y & mask) << 1);
    }

    /// <summary>
    /// Checks the constraints of the two parameter sets on each other, and derives the layout of the pictures that refer
    /// to them.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The picture parameter set does not fit the sequence parameter set.</exception>
    /// <exception cref="ImageResourceLimitException">The tables exceed the live-allocation limit.</exception>
    public static HevcPictureLayout Create(AllocationScope scope, HevcSequenceParameterSet sps, HevcPictureParameterSet pps)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Validate(sps, pps);
        var layout = new HevcPictureLayout(sps, pps);
        try
        {
            layout.DeriveTiles(scope);
            if (sps.ScalingListEnabledFlag)
            {
                // Clause 7.4.3.3.1: the lists of the picture parameter set, otherwise those of the sequence parameter set,
                // otherwise the default ones
                var lists = pps.ScalingList ?? sps.ScalingList ?? HevcScalingList.Default;
                layout._scalingFactors = Av1PooledArray<byte>.Rent(scope, HevcScalingList.ScalingFactorLength, clear: true);
                lists.WriteScalingFactors(layout._scalingFactors.Span, sps.ChromaArrayType);
            }
        }
        catch
        {
            layout.Dispose();
            throw;
        }

        return layout;
    }

    private static void Validate(HevcSequenceParameterSet sps, HevcPictureParameterSet pps)
    {
        if (pps.InitQpMinus26 < -(26 + sps.QpBdOffset))
            throw HevcErrors.Invalid("init_qp_minus26 is out of range.");

        if (pps.DiffCuQpDeltaDepth > sps.CtbLog2SizeY - sps.MinCbLog2SizeY)
            throw HevcErrors.Invalid("diff_cu_qp_delta_depth is out of range.");

        if (pps.DiffCuChromaQpOffsetDepth > sps.CtbLog2SizeY - sps.MinCbLog2SizeY)
            throw HevcErrors.Invalid("diff_cu_chroma_qp_offset_depth is out of range.");

        if (pps.Log2ParMrgLevel > sps.CtbLog2SizeY)
            throw HevcErrors.Invalid("log2_parallel_merge_level_minus2 is out of range.");

        if (pps.Log2MaxTransformSkipSize > sps.MaxTbLog2SizeY)
            throw HevcErrors.Invalid("log2_max_transform_skip_block_size_minus2 is out of range.");

        if (pps.ScalingList is not null && !sps.ScalingListEnabledFlag)
            throw HevcErrors.Invalid("a picture parameter set has scaling lists for a sequence that does not use them.");

        if (pps.CrossComponentPredictionEnabledFlag && sps.ChromaArrayType is not 3)
            throw HevcErrors.Invalid("cross_component_prediction_enabled_flag is set for a chroma format other than 4:4:4.");

        if (pps.ChromaQpOffsetListEnabledFlag && sps.ChromaArrayType is 0)
            throw HevcErrors.Invalid("chroma_qp_offset_list_enabled_flag is set for a monochrome sequence.");

        var maxLog2SaoOffsetScale = Math.Max(0, sps.BitDepth - 10);
        if (pps.Log2SaoOffsetScaleLuma > maxLog2SaoOffsetScale || pps.Log2SaoOffsetScaleChroma > maxLog2SaoOffsetScale)
            throw HevcErrors.Invalid("the scale of the sample adaptive offsets is out of range.");

        if (pps.NumTileColumns > sps.PicWidthInCtbsY || pps.NumTileRows > sps.PicHeightInCtbsY)
            throw HevcErrors.Invalid("a picture has more tiles than coding tree blocks.");
    }

    // Clause 6.5.1 "CTB raster and tile scanning conversion process"
    private void DeriveTiles(AllocationScope scope)
    {
        var picWidthInCtbsY = Sps.PicWidthInCtbsY;
        var picHeightInCtbsY = Sps.PicHeightInCtbsY;
        Span<int> colWidth = stackalloc int[Pps.NumTileColumns];
        Span<int> rowHeight = stackalloc int[Pps.NumTileRows];

        // Equations 6-3 and 6-4
        if (Pps.UniformSpacingFlag)
        {
            for (var i = 0; i < colWidth.Length; i++)
            {
                colWidth[i] = (((i + 1) * picWidthInCtbsY) / colWidth.Length) - ((i * picWidthInCtbsY) / colWidth.Length);
            }

            for (var j = 0; j < rowHeight.Length; j++)
            {
                rowHeight[j] = (((j + 1) * picHeightInCtbsY) / rowHeight.Length) - ((j * picHeightInCtbsY) / rowHeight.Length);
            }
        }
        else
        {
            colWidth[^1] = picWidthInCtbsY;
            for (var i = 0; i < colWidth.Length - 1; i++)
            {
                colWidth[i] = Pps.ColumnWidthMinus1[i] + 1;
                colWidth[^1] -= colWidth[i];
            }

            rowHeight[^1] = picHeightInCtbsY;
            for (var j = 0; j < rowHeight.Length - 1; j++)
            {
                rowHeight[j] = Pps.RowHeightMinus1[j] + 1;
                rowHeight[^1] -= rowHeight[j];
            }

            if (colWidth[^1] <= 0 || rowHeight[^1] <= 0)
                throw HevcErrors.Invalid("the tiles are larger than the picture.");
        }

        // Equations 6-5 and 6-6
        for (var i = 0; i < colWidth.Length; i++)
        {
            _colBd[i + 1] = _colBd[i] + colWidth[i];
        }

        for (var j = 0; j < rowHeight.Length; j++)
        {
            _rowBd[j + 1] = _rowBd[j] + rowHeight[j];
        }

        var picSizeInCtbsY = (long)picWidthInCtbsY * picHeightInCtbsY;
        _ctbAddrRsToTs = Av1PooledArray<int>.Rent(scope, picSizeInCtbsY, clear: false);
        _ctbAddrTsToRs = Av1PooledArray<int>.Rent(scope, picSizeInCtbsY, clear: false);
        _tileId = Av1PooledArray<int>.Rent(scope, picSizeInCtbsY, clear: false);
        var ctbAddrRsToTs = _ctbAddrRsToTs.Span;
        var ctbAddrTsToRs = _ctbAddrTsToRs.Span;
        var tileId = _tileId.Span;

        // Equations 6-7 to 6-9, one tile at a time: the coding tree blocks of a tile follow each other in tile scan
        var ctbAddrTs = 0;
        var tileIdx = 0;
        for (var j = 0; j < rowHeight.Length; j++)
        {
            for (var i = 0; i < colWidth.Length; i++, tileIdx++)
            {
                for (var y = _rowBd[j]; y < _rowBd[j + 1]; y++)
                {
                    for (var x = _colBd[i]; x < _colBd[i + 1]; x++)
                    {
                        var ctbAddrRs = (y * picWidthInCtbsY) + x;
                        ctbAddrRsToTs[ctbAddrRs] = ctbAddrTs;
                        ctbAddrTsToRs[ctbAddrTs] = ctbAddrRs;
                        tileId[ctbAddrTs] = tileIdx;
                        ctbAddrTs++;
                    }
                }
            }
        }
    }

    // The bits of a value of at most 4 bits, each one followed by a zero bit
    private static int Interleave(int value)
    {
        value = (value | (value << 2)) & 0x33;
        return (value | (value << 1)) & 0x55;
    }

    public void Dispose()
    {
        _ctbAddrRsToTs?.Dispose();
        _ctbAddrRsToTs = null;
        _ctbAddrTsToRs?.Dispose();
        _ctbAddrTsToRs = null;
        _tileId?.Dispose();
        _tileId = null;
        _scalingFactors?.Dispose();
        _scalingFactors = null;
    }
}
