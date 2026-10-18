using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>A track of a media file read by <see cref="WptMediaContainer"/>.</summary>
/// <param name="Id">The identifier of the track in the container (MP4 track_ID, Matroska TrackNumber, index from 1 otherwise).</param>
/// <param name="IsVideo"><see langword="true"/> for a video track, <see langword="false"/> for an audio track.</param>
/// <param name="Codec">The codec as named by the container (e.g. <c>avc1</c>, <c>V_VP9</c>, <c>vorbis</c>).</param>
/// <param name="Language">The language tag of the track, or an empty string when it is not determined.</param>
/// <param name="Label">The name of the track, or an empty string.</param>
/// <param name="Width">The display width of a video track in pixels (0 for an audio track).</param>
/// <param name="Height">The display height of a video track in pixels (0 for an audio track).</param>
internal sealed record WptMediaTrack(string Id, bool IsVideo, string Codec, string Language, string Label, int Width, int Height);

/// <summary>The metadata of a media file read by <see cref="WptMediaContainer"/>.</summary>
/// <param name="Container">The container: <c>webm</c>, <c>mp4</c>, <c>ogg</c>, <c>wav</c>, <c>mp3</c>, <c>flac</c> or <c>aac</c>.</param>
/// <param name="Duration">The duration in seconds, or <see cref="double.NaN"/> when it cannot be determined.</param>
/// <param name="Tracks">The audio and video tracks, in the order of the container.</param>
/// <param name="VideoSizes">The sizes of the frames of the first video track over time (empty without a video track).</param>
internal sealed record WptMediaInfo(string Container, double Duration, IReadOnlyList<WptMediaTrack> Tracks, IReadOnlyList<WptMediaVideoSize> VideoSizes)
{
    /// <summary>Gets the first video track, if any.</summary>
    public WptMediaTrack? FirstVideoTrack => Tracks.FirstOrDefault(static track => track.IsVideo);

    /// <summary>Gets the first audio track, if any.</summary>
    public WptMediaTrack? FirstAudioTrack => Tracks.FirstOrDefault(static track => !track.IsVideo);
}

/// <summary>The size of the video frames from a time of the media timeline (the first entry is at 0).</summary>
/// <param name="Time">The time in seconds from which the size applies.</param>
/// <param name="Width">The width of the frames in pixels.</param>
/// <param name="Height">The height of the frames in pixels.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WptMediaVideoSize(double Time, int Width, int Height);

/// <summary>
/// Reads the metadata (container, duration, tracks, video size) of the media files of the tests. Nothing is decoded:
/// only the container structures are read (ISO BMFF, Matroska/WebM, Ogg, RIFF/WAVE, MPEG audio, FLAC, ADTS).
/// </summary>
internal static partial class WptMediaContainer
{
    // The limits protect against hostile inputs: the test files have a handful of tracks and a few thousand samples
    private const int MaxTracks = 64;
    private const int MaxVideoSizes = 256;
    private const int MaxSamples = 1 << 20;
    private const int MaxFrameSearch = 1 << 17;

    /// <summary>
    /// Returns the metadata of the media file, or <see langword="null"/> when the bytes are not a media file of a known
    /// container or when no track can be read (e.g. the headers are missing). This method never throws on malformed
    /// or truncated data: what could be read is returned (e.g. the tracks without the duration).
    /// </summary>
    public static WptMediaInfo? Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            return SniffContainer(data) switch
            {
                "webm" => ParseMatroska(data),
                "mp4" => ParseMp4(data),
                "ogg" => ParseOgg(data),
                "wav" => ParseWav(data),
                "mp3" => ParseMp3(data),
                "flac" => ParseFlac(data),
                "aac" => ParseAdts(data),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            // Safety net: the readers check the bounds, the data is untrusted
            return null;
        }
    }

    /// <summary>
    /// Returns the container identified by the signature of the data: <c>webm</c> (EBML: WebM and Matroska),
    /// <c>mp4</c> (ISO BMFF and QuickTime), <c>ogg</c>, <c>wav</c>, <c>mp3</c> (ID3v2 tag or MPEG audio frame),
    /// <c>flac</c>, <c>aac</c> (ADTS), or <see langword="null"/>.
    /// </summary>
    public static string? SniffContainer(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return null;

        if (data.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
            return "webm";

        if (data.StartsWith("OggS"u8))
            return "ogg";

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WAVE"u8))
            return "wav";

        if (data.Length >= 8 && IsKnownTopLevelBox(data.Slice(4, 4)))
            return "mp4";

        // An ID3v2 tag precedes MPEG audio most of the time, but also FLAC and ADTS streams
        var hasTag = data.StartsWith("ID3"u8);
        var offset = SkipId3v2(data);
        var audio = Slice(data, offset, data.Length);
        if (audio.StartsWith("fLaC"u8))
            return "flac";

        if (TryReadAdtsHeader(audio, 0, out var adtsFrameSize, out _, out _) && (audio.Length < adtsFrameSize + 7 || TryReadAdtsHeader(audio, adtsFrameSize, out _, out _, out _)))
            return "aac";

        if (hasTag)
            return "mp3";

        if (TryReadMpegAudioHeader(data, 0, out var frame) && (data.Length < frame.FrameSize + 4 || (TryReadMpegAudioHeader(data, frame.FrameSize, out var next) && next.IsCompatibleWith(frame))))
            return "mp3";

        return null;
    }

    private static bool IsKnownTopLevelBox(ReadOnlySpan<byte> type)
    {
        return type.SequenceEqual("ftyp"u8) || type.SequenceEqual("styp"u8) || type.SequenceEqual("moov"u8) || type.SequenceEqual("moof"u8)
            || type.SequenceEqual("mdat"u8) || type.SequenceEqual("sidx"u8) || type.SequenceEqual("free"u8) || type.SequenceEqual("skip"u8)
            || type.SequenceEqual("wide"u8) || type.SequenceEqual("emsg"u8) || type.SequenceEqual("prft"u8) || type.SequenceEqual("pnot"u8);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Common helpers
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Reads a big-endian unsigned integer of 1 to 8 bytes; returns 0 when it is out of the data.</summary>
    private static ulong ReadBigEndian(ReadOnlySpan<byte> data, long offset, int length)
    {
        if (offset < 0 || length is <= 0 or > 8 || offset > data.Length - length)
            return 0;

        ulong value = 0;
        foreach (var b in data.Slice((int)offset, length))
        {
            value = (value << 8) | b;
        }

        return value;
    }

    /// <summary>Reads a little-endian unsigned integer of 1 to 8 bytes; returns 0 when it is out of the data.</summary>
    private static ulong ReadLittleEndian(ReadOnlySpan<byte> data, long offset, int length)
    {
        if (offset < 0 || length is <= 0 or > 8 || offset > data.Length - length)
            return 0;

        ulong value = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            value = (value << 8) | data[(int)offset + i];
        }

        return value;
    }

    /// <summary>Returns the part of the data in the range, clamped to the data (empty when the range starts out of the data).</summary>
    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, long offset, long length)
    {
        if (offset < 0 || offset >= data.Length || length <= 0)
            return default;

        return data.Slice((int)offset, (int)Math.Min(length, data.Length - offset));
    }

    private static string ReadString(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        if (end >= 0)
        {
            data = data[..end];
        }

        return Encoding.UTF8.GetString(data).Trim();
    }

    private static string NormalizeLanguage(string language)
    {
        foreach (var c in language)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                return "";
        }

        // "und" (undetermined), "mis", "mul" and "zxx" are not languages
        return language is "und" or "mis" or "mul" or "zxx" ? "" : language;
    }

    private static WptMediaInfo? CreateInfo(string container, double duration, List<WptMediaTrack> tracks, VideoSizeChanges? sizeChanges = null)
    {
        if (tracks.Count == 0)
            return null;

        if (!double.IsFinite(duration) || duration < 0)
        {
            duration = double.NaN;
        }

        var sizes = new List<WptMediaVideoSize>();
        var video = tracks.Find(static track => track.IsVideo);
        if (video is not null)
        {
            sizes.Add(new WptMediaVideoSize(0, video.Width, video.Height));
            if (sizeChanges is not null)
            {
                sizes.AddRange(sizeChanges.Changes);
            }
        }

        return new WptMediaInfo(container, duration, tracks, sizes);
    }

    /// <summary>Stretches the width or the height of the coded frames to have the aspect ratio (width / height).</summary>
    private static (int Width, int Height) ApplyAspectRatio(int width, int height, double aspectRatio)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(aspectRatio) || aspectRatio <= 0)
            return (width, height);

        return aspectRatio > (double)width / height
            ? ((int)Math.Clamp(Math.Round(height * aspectRatio, MidpointRounding.AwayFromZero), 1, 1 << 20), height)
            : (width, (int)Math.Clamp(Math.Round(width / aspectRatio, MidpointRounding.AwayFromZero), 1, 1 << 20));
    }

    /// <summary>Records the changes of the coded size of the key frames of a video track.</summary>
    private sealed class VideoSizeChanges(int displayWidth, int displayHeight)
    {
        private int _firstWidth;
        private int _firstHeight;
        private int _lastWidth;
        private int _lastHeight;

        public List<WptMediaVideoSize> Changes { get; } = [];

        public void Add(double time, int codedWidth, int codedHeight)
        {
            if (codedWidth <= 0 || codedHeight <= 0 || (codedWidth == _lastWidth && codedHeight == _lastHeight))
                return;

            _lastWidth = codedWidth;
            _lastHeight = codedHeight;
            if (_firstWidth == 0)
            {
                // The first frame has the size declared by the track
                _firstWidth = codedWidth;
                _firstHeight = codedHeight;
                return;
            }

            if (Changes.Count >= MaxVideoSizes || !double.IsFinite(time))
                return;

            // The track declares the display size of the first coded size: keep the same scaling for the other ones
            var width = displayWidth > 0 ? Math.Max(1, (int)Math.Round((double)codedWidth * displayWidth / _firstWidth, MidpointRounding.AwayFromZero)) : codedWidth;
            var height = displayHeight > 0 ? Math.Max(1, (int)Math.Round((double)codedHeight * displayHeight / _firstHeight, MidpointRounding.AwayFromZero)) : codedHeight;
            Changes.Add(new WptMediaVideoSize(Math.Max(0, time), width, height));
        }
    }

    /// <summary>Reads the bits of a bitstream, most significant bit first. Reading after the end sets <see cref="IsValid"/> to false.</summary>
    private ref struct BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private long _position;

        public bool IsValid { get; private set; } = true;

        public uint ReadBits(int count)
        {
            uint value = 0;
            for (var i = 0; i < count; i++)
            {
                var index = _position >> 3;
                if (index >= _data.Length)
                {
                    IsValid = false;
                    return 0;
                }

                value = (value << 1) | (uint)((_data[(int)index] >> (7 - (int)(_position & 7))) & 1);
                _position++;
            }

            return value;
        }

        public bool ReadFlag() => ReadBits(1) != 0;

        /// <summary>Reads an unsigned Exp-Golomb code: ue(v) of H.264.</summary>
        public uint ReadUnsignedExpGolomb()
        {
            var zeros = 0;
            while (IsValid && ReadBits(1) == 0)
            {
                zeros++;
                if (zeros > 31)
                {
                    IsValid = false;
                    return 0;
                }
            }

            return IsValid ? (uint)(((1UL << zeros) - 1) + ReadBits(zeros)) : 0;
        }

        /// <summary>Reads a signed Exp-Golomb code: se(v) of H.264.</summary>
        public int ReadSignedExpGolomb()
        {
            var value = (long)ReadUnsignedExpGolomb();
            return (int)((value & 1) != 0 ? (value + 1) / 2 : -(value / 2));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // ISO base media file format (ISO/IEC 14496-12) and QuickTime
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class Mp4Track
    {
        public uint Id { get; set; }
        public bool IsVideo { get; set; }
        public bool IsAudio { get; set; }
        public string Codec { get; set; } = "";
        public string Language { get; set; } = "";
        public string Label { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public uint Timescale { get; set; }

        /// <summary>The duration of the media (mdhd) in the timescale of the track.</summary>
        public double Duration { get; set; }

        /// <summary>The duration of the edits of the track (elst) in the timescale of the movie (0 without edit list).</summary>
        public double EditDuration { get; set; }

        /// <summary>The end of the last sample of the movie fragments in the timescale of the track.</summary>
        public double FragmentEnd { get; set; }

        public VideoSizeChanges? SizeChanges { get; set; }
    }

    /// <summary>A sample entry of stsd. <paramref name="NalLengthSize"/> is the size of the length of the NAL units of H.264 (0 otherwise).</summary>
    private readonly record struct Mp4SampleEntry(int Width, int Height, int NalLengthSize, bool IsVp8, bool IsVp9)
    {
        /// <summary>Gets a value indicating whether the samples have the size of the frames in a header this class can read.</summary>
        public bool HasSizeInSamples => NalLengthSize > 0 || IsVp8 || IsVp9;
    }

    /// <summary>Reads the box at the offset: size (32 bits), type (4 characters), optional 64-bit size, then the payload.</summary>
    private static bool TryReadBox(ReadOnlySpan<byte> data, scoped ref int offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> payload)
    {
        type = default;
        payload = default;
        if (offset < 0 || data.Length - offset < 8)
            return false;

        var available = data.Length - offset;
        var size = (long)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        var headerSize = 8;
        if (size == 1)
        {
            // largesize
            if (available < 16)
                return false;

            var largeSize = BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]);
            size = largeSize > int.MaxValue ? int.MaxValue : (long)largeSize;
            headerSize = 16;
        }
        else if (size == 0)
        {
            // The box extends to the end of the file
            size = available;
        }

        if (size < headerSize)
            return false;

        // A truncated box keeps the available data
        var boxSize = (int)Math.Min(size, available);
        type = data.Slice(offset + 4, 4);
        payload = data.Slice(offset + headerSize, boxSize - headerSize);
        offset += boxSize;
        return true;
    }

    private static ReadOnlySpan<byte> FindBox(ReadOnlySpan<byte> data, ReadOnlySpan<byte> boxType)
    {
        var offset = 0;
        while (TryReadBox(data, ref offset, out var type, out var payload))
        {
            if (type.SequenceEqual(boxType))
                return payload;
        }

        return default;
    }

    private static WptMediaInfo? ParseMp4(ReadOnlySpan<byte> data)
    {
        var tracks = new List<Mp4Track>();
        var defaultSampleDurations = new Dictionary<uint, uint>();
        var duration = double.NaN;

        // First pass: the movie box (it may be after the media data). Only the first one is read: some files of the
        // tests concatenate several initialization segments
        var offset = 0;
        var hasMovie = false;
        while (!hasMovie && TryReadBox(data, ref offset, out var type, out var payload))
        {
            if (!type.SequenceEqual("moov"u8))
                continue;

            hasMovie = true;
            var movieDuration = 0d;
            var fragmentDuration = 0d;
            var movieTimescale = 0d;
            var childOffset = 0;
            while (TryReadBox(payload, ref childOffset, out var childType, out var child))
            {
                if (childType.SequenceEqual("mvhd"u8))
                {
                    // version (1), flags (3), creation and modification times, timescale (4), duration (4 or 8)
                    var isVersion1 = child.Length > 0 && child[0] == 1;
                    movieTimescale = ReadBigEndian(child, isVersion1 ? 20 : 12, 4);
                    movieDuration = ReadMp4Duration(child, isVersion1 ? 24 : 16, isVersion1);
                }
                else if (childType.SequenceEqual("trak"u8))
                {
                    if (tracks.Count < MaxTracks)
                    {
                        tracks.Add(ParseMp4Track(data, child));
                    }
                }
                else if (childType.SequenceEqual("mvex"u8))
                {
                    // mehd: version (1), flags (3), fragment_duration (4 or 8) in the timescale of the movie
                    var mehd = FindBox(child, "mehd"u8);
                    fragmentDuration = ReadMp4Duration(mehd, 4, mehd.Length > 0 && mehd[0] == 1);

                    // trex: version, flags, track_ID, default_sample_description_index, default_sample_duration...
                    var extendsOffset = 0;
                    while (TryReadBox(child, ref extendsOffset, out var extendsType, out var trex))
                    {
                        if (extendsType.SequenceEqual("trex"u8) && trex.Length >= 16 && defaultSampleDurations.Count < MaxTracks)
                        {
                            defaultSampleDurations[(uint)ReadBigEndian(trex, 4, 4)] = (uint)ReadBigEndian(trex, 12, 4);
                        }
                    }
                }
            }

            if (movieTimescale > 0)
            {
                if (movieDuration > 0)
                {
                    // The header has the duration of the longest track; some writers do not remove the samples skipped
                    // by the edit lists (e.g. the priming samples of AAC) from it: the edit lists are the presentation
                    var editDuration = 0d;
                    foreach (var track in tracks)
                    {
                        if (track.IsVideo || track.IsAudio)
                        {
                            editDuration = track.EditDuration > 0 ? Math.Max(editDuration, track.EditDuration) : double.PositiveInfinity;
                        }
                    }

                    duration = Math.Min(movieDuration, editDuration > 0 ? editDuration : movieDuration) / movieTimescale;
                }
                else if (fragmentDuration > 0)
                {
                    duration = fragmentDuration / movieTimescale;
                }
            }
        }

        if (double.IsNaN(duration))
        {
            duration = ReadMp4FragmentsDuration(data, tracks, defaultSampleDurations);
        }

        var result = new List<WptMediaTrack>();
        VideoSizeChanges? sizeChanges = null;
        foreach (var track in tracks)
        {
            if (!track.IsVideo && !track.IsAudio)
                continue;

            if (track.IsVideo && sizeChanges is null && !result.Exists(static t => t.IsVideo))
            {
                sizeChanges = track.SizeChanges;
            }

            result.Add(new WptMediaTrack(track.Id.ToString(CultureInfo.InvariantCulture), track.IsVideo, track.Codec, track.Language, track.Label, track.IsVideo ? track.Width : 0, track.IsVideo ? track.Height : 0));
        }

        return CreateInfo("mp4", duration, result, sizeChanges);
    }

    /// <summary>Reads a duration field of 4 or 8 bytes; the value with all the bits set means "unknown".</summary>
    private static double ReadMp4Duration(ReadOnlySpan<byte> box, int offset, bool is64Bits)
    {
        var value = ReadBigEndian(box, offset, is64Bits ? 8 : 4);
        return value == (is64Bits ? ulong.MaxValue : uint.MaxValue) ? 0 : value;
    }

    private static Mp4Track ParseMp4Track(ReadOnlySpan<byte> file, ReadOnlySpan<byte> trak)
    {
        var track = new Mp4Track();

        // tkhd: version, flags, creation and modification times (4 or 8 bytes each), track_ID (4), reserved (4),
        // duration (4 or 8), reserved (8), layer, alternate_group, volume, reserved (2 each), matrix (36),
        // width and height (16.16 fixed point)
        var tkhd = FindBox(trak, "tkhd"u8);
        var isVersion1 = tkhd.Length > 0 && tkhd[0] == 1;
        track.Id = (uint)ReadBigEndian(tkhd, isVersion1 ? 20 : 12, 4);
        var trackWidth = ReadBigEndian(tkhd, isVersion1 ? 88 : 76, 4) / 65536d;
        var trackHeight = ReadBigEndian(tkhd, isVersion1 ? 92 : 80, 4) / 65536d;

        // elst: version, flags, entry_count, then (segment_duration, media_time: 4 or 8 bytes each, media_rate: 4)
        var elst = FindBox(FindBox(trak, "edts"u8), "elst"u8);
        var editSize = elst.Length > 0 && elst[0] == 1 ? 20 : 12;
        var editCount = (int)Math.Min(ReadBigEndian(elst, 4, 4), (ulong)Math.Max(0, (elst.Length - 8) / editSize));
        for (var i = 0; i < editCount; i++)
        {
            track.EditDuration += ReadBigEndian(elst, 8 + (i * editSize), editSize == 20 ? 8 : 4);
        }

        // mdhd: version, flags, creation and modification times, timescale (4), duration (4 or 8), language (2)
        var mdia = FindBox(trak, "mdia"u8);
        var mdhd = FindBox(mdia, "mdhd"u8);
        isVersion1 = mdhd.Length > 0 && mdhd[0] == 1;
        track.Timescale = (uint)ReadBigEndian(mdhd, isVersion1 ? 20 : 12, 4);
        track.Duration = ReadMp4Duration(mdhd, isVersion1 ? 24 : 16, isVersion1);
        track.Language = ReadMp4Language((int)ReadBigEndian(mdhd, isVersion1 ? 32 : 20, 2));

        // elng: version, flags, extended language tag (BCP 47)
        var elng = FindBox(mdia, "elng"u8);
        if (elng.Length > 4 && NormalizeLanguage(ReadString(elng[4..])) is { Length: > 0 } extendedLanguage)
        {
            track.Language = extendedLanguage;
        }

        // hdlr: version, flags, pre_defined (4), handler_type (4), reserved (12), name
        var hdlr = FindBox(mdia, "hdlr"u8);
        var handlerType = Slice(hdlr, 8, 4);
        track.IsVideo = handlerType.SequenceEqual("vide"u8);
        track.IsAudio = handlerType.SequenceEqual("soun"u8);
        var name = Slice(hdlr, 24, hdlr.Length);
        if (name.Length > 1 && name[0] == name.Length - 1)
        {
            // QuickTime: the name is a Pascal string (length prefixed)
            name = name[1..];
        }

        track.Label = ReadString(name);

        // stsd: version, flags, entry_count (4), then the sample entries (boxes whose type is the codec)
        var stbl = FindBox(FindBox(mdia, "minf"u8), "stbl"u8);
        var stsd = FindBox(stbl, "stsd"u8);
        var entries = new List<Mp4SampleEntry>();
        var pixelAspectRatio = 0d;
        var entryOffset = 8;
        while (entries.Count < 16 && TryReadBox(stsd, ref entryOffset, out var format, out var entry))
        {
            // VisualSampleEntry: reserved (6), data_reference_index (2), pre_defined and reserved (16), width (2),
            // height (2), resolutions, frame count, compressor name, depth... (78 bytes), then the configuration boxes.
            // AudioSampleEntry: reserved (6), data_reference_index (2), reserved (8), channels, sample size, sample
            // rate... (28 bytes), then the configuration boxes
            var boxes = Slice(entry, track.IsVideo ? 78 : 28, entry.Length);
            if (format.SequenceEqual("encv"u8) || format.SequenceEqual("enca"u8))
            {
                // Encrypted samples: the codec is the original format (sinf/frma)
                var frma = FindBox(FindBox(boxes, "sinf"u8), "frma"u8);
                format = frma.Length >= 4 ? frma[..4] : format;
            }

            if (entries.Count == 0)
            {
                track.Codec = IsPrintableAscii(format) ? Encoding.ASCII.GetString(format).Trim() : "";

                // pasp: hSpacing (4), vSpacing (4)
                var pasp = FindBox(boxes, "pasp"u8);
                pixelAspectRatio = pasp.Length >= 8 && ReadBigEndian(pasp, 4, 4) > 0 ? (double)ReadBigEndian(pasp, 0, 4) / ReadBigEndian(pasp, 4, 4) : 0;
            }

            if (!track.IsVideo)
            {
                entries.Add(default);
                continue;
            }

            // avcC: version, profile, compatibility, level, lengthSizeMinusOne (2 low bits)...
            var avcC = format.SequenceEqual("avc1"u8) || format.SequenceEqual("avc3"u8) ? FindBox(boxes, "avcC"u8) : default;
            entries.Add(new Mp4SampleEntry((int)ReadBigEndian(entry, 24, 2), (int)ReadBigEndian(entry, 26, 2), avcC.Length > 4 ? (avcC[4] & 3) + 1 : 0, format.SequenceEqual("vp08"u8), format.SequenceEqual("vp09"u8)));
        }

        if (track.IsVideo)
        {
            // The natural size is the size of the sample entry with the pixel aspect ratio (pasp), or else with the
            // aspect ratio of the size of the track header (like Chromium: this size is not always in pixels)
            track.Width = entries.Count > 0 ? entries[0].Width : 0;
            track.Height = entries.Count > 0 ? entries[0].Height : 0;
            if (track.Width == 0 || track.Height == 0)
            {
                track.Width = (int)Math.Round(trackWidth, MidpointRounding.AwayFromZero);
                track.Height = (int)Math.Round(trackHeight, MidpointRounding.AwayFromZero);
            }
            else if (pixelAspectRatio > 0)
            {
                (track.Width, track.Height) = ApplyAspectRatio(track.Width, track.Height, pixelAspectRatio * track.Width / track.Height);
            }
            else if (trackWidth > 0 && trackHeight > 0)
            {
                (track.Width, track.Height) = ApplyAspectRatio(track.Width, track.Height, trackWidth / trackHeight);
            }
        }

        if (track.IsVideo && entries.Count > 0)
        {
            // The size only changes with several sample entries, or in the samples: the sequence parameter sets of
            // H.264 and the key frames of VP8 and VP9
            if (entries.Count > 1 || entries[0].HasSizeInSamples)
            {
                track.SizeChanges = ReadMp4VideoSizeChanges(file, stbl, track, entries);
            }
        }

        return track;
    }

    private static bool IsPrintableAscii(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            if (b is < 0x20 or > 0x7E)
                return false;
        }

        return true;
    }

    /// <summary>Reads the language of mdhd: three lowercase letters (ISO 639-2/T), each one stored as 5 bits minus 0x60.</summary>
    private static string ReadMp4Language(int packed)
    {
        // QuickTime stores a Macintosh language code (lower than 0x400) instead
        if (packed < 0x400 || packed > 0x7FFF)
            return "";

        ReadOnlySpan<char> letters = [(char)(((packed >> 10) & 0x1F) + 0x60), (char)(((packed >> 5) & 0x1F) + 0x60), (char)((packed & 0x1F) + 0x60)];
        foreach (var letter in letters)
        {
            if (letter is < 'a' or > 'z')
                return "";
        }

        return NormalizeLanguage(new string(letters));
    }

    /// <summary>
    /// Walks the samples of the track (stsc, stco/co64, stsz, stts) to find the changes of the size of the frames:
    /// a sample using another sample entry, or an H.264 sequence parameter set in a sample.
    /// </summary>
    private static VideoSizeChanges ReadMp4VideoSizeChanges(ReadOnlySpan<byte> file, ReadOnlySpan<byte> stbl, Mp4Track track, List<Mp4SampleEntry> entries)
    {
        var changes = new VideoSizeChanges(track.Width, track.Height);

        // stsc: version, flags, entry_count, then (first_chunk, samples_per_chunk, sample_description_index)
        var stsc = FindBox(stbl, "stsc"u8);
        var chunkRunCount = (int)Math.Min(ReadBigEndian(stsc, 4, 4), (ulong)Math.Max(0, (stsc.Length - 8) / 12));

        // stco/co64: version, flags, entry_count, then the offset in the file of each chunk (4 or 8 bytes)
        var chunkOffsets = FindBox(stbl, "stco"u8);
        var chunkOffsetSize = 4;
        if (chunkOffsets.IsEmpty)
        {
            chunkOffsets = FindBox(stbl, "co64"u8);
            chunkOffsetSize = 8;
        }

        var chunkCount = (int)Math.Min(ReadBigEndian(chunkOffsets, 4, 4), (ulong)Math.Max(0, (chunkOffsets.Length - 8) / chunkOffsetSize));

        // stsz: version, flags, sample_size (0 when the samples have different sizes), sample_count, then the sizes
        var stsz = FindBox(stbl, "stsz"u8);
        var constantSampleSize = (long)ReadBigEndian(stsz, 4, 4);
        var sampleCount = (long)ReadBigEndian(stsz, 8, 4);
        if (constantSampleSize == 0)
        {
            sampleCount = Math.Min(sampleCount, Math.Max(0, (stsz.Length - 12) / 4));
        }

        sampleCount = Math.Min(sampleCount, MaxSamples);

        // stts: version, flags, entry_count, then (sample_count, sample_delta)
        var stts = FindBox(stbl, "stts"u8);
        var timeRunCount = (int)Math.Min(ReadBigEndian(stts, 4, 4), (ulong)Math.Max(0, (stts.Length - 8) / 8));

        if (chunkRunCount == 0 || track.Timescale == 0)
            return changes;

        var sample = 0L;
        var chunkRun = 0;
        var timeRun = 0;
        var timeRunRemaining = 0UL;
        var sampleDelta = 0UL;
        var time = 0d;
        var lastDescriptionIndex = -1;
        var width = 0;
        var height = 0;
        for (var chunk = 0; chunk < chunkCount && sample < sampleCount; chunk++)
        {
            while (chunkRun + 1 < chunkRunCount && ReadBigEndian(stsc, 8 + ((chunkRun + 1) * 12L), 4) <= (ulong)chunk + 1)
            {
                chunkRun++;
            }

            var samplesPerChunk = (long)ReadBigEndian(stsc, 12 + (chunkRun * 12L), 4);
            var descriptionIndex = (int)Math.Min(ReadBigEndian(stsc, 16 + (chunkRun * 12L), 4), int.MaxValue) - 1;
            var sampleOffset = (double)ReadBigEndian(chunkOffsets, 8 + ((long)chunk * chunkOffsetSize), chunkOffsetSize);
            for (var i = 0L; i < samplesPerChunk && sample < sampleCount; i++, sample++)
            {
                var sampleSize = constantSampleSize != 0 ? constantSampleSize : (long)ReadBigEndian(stsz, 12 + (sample * 4), 4);
                while (timeRunRemaining == 0 && timeRun < timeRunCount)
                {
                    timeRunRemaining = ReadBigEndian(stts, 8 + (timeRun * 8L), 4);
                    sampleDelta = ReadBigEndian(stts, 12 + (timeRun * 8L), 4);
                    timeRun++;
                }

                if (descriptionIndex >= 0 && descriptionIndex < entries.Count)
                {
                    var entry = entries[descriptionIndex];
                    if (descriptionIndex != lastDescriptionIndex)
                    {
                        lastDescriptionIndex = descriptionIndex;
                        width = entry.Width;
                        height = entry.Height;
                    }

                    if (entry.HasSizeInSamples && sampleOffset < file.Length)
                    {
                        var sampleData = Slice(file, (long)sampleOffset, sampleSize);
                        if (entry.IsVp8 ? TryReadVp8KeyFrameSize(sampleData, out var frameWidth, out var frameHeight)
                            : entry.IsVp9 ? TryReadVp9KeyFrameSize(sampleData, out frameWidth, out frameHeight)
                            : TryFindH264Size(sampleData, entry.NalLengthSize, out frameWidth, out frameHeight))
                        {
                            width = frameWidth;
                            height = frameHeight;
                        }
                    }

                    changes.Add(time / track.Timescale, width, height);
                }

                sampleOffset += sampleSize;
                time += sampleDelta;
                if (timeRunRemaining > 0)
                {
                    timeRunRemaining--;
                }
            }
        }

        return changes;
    }

    /// <summary>Finds a sequence parameter set in the NAL units of a sample (each one is prefixed with its length).</summary>
    private static bool TryFindH264Size(ReadOnlySpan<byte> sample, int nalLengthSize, out int width, out int height)
    {
        width = 0;
        height = 0;
        var offset = 0;
        for (var i = 0; i < 64 && sample.Length - offset > nalLengthSize; i++)
        {
            var length = ReadBigEndian(sample, offset, nalLengthSize);
            offset += nalLengthSize;
            if (length == 0 || length > (ulong)(sample.Length - offset))
                return false;

            // nal_unit_type 7: sequence parameter set
            if ((sample[offset] & 0x1F) == 7)
                return TryReadH264SequenceParameterSet(sample.Slice(offset + 1, (int)length - 1), out width, out height);

            offset += (int)length;
        }

        return false;
    }

    /// <summary>Reads the size of the frames from seq_parameter_set_data() (H.264, 7.3.2.1.1).</summary>
    private static bool TryReadH264SequenceParameterSet(ReadOnlySpan<byte> nal, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Remove the emulation prevention bytes (00 00 03); the size is in the first bytes of the parameter set
        Span<byte> rbsp = stackalloc byte[Math.Min(nal.Length, 512)];
        var rbspLength = 0;
        var zeros = 0;
        foreach (var b in nal)
        {
            if (rbspLength == rbsp.Length)
                break;

            if (zeros >= 2 && b == 3)
            {
                zeros = 0;
                continue;
            }

            zeros = b == 0 ? zeros + 1 : 0;
            rbsp[rbspLength++] = b;
        }

        var reader = new BitReader(rbsp[..rbspLength]);
        var profile = reader.ReadBits(8);
        reader.ReadBits(16); // constraint flags, level_idc
        reader.ReadUnsignedExpGolomb(); // seq_parameter_set_id
        var chromaFormat = 1u;
        if (profile is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
        {
            chromaFormat = reader.ReadUnsignedExpGolomb();
            if (chromaFormat == 3)
            {
                reader.ReadFlag(); // separate_colour_plane_flag
            }

            reader.ReadUnsignedExpGolomb(); // bit_depth_luma_minus8
            reader.ReadUnsignedExpGolomb(); // bit_depth_chroma_minus8
            reader.ReadFlag(); // qpprime_y_zero_transform_bypass_flag
            if (reader.ReadFlag())
            {
                // seq_scaling_matrix_present_flag: skip the scaling lists
                for (var i = 0; i < (chromaFormat != 3 ? 8 : 12); i++)
                {
                    if (!reader.ReadFlag())
                        continue;

                    var lastScale = 8;
                    var nextScale = 8;
                    for (var j = 0; j < (i < 6 ? 16 : 64) && reader.IsValid; j++)
                    {
                        if (nextScale != 0)
                        {
                            nextScale = (lastScale + reader.ReadSignedExpGolomb() + 256) & 0xFF;
                        }

                        lastScale = nextScale == 0 ? lastScale : nextScale;
                    }
                }
            }
        }

        reader.ReadUnsignedExpGolomb(); // log2_max_frame_num_minus4
        var pictureOrderCountType = reader.ReadUnsignedExpGolomb();
        if (pictureOrderCountType == 0)
        {
            reader.ReadUnsignedExpGolomb(); // log2_max_pic_order_cnt_lsb_minus4
        }
        else if (pictureOrderCountType == 1)
        {
            reader.ReadFlag(); // delta_pic_order_always_zero_flag
            reader.ReadSignedExpGolomb(); // offset_for_non_ref_pic
            reader.ReadSignedExpGolomb(); // offset_for_top_to_bottom_field
            var count = reader.ReadUnsignedExpGolomb();
            for (var i = 0u; i < count && i < 256 && reader.IsValid; i++)
            {
                reader.ReadSignedExpGolomb(); // offset_for_ref_frame
            }
        }

        reader.ReadUnsignedExpGolomb(); // max_num_ref_frames
        reader.ReadFlag(); // gaps_in_frame_num_value_allowed_flag
        var widthInMacroblocks = (long)reader.ReadUnsignedExpGolomb() + 1;
        var heightInMapUnits = (long)reader.ReadUnsignedExpGolomb() + 1;
        var frameMacroblocksOnly = reader.ReadFlag();
        if (!frameMacroblocksOnly)
        {
            reader.ReadFlag(); // mb_adaptive_frame_field_flag
        }

        reader.ReadFlag(); // direct_8x8_inference_flag
        long cropLeft = 0, cropRight = 0, cropTop = 0, cropBottom = 0;
        if (reader.ReadFlag())
        {
            // frame_cropping_flag: the offsets are in chroma samples
            cropLeft = reader.ReadUnsignedExpGolomb();
            cropRight = reader.ReadUnsignedExpGolomb();
            cropTop = reader.ReadUnsignedExpGolomb();
            cropBottom = reader.ReadUnsignedExpGolomb();
        }

        if (!reader.IsValid)
            return false;

        var fieldFactor = frameMacroblocksOnly ? 1 : 2;
        var cropUnitX = chromaFormat is 1 or 2 ? 2 : 1;
        var cropUnitY = (chromaFormat == 1 ? 2 : 1) * fieldFactor;
        var codedWidth = (widthInMacroblocks * 16) - (cropUnitX * (cropLeft + cropRight));
        var codedHeight = (heightInMapUnits * 16 * fieldFactor) - (cropUnitY * (cropTop + cropBottom));
        if (codedWidth is <= 0 or > 65535 || codedHeight is <= 0 or > 65535)
            return false;

        width = (int)codedWidth;
        height = (int)codedHeight;
        return true;
    }

    /// <summary>
    /// Computes the duration of a fragmented file: the end of the last sample of the movie fragments (moof), else the
    /// sum of the durations of the segment index (sidx), else the duration of the media of the tracks.
    /// </summary>
    private static double ReadMp4FragmentsDuration(ReadOnlySpan<byte> data, List<Mp4Track> tracks, Dictionary<uint, uint> defaultSampleDurations)
    {
        var cursors = new Dictionary<uint, double>();
        var indexDurations = new Dictionary<uint, double>();
        var offset = 0;
        while (TryReadBox(data, ref offset, out var type, out var payload))
        {
            if (type.SequenceEqual("moof"u8))
            {
                var fragmentOffset = 0;
                while (TryReadBox(payload, ref fragmentOffset, out var fragmentType, out var traf))
                {
                    if (fragmentType.SequenceEqual("traf"u8))
                    {
                        ReadMp4TrackFragment(traf, tracks, defaultSampleDurations, cursors);
                    }
                }
            }
            else if (type.SequenceEqual("sidx"u8))
            {
                // version, flags, reference_ID (4), timescale (4), earliest_presentation_time and first_offset (4 or 8
                // bytes each), reserved (2), reference_count (2), then (reference_type and size, duration, SAP)
                var timescale = ReadBigEndian(payload, 8, 4);
                var position = payload.Length > 0 && payload[0] != 0 ? 30 : 22;
                var count = (int)ReadBigEndian(payload, position, 2);
                var total = 0d;
                for (var i = 0; i < count; i++)
                {
                    // The references to other segment indexes (reference_type 1) are counted by these indexes
                    var reference = position + 2 + (i * 12);
                    if ((ReadBigEndian(payload, reference, 1) & 0x80) == 0)
                    {
                        total += ReadBigEndian(payload, reference + 4, 4);
                    }
                }

                var referenceId = (uint)ReadBigEndian(payload, 4, 4);
                if (timescale > 0 && (indexDurations.Count < MaxTracks || indexDurations.ContainsKey(referenceId)))
                {
                    indexDurations[referenceId] = indexDurations.GetValueOrDefault(referenceId) + (total / timescale);
                }
            }
        }

        var duration = 0d;
        foreach (var track in tracks)
        {
            if (track.Timescale > 0)
            {
                duration = Math.Max(duration, track.FragmentEnd / track.Timescale);
            }
        }

        if (duration <= 0 && indexDurations.Count > 0)
        {
            duration = indexDurations.Values.Max();
        }

        if (duration <= 0)
        {
            foreach (var track in tracks)
            {
                if (track.Timescale > 0)
                {
                    duration = Math.Max(duration, track.Duration / track.Timescale);
                }
            }
        }

        return duration > 0 ? duration : double.NaN;
    }

    private static void ReadMp4TrackFragment(ReadOnlySpan<byte> traf, List<Mp4Track> tracks, Dictionary<uint, uint> defaultSampleDurations, Dictionary<uint, double> cursors)
    {
        // tfhd: version, flags, track_ID, then the fields selected by the flags: base_data_offset (0x1, 8 bytes),
        // sample_description_index (0x2), default_sample_duration (0x8), default_sample_size (0x10)...
        var tfhd = FindBox(traf, "tfhd"u8);
        var trackId = (uint)ReadBigEndian(tfhd, 4, 4);
        Mp4Track? track = null;
        foreach (var candidate in tracks)
        {
            if (candidate.Id == trackId)
            {
                track = candidate;
                break;
            }
        }

        if (track is null)
            return;

        var flags = ReadBigEndian(tfhd, 1, 3);
        double defaultDuration = defaultSampleDurations.GetValueOrDefault(trackId);
        if ((flags & 0x8) != 0)
        {
            defaultDuration = ReadBigEndian(tfhd, 8 + ((flags & 0x1) != 0 ? 8 : 0) + ((flags & 0x2) != 0 ? 4 : 0), 4);
        }

        // tfdt: version, flags, baseMediaDecodeTime (4 or 8 bytes); without it the fragment follows the previous one
        var tfdt = FindBox(traf, "tfdt"u8);
        var time = cursors.GetValueOrDefault(trackId);
        if (tfdt.Length >= 8)
        {
            time = ReadBigEndian(tfdt, 4, tfdt[0] == 1 ? 8 : 4);
        }

        var offset = 0;
        while (TryReadBox(traf, ref offset, out var type, out var trun))
        {
            if (!type.SequenceEqual("trun"u8))
                continue;

            // trun: version, flags, sample_count, data_offset (0x1), first_sample_flags (0x4), then for each sample:
            // duration (0x100), size (0x200), flags (0x400), composition time offset (0x800)
            flags = ReadBigEndian(trun, 1, 3);
            var sampleCount = (long)ReadBigEndian(trun, 4, 4);
            var position = 8 + ((flags & 0x1) != 0 ? 4 : 0) + ((flags & 0x4) != 0 ? 4 : 0);
            var recordSize = 4 * BitOperations.PopCount((uint)(flags & 0xF00));
            if ((flags & 0x100) == 0)
            {
                time += sampleCount * defaultDuration;
                continue;
            }

            sampleCount = Math.Min(sampleCount, Math.Max(0, (trun.Length - position) / recordSize));
            for (var i = 0L; i < sampleCount; i++)
            {
                time += ReadBigEndian(trun, position + (i * recordSize), 4);
            }
        }

        cursors[trackId] = time;
        track.FragmentEnd = Math.Max(track.FragmentEnd, time);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Matroska and WebM (EBML, RFC 8794 and RFC 9559)
    // ---------------------------------------------------------------------------------------------------------------

    private const uint MatroskaSegment = 0x18538067;
    private const uint MatroskaInfo = 0x1549A966;
    private const uint MatroskaTimestampScale = 0x2AD7B1;
    private const uint MatroskaDuration = 0x4489;
    private const uint MatroskaTracks = 0x1654AE6B;
    private const uint MatroskaTrackEntry = 0xAE;
    private const uint MatroskaTrackNumber = 0xD7;
    private const uint MatroskaTrackType = 0x83;
    private const uint MatroskaCodecId = 0x86;
    private const uint MatroskaName = 0x536E;
    private const uint MatroskaLanguage = 0x22B59C;
    private const uint MatroskaLanguageBcp47 = 0x22B59D;
    private const uint MatroskaDefaultDuration = 0x23E383;
    private const uint MatroskaVideo = 0xE0;
    private const uint MatroskaPixelWidth = 0xB0;
    private const uint MatroskaPixelHeight = 0xBA;
    private const uint MatroskaPixelCropBottom = 0x54AA;
    private const uint MatroskaPixelCropTop = 0x54BB;
    private const uint MatroskaPixelCropLeft = 0x54CC;
    private const uint MatroskaPixelCropRight = 0x54DD;
    private const uint MatroskaDisplayWidth = 0x54B0;
    private const uint MatroskaDisplayHeight = 0x54BA;
    private const uint MatroskaDisplayUnit = 0x54B2;
    private const uint MatroskaCluster = 0x1F43B675;
    private const uint MatroskaClusterTimestamp = 0xE7;
    private const uint MatroskaSimpleBlock = 0xA3;
    private const uint MatroskaBlockGroup = 0xA0;
    private const uint MatroskaBlock = 0xA1;
    private const uint MatroskaBlockDuration = 0x9B;

    private sealed class MatroskaTrack
    {
        public ulong Number { get; set; }
        public ulong Type { get; set; }
        public string Codec { get; set; } = "";
        public string Language { get; set; } = "eng";
        public string? LanguageBcp47 { get; set; }
        public string Name { get; set; } = "";
        public long PixelWidth { get; set; }
        public long PixelHeight { get; set; }
        public long CropHorizontal { get; set; }
        public long CropVertical { get; set; }
        public long DisplayWidth { get; set; }
        public long DisplayHeight { get; set; }
        public ulong DisplayUnit { get; set; }

        /// <summary>The duration of each frame in nanoseconds (0 when it is not known).</summary>
        public double DefaultDuration { get; set; }

        // The display size defaults to the cropped pixel size; it is only in pixels when DisplayUnit is 0
        public int Width => ClampSize(DisplayUnit == 0 && DisplayWidth > 0 && DisplayHeight > 0 ? DisplayWidth : PixelWidth - CropHorizontal);
        public int Height => ClampSize(DisplayUnit == 0 && DisplayWidth > 0 && DisplayHeight > 0 ? DisplayHeight : PixelHeight - CropVertical);

        private static int ClampSize(long value) => (int)Math.Clamp(value, 0, 1 << 20);
    }

    /// <summary>
    /// Reads the header of an EBML element: the identifier (1 to 4 bytes, the length marker is kept) and the size of the
    /// data (1 to 8 bytes, the length marker is removed; -1 when all the bits are set: unknown size).
    /// </summary>
    private static bool TryReadEbmlElement(ReadOnlySpan<byte> data, scoped ref int offset, out uint id, out long size)
    {
        id = 0;
        size = 0;
        if (offset < 0 || offset >= data.Length)
            return false;

        var idLength = BitOperations.LeadingZeroCount(data[offset]) - 23;
        if (idLength > 4 || data.Length - offset < idLength)
            return false;

        id = (uint)ReadBigEndian(data, offset, idLength);
        var position = offset + idLength;
        if (!TryReadEbmlVariableInteger(data, ref position, out size))
            return false;

        offset = position;
        return true;
    }

    private static bool TryReadEbmlVariableInteger(ReadOnlySpan<byte> data, scoped ref int offset, out long value)
    {
        value = 0;
        if (offset < 0 || offset >= data.Length)
            return false;

        var length = BitOperations.LeadingZeroCount(data[offset]) - 23;
        if (length > 8 || data.Length - offset < length)
            return false;

        // Remove the length marker: the first bit set
        var mask = (1UL << (7 * length)) - 1;
        var raw = ReadBigEndian(data, offset, length) & mask;
        value = raw == mask ? -1 : (long)raw;
        offset += length;
        return true;
    }

    private static double ReadEbmlFloat(ReadOnlySpan<byte> data)
    {
        return data.Length switch
        {
            4 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data)),
            8 => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data)),
            _ => double.NaN,
        };
    }

    private static long ReadEbmlInteger(ReadOnlySpan<byte> data)
    {
        return data.Length <= 8 ? (long)Math.Min(ReadBigEndian(data, 0, data.Length), long.MaxValue) : 0;
    }

    private static WptMediaInfo? ParseMatroska(ReadOnlySpan<byte> data)
    {
        var tracks = new List<MatroskaTrack>();
        var timestampScale = 1_000_000d; // nanoseconds per tick
        var duration = double.NaN; // ticks
        var clusterTimestamp = 0L;
        var lastBlockEnd = double.NaN; // ticks
        MatroskaTrack? video = null;
        VideoSizeChanges? sizeChanges = null;

        // The elements of the Segment and of the Clusters are read by the same loop: these master elements can have an
        // unknown size (live WebM), their content then ends where the next element of the parent starts
        var offset = 0;
        while (TryReadEbmlElement(data, ref offset, out var id, out var size))
        {
            if (id is MatroskaSegment or MatroskaCluster)
                continue;

            if (size < 0)
                break;

            // A truncated element keeps the available data
            var content = data.Slice(offset, (int)Math.Min(size, data.Length - offset));
            switch (id)
            {
                case MatroskaInfo:
                    var infoOffset = 0;
                    while (TryReadEbmlChild(content, ref infoOffset, out var infoId, out var infoValue))
                    {
                        if (infoId == MatroskaTimestampScale && ReadEbmlInteger(infoValue) > 0)
                        {
                            timestampScale = ReadEbmlInteger(infoValue);
                        }
                        else if (infoId == MatroskaDuration)
                        {
                            duration = ReadEbmlFloat(infoValue);
                        }
                    }

                    break;

                case MatroskaTracks when tracks.Count == 0:
                    var tracksOffset = 0;
                    while (TryReadEbmlChild(content, ref tracksOffset, out var entryId, out var entry))
                    {
                        if (entryId == MatroskaTrackEntry && tracks.Count < MaxTracks)
                        {
                            tracks.Add(ParseMatroskaTrack(entry));
                        }
                    }

                    video = tracks.Find(static track => track.Type == 1);
                    break;

                case MatroskaClusterTimestamp:
                    clusterTimestamp = ReadEbmlInteger(content);
                    break;

                case MatroskaSimpleBlock:
                    ReadMatroskaBlock(content, blockDuration: double.NaN);
                    break;

                case MatroskaBlockGroup:
                    var block = default(ReadOnlySpan<byte>);
                    var blockDuration = double.NaN;
                    var groupOffset = 0;
                    while (TryReadEbmlChild(content, ref groupOffset, out var groupId, out var groupValue))
                    {
                        if (groupId == MatroskaBlock)
                        {
                            block = groupValue;
                        }
                        else if (groupId == MatroskaBlockDuration)
                        {
                            blockDuration = ReadEbmlInteger(groupValue);
                        }
                    }

                    ReadMatroskaBlock(block, blockDuration);
                    break;
            }

            if (size > data.Length - offset)
                break;

            offset += (int)size;
        }

        var seconds = duration * timestampScale / 1_000_000_000;
        if (!(seconds > 0))
        {
            // No Duration (e.g. the files of MediaRecorder): the end of the last block
            seconds = lastBlockEnd * timestampScale / 1_000_000_000;
        }

        var result = new List<WptMediaTrack>();
        foreach (var track in tracks)
        {
            // 1: video, 2: audio (0x11: subtitles, 0x21: metadata... are not media tracks)
            if (track.Type is 1 or 2)
            {
                var isVideo = track.Type == 1;
                result.Add(new WptMediaTrack(track.Number.ToString(CultureInfo.InvariantCulture), isVideo, track.Codec, NormalizeLanguage(track.LanguageBcp47 ?? track.Language), track.Name, isVideo ? track.Width : 0, isVideo ? track.Height : 0));
            }
        }

        return CreateInfo("webm", seconds, result, sizeChanges);

        // Block and SimpleBlock: track number (variable size integer), timestamp relative to the Cluster (16 bits,
        // signed), flags (8 bits), then the frame (or the laced frames)
        void ReadMatroskaBlock(ReadOnlySpan<byte> block, double blockDuration)
        {
            var position = 0;
            if (!TryReadEbmlVariableInteger(block, ref position, out var trackNumber) || block.Length - position < 3)
                return;

            var time = (double)clusterTimestamp + BinaryPrimitives.ReadInt16BigEndian(block[position..]);
            var flags = block[position + 2];
            MatroskaTrack? track = null;
            foreach (var candidate in tracks)
            {
                if (candidate.Number == (ulong)trackNumber)
                {
                    track = candidate;
                    break;
                }
            }

            if (double.IsNaN(blockDuration))
            {
                blockDuration = track is { DefaultDuration: > 0 } ? track.DefaultDuration / timestampScale : 0;
            }

            if (double.IsNaN(lastBlockEnd) || time + blockDuration > lastBlockEnd)
            {
                lastBlockEnd = time + blockDuration;
            }

            // The size of the frames of the first video track: only the key frames can change it. Laced frames (flags
            // 0x06) are not used for video
            if (track is not null && ReferenceEquals(track, video) && (flags & 0x06) == 0)
            {
                var frame = block[(position + 3)..];
                var width = 0;
                var height = 0;
                if ((track.Codec == "V_VP8" && TryReadVp8KeyFrameSize(frame, out width, out height))
                    || (track.Codec == "V_VP9" && TryReadVp9KeyFrameSize(frame, out width, out height)))
                {
                    sizeChanges ??= new VideoSizeChanges(track.Width, track.Height);
                    sizeChanges.Add(time * timestampScale / 1_000_000_000, width, height);
                }
            }
        }
    }

    /// <summary>Reads a child element whose size is known and in the data of the parent (stops at a truncated child).</summary>
    private static bool TryReadEbmlChild(ReadOnlySpan<byte> data, scoped ref int offset, out uint id, out ReadOnlySpan<byte> content)
    {
        content = default;
        if (!TryReadEbmlElement(data, ref offset, out id, out var size) || size < 0 || size > data.Length - offset)
            return false;

        content = data.Slice(offset, (int)size);
        offset += (int)size;
        return true;
    }

    private static MatroskaTrack ParseMatroskaTrack(ReadOnlySpan<byte> entry)
    {
        var track = new MatroskaTrack();
        var offset = 0;
        while (TryReadEbmlChild(entry, ref offset, out var id, out var value))
        {
            switch (id)
            {
                case MatroskaTrackNumber:
                    track.Number = (ulong)ReadEbmlInteger(value);
                    break;
                case MatroskaTrackType:
                    track.Type = (ulong)ReadEbmlInteger(value);
                    break;
                case MatroskaCodecId:
                    track.Codec = ReadString(value);
                    break;
                case MatroskaName:
                    track.Name = ReadString(value);
                    break;
                case MatroskaLanguage:
                    track.Language = ReadString(value);
                    break;
                case MatroskaLanguageBcp47:
                    track.LanguageBcp47 = ReadString(value);
                    break;
                case MatroskaDefaultDuration:
                    track.DefaultDuration = ReadEbmlInteger(value);
                    break;
                case MatroskaVideo:
                    var videoOffset = 0;
                    while (TryReadEbmlChild(value, ref videoOffset, out var videoId, out var videoValue))
                    {
                        var number = ReadEbmlInteger(videoValue);
                        switch (videoId)
                        {
                            case MatroskaPixelWidth:
                                track.PixelWidth = number;
                                break;
                            case MatroskaPixelHeight:
                                track.PixelHeight = number;
                                break;
                            case MatroskaPixelCropLeft or MatroskaPixelCropRight:
                                track.CropHorizontal += Math.Min(number, 1 << 20);
                                break;
                            case MatroskaPixelCropTop or MatroskaPixelCropBottom:
                                track.CropVertical += Math.Min(number, 1 << 20);
                                break;
                            case MatroskaDisplayWidth:
                                track.DisplayWidth = number;
                                break;
                            case MatroskaDisplayHeight:
                                track.DisplayHeight = number;
                                break;
                            case MatroskaDisplayUnit:
                                track.DisplayUnit = (ulong)number;
                                break;
                        }
                    }

                    break;
            }
        }

        return track;
    }

    /// <summary>
    /// Reads the size of a VP8 key frame (RFC 6386, 9.1): frame tag (3 bytes, the first bit is 0 for a key frame),
    /// start code 9D 01 2A, then the width and the height (14 bits each, little endian, with 2 bits of scaling).
    /// </summary>
    private static bool TryReadVp8KeyFrameSize(ReadOnlySpan<byte> frame, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (frame.Length < 10 || (frame[0] & 1) != 0 || frame[3] != 0x9D || frame[4] != 0x01 || frame[5] != 0x2A)
            return false;

        width = BinaryPrimitives.ReadUInt16LittleEndian(frame[6..]) & 0x3FFF;
        height = BinaryPrimitives.ReadUInt16LittleEndian(frame[8..]) & 0x3FFF;
        return width > 0 && height > 0;
    }

    /// <summary>Reads the size of a VP9 key frame from uncompressed_header() (VP9 bitstream specification, 6.2).</summary>
    private static bool TryReadVp9KeyFrameSize(ReadOnlySpan<byte> frame, out int width, out int height)
    {
        width = 0;
        height = 0;
        var reader = new BitReader(Slice(frame, 0, 16));
        if (reader.ReadBits(2) != 2) // frame_marker
            return false;

        var profile = reader.ReadBits(1) | (reader.ReadBits(1) << 1);
        if (profile == 3)
        {
            reader.ReadFlag(); // reserved_zero
        }

        if (reader.ReadFlag()) // show_existing_frame
            return false;

        if (reader.ReadFlag()) // frame_type: 0 is a key frame
            return false;

        reader.ReadBits(2); // show_frame, error_resilient_mode
        if (reader.ReadBits(24) != 0x498342) // frame_sync_code
            return false;

        // color_config()
        if (profile >= 2)
        {
            reader.ReadFlag(); // ten_or_twelve_bit
        }

        if (reader.ReadBits(3) != 7)
        {
            // Not sRGB: color_range, and the subsampling in the profiles 1 and 3
            reader.ReadFlag();
            if (profile is 1 or 3)
            {
                reader.ReadBits(3); // subsampling_x, subsampling_y, reserved_zero
            }
        }
        else if (profile is 1 or 3)
        {
            reader.ReadFlag(); // reserved_zero
        }

        // frame_size()
        width = (int)reader.ReadBits(16) + 1;
        height = (int)reader.ReadBits(16) + 1;
        return reader.IsValid;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Ogg (RFC 3533) with Vorbis, Opus, Theora, FLAC and Speex
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class OggStream
    {
        public uint SerialNumber { get; set; }
        public string? Codec { get; set; }
        public bool IsVideo { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long LastGranulePosition { get; set; } = -1;

        /// <summary>The number of granules (samples or frames) per second.</summary>
        public double Rate { get; set; }

        public int PreSkip { get; set; }
        public int GranuleShift { get; set; }
        public bool GranuleIsFrameCount { get; set; }
    }

    private static WptMediaInfo? ParseOgg(ReadOnlySpan<byte> data)
    {
        // Page: "OggS", version (1), header type (1: 0x02 for the first page of a stream), granule position (8),
        // serial number (4), sequence number (4), checksum (4), segment count (1), segment sizes, then the segments
        var streams = new List<OggStream>();
        var offset = 0;
        while (data.Length - offset >= 27)
        {
            if (!data.Slice(offset, 4).SequenceEqual("OggS"u8))
            {
                // Resynchronize on the next page
                var next = data[(offset + 1)..].IndexOf("OggS"u8);
                if (next < 0)
                    break;

                offset += next + 1;
                continue;
            }

            var headerType = data[offset + 5];
            var granulePosition = BinaryPrimitives.ReadInt64LittleEndian(data[(offset + 6)..]);
            var serialNumber = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 14)..]);
            int segmentCount = data[offset + 26];
            if (data.Length - offset - 27 < segmentCount)
                break;

            var bodySize = 0;
            foreach (var segmentSize in data.Slice(offset + 27, segmentCount))
            {
                bodySize += segmentSize;
            }

            var bodyOffset = offset + 27 + segmentCount;
            OggStream? stream = null;
            foreach (var candidate in streams)
            {
                if (candidate.SerialNumber == serialNumber)
                {
                    stream = candidate;
                    break;
                }
            }

            if (stream is null && (headerType & 0x02) != 0 && streams.Count < MaxTracks)
            {
                stream = ParseOggStream(Slice(data, bodyOffset, bodySize));
                stream.SerialNumber = serialNumber;
                streams.Add(stream);
            }

            // The granule position is -1 when no packet ends on the page
            if (stream is not null && granulePosition > stream.LastGranulePosition)
            {
                stream.LastGranulePosition = granulePosition;
            }

            offset = bodyOffset + bodySize;
        }

        var tracks = new List<WptMediaTrack>();
        var duration = double.NaN;
        foreach (var stream in streams)
        {
            // The Skeleton stream ("fishead") and the unknown streams are not tracks
            if (stream.Codec is null)
                continue;

            tracks.Add(new WptMediaTrack((tracks.Count + 1).ToString(CultureInfo.InvariantCulture), stream.IsVideo, stream.Codec, "", "", stream.Width, stream.Height));
            // Only the header pages (granule position 0) are available in a truncated file
            if (stream.LastGranulePosition <= 0 || stream.Rate <= 0)
                continue;

            double granules = stream.LastGranulePosition - stream.PreSkip;
            if (stream.IsVideo)
            {
                // Theora: the frame index of the last key frame, then the count of frames since this key frame
                var keyFrame = stream.LastGranulePosition >> stream.GranuleShift;
                var delta = stream.LastGranulePosition & ((1L << stream.GranuleShift) - 1);
                granules = keyFrame + delta + (stream.GranuleIsFrameCount ? 0 : 1);
            }

            var streamDuration = Math.Max(0, granules / stream.Rate);
            if (double.IsNaN(duration) || streamDuration > duration)
            {
                duration = streamDuration;
            }
        }

        return CreateInfo("ogg", duration, tracks);
    }

    /// <summary>Identifies the codec of a logical bitstream from its first packet.</summary>
    private static OggStream ParseOggStream(ReadOnlySpan<byte> packet)
    {
        var stream = new OggStream();
        if (packet.StartsWith("\u0001vorbis"u8))
        {
            // vorbis_version (4), audio_channels (1), audio_sample_rate (4, little endian)
            stream.Codec = "vorbis";
            stream.Rate = ReadLittleEndian(packet, 12, 4);
        }
        else if (packet.StartsWith("OpusHead"u8))
        {
            // version (1), channel count (1), pre-skip (2, little endian); the granules are samples at 48 kHz
            stream.Codec = "opus";
            stream.Rate = 48000;
            stream.PreSkip = (int)ReadLittleEndian(packet, 10, 2);
        }
        else if (packet.Length >= 42 && packet[0] == 0x80 && packet[1..].StartsWith("theora"u8))
        {
            // version (3), frame size in macroblocks (2 + 2), picture width and height (3 + 3), picture offset (1 + 1),
            // frame rate numerator and denominator (4 + 4), pixel aspect ratio (3 + 3), color space (1),
            // nominal bitrate (3), then quality (6 bits), key frame granule shift (5 bits)...
            stream.Codec = "theora";
            stream.IsVideo = true;
            stream.Width = (int)ReadBigEndian(packet, 14, 3);
            stream.Height = (int)ReadBigEndian(packet, 17, 3);
            var frameRateDenominator = ReadBigEndian(packet, 26, 4);
            stream.Rate = frameRateDenominator == 0 ? 0 : (double)ReadBigEndian(packet, 22, 4) / frameRateDenominator;
            stream.GranuleShift = (int)((ReadBigEndian(packet, 40, 2) >> 5) & 0x1F);

            // The granule position counts the frames from 1 since the version 3.2.1 of the bitstream
            stream.GranuleIsFrameCount = ReadBigEndian(packet, 7, 3) >= 0x030201;

            // The display size applies the pixel aspect ratio
            var aspectNumerator = ReadBigEndian(packet, 30, 3);
            var aspectDenominator = ReadBigEndian(packet, 33, 3);
            if (aspectNumerator > 0 && aspectDenominator > 0 && stream.Height > 0)
            {
                (stream.Width, stream.Height) = ApplyAspectRatio(stream.Width, stream.Height, (double)stream.Width * aspectNumerator / ((double)stream.Height * aspectDenominator));
            }
        }
        else if (packet.StartsWith("\u007FFLAC"u8))
        {
            // Mapping version (2), header packets (2), "fLaC", metadata block header (4), then STREAMINFO: block and
            // frame sizes (10 bytes), sample rate (20 bits)...
            stream.Codec = "flac";
            stream.Rate = ReadBigEndian(packet, 27, 3) >> 4;
        }
        else if (packet.StartsWith("Speex   "u8))
        {
            // speex_version (20), speex_version_id (4), header_size (4), rate (4, little endian)
            stream.Codec = "speex";
            stream.Rate = ReadLittleEndian(packet, 36, 4);
        }

        return stream;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // WAVE (RIFF)
    // ---------------------------------------------------------------------------------------------------------------

    private static WptMediaInfo? ParseWav(ReadOnlySpan<byte> data)
    {
        // "RIFF", size (4), "WAVE", then the chunks: identifier (4), size (4, little endian), data, padding to an even size
        var hasFormat = false;
        var format = 0;
        var sampleRate = 0d;
        var byteRate = 0d;
        var blockAlign = 0d;
        var sampleCount = double.NaN;
        var duration = double.NaN;
        long offset = 12;
        while (data.Length - offset >= 8)
        {
            var id = data.Slice((int)offset, 4);
            var size = (long)ReadLittleEndian(data, offset + 4, 4);
            var chunk = Slice(data, offset + 8, size);
            if (id.SequenceEqual("fmt "u8) && chunk.Length >= 16)
            {
                // format tag (2), channels (2), samples per second (4), bytes per second (4), block align (2), bits per sample (2)
                hasFormat = true;
                format = (int)ReadLittleEndian(chunk, 0, 2);
                sampleRate = ReadLittleEndian(chunk, 4, 4);
                byteRate = ReadLittleEndian(chunk, 8, 4);
                blockAlign = ReadLittleEndian(chunk, 12, 2);
            }
            else if (id.SequenceEqual("fact"u8) && chunk.Length >= 4)
            {
                // The number of samples of the compressed formats
                sampleCount = ReadLittleEndian(chunk, 0, 4);
            }
            else if (id.SequenceEqual("data"u8))
            {
                // The size is not reliable for a streamed or truncated file: use the available data
                if (!hasFormat)
                    break;

                var isPcm = format is 1 or 3 or 6 or 7 or 0xFFFE;
                if (!isPcm && sampleCount > 0 && sampleRate > 0)
                {
                    duration = sampleCount / sampleRate;
                }
                else if (isPcm && blockAlign > 0 && sampleRate > 0)
                {
                    duration = Math.Floor(chunk.Length / blockAlign) / sampleRate;
                }
                else if (byteRate > 0)
                {
                    duration = chunk.Length / byteRate;
                }

                break;
            }

            offset += 8 + size + (size & 1);
        }

        if (!hasFormat)
            return null;

        var codec = format switch
        {
            1 or 0xFFFE => "pcm",
            3 => "pcm-float",
            6 => "alaw",
            7 => "ulaw",
            0x55 => "mp3",
            _ => "wav",
        };
        return CreateInfo("wav", duration, [new WptMediaTrack("1", IsVideo: false, codec, "", "", 0, 0)]);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // MPEG audio (MP3), ADTS (AAC) and FLAC streams
    // ---------------------------------------------------------------------------------------------------------------

    private readonly record struct MpegAudioFrame(int Version, int Layer, int SampleRate, int SamplesPerFrame, int FrameSize, bool IsMono)
    {
        public bool IsCompatibleWith(MpegAudioFrame other) => Version == other.Version && Layer == other.Layer && SampleRate == other.SampleRate;
    }

    // The bitrates in kbit/s by index: MPEG-1 layers I, II and III, then MPEG-2 and 2.5 layer I, and layers II and III
    private static ReadOnlySpan<ushort> MpegAudioBitrates =>
    [
        0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0,
        0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0,
        0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0,
        0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0,
        0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0,
    ];

    /// <summary>Returns the size of the ID3v2 tags at the start of the data: "ID3", version (2), flags (1), size (4 x 7 bits).</summary>
    private static int SkipId3v2(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        while (data.Length - offset >= 10 && data.Slice(offset, 3).SequenceEqual("ID3"u8))
        {
            var size = ((data[offset + 6] & 0x7F) << 21) | ((data[offset + 7] & 0x7F) << 14) | ((data[offset + 8] & 0x7F) << 7) | (data[offset + 9] & 0x7F);

            // The flag 0x10 adds a footer of 10 bytes
            var total = 10 + size + ((data[offset + 5] & 0x10) != 0 ? 10 : 0);
            if (total > data.Length - offset)
                return data.Length;

            offset += total;
        }

        return offset;
    }

    /// <summary>
    /// Reads the header of an MPEG audio frame: sync (11 bits), version (2), layer (2), protection (1), bitrate index (4),
    /// sample rate index (2), padding (1), private (1), channel mode (2)...
    /// </summary>
    private static bool TryReadMpegAudioHeader(ReadOnlySpan<byte> data, long offset, out MpegAudioFrame frame)
    {
        frame = default;
        if (offset < 0 || offset > data.Length - 4)
            return false;

        var header = data.Slice((int)offset, 4);
        if (header[0] != 0xFF || (header[1] & 0xE0) != 0xE0)
            return false;

        var version = (header[1] >> 3) & 3; // 0: MPEG-2.5, 2: MPEG-2, 3: MPEG-1
        var layer = 4 - ((header[1] >> 1) & 3);
        var bitrateIndex = header[2] >> 4;
        var sampleRateIndex = (header[2] >> 2) & 3;
        var padding = (header[2] >> 1) & 1;

        // The free format (bitrate index 0) is not supported: the size of the frames is not in the header
        if (version == 1 || layer == 4 || bitrateIndex is 0 or 15 || sampleRateIndex == 3)
            return false;

        var isMpeg1 = version == 3;
        var bitrateTable = isMpeg1 ? layer - 1 : (layer == 1 ? 3 : 4);
        var bitrate = MpegAudioBitrates[(bitrateTable * 16) + bitrateIndex] * 1000;
        var sampleRate = (sampleRateIndex switch { 0 => 44100, 1 => 48000, _ => 32000 }) >> (version switch { 3 => 0, 2 => 1, _ => 2 });
        var samplesPerFrame = layer == 1 ? 384 : (layer == 3 && !isMpeg1 ? 576 : 1152);
        var frameSize = layer == 1 ? ((12 * bitrate / sampleRate) + padding) * 4 : (samplesPerFrame / 8 * bitrate / sampleRate) + padding;
        frame = new MpegAudioFrame(version, layer, sampleRate, samplesPerFrame, frameSize, IsMono: (header[3] >> 6) == 3);
        return frameSize >= 4;
    }

    private static WptMediaInfo? ParseMp3(ReadOnlySpan<byte> data)
    {
        // Find the first frame after the tags: it must be followed by another frame of the same stream
        var offset = (long)SkipId3v2(data);
        var searchEnd = Math.Min(data.Length, offset + MaxFrameSearch);
        MpegAudioFrame first;
        while (true)
        {
            if (offset >= searchEnd)
                return null;

            if (TryReadMpegAudioHeader(data, offset, out first)
                && (offset + first.FrameSize > data.Length - 4 || (TryReadMpegAudioHeader(data, offset + first.FrameSize, out var next) && next.IsCompatibleWith(first))))
            {
                break;
            }

            offset++;
        }

        List<WptMediaTrack> tracks = [new WptMediaTrack("1", IsVideo: false, first.Layer switch { 1 => "mp1", 2 => "mp2", _ => "mp3" }, "", "", 0, 0)];
        var frameCount = 0d;
        var hasHeaderFrame = false;
        var padding = 0d;
        if (first.Layer == 3)
        {
            // The first frame of a file can be a Xing/Info header (after the side information of an empty frame):
            // flags (4), then the number of frames (flag 0x1), the number of bytes (0x2), the table of contents
            // (0x4, 100 bytes), the quality (0x8), then the LAME extension: encoder (9), revision and method (1),
            // lowpass (1), replay gain (8), flags (1), bitrate (1), encoder delay and padding (12 bits each)
            var tag = offset + 4 + (first.Version == 3 ? (first.IsMono ? 17 : 32) : (first.IsMono ? 9 : 17));
            var signature = Slice(data, tag, 4);
            if (signature.SequenceEqual("Xing"u8) || signature.SequenceEqual("Info"u8))
            {
                hasHeaderFrame = true;
                var flags = ReadBigEndian(data, tag + 4, 4);
                var position = tag + 8;
                if ((flags & 0x1) != 0)
                {
                    frameCount = ReadBigEndian(data, position, 4);
                    position += 4;
                }

                position += ((flags & 0x2) != 0 ? 4 : 0) + ((flags & 0x4) != 0 ? 100 : 0) + ((flags & 0x8) != 0 ? 4 : 0);
                var encoder = Slice(data, position, 4);
                if (encoder.SequenceEqual("LAME"u8) || encoder.SequenceEqual("Lavf"u8) || encoder.SequenceEqual("Lavc"u8))
                {
                    var delays = ReadBigEndian(data, position + 21, 3);
                    padding = (delays >> 12) + (delays & 0xFFF);
                }
            }
            else if (Slice(data, offset + 36, 4).SequenceEqual("VBRI"u8))
            {
                // VBRI header (Fraunhofer): version (2), delay (2), quality (2), bytes (4), frames (4)
                hasHeaderFrame = true;
                frameCount = ReadBigEndian(data, offset + 50, 4);
            }
        }

        if (frameCount <= 0)
        {
            // Count the frames (a trailing ID3v1 or APE tag is not a frame)
            padding = 0;
            var position = offset;
            while (TryReadMpegAudioHeader(data, position, out var frame) && frame.IsCompatibleWith(first))
            {
                frameCount++;
                position += frame.FrameSize;
            }

            if (hasHeaderFrame)
            {
                frameCount--;
            }
        }

        var sampleCount = (frameCount * first.SamplesPerFrame) - padding;
        return CreateInfo("mp3", sampleCount > 0 ? sampleCount / first.SampleRate : double.NaN, tracks);
    }

    /// <summary>
    /// Reads the header of an ADTS frame: sync (12 bits), version (1), layer (2, always 0), protection (1), profile (2),
    /// sampling frequency index (4), private (1), channels (3), 4 flags, frame length (13), fullness (11), blocks - 1 (2).
    /// </summary>
    private static bool TryReadAdtsHeader(ReadOnlySpan<byte> data, long offset, out int frameSize, out int sampleRate, out int sampleCount)
    {
        frameSize = 0;
        sampleRate = 0;
        sampleCount = 0;
        if (offset < 0 || offset > data.Length - 7)
            return false;

        var header = data.Slice((int)offset, 7);
        if (header[0] != 0xFF || (header[1] & 0xF6) != 0xF0)
            return false;

        ReadOnlySpan<int> sampleRates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];
        var sampleRateIndex = (header[2] >> 2) & 0xF;
        if (sampleRateIndex >= sampleRates.Length)
            return false;

        sampleRate = sampleRates[sampleRateIndex];
        frameSize = ((header[3] & 0x03) << 11) | (header[4] << 3) | (header[5] >> 5);
        sampleCount = ((header[6] & 0x03) + 1) * 1024;
        return frameSize >= 7;
    }

    private static WptMediaInfo? ParseAdts(ReadOnlySpan<byte> data)
    {
        long offset = SkipId3v2(data);
        var samples = 0d;
        var rate = 0;
        while (TryReadAdtsHeader(data, offset, out var frameSize, out var sampleRate, out var sampleCount))
        {
            rate = sampleRate;
            samples += sampleCount;
            offset += frameSize;
        }

        return CreateInfo("aac", rate > 0 ? samples / rate : double.NaN, [new WptMediaTrack("1", IsVideo: false, "aac", "", "", 0, 0)]);
    }

    private static WptMediaInfo? ParseFlac(ReadOnlySpan<byte> data)
    {
        // "fLaC", then the metadata blocks: last block flag (1 bit), type (7 bits), size (3 bytes). STREAMINFO (type 0):
        // block sizes (4), frame sizes (6), sample rate (20 bits), channels (3 bits), bits per sample (5 bits),
        // total samples (36 bits)
        long offset = SkipId3v2(data) + 4;
        var duration = double.NaN;
        while (data.Length - offset >= 4)
        {
            var blockHeader = data[(int)offset];
            var size = (long)ReadBigEndian(data, offset + 1, 3);
            if ((blockHeader & 0x7F) == 0)
            {
                var streamInfo = Slice(data, offset + 4, size);
                var sampleRate = ReadBigEndian(streamInfo, 10, 3) >> 4;
                var totalSamples = ReadBigEndian(streamInfo, 13, 5) & 0xF_FFFF_FFFF;
                if (sampleRate > 0 && totalSamples > 0)
                {
                    duration = (double)totalSamples / sampleRate;
                }

                break;
            }

            if ((blockHeader & 0x80) != 0)
                break;

            offset += 4 + size;
        }

        return CreateInfo("flac", duration, [new WptMediaTrack("1", IsVideo: false, "flac", "", "", 0, 0)]);
    }
}
