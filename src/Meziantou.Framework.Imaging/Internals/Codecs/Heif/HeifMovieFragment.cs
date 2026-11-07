namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A movie fragment of an AVIF image sequence (ISO/IEC 14496-12, section 8.8 "Movie Fragments"): the <c>moof</c> box
/// describes more samples of the tracks of the movie, which follow the samples that are already known. Its samples are
/// appended to the tables of the color track and of the alpha track; the other tracks are skipped.
/// </summary>
/// <remarks>
/// <para>
/// A track fragment (<c>traf</c>) gives the base of its data offsets in its header (<c>tfhd</c>, section 8.8.7): an offset
/// in the file, or the start of the <c>moof</c> box (<c>default-base-is-moof</c>); without either, the start of the
/// <c>moof</c> box for the first track fragment and the end of the data of the previous one for the next ones. Each run
/// (<c>trun</c>, section 8.8.8) starts at the base plus its data offset, or follows the previous run, and its samples
/// follow each other. The duration and the size of a sample are those of its run, else the defaults of the track
/// fragment, else the defaults of the movie (<c>trex</c>).
/// </para>
/// <para>
/// The fragments of an image sequence follow each other: the decode time of a track fragment (<c>tfdt</c>) is the sum of
/// the durations of the samples before it, and its samples are shown in the order they are decoded. A fragment that
/// says otherwise (a gap, composition time offsets, an empty duration, another sample description) is not supported.
/// </para>
/// </remarks>
internal static class HeifMovieFragment
{
    private const uint BaseDataOffsetPresent = 0x000001;
    private const uint SampleDescriptionIndexPresent = 0x000002;
    private const uint DefaultSampleDurationPresent = 0x000008;
    private const uint DefaultSampleSizePresent = 0x000010;
    private const uint DefaultSampleFlagsPresent = 0x000020;
    private const uint DurationIsEmpty = 0x010000;
    private const uint DefaultBaseIsMoof = 0x020000;

    private const uint DataOffsetPresent = 0x000001;
    private const uint FirstSampleFlagsPresent = 0x000004;
    private const uint SampleDurationPresent = 0x000100;
    private const uint SampleSizePresent = 0x000200;
    private const uint SampleFlagsPresent = 0x000400;
    private const uint SampleCompositionTimeOffsetsPresent = 0x000800;

    /// <summary>Appends the samples of a movie fragment to the tracks of the sequence.</summary>
    /// <param name="payload">The payload of the <c>moof</c> box.</param>
    /// <param name="movieFragmentOffset">The offset of the <c>moof</c> box in the file.</param>
    /// <param name="movie">The image sequence.</param>
    /// <param name="scope">The allocation scope of the operation, which the sample tables are rented from.</param>
    /// <param name="firstNeeded">The index of the first image that is still to be read: the samples before it are forgotten.</param>
    /// <param name="maxSamples">The number of samples to locate in each track: the frames that the operation may process.</param>
    /// <exception cref="InvalidImageContentException">The fragment is not valid.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The fragment uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The sample tables exceed the live-allocation limit.</exception>
    public static void Parse(ReadOnlySpan<byte> payload, long movieFragmentOffset, HeifMovieBox movie, AllocationScope scope, int firstNeeded, int maxSamples)
    {
        var reader = new HeifBoxReader(payload, movie.Binding);
        var previousEnd = movieFragmentOffset;
        var isFirst = true;
        while (reader.TryReadBox(out var type, out _, out var box))
        {
            if (type is not HeifFormat.Traf)
                continue;

            previousEnd = ParseTrackFragment(box, movieFragmentOffset, isFirst ? movieFragmentOffset : previousEnd, movie, scope, firstNeeded, maxSamples);
            isFirst = false;
        }
    }

    // Returns the offset that follows the data of the track fragment
    private static long ParseTrackFragment(HeifBoxReader reader, long movieFragmentOffset, long implicitBase, HeifMovieBox movie, AllocationScope scope, int firstNeeded, int maxSamples)
    {
        var binding = reader.Binding;
        // The header comes first; the number of samples of the runs is needed before the tables are rented
        HeifTrack? track = null;
        var hasHeader = false;
        var baseOffset = implicitBase;
        uint defaultDuration = 0;
        uint defaultSize = 0;
        long sampleCount = 0;
        var boxes = reader;
        while (boxes.TryReadBox(out var type, out _, out var box))
        {
            if (type is HeifFormat.Tfhd)
            {
                if (hasHeader)
                    throw binding.Invalid("a track fragment has two headers.");

                hasHeader = true;
                var flags = box.ReadUInt32() & 0xFFFFFF;
                var trackId = box.ReadUInt32();
                track = movie.FindTrack(trackId);
                var extends = movie.FindAnyTrack(trackId);
                if (extends is not { HasTrackExtends: true })
                    throw binding.Invalid("a track fragment belongs to a track that the movie does not extend.");

                (defaultDuration, defaultSize) = (extends.DefaultSampleDuration, extends.DefaultSampleSize);
                var descriptionIndex = extends.DefaultSampleDescriptionIndex;
                if ((flags & BaseDataOffsetPresent) is not 0)
                {
                    var offset = box.ReadUInt64();
                    if (offset > long.MaxValue)
                        throw binding.Invalid("the base data offset of a track fragment is not valid.");

                    baseOffset = (long)offset;
                }
                else if ((flags & DefaultBaseIsMoof) is not 0)
                {
                    baseOffset = movieFragmentOffset;
                }

                if ((flags & SampleDescriptionIndexPresent) is not 0)
                    descriptionIndex = box.ReadUInt32();

                if ((flags & DefaultSampleDurationPresent) is not 0)
                    defaultDuration = box.ReadUInt32();

                if ((flags & DefaultSampleSizePresent) is not 0)
                    defaultSize = box.ReadUInt32();

                if ((flags & DefaultSampleFlagsPresent) is not 0)
                    box.ReadUInt32();

                if (track is not null)
                {
                    if (descriptionIndex != track.SampleDescriptionIndex)
                        throw binding.Unsupported($"The {binding.Name} image sequence has several sample descriptions.", "several sample descriptions");

                    if ((flags & DurationIsEmpty) is not 0)
                        throw binding.Unsupported($"A fragment of the {binding.Name} image sequence is an empty duration.", "fragmented image sequence with a gap");
                }
            }
            else if (type is HeifFormat.Trun)
            {
                if (!hasHeader)
                    throw binding.Invalid("a track fragment has no header before its runs.");

                box.ReadUInt32();
                sampleCount += box.ReadUInt32();
            }
        }

        if (!hasHeader)
            throw binding.Invalid("a track fragment has no header.");

        // The samples that are located: the others, beyond the frame limit of the operation, only count
        var located = 0;
        Span<long> offsets = default;
        Span<int> sizes = default;
        Span<uint> durations = default;
        if (track is not null)
        {
            located = (int)Math.Min(sampleCount, Math.Max(0L, maxSamples - (long)track.LocatedSampleCount));
            if (track.SampleCount != track.LocatedSampleCount)
                located = 0;

            track.AppendSamples(scope, firstNeeded, located, out offsets, out sizes, out durations);
        }

        var next = baseOffset;
        var index = 0;
        ulong duration = 0;
        while (reader.TryReadBox(out var type, out _, out var box))
        {
            if (type is HeifFormat.Tfdt && track is not null)
            {
                // The decode time of the first sample of the fragment: the samples of the track follow each other
                var (version, _) = box.ReadFullBoxHeader();
                var decodeTime = version is 1 ? box.ReadUInt64() : box.ReadUInt32();
                if (decodeTime != track.MediaDuration)
                    throw binding.Unsupported($"The fragments of the {binding.Name} image sequence do not follow each other.", "fragmented image sequence with a gap");
            }
            else if (type is HeifFormat.Trun)
            {
                var flags = box.ReadUInt32() & 0xFFFFFF;
                var count = box.ReadUInt32();
                if ((flags & DataOffsetPresent) is not 0)
                {
                    // The base is a valid offset: a positive data offset may take it past the largest one, a negative one
                    // before the start of the file
                    var dataOffset = box.ReadInt32();
                    if (dataOffset > 0 ? baseOffset > long.MaxValue - dataOffset : baseOffset + dataOffset < 0)
                        throw binding.Invalid("the data offset of a track run is out of range.");

                    next = baseOffset + dataOffset;
                }

                if ((flags & FirstSampleFlagsPresent) is not 0)
                    box.ReadUInt32();

                var hasFields = (flags & (SampleDurationPresent | SampleSizePresent | SampleFlagsPresent | SampleCompositionTimeOffsetsPresent)) is not 0;
                for (uint i = 0; i < count; i++)
                {
                    // A run without field for its samples declares any number of them in a few bytes: the ones that are
                    // not located are counted without a loop
                    if (!hasFields && (track is null || index >= located))
                    {
                        var remaining = count - i;
                        if (defaultSize is 0 || (ulong)defaultSize * remaining > (ulong)(long.MaxValue - next))
                            throw binding.Invalid("the size of a sample is not valid.");

                        next += (long)((ulong)defaultSize * remaining);
                        duration += (ulong)defaultDuration * remaining;
                        break;
                    }

                    var sampleDuration = (flags & SampleDurationPresent) is not 0 ? box.ReadUInt32() : defaultDuration;
                    var size = (flags & SampleSizePresent) is not 0 ? box.ReadUInt32() : defaultSize;
                    if ((flags & SampleFlagsPresent) is not 0)
                        box.ReadUInt32();

                    if ((flags & SampleCompositionTimeOffsetsPresent) is not 0 && box.ReadUInt32() is not 0 && track is not null)
                        throw binding.Unsupported($"The samples of the {binding.Name} image sequence are not shown in the order they are decoded.", "composition time offsets");

                    if (size is 0 || size > CheckedSizes.MaxBufferLength || next > long.MaxValue - size)
                        throw binding.Invalid("the size of a sample is not valid.");

                    if (track is not null && index < located)
                    {
                        offsets[index] = next;
                        sizes[index] = (int)size;
                        durations[index] = sampleDuration;
                    }

                    index++;
                    next += size;
                    duration += sampleDuration;
                }
            }
        }

        if (track is not null)
        {
            track.SampleCount += sampleCount;
            track.MediaDuration += duration;
        }

        return next;
    }
}
