namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The buffers of the HEVC encoder: the source planes, the description of the coding unit being searched, the snapshots
/// that the search restores when a candidate wins against the one tried after it, and the records of the decisions, which
/// are replayed to the arithmetic encoder once a coding tree unit, or a picture, is decided.
/// </summary>
/// <remarks>
/// The coding unit being searched is described by position: maps of one value for each block of 4x4 luma samples of the
/// coding unit, and the transform coefficient levels of its three components at the location of their samples.
/// <see cref="HevcSliceCoder"/> writes a coding unit from that description, in the order of the syntax, and records it at the
/// same time.
/// </remarks>
internal sealed class HevcEncoderWorkspace : IDisposable
{
    /// <summary>The number of blocks of 4x4 luma samples across the largest coding unit.</summary>
    public const int CellStride = 16;

    /// <summary>The number of blocks of 4x4 luma samples of the largest coding unit.</summary>
    public const int CellCount = CellStride * CellStride;

    /// <summary>The number of samples across the largest coding unit.</summary>
    public const int BlockStride = 64;

    /// <summary>The number of samples of a component of the largest coding unit.</summary>
    public const int BlockLength = BlockStride * BlockStride;

    /// <summary>The maps of the coding unit: the depth of the transform tree at each block.</summary>
    public const int MapTrafoDepth = 0;

    /// <summary>The maps of the coding unit: <c>cbf_luma</c>, at the first block of each transform unit.</summary>
    public const int MapCbfLuma = 1;

    /// <summary>The maps of the coding unit: <c>transform_skip_flag</c> of the luma block of each transform unit.</summary>
    public const int MapTransformSkipLuma = 2;

    /// <summary>
    /// The maps of the coding unit: <c>cbf_cb</c> (bits 0 and 1) and <c>cbf_cr</c> (bits 2 and 3) of the chroma blocks, at
    /// the first block of the transform unit, or of the node of the transform tree, that has them.
    /// </summary>
    public const int MapCbfChroma = 3;

    /// <summary>The maps of the coding unit: <c>transform_skip_flag</c> of the chroma blocks, as <see cref="MapCbfChroma"/>.</summary>
    public const int MapTransformSkipChroma = 4;

    /// <summary>The number of maps of the coding unit.</summary>
    public const int MapCount = 5;

    // The snapshots of the context variables
    public const int ContextCtuStart = 0;
    public const int ContextCuStart = 1;
    public const int ContextCuBest = 2;
    public const int ContextLumaStart = 3;
    public const int ContextLumaBest = 4;
    public const int ContextPartStart = 5;
    public const int ContextChromaStart = 6;
    public const int ContextChromaBest = 7;
    public const int ContextBlock = 8;
    public const int ContextBlockBest = 9;
    public const int ContextCheck = 10;

    /// <summary>Two snapshots for each depth of the coding quadtree.</summary>
    public const int ContextQuadtree = 11;

    /// <summary>Two snapshots for each depth of the transform tree.</summary>
    public const int ContextTransform = ContextQuadtree + (2 * TreeDepths);

    private const int ContextSlots = ContextTransform + (2 * TreeDepths);

    // The snapshots of a part of the coding unit being searched: its samples, its levels and its maps
    public const int DraftLumaBest = 0;
    public const int DraftPartBest = 1;
    public const int DraftChromaBest = 2;

    /// <summary>The luma prediction of a motion vector that the motion search tries: only its samples are used.</summary>
    public const int DraftMotionSearch = 3;

    /// <summary>One snapshot for each depth of the transform tree.</summary>
    public const int DraftTransform = 4;

    private const int DraftSlots = DraftTransform + TreeDepths;

    // The snapshots of the samples of a coding unit
    public const int SamplesCuBest = 0;

    /// <summary>One snapshot for each depth of the coding quadtree.</summary>
    public const int SamplesQuadtree = 1;

    /// <summary>The prediction of a coding unit coded in inter prediction mode, which its candidates share.</summary>
    public const int SamplesPrediction = SamplesQuadtree + TreeDepths;

    private const int SampleSlots = SamplesPrediction + 1;

    private const int TreeDepths = 4;
    private const int InitialRecordCapacity = 1 << 14;
    private const int ScratchBlocks = 7;
    private const int ScratchBlockLength = 32 * 32;
    private const int IntraScratchLength = (2 * HevcIntraPredictor.MaxNeighbourCount) + ScratchBlockLength;

    private readonly AllocationScope _scope;
    private readonly Av1Plane?[] _source = new Av1Plane?[3];
    private readonly Av1PooledArray<byte> _contexts;
    private readonly Av1PooledArray<byte> _maps;
    private readonly Av1PooledArray<short> _levels;
    private readonly Av1PooledArray<ushort> _samples;
    private readonly Av1PooledArray<int> _scratch;
    private readonly Av1PooledArray<ushort> _intraScratch;
    private Av1PooledArray<short>? _records;

    public HevcEncoderWorkspace(AllocationScope scope, HevcSequenceParameterSet sps, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(sps);
        _scope = scope;
        Av1PooledArray<byte>? contexts = null;
        Av1PooledArray<byte>? maps = null;
        Av1PooledArray<short>? levels = null;
        Av1PooledArray<ushort>? samples = null;
        Av1PooledArray<int>? scratch = null;
        Av1PooledArray<ushort>? intraScratch = null;
        try
        {
            // The planes of the source have the samples of the picture to encode, in buffers of the size of the planes
            // of the picture being reconstructed
            for (var plane = 0; plane < sps.ComponentCount; plane++)
            {
                var subWidth = plane > 0 ? sps.SubWidthC : 1;
                var subHeight = plane > 0 ? sps.SubHeightC : 1;
                _source[plane] = Av1Plane.Create(scope, (width + subWidth - 1) / subWidth, (height + subHeight - 1) / subHeight, sps.CtbSizeY / subWidth, AllocationKind.Temporary);
            }

            var components = sps.ComponentCount;
            contexts = Av1PooledArray<byte>.Rent(scope, ContextSlots * HevcContextLayout.Count, clear: true, AllocationKind.Temporary);
            maps = Av1PooledArray<byte>.Rent(scope, (1 + DraftSlots) * MapCount * CellCount, clear: true, AllocationKind.Temporary);
            levels = Av1PooledArray<short>.Rent(scope, (long)(1 + DraftSlots) * components * BlockLength, clear: true, AllocationKind.Temporary);
            samples = Av1PooledArray<ushort>.Rent(scope, (long)(DraftSlots + SampleSlots) * components * BlockLength, clear: true, AllocationKind.Temporary);
            scratch = Av1PooledArray<int>.Rent(scope, ScratchBlocks * ScratchBlockLength, clear: true, AllocationKind.Temporary);
            intraScratch = Av1PooledArray<ushort>.Rent(scope, IntraScratchLength, clear: true, AllocationKind.Temporary);
            _records = Av1PooledArray<short>.Rent(scope, InitialRecordCapacity, clear: false, AllocationKind.Temporary);
            _contexts = contexts;
            _maps = maps;
            _levels = levels;
            _samples = samples;
            _scratch = scratch;
            _intraScratch = intraScratch;
            ComponentCount = components;
        }
        catch
        {
            contexts?.Dispose();
            maps?.Dispose();
            levels?.Dispose();
            samples?.Dispose();
            scratch?.Dispose();
            intraScratch?.Dispose();
            foreach (var plane in _source)
            {
                plane?.Dispose();
            }

            throw;
        }
    }

    public int ComponentCount { get; }

    /// <summary>Gets or sets the number of records.</summary>
    public int RecordCount { get; set; }

    /// <summary>Gets or sets the position of the next record that is replayed.</summary>
    public int RecordPosition { get; set; }

    /// <summary>Gets the records: the span is valid until a record is appended.</summary>
    public Span<short> Records => (_records ?? throw new ObjectDisposedException(nameof(HevcEncoderWorkspace))).Span;

    // The coding unit being searched: its prediction
    public bool Skip { get; set; }

    public int PartMode { get; set; }

    public bool RqtRootCbf { get; set; }

    /// <summary>Gets the intra prediction modes of the four luma prediction blocks: one byte each.</summary>
    public uint LumaModes { get; set; }

    /// <summary>Gets <c>intra_chroma_pred_mode</c> of the four prediction blocks: one byte each.</summary>
    public uint ChromaModes { get; set; }

    /// <summary>Gets the motion of the two prediction units of a coding unit coded in inter prediction mode.</summary>
    public HevcMotionChoice[] Motion { get; } = new HevcMotionChoice[2];

    public Av1Plane GetSourcePlane(int plane) => _source[plane] ?? throw new ObjectDisposedException(nameof(HevcEncoderWorkspace));

    /// <summary>Gets a map of the coding unit being searched: <see cref="CellStride"/> values for each row of blocks.</summary>
    public Span<byte> GetMap(int map) => _maps.Span.Slice(map * CellCount, CellCount);

    /// <summary>Gets the maps of a snapshot.</summary>
    public Span<byte> GetSavedMap(int slot, int map) => _maps.Span.Slice((((1 + slot) * MapCount) + map) * CellCount, CellCount);

    /// <summary>Gets the levels of a component of the coding unit being searched: <see cref="BlockStride"/> values for each row.</summary>
    public Span<short> GetLevels(int cIdx) => _levels.Span.Slice(cIdx * BlockLength, BlockLength);

    /// <summary>Gets the levels of a component of a snapshot.</summary>
    public Span<short> GetSavedLevels(int slot, int cIdx) => _levels.Span.Slice((((1 + slot) * ComponentCount) + cIdx) * BlockLength, BlockLength);

    /// <summary>Gets the samples of a component of a snapshot of a part of the coding unit.</summary>
    public Span<ushort> GetDraftSamples(int slot, int cIdx) => _samples.Span.Slice(((slot * ComponentCount) + cIdx) * BlockLength, BlockLength);

    /// <summary>Gets the samples of a component of a snapshot of a coding unit.</summary>
    public Span<ushort> GetSavedSamples(int slot, int cIdx) => _samples.Span.Slice((((DraftSlots + slot) * ComponentCount) + cIdx) * BlockLength, BlockLength);

    /// <summary>Gets a snapshot of the context variables.</summary>
    public Span<byte> GetContexts(int slot) => _contexts.Span.Slice(slot * HevcContextLayout.Count, HevcContextLayout.Count);

    /// <summary>Gets a block of 32x32 values: 0 to 6.</summary>
    public Span<int> GetScratch(int index) => _scratch.Span.Slice(index * ScratchBlockLength, ScratchBlockLength);

    /// <summary>
    /// Gets the scratch of the evaluation of the intra prediction modes of a luma prediction block: its neighbouring samples
    /// and their filtered version (<see cref="HevcIntraPredictor.MaxNeighbourCount"/> samples each), then the prediction of
    /// a mode (32x32 samples).
    /// </summary>
    public Span<ushort> IntraScratch => _intraScratch.Span[..IntraScratchLength];

    /// <summary>Clears the description of the coding unit being searched.</summary>
    public void ClearCodingUnit()
    {
        _maps.Span[..(MapCount * CellCount)].Clear();
        Skip = false;
        PartMode = HevcConstants.Part2Nx2N;
        RqtRootCbf = false;
        LumaModes = 0;
        ChromaModes = 0;
        Motion[0] = default;
        Motion[1] = default;
    }

    /// <summary>Appends a record.</summary>
    public void AppendRecord(int value)
    {
        var records = _records ?? throw new ObjectDisposedException(nameof(HevcEncoderWorkspace));
        if (RecordCount == records.Length)
        {
            EnsureRecords(1);
            records = _records;
        }

        records.Span[RecordCount++] = (short)value;
    }

    /// <summary>Makes room for <paramref name="count"/> more records, and returns where they go: the caller sets <see cref="RecordCount"/>.</summary>
    /// <exception cref="ImageResourceLimitException">The records exceed the live-allocation limit.</exception>
    public Span<short> EnsureRecords(int count)
    {
        var records = _records ?? throw new ObjectDisposedException(nameof(HevcEncoderWorkspace));
        var required = (long)RecordCount + count;
        if (required > records.Length)
        {
            var grown = Av1PooledArray<short>.Rent(_scope, Math.Max(required, (long)records.Length * 2), clear: false, AllocationKind.Temporary);
            records.Span[..RecordCount].CopyTo(grown.Span);
            records.Dispose();
            _records = grown;
            records = grown;
        }

        return records.Span.Slice(RecordCount, count);
    }

    public void Dispose()
    {
        for (var i = 0; i < _source.Length; i++)
        {
            _source[i]?.Dispose();
            _source[i] = null;
        }

        _contexts.Dispose();
        _maps.Dispose();
        _levels.Dispose();
        _samples.Dispose();
        _scratch.Dispose();
        _intraScratch.Dispose();
        _records?.Dispose();
        _records = null;
    }
}
