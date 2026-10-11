using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// ---------------------------------------------------------------------------------------------------------------------
// GIF fixtures
// ---------------------------------------------------------------------------------------------------------------------
internal static partial class GoldenCorpus
{
    /// <summary>Raw Graphic Control Extension delays (hundredths) read from the encoded input; None when a frame has no GCE.</summary>
    private static List<object?> GifEncodedDelays(GifInfo info) =>
        [.. info.Frames.Select(f => f.Gce is not null ? new Obj { ["hundredths"] = f.Gce.Delay } : null)];

    private static readonly Px[] GifPalette = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 0), new(255, 255, 255), new(255, 0, 255), new(0, 0, 0), new(1, 2, 3)];
    private const string GifLetters = "RGBYWMKt"; // index 7 is the transparent index in the disposal fixture

    private static readonly Dictionary<char, Px> GifRgba = new()
    {
        ['R'] = new Px(255, 0, 0, 255),
        ['G'] = new Px(0, 255, 0, 255),
        ['B'] = new Px(0, 0, 255, 255),
        ['Y'] = new Px(255, 255, 0, 255),
        ['W'] = new Px(255, 255, 255, 255),
        ['M'] = new Px(255, 0, 255, 255),
        ['K'] = new Px(0, 0, 0, 255),
        ['T'] = new Px(0, 0, 0, 0),
    };

    private static List<int> GifIndices(IEnumerable<string> rows) =>
        [.. rows.SelectMany(r => r.Select(ch =>
        {
            var index = GifLetters.IndexOf(ch, StringComparison.Ordinal);
            Py.Assert(index >= 0);
            return index;
        }))];

    private static Obj GifAnimation(int? totalPlays, int? encodedLoopValue) => new() { ["totalPlays"] = totalPlays, ["encodedLoopValue"] = encodedLoopValue };

    private static void BuildGifHand(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        List<string> tools = [t.FfmpegLabel + " (cross-check only)", ToolSet.RuntimeLabel];
        static Img G(IReadOnlyList<string> rows) => CommonCorpus.GridImage(rows, GifRgba);

        void Add(string fixtureId, byte[] data, List<Img> frames, List<string> durations, Obj? animation, IEnumerable<LzwStats> lzw, List<string?>? encodings = null, string? notes = null,
            Obj? parameters = null)
        {
            var path = c.Scratch / (fixtureId.Replace('/', '_') + ".gif");
            File.WriteAllBytes(path, data);
            var (info, features) = CommonCorpus.GifInspect(data);
            foreach (var lzwStats in lzw)
            {
                features.Add("gif.lzw.maxCodeSize=" + CommonCorpus.Str(lzwStats.MaxCodeSize));
                if (lzwStats.TableFull)
                {
                    features.Add("gif.lzw.tableFull");
                    features.Add("gif.lzw.clearCodes=" + CommonCorpus.Str(lzwStats.ClearCodes));
                    if (lzwStats.ClearCodes == 1)
                        features.Add("gif.lzw.deferredClear");
                }
            }

            features = [.. features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            var (check, _) = c.FfmpegCrossCheck(path, frames, "rgba8", frames.Count > 1, "gif");
            var expectation = new Obj
            {
                ["format"] = "gif",
                ["width"] = frames[0].Width,
                ["height"] = frames[0].Height,
                ["pixelFormat"] = "Rgba32",
                ["colorModel"] = "Indexed",
                ["bitsPerComponent"] = 8,
                ["orientation"] = 1,
                ["iccProfile"] = "none",
                ["animation"] = animation,
            };
            c.AddValid(fixtureId, fixtureId + ".gif", data, frames, expectation,
                c.Provenance("hand-authored", "BuildGifHand", parameters, tools),
                CommonCorpus.HandReference("Hand-authored GIF (generator LZW encoder); displayed frames hand-computed from the GIF89a specification.", [check]),
                features, durations: durations, frameEncodings: encodings, notes: notes,
                encodedDelays: GifEncodedDelays(info));
        }

        // GIF87a, 2-color global palette (minimum code size 2), odd width, no extensions
        List<string> rows = ["RGRGR", "GRGRG", "RRGGR"];
        List<int> idx = [.. rows.SelectMany(r => r.Select(ch => ch == 'R' ? 0 : 1))];
        var (image, stats) = CommonCorpus.GifImage(0, 0, 5, 3, idx, 2);
        var data = CommonCorpus.GifFile("GIF87a", 5, 3, [new Px(255, 0, 0), new Px(0, 255, 0)], 0, [image]);
        Add("gif/gif87a-two-colors-odd-width", data, [G(rows)], ["0/1"], null, [stats],
            notes: "Single frame without loop extension: not animated (animation null).", parameters: new Obj { ["rows"] = rows });

        // Interlaced frame with a local palette overriding the global palette
        List<Px> local = [new(10, 20, 30), new(200, 100, 50), new(0, 128, 255), new(255, 255, 255), new(5, 6, 7), new(90, 80, 70), new(33, 66, 99), new(250, 0, 125)];
        int w = 4, h = 10;
        idx = [.. CommonCorpus.Grid(w, h, (x, y) => new Px((x + 3 * y) % 8)).Select(p => p[0])];
        (image, stats) = CommonCorpus.GifImage(0, 0, w, h, idx, 3, localPalette: local, interlaced: true);
        data = CommonCorpus.GifFile("GIF89a", w, h, [new Px(0, 0, 0), new Px(255, 255, 255)], 0, [image]);
        var img = new Img(w, h, "rgba", 8, idx.Select(i => local[i].Append(255)));
        Add("gif/interlaced-local-palette", data, [img], ["0/1"], null, [stats],
            notes: "Rows are stored in the four interlace passes; the local palette replaces the 2-color global palette.",
            parameters: new Obj { ["index"] = "(x + 3y) mod 8", ["localPalette"] = local });

        // Disposal methods 1, 2, 3 and 0 with transparency, infinite loop, exact hundredths delays
        List<string> f0 = ["BRRR", "RRRR", "RRRR", "RRRR"];
        List<string> d1 = ["Gt", "tY"];
        List<string> d2 = ["Wt", "tW"];
        List<string> d3 = ["M"];
        List<byte[]> blocks = [CommonCorpus.GifNetscape(0)];
        var statsList = new List<LzwStats>();
        foreach (var (frameRows, (x, y), disposal, delay, transparent) in new (List<string>, (int, int), int, int, int?)[]
                 {
                     (f0, (0, 0), 1, 10, null), (d1, (1, 1), 2, 0, 7), (d2, (2, 2), 3, 7, 7), (d3, (0, 3), 0, 25, null),
                 })
        {
            (image, stats) = CommonCorpus.GifImage(x, y, frameRows[0].Length, frameRows.Count, GifIndices(frameRows), 3);
            blocks.AddRange([CommonCorpus.GifGce(disposal, delay, transparent), image]);
            statsList.Add(stats);
        }

        data = CommonCorpus.GifFile("GIF89a", 4, 4, GifPalette, 6, blocks);
        List<Img> expected =
        [
            G(["BRRR", "RRRR", "RRRR", "RRRR"]),
            G(["BRRR", "RGRR", "RRYR", "RRRR"]),
            G(["BRRR", "RTTR", "RTWR", "RRRW"]),
            G(["BRRR", "RTTR", "RTTR", "MRRR"]),
        ];
        Add("gif/disposal-transparency", data, expected, ["1/10", "0/1", "7/100", "1/4"],
            GifAnimation(null, 0), statsList,
            ["full canvas, disposal 1 (do not dispose), delay 10, opaque",
             "2x2 at (1,1), disposal 2 (restore to background), delay 0, transparent index 7",
             "2x2 at (2,2), disposal 3 (restore to previous), delay 7, transparent index 7",
             "1x1 at (0,3), disposal 0 (unspecified), delay 25"],
            notes: "Disposal 2 restores the frame rectangle to transparent black (never the logical-screen background color). NETSCAPE loop 0 means infinite (totalPlays null).");

        // LZW: 64x64 random indices over 256 colors fill the 4096-entry table; a constant first row exercises KwKwK codes
        var rng = new PyRandom(3535);
        var palette = Enumerable.Range(0, 256).Select(_ =>
        {
            var r = rng.RandRange(256);
            var g = rng.RandRange(256);
            var b = rng.RandRange(256);
            return new Px(r, g, b);
        }).ToList();
        (w, h) = (64, 64);
        idx = [.. Enumerable.Repeat(5, w), .. Enumerable.Range(0, w * (h - 1)).Select(_ => rng.RandRange(256))];
        img = new Img(w, h, "rgba", 8, idx.Select(i => palette[i].Append(255)));
        foreach (var (fixtureId, deferred) in new[] { ("gif/lzw-full-table-clear", false), ("gif/lzw-deferred-clear", true) })
        {
            (image, stats) = CommonCorpus.GifImage(0, 0, w, h, idx, 8, deferredClear: deferred);
            Py.Assert(stats.TableFull && stats.MaxCodeSize == 12);
            data = CommonCorpus.GifFile("GIF89a", w, h, palette, 0, [image]);
            Add(fixtureId, data, [img], ["0/1"], null, [stats],
                notes: deferred
                    ? "When the code table is full the encoder keeps emitting 12-bit codes without a clear code (deferred clear)."
                    : "A clear code is emitted each time the 4096-entry code table is full.",
                parameters: new Obj { ["width"] = w, ["height"] = h, ["seed"] = 3535, ["firstRow"] = "constant index 5", ["deferredClear"] = deferred });
        }

        // Invalid / unsupported GIF inputs (expected errors, no fabricated pixels)
        rows = ["RGRG", "GRGR"];
        (image, _) = CommonCorpus.GifImage(0, 0, 4, 2, GifIndices(rows), 3);
        var good = CommonCorpus.GifFile("GIF89a", 4, 2, GifPalette, 0, [image]);
        var truncated = Bytes.Slice(good, 0, good.Length - 6);
        c.AddError("invalid/gif/truncated-image-data", "invalid", "invalid/gif/truncated-image-data.gif", truncated, "gif",
            c.Provenance("hand-authored", "BuildGifHand", new Obj { ["truncatedBytes"] = 6 }, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Gif" },
            notes: "Image data sub-blocks and trailer are cut: truncation must be an error, never a clean end of input.");
        var plainText = Bytes.Concat([0x21, 0x01, 0x0C], new ByteBuilder().U16LE(0).U16LE(0).U16LE(4).U16LE(2).U8(4).U8(8).U8(0).U8(1).ToArray(),
            CommonCorpus.GifSubBlocks(Bytes.Ascii("Hi")));
        var unsupported = CommonCorpus.GifFile("GIF89a", 4, 2, GifPalette, 0, [plainText, image]);
        c.AddError("invalid/gif/plain-text-extension", "unsupported", "invalid/gif/plain-text-extension.gif", unsupported, "gif",
            c.Provenance("hand-authored", "BuildGifHand", null, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Gif" },
            features: CommonCorpus.GifInspect(unsupported).Features,
            notes: "Plain text extensions would require text rendering; they are rejected.");
    }

    private static void BuildGifFfmpeg(Corpus<GoldenTools> c)
    {
        var frames = new List<string>[]
        {
            ["RRGGBB", "RRGGBB", "YYWWKK", "YYWWKK"],
            ["RRGGBB", "RMMGBB", "YMMWKK", "YYWWKK"],
            ["KRGGBW", "RMMGBB", "YMMWKK", "WYWWKK"],
        }.Select(rows => CommonCorpus.GridImage(rows, GifRgba)).ToList();
        AddGifFfmpeg(c, "gif/ffmpeg-animated", frames, "5", "rgba",
            "split[a][b];[a]palettegen=max_colors=16:reserve_transparent=0:stats_mode=full[p];[b][p]paletteuse=dither=none",
            gifFlags: null, loop: 2, "BuildGifFfmpeg", new Obj { ["frames"] = 3, ["rate"] = 5, ["loop"] = 2 },
            "GIF encoded by FFmpeg (exact palette, no dithering) from hand-defined opaque frames; the expected displayed frames are the source frames.",
            "NETSCAPE loop count 2 stores repetitions: totalPlays = 3.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // GIF decoding fixtures: LZW boundaries, palettes, interlacing, clipping, disposal, transparency, loop conventions
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The keyword options of lzw_codes.</summary>
    private sealed record LzwOptions(bool InitialClear = true, int LeadingClears = 1, IReadOnlyList<int>? ClearAt = null, bool EndCode = true)
    {
        /// <summary>The options from Python-style keyword arguments (initial_clear, leading_clears, clear_at, end_code).</summary>
        public static LzwOptions From(Obj options) => new(
            options.TryGetValue("initial_clear", out var initialClear) ? (bool)initialClear! : true,
            options.TryGetValue("leading_clears", out var leadingClears) ? (int)leadingClears! : 1,
            options.TryGetValue("clear_at", out var clearAt) ? (IReadOnlyList<int>)clearAt! : null,
            options.TryGetValue("end_code", out var endCode) ? (bool)endCode! : true);
    }

    /// <summary>Properties of an LZW datastream found by lzw_analyze.</summary>
    private sealed class LzwAnalysis
    {
        public int MaxCodeSize { get; set; }

        public int ClearCodes { get; set; }

        public bool InitialClear { get; set; }

        public bool TableFull { get; set; }

        public bool DeferredClear { get; set; }

        public int Kwkwk { get; set; }

        public bool EndCode { get; set; }

        public bool DataAfterEndCode { get; set; }
    }

    /// <summary>Packs (code, size) pairs least-significant bit first (GIF89a Appendix F).</summary>
    private static byte[] LzwPack(IEnumerable<(int Code, int Size)> codes)
    {
        var output = new List<byte>();
        long accumulator = 0;
        var count = 0;
        foreach (var (code, size) in codes)
        {
            Py.Assert(code >= 0 && code < (1 << size) && size >= 2 && size <= 12, $"({code}, {size})");
            accumulator |= (long)code << count;
            count += size;
            while (count >= 8)
            {
                output.Add((byte)(accumulator & 0xFF));
                accumulator >>= 8;
                count -= 8;
            }
        }

        if (count != 0)
            output.Add((byte)(accumulator & 0xFF));
        return [.. output];
    }

    /// <summary>Variant of lzw_encode returning the (code, size) sequence: optional leading clear codes, clear codes emitted
    /// when the next table code reaches the next value of clear_at (in order, each once: a code-size boundary 2^k, or 2^k + 1
    /// right after the growth) and whenever the table is full, and an optional end code (written with the code size the
    /// decoder has after the last data code).</summary>
    private static List<(int Code, int Size)> LzwCodes(List<int> indices, int minCodeSize, LzwOptions? options = null)
    {
        options ??= new LzwOptions();
        int clear = 1 << minCodeSize, eoi = (1 << minCodeSize) + 1;
        var codes = Enumerable.Repeat((clear, minCodeSize + 1), options.InitialClear ? options.LeadingClears : 0).ToList();
        var table = new Dictionary<(int, int), int>();
        int nextCode = eoi + 1, size = minCodeSize + 1;
        var pendingClears = new List<int>(options.ClearAt ?? []);
        var prefix = indices[0];
        foreach (var k in indices.Skip(1))
        {
            if (table.TryGetValue((prefix, k), out var existing))
            {
                prefix = existing;
                continue;
            }

            codes.Add((prefix, size));
            table[(prefix, k)] = nextCode;
            nextCode++;
            if (nextCode > (1 << size) && size < 12)
                size++;
            if (nextCode == 4096 || (pendingClears.Count > 0 && nextCode == pendingClears[0]))
            {
                if (pendingClears.Count > 0 && nextCode == pendingClears[0])
                    pendingClears.RemoveAt(0);
                codes.Add((clear, size));
                table.Clear();
                nextCode = eoi + 1;
                size = minCodeSize + 1;
            }

            prefix = k;
        }

        Py.Assert(pendingClears.Count == 0, "clear_at values never reached " + Py.Repr(pendingClears));
        codes.Add((prefix, size));
        if (options.EndCode)
        {
            if (nextCode >= (1 << size) && size < 12)
                size++; // the decoder adds an entry for the last data code too
            codes.Add((eoi, size));
        }

        return codes;
    }

    /// <summary>Independent GIF LZW decoding of the encoded bytes (no library code): returns the first <paramref name="pixels"/>
    /// indices and the stream properties used as fixture features.</summary>
    private static (List<int> Indices, LzwAnalysis Stats) LzwAnalyze(byte[] data, int minCodeSize, int pixels)
    {
        int clear = 1 << minCodeSize, eoi = (1 << minCodeSize) + 1;
        var stats = new LzwAnalysis { MaxCodeSize = minCodeSize + 1 };
        Dictionary<int, List<int>> NewTable() => Enumerable.Range(0, clear).ToDictionary(i => i, i => new List<int> { i });
        var table = NewTable();
        int size = minCodeSize + 1, nextCode = eoi + 1;
        int? previous = null;
        long position = 0, total = (long)data.Length * 8;
        var output = new List<int>();
        var first = true;
        while (position + size <= total)
        {
            var code = 0;
            for (var i = 0; i < size; i++)
            {
                var bit = position + i;
                code |= ((data[bit >> 3] >> (int)(bit & 7)) & 1) << i;
            }

            stats.MaxCodeSize = Math.Max(stats.MaxCodeSize, size);
            position += size;
            if (first)
            {
                stats.InitialClear = code == clear;
                first = false;
            }

            if (code == clear)
            {
                stats.ClearCodes++;
                table = NewTable();
                size = minCodeSize + 1;
                nextCode = eoi + 1;
                previous = null;
                continue;
            }

            if (code == eoi)
            {
                stats.EndCode = true;
                stats.DataAfterEndCode = total - position >= 8;
                break;
            }

            if (output.Count >= pixels)
                break; // padding of a stream without end code
            List<int> entry;
            if (previous is null)
            {
                Py.Assert(code < clear, CommonCorpus.Str(code));
                entry = table[code];
            }
            else if (table.TryGetValue(code, out var found))
            {
                entry = found;
            }
            else
            {
                Py.Assert(code == nextCode, $"({code}, {nextCode})");
                entry = [.. table[previous.Value], table[previous.Value][0]];
                stats.Kwkwk++;
            }

            output.AddRange(entry);
            if (previous is not null)
            {
                if (nextCode < 4096)
                {
                    table[nextCode] = [.. table[previous.Value], entry[0]];
                    nextCode++;
                }
                else
                {
                    stats.TableFull = stats.DeferredClear = true;
                }
            }

            if (nextCode >= (1 << size) && size < 12)
                size++;
            if (nextCode == 4096)
                stats.TableFull = true;
            previous = code;
        }

        return ([.. output.Take(pixels)], stats);
    }

    private static byte[] GifImageRaw(int x, int y, int width, int height, int minCodeSize, byte[] data, IReadOnlyList<Px>? localPalette = null, bool interlaced = false)
    {
        var packed = interlaced ? 0x40 : 0;
        byte[] palette = [];
        if (localPalette is not null)
        {
            (var sizeField, palette) = CommonCorpus.GifPaletteBytes(localPalette);
            packed |= 0x80 | sizeField;
        }

        return new ByteBuilder().U8(0x2C).U16LE(x).U16LE(y).U16LE(width).U16LE(height).U8(packed).Bytes(palette).U8(minCodeSize).Bytes(CommonCorpus.GifSubBlocks(data)).ToArray();
    }

    private static byte[] GifImageCodes(int x, int y, int width, int height, IReadOnlyList<int> indices, int minCodeSize, IReadOnlyList<Px>? localPalette = null, bool interlaced = false,
        LzwOptions? options = null)
    {
        var rows = Enumerable.Range(0, height).Select(r => indices.Skip(r * width).Take(width).ToList()).ToList();
        var order = interlaced ? CommonCorpus.GifInterlaceOrder(height) : CommonCorpus.Range(0, height);
        var ordered = order.SelectMany(r => rows[r]).ToList();
        return GifImageRaw(x, y, width, height, minCodeSize, LzwPack(LzwCodes(ordered, minCodeSize, options)), localPalette, interlaced);
    }

    private static byte[] GifApplication(string identifier, IEnumerable<byte[]> subBlocks)
    {
        var output = new ByteBuilder().U8(0x21).U8(0xFF).U8(0x0B).Ascii(identifier);
        foreach (var block in subBlocks)
            output.U8(block.Length).Bytes(block);
        return output.U8(0).ToArray();
    }

    private static byte[] GifComment(string text)
    {
        var blocks = CommonCorpus.GifSubBlocks(Bytes.Latin1(text));
        return new ByteBuilder().U8(0x21).U8(0xFE).Bytes(blocks.AsSpan(0, blocks.Length - 1)).U8(0).ToArray();
    }

    private static byte[] GifAnimexts(int loopCount) => GifApplication("ANIMEXTS1.0", [new ByteBuilder().U8(1).U16LE(loopCount).ToArray()]);

    /// <summary>Hand computation of an opaque/transparent-index rectangle drawn on a literal canvas (lists of characters),
    /// clipped to the canvas: a transparent pixel keeps the canvas character.</summary>
    private static List<string> Paint(List<char[]> canvas, int x, int y, List<string> rows, char? transparent = null)
    {
        for (var dy = 0; dy < rows.Count; dy++)
        {
            var row = rows[dy];
            for (var dx = 0; dx < row.Length; dx++)
            {
                var ch = row[dx];
                int cx = x + dx, cy = y + dy;
                if (ch != transparent && cy >= 0 && cy < canvas.Count && cx >= 0 && cx < canvas[0].Length)
                    canvas[cy][cx] = ch;
            }
        }

        return [.. canvas.Select(r => new string(r))];
    }

    /// <summary>True when every differing RGBA8 pixel satisfies allowed(expected_pixel, actual_pixel).</summary>
    private static bool DiffersOnlyWhere(byte[] expected, byte[] actual, Func<Px, Px, bool> allowed)
    {
        for (var i = 0; i < expected.Length; i += 4)
        {
            Px e = Px.From(Bytes.Slice(expected, i, i + 4)), a = Px.From(Bytes.Slice(actual, i, i + 4));
            if (e != a && !allowed(e, a))
                return false;
        }

        return true;
    }

    private const string GifBackgroundExplanation =
        "FFmpeg paints the logical-screen background color (opaque) where the reference is transparent black: on the initial " +
        "canvas, and when restoring to the background (disposal 2) an image without a transparent index (with one, it clears to " +
        "transparent white). Chromium, Firefox and Apple ImageIO never paint the background color.";

    private const string GifOpaqueExplanation =
        "Apple ImageIO returns the frames whose Graphic Control Extension has no transparent index as opaque images (alpha " +
        "dropped): canvas areas left transparent black appear as opaque black (0,0,0,255). Their colors agree with the " +
        "transparent-black canvas of the reference.";

    /// <summary>An explained cross-check disagreement: kind "background", "opaque", "frames" (only <see cref="Listed"/> may
    /// differ) or "failure".</summary>
    private sealed record GifExplanation(string Kind, string Text, IReadOnlyList<int>? Listed = null);

    /// <summary>GIF decoding fixtures. Every expected frame is a literal grid written by hand from the GIF89a
    /// specification and the decoding rules of the library (rectangles drawn with Paint(), a hand
    /// computation), cross-checked with FFmpeg and Apple ImageIO (CGImageSource): every disagreement is explained and
    /// verified pixel by pixel, never hidden.</summary>
    private static void BuildGifDecoding(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        List<string> tools = [t.FfmpegLabel + " (cross-check only)", "Apple ImageIO " + t.MacOS + " (cross-check only)", ToolSet.RuntimeLabel];
        var colors = new Dictionary<char, Px>(GifRgba) { ['.'] = new Px(0, 0, 0, 0) };

        void Add(string fixtureId, byte[] data, List<Img> frames, List<string> encodings, Obj? animation, Dictionary<string, GifExplanation>? explanations = null,
            IReadOnlyCollection<int>? unverified = null, string? notes = null, Obj? parameters = null)
        {
            var path = c.Scratch / (fixtureId.Replace('/', '_') + ".gif");
            File.WriteAllBytes(path, data);
            var (info, features) = CommonCorpus.GifInspect(data);
            Py.Assert(info.Frames.Count == frames.Count && frames.Count == encodings.Count, fixtureId);
            foreach (var frame in info.Frames)
            {
                var (_, stats) = LzwAnalyze(frame.Data, frame.MinCodeSize, frame.Rect.W * frame.Rect.H);
                features.Add("gif.lzw.maxCodeSize=" + CommonCorpus.Str(stats.MaxCodeSize));
                if (stats.TableFull)
                    features.Add("gif.lzw.tableFull");
                if (stats.DeferredClear)
                    features.Add("gif.lzw.deferredClear");
                if (stats.DataAfterEndCode)
                    features.Add("gif.lzw.dataAfterEndCode");
                if (stats.Kwkwk != 0)
                    features.Add("gif.lzw.kwkwk");
                if (!stats.InitialClear)
                    features.Add("gif.lzw.noInitialClear");
                if (!stats.EndCode)
                    features.Add("gif.lzw.missingEndCode");
                if (stats.ClearCodes > 1)
                    features.Add("gif.lzw.clearCodes=" + CommonCorpus.Str(stats.ClearCodes));
            }

            if (info.Frames.Any(f => f.Gce is not null && f.Gce.Disposal > 3))
                features.Add("gif.disposal.undefined");
            if (info.Frames.Any(f => f.Rect.X + f.Rect.W > info.Width || f.Rect.Y + f.Rect.H > info.Height))
                features.Add("gif.clipped");
            features = [.. features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            Px? background = null;
            if ((data[10] & 0x80) != 0)
            {
                var index = data[11];
                background = Px.From(Bytes.Slice(data, 13 + 3 * index, 16 + 3 * index)).Append(255);
            }

            List<(Obj Check, List<byte[]>? Decoded)> results =
            [
                c.FfmpegCrossCheck(path, frames, "rgba8", true, "gif", allowFailure: true),
                ImageioCrossCheck(c, path, frames, "rgba8", binaryAlpha: true),
            ];
            var checks = results.Select(r => r.Check).ToList();
            var expectedRaw = frames.Select(f => f.Raw("rgba8")).ToList();
            var verified = new HashSet<int>();
            explanations ??= new Dictionary<string, GifExplanation>(StringComparer.Ordinal);
            foreach (var ((check, decoded), key) in results.Zip(new[] { "ffmpeg", "imageio" }))
            {
                var agreeing = new HashSet<int>();
                var sameCount = decoded is not null && decoded.Count == expectedRaw.Count;
                if (sameCount)
                {
                    for (var i = 0; i < expectedRaw.Count; i++)
                    {
                        if (expectedRaw[i].AsSpan().SequenceEqual(decoded![i]) || CommonCorpus.DiffersOnlyInHiddenColors(expectedRaw[i], decoded![i], 1))
                            agreeing.Add(i);
                    }
                }

                verified.UnionWith(agreeing);
                if ((string)check["result"]! is "exact" or "equivalent")
                {
                    Py.Assert(!explanations.ContainsKey(key), fixtureId + ": " + key + " now agrees; remove its explanation");
                    continue;
                }

                Py.Assert(explanations.ContainsKey(key), fixtureId + ": " + key + " disagrees with the reference; explain why: " + PyJson.Dumps(check));
                var explanation = explanations[key];
                var allFrames = Enumerable.Range(0, expectedRaw.Count).ToHashSet();
                if (explanation.Kind is "background" or "opaque")
                {
                    static bool Transparent(Px e) => e == new Px(0, 0, 0, 0);
                    Func<Px, Px, bool> allowed = explanation.Kind == "background"
                        ? (e, a) => Transparent(e) && (a == background || a[3] == 0)
                        : (e, a) => Transparent(e) && a == new Px(0, 0, 0, 255);
                    Py.Assert(sameCount, fixtureId + " " + key + " frame count");
                    var claimed = Enumerable.Range(0, expectedRaw.Count).Where(i => !agreeing.Contains(i) && DiffersOnlyWhere(expectedRaw[i], decoded![i], allowed)).ToHashSet();
                    Py.Assert(agreeing.Union(claimed).ToHashSet().SetEquals(allFrames), fixtureId + " " + key + " " + explanation.Kind + " claim does not hold");
                    verified.UnionWith(claimed);
                }
                else if (explanation.Kind == "frames")
                {
                    Py.Assert(sameCount && agreeing.SetEquals(allFrames.Except(explanation.Listed!)), fixtureId + " " + key + " only the listed frames may differ");
                }
                else
                {
                    Py.Assert(explanation.Kind == "failure", explanation.Kind);
                    Py.Assert(!sameCount, fixtureId + " " + key + " the decoder produced every frame");
                }

                check["notes"] = ((check.TryGetValue("notes", out var existing) ? (string)existing! : "") + " " + explanation.Text).Trim();
            }

            var missing = Enumerable.Range(0, expectedRaw.Count).Except(verified).ToHashSet();
            Py.Assert(missing.SetEquals(unverified ?? []), fixtureId + " frames without independent confirmation");
            List<string> durations = [.. info.Frames.Select(f => f.Gce is not null ? CommonCorpus.Normalize(f.Gce.Delay, 100) : "0/1")];
            var expectation = new Obj
            {
                ["format"] = "gif",
                ["width"] = frames[0].Width,
                ["height"] = frames[0].Height,
                ["pixelFormat"] = "Rgba32",
                ["colorModel"] = "Indexed",
                ["bitsPerComponent"] = 8,
                ["orientation"] = 1,
                ["iccProfile"] = "none",
                ["animation"] = animation,
            };
            c.AddValid(fixtureId, fixtureId + ".gif", data, frames, expectation,
                c.Provenance("hand-authored", "BuildGifDecoding", parameters, tools),
                CommonCorpus.HandReference("Hand-authored GIF (generator LZW encoder, decoded back by the generator's own LZW reader); " +
                    "displayed frames written literally from the GIF89a specification and the decoding rules of " +
                    "the library.", checks),
                features, durations: durations, frameEncodings: encodings, notes: notes,
                encodedDelays: GifEncodedDelays(info));
        }

        List<Img> Grids(IEnumerable<IReadOnlyList<string>> rowsList, IReadOnlyDictionary<char, Px>? palette = null) =>
            [.. rowsList.Select(rows => CommonCorpus.GridImage(rows, palette ?? colors))];

        static List<int> Letters(IEnumerable<string> rows) => GifIndices(rows);

        static byte[] Image(int x, int y, IReadOnlyList<string> rows, int minCodeSize = 3, bool interlaced = false) =>
            GifImageCodes(x, y, rows[0].Length, rows.Count, Letters(rows), minCodeSize, interlaced: interlaced);

        static List<char[]> Canvas(IEnumerable<string> rows) => [.. rows.Select(r => r.ToCharArray())];

        // --- Palette changes: global, local (4 and 16 entries), back to global, transparent index of a 2-entry local table ---
        List<Px> l1 = [new(10, 20, 30), new(200, 100, 50), new(0, 128, 255), new(250, 0, 125)];
        List<Px> l2 = [new(9, 9, 9), new(240, 240, 240)];
        List<Px> l3 = [.. Enumerable.Range(0, 16).Select(i => new Px(16 * i, 255 - 16 * i, (37 * i) % 256))];
        var p = new Dictionary<char, Px>(colors)
        {
            ['a'] = l1[0].Append(255),
            ['b'] = l1[1].Append(255),
            ['c'] = l1[2].Append(255),
            ['d'] = l1[3].Append(255),
            ['e'] = l2[0].Append(255),
            ['p'] = l3[15].Append(255),
            ['q'] = l3[8].Append(255),
            ['r'] = l3[3].Append(255),
            ['s'] = l3[0].Append(255),
        };
        var canvas = Canvas(["RRGGBB", "RRGGBB", "YYWWMM", "YYWWMM"]);
        List<List<string>> expected = [Paint(canvas, 0, 0, [])];
        expected.Add(Paint(canvas, 1, 1, ["abc", "dab"]));
        expected.Add(Paint(canvas, 4, 2, ["KW", "WK"]));
        expected.Add(Paint(canvas, 0, 0, ["eTeTeT"], transparent: 'T'));
        expected.Add(Paint(canvas, 0, 2, ["pq", "rs"]));
        Py.Assert(expected[3][0] == "eReGeB" && expected[4].SequenceEqual(["eReGeB", "RabcBB", "pqabKW", "rsWWWK"], StringComparer.Ordinal));
        List<byte[]> blocks =
        [
            CommonCorpus.GifNetscape(1),
            CommonCorpus.GifGce(1, 5), Image(0, 0, ["RRGGBB", "RRGGBB", "YYWWMM", "YYWWMM"]),
            CommonCorpus.GifGce(1, 0), GifImageCodes(1, 1, 3, 2, [0, 1, 2, 3, 0, 1], 2, localPalette: l1),
            GifImageCodes(4, 2, 2, 2, Letters(["KW", "WK"]), 3),
            CommonCorpus.GifGce(1, 65535, 1), GifImageCodes(0, 0, 6, 1, [0, 1, 0, 1, 0, 1], 2, localPalette: l2),
            CommonCorpus.GifGce(0, 3), GifImageCodes(0, 2, 2, 2, [15, 8, 3, 0], 4, localPalette: l3),
        ];
        Add("gif/palette-changes", CommonCorpus.GifFile("GIF89a", 6, 4, GifPalette, 0, blocks), Grids(expected, p),
            ["full canvas, global palette, disposal 1, delay 5",
             "3x2 at (1,1), local 4-entry palette (minimum code size 2), disposal 1, delay 0",
             "2x2 at (4,2), global palette again, no Graphic Control Extension (zero duration)",
             "6x1 at (0,0), local 2-entry palette, transparent index 1 (odd pixels keep the canvas), delay 65535",
             "2x2 at (0,2), local 16-entry palette (minimum code size 4), disposal 0, delay 3"],
            GifAnimation(2, 1),
            notes: "Each image uses its own local color table or the global one; the transparent index of a local table leaves the " +
                   "canvas unchanged. NETSCAPE loop 1 stores one repetition: totalPlays = 2.",
            parameters: new Obj { ["localPalettes"] = new List<object?> { l1, l2, l3 } });

        // --- Disposal and the background color: bg index 2 (blue) is never painted; no transparent index anywhere ----------
        canvas = Canvas(Enumerable.Repeat("....", 4));
        expected = [Paint(canvas, 1, 1, ["RR", "RR"])];
        Paint(canvas, 1, 1, ["..", ".."]); // disposal 3 on the first image: back to the initial transparent canvas
        expected.Add(Paint(canvas, 0, 0, ["GGGG", "GYYG", "GYYG", "GGGG"]));
        Paint(canvas, 0, 0, ["....", "....", "....", "...."]); // disposal 2: transparent black
        expected.Add(Paint(canvas, 0, 0, ["WW", "WW"]));
        expected.Add(Paint(canvas, 3, 3, ["M"]));
        Paint(canvas, 3, 3, ["."]);
        expected.Add(Paint(canvas, 2, 3, ["KK"]));
        List<string[]> handExpected = [["....", ".RR.", ".RR.", "...."], ["GGGG", "GYYG", "GYYG", "GGGG"], ["WW..", "WW..", "....", "...."],
            ["WW..", "WW..", "....", "...M"], ["WW..", "WW..", "....", "..KK"]];
        Py.Assert(expected.Count == handExpected.Count && expected.Zip(handExpected).All(pair => pair.First.SequenceEqual(pair.Second, StringComparer.Ordinal)));
        blocks =
        [
            CommonCorpus.GifNetscape(0),
            CommonCorpus.GifGce(3, 10), Image(1, 1, ["RR", "RR"]),
            CommonCorpus.GifGce(2, 10), Image(0, 0, ["GGGG", "GYYG", "GYYG", "GGGG"]),
            CommonCorpus.GifGce(0, 10), Image(0, 0, ["WW", "WW"]),
            CommonCorpus.GifGce(2, 10), Image(3, 3, ["M"]),
            CommonCorpus.GifGce(1, 10), Image(2, 3, ["KK"]),
        ];
        Add("gif/disposal-background-color", CommonCorpus.GifFile("GIF89a", 4, 4, GifPalette, 2, blocks), Grids(expected),
            ["2x2 at (1,1) on the initial canvas, disposal 3 (first image: back to transparent black)",
             "full canvas, disposal 2 without transparent index (cleared to transparent black)",
             "2x2 at (0,0), disposal 0", "1x1 at (3,3), disposal 2", "2x1 at (2,3), disposal 1"],
            GifAnimation(null, 0),
            explanations: new(StringComparer.Ordinal) { ["ffmpeg"] = new("background", GifBackgroundExplanation), ["imageio"] = new("opaque", GifOpaqueExplanation) },
            notes: "The logical-screen background color index (2, blue) is never painted: the canvas starts transparent black and " +
                   "disposal 2 clears to transparent black even without a transparent index.");

        // --- Undefined disposal 4: restore to previous (Chromium, Firefox, Apple ImageIO) --------------------------------------
        List<string> full = [.. Enumerable.Repeat("RRRR", 4)];
        expected = [full, ["GGRR", "GGRR", "RRRR", "RRRR"], ["RRRR", "RRRR", "RRRR", "RRRY"]];
        blocks = [CommonCorpus.GifGce(1, 4), Image(0, 0, full), CommonCorpus.GifGce(4, 4), Image(0, 0, ["GG", "GG"]), CommonCorpus.GifGce(1, 4), Image(3, 3, ["Y"])];
        Add("gif/disposal-undefined-4", CommonCorpus.GifFile("GIF89a", 4, 4, GifPalette, 0, blocks), Grids(expected),
            ["full canvas, disposal 1", "2x2 at (0,0), disposal 4 (undefined: restore to previous)", "1x1 at (3,3), disposal 1"],
            GifAnimation(1, null),
            explanations: new(StringComparer.Ordinal)
            {
                ["ffmpeg"] = new("frames", "FFmpeg treats the undefined disposal 4 as 'do not dispose' (frame 2 keeps " +
                                           "the green rectangle); Chromium, Firefox and Apple ImageIO restore the previous canvas.", [2]),
            },
            notes: "Several images without loop extension: animated, played once (totalPlays 1).");

        // --- Undefined disposals 5-7: not specified (Chromium, Firefox, FFmpeg) ------------------------------------------------
        expected = [full, ["GGRR", "GGRR", "RRRR", "RRRR"], ["GGYY", "GGYY", "RRRR", "RRRR"], ["GGYY", "GGYY", "WWRR", "WWRR"],
            ["GGYY", "GGYY", "WWRR", "WWRM"]];
        blocks = [CommonCorpus.GifGce(1, 4), Image(0, 0, full), CommonCorpus.GifGce(5, 4), Image(0, 0, ["GG", "GG"]), CommonCorpus.GifGce(6, 4), Image(2, 0, ["YY", "YY"]),
            CommonCorpus.GifGce(7, 4), Image(0, 2, ["WW", "WW"]), CommonCorpus.GifGce(1, 4), Image(3, 3, ["M"])];
        Add("gif/disposal-undefined-5-7", CommonCorpus.GifFile("GIF89a", 4, 4, GifPalette, 0, blocks), Grids(expected),
            ["full canvas, disposal 1", "2x2 at (0,0), disposal 5", "2x2 at (2,0), disposal 6", "2x2 at (0,2), disposal 7",
             "1x1 at (3,3), disposal 1"],
            GifAnimation(1, null),
            explanations: new(StringComparer.Ordinal)
            {
                ["imageio"] = new("frames", "Apple ImageIO treats every undefined disposal (4-7) as restore to " +
                                            "previous; Chromium, Firefox and FFmpeg treat 5-7 as not specified.", [2, 3, 4]),
            });

        // --- Transparent first image and transparency over the initial canvas -------------------------------------------------
        expected = [["R..G", ".BY."], ["RW.G", "MBY."]];
        blocks = [CommonCorpus.GifNetscape(0), CommonCorpus.GifGce(1, 2, 7), Image(0, 0, ["RttG", "tBYt"]), CommonCorpus.GifGce(0, 2, 7), Image(0, 0, ["tWtt", "Mttt"])];
        Add("gif/transparent-first-frame", CommonCorpus.GifFile("GIF89a", 4, 2, GifPalette, 4, blocks), Grids(expected),
            ["full canvas, transparent index 7 (shows the initial transparent black canvas, not the palette color 1,2,3)",
             "full canvas, transparent index 7 over the previous image and over still-transparent pixels"],
            GifAnimation(null, 0));

        // --- Interlaced images of every small height (empty passes) and odd positions --------------------------------------
        canvas = Canvas(Enumerable.Repeat(new string('.', 9), 9));
        (int X, int Y, int W, int H)[] rects = [(0, 0, 9, 9), (1, 1, 2, 1), (5, 0, 3, 2), (0, 4, 2, 3), (4, 4, 4, 4), (2, 2, 5, 5), (7, 1, 1, 8)];
        const string Order = "RGBYWMK";
        expected = [];
        blocks = [CommonCorpus.GifNetscape(0)];
        List<string> encodings = [];
        for (var n = 0; n < rects.Length; n++)
        {
            var (x, y, w, h) = rects[n];
            List<string> rows = [.. Enumerable.Range(0, h).Select(r => new string([.. Enumerable.Range(0, w).Select(col => Order[(r + 2 * n + (n == 0 ? col : 0)) % 7])]))];
            expected.Add(Paint(canvas, x, y, rows));
            blocks.AddRange([CommonCorpus.GifGce(1, n + 1), Image(x, y, rows, interlaced: true)]);
            encodings.Add($"{w}x{h} at ({x},{y}), interlaced, disposal 1");
        }

        Add("gif/interlaced-heights", CommonCorpus.GifFile("GIF89a", 9, 9, GifPalette, 0, blocks), Grids(expected), encodings,
            GifAnimation(null, 0),
            notes: "Interlaced images with heights 9, 1, 2, 3, 4, 5 and 8: rows are stored in the four passes (rows 0+8k, 4+8k, 2+4k, " +
                   "1+2k), some of them empty; each row has its own color so that any misplaced row is detected.");

        // --- Rectangles clipped by the logical screen ----------------------------------------------------------------------
        expected = [["RGB", "GBY", "BYR"], ["RGB", "GBY", "BYG"], ["RGB", "GBY", "BY."], ["MGB", "GBY", "BY."]];
        blocks = [CommonCorpus.GifGce(1, 10), Image(0, 0, ["RGBY", "GBYR", "BYRG", "YRGB"]),
            CommonCorpus.GifGce(2, 10), Image(2, 2, ["GB", "YW"]),
            CommonCorpus.GifGce(2, 10), Image(5, 5, ["KK", "KK"]),
            CommonCorpus.GifGce(1, 10), Image(0, 0, ["M"])];
        Add("gif/region-clipping", CommonCorpus.GifFile("GIF89a", 3, 3, GifPalette, 0, blocks), Grids(expected),
            ["4x4 at (0,0), larger than the 3x3 screen (clipped)", "2x2 at (2,2), partly outside (clipped), disposal 2",
             "2x2 at (5,5), entirely outside: displayed frame = unchanged canvas, disposal 2 without effect",
             "1x1 at (0,0), disposal 1"],
            GifAnimation(1, null),
            explanations: new(StringComparer.Ordinal)
            {
                ["ffmpeg"] = new("failure", "FFmpeg drops the image that lies entirely outside the logical screen (3 frames " +
                                            "instead of 4) and misplaces the rows of an image wider than the screen."),
                ["imageio"] = new("opaque", GifOpaqueExplanation),
            },
            notes: "Image rectangles are clipped to the logical screen (Apple ImageIO and the browsers); an image outside the " +
                   "screen is still a displayed frame.");

        // --- LZW: minimum code size 1 (2-color image), codes growing from 2 to 12 bits with clear codes ----------------------
        var rng = new PyRandom(1616);
        int width = 64, height = 48;
        List<int> idx = [.. Enumerable.Range(0, width * height).Select(_ => rng.RandRange(2))];
        var data = GifImageCodes(0, 0, width, height, idx, 1);
        var (analyzed, analysis) = LzwAnalyze(LzwCodesData(data), 1, width * height);
        Py.Assert(analyzed.SequenceEqual(idx) && analysis.MaxCodeSize >= 9);
        Add("gif/lzw-min-code-size-1", CommonCorpus.GifFile("GIF89a", width, height, [new Px(255, 0, 0), new Px(0, 255, 0)], 0, [data]),
            [new Img(width, height, "rgba", 8, idx.Select(i => colors["RG"[i]]))], ["full canvas, minimum code size 1 (clear 2, end 3, 2-bit first codes)"],
            null, parameters: new Obj { ["width"] = width, ["height"] = height, ["seed"] = 1616 },
            notes: "A minimum code size of 1 is outside the GIF89a recommendation (2 for bilevel images) but decoded by FFmpeg and " +
                   "Apple ImageIO: the 2-bit code space is full from the start, so the code size grows after the first data code " +
                   $"(then up to {CommonCorpus.Str(analysis.MaxCodeSize)} bits).");

        // --- LZW code-size boundaries: no initial clear, clear codes exactly at and right after each growth, double clear,
        //     KwKwK runs, and a minimum code size larger than the color table needs (8 for 16 colors) ------------------------
        (width, height) = (32, 32);
        rng = new PyRandom(1617);
        var pal16 = Enumerable.Range(0, 16).Select(_ =>
        {
            var r = rng.RandRange(256);
            var g = rng.RandRange(256);
            var b = rng.RandRange(256);
            return new Px(r, g, b);
        }).ToList();
        var q = pal16.Select(color => color.Append(255)).ToList();
        var w2 = width;
        var h2 = height;
        // The patterns are evaluated in order (they share the random generator)
        var patterns = new List<(List<int> Indices, int MinCodeSize, Obj Options)>();
        patterns.Add(([.. Enumerable.Range(0, w2 * h2).Select(_ => rng.RandRange(4))], 2, new Obj { ["initial_clear"] = false, ["clear_at"] = new List<int> { 8, 17, 33, 64, 129 } }));
        patterns.Add(([.. Enumerable.Range(0, w2 * h2).Select(_ => rng.RandRange(8))], 3, new Obj { ["leading_clears"] = 2, ["clear_at"] = new List<int> { 15, 16, 32, 65, 128 } }));
        patterns.Add(([.. Enumerable.Repeat(0, 128), .. Enumerable.Range(0, w2 * h2 - 128).Select(_ => rng.RandRange(16))], 4, new Obj()));
        patterns.Add(([.. CommonCorpus.Grid(w2, h2, (x, y) => new Px((x * 3 + y * 5) % 16)).Select(px => px[0])], 8, new Obj()));
        blocks = [];
        var frames = new List<Img>();
        encodings = [];
        foreach (var (indices, mcs, options) in patterns)
        {
            data = GifImageCodes(0, 0, w2, h2, indices, mcs, options: LzwOptions.From(options));
            (analyzed, _) = LzwAnalyze(LzwCodesData(data), mcs, w2 * h2);
            Py.Assert(analyzed.SequenceEqual(indices));
            blocks.AddRange([CommonCorpus.GifGce(1, 1), data]);
            frames.Add(new Img(w2, h2, "rgba", 8, indices.Select(i => q[i])));
            var description = string.Join(", ", options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key + "=" + Py.Str(o.Value)));
            encodings.Add($"full canvas, minimum code size {CommonCorpus.Str(mcs)}, {(description.Length > 0 ? description : "constant 128-pixel run (KwKwK codes)")}");
        }

        Add("gif/lzw-code-boundaries", CommonCorpus.GifFile("GIF89a", w2, h2, pal16, 0, blocks), frames, encodings, GifAnimation(1, null),
            parameters: new Obj { ["width"] = w2, ["height"] = h2, ["seed"] = 1617 },
            notes: "clear_at lists the next table codes at which a clear code is written: 2^k (just before the code size grows) and " +
                   "2^k + 1 (right after: the clear code is written with the grown size). The first image starts without a clear " +
                   "code; the second starts with two. The last image uses minimum code size 8 with a 16-color table.");

        // --- LZW end code: missing (padding bits form a data code) and followed by ignored data ----------------------------
        List<string> endRows = ["RGB", "YWM"];
        var codes = LzwCodes(Letters(endRows), 3, new LzwOptions(EndCode: false));
        var bits = codes.Sum(code => code.Size);
        Py.Assert(bits % 8 >= 1 && 8 - bits % 8 >= 4); // at least one padding code
        data = GifImageRaw(0, 0, 3, 2, 3, LzwPack(codes));
        Add("gif/lzw-missing-end-code", CommonCorpus.GifFile("GIF89a", 3, 2, GifPalette, 0, [data]), Grids([endRows]),
            ["3x2, the datastream ends after the last data code (no end code; the zero padding bits of the last byte form a 4-bit code)"], null,
            notes: "A missing end code after a complete image is accepted, like FFmpeg and Apple ImageIO: the unused bits of the last " +
                   "byte are padding.");
        codes = [.. LzwCodes(Letters(endRows), 3), (7, 4), (1, 4), (6, 4)];
        var payload = Bytes.Concat(LzwPack(codes), [0xFF, 0xA5]);
        data = new ByteBuilder().U8(0x2C).U16LE(0).U16LE(0).U16LE(3).U16LE(2).U8(0x00).U8(0x03).U8(payload.Length).Bytes(payload)
            .Bytes([0x04, 0xDE, 0xAD, 0xBE, 0xEF, 0x00]).ToArray();
        Add("gif/lzw-data-after-end-code", CommonCorpus.GifFile("GIF89a", 3, 2, GifPalette, 0, [data]), Grids([endRows]),
            ["3x2, the end code is followed by data codes and bytes in the same sub-block and by a second sub-block"], null,
            notes: "Everything after the end code is ignored (FFmpeg and Apple ImageIO do the same).");

        // --- Loop conventions ---------------------------------------------------------------------------------------------
        byte[][] two = [Image(0, 0, ["RG"]), Image(0, 0, ["YW"])];
        blocks = [GifAnimexts(4), CommonCorpus.GifGce(1, 50), two[0], CommonCorpus.GifGce(1, 50), two[1]];
        Add("gif/loop-animexts", CommonCorpus.GifFile("GIF89a", 2, 1, GifPalette, 0, blocks), Grids([["RG"], ["YW"]]), ["full canvas", "full canvas"],
            GifAnimation(5, 4),
            notes: "The ANIMEXTS1.0 loop extension has the NETSCAPE2.0 layout: loop count 4 stores repetitions, totalPlays = 5 " +
                   "(FFmpeg honors it; Apple ImageIO ignores ANIMEXTS1.0).");
        blocks = [CommonCorpus.GifGce(1, 50), two[0], CommonCorpus.GifNetscape(3), CommonCorpus.GifGce(1, 50), two[1], CommonCorpus.GifNetscape(5)];
        Add("gif/loop-after-first-image", CommonCorpus.GifFile("GIF89a", 2, 1, GifPalette, 0, blocks), Grids([["RG"], ["YW"]]), ["full canvas", "full canvas"],
            GifAnimation(6, 5),
            notes: "Loop extensions after the first image (a header identification cannot know them); the last one wins (5: totalPlays " +
                   "6), as in FFmpeg and Apple ImageIO.");
        blocks = [GifApplication("NETSCAPE2.0", [[0x02, 0x00, 0x10, 0x00, 0x00], [0x01, 0xFF, 0xFF]]), CommonCorpus.GifGce(0, 0), Image(0, 0, ["RG"])];
        Add("gif/loop-single-image", CommonCorpus.GifFile("GIF89a", 2, 1, GifPalette, 0, blocks), Grids([["RG"]]), ["full canvas, delay 0"],
            GifAnimation(65536, 65535),
            notes: "One image with a loop extension is a one-frame animation. The NETSCAPE2.0 buffering " +
                   "sub-block (id 2) is skipped; loop count 65535 means 65536 plays (Apple ImageIO reports 65536).");

        // --- Skipped extensions and comments --------------------------------------------------------------------------------
        blocks = [GifComment("first comment"), GifComment("café " + new string('x', 300)),
            [0x21, 0x99, 3, (byte)'a', (byte)'b', (byte)'c', 2, (byte)'d', (byte)'e', 0x00],
            GifApplication("XMP DataXMP", [Bytes.Ascii("<x/>"), [0x01, 0x00]]),
            GifApplication("NETSCAPE2.0", [[0x02, 0x00, 0x00, 0x00, 0x00]]),
            CommonCorpus.GifGce(1, 20), Image(0, 0, ["RGB"]),
            [0x21, 0x77, 0x00],
            CommonCorpus.GifGce(1, 20), Image(1, 0, ["M"])];
        Add("gif/extensions-skipped", CommonCorpus.GifFile("GIF89a", 3, 1, GifPalette, 0, blocks), Grids([["RGB"], ["RMB"]]),
            ["full canvas", "1x1 at (1,0)"], GifAnimation(1, null),
            notes: "Unknown extensions (0x99 with two sub-blocks, 0x77 empty), a non-loop application extension and a NETSCAPE2.0 " +
                   "extension without loop sub-block are skipped; comments become Comment text entries (Latin-1, sub-blocks joined).");

        BuildInvalidGifDecoding(c);
    }

    /// <summary>The concatenated data sub-blocks of an image block produced by GifImageRaw.</summary>
    private static byte[] LzwCodesData(byte[] imageBlock)
    {
        var flags = imageBlock[9];
        var offset = 10 + ((flags & 0x80) != 0 ? 3 * (2 << (flags & 7)) : 0) + 1;
        var data = new List<byte>();
        while (imageBlock[offset] != 0)
        {
            data.AddRange(Bytes.Slice(imageBlock, offset + 1, offset + 1 + imageBlock[offset]));
            offset += 1 + imageBlock[offset];
        }

        return [.. data];
    }

    /// <summary>GIF error fixtures: malformed LZW datastreams and color tables inside valid containers (only decoding
    /// detects them) and block-level defects (found by identification). No expected pixels are fabricated.</summary>
    private static void BuildInvalidGifDecoding(Corpus<GoldenTools> c)
    {
        void Add(string fixtureId, byte[] data, string notes, string kind = "invalid", string exception = "InvalidImageContentException") =>
            c.AddError(fixtureId, kind, fixtureId + ".gif", data, "gif",
                c.Provenance("hand-authored", "BuildInvalidGifDecoding", null, [ToolSet.RuntimeLabel]),
                new Obj { ["exception"] = exception, ["format"] = "Gif" }, notes: notes);

        static byte[] Single(IEnumerable<(int Code, int Size)> codes, int width = 2, int height = 1, IReadOnlyList<Px>? palette = null, int mcs = 2) =>
            CommonCorpus.GifFile("GIF89a", width, height, palette ?? GifPalette, 0, [GifImageRaw(0, 0, width, height, mcs, LzwPack(codes))]);

        // Minimum code size 2: clear 4, end 5, first table code 6, 3-bit codes
        Add("invalid/gif/lzw-code-out-of-range", Single([(4, 3), (0, 3), (7, 3), (5, 3)]),
            "After one literal the next table code is 6: code 7 is not defined (FFmpeg and ImageIO silently decode garbage).");
        Add("invalid/gif/lzw-first-code-not-literal", Single([(4, 3), (6, 3), (5, 3)]),
            "The first code after a clear code must be a color index: code 6 (KwKwK without a previous code) is invalid.");
        Add("invalid/gif/lzw-end-code-too-early", Single([(4, 3), (0, 3), (1, 3), (5, 3)], height: 2),
            "The end code arrives after 2 of the 4 pixels: a short image is an error, never a partial frame.");
        Add("invalid/gif/lzw-truncated-without-end-code", Single([(4, 3), (0, 3), (1, 3)], width: 10),
            "The datastream ends without an end code after 2 indices (4 counting the zero padding bits) of a 10-pixel image.");
        Add("invalid/gif/lzw-too-many-pixels", Single([(4, 3), (0, 3), (1, 3), (2, 3), (5, 3)]),
            "Three indices then the end code for a 2x1 image: more pixels than the rectangle (FFmpeg and ImageIO truncate silently).");
        Add("invalid/gif/lzw-string-exceeds-image", Single([(4, 3), (0, 3), (6, 3), (5, 3)], width: 2),
            "The second code (KwKwK) expands to two indices where one pixel remains.");
        Add("invalid/gif/palette-index-out-of-range", Single([(4, 3), (0, 3), (3, 3), (5, 3)], palette: [new Px(255, 0, 0), new Px(0, 255, 0)]),
            "Index 3 with a 2-entry global color table (FFmpeg shows it transparent, ImageIO as color 0).");
        var noTable = new ByteBuilder().Ascii("GIF89a").U16LE(2).U16LE(1).Bytes([0x00, 0x00, 0x00])
            .Bytes(GifImageRaw(0, 0, 2, 1, 2, LzwPack([(4, 3), (0, 3), (1, 3), (5, 3)]))).U8(0x3B).ToArray();
        Add("invalid/gif/missing-color-table", noTable,
            "Neither a global nor a local color table (FFmpeg also rejects it; the specification only suggests a system default).");
        List<string> rows = ["RG"];
        var good = CommonCorpus.GifFile("GIF89a", 2, 1, GifPalette, 0, [CommonCorpus.GifGce(1, 1), GifImageCodes(0, 0, 2, 1, GifIndices(rows), 3)]);
        Add("invalid/gif/unknown-block", Bytes.Concat(good[..^1], [0x2A, 0x00, 0x3B]), "Byte 0x2A after the first image is neither an extension, an image nor the trailer.");
        Add("invalid/gif/graphic-control-wrong-size", CommonCorpus.GifFile("GIF89a", 2, 1, GifPalette, 0, [[0x21, 0xF9, 0x03, 0x00, 0x01, 0x00, 0x00], GifImageCodes(0, 0, 2, 1, GifIndices(rows), 3)]),
            "A Graphic Control Extension block of 3 bytes instead of 4, before the first image.");
        Add("invalid/gif/lzw-minimum-code-size-12", CommonCorpus.GifFile("GIF89a", 2, 1, GifPalette, 0, [GifImageRaw(0, 0, 2, 1, 12, [0x00, 0x10])]),
            "Minimum code size 12: the clear code would need 13 bits (at most 11 is valid).");
        Add("invalid/gif/missing-trailer", good[..^1], "The file ends after the last image without the trailer: truncation, never a clean end.");
        Add("invalid/gif/no-image", CommonCorpus.GifFile("GIF89a", 2, 1, GifPalette, 0, [CommonCorpus.GifNetscape(0)]), "The trailer follows the loop extension: a GIF needs at least one image.");
        Add("invalid/gif/empty-logical-screen", new ByteBuilder().Ascii("GIF89a").U16LE(0).U16LE(1).Bytes(good.AsSpan(10)).ToArray(), "Logical screen width 0.");
    }
}
