using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using static Meziantou.Framework.Imaging.CorpusGenerator.Common.HandAssembledCorpus;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the Netpbm fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/pnm, invalid/pnm and limit/pnm entries).
/// <para>
/// This generator is the reviewed, reproducible recipe of every committed Netpbm fixture. It is NEVER run by the tests and
/// does not use the library under test. The shared driver and helpers are in HandAssembledCorpus.
/// </para>
/// <para>
/// Expected pixels are hand-defined:
/// - every input is assembled byte by byte by the writers below, next to the literal pixels it must produce;
/// - a few inputs are produced by FFmpeg's independent PNM encoder, from hand-defined patterns: the format is lossless,
///   so the pattern is the reference.
/// </para>
/// <para>
/// Every input is then decoded by the strict transcription of the specification in this generator (headers, white space and MAXVAL normalization
/// are all validated) and by FFmpeg/libavcodec, and the decodings must agree.
/// </para>
/// <para>
/// Malformed, unsupported and over-limit inputs are byte edits of valid inputs; each defect is confirmed by the strict
/// transcription.
/// </para>
/// <para>
/// It complements GoldenCorpus: this generator only replaces the manifest entries whose format is pnm and their files, and
/// keeps every other entry and file unchanged.
/// </para>
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- pnm          # check (temporary directory and diff)
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- pnm --write  # explicit regeneration (review the diff!)
/// Requirements: FFmpeg at the pinned version (FfmpegTools; override with --accept-tool-versions after reviewing the differences).
/// Any OS.
/// </summary>
internal static class PnmCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/PnmCorpus.cs";

    private static readonly HandAssembledFormat Format = new("pnm", "Pnm", PnmRead);

    public static int Run(GeneratorOptions options) => HandAssembledCorpus.Run(options, Format, ScriptPath, c =>
    {
        BuildPnm(c);
        BuildErrors(c);
        BuildLimits(c);
    });

    // ---------------------------------------------------------------------------------------------------------------------
    // Netpbm PBM/PGM/PPM/PAM: the Netpbm format specifications, transcribed as a strict reader and as writers
    // ---------------------------------------------------------------------------------------------------------------------

    private static readonly byte[] PnmWhitespace = Bytes.Ascii(" \t\n\r\v\f");

    private static readonly Dictionary<string, int> PamChannels = new(StringComparer.Ordinal)
    {
        ["BLACKANDWHITE"] = 1,
        ["GRAYSCALE"] = 1,
        ["RGB"] = 3,
        ["BLACKANDWHITE_ALPHA"] = 2,
        ["GRAYSCALE_ALPHA"] = 2,
        ["RGB_ALPHA"] = 4,
    };

    private static int PnmScale(long value, long maximum, int target)
    {
        if (value > maximum)
            throw new FormatError(Inv($"sample {value} greater than MAXVAL {maximum}"));
        if (maximum == target)
            return (int)value;
        return (int)((value * target + maximum / 2) / maximum);
    }

    private static byte[] PbmPack(int[] bits, int width)
    {
        var output = new byte[(width + 7) / 8];
        for (var x = 0; x < bits.Length; x++)
        {
            if (bits[x] != 0)
                output[x >> 3] |= (byte)(0x80 >> (x & 7));
        }

        return output;
    }

    /// <summary>Assembles a binary PBM (P4), PGM (P5), PPM (P6) or PAM (P7) file; rows are the stored bytes of each row.</summary>
    private static byte[] PnmBinary(int magic, int width, int height, int maxval, IEnumerable<byte[]> rows, string? comment = null, int? depth = null, string? tupleType = null)
    {
        string header;
        if (magic == 7)
        {
            header = "P7\n";
            if (!string.IsNullOrEmpty(comment))
                header += "#" + comment + "\n";
            header += Inv($"WIDTH {width}\nHEIGHT {height}\nDEPTH {depth}\nMAXVAL {maxval}\nTUPLTYPE {tupleType}\nENDHDR\n");
        }
        else
        {
            header = Inv($"P{magic}\n");
            if (!string.IsNullOrEmpty(comment))
                header += "#" + comment + "\n";
            header += Inv($"{width} {height}\n");
            if (magic != 4)
                header += Inv($"{maxval}\n");
        }

        return Bytes.Concat([Bytes.Ascii(header), .. rows]);
    }

    /// <summary>Assembles a plain PBM (P1), PGM (P2) or PPM (P3) file.</summary>
    private static byte[] PnmPlain(int magic, int width, int height, int maxval, IEnumerable<IEnumerable<int>> samplesPerRow, string? comment = null)
    {
        var header = Inv($"P{magic}\n");
        if (!string.IsNullOrEmpty(comment))
            header += "#" + comment + "\n";
        header += Inv($"{width} {height}\n");
        if (magic != 1)
            header += Inv($"{maxval}\n");
        var body = string.Concat(samplesPerRow.Select(row => string.Join(' ', row.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "\n"));
        return Bytes.Ascii(header + body);
    }

    /// <summary>str.isdigit() of an ASCII token (false for the empty string).</summary>
    private static bool IsDigits(string token) => token.Length > 0 && token.All(char.IsAsciiDigit);

    /// <summary>bytes.decode("ascii"): non-ASCII bytes are a decoding error (not a FormatError).</summary>
    private static string DecodeAscii(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            if (b >= 0x80)
                throw new InvalidOperationException("'ascii' codec can't decode byte 0x" + b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return Bytes.Latin1(data);
    }

    private static bool IsWhitespace(byte value) => PnmWhitespace.Contains(value);

    /// <summary>White space, comments and tokens of a Netpbm header or plain raster, exactly as the formats define them.</summary>
    private sealed class PnmScanner(byte[] input)
    {
        public byte[] Data { get; } = input;

        public int Offset { get; set; }

        public string NextToken(bool singleCharacter = false)
        {
            var data = Data;
            byte value;
            while (true)
            {
                if (Offset >= data.Length)
                    throw new FormatError("truncated header or raster");
                value = data[Offset];
                if (IsWhitespace(value))
                {
                    Offset++;
                    continue;
                }

                if (value == 0x23)
                {
                    while (Offset < data.Length && data[Offset] is not ((byte)'\n' or (byte)'\r'))
                        Offset++;
                    continue;
                }

                break;
            }

            if (singleCharacter)
            {
                Offset++;
                return ((char)value).ToString();
            }

            var start = Offset;
            while (Offset < data.Length && !IsWhitespace(data[Offset]) && data[Offset] != 0x23)
                Offset++;
            return DecodeAscii(data.AsSpan(start, Offset - start));
        }

        public long NextNumber()
        {
            var token = NextToken();
            if (!IsDigits(token))
                throw new FormatError("token " + Py.Repr(token) + " is not a decimal number");
            return long.Parse(token, CultureInfo.InvariantCulture);
        }

        public void ConsumeSingleWhitespace()
        {
            if (Offset >= Data.Length || !IsWhitespace(Data[Offset]))
                throw new FormatError("the header token is not followed by white space");
            Offset++;
        }
    }

    /// <summary>bytes.strip(): removes leading and trailing ASCII white space.</summary>
    private static ReadOnlySpan<byte> StripBytes(ReadOnlySpan<byte> value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsWhitespace(value[start]))
            start++;
        while (end > start && IsWhitespace(value[end - 1]))
            end--;
        return value[start..end];
    }

    /// <summary>bytes.split(None, 1).</summary>
    private static List<byte[]> SplitOnce(ReadOnlySpan<byte> value)
    {
        var parts = new List<byte[]>();
        var start = 0;
        while (start < value.Length && IsWhitespace(value[start]))
            start++;
        if (start == value.Length)
            return parts;
        var end = start;
        while (end < value.Length && !IsWhitespace(value[end]))
            end++;
        parts.Add(value[start..end].ToArray());
        var rest = end;
        while (rest < value.Length && IsWhitespace(value[rest]))
            rest++;
        if (rest < value.Length)
            parts.Add(value[rest..].ToArray());
        return parts;
    }

    /// <summary>Strict transcription of the supported Netpbm subset: returns (Img, features). Only the first image is read.</summary>
    private static (Img Image, List<string> Features) PnmRead(byte[] data)
    {
        if (data.Length < 3 || data[0] != (byte)'P' || data[1] is < (byte)'1' or > (byte)'7' || !IsWhitespace(data[2]))
            throw new FormatError("magic number");
        var magic = data[1] - 0x30;
        var scanner = new PnmScanner(data) { Offset = 2 };
        var comments = data.AsSpan(0, Math.Min(64, data.Length)).Count((byte)'#');
        long width, height, maxval;
        int depth;
        string? tupleType;
        bool plain, bitmap;
        if (magic == 7)
        {
            var fields = new Dictionary<string, long>(StringComparer.Ordinal);
            tupleType = null;
            while (true)
            {
                var lineEnd = Bytes.Find(data, "\n"u8, scanner.Offset);
                if (lineEnd < 0)
                    throw new FormatError("ENDHDR is missing");
                var lineBytes = data.AsSpan(scanner.Offset, lineEnd - scanner.Offset);
                while (lineBytes.Length > 0 && lineBytes[^1] == (byte)'\r')
                    lineBytes = lineBytes[..^1];
                var line = StripBytes(lineBytes);
                scanner.Offset = lineEnd + 1;
                if (line.IsEmpty || line[0] == (byte)'#')
                    continue;
                var parts = SplitOnce(line);
                var keyword = DecodeAscii(parts[0]);
                if (keyword == "ENDHDR")
                    break;
                if (keyword == "TUPLTYPE")
                {
                    if (tupleType is not null)
                        throw new FormatError("TUPLTYPE declared twice");
                    tupleType = DecodeAscii(parts[1]);
                    if (!PamChannels.ContainsKey(tupleType))
                        throw new FormatError("unsupported tuple type " + tupleType);
                    continue;
                }

                if (keyword is not ("WIDTH" or "HEIGHT" or "DEPTH" or "MAXVAL"))
                    throw new FormatError("undefined PAM keyword " + keyword);
                if (fields.ContainsKey(keyword))
                    throw new FormatError(keyword + " declared twice");
                if (parts.Count < 2 || !IsDigits(DecodeAscii(StripBytes(parts[1]))))
                    throw new FormatError(keyword + " has no decimal value");
                fields[keyword] = long.Parse(DecodeAscii(StripBytes(parts[1])), CultureInfo.InvariantCulture);
            }

            if (fields.Count != 4)
                throw new FormatError("WIDTH, HEIGHT, DEPTH and MAXVAL are required");
            (width, height, maxval) = (fields["WIDTH"], fields["HEIGHT"], fields["MAXVAL"]);
            var declaredDepth = fields["DEPTH"];
            if (tupleType is null)
            {
                tupleType = declaredDepth switch { 1 => "GRAYSCALE", 2 => "GRAYSCALE_ALPHA", 3 => "RGB", 4 => "RGB_ALPHA", _ => null };
                if (tupleType is null)
                    throw new FormatError(Inv($"DEPTH {declaredDepth} without a TUPLTYPE"));
            }

            if (PamChannels[tupleType] != declaredDepth)
                throw new FormatError(Inv($"DEPTH {declaredDepth} for the tuple type {tupleType}"));
            depth = (int)declaredDepth;
            if (tupleType.StartsWith("BLACKANDWHITE", StringComparison.Ordinal) && maxval != 1)
                throw new FormatError(tupleType + " requires MAXVAL 1");
            (plain, bitmap) = (false, false);
        }
        else
        {
            width = scanner.NextNumber();
            height = scanner.NextNumber();
            maxval = magic is 1 or 4 ? 1 : scanner.NextNumber();
            bitmap = magic is 1 or 4;
            plain = magic is 1 or 2 or 3;
            if (!plain)
                scanner.ConsumeSingleWhitespace();
            tupleType = magic switch { 1 or 4 => "BLACKANDWHITE", 2 or 5 => "GRAYSCALE", _ => "RGB" };
            depth = PamChannels[tupleType];
        }

        if (width <= 0 || height <= 0)
            throw new FormatError(Inv($"dimensions {width}x{height}"));
        if (maxval is < 1 or > 65535)
            throw new FormatError(Inv($"MAXVAL {maxval}"));

        var target = maxval > 255 ? 65535 : 255;
        var sampleBytes = maxval > 255 ? 2 : 1;
        var pixels = new List<Px>();
        var black = new Px(0, 0, 0, 255);
        var white = new Px(255, 255, 255, 255);
        for (var y = 0; y < height; y++)
        {
            if (bitmap && plain)
            {
                var row = new List<int>();
                for (var i = 0; i < width; i++)
                    row.Add(scanner.NextToken(singleCharacter: true) == "1" ? 1 : 0);
                foreach (var bit in row)
                {
                    if (bit is not (0 or 1))
                        throw new FormatError("plain PBM sample");
                }

                pixels.AddRange(row.Select(bit => bit != 0 ? black : white));
                continue;
            }

            if (bitmap)
            {
                var rowBytes = (int)((width + 7) / 8);
                if (data.Length < scanner.Offset + rowBytes)
                    throw new FormatError("truncated raster");
                var row = data.AsSpan(scanner.Offset, rowBytes);
                scanner.Offset += rowBytes;
                for (var x = 0; x < width; x++)
                {
                    var bit = (row[x >> 3] >> (7 - (x & 7))) & 1;
                    pixels.Add(bit != 0 ? black : white);
                }

                continue;
            }

            var samples = new List<long>();
            for (var i = 0; i < width * depth; i++)
            {
                if (plain)
                {
                    var token = scanner.NextToken();
                    if (!IsDigits(token))
                        throw new FormatError("plain sample " + Py.Repr(token));
                    samples.Add(long.Parse(token, CultureInfo.InvariantCulture));
                }
                else
                {
                    if (data.Length < scanner.Offset + sampleBytes)
                        throw new FormatError("truncated raster");
                    samples.Add(sampleBytes == 2 ? Bytes.U16BE(data, scanner.Offset) : data[scanner.Offset]);
                    scanner.Offset += sampleBytes;
                }
            }

            for (var x = 0; x < width; x++)
            {
                var tupleSamples = samples.Skip(x * depth).Take(depth).Select(v => PnmScale(v, maxval, target)).ToList();
                pixels.Add(depth switch
                {
                    1 => new Px(tupleSamples[0], tupleSamples[0], tupleSamples[0], target),
                    2 => new Px(tupleSamples[0], tupleSamples[0], tupleSamples[0], tupleSamples[1]),
                    3 => new Px(tupleSamples[0], tupleSamples[1], tupleSamples[2], target),
                    _ => Px.From(tupleSamples),
                });
            }
        }

        var features = new List<string>
        {
            Inv($"pnm.magic=P{magic}"),
            "pnm.tuple=" + tupleType,
            Inv($"pnm.maxval={maxval}"),
            "pnm.raster=" + (plain ? "plain" : "binary"),
        };
        if (maxval is not (1 or 255 or 65535))
            features.Add("pnm.maxval.normalized");
        if (comments != 0)
            features.Add("pnm.comments");
        if (width % 2 == 1)
            features.Add("pnm.width=odd");
        if (bitmap && width % 8 != 0)
            features.Add("pnm.pbm.rowPadding");
        if (scanner.Offset < data.Length)
            features.Add("pnm.trailingData");
        return (new Img((int)width, (int)height, "rgba", target == 65535 ? 16 : 8, pixels), SortedSet(features));
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Netpbm fixtures
    // ---------------------------------------------------------------------------------------------------------------------

    private static byte[] U16BERow(IEnumerable<Px> pixels)
    {
        var b = new ByteBuilder();
        foreach (var p in pixels)
        {
            foreach (var v in p)
                b.U16BE(v);
        }

        return b.ToArray();
    }

    private static void BuildPnm(Corpus<FfmpegTools> c)
    {
        // PBM: a set bit is black; the bits past the width are padding
        int[] bits =
        [
            0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 1,
            1, 1, 0, 0, 1, 1, 0, 1, 0, 0, 0,
            0, 0, 0, 1, 0, 1, 1, 1, 0, 1, 1,
        ];
        var bitmapImg = new Img(11, 3, "gray", 8, bits.Select(b => new Px(b != 0 ? 0 : 255)));
        var rows = Enumerable.Range(0, 3).Select(y => PbmPack(bits[(y * 11)..((y + 1) * 11)], 11)).ToList();
        var data = PnmBinary(4, 11, 3, 1, rows, comment: "binary portable bitmap");
        AddFixture(c, Format, "pnm/p4-bitmap-padding", "pnm/p4-bitmap-padding.pbm", data, bitmapImg, "Gray8", "Grayscale", 1,
            "BuildPnm", new Obj { ["width"] = 11, ["height"] = 3 },
            "Binary PBM (P4) with a header comment: a set bit is black, rows are padded to whole bytes (11 pixels use " +
            "two bytes and five padding bits) and the single white-space byte after the header ends it.",
            required: ["pnm.magic=P4", "pnm.pbm.rowPadding", "pnm.comments", "pnm.width=odd"]);

        data = PnmPlain(1, 11, 3, 1, Enumerable.Range(0, 3).Select(y => bits[(y * 11)..((y + 1) * 11)]), comment: "plain portable bitmap");
        AddFixture(c, Format, "pnm/p1-plain-bitmap", "pnm/p1-plain-bitmap.pbm", data, bitmapImg, "Gray8", "Grayscale", 1,
            "BuildPnm", new Obj { ["width"] = 11, ["height"] = 3 },
            "Plain PBM (P1) with the same pixels as pnm/p4-bitmap-padding: every non-blank character is one pixel and " +
            "white space is free-form, so both fixtures must decode to the same reference.",
            required: ["pnm.magic=P1", "pnm.raster=plain"]);

        int[] grays = [0, 17, 34, 51, 255, 128, 64, 32, 16, 8, 200, 150, 100, 50, 1];
        var grayImg = new Img(5, 3, "gray", 8, grays.Select(v => new Px(v)));
        rows = [.. Enumerable.Range(0, 3).Select(y => grays[(y * 5)..((y + 1) * 5)].Select(v => (byte)v).ToArray())];
        data = PnmBinary(5, 5, 3, 255, rows, comment: "MAXVAL 255: samples are stored unchanged");
        AddFixture(c, Format, "pnm/p5-gray8", "pnm/p5-gray8.pgm", data, grayImg, "Gray8", "Grayscale", 8,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["maxval"] = 255 },
            "Binary PGM (P5) with MAXVAL 255: one byte per sample, decoded to Gray8 unchanged.",
            required: ["pnm.magic=P5", "pnm.maxval=255", "pnm.comments"]);

        int[] grays16 = [0, 1, 256, 257, 65535, 32768, 4369, 61166, 8738, 100, 60000, 1000, 2000, 30000, 65534];
        var gray16Img = new Img(5, 3, "gray", 16, grays16.Select(v => new Px(v)));
        rows = [.. Enumerable.Range(0, 3).Select(y => U16BERow(grays16[(y * 5)..((y + 1) * 5)].Select(v => new Px(v))))];
        data = PnmBinary(5, 5, 3, 65535, rows);
        AddFixture(c, Format, "pnm/p5-gray16", "pnm/p5-gray16.pgm", data, gray16Img, "Gray16", "Grayscale", 16,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["maxval"] = 65535 },
            "Binary PGM (P5) with MAXVAL 65535: two big-endian bytes per sample, decoded to Gray16 without losing a bit " +
            "(the low bytes differ from the high bytes so a byte swap or an 8-bit bottleneck is visible).",
            required: ["pnm.magic=P5", "pnm.maxval=65535"]);

        int[] small = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15];
        var scaled = small.Select(v => (v * 255 + 7) / 15).ToList();
        var smallImg = new Img(5, 3, "gray", 8, scaled.Select(v => new Px(v)));
        data = PnmPlain(2, 5, 3, 15, Enumerable.Range(0, 3).Select(y => small[(y * 5)..((y + 1) * 5)]), comment: "MAXVAL 15");
        AddFixture(c, Format, "pnm/p2-plain-gray-maxval15", "pnm/p2-plain-gray-maxval15.pgm", data, smallImg, "Gray8", "Grayscale", 4,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["maxval"] = 15 },
            "Plain PGM (P2) with MAXVAL 15: samples are normalized to the full 8-bit range with the exact ratio " +
            "round(value * 255 / 15), so 0 stays 0 and 15 becomes 255.",
            required: ["pnm.magic=P2", "pnm.maxval=15", "pnm.maxval.normalized"]);

        Px[] rgb =
        [
            new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(16, 32, 48), new(240, 224, 208),
            new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(250, 251, 252), new(128, 128, 128),
            new(11, 22, 33), new(44, 55, 66), new(77, 88, 99), new(200, 100, 50), new(0, 0, 0),
        ];
        var rgbImg = new Img(5, 3, "rgb", 8, rgb);
        rows = [.. Enumerable.Range(0, 3).Select(y => rgb[(y * 5)..((y + 1) * 5)].SelectMany(p => p).Select(v => (byte)v).ToArray())];
        data = PnmBinary(6, 5, 3, 255, rows);
        AddFixture(c, Format, "pnm/p6-rgb8", "pnm/p6-rgb8.ppm", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["maxval"] = 255 },
            "Binary PPM (P6) with MAXVAL 255: three bytes per pixel in red, green, blue order.",
            required: ["pnm.magic=P6", "pnm.tuple=RGB"]);

        Px[] rgb16 =
        [
            new(0, 1, 2), new(65535, 65534, 65533), new(256, 257, 258), new(4369, 8738, 13107), new(1000, 2000, 3000),
            new(60000, 50000, 40000), new(7, 70, 700), new(32768, 32769, 32770), new(5, 6, 7), new(65535, 0, 32768),
            new(11, 2222, 33333), new(44, 5555, 6666), new(777, 8888, 9999), new(20000, 10000, 5000), new(3, 2, 1),
        ];
        var rgb16Img = new Img(5, 3, "rgb", 16, rgb16);
        rows = [.. Enumerable.Range(0, 3).Select(y => U16BERow(rgb16[(y * 5)..((y + 1) * 5)]))];
        data = PnmBinary(6, 5, 3, 65535, rows);
        AddFixture(c, Format, "pnm/p6-rgb16", "pnm/p6-rgb16.ppm", data, rgb16Img, "Rgba64", "Rgb", 16,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["maxval"] = 65535 },
            "Binary PPM (P6) with MAXVAL 65535: the library has no 16-bit RGB pixel format, so the samples are decoded " +
            "into an opaque Rgba64 without losing a bit.",
            required: ["pnm.magic=P6", "pnm.maxval=65535"]);

        Px[] thousand =
        [
            new(0, 500, 1000), new(1000, 0, 500), new(500, 1000, 0), new(1, 2, 3), new(999, 998, 997),
            new(250, 750, 125), new(333, 666, 999), new(100, 200, 300), new(7, 77, 777), new(1000, 1000, 1000),
            new(10, 20, 30), new(400, 500, 600), new(900, 800, 700), new(55, 555, 5), new(0, 0, 0),
        ];
        var scaledThousand = thousand.Select(p => p.Select(v => (int)(((long)v * 65535 + 500) / 1000))).ToList();
        var thousandImg = new Img(5, 3, "rgb", 16, scaledThousand);
        data = PnmPlain(3, 5, 3, 1000, Enumerable.Range(0, 3).Select(y => thousand[(y * 5)..((y + 1) * 5)].SelectMany(p => p)));
        AddFixture(c, Format, "pnm/p3-plain-rgb-maxval1000", "pnm/p3-plain-rgb-maxval1000.ppm", data, thousandImg, "Rgba64", "Rgb", 16,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["maxval"] = 1000 },
            "Plain PPM (P3) with MAXVAL 1000: above 255 the samples are 16-bit, normalized with the exact ratio " +
            "round(value * 65535 / 1000).",
            required: ["pnm.magic=P3", "pnm.maxval=1000", "pnm.maxval.normalized", "pnm.raster=plain"]);

        Px[] rgba =
        [
            new(255, 0, 0, 255), new(0, 255, 0, 128), new(0, 0, 255, 0), new(16, 32, 48, 1), new(240, 224, 208, 254),
            new(1, 2, 3, 64), new(4, 5, 6, 255), new(7, 8, 9, 0), new(250, 251, 252, 192), new(128, 128, 128, 32),
            new(11, 22, 33, 255), new(44, 55, 66, 7), new(77, 88, 99, 250), new(200, 100, 50, 128), new(0, 0, 0, 0),
        ];
        var rgbaImg = new Img(5, 3, "rgba", 8, rgba);
        rows = [.. Enumerable.Range(0, 3).Select(y => rgba[(y * 5)..((y + 1) * 5)].SelectMany(p => p).Select(v => (byte)v).ToArray())];
        data = PnmBinary(7, 5, 3, 255, rows, depth: 4, tupleType: "RGB_ALPHA");
        AddFixture(c, Format, "pnm/p7-rgb-alpha8", "pnm/p7-rgb-alpha8.pam", data, rgbaImg, "Rgba32", "Rgba", 8,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["tupleType"] = "RGB_ALPHA" },
            "PAM (P7) with TUPLTYPE RGB_ALPHA and DEPTH 4: straight alpha, including fully transparent pixels whose " +
            "colors stay defined.",
            required: ["pnm.magic=P7", "pnm.tuple=RGB_ALPHA"]);

        var rgba16 = rgba.Select((p, i) => p.Select(v => v * 257 + (i % 3))).ToList();
        rgba16 = [.. rgba16.Select(p => p.Select(v => Math.Min(65535, v)))];
        var rgba16Img = new Img(5, 3, "rgba", 16, rgba16);
        rows = [.. Enumerable.Range(0, 3).Select(y => U16BERow(rgba16.Skip(y * 5).Take(5)))];
        data = PnmBinary(7, 5, 3, 65535, rows, depth: 4, tupleType: "RGB_ALPHA");
        AddFixture(c, Format, "pnm/p7-rgb-alpha16", "pnm/p7-rgb-alpha16.pam", data, rgba16Img, "Rgba64", "Rgba", 16,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["maxval"] = 65535 },
            "PAM (P7) with MAXVAL 65535 and TUPLTYPE RGB_ALPHA: 16-bit straight alpha decoded into Rgba64.",
            required: ["pnm.magic=P7", "pnm.maxval=65535"]);

        Px[] graya =
        [
            new(0, 255), new(17, 128), new(34, 0), new(51, 1), new(255, 254),
            new(128, 64), new(64, 255), new(32, 0), new(16, 192), new(8, 32),
            new(200, 255), new(150, 7), new(100, 250), new(50, 128), new(1, 0),
        ];
        var grayaImg = new Img(5, 3, "rgba", 8, graya.Select(p => new Px(p[0], p[0], p[0], p[1])));
        rows = [.. Enumerable.Range(0, 3).Select(y => graya[(y * 5)..((y + 1) * 5)].SelectMany(p => p).Select(v => (byte)v).ToArray())];
        data = PnmBinary(7, 5, 3, 255, rows, depth: 2, tupleType: "GRAYSCALE_ALPHA");
        AddFixture(c, Format, "pnm/p7-grayscale-alpha8", "pnm/p7-grayscale-alpha8.pam", data, grayaImg, "Rgba32", "GrayscaleAlpha", 8,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["tupleType"] = "GRAYSCALE_ALPHA" },
            "PAM (P7) with TUPLTYPE GRAYSCALE_ALPHA and DEPTH 2: the library has no gray-with-alpha pixel format, so the " +
            "gray sample is replicated into red, green and blue, which is lossless.",
            required: ["pnm.magic=P7", "pnm.tuple=GRAYSCALE_ALPHA"]);

        int[] bw = [1, 0, 0, 1, 1, 0, 1, 0, 1, 0, 1, 1, 0, 0, 1];
        var bwImg = new Img(5, 3, "gray", 8, bw.Select(v => new Px(v != 0 ? 255 : 0)));
        rows = [.. Enumerable.Range(0, 3).Select(y => bw[(y * 5)..((y + 1) * 5)].Select(v => (byte)v).ToArray())];
        data = PnmBinary(7, 5, 3, 1, rows, depth: 1, tupleType: "BLACKANDWHITE");
        AddFixture(c, Format, "pnm/p7-blackandwhite", "pnm/p7-blackandwhite.pam", data, bwImg, "Gray8", "Grayscale", 1,
            "BuildPnm", new Obj { ["width"] = 5, ["height"] = 3, ["tupleType"] = "BLACKANDWHITE" },
            "PAM (P7) with TUPLTYPE BLACKANDWHITE and MAXVAL 1: unlike a PBM bit, a PAM sample is an intensity, so 0 is " +
            "black and 1 is white (the opposite convention of pnm/p4-bitmap-padding).",
            required: ["pnm.magic=P7", "pnm.tuple=BLACKANDWHITE", "pnm.maxval=1"]);

        var pattern = CommonCorpus.PatternSmoothColor(17, 9);
        (data, var command) = FfmpegEncode(c, pattern, "pnm-ffmpeg-p6", "ppm", ["-pix_fmt", "rgb24"]);
        AddFixture(c, Format, "pnm/ffmpeg-p6-rgb", "pnm/ffmpeg-p6-rgb.ppm", data, pattern, "Rgb24", "Rgb", 8,
            "BuildPnm " + pattern.PatternName, new Obj { ["width"] = 17, ["height"] = 9 },
            "Encoded by FFmpeg's independent PPM encoder from the hand-defined pattern; PPM is lossless, so the pattern " +
            "is the reference.",
            commands: [command], required: ["pnm.magic=P6"]);

        pattern = CommonCorpus.Named(CommonCorpus.PatternCornerMarkers(13, 7), "PatternCornerMarkers");
        (data, command) = FfmpegEncode(c, pattern, "pnm-ffmpeg-p7", "pam", ["-pix_fmt", "rgba"]);
        AddFixture(c, Format, "pnm/ffmpeg-p7-rgba", "pnm/ffmpeg-p7-rgba.pam", data, pattern, "Rgba32", "Rgba", 8,
            "BuildPnm " + pattern.PatternName, new Obj { ["width"] = 13, ["height"] = 7 },
            "Encoded by FFmpeg's independent PAM encoder from the hand-defined pattern (RGB_ALPHA); PAM is lossless, so " +
            "the pattern is the reference.",
            commands: [command], required: ["pnm.magic=P7", "pnm.tuple=RGB_ALPHA"]);

        var gray = CommonCorpus.PatternDetailGray(15, 7, 62);
        (data, command) = FfmpegEncode(c, gray, "pnm-ffmpeg-p5", "pgm", ["-pix_fmt", "gray"]);
        AddFixture(c, Format, "pnm/ffmpeg-p5-gray", "pnm/ffmpeg-p5-gray.pgm", data, gray, "Gray8", "Grayscale", 8,
            "BuildPnm " + gray.PatternName, new Obj { ["width"] = 15, ["height"] = 7, ["seed"] = 62 },
            "Encoded by FFmpeg's independent PGM encoder from the hand-defined pattern; PGM is lossless, so the pattern " +
            "is the reference.",
            commands: [command], required: ["pnm.magic=P5"]);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Malformed, unsupported and over-limit inputs: byte edits of the valid fixtures, each defect confirmed by the
    // transcribed specification above
    // ---------------------------------------------------------------------------------------------------------------------

    private static void BuildErrors(Corpus<FfmpegTools> c)
    {
        var read = ReadValidInputs(c);
        var gray = read["pnm/p5-gray8"];
        AddError(c, Format, "invalid/pnm/zero-width", "invalid/pnm/zero-width.pgm",
            ReplaceFirst(PnmBinary(5, 5, 3, 255, Enumerable.Repeat(new byte[5], 3)), Bytes.Ascii("5 3"), Bytes.Ascii("0 3")),
            "InvalidImageContentException", "The header declares a width of 0.");
        AddError(c, Format, "invalid/pnm/maxval-zero", "invalid/pnm/maxval-zero.pgm",
            ReplaceFirst(PnmBinary(5, 5, 3, 255, Enumerable.Repeat(new byte[5], 3)), Bytes.Ascii("\n255\n"), Bytes.Ascii("\n0\n")),
            "InvalidImageContentException", "MAXVAL is 0; the specification requires 1 to 65535.");
        AddError(c, Format, "invalid/pnm/maxval-65536", "invalid/pnm/maxval-65536.pgm",
            ReplaceFirst(PnmBinary(5, 5, 3, 255, Enumerable.Repeat(new byte[10], 3)), Bytes.Ascii("\n255\n"), Bytes.Ascii("\n65536\n")),
            "InvalidImageContentException", "MAXVAL is 65536, above the 65535 the formats allow.");
        AddError(c, Format, "invalid/pnm/sample-above-maxval", "invalid/pnm/sample-above-maxval.pgm",
            PnmPlain(2, 3, 2, 15, [[0, 7, 16], [15, 1, 2]]),
            "InvalidImageContentException", "A plain PGM sample is 16 while MAXVAL is 15: out-of-range samples are " +
            "rejected, never clamped.");
        AddError(c, Format, "invalid/pnm/truncated-raster", "invalid/pnm/truncated-raster.pgm", Bytes.Slice(gray, null, -3),
            "InvalidImageContentException", "The input ends three bytes before the end of the raster.");
        AddError(c, Format, "invalid/pnm/header-too-long", "invalid/pnm/header-too-long.pgm",
            Bytes.Concat(Bytes.Ascii("P5\n#"), Bytes.Ascii(new string('c', 70000)), Bytes.Ascii("\n5 3\n255\n"), new byte[15]),
            "InvalidImageContentException", "A 70,000-byte comment: the header is bounded at 65,536 bytes so adversarial " +
            "white space and comments cannot make the header walk unbounded.", confirm: false);
        AddError(c, Format, "invalid/pnm/comment-in-token", "invalid/pnm/comment-in-token.pgm",
            Bytes.Concat(Bytes.Ascii("P5\n5 3\n255#comment\n"), new byte[15]),
            "InvalidImageContentException", "A comment starts immediately after MAXVAL: the last header token must be " +
            "followed by exactly one white-space byte, which is what separates the header from a binary raster.",
            confirm: false);
        AddError(c, Format, "invalid/pnm/plain-pbm-digit-2", "invalid/pnm/plain-pbm-digit-2.pbm",
            Bytes.Ascii("P1\n4 2\n0 1 2 1\n1 0 0 1\n"),
            "InvalidImageContentException", "A plain PBM raster contains the digit 2; only 0 and 1 are defined.",
            confirm: false);

        var pam = read["pnm/p7-rgb-alpha8"];
        AddError(c, Format, "invalid/pnm/pam-missing-endhdr", "invalid/pnm/pam-missing-endhdr.pam",
            ReplaceFirst(pam, Bytes.Ascii("ENDHDR\n"), []),
            "InvalidImageContentException", "The PAM header has no ENDHDR line, so the raster never starts.");
        AddError(c, Format, "invalid/pnm/pam-duplicate-width", "invalid/pnm/pam-duplicate-width.pam",
            ReplaceFirst(pam, Bytes.Ascii("WIDTH 5\n"), Bytes.Ascii("WIDTH 5\nWIDTH 5\n")),
            "InvalidImageContentException", "WIDTH is declared twice.");
        AddError(c, Format, "invalid/pnm/pam-depth-mismatch", "invalid/pnm/pam-depth-mismatch.pam",
            ReplaceFirst(pam, Bytes.Ascii("DEPTH 4\n"), Bytes.Ascii("DEPTH 3\n")),
            "InvalidImageContentException", "DEPTH 3 is declared with TUPLTYPE RGB_ALPHA, which has four samples.");
        AddError(c, Format, "invalid/pnm/pam-unknown-keyword", "invalid/pnm/pam-unknown-keyword.pam",
            ReplaceFirst(pam, Bytes.Ascii("MAXVAL 255\n"), Bytes.Ascii("MAXVAL 255\nGAMMA 2\n")),
            "InvalidImageContentException", "The PAM header declares the undefined keyword GAMMA.");
        AddError(c, Format, "invalid/pnm/pam-tuple-cmyk", "invalid/pnm/pam-tuple-cmyk.pam",
            ReplaceFirst(pam, Bytes.Ascii("TUPLTYPE RGB_ALPHA\n"), Bytes.Ascii("TUPLTYPE CMYK\n")),
            "UnsupportedImageFeatureException", "TUPLTYPE CMYK is recognized and rejected: this version decodes the " +
            "standard grayscale, RGB and alpha tuples.", feature: "PAM tuple type", features: ["pnm.unsupported=tuple-type"]);
    }

    private static void BuildLimits(Corpus<FfmpegTools> c)
    {
        AddLimit(c, Format, "limit/pnm/width-over-limit", "pnm/p6-rgb8", new Obj { ["MaxWidth"] = 4 }, "Width",
            "Reuses the 5x3 pnm/p6-rgb8 input with MaxWidth = 4 (checked once the header is complete, before the raster).");
        AddLimit(c, Format, "limit/pnm/total-pixels-over-limit", "pnm/p4-bitmap-padding", new Obj { ["MaxTotalPixels"] = 20 }, "TotalPixels",
            "Reuses the 11x3 (33 pixels) pnm/p4-bitmap-padding input with MaxTotalPixels = 20.");
    }
}
