namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The block-level state of the AV1 frame being decoded: the arrays that the specification indexes by the position of a 4x4
/// luma block (<c>MiSizes</c>, <c>YModes</c>, <c>Skips</c>, ...) and the parameters of the post-processing filters that
/// are read with the blocks (<c>cdef_idx</c> and the loop restoration units). The buffers are rented once and reused by the
/// frames of the same geometry.
/// </summary>
/// <remarks>
/// Only the arrays that are read at positions other than the block above and the block to the left are stored for the whole
/// frame: the above context of the tiles is in their scratch (<see cref="Av1TileScratch.PrepareAboveContext"/>). A tile
/// writes the arrays at the positions of its own blocks only, and reads other positions only inside the tile.
/// </remarks>
internal sealed class Av1FrameState : IDisposable
{
    /// <summary>The <c>Skips</c> flag of <see cref="Flags"/>.</summary>
    public const byte SkipFlag = 1;

    /// <summary>The <c>IsInters</c> flag of <see cref="Flags"/>.</summary>
    public const byte IsInterFlag = 2;

    /// <summary>The <c>SkipModes</c> flag of <see cref="Flags"/>.</summary>
    public const byte SkipModeFlag = 4;

    /// <summary>The bit of <see cref="InterInfo"/> that holds <c>CompGroupIdxs</c>.</summary>
    public const int CompGroupIdxShift = 4;

    /// <summary>The bit of <see cref="InterInfo"/> that holds <c>CompoundIdxs</c>.</summary>
    public const int CompoundIdxShift = 5;

    /// <summary>The value of <see cref="RefFrames"/> for a block that is not decoded yet.</summary>
    public const sbyte RefFrameNotWritten = sbyte.MinValue;

    /// <summary>The number of values stored for a loop restoration unit: its type, 6 Wiener coefficients, the self-guided set and its 2 weights.</summary>
    public const int LoopRestorationUnitLength = 10;

    private readonly Av1PooledArray<byte>?[] _loopFilterTxSizes = new Av1PooledArray<byte>?[3];
    private readonly Av1PooledArray<sbyte>?[] _loopRestorationUnits = new Av1PooledArray<sbyte>?[3];
    private readonly int[] _loopRestorationUnitRows = new int[3];
    private readonly int[] _loopRestorationUnitCols = new int[3];
    private Av1PooledArray<byte>? _miSizes;
    private Av1PooledArray<byte>? _yModes;
    private Av1PooledArray<byte>? _uvModes;
    private Av1PooledArray<byte>? _flags;
    private Av1PooledArray<byte>? _paletteSizes;
    private Av1PooledArray<byte>? _segmentIds;
    private Av1PooledArray<byte>? _interTxSizes;
    private Av1PooledArray<sbyte>? _deltaLfs;
    private Av1PooledArray<sbyte>? _refFrames;
    private Av1PooledArray<short>? _mvs;
    private Av1PooledArray<byte>? _interInfo;
    private Av1PooledArray<short>? _motionFieldMvs;
    private Av1FrameBuffer? _segmentationSource;
    private Av1PooledArray<sbyte>? _cdefIdx;

    /// <summary>Gets the number of 4x4 luma columns of the frame (<c>MiCols</c>).</summary>
    public int MiCols { get; private set; }

    /// <summary>Gets the number of 4x4 luma rows of the frame (<c>MiRows</c>).</summary>
    public int MiRows { get; private set; }

    /// <summary>Gets the number of 64x64 luma columns of the frame: the stride of <see cref="CdefIdx"/>.</summary>
    public int CdefCols { get; private set; }

    /// <summary>Gets <c>MiSizes</c>: one entry per 4x4 luma block, <see cref="MiCols"/> entries per row.</summary>
    public Span<byte> MiSizes => Get(_miSizes);

    /// <summary>Gets <c>YModes</c>.</summary>
    public Span<byte> YModes => Get(_yModes);

    /// <summary>Gets <c>UVModes</c>.</summary>
    public Span<byte> UvModes => Get(_uvModes);

    /// <summary>Gets the <c>Skips</c>, <c>IsInters</c> and <c>SkipModes</c> flags of each 4x4 luma block.</summary>
    public Span<byte> Flags => Get(_flags);

    /// <summary>Gets <c>PaletteSizes</c>: the luma palette size in the low 4 bits, the chroma palette size in the high 4 bits.</summary>
    public Span<byte> PaletteSizes => Get(_paletteSizes);

    /// <summary>Gets <c>SegmentIds</c>.</summary>
    public Span<byte> SegmentIds => Get(_segmentIds);

    /// <summary>Gets <c>InterTxSizes</c>.</summary>
    public Span<byte> InterTxSizes => Get(_interTxSizes);

    /// <summary>Gets <c>DeltaLFs</c>: 4 entries per 4x4 luma block. Only allocated for a frame with <c>delta_lf_present</c>.</summary>
    public Span<sbyte> DeltaLfs => Get(_deltaLfs);

    /// <summary>
    /// Gets <c>RefFrames</c>: 2 entries per 4x4 luma block. Only allocated for a frame that is not intra or that allows intra
    /// block copy.
    /// </summary>
    public Span<sbyte> RefFrames => Get(_refFrames);

    /// <summary>
    /// Gets <c>Mvs</c>: for each 4x4 luma block, the row and the column of the motion vector of the first reference list, then
    /// of the second one. Allocated with <see cref="RefFrames"/>.
    /// </summary>
    public Span<short> Mvs => Get(_mvs);

    /// <summary>
    /// Gets <c>InterpFilters</c> (2 bits for each direction, from the low bit), <c>CompGroupIdxs</c> and <c>CompoundIdxs</c> of
    /// each 4x4 luma block. Allocated with <see cref="RefFrames"/>.
    /// </summary>
    public Span<byte> InterInfo => Get(_interInfo);

    /// <summary>
    /// Gets <c>MotionFieldMvs</c>: for each reference frame type from <c>LAST_FRAME</c>, the row and the column of the motion
    /// vector of each 8x8 luma block. Only allocated for a frame with <c>use_ref_frame_mvs</c>.
    /// </summary>
    public Span<short> MotionFieldMvs => Get(_motionFieldMvs);

    /// <summary>
    /// Gets <c>PrevSegmentIds</c>: the segmentation map that the frame predicts its own from, or an empty span when every
    /// segment of this map is 0.
    /// </summary>
    public ReadOnlySpan<byte> PrevSegmentIds => _segmentationSource is null ? default : _segmentationSource.SegmentIds;

    /// <summary>Gets <c>cdef_idx</c>: one entry per 64x64 luma block, <see cref="CdefCols"/> entries per row; -1 when not coded.</summary>
    public Span<sbyte> CdefIdx => Get(_cdefIdx);

    /// <summary>Gets <c>LoopfilterTxSizes[ plane ]</c>: one entry per 4x4 block of the plane, <see cref="MiCols"/> entries per row.</summary>
    public Span<byte> GetLoopFilterTxSizes(int plane) => Get(_loopFilterTxSizes[plane]);

    /// <summary>
    /// Gets the loop restoration units of a plane: <see cref="LoopRestorationUnitLength"/> values per unit
    /// (<c>LrType</c>, <c>LrWiener</c>, <c>LrSgrSet</c>, <c>LrSgrXqd</c>), <see cref="GetLoopRestorationUnitCols"/> units per row.
    /// </summary>
    public Span<sbyte> GetLoopRestorationUnits(int plane) => Get(_loopRestorationUnits[plane]);

    public int GetLoopRestorationUnitRows(int plane) => _loopRestorationUnitRows[plane];

    public int GetLoopRestorationUnitCols(int plane) => _loopRestorationUnitCols[plane];

    /// <summary>
    /// Prepares the state for a frame: rents the buffers when the geometry of the frame differs from the previous one, and
    /// resets the values that a frame does not always write.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="sequence">The sequence header.</param>
    /// <param name="header">The header of the frame.</param>
    /// <param name="segmentationSource">The frame whose segmentation map is <c>PrevSegmentIds</c>, or <see langword="null"/>.</param>
    public void Prepare(AllocationScope scope, Av1SequenceHeader sequence, Av1FrameHeader header, Av1FrameBuffer? segmentationSource)
    {
        var cdefCols = (header.MiCols + 15) >> 4;
        var cdefRows = (header.MiRows + 15) >> 4;
        if (header.MiCols != MiCols || header.MiRows != MiRows || _miSizes is null)
        {
            Dispose();
            MiCols = header.MiCols;
            MiRows = header.MiRows;
            CdefCols = cdefCols;
            var count = (long)MiCols * MiRows;
            _miSizes = Rent<byte>(scope, count);
            _yModes = Rent<byte>(scope, count);
            _uvModes = Rent<byte>(scope, count);
            _flags = Rent<byte>(scope, count);
            _paletteSizes = Rent<byte>(scope, count);
            _segmentIds = Rent<byte>(scope, count);
            _interTxSizes = Rent<byte>(scope, count);
            _cdefIdx = Rent<sbyte>(scope, (long)cdefCols * cdefRows);
            for (var plane = 0; plane < 3; plane++)
            {
                _loopFilterTxSizes[plane] = Rent<byte>(scope, count);
            }
        }

        _segmentationSource = segmentationSource;
        if (!header.FrameIsIntra || header.AllowIntrabc)
        {
            _refFrames ??= Rent<sbyte>(scope, 2L * MiCols * MiRows);
            _mvs ??= Rent<short>(scope, 4L * MiCols * MiRows);
            _interInfo ??= Rent<byte>(scope, (long)MiCols * MiRows);
            RefFrames.Fill(RefFrameNotWritten);
        }

        if (header.UseRefFrameMvs)
            _motionFieldMvs ??= Rent<short>(scope, 2L * Av1Constants.RefsPerFrame * (MiCols >> 1) * (MiRows >> 1));

        if (header.DeltaLfPresent && _deltaLfs is null)
            _deltaLfs = Rent<sbyte>(scope, (long)MiCols * MiRows * Av1Constants.FrameLfCount);

        // The segment of a block that is not coded is segment 0, and a 64x64 block without coded block has no CDEF index
        SegmentIds.Clear();
        CdefIdx.Fill(-1);

        for (var plane = 0; plane < sequence.PlaneCount; plane++)
        {
            if (header.FrameRestorationType[plane] is Av1Constants.RestoreNone)
                continue;

            var subX = plane > 0 ? sequence.SubsamplingX : 0;
            var subY = plane > 0 ? sequence.SubsamplingY : 0;
            var unitSize = header.LoopRestorationSize[plane];
            var unitRows = CountUnitsInFrame(unitSize, (header.FrameHeight + subY) >> subY);
            var unitCols = CountUnitsInFrame(unitSize, (header.UpscaledWidth + subX) >> subX);
            var length = (long)unitRows * unitCols * LoopRestorationUnitLength;
            if (_loopRestorationUnits[plane] is not { } units || units.Length < length)
            {
                _loopRestorationUnits[plane]?.Dispose();
                _loopRestorationUnits[plane] = null;
                _loopRestorationUnits[plane] = Rent<sbyte>(scope, length);
            }

            _loopRestorationUnitRows[plane] = unitRows;
            _loopRestorationUnitCols[plane] = unitCols;
            GetLoopRestorationUnits(plane).Clear();
        }
    }

    /// <summary>Returns <c>count_units_in_frame( unitSize, frameSize )</c> (section 5.11.57).</summary>
    public static int CountUnitsInFrame(int unitSize, int frameSize) => Math.Max((frameSize + (unitSize >> 1)) / unitSize, 1);

    public void Dispose()
    {
        _miSizes?.Dispose();
        _yModes?.Dispose();
        _uvModes?.Dispose();
        _flags?.Dispose();
        _paletteSizes?.Dispose();
        _segmentIds?.Dispose();
        _interTxSizes?.Dispose();
        _deltaLfs?.Dispose();
        _refFrames?.Dispose();
        _mvs?.Dispose();
        _interInfo?.Dispose();
        _motionFieldMvs?.Dispose();
        _mvs = _motionFieldMvs = null;
        _interInfo = null;
        _segmentationSource = null;
        _cdefIdx?.Dispose();
        _miSizes = _yModes = _uvModes = _flags = _paletteSizes = _segmentIds = _interTxSizes = null;
        _deltaLfs = _cdefIdx = _refFrames = null;
        for (var plane = 0; plane < 3; plane++)
        {
            _loopFilterTxSizes[plane]?.Dispose();
            _loopRestorationUnits[plane]?.Dispose();
            _loopFilterTxSizes[plane] = null;
            _loopRestorationUnits[plane] = null;
        }

        MiCols = MiRows = CdefCols = 0;
    }

    private static Av1PooledArray<T> Rent<T>(AllocationScope scope, long length)
        where T : unmanaged
    {
        return Av1PooledArray<T>.Rent(scope, length);
    }

    private static Span<T> Get<T>(Av1PooledArray<T>? array)
        where T : unmanaged
    {
        return (array ?? throw new ObjectDisposedException(nameof(Av1FrameState))).Span;
    }
}
