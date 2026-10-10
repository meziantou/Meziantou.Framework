using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>An APNG frame: the encoded image, its offset, delay fraction, dispose and blend operations.</summary>
internal sealed record ApngFrame(Img Img, int X, int Y, int Num, int Den, string Dispose, string Blend);

/// <summary>Parsed PNG/APNG container (png_inspect).</summary>
internal sealed class PngInfo
{
    public List<int[]> FcTL { get; } = [];

    public int IdatCount { get; set; }

    public bool FctlBeforeIdat { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int BitDepth { get; set; }

    public int ColorType { get; set; }

    public int Interlace { get; set; }

    public int NumFrames { get; set; }

    public int NumPlays { get; set; }

    public List<string> Chunks { get; } = [];
}

internal static partial class CommonCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // PNG / APNG writer (hand-authored inputs). zlib streams use stored (uncompressed) deflate blocks so the bytes are
    // deterministic and independent of any zlib version; FFmpeg-encoded fixtures cover compressed streams.
    // -----------------------------------------------------------------------------------------------------------------

    public static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', (byte)'\r', (byte)'\n', 0x1A, (byte)'\n'];

    public static readonly (int X0, int Y0, int Dx, int Dy)[] Adam7 = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    public static readonly IReadOnlyDictionary<string, int> Dispose = new Dictionary<string, int>(StringComparer.Ordinal) { ["none"] = 0, ["background"] = 1, ["previous"] = 2 };

    public static readonly IReadOnlyDictionary<string, int> Blend = new Dictionary<string, int>(StringComparer.Ordinal) { ["source"] = 0, ["over"] = 1 };

    public static int PngChannels(int colorType) => colorType switch
    {
        0 => 1,
        2 => 3,
        3 => 1,
        4 => 2,
        6 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(colorType)),
    };

    public static byte[] PngChunk(string kind, byte[] data)
    {
        var kindBytes = Bytes.Ascii(kind);
        return new ByteBuilder().U32BE(data.Length).Bytes(kindBytes).Bytes(data).U32BE(Bytes.Crc32(Bytes.Concat(kindBytes, data))).ToArray();
    }

    public static byte[] ZlibStored(byte[] data)
    {
        var output = new ByteBuilder().U8(0x78).U8(0x01);
        var offset = 0;
        while (true)
        {
            var block = Bytes.Slice(data, offset, offset + 65535);
            offset += block.Length;
            var final = offset >= data.Length;
            output.U8(final ? 1 : 0).U16LE(block.Length).U16LE(block.Length ^ 0xFFFF).Bytes(block);
            if (final)
                break;
        }

        return output.U32BE(Bytes.Adler32(data)).ToArray();
    }

    public static byte[] PngPackRow(IReadOnlyList<int> samples, int bitDepth)
    {
        var output = new ByteBuilder();
        if (bitDepth == 16)
        {
            foreach (var s in samples)
                output.U16BE(s);
            return output.ToArray();
        }

        if (bitDepth == 8)
        {
            foreach (var s in samples)
                output.U8(s);
            return output.ToArray();
        }

        var perByte = 8 / bitDepth;
        for (var i = 0; i < samples.Count; i += perByte)
        {
            var value = 0;
            var group = samples.Skip(i).Take(perByte).ToList();
            for (var j = 0; j < group.Count; j++)
                value |= group[j] << (8 - bitDepth * (j + 1));
            output.U8(value);
        }

        return output.ToArray();
    }

    public static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    public static byte[] PngFilterRow(int kind, byte[] row, byte[]? previous, int bpp)
    {
        var output = new byte[row.Length + 1];
        output[0] = (byte)kind;
        for (var i = 0; i < row.Length; i++)
        {
            var a = i >= bpp ? row[i - bpp] : 0;
            var b = previous is not null ? previous[i] : 0;
            var c = previous is not null && i >= bpp ? previous[i - bpp] : 0;
            var predictor = kind switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, _ => Paeth(a, b, c) };
            output[i + 1] = (byte)((row[i] - predictor) & 0xFF);
        }

        return output;
    }

    /// <summary>Filtered scanlines. Filter types cycle 0..4 row by row (per pass) so every filter is exercised.</summary>
    public static byte[] PngImageData(int width, int height, IReadOnlyList<IReadOnlyList<Px>> rowsOfSamples, int channels, int bitDepth, bool interlace)
    {
        var bpp = Math.Max(1, channels * bitDepth / 8);
        var output = new ByteBuilder();
        var counter = 0;
        var passes = interlace ? Adam7 : [(0, 0, 1, 1)];
        foreach (var (x0, y0, dx, dy) in passes)
        {
            var xs = Range(x0, width, dx);
            var ys = Range(y0, height, dy);
            if (xs.Count == 0 || ys.Count == 0)
                continue; // empty pass: no scanlines at all
            byte[]? previous = null;
            foreach (var y in ys)
            {
                var samples = xs.SelectMany(x => rowsOfSamples[y][x]).ToList();
                var row = PngPackRow(samples, bitDepth);
                output.Bytes(PngFilterRow(counter % 5, row, previous, bpp));
                counter++;
                previous = row;
            }
        }

        return output.ToArray();
    }

    public static byte[] PngIhdr(int width, int height, int bitDepth, int colorType, bool interlace) =>
        PngChunk("IHDR", new ByteBuilder().U32BE(width).U32BE(height).U8(bitDepth).U8(colorType).U8(0).U8(0).U8(interlace ? 1 : 0).ToArray());

    public static byte[] PngFile(int width, int height, int bitDepth, int colorType, IReadOnlyList<Px> samples, bool interlace = false, IEnumerable<byte[]>? beforeIdat = null,
        int idatSplits = 1, bool emptyIdat = false, IEnumerable<byte[]>? afterIdat = null)
    {
        var channels = PngChannels(colorType);
        var rows = Rows(samples, width, height);
        var stream = ZlibStored(PngImageData(width, height, rows, channels, bitDepth, interlace));
        var output = new ByteBuilder().Bytes(PngSignature).Bytes(PngIhdr(width, height, bitDepth, colorType, interlace));
        foreach (var chunk in beforeIdat ?? [])
            output.Bytes(chunk);
        var size = Py.CeilDiv(stream.Length, idatSplits);
        for (var i = 0; i < idatSplits; i++)
        {
            output.Bytes(PngChunk("IDAT", Bytes.Slice(stream, i * size, (i + 1) * size)));
            if (emptyIdat && i == 0)
                output.Bytes(PngChunk("IDAT", []));
        }

        foreach (var chunk in afterIdat ?? [])
            output.Bytes(chunk);
        return output.Bytes(PngChunk("IEND", [])).ToArray();
    }

    public static byte[] RgbaRowsStream(Img img) => ZlibStored(PngImageData(img.Width, img.Height, Rows(img.Pixels, img.Width, img.Height), 4, 8, false));

    public static byte[] Fctl(int seq, Img img, int x, int y, int delayNum, int delayDen, int dispose, int blend) =>
        PngChunk("fcTL", new ByteBuilder().U32BE(seq).U32BE(img.Width).U32BE(img.Height).U32BE(x).U32BE(y).U16BE(delayNum).U16BE(delayDen).U8(dispose).U8(blend).ToArray());

    /// <summary>frames: the first frame is the default image unless a separate poster is given (then the poster is the IDAT
    /// image and is not part of the animation). Images carry the encoded samples (Img model matching the color type; palette
    /// images: 1-tuples of indices); fdatSplits splits each fdAT datastream into that many chunks (each with its own
    /// sequence number).</summary>
    public static byte[] ApngFile(int canvasW, int canvasH, int numPlays, IReadOnlyList<ApngFrame> frames, Img? poster = null, int bitDepth = 8, int colorType = 6,
        bool interlace = false, IEnumerable<byte[]>? beforeIdat = null, int fdatSplits = 1, int? numFrames = null)
    {
        var channels = PngChannels(colorType);
        byte[] Stream(Img img) => ZlibStored(PngImageData(img.Width, img.Height, Rows(img.Pixels, img.Width, img.Height), channels, bitDepth, interlace));

        var output = new ByteBuilder().Bytes(PngSignature).Bytes(PngIhdr(canvasW, canvasH, bitDepth, colorType, interlace));
        output.Bytes(PngChunk("acTL", new ByteBuilder().U32BE(numFrames ?? frames.Count).U32BE(numPlays).ToArray()));
        foreach (var chunk in beforeIdat ?? [])
            output.Bytes(chunk);
        var seq = 0;
        for (var index = 0; index < frames.Count; index++)
        {
            var f = frames[index];
            var control = Fctl(seq, f.Img, f.X, f.Y, f.Num, f.Den, Dispose[f.Dispose], Blend[f.Blend]);
            seq++;
            if (index == 0 && poster is null)
            {
                output.Bytes(control).Bytes(PngChunk("IDAT", Stream(f.Img)));
                continue;
            }

            if (index == 0)
                output.Bytes(PngChunk("IDAT", Stream(poster!)));
            output.Bytes(control);
            var data = Stream(f.Img);
            var size = Py.CeilDiv(data.Length, fdatSplits);
            for (var i = 0; i < fdatSplits; i++)
            {
                output.Bytes(PngChunk("fdAT", new ByteBuilder().U32BE(seq).Bytes(Bytes.Slice(data, i * size, (i + 1) * size)).ToArray()));
                seq++;
            }
        }

        return output.Bytes(PngChunk("IEND", [])).ToArray();
    }

    /// <summary>APNG_BLEND_OP_OVER rounding contract of the library, transcribed with exact rationals: a
    /// transparent source keeps the canvas; otherwise alpha = round(A / max) with A = sa*max + da*(max - sa) (no ties: max is
    /// odd) and color = round((sc*sa*max + dc*da*(max - sa)) / A), ties upward. Straight alpha in and out.</summary>
    public static Px Over(Px src, Px dst, int depth)
    {
        var maximum = (1 << depth) - 1;
        int sa = src[3], da = dst[3];
        if (sa == 0)
            return dst;
        var total = (long)sa * maximum + (long)da * (maximum - sa);

        static int Nearest(Rational value) => (int)(value + new Rational(1, 2)).Floor();

        var color = Enumerable.Range(0, 3).Select(i => Nearest(new Rational((long)src[i] * sa * maximum + (long)dst[i] * da * (maximum - sa), total))).ToList();
        return new Px(color[0], color[1], color[2], Nearest(new Rational(total, maximum)));
    }

    /// <summary>Parses the container to record the encoding features actually present (never trusting requested flags).</summary>
    public static (PngInfo Info, List<string> Features) PngInspect(byte[] data)
    {
        Py.Assert(Bytes.StartsWith(data, PngSignature));
        var offset = 8;
        var info = new PngInfo();
        var stream = new ByteBuilder();
        var seenIdat = false;
        while (offset < data.Length)
        {
            var length = (int)Bytes.U32BE(data, offset);
            var kind = Bytes.Latin1(data.AsSpan(offset + 4, 4));
            var body = Bytes.Slice(data, offset + 8, offset + 8 + length);
            offset += 12 + length;
            info.Chunks.Add(kind);
            if (kind == "IHDR")
            {
                info.Width = (int)Bytes.U32BE(body, 0);
                info.Height = (int)Bytes.U32BE(body, 4);
                info.BitDepth = body[8];
                info.ColorType = body[9];
                info.Interlace = body[12];
            }
            else if (kind == "acTL")
            {
                info.NumFrames = (int)Bytes.U32BE(body, 0);
                info.NumPlays = (int)Bytes.U32BE(body, 4);
            }
            else if (kind == "fcTL")
            {
                info.FcTL.Add([(int)Bytes.U32BE(body, 0), (int)Bytes.U32BE(body, 4), (int)Bytes.U32BE(body, 8), (int)Bytes.U32BE(body, 12), (int)Bytes.U32BE(body, 16),
                    Bytes.U16BE(body, 20), Bytes.U16BE(body, 22), body[24], body[25]]);
                if (!seenIdat)
                    info.FctlBeforeIdat = true;
            }
            else if (kind == "IDAT")
            {
                seenIdat = true;
                info.IdatCount++;
                stream.Bytes(body);
            }
        }

        var features = new List<string>
        {
            "png.colorType=" + Str(info.ColorType),
            "png.bitDepth=" + Str(info.BitDepth),
            "png.interlace=" + (info.Interlace != 0 ? "adam7" : "none"),
        };
        string[] excluded = ["IHDR", "IDAT", "IEND", "acTL", "fcTL", "fdAT"];
        foreach (var kind in info.Chunks.Distinct().Except(excluded).Order(StringComparer.Ordinal))
            features.Add("png.chunk=" + kind);
        if (info.IdatCount > 1)
            features.Add("png.idatChunks=" + Str(info.IdatCount));
        features.Add("png.filters=" + string.Join(',', PngFilterTypes(info, Bytes.ZlibDecompress(stream.ToArray())).Order()));
        if (info.Chunks.Contains("acTL"))
        {
            features.Add("apng");
            features.Add("apng.numPlays=" + Str(info.NumPlays));
            features.Add("apng.poster=" + (info.FctlBeforeIdat ? "frame0" : "separate"));
            string[] disposeNames = ["none", "background", "previous"];
            string[] blendNames = ["source", "over"];
            features.Add("apng.dispose=" + string.Join(',', info.FcTL.Select(f => disposeNames[f[7]]).Distinct().Order(StringComparer.Ordinal)));
            features.Add("apng.blend=" + string.Join(',', info.FcTL.Select(f => blendNames[f[8]]).Distinct().Order(StringComparer.Ordinal)));
            if (info.FcTL.Any(f => f[1] != info.Width || f[2] != info.Height))
                features.Add("apng.subRectangles");
        }

        return (info, [.. features.Order(StringComparer.Ordinal)]);
    }

    public static HashSet<int> PngFilterTypes(PngInfo info, byte[] raw)
    {
        var channels = PngChannels(info.ColorType);
        var bits = channels * info.BitDepth;
        var passes = info.Interlace != 0 ? Adam7 : [(0, 0, 1, 1)];
        var offset = 0;
        var types = new HashSet<int>();
        foreach (var (x0, y0, dx, dy) in passes)
        {
            var pw = Range(x0, info.Width, dx).Count;
            var ph = Range(y0, info.Height, dy).Count;
            if (pw == 0 || ph == 0)
                continue;
            var rowBytes = (pw * bits + 7) / 8;
            for (var i = 0; i < ph; i++)
            {
                types.Add(raw[offset]);
                offset += 1 + rowBytes;
            }
        }

        return types;
    }

    /// <summary>range(start, stop, step) for a positive step.</summary>
    public static List<int> Range(int start, int stop, int step = 1)
    {
        var result = new List<int>();
        for (var i = start; i < stop; i += step)
            result.Add(i);
        return result;
    }

    /// <summary>The rows of a row-major pixel list.</summary>
    public static List<IReadOnlyList<Px>> Rows(IReadOnlyList<Px> pixels, int width, int height) =>
        [.. Enumerable.Range(0, height).Select(y => (IReadOnlyList<Px>)pixels.Skip(y * width).Take(width).ToList())];

    /// <summary>Decimal text of an integer ("%d").</summary>
    public static string Str(long value) => value.ToString(CultureInfo.InvariantCulture);
}
