using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The image sequence of an AVIF file, resolved from its <c>moov</c> box (ISO/IEC 14496-12, section 8 and the AV1 Image File
/// Format specification, section 3 "Image Sequences"): the track of the color images, the track of their alpha planes, and
/// the number of times the sequence is played.
/// </summary>
internal sealed class AvifMovieBox : IDisposable
{
    // The fields of a visual sample entry that precede its boxes: 8 bytes of sample entry, then 70 bytes
    private const int VisualSampleEntryLength = 78;

    private readonly List<AvifTrack> _tracks = [];
    private uint _timescale;

    private AvifMovieBox()
    {
    }

    /// <summary>Gets the track of the color images.</summary>
    public AvifTrack Color { get; private set; } = null!;

    /// <summary>Gets the track of the alpha planes, or <see langword="null"/> for an opaque sequence.</summary>
    public AvifTrack? Alpha { get; private set; }

    /// <summary>Gets the number of images of the sequence.</summary>
    public long FrameCount => Color.SampleCount;

    /// <summary>
    /// Gets the number of times the sequence is played, or <see langword="null"/> when it repeats forever. The edit list of the
    /// color track gives the duration of one play, and the track header the duration of all of them (AV1 Image File Format
    /// specification, section 3.2). A sequence without edit list, whose number of repetitions is not known, repeats forever.
    /// </summary>
    public int? TotalPlays
    {
        get
        {
            var track = Color;
            if (!track.HasEditList)
                return null;

            if (!track.IsRepeating)
                return 1;

            if (track.Duration is ulong.MaxValue || track.SegmentDuration is 0 or ulong.MaxValue)
                return null;

            // A number of plays that is not a 32-bit number stands for a sequence that repeats forever
            var plays = (track.Duration / track.SegmentDuration) + (track.Duration % track.SegmentDuration is 0 ? 0UL : 1UL);
            return plays > int.MaxValue ? null : (int)Math.Max(plays, 1);
        }
    }

    /// <summary>Resolves the image sequence of a <c>moov</c> box.</summary>
    /// <param name="payload">The payload of the box.</param>
    /// <param name="scope">The allocation scope of the operation, which the sample tables are rented from.</param>
    /// <param name="maxSamples">The number of samples to locate in each track: the frames that the operation may process.</param>
    /// <exception cref="InvalidImageContentException">The description of the sequence is inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The sequence uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The sequence exceeds a resource limit.</exception>
    public static AvifMovieBox Parse(ReadOnlySpan<byte> payload, AllocationScope scope, int maxSamples)
    {
        var movie = new AvifMovieBox();
        try
        {
            var reader = new AvifBoxReader(payload);
            while (reader.TryReadBox(out var type, out var offset, out var box))
            {
                if (type is AvifFormat.Mvhd)
                {
                    var (version, _) = box.ReadFullBoxHeader();
                    box.ReadBytes(version is 1 ? 16 : 8);
                    movie._timescale = box.ReadUInt32();
                    continue;
                }

                if (type is not AvifFormat.Trak)
                    continue;

                var track = new AvifTrack();
                movie._tracks.Add(track);
                ParseTrack(box, offset, track, scope, maxSamples);
            }

            movie.SelectTracks();
            return movie;
        }
        catch
        {
            movie.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var track in _tracks)
        {
            track.Dispose();
        }

        _tracks.Clear();
    }

    private void SelectTracks()
    {
        AvifTrack? color = null;
        foreach (var track in _tracks)
        {
            if (track.HandlerType is AvifFormat.Pict && track.HasAv1SampleEntry && track.SampleCount > 0)
            {
                color = track;
                break;
            }
        }

        Color = color ?? throw AvifFormat.Invalid("the image sequence has no track of AV1 images.");
        if (color.Timescale is 0)
            throw AvifFormat.Invalid("the time scale of the image sequence is zero.");

        // The edit of an image sequence plays every image: an edit that ends before the last image starts selects a part of
        // the track. The duration of the edit is in units of the time scale of the movie
        if (color.HasEditList && color.SegmentDuration is not (0 or ulong.MaxValue) && _timescale is not 0)
        {
            var lastImageStart = color.MediaDuration - Math.Min(color.MediaDuration, color.LastSampleDuration);
            if ((UInt128)color.SegmentDuration * color.Timescale <= (UInt128)lastImageStart * _timescale)
                throw AvifFormat.Unsupported("The AVIF image sequence has an edit list that does not play the whole track.", "edit list");
        }

        foreach (var track in _tracks)
        {
            if (ReferenceEquals(track, color) || !track.HasAv1SampleEntry || !track.AuxiliaryOf.Contains(color.Id) || !track.IsAlpha)
                continue;

            if (color.PremultipliedBy.Contains(track.Id))
                throw AvifFormat.Unsupported("The AVIF image sequence has premultiplied alpha.", "premultiplied alpha");

            if (track.SampleCount != color.SampleCount)
                throw AvifFormat.Invalid("the alpha track does not have the number of images of the color track.");

            if (track.Size != color.Size)
                throw AvifFormat.Unsupported("The alpha plane of the AVIF image sequence does not have the size of the color images.", "alpha plane of a different size");

            Alpha = track;
            break;
        }
    }

    // ISO/IEC 14496-12, section 8.3.1 "Track Box"
    private static void ParseTrack(AvifBoxReader reader, int baseOffset, AvifTrack track, AllocationScope scope, int maxSamples)
    {
        while (reader.TryReadBox(out var type, out var offset, out var box))
        {
            switch (type)
            {
                case AvifFormat.Tkhd:
                {
                    var (version, _) = box.ReadFullBoxHeader();
                    box.ReadBytes(version is 1 ? 16 : 8);
                    track.Id = box.ReadUInt32();
                    box.ReadUInt32();
                    if (version is 1)
                    {
                        track.Duration = box.ReadUInt64();
                    }
                    else
                    {
                        var duration = box.ReadUInt32();
                        track.Duration = duration is uint.MaxValue ? ulong.MaxValue : duration;
                    }

                    break;
                }

                case AvifFormat.Tref:
                    while (box.TryReadBox(out var referenceType, out _, out var reference))
                    {
                        while (!reference.IsEnd)
                        {
                            var id = reference.ReadUInt32();
                            if (referenceType is AvifFormat.Auxl)
                            {
                                track.AuxiliaryOf.Add(id);
                            }
                            else if (referenceType is AvifFormat.Prem)
                            {
                                track.PremultipliedBy.Add(id);
                            }
                        }
                    }

                    break;
                case AvifFormat.Edts:
                    while (box.TryReadBox(out var editType, out _, out var edit))
                    {
                        if (editType is AvifFormat.Elst)
                            ParseEditList(edit, track);
                    }

                    break;
                case AvifFormat.Meta:
                    track.Meta = (baseOffset + offset, box.Remaining);
                    break;
                case AvifFormat.Mdia:
                    ParseMedia(box, baseOffset + offset, track, scope, maxSamples);
                    break;
                default:
                    break;
            }
        }
    }

    // ISO/IEC 14496-12, section 8.6.6 "Edit List Box": the only edit of an image sequence plays the whole track, once or
    // repeatedly
    private static void ParseEditList(AvifBoxReader reader, AvifTrack track)
    {
        var (version, flags) = reader.ReadFullBoxHeader();
        var entryCount = reader.ReadUInt32();
        if (entryCount is not 1)
            throw AvifFormat.Unsupported("The AVIF image sequence has an edit list that does not play the whole track.", "edit list");

        ulong segmentDuration;
        long mediaTime;
        if (version is 1)
        {
            segmentDuration = reader.ReadUInt64();
            mediaTime = (long)reader.ReadUInt64();
        }
        else
        {
            var duration = reader.ReadUInt32();
            segmentDuration = duration is uint.MaxValue ? ulong.MaxValue : duration;
            mediaTime = reader.ReadInt32();
        }

        var mediaRate = reader.ReadUInt32();
        if (mediaTime is not 0 || mediaRate is not 0x00010000)
            throw AvifFormat.Unsupported("The AVIF image sequence has an edit list that does not play the whole track.", "edit list");

        track.HasEditList = true;
        track.IsRepeating = (flags & 1) is not 0;
        track.SegmentDuration = segmentDuration;
    }

    // ISO/IEC 14496-12, section 8.4.1 "Media Box"
    private static void ParseMedia(AvifBoxReader reader, int baseOffset, AvifTrack track, AllocationScope scope, int maxSamples)
    {
        while (reader.TryReadBox(out var type, out var offset, out var box))
        {
            switch (type)
            {
                case AvifFormat.Mdhd:
                {
                    var (version, _) = box.ReadFullBoxHeader();
                    box.ReadBytes(version is 1 ? 16 : 8);
                    track.Timescale = box.ReadUInt32();
                    break;
                }

                case AvifFormat.Hdlr:
                    box.ReadFullBoxHeader();
                    box.ReadUInt32();
                    track.HandlerType = box.ReadUInt32();
                    break;
                case AvifFormat.Minf:
                    while (box.TryReadBox(out var informationType, out var informationOffset, out var information))
                    {
                        if (informationType is AvifFormat.Stbl)
                            ParseSampleTable(information, baseOffset + offset + informationOffset, track, scope, maxSamples);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    // ISO/IEC 14496-12, section 8.5.1 "Sample Table Box"
    private static void ParseSampleTable(AvifBoxReader reader, int baseOffset, AvifTrack track, AllocationScope scope, int maxSamples)
    {
        AvifBoxReader timeToSample = default;
        AvifBoxReader sampleToChunk = default;
        AvifBoxReader sampleSizes = default;
        AvifBoxReader chunkOffsets = default;
        var hasTimeToSample = false;
        var hasSampleToChunk = false;
        uint sampleSizeType = 0;
        uint chunkOffsetType = 0;
        uint descriptionIndex = 0;
        while (reader.TryReadBox(out var type, out var offset, out var box))
        {
            switch (type)
            {
                case AvifFormat.Stsd:
                    descriptionIndex = ParseSampleDescriptions(box, baseOffset + offset, track);
                    break;
                case AvifFormat.Stts:
                    timeToSample = box;
                    hasTimeToSample = true;
                    break;
                case AvifFormat.Stsc:
                    sampleToChunk = box;
                    hasSampleToChunk = true;
                    break;
                case AvifFormat.Stsz or AvifFormat.Stz2:
                    sampleSizes = box;
                    sampleSizeType = type;
                    break;
                case AvifFormat.Stco or AvifFormat.Co64:
                    chunkOffsets = box;
                    chunkOffsetType = type;
                    break;
                default:
                    break;
            }
        }

        // A track that is not coded with AV1 is not part of the image sequence
        if (!track.HasAv1SampleEntry)
            return;

        if (!hasTimeToSample || !hasSampleToChunk || sampleSizeType is 0 || chunkOffsetType is 0)
            throw AvifFormat.Invalid("the sample table of a track is not complete.");

        // The sizes of the samples (sections 8.7.3.2 "Sample Size Box" and 8.7.3.3 "Compact Sample Size Box")
        sampleSizes.ReadFullBoxHeader();
        uint constantSize = 0;
        var fieldSize = 32;
        if (sampleSizeType is AvifFormat.Stsz)
        {
            constantSize = sampleSizes.ReadUInt32();
        }
        else
        {
            sampleSizes.ReadBytes(3);
            fieldSize = sampleSizes.ReadByte();
            if (fieldSize is not (4 or 8 or 16))
                throw AvifFormat.Invalid("the sample sizes of a track are not valid.");
        }

        var sampleCount = sampleSizes.ReadUInt32();
        track.SampleCount = sampleCount;
        var located = (int)Math.Min(sampleCount, (uint)maxSamples);
        track.AllocateSamples(scope, located, out var offsets, out var sizes);
        var nibbles = 0;
        for (var i = 0; i < located; i++)
        {
            uint size;
            if (constantSize is not 0)
            {
                size = constantSize;
            }
            else if (fieldSize is 4)
            {
                // Two sizes in a byte, the first one in its high bits
                if ((i & 1) is 0)
                    nibbles = sampleSizes.ReadByte();

                size = (uint)((i & 1) is 0 ? nibbles >> 4 : nibbles & 0xF);
            }
            else if (fieldSize is 8)
            {
                size = sampleSizes.ReadByte();
            }
            else
            {
                size = fieldSize is 16 ? sampleSizes.ReadUInt16() : sampleSizes.ReadUInt32();
            }

            if (size is 0 || size > CheckedSizes.MaxBufferLength)
                throw AvifFormat.Invalid("the size of a sample is not valid.");

            sizes[i] = (int)size;
        }

        // The durations of the samples (section 8.6.1.2 "Decoding Time to Sample Box")
        timeToSample.ReadFullBoxHeader();
        var durationCount = timeToSample.ReadUInt32();
        long covered = 0;
        ulong mediaDuration = 0;
        for (uint i = 0; i < durationCount; i++)
        {
            var count = timeToSample.ReadUInt32();
            var delta = timeToSample.ReadUInt32();
            if (count is 0)
                continue;

            // Only the durations of the located samples are kept; the others take part in the duration of the track
            if (covered < located)
                track.Durations.Add((count, delta));

            covered += count;
            mediaDuration += (ulong)count * delta;
            track.LastSampleDuration = delta;
        }

        track.MediaDuration = mediaDuration;

        if (covered < located)
            throw AvifFormat.Invalid("the sample table of a track does not give the duration of every sample.");

        // The location of the samples (sections 8.7.4 "Sample To Chunk Box" and 8.7.5 "Chunk Offset Box")
        sampleToChunk.ReadFullBoxHeader();
        var runCount = sampleToChunk.ReadUInt32();
        chunkOffsets.ReadFullBoxHeader();
        var chunkCount = chunkOffsets.ReadUInt32();
        uint samplesPerChunk = 0;
        uint runDescriptionIndex = descriptionIndex;
        uint runsRead = 0;
        ReadNextRun(ref sampleToChunk, out var nextFirstChunk, out var nextSamplesPerChunk, out var nextDescriptionIndex);
        var sample = 0;
        for (uint chunk = 1; chunk <= chunkCount && sample < located; chunk++)
        {
            while (nextFirstChunk is not 0 && nextFirstChunk <= chunk)
            {
                samplesPerChunk = nextSamplesPerChunk;
                runDescriptionIndex = nextDescriptionIndex;
                ReadNextRun(ref sampleToChunk, out nextFirstChunk, out nextSamplesPerChunk, out nextDescriptionIndex);
            }

            var chunkOffset = chunkOffsets.ReadUInt(chunkOffsetType is AvifFormat.Stco ? 4 : 8);
            if (chunkOffset > long.MaxValue)
                throw AvifFormat.Invalid("the offset of a chunk is not valid.");

            if (samplesPerChunk is not 0 && runDescriptionIndex != descriptionIndex)
                throw AvifFormat.Unsupported("The AVIF image sequence has several sample descriptions.", "several sample descriptions");

            var offset = (long)chunkOffset;
            for (uint i = 0; i < samplesPerChunk && sample < located; i++)
            {
                offsets[sample] = offset;
                if (offset > long.MaxValue - sizes[sample])
                    throw AvifFormat.Invalid("the offset of a chunk is not valid.");

                offset += sizes[sample];
                sample++;
            }
        }

        if (sample < located)
            throw AvifFormat.Invalid("the sample table of a track does not locate every sample.");

        // Reads the next run of chunks with the same number of samples; the first chunk of a run is 0 after the last one
        void ReadNextRun(ref AvifBoxReader runs, out uint firstChunk, out uint samples, out uint description)
        {
            firstChunk = 0;
            samples = 0;
            description = 0;
            if (runsRead == runCount)
                return;

            runsRead++;
            firstChunk = runs.ReadUInt32();
            samples = runs.ReadUInt32();
            description = runs.ReadUInt32();
            if (firstChunk is 0)
                throw AvifFormat.Invalid("the sample table of a track is not valid.");
        }
    }

    // ISO/IEC 14496-12, section 8.5.2 "Sample Description Box"; returns the index of the AV1 sample entry
    private static uint ParseSampleDescriptions(AvifBoxReader reader, int baseOffset, AvifTrack track)
    {
        reader.ReadFullBoxHeader();
        var entryCount = reader.ReadUInt32();
        for (uint index = 1; index <= entryCount; index++)
        {
            if (!reader.TryReadBox(out var type, out var offset, out var entry))
                throw AvifFormat.Invalid("the sample descriptions of a track are not complete.");

            if (type is not AvifFormat.Av01 || track.HasAv1SampleEntry)
                continue;

            // SampleEntry then VisualSampleEntry (section 12.1.3)
            entry.ReadBytes(24);
            var width = entry.ReadUInt16();
            var height = entry.ReadUInt16();
            entry.ReadBytes(VisualSampleEntryLength - 28);
            if (width is 0 || height is 0)
                throw AvifFormat.Invalid("the size of the images of a track is zero.");

            track.Size = new Size(width, height);
            var hasConfiguration = false;
            while (entry.TryReadBox(out var childType, out var childOffset, out var child))
            {
                switch (childType)
                {
                    case AvifFormat.Av1C:
                    {
                        // AV1 Codec ISO Media File Format Binding, section 2.3 "AV1 Codec Configuration Box"
                        if (child.ReadByte() is not 0x81)
                            throw AvifFormat.Invalid("the AV1 configuration record is not valid.");

                        child.ReadByte();
                        var flags = child.ReadByte();
                        var highBitDepth = (flags & 0x40) is not 0;
                        var twelveBit = (flags & 0x20) is not 0;
                        track.BitDepth = highBitDepth ? (twelveBit ? 12 : 10) : 8;
                        track.IsMonochrome = (flags & 0x10) is not 0;
                        track.SubsamplingX = (flags >> 3) & 1;
                        track.SubsamplingY = (flags >> 2) & 1;
                        hasConfiguration = true;
                        break;
                    }

                    case AvifFormat.Colr:
                    {
                        var colorType = child.ReadUInt32();
                        if (colorType is AvifFormat.Nclx && track.Nclx is null)
                        {
                            var primaries = child.ReadUInt16();
                            var transfer = child.ReadUInt16();
                            var matrix = child.ReadUInt16();
                            var fullRange = (child.ReadByte() & 0x80) is not 0;
                            if (primaries > 255 || transfer > 255 || matrix > 255)
                                throw AvifFormat.Invalid("a color description code point is out of range.");

                            track.Nclx = new CicpColorDescription((byte)primaries, (byte)transfer, (byte)matrix, fullRange);
                        }
                        else if (colorType is AvifFormat.Prof or AvifFormat.RICC && track.IccProfile.Length is 0)
                        {
                            track.IccProfile = (baseOffset + offset + childOffset + child.Position, child.Remaining);
                        }

                        break;
                    }

                    case AvifFormat.Auxi:
                        child.ReadFullBoxHeader();
                        track.AuxiliaryType = child.ReadCString().ToArray();
                        break;
                    default:
                        break;
                }
            }

            if (!hasConfiguration)
                throw AvifFormat.Invalid("an AV1 track has no configuration record.");

            track.HasAv1SampleEntry = true;
            return index;
        }

        return 0;
    }
}
