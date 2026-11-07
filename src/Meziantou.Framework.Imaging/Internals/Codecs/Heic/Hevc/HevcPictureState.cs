namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The state of the picture being decoded that is not its samples: what the syntax elements and the decoding processes of
/// a block read of the blocks decoded before it, and what the in-loop filters read of every block (Rec. ITU-T H.265,
/// clauses 7.4.9, 8.4, 8.6.1 and 8.7). The arrays are rented for the size of the pictures of a sequence and are used by one
/// picture after the other.
/// </summary>
internal sealed class HevcPictureState : IDisposable
{
    /// <summary>The bit of <see cref="CuFlags"/> of a coding unit whose <c>CuPredMode</c> is <c>MODE_INTRA</c>.</summary>
    public const byte CuIntra = 1;

    /// <summary>The bit of <see cref="CuFlags"/> of a coding unit whose <c>cu_skip_flag</c> is 1.</summary>
    public const byte CuSkip = 2;

    /// <summary>The bit of <see cref="CuFlags"/> of a coding unit whose <c>pcm_flag</c> is 1.</summary>
    public const byte CuPcm = 4;

    /// <summary>The bit of <see cref="CuFlags"/> of a coding unit whose <c>cu_transquant_bypass_flag</c> is 1.</summary>
    public const byte CuTransquantBypass = 8;

    /// <summary>The position in <see cref="CuFlags"/> of the two bits of <c>CtDepth</c>.</summary>
    public const int CuDepthShift = 4;

    /// <summary>The bit of <see cref="EdgeFlags"/> of a 4x4 block whose left edge is an edge of a transform block.</summary>
    public const byte EdgeLeftTransform = 1;

    /// <summary>The bit of <see cref="EdgeFlags"/> of a 4x4 block whose left edge is an edge of a prediction block.</summary>
    public const byte EdgeLeftPrediction = 2;

    /// <summary>The bit of <see cref="EdgeFlags"/> of a 4x4 block whose top edge is an edge of a transform block.</summary>
    public const byte EdgeTopTransform = 4;

    /// <summary>The bit of <see cref="EdgeFlags"/> of a 4x4 block whose top edge is an edge of a prediction block.</summary>
    public const byte EdgeTopPrediction = 8;

    /// <summary>The bit of <see cref="EdgeFlags"/> of a 4x4 block of a luma transform block that has a transform coefficient level that is not zero.</summary>
    public const byte CodedLuma = 16;

    /// <summary>The number of values of a coding tree block in <see cref="Sao"/>: 6 per colour component.</summary>
    public const int SaoValuesPerCtb = 18;

    private readonly Av1PooledArray<byte> _cuFlags;
    private readonly Av1PooledArray<sbyte> _qpY;
    private readonly Av1PooledArray<byte> _intraPredModeY;
    private readonly Av1PooledArray<byte> _edgeFlags;
    private readonly Av1PooledArray<int> _sliceIndex;
    private readonly Av1PooledArray<sbyte> _sao;
    private readonly AllocationScope _scope;
    private Av1PooledArray<HevcMotion>? _motion;

    private HevcPictureState(AllocationScope scope, HevcSequenceParameterSet sps, Av1PooledArray<byte> cuFlags, Av1PooledArray<sbyte> qpY, Av1PooledArray<byte> intraPredModeY, Av1PooledArray<byte> edgeFlags, Av1PooledArray<int> sliceIndex, Av1PooledArray<sbyte> sao)
    {
        MinCbLog2SizeY = sps.MinCbLog2SizeY;
        WidthInMinCbs = sps.PicWidthInCtbsY << (sps.CtbLog2SizeY - sps.MinCbLog2SizeY);
        HeightInMinCbs = sps.PicHeightInCtbsY << (sps.CtbLog2SizeY - sps.MinCbLog2SizeY);
        WidthIn4x4 = sps.PicWidthInCtbsY << (sps.CtbLog2SizeY - 2);
        HeightIn4x4 = sps.PicHeightInCtbsY << (sps.CtbLog2SizeY - 2);
        _scope = scope;
        _cuFlags = cuFlags;
        _qpY = qpY;
        _intraPredModeY = intraPredModeY;
        _edgeFlags = edgeFlags;
        _sliceIndex = sliceIndex;
        _sao = sao;
    }

    public int MinCbLog2SizeY { get; }

    /// <summary>Gets the number of minimum coding blocks of a row of <see cref="CuFlags"/> and <see cref="QpY"/>.</summary>
    public int WidthInMinCbs { get; }

    public int HeightInMinCbs { get; }

    /// <summary>Gets the number of 4x4 blocks of a row of <see cref="IntraPredModeY"/> and <see cref="EdgeFlags"/>.</summary>
    public int WidthIn4x4 { get; }

    public int HeightIn4x4 { get; }

    /// <summary>Gets the prediction mode, the flags and the depth of the coding unit of each minimum coding block.</summary>
    public Span<byte> CuFlags => _cuFlags.Span;

    /// <summary>Gets <c>QpY</c> of the coding unit of each minimum coding block.</summary>
    public Span<sbyte> QpY => _qpY.Span;

    /// <summary>Gets <c>IntraPredModeY</c> of each 4x4 luma block.</summary>
    public Span<byte> IntraPredModeY => _intraPredModeY.Span;

    /// <summary>Gets the edges of each 4x4 luma block that the deblocking filter considers.</summary>
    public Span<byte> EdgeFlags => _edgeFlags.Span;

    /// <summary>Gets the index in <see cref="Slices"/> of the slice of each coding tree block, in raster scan.</summary>
    public Span<int> SliceIndex => _sliceIndex.Span;

    /// <summary>
    /// Gets the sample adaptive offset parameters of each coding tree block, in raster scan: for each colour component,
    /// <c>SaoTypeIdx</c>, then <c>sao_band_position</c> or <c>SaoEoClass</c>, then the four offsets before their scaling.
    /// </summary>
    public Span<sbyte> Sao => _sao.Span;

    /// <summary>
    /// Gets the motion of each 4x4 luma block of the picture being decoded; empty for a picture that has had no P or B slice
    /// so far, whose blocks are all coded in intra prediction mode.
    /// </summary>
    public Span<HevcMotion> Motion => _motion is null || !HasMotion ? default : _motion.Span;

    /// <summary>Gets whether the picture being decoded has a P or B slice.</summary>
    public bool HasMotion { get; private set; }

    /// <summary>Gets the reference picture lists of the slices of the picture, in decoding order: <see langword="null"/> for an I slice.</summary>
    public List<HevcReferencePictureLists?> SliceReferences { get; } = [];

    /// <summary>Rents the motion of the picture being decoded, before its first P or B slice.</summary>
    /// <exception cref="ImageResourceLimitException">The motion exceeds the live-allocation limit.</exception>
    public void EnsureMotion()
    {
        if (HasMotion)
            return;

        _motion ??= Av1PooledArray<HevcMotion>.Rent(_scope, (long)WidthIn4x4 * HeightIn4x4, clear: false);
        _motion.Span.Clear();
        HasMotion = true;
    }

    /// <summary>Gets the slice headers of the slices of the picture, in decoding order.</summary>
    public List<HevcSliceSegmentHeader> Slices { get; } = [];

    /// <summary>Gets or sets the address in tile scan of the coding tree block that the next slice segment of the picture starts with.</summary>
    public int NextCtbAddrInTs { get; set; }

    /// <summary>Gets or sets <c>QpY</c> of the last coding unit decoded, which predicts the first quantization group of a dependent slice segment.</summary>
    public int LastQpY { get; set; }

    /// <summary>Gets the context variables stored for the next row of coding tree blocks (<c>TableStateIdxWpp</c>, <c>TableMpsValWpp</c>).</summary>
    public byte[] ContextsWpp { get; } = new byte[HevcContextLayout.Count];

    /// <summary>Gets the Rice parameter initialization states stored for the next row of coding tree blocks (<c>TableStatCoeffWpp</c>).</summary>
    public byte[] StatCoeffWpp { get; } = new byte[4];

    /// <summary>Gets the context variables stored for the next dependent slice segment (<c>TableStateIdxDs</c>, <c>TableMpsValDs</c>).</summary>
    public byte[] ContextsDs { get; } = new byte[HevcContextLayout.Count];

    /// <summary>Gets the Rice parameter initialization states stored for the next dependent slice segment (<c>TableStatCoeffDs</c>).</summary>
    public byte[] StatCoeffDs { get; } = new byte[4];

    /// <summary>Gets or sets the coding tools that the pictures used: a diagnostic.</summary>
    public HevcTools Tools { get; set; }

    /// <summary>Rents the state of the pictures of a sequence.</summary>
    /// <exception cref="ImageResourceLimitException">The state exceeds the live-allocation limit.</exception>
    public static HevcPictureState Create(AllocationScope scope, HevcSequenceParameterSet sps)
    {
        var minCbs = ((long)sps.PicWidthInCtbsY << (sps.CtbLog2SizeY - sps.MinCbLog2SizeY)) * ((long)sps.PicHeightInCtbsY << (sps.CtbLog2SizeY - sps.MinCbLog2SizeY));
        var blocks4x4 = ((long)sps.PicWidthInCtbsY << (sps.CtbLog2SizeY - 2)) * ((long)sps.PicHeightInCtbsY << (sps.CtbLog2SizeY - 2));
        var ctbs = (long)sps.PicWidthInCtbsY * sps.PicHeightInCtbsY;
        Av1PooledArray<byte>? cuFlags = null;
        Av1PooledArray<sbyte>? qpY = null;
        Av1PooledArray<byte>? intraPredModeY = null;
        Av1PooledArray<byte>? edgeFlags = null;
        Av1PooledArray<int>? sliceIndex = null;
        try
        {
            cuFlags = Av1PooledArray<byte>.Rent(scope, minCbs);
            qpY = Av1PooledArray<sbyte>.Rent(scope, minCbs);
            intraPredModeY = Av1PooledArray<byte>.Rent(scope, blocks4x4);
            edgeFlags = Av1PooledArray<byte>.Rent(scope, blocks4x4);
            sliceIndex = Av1PooledArray<int>.Rent(scope, ctbs);
            var sao = Av1PooledArray<sbyte>.Rent(scope, ctbs * SaoValuesPerCtb);
            return new HevcPictureState(scope, sps, cuFlags, qpY, intraPredModeY, edgeFlags, sliceIndex, sao);
        }
        catch
        {
            cuFlags?.Dispose();
            qpY?.Dispose();
            intraPredModeY?.Dispose();
            edgeFlags?.Dispose();
            sliceIndex?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Keeps the motion of the picture that is decoded for the temporal motion vector prediction of the pictures that
    /// follow it: the motion of the block at the top left of each 16x16 block (clause 8.5.3.2.8).
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The motion exceeds the live-allocation limit.</exception>
    public void StoreCollocatedMotion(HevcPicture picture, HevcSequenceParameterSet sps)
    {
        if (!HasMotion)
            return;

        var motion = _motion!.Span;
        var collocated = picture.AllocateCollocatedMotion(_scope);
        var stride = picture.CollocatedMotionStride;
        var sliceIndex = _sliceIndex.Span;
        for (var by = 0; by < HeightIn4x4; by += 4)
        {
            for (var bx = 0; bx < WidthIn4x4; bx += 4)
            {
                var m = motion[(by * WidthIn4x4) + bx];
                if (m.PredFlags is 0)
                    continue;

                var ctb = (((by << 2) >> sps.CtbLog2SizeY) * sps.PicWidthInCtbsY) + ((bx << 2) >> sps.CtbLog2SizeY);
                var references = SliceReferences[sliceIndex[ctb]];
                if (references is null)
                    continue;

                ref var target = ref collocated[((by >> 2) * stride) + (bx >> 2)];
                target.Flags = m.PredFlags;
                target.Mv0X = m.Mv0X;
                target.Mv0Y = m.Mv0Y;
                target.Mv1X = m.Mv1X;
                target.Mv1Y = m.Mv1Y;
                if (m.Uses(0))
                {
                    target.RefPoc0 = references.GetPoc(0, m.RefIdx0);
                    target.Flags |= (byte)(references.IsLongTerm(0, m.RefIdx0) ? 1 << HevcCollocatedMotion.LongTermShift : 0);
                }

                if (m.Uses(1))
                {
                    target.RefPoc1 = references.GetPoc(1, m.RefIdx1);
                    target.Flags |= (byte)(references.IsLongTerm(1, m.RefIdx1) ? 2 << HevcCollocatedMotion.LongTermShift : 0);
                }
            }
        }
    }

    /// <summary>Clears the state before the first slice segment of a picture.</summary>
    public void BeginPicture()
    {
        _cuFlags.Span.Clear();
        _qpY.Span.Clear();
        _intraPredModeY.Span.Clear();
        _edgeFlags.Span.Clear();
        _sliceIndex.Span.Clear();
        _sao.Span.Clear();
        Slices.Clear();
        SliceReferences.Clear();
        HasMotion = false;
        NextCtbAddrInTs = 0;
        LastQpY = 0;
    }

    /// <summary>
    /// Gives the coding tree blocks whose addresses in tile scan are in [<paramref name="first"/>,
    /// <paramref name="end"/>) the state that <see cref="BeginPicture"/> gives them: the workers of
    /// <see cref="HevcSubstreamWorkers"/> decoded them for a slice segment that ends before them.
    /// </summary>
    public void ClearCodingTreeBlocks(HevcPictureLayout layout, int first, int end)
    {
        var sps = layout.Sps;
        var minCbs = 1 << (sps.CtbLog2SizeY - MinCbLog2SizeY);
        var blocks = 1 << (sps.CtbLog2SizeY - 2);
        var ctbAddrTsToRs = layout.CtbAddrTsToRs;
        var cuFlags = _cuFlags.Span;
        var qpY = _qpY.Span;
        var intraPredModeY = _intraPredModeY.Span;
        var edgeFlags = _edgeFlags.Span;
        var motion = Motion;
        for (var ctbAddrInTs = first; ctbAddrInTs < end; ctbAddrInTs++)
        {
            var ctbAddrInRs = ctbAddrTsToRs[ctbAddrInTs];
            var rx = ctbAddrInRs % sps.PicWidthInCtbsY;
            var ry = ctbAddrInRs / sps.PicWidthInCtbsY;
            _sliceIndex.Span[ctbAddrInRs] = 0;
            _sao.Span.Slice(ctbAddrInRs * SaoValuesPerCtb, SaoValuesPerCtb).Clear();
            for (var y = 0; y < minCbs; y++)
            {
                var index = (((ry * minCbs) + y) * WidthInMinCbs) + (rx * minCbs);
                cuFlags.Slice(index, minCbs).Clear();
                qpY.Slice(index, minCbs).Clear();
            }

            for (var y = 0; y < blocks; y++)
            {
                var index = (((ry * blocks) + y) * WidthIn4x4) + (rx * blocks);
                intraPredModeY.Slice(index, blocks).Clear();
                edgeFlags.Slice(index, blocks).Clear();
                if (!motion.IsEmpty)
                {
                    motion.Slice(index, blocks).Clear();
                }
            }
        }
    }

    public void Dispose()
    {
        _cuFlags.Dispose();
        _qpY.Dispose();
        _intraPredModeY.Dispose();
        _edgeFlags.Dispose();
        _sliceIndex.Dispose();
        _sao.Dispose();
        _motion?.Dispose();
        _motion = null;
    }
}
