using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>The description of a track of a <see cref="WebMWriter"/>.</summary>
/// <param name="Number">The track number (1 to 126).</param>
/// <param name="IsVideo">Whether the track is a video track.</param>
/// <param name="CodecId">The Matroska codec identifier (e.g. <c>V_VP8</c>, <c>A_OPUS</c>).</param>
/// <param name="CodecPrivate">The codec private data (e.g. the <c>OpusHead</c> of Opus), or null.</param>
/// <param name="Width">The width of the frames of a video track.</param>
/// <param name="Height">The height of the frames of a video track.</param>
/// <param name="SampleRate">The sample rate of an audio track.</param>
/// <param name="Channels">The number of channels of an audio track.</param>
/// <param name="CodecDelay">The delay of the codec in nanoseconds (the pre-skip of Opus), or 0.</param>
internal sealed record WebMTrack(int Number, bool IsVideo, string CodecId, byte[]? CodecPrivate = null, int Width = 0, int Height = 0, double SampleRate = 0, int Channels = 0, long CodecDelay = 0);

/// <summary>
/// Writes a live WebM stream (https://www.webmproject.org/docs/container/, a subset of Matroska): the EBML header, a
/// segment and clusters of unknown size, so the stream can be written while it is recorded and split into blobs. The
/// timestamps are in milliseconds (a timecode scale of 1 ms); a cluster starts at each video key frame, when the
/// relative timestamp of a block does not fit in 16 bits, and after <see cref="StartCluster"/>.
/// </summary>
internal sealed class WebMWriter
{
    private const uint EbmlId = 0x1A45DFA3;
    private const uint SegmentId = 0x18538067;
    private const uint InfoId = 0x1549A966;
    private const uint TracksId = 0x1654AE6B;
    private const uint TrackEntryId = 0xAE;
    private const uint ClusterId = 0x1F43B675;
    private const uint TimecodeId = 0xE7;
    private const uint SimpleBlockId = 0xA3;

    /// <summary>The size of an element of unknown size (8 bytes, all the bits of the value set).</summary>
    private static readonly byte[] UnknownSize = [0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    private readonly List<byte> _output = [];
    private readonly IReadOnlyList<WebMTrack> _tracks;
    private bool _headerWritten;
    private long? _clusterTimecode;

    private readonly string _docType;

    /// <summary>Creates a writer: the header of the stream is written with the first frame or by <see cref="WriteHeader"/>.</summary>
    /// <param name="tracks">The tracks of the stream.</param>
    /// <param name="docType">The document type: <c>webm</c>, or <c>matroska</c> for a Matroska stream.</param>
    public WebMWriter(IReadOnlyList<WebMTrack> tracks, string docType = "webm")
    {
        _docType = docType;
        if (tracks.Count is 0 or > 126)
            throw new ArgumentException("A WebM stream has 1 to 126 tracks", nameof(tracks));

        _tracks = tracks;
    }

    /// <summary>The number of bytes written since the last <see cref="TakeBytes"/>.</summary>
    public long PendingLength => _output.Count;

    /// <summary>Writes a frame of a track: the header is written first; a cluster starts when needed.</summary>
    /// <param name="trackNumber">The number of the track.</param>
    /// <param name="timestamp">The timestamp of the frame in milliseconds, relative to the start of the recording (not negative).</param>
    /// <param name="isKeyFrame">Whether the frame is a key frame.</param>
    /// <param name="data">The encoded frame.</param>
    public void WriteFrame(int trackNumber, long timestamp, bool isKeyFrame, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timestamp);
        var track = _tracks.FirstOrDefault(t => t.Number == trackNumber) ?? throw new ArgumentOutOfRangeException(nameof(trackNumber));
        WriteHeader();
        if (_clusterTimecode is not { } clusterTimecode || timestamp - clusterTimecode is > short.MaxValue or < 0 || (track.IsVideo && isKeyFrame && timestamp != clusterTimecode))
        {
            StartClusterAt(timestamp);
            clusterTimecode = timestamp;
        }

        // https://www.matroska.org/technical/basics.html#simpleblock-structure: the track number, the relative timestamp and the flags
        var block = new byte[1 + 2 + 1 + data.Length];
        block[0] = (byte)(0x80 | trackNumber);
        BinaryPrimitives.WriteInt16BigEndian(block.AsSpan(1), (short)(timestamp - clusterTimecode));
        block[3] = isKeyFrame || !track.IsVideo ? (byte)0x80 : (byte)0;
        data.CopyTo(block.AsSpan(4));
        WriteElement(_output, SimpleBlockId, block);
    }

    /// <summary>Starts a new cluster with the next frame (e.g. at the start of a blob).</summary>
    public void StartCluster() => _clusterTimecode = null;

    /// <summary>Returns the bytes written since the last call, and clears them.</summary>
    public byte[] TakeBytes()
    {
        var bytes = _output.ToArray();
        _output.Clear();
        return bytes;
    }

    private void StartClusterAt(long timestamp)
    {
        _output.AddRange(EncodeId(ClusterId));
        _output.AddRange(UnknownSize);
        WriteElement(_output, TimecodeId, EncodeUnsigned((ulong)timestamp));
        _clusterTimecode = timestamp;
    }

    /// <summary>Writes the header of the stream (the EBML header, the segment, the information and the tracks), once.</summary>
    public void WriteHeader()
    {
        if (_headerWritten)
            return;

        _headerWritten = true;
        var ebml = new List<byte>();
        WriteElement(ebml, 0x4286, EncodeUnsigned(1)); // EBMLVersion
        WriteElement(ebml, 0x42F7, EncodeUnsigned(1)); // EBMLReadVersion
        WriteElement(ebml, 0x42F2, EncodeUnsigned(4)); // EBMLMaxIDLength
        WriteElement(ebml, 0x42F3, EncodeUnsigned(8)); // EBMLMaxSizeLength
        WriteElement(ebml, 0x4282, Encoding.ASCII.GetBytes(_docType)); // DocType
        WriteElement(ebml, 0x4287, EncodeUnsigned(4)); // DocTypeVersion
        WriteElement(ebml, 0x4285, EncodeUnsigned(2)); // DocTypeReadVersion
        WriteElement(_output, EbmlId, ebml.ToArray());

        _output.AddRange(EncodeId(SegmentId));
        _output.AddRange(UnknownSize);

        var info = new List<byte>();
        WriteElement(info, 0x2AD7B1, EncodeUnsigned(1_000_000)); // TimestampScale: 1 ms
        WriteElement(info, 0x4D80, Encoding.ASCII.GetBytes("Meziantou.Framework.WebPlatform")); // MuxingApp
        WriteElement(info, 0x5741, Encoding.ASCII.GetBytes("Meziantou.Framework.WebPlatform")); // WritingApp
        WriteElement(_output, InfoId, info.ToArray());

        var tracks = new List<byte>();
        foreach (var track in _tracks)
        {
            var entry = new List<byte>();
            WriteElement(entry, 0xD7, EncodeUnsigned((ulong)track.Number)); // TrackNumber
            WriteElement(entry, 0x73C5, EncodeUnsigned((ulong)track.Number)); // TrackUID
            WriteElement(entry, 0x83, EncodeUnsigned(track.IsVideo ? 1u : 2u)); // TrackType
            WriteElement(entry, 0x86, Encoding.ASCII.GetBytes(track.CodecId)); // CodecID
            if (track.CodecPrivate is { } codecPrivate)
                WriteElement(entry, 0x63A2, codecPrivate); // CodecPrivate

            if (track.CodecDelay > 0)
            {
                WriteElement(entry, 0x56AA, EncodeUnsigned((ulong)track.CodecDelay)); // CodecDelay
                WriteElement(entry, 0x56BB, EncodeUnsigned(80_000_000)); // SeekPreRoll: 80 ms
            }

            var settings = new List<byte>();
            if (track.IsVideo)
            {
                WriteElement(settings, 0xB0, EncodeUnsigned((ulong)track.Width)); // PixelWidth
                WriteElement(settings, 0xBA, EncodeUnsigned((ulong)track.Height)); // PixelHeight
                WriteElement(entry, 0xE0, settings.ToArray()); // Video
            }
            else
            {
                var frequency = new byte[8];
                BinaryPrimitives.WriteDoubleBigEndian(frequency, track.SampleRate);
                WriteElement(settings, 0xB5, frequency); // SamplingFrequency
                WriteElement(settings, 0x9F, EncodeUnsigned((ulong)track.Channels)); // Channels
                WriteElement(entry, 0xE1, settings.ToArray()); // Audio
            }

            WriteElement(tracks, TrackEntryId, entry.ToArray());
        }

        WriteElement(_output, TracksId, tracks.ToArray());
    }

    private static void WriteElement(List<byte> output, uint id, ReadOnlySpan<byte> payload)
    {
        output.AddRange(EncodeId(id));
        output.AddRange(EncodeSize(payload.Length));
        output.AddRange(payload);
    }

    /// <summary>The bytes of an element ID (the ID keeps its length marker).</summary>
    private static byte[] EncodeId(uint id)
    {
        var length = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[length - 1 - i] = (byte)(id >> (8 * i));
        }

        return bytes;
    }

    /// <summary>The variable-size integer of a size (https://www.rfc-editor.org/rfc/rfc8794#section-4): the shortest form that is not the reserved value.</summary>
    internal static byte[] EncodeSize(long size)
    {
        var length = 1;
        while (length < 8 && size >= (1L << (7 * length)) - 1)
        {
            length++;
        }

        var bytes = new byte[length];
        var value = (ulong)size | (1UL << (7 * length));
        for (var i = 0; i < length; i++)
        {
            bytes[length - 1 - i] = (byte)(value >> (8 * i));
        }

        return bytes;
    }

    /// <summary>The bytes of an unsigned integer element (big-endian, without leading zeros).</summary>
    private static byte[] EncodeUnsigned(ulong value)
    {
        var length = 1;
        while (length < 8 && value >> (8 * length) != 0)
        {
            length++;
        }

        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[length - 1 - i] = (byte)(value >> (8 * i));
        }

        return bytes;
    }
}
