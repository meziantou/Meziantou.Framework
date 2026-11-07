namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A decoded AV1 frame and the state that later frames load from it (AV1 specification, section 7.20 "Reference frame update
/// process"): the sequence and frame headers of the frame that produced it, its planes and its cumulative distributions.
/// A frame buffer is shared by the reference slots that hold it, the frame being decoded and the output frame: it is
/// released when the last of them releases it.
/// </summary>
internal sealed class Av1FrameBuffer
{
    private readonly Av1Plane?[] _planes = new Av1Plane?[3];
    private Av1PooledArray<ushort>? _cdfs;
    private Av1PooledArray<byte>? _segmentIds;
    private Av1PooledArray<sbyte>? _motionFieldRefFrames;
    private Av1PooledArray<short>? _motionFieldMvs;
    private int _referenceCount = 1;

    public Av1FrameBuffer(Av1SequenceHeader sequence, Av1FrameHeader header)
    {
        Sequence = sequence;
        Header = header;
    }

    public Av1SequenceHeader Sequence { get; }

    public Av1FrameHeader Header { get; }

    /// <summary>Gets the number of planes: 1 for a monochrome frame, otherwise 3.</summary>
    public int PlaneCount => Sequence.PlaneCount;

    /// <summary>Gets the plane <paramref name="index"/>: 0 for Y, 1 for U, 2 for V.</summary>
    public Av1Plane GetPlane(int index) => _planes[index] ?? throw new ObjectDisposedException(nameof(Av1FrameBuffer));

    /// <summary>
    /// Gets the cumulative distributions saved with the frame (<c>save_cdfs</c>): the frame context that a later frame loads
    /// when this frame is its primary reference frame.
    /// </summary>
    public Span<ushort> Cdfs => (_cdfs ?? throw new ObjectDisposedException(nameof(Av1FrameBuffer))).Span;

    /// <summary>
    /// Gets the segmentation map saved with the frame (<c>SavedSegmentIds</c>): one entry per 4x4 luma block, or an empty span
    /// when every segment is 0.
    /// </summary>
    public ReadOnlySpan<byte> SegmentIds => _segmentIds is null ? default : _segmentIds.Span;

    /// <summary>
    /// Gets the reference frames saved with the frame for the motion field of later frames (<c>SavedRefFrames</c>): one entry
    /// per 8x8 luma block, or an empty span when no block has one.
    /// </summary>
    public ReadOnlySpan<sbyte> MotionFieldRefFrames => _motionFieldRefFrames is null ? default : _motionFieldRefFrames.Span;

    /// <summary>Gets the motion vectors saved with the frame (<c>SavedMvs</c>): a row and a column per 8x8 luma block.</summary>
    public ReadOnlySpan<short> MotionFieldMvs => _motionFieldMvs is null ? default : _motionFieldMvs.Span;

    /// <summary>Saves the segmentation map of the frame.</summary>
    /// <exception cref="ImageResourceLimitException">The map exceeds the live-allocation limit.</exception>
    public void SaveSegmentIds(AllocationScope scope, ReadOnlySpan<byte> segmentIds)
    {
        _segmentIds ??= Av1PooledArray<byte>.Rent(scope, segmentIds.Length, clear: false);
        segmentIds.CopyTo(_segmentIds.Span);
    }

    /// <summary>Saves the motion vectors of the frame for the motion field of later frames.</summary>
    /// <exception cref="ImageResourceLimitException">The motion vectors exceed the live-allocation limit.</exception>
    public void SaveMotionField(AllocationScope scope, ReadOnlySpan<sbyte> refFrames, ReadOnlySpan<short> mvs)
    {
        var count = (long)(Header.MiCols >> 1) * (Header.MiRows >> 1);
        _motionFieldRefFrames ??= Av1PooledArray<sbyte>.Rent(scope, count, clear: false);
        _motionFieldMvs ??= Av1PooledArray<short>.Rent(scope, 2 * count, clear: false);
        Av1MotionField.Store(Sequence, Header, refFrames, mvs, _motionFieldRefFrames.Span, _motionFieldMvs.Span);
    }

    /// <summary>
    /// Rents the planes and the frame context of the frame: the planes have the size of the frame after super-resolution
    /// upscaling, and at least the size of the coded frame.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    public void Allocate(AllocationScope scope)
    {
        var alignment = Sequence.Use128x128Superblock ? 128 : 64;
        var width = Math.Max(Header.UpscaledWidth, Header.FrameWidth);
        try
        {
            _cdfs = Av1PooledArray<ushort>.Rent(scope, Av1CdfLayout.TotalLength, clear: false);
            for (var plane = 0; plane < PlaneCount; plane++)
            {
                var subX = plane > 0 ? Sequence.SubsamplingX : 0;
                var subY = plane > 0 ? Sequence.SubsamplingY : 0;
                _planes[plane] = Av1Plane.Create(scope, (width + subX) >> subX, (Header.FrameHeight + subY) >> subY, alignment);
            }
        }
        catch
        {
            ReleaseBuffers();
            throw;
        }
    }

    /// <summary>Adds a holder of the frame buffer.</summary>
    public void AddReference() => _referenceCount++;

    /// <summary>Removes a holder of the frame buffer, and returns its buffers to the pool when it was the last one.</summary>
    public void Release()
    {
        if (--_referenceCount is 0)
            ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        for (var i = 0; i < _planes.Length; i++)
        {
            _planes[i]?.Dispose();
            _planes[i] = null;
        }

        _cdfs?.Dispose();
        _cdfs = null;
        _segmentIds?.Dispose();
        _segmentIds = null;
        _motionFieldRefFrames?.Dispose();
        _motionFieldRefFrames = null;
        _motionFieldMvs?.Dispose();
        _motionFieldMvs = null;
    }
}
