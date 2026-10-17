using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

internal static partial class WebPCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // RIFF container (WebP container specification) and bitstream headers
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A chunk of a RIFF body or of an ANMF payload: its FourCC (one character per byte) and its payload.</summary>
    private sealed record WebPChunk(string FourCC, byte[] Payload);

    private static byte[] Chunk(string fourcc, byte[] payload)
    {
        Py.Assert(fourcc.Length == 4);
        var output = new ByteBuilder().Ascii(fourcc).U32LE(payload.Length).Bytes(payload);
        if (payload.Length % 2 != 0)
            output.U8(0);
        return output.ToArray();
    }

    private static byte[] Riff(IEnumerable<byte[]> chunks)
    {
        var body = Bytes.Concat([Bytes.Ascii("WEBP"), .. chunks]);
        return new ByteBuilder().Ascii("RIFF").U32LE(body.Length).Bytes(body).ToArray();
    }

    private static byte[] Vp8x(int width, int height, bool icc = false, bool alpha = false, bool exif = false, bool xmp = false, bool animation = false)
    {
        var flags = (icc ? 0x20 : 0) | (alpha ? 0x10 : 0) | (exif ? 0x08 : 0) | (xmp ? 0x04 : 0) | (animation ? 0x02 : 0);
        return Chunk("VP8X", Bytes.Concat([(byte)flags, 0, 0, 0], ToBytes3(width - 1), ToBytes3(height - 1)));
    }

    private static byte[] Anim(Px backgroundBgra, int loopCount) =>
        Chunk("ANIM", new ByteBuilder().Bytes(backgroundBgra.ToArray().Select(v => checked((byte)v)).ToArray()).U16LE(loopCount).ToArray());

    private static byte[] Anmf(int x, int y, int width, int height, int duration, bool blend, bool dispose, IEnumerable<byte[]> frameChunks)
    {
        Py.Assert(x % 2 == 0 && y % 2 == 0, "ANMF offsets are stored divided by two");
        var flags = (blend ? 0 : 0x02) | (dispose ? 0x01 : 0);
        var header = Bytes.Concat(ToBytes3(Py.FloorDiv(x, 2)), ToBytes3(Py.FloorDiv(y, 2)), ToBytes3(width - 1));
        header = Bytes.Concat(header, ToBytes3(height - 1), ToBytes3(duration), [(byte)flags]);
        return Chunk("ANMF", Bytes.Concat([header, .. frameChunks]));
    }

    /// <summary>int.to_bytes(3, "little"): raises when the value does not fit.</summary>
    private static byte[] ToBytes3(int value)
    {
        if (value is < 0 or > 0xFFFFFF)
            throw new OverflowException("int too big to convert");
        return [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];
    }

    /// <summary>int.from_bytes(data, "little") of a (possibly short or empty) slice.</summary>
    private static int FromBytesLE(byte[] data)
    {
        var value = 0;
        for (var i = data.Length - 1; i >= 0; i--)
            value = (value << 8) | data[i];
        return value;
    }

    /// <summary>struct.unpack("&lt;I", data[start:start + 4]): raises unless the slice has exactly four bytes.</summary>
    private static long UnpackU32LE(byte[] data, int start)
    {
        var slice = Bytes.Slice(data, start, start + 4);
        if (slice.Length != 4)
            throw new FormatException("unpack requires a buffer of 4 bytes");
        return Bytes.U32LE(slice, 0);
    }

    /// <summary>Chunk list (fourcc, payload) of a RIFF body or of an ANMF payload; checks sizes and padding.</summary>
    private static List<WebPChunk> ParseChunks(byte[] data, int offset = 12, int? end = null)
    {
        var limit = end ?? data.Length;
        var result = new List<WebPChunk>();
        long position = offset;
        while (position < limit)
        {
            var at = (int)position;
            var fourcc = Bytes.Slice(data, at, at + 4);
            var size = UnpackU32LE(data, at + 4);
            Py.Assert(position + 8 + size <= limit, "('chunk overruns', " + Py.Repr(Bytes.Latin1(fourcc)) + ")");
            result.Add(new WebPChunk(Bytes.Latin1(fourcc), Bytes.Slice(data, at + 8, (int)(at + 8 + size))));
            position += 8 + size + (size & 1);
        }

        return result;
    }

    private static List<WebPChunk> RiffChunks(byte[] data)
    {
        Py.Assert(Bytes.Equal(Bytes.Slice(data, 0, 4), "RIFF"u8) && Bytes.Equal(Bytes.Slice(data, 8, 12), "WEBP"u8));
        var size = UnpackU32LE(data, 4);
        Py.Assert(size + 8 == data.Length, "RIFF size");
        return ParseChunks(data, 12, data.Length);
    }

    /// <summary>The image chunks (ALPH, VP8, VP8L) of a still WebP written by cwebp, for reuse in other containers.</summary>
    private static List<WebPChunk> BitstreamChunks(byte[] data) => [.. RiffChunks(data).Where(c => c.FourCC is "ALPH" or "VP8 " or "VP8L")];

    /// <summary>The boolean entropy decoder of RFC 6386 section 7.3 (transcribed), used to read the VP8 frame header fields.</summary>
    private sealed class BoolDecoder
    {
        private readonly byte[] _data;
        private int _position;
        private long _value;
        private long _range;
        private int _bitCount;

        public BoolDecoder(byte[] data)
        {
            _data = data;
            _position = 2;
            _value = (data[0] << 8) | data[1];
            _range = 255;
            _bitCount = 0;
        }

        public int Read(int probability = 128)
        {
            var split = 1 + (((_range - 1) * probability) >> 8);
            var bigSplit = split << 8;
            int result;
            if (_value >= bigSplit)
            {
                result = 1;
                _range -= split;
                _value -= bigSplit;
            }
            else
            {
                result = 0;
                _range = split;
            }

            while (_range < 128)
            {
                _value <<= 1;
                _range <<= 1;
                _bitCount++;
                if (_bitCount == 8)
                {
                    _bitCount = 0;
                    if (_position < _data.Length)
                        _value |= _data[_position];
                    _position++;
                }
            }

            return result;
        }

        public int Literal(int bits)
        {
            var value = 0;
            for (var i = 0; i < bits; i++)
                value = (value << 1) | Read();
            return value;
        }

        public int OptionalSigned(int bits)
        {
            if (Read() == 0)
                return 0;
            var magnitude = Literal(bits);
            return Read() != 0 ? -magnitude : magnitude;
        }
    }

    private sealed class Vp8HeaderInfo
    {
        public bool KeyFrame { get; init; }

        public int Version { get; init; }

        public int Show { get; init; }

        public int FirstPartitionSize { get; init; }

        public int Width { get; set; }

        public int Height { get; set; }

        public int HorizontalScale { get; set; }

        public int VerticalScale { get; set; }

        public int ColorSpace { get; set; }

        public int ClampingType { get; set; }

        public int Segmentation { get; set; }

        public string FilterType { get; set; } = "";

        public int FilterLevel { get; set; }

        public int Sharpness { get; set; }

        public int Partitions { get; set; }
    }

    /// <summary>Frame tag, dimensions and the first fields of the key-frame header (RFC 6386 sections 9.2-9.6, 19.2).</summary>
    private static Vp8HeaderInfo Vp8Header(byte[] payload)
    {
        var tag = payload[0] | (payload[1] << 8) | (payload[2] << 16);
        var info = new Vp8HeaderInfo { KeyFrame = (tag & 1) == 0, Version = (tag >> 1) & 7, Show = (tag >> 4) & 1, FirstPartitionSize = tag >> 5 };
        if (!info.KeyFrame)
            return info;
        Py.Assert(Bytes.Equal(Bytes.Slice(payload, 3, 6), [0x9D, 0x01, 0x2A]));
        var dimensions = Bytes.Slice(payload, 6, 10);
        if (dimensions.Length != 4)
            throw new FormatException("unpack requires a buffer of 4 bytes");
        int width = Bytes.U16LE(dimensions, 0), height = Bytes.U16LE(dimensions, 2);
        info.Width = width & 0x3FFF;
        info.Height = height & 0x3FFF;
        info.HorizontalScale = width >> 14;
        info.VerticalScale = height >> 14;
        var d = new BoolDecoder(Bytes.Slice(payload, 10, 10 + info.FirstPartitionSize));
        info.ColorSpace = d.Literal(1);
        info.ClampingType = d.Literal(1);
        info.Segmentation = d.Literal(1);
        if (info.Segmentation != 0)
        {
            int updateMap = d.Literal(1), updateData = d.Literal(1);
            if (updateData != 0)
            {
                d.Literal(1);
                for (var i = 0; i < 4; i++)
                    d.OptionalSigned(7);
                for (var i = 0; i < 4; i++)
                    d.OptionalSigned(6);
            }

            if (updateMap != 0)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (d.Literal(1) != 0)
                        d.Literal(8);
                }
            }
        }

        info.FilterType = d.Literal(1) != 0 ? "simple" : "normal";
        info.FilterLevel = d.Literal(6);
        info.Sharpness = d.Literal(3);
        if (d.Literal(1) != 0)
        {
            if (d.Literal(1) != 0)
            {
                for (var i = 0; i < 8; i++)
                    d.OptionalSigned(6);
            }
        }

        info.Partitions = 1 << d.Literal(2);
        return info;
    }

    /// <summary>LSB-first reader of the VP8L bitstream (WebP lossless specification, section 3).</summary>
    private sealed class BitReader(byte[] data)
    {
        private int _position;

        public int Read(int bits)
        {
            var value = 0;
            for (var i = 0; i < bits; i++)
            {
                var b = (_position >> 3) < data.Length ? data[_position >> 3] : 0;
                value |= ((b >> (_position & 7)) & 1) << i;
                _position++;
            }

            return value;
        }
    }

    private static readonly string[] Vp8lTransforms = ["predictor", "color", "subtract-green", "color-indexing"];

    private sealed record Vp8lHeaderInfo(int Width, int Height, int AlphaHint, int Version, List<string> Transforms);

    /// <summary>Header of a VP8L bitstream and the transforms that can be read without entropy decoding: the sequence stops
    /// at the first transform followed by an entropy-coded sub-image (predictor, color, color indexing).</summary>
    private static Vp8lHeaderInfo Vp8lHeader(byte[] payload)
    {
        Py.Assert(payload[0] == 0x2F);
        var r = new BitReader(Bytes.Slice(payload, 1));
        var width = r.Read(14) + 1;
        var height = r.Read(14) + 1;
        var alphaHint = r.Read(1);
        var version = r.Read(3);
        var info = new Vp8lHeaderInfo(width, height, alphaHint, version, []);
        while (r.Read(1) != 0)
        {
            var kind = Vp8lTransforms[r.Read(2)];
            info.Transforms.Add(kind);
            if (kind != "subtract-green")
                break;
        }

        return info;
    }

    private static readonly string[] AlphFilters = ["none", "horizontal", "vertical", "gradient"];

    /// <summary>A frame (ANMF) or the still image of an inspected input: the ANMF fields and what its bitstream declares
    /// (width, height and lossless stay unset when no key frame or VP8L header was read).</summary>
    private sealed class InspectedFrame
    {
        public int X { get; init; }

        public int Y { get; init; }

        public int W { get; init; }

        public int H { get; init; }

        public int Duration { get; init; }

        public bool Blend { get; init; }

        public bool Dispose { get; init; }

        public bool Alpha { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public bool? Lossless { get; set; }
    }

    private sealed class WebPInfo
    {
        public List<InspectedFrame> Frames { get; } = [];

        public int? Loop { get; set; }

        public Px? Background { get; set; }

        public (int? Width, int? Height)? Canvas { get; set; }

        public int? Flags { get; set; }

        public InspectedFrame? Still { get; set; }
    }

    /// <summary>Parses the container and bitstream headers of an input: features actually present in the bytes (never
    /// trusted from the requested encoder flags), canvas, frames and timing fields.</summary>
    private static (List<string> Features, WebPInfo Info) WebPInspect(byte[] data)
    {
        var chunks = RiffChunks(data);
        var features = new HashSet<string>(StringComparer.Ordinal);
        var info = new WebPInfo();
        var first = chunks[0].FourCC;
        features.Add("webp.layout=" + (first == "VP8X" ? "extended" : "simple"));

        void Image(List<WebPChunk> subChunks, InspectedFrame frame)
        {
            foreach (var (fourcc, payload) in subChunks)
            {
                if (fourcc == "ALPH")
                {
                    var header = payload[0];
                    int compression = header & 3, filtering = (header >> 2) & 3;
                    features.Add("webp.alpha=alph");
                    features.Add("webp.alph.compression=" + compression switch
                    {
                        0 => "none",
                        1 => "vp8l",
                        _ => throw new KeyNotFoundException(CommonCorpus.Str(compression)),
                    });
                    features.Add("webp.alph.filter=" + AlphFilters[filtering]);
                    if (((header >> 4) & 3) != 0)
                        features.Add("webp.alph.preprocessing=level-reduction");
                    if (compression == 1)
                    {
                        // The alpha bitstream is a VP8L image stream without header: transforms start immediately
                        var r = new BitReader(Bytes.Slice(payload, 1));
                        while (r.Read(1) != 0)
                        {
                            var kind = Vp8lTransforms[r.Read(2)];
                            features.Add("webp.alph.transform=" + kind);
                            if (kind != "subtract-green")
                                break;
                        }
                    }

                    frame.Alpha = true;
                }
                else if (fourcc == "VP8 ")
                {
                    var header = Vp8Header(payload);
                    features.Add("webp.bitstream=vp8");
                    if (!header.KeyFrame)
                    {
                        features.Add("webp.vp8.frame=inter");
                        continue;
                    }

                    features.Add("webp.vp8.filter=" + header.FilterType);
                    features.Add("webp.vp8.partitions=" + CommonCorpus.Str(header.Partitions));
                    if (header.Segmentation != 0)
                        features.Add("webp.vp8.segmentation");
                    if (header.FilterLevel == 0)
                        features.Add("webp.vp8.filterLevel=0");
                    frame.Width = header.Width;
                    frame.Height = header.Height;
                    frame.Lossless = false;
                }
                else if (fourcc == "VP8L")
                {
                    var header = Vp8lHeader(payload);
                    features.Add("webp.bitstream=vp8l");
                    if (header.AlphaHint != 0)
                    {
                        features.Add("webp.alpha=vp8l");
                        frame.Alpha = true;
                    }

                    foreach (var kind in header.Transforms)
                        features.Add("webp.vp8l.transform=" + kind);
                    frame.Width = header.Width;
                    frame.Height = header.Height;
                    frame.Lossless = true;
                }
            }
        }

        var still = new InspectedFrame { Alpha = false };
        foreach (var (fourcc, payload) in chunks)
        {
            if (fourcc == "VP8X")
            {
                var flags = payload[0];
                info.Canvas = (FromBytesLE(Bytes.Slice(payload, 4, 7)) + 1, FromBytesLE(Bytes.Slice(payload, 7, 10)) + 1);
                info.Flags = flags;
                if ((flags & 0x10) != 0)
                    features.Add("webp.vp8x.alphaFlag");
            }
            else if (fourcc == "ANIM")
            {
                info.Background = Px.From(Bytes.Slice(payload, 0, 4));
                var loop = Bytes.Slice(payload, 4, 6);
                if (loop.Length != 2)
                    throw new FormatException("unpack requires a buffer of 2 bytes");
                info.Loop = Bytes.U16LE(loop, 0);
                features.Add("webp.animation");
                features.Add("webp.loop=" + (info.Loop == 0 ? "infinite" : "finite"));
            }
            else if (fourcc == "ANMF")
            {
                int x = 2 * FromBytesLE(Bytes.Slice(payload, 0, 3)), y = 2 * FromBytesLE(Bytes.Slice(payload, 3, 6));
                int w = FromBytesLE(Bytes.Slice(payload, 6, 9)) + 1, h = FromBytesLE(Bytes.Slice(payload, 9, 12)) + 1;
                int duration = FromBytesLE(Bytes.Slice(payload, 12, 15)), flags = payload[15];
                var frame = new InspectedFrame { X = x, Y = y, W = w, H = h, Duration = duration, Blend = (flags & 0x02) == 0, Dispose = (flags & 0x01) != 0, Alpha = false };
                Image(ParseChunks(payload, 16, payload.Length), frame);
                if (frame.Width is null || frame.Height is null)
                    throw new KeyNotFoundException("width");
                Py.Assert((frame.Width, frame.Height) == (w, h), "ANMF size and bitstream size");
                features.Add("webp.blend=" + (frame.Blend ? "alpha" : "none"));
                features.Add("webp.dispose=" + (frame.Dispose ? "background" : "none"));
                var canvas = info.Canvas ?? throw new KeyNotFoundException("canvas");
                if ((x, y, w, h) != (0, 0, canvas.Width, canvas.Height))
                    features.Add("webp.frame=partial");
                info.Frames.Add(frame);
            }
            else if (fourcc is "ALPH" or "VP8 " or "VP8L")
            {
                Image([new WebPChunk(fourcc, payload)], still);
            }
            else if (fourcc is "ICCP" or "EXIF" or "XMP ")
            {
                features.Add("webp.chunk=" + fourcc.Trim());
            }
            else
            {
                features.Add("webp.chunk=unknown");
            }
        }

        if (info.Frames.Count == 0)
        {
            info.Canvas ??= (still.Width, still.Height);
            info.Still = still;
        }

        if (info.Frames.Select(f => f.Lossless).Distinct().Count() > 1)
            features.Add("webp.frames=mixed");
        return ([.. features.Order(StringComparer.Ordinal)], info);
    }
}
