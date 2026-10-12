using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A track of an AVIF image sequence (ISO/IEC 14496-12, section 8.3 "Track Structure"): the description of its samples, which
/// are the temporal units of an AV1 bitstream, and the location and the duration of each of them.
/// </summary>
internal sealed class AvifTrack : IDisposable
{
    private Av1PooledArray<long>? _sampleOffsets;
    private Av1PooledArray<int>? _sampleSizes;

    // The samples of the movie fragments are appended to the tables, which then only hold the samples that are still to
    // be read: the index of the first one, and the duration of each (the 'stts' runs describe the samples of the 'moov' box)
    private Av1PooledArray<uint>? _fragmentDurations;
    private int _firstLocated;
    private int _locatedCount;

    /// <summary>Gets or sets <c>track_ID</c>.</summary>
    public uint Id { get; set; }

    /// <summary>
    /// Gets or sets the duration of the track with its repetitions, in units of the time scale of the movie;
    /// <see cref="ulong.MaxValue"/> when it is indefinite.
    /// </summary>
    public ulong Duration { get; set; }

    /// <summary>Gets or sets the handler type of the media: <c>pict</c> for an image sequence, <c>auxv</c> for an auxiliary one.</summary>
    public uint HandlerType { get; set; }

    /// <summary>Gets or sets the number of time units of the media in a second.</summary>
    public uint Timescale { get; set; }

    /// <summary>Gets the identifiers of the tracks that this track is auxiliary to (<c>auxl</c> track references).</summary>
    public List<uint> AuxiliaryOf { get; } = [];

    /// <summary>Gets the identifiers of the tracks that this track is premultiplied by (<c>prem</c> track references).</summary>
    public List<uint> PremultipliedBy { get; } = [];

    /// <summary>Gets or sets a value indicating whether the track has an edit list.</summary>
    public bool HasEditList { get; set; }

    /// <summary>Gets or sets a value indicating whether the edit list repeats.</summary>
    public bool IsRepeating { get; set; }

    /// <summary>
    /// Gets or sets the duration of the edits, which is one play of the track, in units of the time scale of the movie;
    /// 0 or <see cref="ulong.MaxValue"/> when an edit has no duration of its own and plays the media to its end.
    /// </summary>
    public ulong SegmentDuration { get; set; }

    /// <summary>
    /// Gets the edits of the edit list, in the order of the list: the duration of each, in units of the time scale of the
    /// movie (0 or <see cref="ulong.MaxValue"/>: to the end of the media), and the time of the media it starts at, in
    /// units of <see cref="Timescale"/>.
    /// </summary>
    public List<(ulong SegmentDuration, long MediaTime)> Edits { get; } = [];

    /// <summary>Gets or sets a value indicating whether the samples are coded with AV1 (an <c>av01</c> sample entry).</summary>
    public bool HasAv1SampleEntry { get; set; }

    /// <summary>Gets or sets the size of the samples that the sample entry declares.</summary>
    public Size Size { get; set; }

    /// <summary>
    /// Gets or sets the part of the samples that the clean aperture of the sample entry (<c>clap</c>) shows, or
    /// <see langword="null"/> for the whole samples.
    /// </summary>
    public Rectangle? CleanAperture { get; set; }

    /// <summary>Gets the part of the samples that is shown: the clean aperture, or the whole samples.</summary>
    public Rectangle Crop => CleanAperture ?? new Rectangle(0, 0, Size.Width, Size.Height);

    /// <summary>
    /// Gets or sets the orientation that the matrix of the track header describes: one of the eight orientations for a
    /// matrix that turns or mirrors the images by quarter turns, <see cref="ExifOrientation.TopLeft"/> for any other one.
    /// </summary>
    public ExifOrientation Orientation { get; set; } = ExifOrientation.TopLeft;

    /// <summary>Gets or sets the bit depth that the AV1 configuration record declares.</summary>
    public int BitDepth { get; set; }

    public bool IsMonochrome { get; set; }

    public int SubsamplingX { get; set; }

    public int SubsamplingY { get; set; }

    /// <summary>Gets or sets the type of an auxiliary track (<c>auxi</c>), or <see langword="null"/>.</summary>
    public byte[]? AuxiliaryType { get; set; }

    /// <summary>Gets or sets the <c>colr</c> box of type <c>nclx</c> of the sample entry, or <see langword="null"/>.</summary>
    public CicpColorDescription? Nclx { get; set; }

    /// <summary>Gets or sets the <c>pasp</c> box of the sample entry, or <see langword="null"/>.</summary>
    public PixelAspectRatio? PixelAspectRatio { get; set; }

    /// <summary>Gets or sets the <c>clli</c> box of the sample entry, or <see langword="null"/>.</summary>
    public ContentLightLevel? ContentLightLevel { get; set; }

    /// <summary>Gets or sets the <c>mdcv</c> box of the sample entry, or <see langword="null"/>.</summary>
    public MasteringDisplayColorVolume? MasteringDisplay { get; set; }

    /// <summary>Gets or sets the range of the ICC profile of the sample entry in the <c>moov</c> payload, or an empty range.</summary>
    public (int Offset, int Length) IccProfile { get; set; }

    /// <summary>Gets or sets the range of the payload of the <c>meta</c> box of the track in the <c>moov</c> payload, or an empty range.</summary>
    public (int Offset, int Length) Meta { get; set; }

    /// <summary>Gets or sets the number of samples of the track.</summary>
    public long SampleCount { get; set; }

    /// <summary>Gets the number of samples whose location is known: the first ones, up to the frame limit of the operation.</summary>
    public int LocatedSampleCount => _firstLocated + _locatedCount;

    /// <summary>Gets or sets the index of the AV1 sample entry in the sample description box, from 1; 0 when the track has none.</summary>
    public uint SampleDescriptionIndex { get; set; }

    /// <summary>Gets or sets a value indicating whether the movie declares defaults for the fragments of the track (<c>trex</c>).</summary>
    public bool HasTrackExtends { get; set; }

    /// <summary>Gets or sets the default sample description index of the fragments of the track (<c>trex</c>).</summary>
    public uint DefaultSampleDescriptionIndex { get; set; }

    /// <summary>Gets or sets the default duration of the samples of the fragments of the track (<c>trex</c>).</summary>
    public uint DefaultSampleDuration { get; set; }

    /// <summary>Gets or sets the default size of the samples of the fragments of the track (<c>trex</c>).</summary>
    public uint DefaultSampleSize { get; set; }

    /// <summary>Gets the durations of the samples in units of <see cref="Timescale"/>: runs of samples of the same duration.</summary>
    public List<(uint Count, uint Delta)> Durations { get; } = [];

    /// <summary>Gets or sets the sum of the durations of the samples, in units of <see cref="Timescale"/>.</summary>
    public ulong MediaDuration { get; set; }

    /// <summary>Gets or sets the duration of the last sample, in units of <see cref="Timescale"/>.</summary>
    public uint LastSampleDuration { get; set; }

    /// <summary>Gets a value indicating whether the track describes an alpha plane.</summary>
    public bool IsAlpha => AuxiliaryType is null || AuxiliaryType.AsSpan().SequenceEqual(AvifFormat.AlphaAuxiliaryType);

    /// <summary>Gets the offset of a sample in the file.</summary>
    public long GetSampleOffset(int index) => _sampleOffsets!.Span[index - _firstLocated];

    /// <summary>Gets the number of bytes of a sample.</summary>
    public int GetSampleSize(int index) => _sampleSizes!.Span[index - _firstLocated];

    /// <summary>Gets the duration of a sample, in units of <see cref="Timescale"/>.</summary>
    public uint GetSampleDuration(int index)
    {
        if (_fragmentDurations is { } durations)
            return durations.Span[index - _firstLocated];

        long first = 0;
        foreach (var (count, delta) in Durations)
        {
            if (index < first + count)
                return delta;

            first += count;
        }

        return 0;
    }

    /// <summary>Rents the tables that locate the first <paramref name="count"/> samples.</summary>
    /// <exception cref="ImageResourceLimitException">The tables exceed the live-allocation limit.</exception>
    public void AllocateSamples(AllocationScope scope, int count, out Span<long> offsets, out Span<int> sizes)
    {
        _sampleOffsets?.Dispose();
        _sampleOffsets = null;
        _sampleSizes?.Dispose();
        _sampleSizes = null;
        _sampleOffsets = Av1PooledArray<long>.Rent(scope, count);
        _sampleSizes = Av1PooledArray<int>.Rent(scope, count);
        _firstLocated = 0;
        _locatedCount = count;
        offsets = _sampleOffsets.Span[..count];
        sizes = _sampleSizes.Span[..count];
    }

    /// <summary>
    /// Makes room for the samples of a movie fragment after the samples that are located, and forgets the samples that
    /// were read: the tables only hold what is still to be read, so their size does not grow with the sequence.
    /// </summary>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="firstNeeded">The index of the first sample that is still to be read.</param>
    /// <param name="count">The number of samples to append.</param>
    /// <param name="offsets">Receives the offsets of the appended samples, to fill.</param>
    /// <param name="sizes">Receives the sizes of the appended samples, to fill.</param>
    /// <param name="durations">Receives the durations of the appended samples, to fill.</param>
    /// <exception cref="ImageResourceLimitException">The tables exceed the live-allocation limit.</exception>
    public void AppendSamples(AllocationScope scope, int firstNeeded, int count, out Span<long> offsets, out Span<int> sizes, out Span<uint> durations)
    {
        firstNeeded = Math.Clamp(firstNeeded, _firstLocated, LocatedSampleCount);
        var kept = LocatedSampleCount - firstNeeded;
        var total = checked(kept + count);

        // The samples that are still to be read are set aside while the tables are replaced
        using var keptOffsets = Av1PooledArray<long>.Rent(scope, kept);
        using var keptSizes = Av1PooledArray<int>.Rent(scope, kept);
        using var keptDurations = Av1PooledArray<uint>.Rent(scope, kept);
        for (var i = 0; i < kept; i++)
        {
            keptOffsets.Span[i] = GetSampleOffset(firstNeeded + i);
            keptSizes.Span[i] = GetSampleSize(firstNeeded + i);
            keptDurations.Span[i] = GetSampleDuration(firstNeeded + i);
        }

        Dispose();
        _firstLocated = firstNeeded;
        _locatedCount = 0;
        _sampleOffsets = Av1PooledArray<long>.Rent(scope, total);
        _sampleSizes = Av1PooledArray<int>.Rent(scope, total);
        _fragmentDurations = Av1PooledArray<uint>.Rent(scope, total);
        keptOffsets.Span[..kept].CopyTo(_sampleOffsets.Span);
        keptSizes.Span[..kept].CopyTo(_sampleSizes.Span);
        keptDurations.Span[..kept].CopyTo(_fragmentDurations.Span);
        _locatedCount = total;
        offsets = _sampleOffsets.Span.Slice(kept, count);
        sizes = _sampleSizes.Span.Slice(kept, count);
        durations = _fragmentDurations.Span.Slice(kept, count);
    }

    public void Dispose()
    {
        _sampleOffsets?.Dispose();
        _sampleOffsets = null;
        _sampleSizes?.Dispose();
        _sampleSizes = null;
        _fragmentDurations?.Dispose();
        _fragmentDurations = null;
    }
}
