namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A decoded HEVC picture: its sample arrays, and what identifies it in the decoded picture buffer. A picture is shared by
/// the decoded picture buffer, the picture being decoded and the caller that it is output to: its buffers are released
/// when the last of them releases it.
/// </summary>
internal sealed class HevcPicture
{
    private readonly Av1Plane?[] _planes = new Av1Plane?[3];
    private int _referenceCount = 1;
    private Av1PooledArray<HevcCollocatedMotion>? _collocatedMotion;

    public HevcPicture(HevcSequenceParameterSet sps)
    {
        Sps = sps;
    }

    /// <summary>
    /// Gets the sequence parameter set that was active for the picture: its bit depth, its chroma format, its size and its
    /// conformance cropping window (<see cref="HevcSequenceParameterSet.OutputLeft"/> and the other members), which the
    /// planes are not cropped to.
    /// </summary>
    public HevcSequenceParameterSet Sps { get; }

    /// <summary>Gets the number of planes: 1 for a monochrome picture, otherwise 3.</summary>
    public int PlaneCount => Sps.ComponentCount;

    /// <summary>Gets <c>PicOrderCntVal</c>, the picture order count of the picture.</summary>
    public int PicOrderCntVal { get; set; }

    /// <summary>Gets the index of the access unit that the picture was decoded from, the first one given to the decoder being 0.</summary>
    public int AccessUnitIndex { get; set; }

    /// <summary>Gets the hash of the picture that its access unit carries, or <see langword="null"/>.</summary>
    public HevcDecodedPictureHash? DecodedPictureHash { get; set; }

    /// <summary>Gets or sets <c>PicOutputFlag</c>: whether the picture is output.</summary>
    public bool PicOutputFlag { get; set; }

    /// <summary>Gets or sets whether the picture is marked as "needed for output" (clause C.5.2).</summary>
    public bool NeededForOutput { get; set; }

    /// <summary>Gets or sets <c>PicLatencyCount</c> (clause C.5.2.3).</summary>
    public int PicLatencyCount { get; set; }

    /// <summary>Gets or sets whether the picture is marked as "used for reference", short-term or long-term (clause 8.3.2).</summary>
    public bool UsedForReference { get; set; }

    /// <summary>Gets the plane <paramref name="index"/>: 0 for Y, 1 for Cb, 2 for Cr.</summary>
    public Av1Plane GetPlane(int index) => _planes[index] ?? throw new ObjectDisposedException(nameof(HevcPicture));

    /// <summary>
    /// Rents the planes of the picture: <c>pic_width_in_luma_samples</c> x <c>pic_height_in_luma_samples</c> luma samples,
    /// and the chroma samples of the chroma format, allocated for whole coding tree blocks.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The planes exceed the live-allocation limit.</exception>
    public void Allocate(AllocationScope scope)
    {
        try
        {
            for (var plane = 0; plane < PlaneCount; plane++)
            {
                var subWidth = plane > 0 ? Sps.SubWidthC : 1;
                var subHeight = plane > 0 ? Sps.SubHeightC : 1;
                _planes[plane] = Av1Plane.Create(scope, Sps.PicWidthInLumaSamples / subWidth, Sps.PicHeightInLumaSamples / subHeight, Sps.CtbSizeY / subWidth);
            }
        }
        catch
        {
            ReleaseBuffers();
            throw;
        }
    }

    /// <summary>Gets or sets whether the picture is marked as "used for long-term reference" rather than as "used for short-term reference" (clause 8.3.2).</summary>
    public bool IsLongTermReference { get; set; }

    /// <summary>Gets or sets whether the reference picture set of the picture being decoded has the picture: a working value of clause 8.3.2.</summary>
    public bool IsInReferencePictureSet { get; set; }

    /// <summary>Gets the number of 16x16 blocks of a row of <see cref="CollocatedMotion"/>.</summary>
    public int CollocatedMotionStride => (Sps.PicWidthInLumaSamples + 15) >> 4;

    /// <summary>
    /// Gets the motion that the temporal motion vector prediction of other pictures reads, one value for each 16x16 block
    /// of luma samples; empty for a picture that has no block predicted from another picture.
    /// </summary>
    public ReadOnlySpan<HevcCollocatedMotion> CollocatedMotion => _collocatedMotion is null ? default : _collocatedMotion.Span;

    /// <summary>Rents <see cref="CollocatedMotion"/>, whose values are those of blocks that are not predicted from another picture.</summary>
    /// <exception cref="ImageResourceLimitException">The motion exceeds the live-allocation limit.</exception>
    public Span<HevcCollocatedMotion> AllocateCollocatedMotion(AllocationScope scope)
    {
        _collocatedMotion ??= Av1PooledArray<HevcCollocatedMotion>.Rent(scope, (long)CollocatedMotionStride * ((Sps.PicHeightInLumaSamples + 15) >> 4));
        return _collocatedMotion.Span;
    }

    /// <summary>Adds a holder of the picture.</summary>
    public void AddReference() => _referenceCount++;

    /// <summary>Removes a holder of the picture, and returns its buffers to the pool when it was the last one.</summary>
    public void Release()
    {
        if (--_referenceCount is 0)
        {
            ReleaseBuffers();
        }
    }

    private void ReleaseBuffers()
    {
        _collocatedMotion?.Dispose();
        _collocatedMotion = null;
        for (var i = 0; i < _planes.Length; i++)
        {
            _planes[i]?.Dispose();
            _planes[i] = null;
        }
    }
}
