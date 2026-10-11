namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The state that the frames of an AV1 sequence share, whichever side codes them (AV1 specification, sections 7.20 "Reference
/// frame update process" and 7.21 "Reference frame loading process"): the 8 reference slots, the block-level state of the
/// frame being coded, the working buffers of its tiles and the cumulative distributions of the frame, of the tile being coded
/// and of the tile that updates the frame context. The decoder and the encoder drive the same state, so that an encoder
/// keeps exactly what a decoder of its output keeps.
/// </summary>
internal sealed class Av1FrameCodingState : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly Av1FrameBuffer?[] _slots = new Av1FrameBuffer?[Av1Constants.NumRefFrames];
    private readonly bool[] _refValid = new bool[Av1Constants.NumRefFrames];
    private Av1PooledArray<ushort>? _contexts;

    public Av1FrameCodingState(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    /// <summary>Gets the block-level state of the frame being coded.</summary>
    public Av1FrameState State { get; } = new();

    /// <summary>Gets the post-processing filters and their working buffers.</summary>
    public Av1PostFilter PostFilter { get; } = new();

    /// <summary>Gets the reference frames of the frame being coded.</summary>
    public Av1ReferenceFrames References { get; } = new();

    /// <summary>Gets the working buffers of the tiles, rented by the first frame.</summary>
    public Av1TileScratch? Scratch { get; private set; }

    /// <summary>Gets the working buffers of the inter prediction, rented by the first frame that predicts from another frame.</summary>
    public Av1InterScratch? InterScratch { get; private set; }

    /// <summary>Gets the frame being coded, or <see langword="null"/> between two frames.</summary>
    public Av1FrameBuffer? CurrentFrame { get; private set; }

    /// <summary>Gets the frames of the reference slots (<c>ref_frame_idx</c> indexes them).</summary>
    public ReadOnlySpan<Av1FrameBuffer?> Slots => _slots;

    /// <summary>Gets <c>RefValid</c>.</summary>
    public Span<bool> RefValid => _refValid;

    /// <summary>Gets the frame context: the cumulative distributions that the tiles of the frame start from.</summary>
    public Span<ushort> FrameContext => _contexts!.Span[..Av1CdfLayout.TotalLength];

    /// <summary>Gets the context of the tile being coded.</summary>
    public Span<ushort> TileContext => _contexts!.Span.Slice(Av1CdfLayout.TotalLength, Av1CdfLayout.TotalLength);

    /// <summary>Gets the context saved by the tile that updates the frame context.</summary>
    public Span<ushort> SavedContext => _contexts!.Span.Slice(2 * Av1CdfLayout.TotalLength, Av1CdfLayout.TotalLength);

    /// <summary>
    /// Starts a frame: rents its planes, resolves its reference frames, prepares the block-level state and loads the frame
    /// context (<c>init_non_coeff_cdfs</c> and <c>init_coeff_cdfs</c>, or <c>load_cdfs</c>).
    /// </summary>
    /// <returns>The frame, which stays <see cref="CurrentFrame"/> until <see cref="StoreFrame"/>.</returns>
    /// <exception cref="InvalidImageContentException">The frame refers to an empty reference slot.</exception>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    public Av1FrameBuffer BeginFrame(Av1SequenceHeader sequence, Av1FrameHeader header)
    {
        Scratch ??= new Av1TileScratch(_scope);
        _contexts ??= Av1PooledArray<ushort>.Rent(_scope, 3L * Av1CdfLayout.TotalLength, clear: false);

        var frame = new Av1FrameBuffer(sequence, header);
        CurrentFrame?.Release();
        CurrentFrame = frame;
        frame.Allocate(_scope);

        // The reference frames of the frame, and load_previous_segment_ids
        References.Clear();
        Av1FrameBuffer? segmentationSource = null;
        if (!header.FrameIsIntra)
        {
            for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
            {
                var reference = _slots[header.RefFrameIdx[i]] ?? throw Av1Errors.Invalid("a frame refers to an empty reference slot.");
                References.Set(Av1Constants.LastFrame + i, reference, sequence, header);
            }
        }

        if (header.PrimaryRefFrame is not Av1Constants.PrimaryRefNone && header.SegmentationEnabled)
        {
            var previous = _slots[header.RefFrameIdx[header.PrimaryRefFrame]] ?? throw Av1Errors.Invalid("a frame refers to an empty reference slot.");
            if (previous.Header.MiCols == header.MiCols && previous.Header.MiRows == header.MiRows)
                segmentationSource = previous;
        }

        if (!header.FrameIsIntra || header.AllowIntrabc)
            InterScratch ??= new Av1InterScratch(_scope);

        State.Prepare(_scope, sequence, header, segmentationSource);
        if (header.UseRefFrameMvs)
            Av1MotionField.Estimate(sequence, header, References, State.MotionFieldMvs);

        // init_non_coeff_cdfs and init_coeff_cdfs, or load_cdfs
        var frameContext = FrameContext;
        if (header.PrimaryRefFrame is Av1Constants.PrimaryRefNone)
        {
            Av1CdfLayout.InitializeNonCoefficients(frameContext);
            var index = header.BaseQIdx switch
            {
                <= 20 => 0,
                <= 60 => 1,
                <= 120 => 2,
                _ => 3,
            };

            Av1CdfLayout.InitializeCoefficients(frameContext, index);
        }
        else
        {
            var reference = _slots[header.RefFrameIdx[header.PrimaryRefFrame]] ?? throw Av1Errors.Invalid("a frame refers to an empty reference slot.");
            reference.Cdfs.CopyTo(frameContext);
            Av1CdfLayout.ResetCounters(frameContext);
        }

        return frame;
    }

    /// <summary>
    /// Starts a tile (<c>init_symbol</c>, section 8.2.2): the tile is coded with a copy of the frame context, and with the
    /// default intra frame y mode distributions.
    /// </summary>
    /// <returns><see cref="TileContext"/>.</returns>
    public Span<ushort> BeginTile()
    {
        var tileContext = TileContext;
        FrameContext.CopyTo(tileContext);
        Av1Tables.DefaultIntraFrameYModeCdf.CopyTo(tileContext[Av1CdfLayout.IntraFrameYMode..]);
        return tileContext;
    }

    /// <summary>Ends the tile <paramref name="tileNum"/> (<c>exit_symbol</c>, section 8.2.4): saves its context when it is the one that updates the frame context.</summary>
    public void EndTile(Av1FrameHeader header, int tileNum)
    {
        if (!header.DisableFrameEndUpdateCdf && tileNum == header.ContextUpdateTileId)
            TileContext.CopyTo(SavedContext);
    }

    /// <summary>Ends the last tile of the frame: the saved context becomes the frame context (section 7.4).</summary>
    public void EndTiles(Av1FrameHeader header)
    {
        if (!header.DisableFrameEndUpdateCdf)
            SavedContext.CopyTo(FrameContext);
    }

    /// <summary>
    /// Ends <see cref="CurrentFrame"/> after its post-processing filters: saves its motion field, its segmentation map and its
    /// frame context, and updates the reference slots (sections 7.19 and 7.20).
    /// </summary>
    /// <returns>The frame; the caller releases it.</returns>
    /// <exception cref="ImageResourceLimitException">The saved state exceeds the live-allocation limit.</exception>
    public Av1FrameBuffer StoreFrame()
    {
        var frame = CurrentFrame ?? throw new InvalidOperationException("No frame is being coded.");
        var header = frame.Header;

        // Section 7.19 "Motion field motion vector storage process", and the segmentation map of the frame
        if (!header.FrameIsIntra && frame.Sequence.EnableRefFrameMvs)
            frame.SaveMotionField(_scope, State.RefFrames, State.Mvs);

        if (header.SegmentationEnabled)
        {
            var segmentIds = header.SegmentationUpdateMap ? State.SegmentIds : State.PrevSegmentIds;
            if (!segmentIds.IsEmpty)
                frame.SaveSegmentIds(_scope, segmentIds);
        }

        References.Clear();

        // Section 7.20 "Reference frame update process"
        FrameContext.CopyTo(frame.Cdfs);
        for (var i = 0; i < Av1Constants.NumRefFrames; i++)
        {
            if (((header.RefreshFrameFlags >> i) & 1) is not 0)
                SetSlot(i, frame);
        }

        CurrentFrame = null;
        return frame;
    }

    /// <summary>Stores <paramref name="frame"/> in a reference slot, which holds it until it is replaced.</summary>
    public void SetSlot(int index, Av1FrameBuffer frame)
    {
        if (!ReferenceEquals(_slots[index], frame))
        {
            frame.AddReference();
            _slots[index]?.Release();
            _slots[index] = frame;
        }

        _refValid[index] = true;
    }

    /// <summary>Empties the reference slots: a new coded video sequence does not refer to the frames of the previous one.</summary>
    public void ReleaseSlots()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i]?.Release();
            _slots[i] = null;
            _refValid[i] = false;
        }
    }

    public void Dispose()
    {
        ReleaseSlots();
        CurrentFrame?.Release();
        CurrentFrame = null;
        State.Dispose();
        PostFilter.Dispose();
        Scratch?.Dispose();
        Scratch = null;
        InterScratch?.Dispose();
        InterScratch = null;
        References.Clear();
        _contexts?.Dispose();
        _contexts = null;
    }
}
