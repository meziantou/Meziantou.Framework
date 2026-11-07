using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the QOI fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/qoi, invalid/qoi, limit/qoi entries).
/// This generator is the reviewed, reproducible recipe of every committed QOI fixture. It is NEVER run by the
/// tests and does not use the library under test. Expected pixels are hand-defined:
/// - chunk streams assembled chunk by chunk below, each chunk written next to the literal pixel it must produce (every
///   opcode, bias extreme, wrap-around, run and index boundary);
/// - patterns encoded by the qoi.h reference implementation (phoboslab/qoi, MIT, the pinned file below, compiled into a
///   minimal raw &lt;-&gt; QOI driver) and by FFmpeg's independent QOI encoder: QOI is lossless, so the pattern is the
///   reference.
/// Every input is decoded by the transcription of the QOI specification below (strict: header fields, run bounds and the
/// end marker are checked), by the qoi.h reference decoder and by FFmpeg/libavcodec, and every decoding must agree exactly
/// with the reference. Malformed and limit inputs are byte edits of valid streams; each defect is confirmed by the strict
/// transcription (the reference decoder does not validate the end marker or truncation, which is recorded).
/// It complements GoldenCorpus: this generator only replaces the QOI entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format
/// "qoi") and their files, and keeps every other entry and file unchanged; the other generators keep the QOI entries and
/// files when they regenerate their own.
/// Requirements: a C compiler (cc), FFmpeg at the pinned version below (override with --accept-tool-versions after
/// reviewing the differences), and qoi.h from https://github.com/phoboslab/qoi whose SHA-256 is QoiHeaderSha256 (download
/// it yourself: the generator never downloads anything; pass it with --qoi-header). Any OS.
/// </summary>
internal static partial class QoiCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/QoiCorpus.cs";

    // Tool versions used to produce the committed QOI fixtures. Regenerating with other versions is allowed only explicitly
    // (--accept-tool-versions) and must be reviewed.
    public static readonly IReadOnlyDictionary<string, string> PinnedTools = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ffmpeg"] = "6.1.1-3ubuntu5",
    };

    // The reference implementation (https://github.com/phoboslab/qoi/blob/master/qoi.h, MIT), identified by its content.
    public const string QoiHeaderSha256 = "7de6fca1a285b1c20d38f2723dec8b774eb9f144edb9710800a95feeea09375a";

    // A minimal driver around the reference encoder and decoder: raw interleaved samples in, QOI out, and back. Written for
    // this generator; it only calls the public qoi_encode/qoi_decode functions of qoi.h.
    public const string QoiDriver = """

        #define QOI_IMPLEMENTATION
        #define QOI_NO_STDIO
        #include <stdio.h>
        #include <stdlib.h>
        #include "qoi.h"

        static unsigned char *read_all(const char *path, int *size) {
            FILE *f = fopen(path, "rb");
            if (!f) return NULL;
            fseek(f, 0, SEEK_END);
            *size = (int)ftell(f);
            fseek(f, 0, SEEK_SET);
            unsigned char *data = malloc(*size > 0 ? *size : 1);
            if (fread(data, 1, *size, f) != (size_t)*size) { fclose(f); free(data); return NULL; }
            fclose(f);
            return data;
        }

        static int write_all(const char *path, const void *data, int size) {
            FILE *f = fopen(path, "wb");
            if (!f) return 0;
            int ok = fwrite(data, 1, size, f) == (size_t)size;
            fclose(f);
            return ok;
        }

        int main(int argc, char **argv) {
            int size, out_len;
            if (argc == 8 && argv[1][0] == 'e') {
                /* encode <raw> <width> <height> <channels> <colorspace> <out.qoi> */
                unsigned char *raw = read_all(argv[2], &size);
                qoi_desc desc = { (unsigned int)atoi(argv[3]), (unsigned int)atoi(argv[4]), (unsigned char)atoi(argv[5]), (unsigned char)atoi(argv[6]) };
                void *encoded = raw ? qoi_encode(raw, &desc, &out_len) : NULL;
                if (!encoded || !write_all(argv[7], encoded, out_len)) return 1;
                return 0;
            }
            if (argc == 5 && argv[1][0] == 'd') {
                /* decode <in.qoi> <channels: 0 = header> <out.raw>; prints width height channels colorspace */
                unsigned char *data = read_all(argv[2], &size);
                qoi_desc desc;
                int channels = atoi(argv[3]);
                unsigned char *pixels = data ? qoi_decode(data, size, &desc, channels) : NULL;
                if (!pixels) return 1;
                int out_channels = channels ? channels : desc.channels;
                if (!write_all(argv[4], pixels, (int)(desc.width * desc.height * out_channels))) return 1;
                printf("%u %u %u %u\n", desc.width, desc.height, desc.channels, desc.colorspace);
                return 0;
            }
            return 2;
        }

        """;

    public static int Run(GeneratorOptions options)
    {
        var tools = new QoiTools(options.AcceptToolVersions, options.QoiHeader!);
        return CorpusDriver.Run(options, "mfi-qoi-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => $"{fixtures} QOI fixtures; corpus: {files} files, {bytes} bytes",
            "The committed QOI fixtures are reproducible.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // QOI specification (version 1.0, https://qoiformat.org/qoi-specification.pdf), transcribed: a strict reader and the
    // chunk writer used to assemble streams by hand
    // -----------------------------------------------------------------------------------------------------------------

    private static readonly byte[] EndMarker = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly Px Initial = new(0, 0, 0, 255);
    private static readonly Px TransparentBlack = new(0, 0, 0, 0);

    private sealed class QoiException(string message) : Exception(message);

    /// <summary>Chunk statistics of a decoded stream.</summary>
    private sealed class QoiStats
    {
        public SortedSet<string> Ops { get; } = new(StringComparer.Ordinal);

        public List<int> Runs { get; } = [];

        public bool DiffWrap { get; set; }

        public bool LumaWrap { get; set; }

        public bool IndexUnset { get; set; }

        public bool IndexInitial { get; set; }

        public bool RgbKeepsAlpha { get; set; }

        public bool RunCrossesRow { get; set; }

        public bool RunAtStart { get; set; }

        public bool RunAtEnd { get; set; }

        public bool RgbaInRgb { get; set; }
    }

    private sealed record QoiImage(long Width, long Height, int Channels, int Colorspace, List<Px> Pixels, QoiStats Stats);

    private static int QoiHash(Px p) => (p[0] * 3 + p[1] * 5 + p[2] * 7 + p[3] * 11) % 64;

    private static byte[] Header(long width, long height, int channels, int colorspace) =>
        new ByteBuilder().Ascii("qoif").U32BE(width).U32BE(height).U8(channels).U8(colorspace).ToArray();

    /// <summary>Strict decoder: returns (width, height, channels, colorspace, RGBA pixels, chunk statistics). Throws
    /// QoiException for every defect: header fields, truncation, runs past the last pixel, a missing or different end
    /// marker. Bytes after the end marker are ignored.</summary>
    private static QoiImage QoiRead(byte[] data)
    {
        if (data.Length < 14)
            throw new QoiException("truncated header");
        if (!Bytes.Equal(data.AsSpan(0, 4), "qoif"u8))
            throw new QoiException("magic");
        long width = Bytes.U32BE(data, 4), height = Bytes.U32BE(data, 8);
        int channels = data[12], colorspace = data[13];
        if (width == 0 || height == 0)
            throw new QoiException("zero dimension");
        if (channels is not (3 or 4))
            throw new QoiException("channels");
        if (colorspace is not (0 or 1))
            throw new QoiException("colorspace");
        var total = width * height;
        if (total > 1 << 24)
            throw new QoiException("too large for this generator");
        var index = Enumerable.Repeat(TransparentBlack, 64).ToArray();
        var written = new bool[64];
        var p = Initial;
        var pixels = new List<Px>();
        var stats = new QoiStats();
        var offset = 14;

        void Need(int count)
        {
            if (offset + count > data.Length)
                throw new QoiException("truncated chunk stream");
        }

        while (pixels.Count < total)
        {
            Need(1);
            int tag = data[offset];
            var count = 1;
            if (tag == 0xFE)
            {
                Need(4);
                p = new Px(data[offset + 1], data[offset + 2], data[offset + 3], p[3]);
                offset += 4;
                stats.Ops.Add("rgb");
                stats.RgbKeepsAlpha |= p[3] != 255;
            }
            else if (tag == 0xFF)
            {
                Need(5);
                p = new Px(data[offset + 1], data[offset + 2], data[offset + 3], data[offset + 4]);
                offset += 5;
                stats.Ops.Add("rgba");
                stats.RgbaInRgb |= channels == 3 && p[3] != 255;
            }
            else if (tag >> 6 == 0)
            {
                if (!written[tag])
                    stats.IndexUnset = true;
                if (tag == QoiHash(Initial) && index[tag] == Initial)
                    stats.IndexInitial = true;
                p = index[tag];
                offset += 1;
                stats.Ops.Add("index");
            }
            else if (tag >> 6 == 1)
            {
                int[] d = [((tag >> 4) & 3) - 2, ((tag >> 2) & 3) - 2, (tag & 3) - 2];
                int[] raw = [p[0] + d[0], p[1] + d[1], p[2] + d[2]];
                stats.DiffWrap |= raw.Any(v => v < 0 || v > 255);
                p = new Px(Py.Mod(raw[0], 256), Py.Mod(raw[1], 256), Py.Mod(raw[2], 256), p[3]);
                offset += 1;
                stats.Ops.Add("diff");
                stats.RgbKeepsAlpha |= p[3] != 255;
            }
            else if (tag >> 6 == 2)
            {
                Need(2);
                var dg = (tag & 0x3F) - 32;
                int second = data[offset + 1];
                int[] raw = [p[0] + dg + (second >> 4) - 8, p[1] + dg, p[2] + dg + (second & 15) - 8];
                stats.LumaWrap |= raw.Any(v => v < 0 || v > 255);
                p = new Px(Py.Mod(raw[0], 256), Py.Mod(raw[1], 256), Py.Mod(raw[2], 256), p[3]);
                offset += 2;
                stats.Ops.Add("luma");
                stats.RgbKeepsAlpha |= p[3] != 255;
            }
            else
            {
                count = (tag & 0x3F) + 1;
                offset += 1;
                if (count > total - pixels.Count)
                    throw new QoiException("run past the last pixel");
                stats.Ops.Add("run");
                stats.Runs.Add(count);
                long start = pixels.Count;
                stats.RunAtStart |= start == 0;
                stats.RunCrossesRow |= start / width != (start + count - 1) / width;
                stats.RunAtEnd |= start + count == total;
            }

            index[QoiHash(p)] = p;
            written[QoiHash(p)] = true;
            for (var i = 0; i < count; i++)
                pixels.Add(p);
        }

        if (!Bytes.Equal(Bytes.Slice(data, offset, offset + 8), EndMarker))
            throw new QoiException("end marker");
        return new QoiImage(width, height, channels, colorspace, pixels, stats);
    }

    private static List<string> QoiFeatures(byte[] data)
    {
        var (width, _, channels, colorspace, _, stats) = QoiRead(data);
        var features = new HashSet<string>(StringComparer.Ordinal) { "qoi.channels=" + Py.Str(channels), "qoi.colorspace=" + (colorspace != 0 ? "linear" : "srgb") };
        features.UnionWith(stats.Ops.Select(op => "qoi.op=" + op));
        foreach (var (flag, name) in new[]
        {
            (stats.DiffWrap, "qoi.diff.wrap"), (stats.LumaWrap, "qoi.luma.wrap"), (stats.IndexUnset, "qoi.index.unset"),
            (stats.IndexInitial, "qoi.index.initialPixel"), (stats.RgbKeepsAlpha, "qoi.alpha.kept"),
            (stats.RunCrossesRow, "qoi.run.crossesRow"), (stats.RunAtStart, "qoi.run.atStart"), (stats.RunAtEnd, "qoi.run.atEnd"),
            (stats.RgbaInRgb, "qoi.rgbStream.alphaChunks"),
        })
        {
            if (flag)
                features.Add(name);
        }

        if (stats.Runs.Contains(62))
            features.Add("qoi.run=62");
        if (stats.Runs.Contains(1))
            features.Add("qoi.run=1");
        if (width % 2 == 1)
            features.Add("qoi.width=odd");
        return [.. features.Order(StringComparer.Ordinal)];
    }

    /// <summary>Assembles a chunk stream by hand: each chunk is written with the literal pixel(s) it must produce, and the
    /// writer checks the literal against the transcribed semantics as it goes (the hash index, the previous pixel).</summary>
    private sealed class Stream(int width, int height, int channels, int colorspace = 0)
    {
        private readonly List<byte> _chunks = [];
        private readonly List<Px> _pixels = [];
        private readonly Px[] _index = Enumerable.Repeat(TransparentBlack, 64).ToArray();
        private readonly HashSet<int> _written = [];
        private Px _previous = Initial;

        private void Emit(int[] chunk, Px expected, int count = 1)
        {
            Py.Assert(expected.Count == 4);
            foreach (var value in chunk)
                _chunks.Add(checked((byte)value));
            _index[QoiHash(expected)] = expected;
            _written.Add(QoiHash(expected));
            _previous = expected;
            for (var i = 0; i < count; i++)
                _pixels.Add(expected);
        }

        public void Rgb(int r, int g, int b, Px expected)
        {
            Py.Assert(expected == new Px(r, g, b, _previous[3]), "rgb " + expected);
            Emit([0xFE, r, g, b], expected);
        }

        public void Rgba(int r, int g, int b, int a, Px expected)
        {
            Py.Assert(expected == new Px(r, g, b, a), "rgba " + expected);
            Emit([0xFF, r, g, b, a], expected);
        }

        public void IndexOp(int slot, Px expected)
        {
            Py.Assert(slot is >= 0 and < 64 && _index[slot] == expected, $"index {slot} {_index[slot]} {expected}");
            Emit([slot], expected);
        }

        public void Diff(int dr, int dg, int db, Px expected)
        {
            Py.Assert(new[] { dr, dg, db }.All(d => d is >= -2 and <= 1));
            var p = _previous;
            Py.Assert(expected == new Px(Py.Mod(p[0] + dr, 256), Py.Mod(p[1] + dg, 256), Py.Mod(p[2] + db, 256), p[3]), "diff " + expected);
            Emit([0x40 | ((dr + 2) << 4) | ((dg + 2) << 2) | (db + 2)], expected);
        }

        public void Luma(int dg, int drDg, int dbDg, Px expected)
        {
            Py.Assert(dg is >= -32 and <= 31 && drDg is >= -8 and <= 7 && dbDg is >= -8 and <= 7);
            var p = _previous;
            Py.Assert(expected == new Px(Py.Mod(p[0] + dg + drDg, 256), Py.Mod(p[1] + dg, 256), Py.Mod(p[2] + dg + dbDg, 256), p[3]), "luma " + expected);
            Emit([0x80 | (dg + 32), ((drDg + 8) << 4) | (dbDg + 8)], expected);
        }

        /// <summary>The first index slot no chunk has written yet (it still holds transparent black).</summary>
        public int FirstUnsetSlot() => Enumerable.Range(0, 64).Where(slot => !_written.Contains(slot)).Min();

        public void Run(int count, Px expected)
        {
            Py.Assert(count is >= 1 and <= 62 && expected == _previous, "run " + expected);
            Emit([0xC0 | (count - 1)], expected, count);
        }

        public byte[] Data()
        {
            Py.Assert(_pixels.Count == width * height, $"({_pixels.Count}, {width * height})");
            return Bytes.Concat(Header(width, height, channels, colorspace), [.. _chunks], EndMarker);
        }

        public Img Image()
        {
            if (channels == 3)
                return new Img(width, height, "rgb", 8, _pixels.Select(p => p.Slice(0, 3)));
            return new Img(width, height, "rgba", 8, _pixels);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Reference implementation (qoi.h) and FFmpeg
    // -----------------------------------------------------------------------------------------------------------------

    private static byte[] RawSamples(Img img) => img.Model == "rgba" ? img.Raw("rgba8") : img.Raw("rgb8");

    private static (byte[] Data, string Shown) RefEncode(Corpus<QoiTools> c, Img img, int colorspace, string name)
    {
        var channels = img.Model == "rgba" ? 4 : 3;
        var source = c.Scratch / (name + ".raw");
        var output = c.Scratch / (name + ".qoi");
        File.WriteAllBytes(source, RawSamples(img));
        List<string> command = [c.Tools.Driver!.Value.Value, "encode", source.Value, Py.Str(img.Width), Py.Str(img.Height), Py.Str(channels), Py.Str(colorspace), output.Value];
        Proc.Run(command);
        var shown = $"qoi_driver encode {{source}}.raw {Py.Str(img.Width)} {Py.Str(img.Height)} {Py.Str(channels)} {Py.Str(colorspace)} {{output}}.qoi";
        return (File.ReadAllBytes(output), shown);
    }

    /// <summary>Decodes with the reference decoder (the header's channel count); returns RGBA pixels (opaque for 3-channel
    /// streams) and the header it reports.</summary>
    private static (Img? Decoded, (int Channels, int Colorspace)? Reported) RefDecode(Corpus<QoiTools> c, byte[] data, string name)
    {
        var path = c.Scratch / (name + ".ref.qoi");
        var output = c.Scratch / (name + ".ref.raw");
        File.WriteAllBytes(path, data);
        var result = Proc.Exec([c.Tools.Driver!.Value.Value, "decode", path.Value, "0", output.Value]);
        if (result.ExitCode != 0)
            return (null, null);
        var values = Encoding.UTF8.GetString(result.Stdout).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Py.Assert(values.Length == 4);
        var (width, height, channels, colorspace) = (values[0], values[1], values[2], values[3]);
        var raw = File.ReadAllBytes(output);
        var pixels = new List<Px>();
        for (var i = 0; i < raw.Length; i += channels)
        {
            var p = Px.From(Bytes.Slice(raw, i, i + channels));
            pixels.Add(channels == 3 ? p.Append(255) : p);
        }

        return (new Img(width, height, "rgba", 8, pixels), (channels, colorspace));
    }

    private static (byte[] Data, string Shown) FfmpegEncode(Corpus<QoiTools> c, Img img, string name)
    {
        var source = c.Scratch / (name + ".ffsrc.raw");
        var output = c.Scratch / (name + ".ff.qoi");
        File.WriteAllBytes(source, RawSamples(img));
        var pixelFormat = img.Model == "rgba" ? "rgba" : "rgb24";
        var command = c.Tools.FfmpegCmd("-f", "rawvideo", "-pixel_format", pixelFormat, "-video_size", $"{Py.Str(img.Width)}x{Py.Str(img.Height)}",
            "-i", source.Value, "-frames:v", "1", "-c:v", "qoi", output.Value);
        Proc.Run(command);
        var shown = CommonCorpus.Display(command, c.Tools, [(source.Value, "{source}.raw"), (output.Value, "{output}.qoi")]).Replace(c.Tools.Ffmpeg, "ffmpeg", StringComparison.Ordinal);
        return (File.ReadAllBytes(output), shown);
    }

    private static (Img Decoded, string Shown) FfmpegDecode(Corpus<QoiTools> c, byte[] data, string name, int width, int height, string pixelFormat)
    {
        var path = c.Scratch / (name + ".ffdec.qoi");
        File.WriteAllBytes(path, data);
        var command = c.Tools.FfmpegCmd("-i", path.Value, "-f", "rawvideo", "-pix_fmt", pixelFormat, "-");
        var output = Proc.Run(command);
        var bpp = pixelFormat == "rgba" ? 4 : 3;
        Py.Assert(output.Length == width * height * bpp, $"({name}, {output.Length})");
        var pixels = new List<Px>();
        for (var i = 0; i < output.Length; i += bpp)
        {
            var p = Px.From(Bytes.Slice(output, i, i + bpp));
            pixels.Add(bpp == 4 ? p : p.Append(255));
        }

        var shown = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]).Replace(c.Tools.Ffmpeg, "ffmpeg", StringComparison.Ordinal);
        return (new Img(width, height, "rgba", 8, pixels), shown);
    }

    private static Obj CheckRecord(string decoder, string command, Img expected, Img? actual, string? notes = null)
    {
        if (actual is null)
        {
            return new Obj
            {
                ["decoder"] = decoder,
                ["command"] = command,
                ["result"] = "differs",
                ["notes"] = string.IsNullOrEmpty(notes) ? "The decoder rejected the input." : notes,
            };
        }

        var (m, mean, _) = CommonCorpus.Compare(expected.Raw("rgba8"), actual.Raw("rgba8"), 1);
        var record = new Obj
        {
            ["decoder"] = decoder,
            ["command"] = command,
            ["maxAbsoluteError"] = m,
            ["meanAbsoluteError"] = Py.Round(mean, 4),
            ["result"] = m == 0 ? "exact" : "differs",
        };
        if (!string.IsNullOrEmpty(notes))
            record["notes"] = notes;
        return record;
    }

    private static Img ToRgba(Img img) => new(img.Width, img.Height, "rgba", 8, img.Rgba());

    /// <summary>The reference decoder (qoi.h) and FFmpeg must both reproduce the reference exactly (3-channel streams are
    /// compared as opaque RGBA: the reference decoder returns the header's 3 channels, FFmpeg is asked for rgb24).</summary>
    private static List<Obj> CrossChecks(Corpus<QoiTools> c, byte[] data, Img reference, string name)
    {
        var rgba = ToRgba(reference);
        var (decoded, reported) = RefDecode(c, data, name);
        var read = QoiRead(data);
        Py.Assert(reported == (read.Channels, read.Colorspace), $"{name} {reported}");
        var ffFormat = read.Channels == 4 ? "rgba" : "rgb24";
        var (ff, ffCommand) = FfmpegDecode(c, data, name, reference.Width, reference.Height, ffFormat);
        List<Obj> checks =
        [
            CheckRecord($"{c.Tools.QoiLabel} (qoi_decode, header channel count)", "qoi_driver decode {input} 0 {output}.raw", rgba, decoded),
            CheckRecord($"{c.Tools.FfmpegLabel} (libavcodec qoi decoder)", ffCommand, rgba, ff),
        ];
        foreach (var check in checks)
            Py.Assert((string?)check["result"] == "exact", $"{name} {PyJson.Dumps(check)}");
        return checks;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static Obj ExpectedFor(Img img, int channels, int colorspace) => new()
    {
        ["format"] = "qoi",
        ["width"] = img.Width,
        ["height"] = img.Height,
        ["pixelFormat"] = channels == 4 ? "Rgba32" : "Rgb24",
        ["colorModel"] = channels == 4 ? "Rgba" : "Rgb",
        ["bitsPerComponent"] = 8,
        ["orientation"] = 1,
        ["iccProfile"] = "none",
        ["transferFunction"] = colorspace != 0 ? "linear" : "srgb",
        ["animation"] = null,
    };

    private static void AddFixture(Corpus<QoiTools> c, string fixtureId, byte[] data, Img reference, string function, Obj parameters, IList<string> tools,
        IList<string> commands, string description, string? notes = null, IReadOnlyCollection<string>? required = null)
    {
        var (_, _, channels, colorspace, pixels, _) = QoiRead(data);
        Py.Assert(ToRgba(reference).Rgba().SequenceEqual(pixels.Select(p => channels == 4 ? p : p.Slice(0, 3).Append(255))), fixtureId);
        var features = QoiFeatures(data);
        required ??= [];
        Py.Assert(required.All(features.Contains), $"{fixtureId} {Py.Repr(required)} {Py.Repr(features)}");
        var name = fixtureId.Replace('/', '-');
        var checks = CrossChecks(c, data, reference, name);
        List<string> layouts = channels == 4 ? ["rgba8"] : ["rgba8", "rgb8"];
        c.AddValid(fixtureId, fixtureId + ".qoi", data, [reference], ExpectedFor(reference, channels, colorspace),
            c.Provenance(commands.Count > 0 ? "generated" : "hand-authored", function, parameters,
                [.. tools, c.Tools.QoiLabel + " (cross-check only)", c.Tools.FfmpegLabel + " (cross-check only)", ToolSet.RuntimeLabel],
                commands),
            CommonCorpus.HandReference(description, checks), features, layouts: layouts, notes: notes);
    }

    private static void BuildHandStreams(Corpus<QoiTools> c)
    {
        // Every opcode, its bias extremes and wrap-around, the initial pixel, unset and initial-pixel index slots, runs of 1
        // and 62 crossing rows, a run ending the image; 4 channels
        var s = new Stream(7, 13, 4);
        s.Run(2, Initial);                                         // starts from the initial pixel (opaque black)
        s.Rgb(200, 100, 50, new(200, 100, 50, 255));
        s.Diff(-2, -2, -2, new(198, 98, 48, 255));                 // smallest differences
        s.Diff(1, 1, 1, new(199, 99, 49, 255));                    // largest differences
        s.Diff(0, 0, 0, new(199, 99, 49, 255));                    // a zero difference (a run would be smaller, but it is valid)
        s.Luma(-32, -8, 7, new(159, 67, 24, 255));                 // smallest green difference, red/blue extremes
        s.Luma(31, 7, -8, new(197, 98, 47, 255));                  // largest green difference
        s.Rgba(10, 20, 30, 0, new(10, 20, 30, 0));                 // fully transparent with a defined (hidden) color
        s.Rgb(1, 2, 3, new(1, 2, 3, 0));                           // RGB keeps the previous alpha
        s.Diff(-2, 1, 0, new(255, 3, 3, 0));                       // red wraps below zero; alpha is kept
        s.Luma(-4, 3, -8, new(254, 255, 247, 0));                  // green wraps below zero; red and blue too
        s.Rgba(250, 3, 1, 128, new(250, 3, 1, 128));
        s.Diff(1, -2, -2, new(251, 1, 255, 128));                  // blue wraps below zero
        s.Luma(5, 7, -8, new(7, 6, 252, 128));                     // red wraps above 255
        s.IndexOp(QoiHash(new(200, 100, 50, 255)), new(200, 100, 50, 255));
        s.IndexOp(QoiHash(Initial), Initial);                      // stored by the decoder after the initial run
        s.IndexOp(s.FirstUnsetSlot(), new(0, 0, 0, 0));            // a slot never written: transparent black
        s.Rgb(9, 8, 7, new(9, 8, 7, 0));
        s.Run(1, new(9, 8, 7, 0));
        s.Rgba(40, 80, 120, 255, new(40, 80, 120, 255));
        s.Run(62, new(40, 80, 120, 255));                          // the longest run, across several rows
        s.IndexOp(QoiHash(new(10, 20, 30, 0)), new(10, 20, 30, 0));
        s.IndexOp(QoiHash(new(250, 3, 1, 128)), new(250, 3, 1, 128));
        s.Rgba(0, 0, 0, 255, new(0, 0, 0, 255));                   // RGBA chunk reproducing the initial pixel
        s.Rgb(128, 64, 32, new(128, 64, 32, 255));
        s.Run(4, new(128, 64, 32, 255));                           // the run completes the last row and ends the image
        var data = s.Data();
        AddFixture(c, "qoi/chunks-every-op-rgba", data, s.Image(), "BuildHandStreams Stream(7, 13, 4)", new Obj { ["width"] = 7, ["height"] = 13 },
            [], [],
            "Assembled chunk by chunk in the generator, each chunk written with the literal pixel it must produce (QOI " +
            "specification 1.0 semantics, checked by the writer): every opcode, bias extremes, wrap-around, alpha kept by " +
            "RGB/DIFF/LUMA, an index slot never written (transparent black), the initial pixel's slot after a leading run, " +
            "runs of 1 and 62 across rows and a run ending the image.",
            required: ["qoi.op=rgb", "qoi.op=rgba", "qoi.op=index", "qoi.op=diff", "qoi.op=luma", "qoi.op=run", "qoi.run=62",
                "qoi.run=1", "qoi.run.crossesRow", "qoi.run.atStart", "qoi.run.atEnd", "qoi.diff.wrap", "qoi.luma.wrap",
                "qoi.index.unset", "qoi.index.initialPixel", "qoi.alpha.kept"]);

        // A 3-channel stream with RGBA chunks: alpha takes part in the index hash but the image is opaque RGB
        s = new Stream(5, 3, 3);
        s.Rgb(10, 20, 30, new(10, 20, 30, 255));
        s.Rgba(10, 20, 30, 7, new(10, 20, 30, 7));                 // same color, different alpha: a different index slot
        s.Diff(1, 0, -1, new(11, 20, 29, 7));
        s.IndexOp(QoiHash(new(10, 20, 30, 255)), new(10, 20, 30, 255));
        s.IndexOp(QoiHash(new(10, 20, 30, 7)), new(10, 20, 30, 7));
        s.Rgba(200, 150, 100, 0, new(200, 150, 100, 0));
        s.Run(4, new(200, 150, 100, 0));
        s.Luma(-10, 2, -3, new(192, 140, 87, 0));
        s.Rgba(1, 2, 3, 255, new(1, 2, 3, 255));
        s.IndexOp(QoiHash(new(192, 140, 87, 0)), new(192, 140, 87, 0)); // the slot of (11, 20, 29, 7), since overwritten
        s.Rgb(255, 255, 255, new(255, 255, 255, 0));
        s.Run(1, new(255, 255, 255, 0));
        data = s.Data();
        AddFixture(c, "qoi/chunks-rgb-stream-alpha-chunks", data, s.Image(), "BuildHandStreams Stream(5, 3, 3)", new Obj { ["width"] = 5, ["height"] = 3 },
            [], [],
            "Assembled chunk by chunk (literal pixels): a 3-channel stream whose RGBA chunks change alpha. The specification " +
            "keeps alpha in the decoder state (index hash, later RGB chunks), but the header declares RGB: the image is the " +
            "color samples, opaque.",
            notes: "The channel count is authoritative: the reference decoder and FFmpeg return the same opaque RGB samples.",
            required: ["qoi.channels=3", "qoi.rgbStream.alphaChunks"]);

        // The linear colorspace indicator: the samples are unchanged, only the label differs
        s = new Stream(3, 2, 4, colorspace: 1);
        s.Rgba(0, 64, 128, 255, new(0, 64, 128, 255));
        s.Diff(1, 1, 1, new(1, 65, 129, 255));
        s.Luma(20, 0, 0, new(21, 85, 149, 255));
        s.Rgba(255, 0, 0, 128, new(255, 0, 0, 128));
        s.Run(2, new(255, 0, 0, 128));
        data = s.Data();
        AddFixture(c, "qoi/chunks-linear-rgba", data, s.Image(), "BuildHandStreams Stream(3, 2, 4, colorspace=1)", new Obj { ["width"] = 3, ["height"] = 2, ["colorspace"] = 1 },
            [], [],
            "Assembled chunk by chunk (literal pixels) with the header colorspace 1 (all channels linear): the samples are " +
            "decoded unchanged and labeled linear, never converted or relabeled as sRGB.",
            required: ["qoi.colorspace=linear"]);
    }

    /// <summary>Smooth gradients, a wave, sharp edges and seeded noise: a mix of DIFF, LUMA, RGB and index chunks.</summary>
    private static Img PatternPhoto(int w, int h, int seed)
    {
        var rng = new PyRandom(seed);
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var edge = (x / 6 + y / 5) % 2 != 0 ? 60 : 0;
                var r = Py.Round(30 + (double)(150 * x) / Math.Max(1, w - 1)) + edge + rng.RandInt(-3, 3);
                var g = Py.Round(200 - (double)(120 * y) / Math.Max(1, h - 1)) + rng.RandInt(-2, 2);
                var b = Py.Round(120 + 70 * Math.Sin(x * 0.35) * Math.Cos(y * 0.3)) + rng.RandInt(-6, 6);
                pixels.Add(new Px(CommonCorpus.Clamp8(r), CommonCorpus.Clamp8(g), CommonCorpus.Clamp8(b)));
            }
        }

        return CommonCorpus.Named(new Img(w, h, "rgb", 8, pixels), "PatternPhoto");
    }

    /// <summary>Alpha ramp with fully transparent pixels whose (hidden) colors are defined and distinct, plus repeated
    /// colors.</summary>
    private static Img PatternAlphaHidden(int w, int h)
    {
        int[] alphas = [0, 1, 64, 128, 254, 255];
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var alpha = alphas[(x + 2 * y) % 6];
                if ((x + y) % 5 == 0)
                    pixels.Add(new Px(12, 34, 56, alpha));
                else
                    pixels.Add(new Px((x * 41 + 9) % 256, (y * 67 + 30 + x) % 256, (x * y * 23 + 200) % 256, alpha));
            }
        }

        return CommonCorpus.Named(new Img(w, h, "rgba", 8, pixels), "PatternAlphaHidden");
    }

    /// <summary>Flat areas whose run lengths hit the encoder boundaries (61, 62, 63, 124 pixels), runs ending exactly at a
    /// row end and at the image end, and isolated pixels between runs.</summary>
    private static Img PatternRuns(int w, int h)
    {
        var pixels = new List<Px>();
        (Px Color, int Count)[] plan =
        [
            (new(255, 0, 0, 255), 61), (new(0, 255, 0, 255), 1), (new(0, 0, 255, 255), 62), (new(9, 9, 9, 255), 1),
            (new(0, 255, 0, 255), 63), (new(7, 7, 7, 128), 1), (new(255, 255, 0, 255), 124),
        ];
        foreach (var (color, count) in plan)
            pixels.AddRange(Enumerable.Repeat(color, count));
        pixels.AddRange(Enumerable.Repeat(new Px(30, 30, 30, 255), Math.Max(0, w * h - pixels.Count)));
        Py.Assert(pixels.Count == w * h);
        return CommonCorpus.Named(new Img(w, h, "rgba", 8, pixels), "PatternRuns");
    }

    /// <summary>Eight colors (four alpha values) in a seeded order: mostly index chunks.</summary>
    private static Img PatternPalette(int w, int h)
    {
        var rng = new PyRandom(2909);
        Px[] colors =
        [
            new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255), new(255, 255, 255, 0), new(17, 34, 51, 128), new(200, 180, 160, 255),
            new(1, 2, 3, 4), new(90, 90, 90, 255),
        ];
        return CommonCorpus.Named(new Img(w, h, "rgba", 8, Enumerable.Range(0, w * h).Select(_ => colors[rng.RandInt(0, 7)]).ToList()), "PatternPalette");
    }

    private static void ReferenceEncoded(Corpus<QoiTools> c, string fixtureId, Img img, int colorspace, string function, Obj parameters, string description,
        IReadOnlyCollection<string>? required = null)
    {
        var name = fixtureId.Replace('/', '-');
        var (data, command) = RefEncode(c, img, colorspace, name);
        AddFixture(c, fixtureId, data, img, function, parameters, [c.Tools.QoiLabel + " (qoi_encode)"], [command],
            "The pattern itself: encoded by the qoi.h reference encoder (QOI is lossless: every sample, including the " +
            "colors of fully transparent pixels, is preserved). " + description, required: required);
    }

    private static void BuildReference(Corpus<QoiTools> c)
    {
        ReferenceEncoded(c, "qoi/ref-rgb-photo", PatternPhoto(33, 17, 29), 0, "BuildReference PatternPhoto", new Obj { ["width"] = 33, ["height"] = 17, ["seed"] = 29 },
            "Photographic-like RGB content with an odd width.", required: ["qoi.channels=3", "qoi.op=luma", "qoi.op=rgb", "qoi.width=odd"]);
        ReferenceEncoded(c, "qoi/ref-rgba-alpha-hidden", PatternAlphaHidden(9, 7), 0, "BuildReference PatternAlphaHidden", new Obj { ["width"] = 9, ["height"] = 7 },
            "Alpha ramp with defined colors under alpha 0.", required: ["qoi.op=rgba", "qoi.op=index"]);
        ReferenceEncoded(c, "qoi/ref-rgba-runs", PatternRuns(31, 11), 0, "BuildReference PatternRuns", new Obj { ["width"] = 31, ["height"] = 11 },
            "Runs of 61, 62, 63 and 124 pixels, across rows and to the end of the image.",
            required: ["qoi.run=62", "qoi.run.crossesRow", "qoi.run.atEnd"]);
        ReferenceEncoded(c, "qoi/ref-rgba-palette", PatternPalette(16, 9), 0, "BuildReference PatternPalette", new Obj { ["width"] = 16, ["height"] = 9 },
            "Eight colors: mostly index chunks.", required: ["qoi.op=index"]);
        ReferenceEncoded(c, "qoi/ref-1x1", CommonCorpus.Named(new Img(1, 1, "rgba", 8, [new Px(77, 160, 210, 99)]), "literal"), 0,
            "BuildReference literal", new Obj { ["width"] = 1, ["height"] = 1 }, "A single translucent pixel.");
        ReferenceEncoded(c, "qoi/ref-rgb-linear", PatternPhoto(6, 5, 7), 1, "BuildReference PatternPhoto", new Obj { ["width"] = 6, ["height"] = 5, ["seed"] = 7, ["colorspace"] = 1 },
            "Written with the colorspace 1 (linear) indicator: decoded unchanged and labeled linear.",
            required: ["qoi.colorspace=linear", "qoi.channels=3"]);

        // FFmpeg's encoder is a second, independent encoder
        var img = PatternAlphaHidden(12, 5);
        var (data, command) = FfmpegEncode(c, img, "qoi-ffmpeg-rgba-alpha");
        AddFixture(c, "qoi/ffmpeg-rgba-alpha", data, img, "BuildReference PatternAlphaHidden", new Obj { ["width"] = 12, ["height"] = 5 },
            [c.Tools.FfmpegLabel + " (libavcodec qoi encoder)"], [command],
            "The pattern itself: encoded by FFmpeg's QOI encoder (lossless).", required: ["qoi.channels=4"]);
    }

    private static void BuildInvalid(Corpus<QoiTools> c)
    {
        var byId = c.Fixtures.ToDictionary(f => (string)f["id"]!, StringComparer.Ordinal);

        byte[] Read(string fixtureId) => File.ReadAllBytes(c.Out / (string)((Obj)byId[fixtureId]["input"]!)["path"]!);

        void Add(string fixtureId, byte[] data, string notes, Obj? parameters = null)
        {
            try
            {
                QoiRead(data);
            }
            catch (QoiException)
            {
                c.AddError(fixtureId, "invalid", fixtureId + ".qoi", data, "qoi",
                    c.Provenance("hand-authored", "BuildInvalid", parameters, [ToolSet.RuntimeLabel + " (strict transcription, confirms the defect)"]),
                    new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Qoi" }, notes: notes);
                return;
            }

            throw new InvalidOperationException($"{fixtureId}: the strict transcription accepts the input");
        }

        static byte[] U32BE(long value) => new ByteBuilder().U32BE(value).ToArray();

        var valid = Read("qoi/chunks-every-op-rgba");
        Add("invalid/qoi/zero-width", Bytes.Concat(Bytes.Slice(valid, null, 4), U32BE(0), Bytes.Slice(valid, 8)), "The header width is 0.");
        Add("invalid/qoi/zero-height", Bytes.Concat(Bytes.Slice(valid, null, 8), U32BE(0), Bytes.Slice(valid, 12)), "The header height is 0.");
        Add("invalid/qoi/channels-5", Bytes.Concat(Bytes.Slice(valid, null, 12), [5], Bytes.Slice(valid, 13)), "The header channel count is 5 (3 or 4 are defined).");
        Add("invalid/qoi/colorspace-2", Bytes.Concat(Bytes.Slice(valid, null, 13), [2], Bytes.Slice(valid, 14)), "The header colorspace is 2 (0 or 1 are defined).");
        Add("invalid/qoi/truncated-header", Bytes.Slice(valid, null, 10), "The input ends inside the 14-byte header (after the magic and the width).",
            new Obj { ["keptBytes"] = 10 });
        Add("invalid/qoi/truncated-chunks", Bytes.Slice(valid, null, 14 + 20), "The input ends in the middle of the chunk stream: truncated input is " +
            "malformed data, never a clean end of input.", new Obj { ["keptBytes"] = 34, ["fileBytes"] = valid.Length });
        var rgba = Read("qoi/ref-rgba-alpha-hidden");
        var lastRgba = Enumerable.Range(14, Math.Max(0, rgba.Length - 8 - 14)).Where(i => rgba[i] == 0xFF && i + 5 <= rgba.Length - 8).Max();
        Add("invalid/qoi/truncated-in-chunk", Bytes.Slice(rgba, null, lastRgba + 3), "The input ends inside a QOI_OP_RGBA chunk.", new Obj { ["keptBytes"] = lastRgba + 3 });
        Add("invalid/qoi/missing-end-marker", Bytes.Slice(valid, null, -8), "Every pixel is present but the end marker is missing (the input ends after the last chunk).");
        Add("invalid/qoi/end-marker-truncated", Bytes.Slice(valid, null, -4), "The input ends after 4 of the 8 end-marker bytes.");
        Add("invalid/qoi/bad-end-marker", Bytes.Concat(Bytes.Slice(valid, null, -1), [2]), "The last end-marker byte is 0x02 instead of 0x01.");
        // One more pixel chunk than the header declares: the bytes after the last pixel are not the end marker
        Add("invalid/qoi/chunk-after-last-pixel", Bytes.Concat(Bytes.Slice(valid, null, -8), [0xFE, 1, 2, 3], EndMarker),
            "A QOI_OP_RGB chunk follows the last pixel, before the end marker.");
        // A final run longer than the pixels left
        var runs = Read("qoi/ref-rgba-runs");
        Py.Assert(runs[^9] >> 6 == 3 && runs[^9] < 0xFE, "the reference stream ends with a run");
        var remaining = (runs[^9] & 0x3F) + 1;
        Add("invalid/qoi/run-past-end", Bytes.Concat(Bytes.Slice(runs, null, -9), [checked((byte)(0xC0 | remaining))], EndMarker),
            $"The final QOI_OP_RUN chunk repeats {Py.Str(remaining + 1)} pixels where {Py.Str(remaining)} are left.", new Obj { ["runLength"] = remaining + 1, ["pixelsLeft"] = remaining });
    }

    private static void BuildLimits(Corpus<QoiTools> c)
    {
        var byId = c.Fixtures.ToDictionary(f => (string)f["id"]!, StringComparer.Ordinal);

        void Add(string fixtureId, string sourceId, Obj limits, string limitKind, string notes)
        {
            var source = byId[sourceId];
            var input = (Obj)source["input"]!;
            c.AddError(fixtureId, "limit", null, null, "qoi",
                c.Provenance("hand-authored", "BuildLimits", null, [ToolSet.RuntimeLabel]),
                new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = limitKind },
                features: source.TryGetValue("features", out var features) ? (IList<string>?)features : null, decodeOptions: new Obj { ["limits"] = limits },
                existingInput: new Obj { ["path"] = input["path"], ["sha256"] = input["sha256"] }, notes: notes);
        }

        Add("limit/qoi/width-over-limit", "qoi/ref-rgb-photo", new Obj { ["MaxWidth"] = 32 }, "Width",
            "Reuses the 33x17 qoi/ref-rgb-photo input with MaxWidth = 32 (checked from the header).");
        Add("limit/qoi/frame-pixels-over-limit", "qoi/chunks-every-op-rgba", new Obj { ["MaxFramePixels"] = 90 }, "FramePixels",
            "Reuses the 7x13 (91 pixels) qoi/chunks-every-op-rgba input with MaxFramePixels = 90 (checked from the header).");
        var size = File.ReadAllBytes(c.Out / (string)((Obj)byId["qoi/ref-rgba-runs"]["input"]!)["path"]!).Length;
        Add("limit/qoi/encoded-bytes-over-limit", "qoi/ref-rgba-runs", new Obj { ["MaxEncodedBytes"] = size - 1 }, "EncodedBytes",
            $"Reuses qoi/ref-rgba-runs ({Py.Str(size)} bytes) with MaxEncodedBytes one byte below its length: the end marker cannot be " +
            "examined, and the limit is reported instead of truncation.");

        // Dimensions beyond any configurable limit (32-bit header fields above 2^31 - 1)
        var data = Bytes.Concat(Header(0xFFFFFFFF, 1, 4, 0), [0xC0 | 61], EndMarker);
        c.AddError("limit/qoi/width-beyond-int32", "limit", "invalid/qoi/width-beyond-int32.qoi", data, "qoi",
            c.Provenance("hand-authored", "BuildLimits", new Obj { ["width"] = 0xFFFFFFFFL, ["height"] = 1 }, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "Width" }, decodeOptions: new Obj { ["limits"] = new Obj { ["MaxWidth"] = 32768 } },
            notes: "The header declares a width of 2^32 - 1, beyond any configurable limit (the default MaxWidth is stated " +
                   "explicitly): reported as the width limit before anything is allocated (the chunk stream is not examined).");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Driver
    // -----------------------------------------------------------------------------------------------------------------

    private static bool IsQoiFixture(Obj fixture) => (string)fixture["format"]! == "qoi";

    private static int Generate(FullPath outDir, QoiTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<QoiTools>(outDir, tools, ScriptPath))
        {
            tools.BuildDriver(corpus.Scratch);
            BuildHandStreams(corpus);
            BuildReference(corpus);
            BuildInvalid(corpus);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsQoiFixture(fixture), (string)fixture["id"]!);

        // Every other fixture and file of the committed corpus is kept unchanged (the other generators own them)
        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsQoiFixture);
        return fixtures.Count;
    }
}

/// <summary>The tools of the QOI generator: FFmpeg, a C compiler and the pinned qoi.h reference implementation.</summary>
internal sealed class QoiTools : ToolSet
{
    public QoiTools(bool acceptVersions, string qoiHeader)
        : base(Locate(qoiHeader, out var cc, out var header))
    {
        Cc = cc;
        QoiHeader = header;
        if (FfmpegVersion != QoiCorpus.PinnedTools["ffmpeg"] && !acceptVersions)
        {
            throw new FatalException($"FFmpeg {FfmpegVersion} differs from the pinned version {QoiCorpus.PinnedTools["ffmpeg"]}. Review the impact and rerun with --accept-tool-versions " +
                "(then update PinnedTools).");
        }

        QoiLabel = $"qoi.h reference implementation (sha256 {QoiCorpus.QoiHeaderSha256[..12]})";
        DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg" };
    }

    public string Cc { get; }

    public FullPath QoiHeader { get; }

    public string QoiLabel { get; }

    /// <summary>The compiled reference driver (set by <see cref="BuildDriver"/>).</summary>
    public FullPath? Driver { get; private set; }

    public override IReadOnlyDictionary<string, string> DisplayNames { get; }

    /// <summary>Checks the required tools (one message lists all the missing ones) and the pinned qoi.h, before FFmpeg is
    /// run; returns the FFmpeg path.</summary>
    private static string Locate(string qoiHeader, out string cc, out FullPath header)
    {
        var ffmpeg = ExecutableFinder.GetFullExecutablePath("ffmpeg");
        var compiler = ExecutableFinder.GetFullExecutablePath("cc");
        if (ffmpeg is null || compiler is null)
        {
            var missing = new[] { ("ffmpeg", ffmpeg), ("cc", compiler) }.Where(t => t.Item2 is null).Select(t => t.Item1);
            throw new FatalException("Missing required tools: " + string.Join(", ", missing));
        }

        header = FullPath.FromPath(qoiHeader);
        var digest = Bytes.Sha256Hex(File.ReadAllBytes(header));
        if (digest != QoiCorpus.QoiHeaderSha256)
            throw new FatalException($"qoi.h has SHA-256 {digest}; the pinned reference is {QoiCorpus.QoiHeaderSha256}.");
        cc = compiler;
        return ffmpeg;
    }

    public void BuildDriver(FullPath scratch)
    {
        var source = scratch / "qoi_driver.c";
        File.WriteAllText(source, QoiCorpus.QoiDriver, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Driver = scratch / "qoi_driver";
        Proc.Run([Cc, "-O2", "-std=c99", "-I", QoiHeader.Parent.Value, source.Value, "-o", Driver.Value.Value]);
    }
}
