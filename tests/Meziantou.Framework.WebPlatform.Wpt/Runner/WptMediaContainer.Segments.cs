using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The byte stream formats of the Media Source Extensions (https://www.w3.org/TR/mse-byte-stream-format-registry/) of the
// media backend of the tests: the initialization segments and the media segments of the ISO BMFF (fragmented MP4), WebM and
// MPEG audio streams are split, and the timestamps, the durations and the key frame flags of their coded frames are read
// from the container. Nothing is decoded.
internal static partial class WptMediaContainer
{
    /// <summary>Creates the parser of the byte stream format of a container (<c>mp4</c>, <c>webm</c>, <c>mpeg</c> or <c>aac</c>), or null.</summary>
    /// <param name="container">The container.</param>
    /// <param name="isCodecSupported">Whether the codec of a track of an initialization segment is supported (the sample entry type of MP4, the codec ID of WebM).</param>
    public static MediaSegmentParser? CreateSegmentParser(string container, Func<string, bool> isCodecSupported) => container switch
    {
        "mp4" => new Mp4SegmentParser(isCodecSupported),
        "webm" => new WebMSegmentParser(isCodecSupported),
        "mpeg" => new MpegAudioSegmentParser(isAdts: false),
        "aac" => new MpegAudioSegmentParser(isAdts: true),
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------------------------
    // ISO BMFF byte stream format (https://www.w3.org/TR/mse-byte-stream-format-isobmff/)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// An initialization segment is a movie box (<c>moov</c>), after a file type box; a media segment is a movie fragment
    /// box (<c>moof</c>) followed by its media data box (<c>mdat</c>). The other valid top-level boxes are ignored.
    /// </summary>
    private sealed class Mp4SegmentParser(Func<string, bool> isCodecSupported) : MediaSegmentParser
    {
        private sealed class FragmentTrack
        {
            public bool IsVideo { get; init; }
            public double Timescale { get; init; }

            /// <summary>The offset of the presentation timestamps, in seconds: the edit list of the track (the empty edits delay the track, the media time of the first edit is skipped).</summary>
            public double PresentationOffset { get; init; }

            // trex: the defaults of the samples of the fragments
            public uint DefaultDuration { get; set; }
            public uint DefaultFlags { get; set; }

            /// <summary>The decode time of the next sample, for a fragment without <c>tfdt</c> box.</summary>
            public double NextDecodeTime { get; set; }
        }

        private readonly Dictionary<uint, FragmentTrack> _tracks = [];

        public override MediaSegmentParseResult Parse(ReadOnlyMemory<byte> input)
        {
            var data = input.Span;
            if (!TryReadBoxHeader(data, 0, out var type, out var headerSize, out var size, out var isValid))
                return isValid ? MediaSegmentParseResult.NeedMoreData() : MediaSegmentParseResult.Error("Invalid box");

            if (!IsValidTopLevelBox(type))
                return MediaSegmentParseResult.Error("Unrecognized top-level box");

            if (type.SequenceEqual("moof"u8))
            {
                // A media segment: the movie fragment and its media data
                if (_tracks.Count == 0 || size > data.Length)
                    return MediaSegmentParseResult.NeedMoreData(isParsingMediaSegment: true);

                if (!TryReadBoxHeader(data, (int)size, out var dataType, out _, out var dataSize, out isValid))
                    return isValid ? MediaSegmentParseResult.NeedMoreData(isParsingMediaSegment: true) : MediaSegmentParseResult.Error("Invalid box");

                if (!dataType.SequenceEqual("mdat"u8))
                    return MediaSegmentParseResult.Error("A movie fragment box must be followed by a media data box");

                if (size + dataSize > data.Length)
                    return MediaSegmentParseResult.NeedMoreData(isParsingMediaSegment: true);

                var frames = new List<MediaCodedFrame>();
                var fragment = data[headerSize..(int)size];
                var offset = 0;
                while (TryReadBox(fragment, ref offset, out var childType, out var traf))
                {
                    if (childType.SequenceEqual("traf"u8))
                    {
                        ReadTrackFragment(traf, frames);
                    }
                }

                return MediaSegmentParseResult.Media((int)(size + dataSize), Interleave(frames));
            }

            if (size > data.Length)
                return MediaSegmentParseResult.NeedMoreData();

            if (type.SequenceEqual("moov"u8))
                return ReadMovie(data, data[headerSize..(int)size], (int)size);

            return MediaSegmentParseResult.Ignore((int)size);
        }

        /// <summary>
        /// The samples of the tracks of a fragment are interleaved in the media data: the frames of each track stay in decode
        /// order, and the next frame is the one of the track whose next frame is presented first.
        /// </summary>
        private static List<MediaCodedFrame> Interleave(List<MediaCodedFrame> frames)
        {
            var queues = new List<Queue<MediaCodedFrame>>();
            var tracks = new Dictionary<long, Queue<MediaCodedFrame>>();
            foreach (var frame in frames)
            {
                if (!tracks.TryGetValue(frame.TrackId, out var queue))
                {
                    queue = new Queue<MediaCodedFrame>();
                    tracks.Add(frame.TrackId, queue);
                    queues.Add(queue);
                }

                queue.Enqueue(frame);
            }

            if (queues.Count <= 1)
                return frames;

            var result = new List<MediaCodedFrame>(frames.Count);
            while (result.Count < frames.Count)
            {
                Queue<MediaCodedFrame>? next = null;
                foreach (var queue in queues)
                {
                    if (queue.Count > 0 && (next is null || queue.Peek().PresentationTimestamp < next.Peek().PresentationTimestamp))
                    {
                        next = queue;
                    }
                }

                result.Add(next!.Dequeue());
            }

            return result;
        }

        /// <summary>Reads the header of the box at an offset. <paramref name="isValid"/> is false when the header cannot be the one of a box.</summary>
        private static bool TryReadBoxHeader(ReadOnlySpan<byte> data, int offset, out ReadOnlySpan<byte> type, out int headerSize, out long size, out bool isValid)
        {
            type = default;
            headerSize = 8;
            size = 0;
            isValid = true;
            if (data.Length - offset < 8)
                return false;

            size = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            type = data.Slice(offset + 4, 4);
            if (size == 1)
            {
                if (data.Length - offset < 16)
                    return false;

                var largeSize = BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]);
                headerSize = 16;
                size = largeSize > int.MaxValue ? -1 : (long)largeSize;
            }

            // A box that extends to the end of the file (size 0) has no end in a stream
            if (size < headerSize || size > int.MaxValue / 2)
            {
                isValid = false;
                return false;
            }

            return true;
        }

        /// <summary>The top-level boxes of ISO/IEC 14496-12 that can be in a stream: another type is corrupted data.</summary>
        private static bool IsValidTopLevelBox(ReadOnlySpan<byte> type)
        {
            return type.SequenceEqual("ftyp"u8) || type.SequenceEqual("styp"u8) || type.SequenceEqual("moov"u8) || type.SequenceEqual("moof"u8)
                || type.SequenceEqual("mdat"u8) || type.SequenceEqual("sidx"u8) || type.SequenceEqual("ssix"u8) || type.SequenceEqual("free"u8)
                || type.SequenceEqual("skip"u8) || type.SequenceEqual("emsg"u8) || type.SequenceEqual("prft"u8) || type.SequenceEqual("pdin"u8)
                || type.SequenceEqual("meta"u8) || type.SequenceEqual("mfra"u8) || type.SequenceEqual("uuid"u8) || type.SequenceEqual("bloc"u8);
        }

        private MediaSegmentParseResult ReadMovie(ReadOnlySpan<byte> file, ReadOnlySpan<byte> movie, int size)
        {
            var tracks = new List<MediaSegmentTrack>();
            var fragmentTracks = new Dictionary<uint, FragmentTrack>();
            var defaults = new Dictionary<uint, (uint Duration, uint Flags)>();
            var movieTimescale = 0d;
            var movieDuration = 0d;
            var fragmentDuration = 0d;
            var offset = 0;
            while (TryReadBox(movie, ref offset, out var type, out var child))
            {
                if (type.SequenceEqual("mvhd"u8))
                {
                    var isVersion1 = child.Length > 0 && child[0] == 1;
                    movieTimescale = ReadBigEndian(child, isVersion1 ? 20 : 12, 4);
                    movieDuration = ReadMp4Duration(child, isVersion1 ? 24 : 16, isVersion1);
                }
                else if (type.SequenceEqual("mvex"u8))
                {
                    var mehd = FindBox(child, "mehd"u8);
                    fragmentDuration = ReadMp4Duration(mehd, 4, mehd.Length > 0 && mehd[0] == 1);

                    // trex: version, flags, track_ID, default_sample_description_index, default_sample_duration,
                    // default_sample_size, default_sample_flags
                    var extendsOffset = 0;
                    while (TryReadBox(child, ref extendsOffset, out var extendsType, out var trex))
                    {
                        if (extendsType.SequenceEqual("trex"u8) && trex.Length >= 24 && defaults.Count < MaxTracks)
                        {
                            defaults[(uint)ReadBigEndian(trex, 4, 4)] = ((uint)ReadBigEndian(trex, 12, 4), (uint)ReadBigEndian(trex, 20, 4));
                        }
                    }
                }
            }

            offset = 0;
            while (TryReadBox(movie, ref offset, out var type, out var trak))
            {
                if (!type.SequenceEqual("trak"u8) || fragmentTracks.Count >= MaxTracks)
                    continue;

                var track = ParseMp4Track(file, trak);
                if (!track.IsVideo && !track.IsAudio)
                    continue;

                if (track.Timescale == 0)
                    return MediaSegmentParseResult.Error("A track has no timescale");

                if (!isCodecSupported(track.Codec))
                    return MediaSegmentParseResult.Error($"The codec '{track.Codec}' is not supported");

                // As Chromium does, the tracks are the main tracks and have no label
                tracks.Add(new MediaSegmentTrack(track.Id, track.IsVideo, "main", "", track.Language, track.IsVideo ? track.Width : 0, track.IsVideo ? track.Height : 0));
                var (defaultDuration, defaultFlags) = defaults.GetValueOrDefault(track.Id);
                fragmentTracks[track.Id] = new FragmentTrack
                {
                    IsVideo = track.IsVideo,
                    Timescale = track.Timescale,
                    PresentationOffset = ReadPresentationOffset(trak, movieTimescale, track.Timescale),
                    DefaultDuration = defaultDuration,
                    DefaultFlags = defaultFlags,
                };
            }

            _tracks.Clear();
            foreach (var (id, track) in fragmentTracks)
            {
                _tracks[id] = track;
            }

            var duration = double.NaN;
            if (movieTimescale > 0 && (fragmentDuration > 0 || movieDuration > 0))
            {
                duration = (fragmentDuration > 0 ? fragmentDuration : movieDuration) / movieTimescale;
            }

            return MediaSegmentParseResult.Initialization(size, new MediaInitializationSegment(tracks, duration));
        }

        /// <summary>
        /// Reads the edit list of a track (<c>elst</c>): the empty edits at the start delay the presentation of the track (their
        /// duration is in the timescale of the movie), and the presentation starts at the media time of the first edit.
        /// </summary>
        private static double ReadPresentationOffset(ReadOnlySpan<byte> trak, double movieTimescale, double trackTimescale)
        {
            // elst: version, flags, entry_count, then (segment_duration, media_time: 4 or 8 bytes each, media_rate: 4)
            var elst = FindBox(FindBox(trak, "edts"u8), "elst"u8);
            if (elst.Length < 8)
                return 0;

            var isVersion1 = elst[0] == 1;
            var entrySize = isVersion1 ? 20 : 12;
            var count = (int)Math.Min(ReadBigEndian(elst, 4, 4), (ulong)((elst.Length - 8) / entrySize));
            var result = 0d;
            for (var i = 0; i < count; i++)
            {
                var entry = elst.Slice(8 + (i * entrySize), entrySize);
                var duration = (double)ReadBigEndian(entry, 0, isVersion1 ? 8 : 4);
                var mediaTime = isVersion1 ? (long)ReadBigEndian(entry, 8, 8) : (int)ReadBigEndian(entry, 4, 4);
                if (mediaTime < 0)
                {
                    // An empty edit
                    result += movieTimescale > 0 ? duration / movieTimescale : 0;
                    continue;
                }

                return result - (mediaTime / trackTimescale);
            }

            return result;
        }

        private void ReadTrackFragment(ReadOnlySpan<byte> traf, List<MediaCodedFrame> frames)
        {
            // tfhd: version, flags, track_ID, then the fields selected by the flags: base_data_offset (0x1, 8 bytes),
            // sample_description_index (0x2), default_sample_duration (0x8), default_sample_size (0x10),
            // default_sample_flags (0x20)
            var tfhd = FindBox(traf, "tfhd"u8);
            var trackId = (uint)ReadBigEndian(tfhd, 4, 4);
            if (!_tracks.TryGetValue(trackId, out var track))
                return;

            var headerFlags = ReadBigEndian(tfhd, 1, 3);
            var position = 8 + ((headerFlags & 0x1) != 0 ? 8 : 0) + ((headerFlags & 0x2) != 0 ? 4 : 0);
            var defaultDuration = track.DefaultDuration;
            var defaultFlags = track.DefaultFlags;
            if ((headerFlags & 0x8) != 0)
            {
                defaultDuration = (uint)ReadBigEndian(tfhd, position, 4);
                position += 4;
            }

            if ((headerFlags & 0x10) != 0)
            {
                position += 4;
            }

            if ((headerFlags & 0x20) != 0)
            {
                defaultFlags = (uint)ReadBigEndian(tfhd, position, 4);
            }

            // tfdt: version, flags, baseMediaDecodeTime (4 or 8 bytes); without it the fragment follows the previous one
            var tfdt = FindBox(traf, "tfdt"u8);
            var time = track.NextDecodeTime;
            if (tfdt.Length >= 8)
            {
                time = ReadBigEndian(tfdt, 4, tfdt[0] == 1 ? 8 : 4);
            }

            var offset = 0;
            while (TryReadBox(traf, ref offset, out var type, out var trun))
            {
                if (!type.SequenceEqual("trun"u8) || trun.Length < 8)
                    continue;

                // trun: version, flags, sample_count, data_offset (0x1), first_sample_flags (0x4), then for each sample:
                // duration (0x100), size (0x200), flags (0x400), composition time offset (0x800, signed in version 1)
                var flags = ReadBigEndian(trun, 1, 3);
                var sampleCount = (long)ReadBigEndian(trun, 4, 4);
                position = 8 + ((flags & 0x1) != 0 ? 4 : 0);
                uint? firstSampleFlags = null;
                if ((flags & 0x4) != 0)
                {
                    firstSampleFlags = (uint)ReadBigEndian(trun, position, 4);
                    position += 4;
                }

                var recordSize = 4 * System.Numerics.BitOperations.PopCount((uint)(flags & 0xF00));
                if (recordSize > 0)
                {
                    sampleCount = Math.Min(sampleCount, Math.Max(0, (trun.Length - position) / recordSize));
                }

                sampleCount = Math.Min(sampleCount, MaxSamples);
                for (var i = 0L; i < sampleCount; i++)
                {
                    var field = position + (int)(i * recordSize);
                    var duration = defaultDuration;
                    var sampleFlags = i == 0 && firstSampleFlags is { } first ? first : defaultFlags;
                    var compositionOffset = 0L;
                    if ((flags & 0x100) != 0)
                    {
                        duration = (uint)ReadBigEndian(trun, field, 4);
                        field += 4;
                    }

                    if ((flags & 0x200) != 0)
                    {
                        field += 4;
                    }

                    if ((flags & 0x400) != 0)
                    {
                        sampleFlags = (uint)ReadBigEndian(trun, field, 4);
                        field += 4;
                    }

                    if ((flags & 0x800) != 0)
                    {
                        var value = (uint)ReadBigEndian(trun, field, 4);
                        compositionOffset = trun[0] == 0 ? value : (int)value;
                    }

                    // sample_is_non_sync_sample: the bit 16 of the sample flags
                    var isKeyFrame = !track.IsVideo || (sampleFlags & 0x10000) == 0;
                    frames.Add(new MediaCodedFrame(trackId, ((time + compositionOffset) / track.Timescale) + track.PresentationOffset, (time / track.Timescale) + track.PresentationOffset, duration / track.Timescale, isKeyFrame));
                    time += duration;
                }
            }

            track.NextDecodeTime = time;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // WebM byte stream format (https://www.w3.org/TR/mse-byte-stream-format-webm/)
    // ---------------------------------------------------------------------------------------------------------------

    private const uint EbmlHeader = 0x1A45DFA3;
    private const uint EbmlVoid = 0xEC;
    private const uint MatroskaSeekHead = 0x114D9B74;
    private const uint MatroskaCues = 0x1C53BB6B;
    private const uint MatroskaChapters = 0x1043A770;
    private const uint MatroskaTags = 0x1254C367;
    private const uint MatroskaAttachments = 0x1941A469;
    private const uint MatroskaReferenceBlock = 0xFB;

    /// <summary>
    /// An initialization segment is the EBML header, the header of the Segment element and its elements up to the Tracks
    /// element; a media segment is a Cluster element. The blocks of a cluster are read when the cluster is complete: the
    /// duration of a block without <c>BlockDuration</c> is the time to the next block of its track (the default duration of
    /// the track, or the duration of the previous block, for the last one).
    /// </summary>
    private sealed class WebMSegmentParser(Func<string, bool> isCodecSupported) : MediaSegmentParser
    {
        private sealed class ClusterTrack
        {
            public bool IsVideo { get; init; }

            /// <summary>The default duration of the frames in seconds, or 0.</summary>
            public double DefaultDuration { get; init; }

            /// <summary>The duration of the last block of the track: the estimated duration of the last block of a cluster.</summary>
            public double LastDuration { get; set; }
        }

        private readonly Dictionary<ulong, ClusterTrack> _tracks = [];

        /// <summary>The duration of a tick of the timestamps, in seconds.</summary>
        private double _timestampScale = 0.001;

        public override MediaSegmentParseResult Parse(ReadOnlyMemory<byte> input)
        {
            var data = input.Span;
            var offset = 0;
            if (!TryReadElementHeader(data, ref offset, out var id, out var size, out var isValid))
                return isValid ? MediaSegmentParseResult.NeedMoreData() : MediaSegmentParseResult.Error("Invalid EBML element");

            switch (id)
            {
                case EbmlHeader:
                    return ReadInitializationSegment(data);

                case MatroskaCluster:
                    // A cluster of unknown size ends at the next cluster: it is not supported by this parser
                    if (size < 0)
                        return MediaSegmentParseResult.Error("A cluster of unknown size is not supported");

                    if (_tracks.Count == 0 || size > data.Length - offset)
                        return MediaSegmentParseResult.NeedMoreData(isParsingMediaSegment: true);

                    // A block of a track that is not in the initialization segment is an error
                    return ReadCluster(data.Slice(offset, (int)size)) is { } frames
                        ? MediaSegmentParseResult.Media(offset + (int)size, frames)
                        : MediaSegmentParseResult.Error("A block has an unknown track number");

                case MatroskaSegment:
                    // The header of a segment: its elements follow
                    return MediaSegmentParseResult.Ignore(offset);

                case MatroskaInfo or MatroskaTracks or MatroskaSeekHead or MatroskaCues or MatroskaChapters or MatroskaTags or MatroskaAttachments or EbmlVoid:
                    if (size < 0)
                        return MediaSegmentParseResult.Error("Invalid EBML element");

                    return size > data.Length - offset ? MediaSegmentParseResult.NeedMoreData() : MediaSegmentParseResult.Ignore(offset + (int)size);

                default:
                    return MediaSegmentParseResult.Error("Unexpected EBML element");
            }
        }

        /// <summary>Reads the header of an element. <paramref name="isValid"/> is false when the bytes cannot be the header of an element.</summary>
        private static bool TryReadElementHeader(ReadOnlySpan<byte> data, scoped ref int offset, out uint id, out long size, out bool isValid)
        {
            isValid = true;
            id = 0;
            size = 0;
            if (offset >= data.Length)
                return false;

            // The first byte gives the length of the identifier (1 to 4 bytes)
            if (data[offset] < 0x10)
            {
                isValid = false;
                return false;
            }

            var idLength = System.Numerics.BitOperations.LeadingZeroCount(data[offset]) - 23;
            if (data.Length - offset <= idLength)
                return false;

            if (data[offset + idLength] == 0)
            {
                isValid = false;
                return false;
            }

            var position = offset;
            if (!TryReadEbmlElement(data, ref position, out id, out size))
                return false;

            if (size > int.MaxValue / 2)
            {
                isValid = false;
                return false;
            }

            offset = position;
            return true;
        }

        private MediaSegmentParseResult ReadInitializationSegment(ReadOnlySpan<byte> data)
        {
            var offset = 0;
            var tracks = new List<MediaSegmentTrack>();
            var clusterTracks = new Dictionary<ulong, ClusterTrack>();
            var timestampScale = 1_000_000d;
            var duration = double.NaN;
            var hasAudio = false;
            var hasVideo = false;
            while (true)
            {
                if (!TryReadElementHeader(data, ref offset, out var id, out var size, out var isValid))
                    return isValid ? MediaSegmentParseResult.NeedMoreData() : MediaSegmentParseResult.Error("Invalid EBML element");

                if (id == MatroskaSegment)
                    continue;

                if (id == MatroskaCluster)
                    return MediaSegmentParseResult.Error("The initialization segment has no Tracks element");

                if (size < 0)
                    return MediaSegmentParseResult.Error("Invalid EBML element");

                if (size > data.Length - offset)
                    return MediaSegmentParseResult.NeedMoreData();

                var content = data.Slice(offset, (int)size);
                offset += (int)size;
                if (id == MatroskaInfo)
                {
                    var infoOffset = 0;
                    while (TryReadEbmlChild(content, ref infoOffset, out var infoId, out var value))
                    {
                        if (infoId == MatroskaTimestampScale && ReadEbmlInteger(value) is > 0 and var scale)
                        {
                            timestampScale = scale;
                        }
                        else if (infoId == MatroskaDuration)
                        {
                            duration = ReadEbmlFloat(value);
                        }
                    }
                }
                else if (id == MatroskaTracks)
                {
                    var trackOffset = 0;
                    while (TryReadEbmlChild(content, ref trackOffset, out var trackId, out var entry))
                    {
                        if (trackId != MatroskaTrackEntry || clusterTracks.Count >= MaxTracks)
                            continue;

                        var track = ParseMatroskaTrack(entry);
                        if (track.Type is not (1 or 2))
                            continue;

                        if (!isCodecSupported(track.Codec))
                            return MediaSegmentParseResult.Error($"The codec '{track.Codec}' is not supported");

                        // The first track of each type is the main track
                        var isVideo = track.Type == 1;
                        var kind = (isVideo ? hasVideo : hasAudio) ? "" : "main";
                        hasVideo |= isVideo;
                        hasAudio |= !isVideo;
                        tracks.Add(new MediaSegmentTrack((long)track.Number, isVideo, kind, track.Name, NormalizeLanguage(track.LanguageBcp47 ?? track.Language), isVideo ? track.Width : 0, isVideo ? track.Height : 0));
                        clusterTracks[track.Number] = new ClusterTrack { IsVideo = isVideo, DefaultDuration = track.DefaultDuration / 1_000_000_000d };
                    }

                    break;
                }
            }

            _timestampScale = timestampScale / 1_000_000_000d;
            _tracks.Clear();
            foreach (var (number, track) in clusterTracks)
            {
                _tracks[number] = track;
            }

            return MediaSegmentParseResult.Initialization(offset, new MediaInitializationSegment(tracks, double.IsNaN(duration) || duration <= 0 ? double.NaN : duration * _timestampScale));
        }

        private List<MediaCodedFrame>? ReadCluster(ReadOnlySpan<byte> cluster)
        {
            // The blocks of the cluster: the track, the timestamp in ticks, the duration in ticks (or NaN), the key frame flag
            var blocks = new List<(ulong Track, double Timestamp, double Duration, bool IsKeyFrame)>();
            var clusterTimestamp = 0L;
            var offset = 0;
            while (blocks.Count < MaxSamples && TryReadEbmlChild(cluster, ref offset, out var id, out var content))
            {
                switch (id)
                {
                    case MatroskaClusterTimestamp:
                        clusterTimestamp = ReadEbmlInteger(content);
                        break;

                    case MatroskaSimpleBlock:
                        if (TryReadBlockHeader(content, out var track, out var relativeTimestamp, out var flags))
                        {
                            blocks.Add((track, clusterTimestamp + relativeTimestamp, double.NaN, (flags & 0x80) != 0));
                        }

                        break;

                    case MatroskaBlockGroup:
                    {
                        var groupOffset = 0;
                        var blockDuration = double.NaN;
                        var hasReference = false;
                        (ulong Track, double Timestamp)? block = null;
                        while (TryReadEbmlChild(content, ref groupOffset, out var groupId, out var value))
                        {
                            if (groupId == MatroskaBlock && TryReadBlockHeader(value, out track, out relativeTimestamp, out _))
                            {
                                block = (track, clusterTimestamp + relativeTimestamp);
                            }
                            else if (groupId == MatroskaBlockDuration)
                            {
                                blockDuration = ReadEbmlInteger(value);
                            }
                            else if (groupId == MatroskaReferenceBlock)
                            {
                                hasReference = true;
                            }
                        }

                        if (block is var (groupTrack, timestamp))
                        {
                            blocks.Add((groupTrack, timestamp, blockDuration, !hasReference));
                        }

                        break;
                    }
                }
            }

            var frames = new List<MediaCodedFrame>(blocks.Count);
            for (var i = 0; i < blocks.Count; i++)
            {
                var (trackNumber, timestamp, duration, isKeyFrame) = blocks[i];
                if (!_tracks.TryGetValue(trackNumber, out var track))
                    return null;

                var seconds = double.IsNaN(duration) ? double.NaN : duration * _timestampScale;
                if (double.IsNaN(seconds))
                {
                    // The time to the next block of the track
                    for (var j = i + 1; j < blocks.Count; j++)
                    {
                        if (blocks[j].Track == trackNumber)
                        {
                            seconds = (blocks[j].Timestamp - timestamp) * _timestampScale;
                            break;
                        }
                    }
                }

                if (double.IsNaN(seconds) || seconds <= 0)
                {
                    // The last block of the track in the cluster: the default duration of the track, else the duration of
                    // its previous block, else an estimation (as Chromium: 33ms for a video frame, 23ms for an audio frame)
                    seconds = track.DefaultDuration > 0 ? track.DefaultDuration : track.LastDuration > 0 ? track.LastDuration : track.IsVideo ? 0.033 : 0.023;
                }

                track.LastDuration = seconds;
                var time = timestamp * _timestampScale;
                frames.Add(new MediaCodedFrame((long)trackNumber, time, time, seconds, isKeyFrame || !track.IsVideo));
            }

            return frames;
        }

        /// <summary>Reads the header of a block: the track number (a variable-size integer), the timestamp relative to the cluster (16 bits, signed) and the flags.</summary>
        private static bool TryReadBlockHeader(ReadOnlySpan<byte> block, out ulong track, out long relativeTimestamp, out byte flags)
        {
            track = 0;
            relativeTimestamp = 0;
            flags = 0;
            var offset = 0;
            if (!TryReadEbmlVariableInteger(block, ref offset, out var number) || number < 0 || block.Length - offset < 3)
                return false;

            track = (ulong)number;
            relativeTimestamp = BinaryPrimitives.ReadInt16BigEndian(block[offset..]);
            flags = block[offset + 2];
            return true;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // MPEG audio byte stream format (https://www.w3.org/TR/mse-byte-stream-format-mpeg-audio/)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A sequence of MPEG audio frames (MP3) or of ADTS frames (AAC), whose timestamps are generated by the source buffer:
    /// the header of the first frame is the initialization segment, and each frame is a media segment. The ID3 tags are
    /// ignored.
    /// </summary>
    private sealed class MpegAudioSegmentParser(bool isAdts) : MediaSegmentParser
    {
        private bool _initialized;

        public override bool GeneratesTimestamps => true;

        public override MediaSegmentParseResult Parse(ReadOnlyMemory<byte> input)
        {
            var data = input.Span;
            if (data.Length >= 3 && data[..3].SequenceEqual("ID3"u8))
            {
                if (data.Length < 10)
                    return MediaSegmentParseResult.NeedMoreData();

                var tagSize = GetId3v2Size(data);
                return tagSize > data.Length ? MediaSegmentParseResult.NeedMoreData() : MediaSegmentParseResult.Ignore(tagSize);
            }

            var headerSize = isAdts ? 7 : 4;
            if (data.Length < headerSize)
                return MediaSegmentParseResult.NeedMoreData(isParsingMediaSegment: _initialized);

            int frameSize;
            double duration;
            if (isAdts)
            {
                if (!TryReadAdtsHeader(data, 0, out frameSize, out var sampleRate, out var sampleCount))
                    return MediaSegmentParseResult.Error("Invalid ADTS frame");

                duration = (double)sampleCount / sampleRate;
            }
            else
            {
                if (!TryReadMpegAudioHeader(data, 0, out var frame))
                    return MediaSegmentParseResult.Error("Invalid MPEG audio frame");

                frameSize = frame.FrameSize;
                duration = (double)frame.SamplesPerFrame / frame.SampleRate;
            }

            if (!_initialized)
            {
                // The initialization segment has no byte of its own: it is the header of the first frame
                _initialized = true;
                return MediaSegmentParseResult.Initialization(0, new MediaInitializationSegment([new MediaSegmentTrack(1, IsVideo: false, "main")]));
            }

            if (data.Length < frameSize)
                return MediaSegmentParseResult.NeedMoreData(isParsingMediaSegment: true);

            return MediaSegmentParseResult.Media(frameSize, [new MediaCodedFrame(1, 0, 0, duration, IsRandomAccessPoint: true)]);
        }

        /// <summary>The size of the ID3v2 tag at the start of the data: "ID3", version (2), flags (1), size (4 x 7 bits), and a footer of 10 bytes with the flag 0x10.</summary>
        private static int GetId3v2Size(ReadOnlySpan<byte> data)
        {
            var size = ((data[6] & 0x7F) << 21) | ((data[7] & 0x7F) << 14) | ((data[8] & 0x7F) << 7) | (data[9] & 0x7F);
            return 10 + size + ((data[5] & 0x10) != 0 ? 10 : 0);
        }
    }
}
