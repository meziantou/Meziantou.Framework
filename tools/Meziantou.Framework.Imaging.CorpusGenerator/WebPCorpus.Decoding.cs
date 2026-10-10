using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

internal static partial class WebPCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // Encoding (libwebp cwebp) and independent decoding (libwebp dwebp/anim_dump, FFmpeg)
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Hand-authored PNG (stored deflate, CommonCorpus.PngFile) used as the cwebp input: RGBA or RGB, 8-bit, no gAMA.</summary>
    private static byte[] PngBytes(Img img)
    {
        var colorType = img.Model switch
        {
            "rgba" => 6,
            "rgb" => 2,
            _ => throw new KeyNotFoundException(img.Model),
        };
        return CommonCorpus.PngFile(img.Width, img.Height, 8, colorType, img.Pixels);
    }

    private static (byte[] Data, string Command) Cwebp(Corpus<WebPTools> c, Img img, IEnumerable<string> args, string name)
    {
        var source = c.Scratch / (name + ".png");
        var output = c.Scratch / (name + ".webp");
        File.WriteAllBytes(source, PngBytes(img));
        List<string> command = [c.Tools.Cwebp, "-quiet", .. args, source.Value, "-o", output.Value];
        Proc.Run(command);
        var data = File.ReadAllBytes(output);
        return (data, Display(command, c.Tools, [(source.Value, "{source}.png"), (output.Value, "{output}.webp")]));
    }

    /// <summary>Parses the P7 RGB_ALPHA PAM written by dwebp/anim_dump (straight alpha, 8-bit).</summary>
    internal static Img ReadPam(byte[] data)
    {
        var index = Bytes.Find(data, "ENDHDR\n"u8);
        if (index < 0)
            throw new InvalidOperationException("subsection not found");
        var end = index + 7;
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Bytes.Latin1(data.AsSpan(0, end)).Split('\n').Skip(1))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space >= 0)
                header[line[..space]] = line[(space + 1)..];
        }

        int width = PyInt(header["WIDTH"]), height = PyInt(header["HEIGHT"]), depth = PyInt(header["DEPTH"]);
        Py.Assert(depth == 4 && PyInt(header["MAXVAL"]) == 255);
        var payload = data[end..];
        Py.Assert(payload.Length == width * height * 4);
        return new Img(width, height, "rgba", 8, CommonCorpus.Chunks(payload, 4).Select(p => Px.From(p)));

        // int(bytes): surrounding whitespace is allowed
        static int PyInt(string text) => int.Parse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    /// <summary>Decodes a still WebP with libwebp dwebp: straight RGBA, default (fancy) chroma upsampling.</summary>
    private static (Img Image, string Command) Dwebp(Corpus<WebPTools> c, byte[] data, string name)
    {
        var path = c.Scratch / (name + ".webp");
        var output = c.Scratch / (name + ".pam");
        File.WriteAllBytes(path, data);
        List<string> command = [c.Tools.Dwebp, "-quiet", "-pam", path.Value, "-o", output.Value];
        Proc.Run(command);
        return (ReadPam(File.ReadAllBytes(output)), Display(command, c.Tools, [(path.Value, "{input}"), (output.Value, "{output}.pam")]));
    }

    /// <summary>Decodes an animation with libwebp anim_dump (WebPAnimDecoder: one full canvas per displayed frame, straight RGBA).</summary>
    private static (List<Img> Frames, string Command) AnimDump(Corpus<WebPTools> c, byte[] data, string name)
    {
        var folder = c.Scratch / (name + "-frames");
        if (Directory.Exists(folder) || File.Exists(folder))
            throw new IOException("File exists: " + folder);
        Directory.CreateDirectory(folder);
        var path = c.Scratch / (name + ".webp");
        File.WriteAllBytes(path, data);
        List<string> command = [c.Tools.AnimDump, "-folder", folder.Value, "-prefix", "frame_", "-pam", path.Value];
        Proc.Run(command);
        var frames = Directory.EnumerateFiles(folder)
            .Where(p => Path.GetFileName(p).StartsWith("frame_", StringComparison.Ordinal) && Path.GetFileName(p).EndsWith(".pam", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        var decoded = frames.Select(p => ReadPam(File.ReadAllBytes(p))).ToList();
        Directory.Delete(folder, recursive: true);
        return (decoded, Display(command, c.Tools, [(path.Value, "{input}"), (folder.Value, "{folder}")]));
    }

    /// <summary>Decodes a still WebP with FFmpeg's own decoders (libavcodec webp: VP8L and VP8 with ALPH; not libwebp),
    /// swscale to straight RGBA with the corpus flags (bilinear, accurate rounding, full chroma interpolation).</summary>
    private static (Img Image, string Command) FfmpegStill(Corpus<WebPTools> c, byte[] data, string name, int width, int height)
    {
        var path = c.Scratch / (name + ".ffmpeg.webp");
        File.WriteAllBytes(path, data);
        var (frames, command) = CommonCorpus.FfmpegDecode(c.Tools, path, "rgba", width, height, 4, false);
        Py.Assert(frames.Count == 1);
        return (new Img(width, height, "rgba", 8, CommonCorpus.Chunks(frames[0], 4).Select(p => Px.From(p))),
            Display(command, c.Tools, [(path.Value, "{input}")]));
    }

    private static readonly Rational KR = new(299, 1000);
    private static readonly Rational KB = new(114, 1000);
    private static readonly Rational KG = 1 - KR - KB;

    /// <summary>BT.601 limited range with exact rationals (Kr = 0.299, Kb = 0.114), rounded to nearest (ties up) and clamped.</summary>
    private static Px YuvToRgbExact(int y, Rational cb, Rational cr)
    {
        var luma = new Rational(255, 219) * (y - 16);
        (cb, cr) = (new Rational(255, 224) * (cb - 128), new Rational(255, 224) * (cr - 128));
        var r = luma + 2 * (1 - KR) * cr;
        var g = luma - 2 * (1 - KB) * KB / KG * cb - 2 * (1 - KR) * KR / KG * cr;
        var b = luma + 2 * (1 - KB) * cb;
        static int Convert(Rational v) => (int)BigIntegerClamp((v + new Rational(1, 2)).Floor());
        return new Px(Convert(r), Convert(g), Convert(b));

        static System.Numerics.BigInteger BigIntegerClamp(System.Numerics.BigInteger value) =>
            System.Numerics.BigInteger.Min(255, System.Numerics.BigInteger.Max(0, value));
    }

    /// <summary>Centered bilinear chroma (MPEG-1/JPEG siting, the 9-3-3-1 weights of libwebp's fancy upsampler) as an exact
    /// rational: the nearest chroma sample 9/16, its horizontal and vertical neighbors on the pixel's side 3/16 each, the
    /// diagonal 1/16; samples past the edges repeat the edge.</summary>
    private static Rational Upsample(byte[] plane, int chromaWidth, int chromaHeight, int x, int y)
    {
        int cx = x >> 1, cy = y >> 1;
        var nx = Math.Min(Math.Max(cx + ((x & 1) != 0 ? 1 : -1), 0), chromaWidth - 1);
        var ny = Math.Min(Math.Max(cy + ((y & 1) != 0 ? 1 : -1), 0), chromaHeight - 1);
        int Value(int u, int v) => plane[v * chromaWidth + u];
        return new Rational(9 * Value(cx, cy) + 3 * Value(nx, cy) + 3 * Value(cx, ny) + Value(nx, ny), 16);
    }

    /// <summary>Decodes a still lossy WebP with FFmpeg's own VP8 decoder (libavcodec, not libwebp) to raw 4:2:0 planes (and
    /// the ALPH plane), without any color conversion, and converts them with YuvToRgbExact: an independent reconstruction
    /// with a fully specified conversion.</summary>
    private static (Img Image, string Command) FfmpegPlanes(Corpus<WebPTools> c, byte[] data, string name, int width, int height, bool alpha)
    {
        var path = c.Scratch / (name + ".ffmpeg.webp");
        File.WriteAllBytes(path, data);
        var pixelFormat = alpha ? "yuva420p" : "yuv420p";
        var command = c.Tools.FfmpegCmd("-i", path.Value, "-f", "rawvideo", "-pix_fmt", pixelFormat, "-");
        var output = Proc.Run(command);
        int cw = Py.FloorDiv(width + 1, 2), ch = Py.FloorDiv(height + 1, 2);
        var expectedLength = width * height * (alpha ? 2 : 1) + 2 * cw * ch;
        Py.Assert(output.Length == expectedLength, Py.ReprTuple([name, output.Length, expectedLength]));
        var yPlane = Bytes.Slice(output, 0, width * height);
        var uPlane = Bytes.Slice(output, width * height, width * height + cw * ch);
        var vPlane = Bytes.Slice(output, width * height + cw * ch, width * height + 2 * cw * ch);
        var aPlane = alpha ? Bytes.Slice(output, width * height + 2 * cw * ch) : null;
        var pixels = new List<Px>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var rgb = YuvToRgbExact(yPlane[y * width + x], Upsample(uPlane, cw, ch, x, y), Upsample(vPlane, cw, ch, x, y));
                pixels.Add(rgb.Append(alpha ? aPlane![y * width + x] : 255));
            }
        }

        return (new Img(width, height, "rgba", 8, pixels), Display(command, c.Tools, [(path.Value, "{input}")]) +
            " (then BT.601 with exact rationals and centered 9-3-3-1 chroma upsampling, rounded to nearest: WebPCorpus.YuvToRgbExact)");
    }

    /// <summary>FFmpeg cross-checks of a lossy still: its VP8 planes converted exactly (justifies the tolerance), and swscale
    /// RGBA (recorded, excluded: swscale's odd-size chroma scaling is a conversion artifact, not a decoder difference).</summary>
    private static List<Obj> LossyCrossChecks(Corpus<WebPTools> c, byte[] data, string name, Img reference, bool hasAlpha, string label)
    {
        var (planes, pcommand) = FfmpegPlanes(c, data, name, reference.Width, reference.Height, hasAlpha);
        var exact = CheckRecord($"{c.Tools.FfmpegLabel} (libavcodec vp8 decoder{(hasAlpha ? " and ALPH" : "")}, raw yuv420p planes) + exact BT.601 conversion{label}",
            pcommand, reference, planes,
            "Lossy inter-decoder differences: libavcodec reconstructs the VP8 planes independently of libwebp; the " +
            "remaining differences come from libwebp's 14-bit fixed-point YUV->RGB tables and integer fancy " +
            "upsampling versus the exact conversion; they bound the tolerance that independent decoders justify.");
        var (swscale, scommand) = FfmpegStill(c, data, name, reference.Width, reference.Height);
        var converted = CheckRecord($"{c.Tools.FfmpegLabel} (libavcodec vp8 decoder{(hasAlpha ? " and ALPH" : "")}, swscale yuv420p->rgba){label}",
            scommand, reference, swscale,
            "swscale converts with bilinear chroma scaling whose sample positions differ from centered upsampling " +
            "(and stretches odd-size chroma planes): an FFmpeg conversion artifact, not a legitimate decoder " +
            "difference, so it does not justify the tolerance.", excluded: true);
        return [exact, converted];
    }

    private static Img ToRgb(Img img)
    {
        var rgba = img.Rgba();
        Py.Assert(rgba.All(p => p[3] == 255));
        return new Img(img.Width, img.Height, "rgb", 8, rgba.Select(p => p.Slice(0, 3)));
    }

    private static Img ToRgba(Img img) => new(img.Width, img.Height, "rgba", 8, img.Rgba());

    private static Obj CheckRecord(string decoder, string command, Img expected, Img actual, string notesDiffers, bool excluded = false)
    {
        var (m, mean, alpha) = CommonCorpus.Compare(expected.Raw("rgba8"), actual.Raw("rgba8"), 1);
        var record = new Obj { ["decoder"] = decoder, ["command"] = command, ["maxAbsoluteError"] = m, ["meanAbsoluteError"] = Py.Round(mean, 4) };
        if (m == 0)
        {
            record["result"] = "exact";
        }
        else if (CommonCorpus.DiffersOnlyInHiddenColors(expected.Raw("rgba8"), actual.Raw("rgba8"), 1))
        {
            record["result"] = "equivalent";
            record["notes"] = "Only the color of fully transparent pixels differs; every visible pixel and every alpha value agree.";
        }
        else
        {
            record["result"] = "differs";
            record["notes"] = notesDiffers + (alpha != 0 ? $" Alpha differs by up to {CommonCorpus.Str(alpha)}." : " Alpha is identical.");
        }

        if (excluded)
            record["excludedFromTolerance"] = true;
        return record;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Animation canvas composition: a transcription of the WebP container specification ("Assembling the Canvas From
    // Frames") with the rounding contract of the library (CommonCorpus.Over: exact rationals).
    // -----------------------------------------------------------------------------------------------------------------

    private static readonly Px Transparent = new(0, 0, 0, 0);

    /// <summary>An animation frame: its rectangle (x, y, the source pattern), its ANMF fields, the recorded pattern text, and
    /// <see cref="Img"/>, the straight RGBA8 pixels of the rectangle as decoded (set by <see cref="Animated"/>).</summary>
    private sealed class AnimFrame
    {
        public required int X { get; init; }

        public required int Y { get; init; }

        public required Img Source { get; init; }

        public required bool Lossless { get; init; }

        public required bool Blend { get; init; }

        public required bool Dispose { get; init; }

        public required int Duration { get; init; }

        public required string Pattern { get; init; }

        public IReadOnlyList<string> Args { get; init; } = [];

        public Img? Img { get; set; }
    }

    /// <summary>frames: x, y, img (straight RGBA8 of the frame rectangle), blend, dispose. Returns one full canvas per
    /// displayed frame. The canvas starts transparent black (the ANIM background color is a hint the library never paints);
    /// the first frame is drawn with SOURCE (blending onto the initial transparent canvas only differs in the hidden colors
    /// of fully transparent source pixels); disposal to background clears the rectangle to transparent black after display.</summary>
    private static List<Img> Compose(int canvasWidth, int canvasHeight, IReadOnlyList<AnimFrame> frames)
    {
        var canvas = Enumerable.Repeat(Transparent, canvasWidth * canvasHeight).ToArray();
        var displayed = new List<Img>();
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var img = frame.Img!;
            var rgba = img.Rgba();
            Py.Assert(frame.X + img.Width <= canvasWidth && frame.Y + img.Height <= canvasHeight);
            for (var y = 0; y < img.Height; y++)
            {
                for (var x = 0; x < img.Width; x++)
                {
                    var position = (frame.Y + y) * canvasWidth + frame.X + x;
                    var source = rgba[y * img.Width + x];
                    canvas[position] = frame.Blend && index > 0 ? CommonCorpus.Over(source, canvas[position], 8) : source;
                }
            }

            displayed.Add(new Img(canvasWidth, canvasHeight, "rgba", 8, [.. canvas]));
            if (frame.Dispose)
            {
                for (var y = 0; y < img.Height; y++)
                {
                    for (var x = 0; x < img.Width; x++)
                        canvas[(frame.Y + y) * canvasWidth + frame.X + x] = Transparent;
                }
            }
        }

        return displayed;
    }

    /// <summary>Canvas positions written by a translucent OVER (0 &lt; source alpha &lt; 255 over a non-transparent canvas):
    /// the only pixels where integer implementations may round differently from the exact contract.</summary>
    private static HashSet<int> BlendedPixels(int canvasWidth, IReadOnlyList<AnimFrame> frames)
    {
        var canvasAlpha = new Dictionary<int, int>();
        var positions = new HashSet<int>();
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var img = frame.Img!;
            var rgba = img.Rgba();
            for (var y = 0; y < img.Height; y++)
            {
                for (var x = 0; x < img.Width; x++)
                {
                    var position = (frame.Y + y) * canvasWidth + frame.X + x;
                    var source = rgba[y * img.Width + x];
                    var current = canvasAlpha.GetValueOrDefault(position, 0);
                    if (frame.Blend && index > 0 && source[3] is > 0 and < 255 && current > 0)
                        positions.Add(position);
                    canvasAlpha[position] = frame.Blend && index > 0 ? CommonCorpus.Over(source, new Px(0, 0, 0, current), 8)[3] : source[3];
                }
            }

            if (frame.Dispose)
            {
                for (var y = 0; y < img.Height; y++)
                {
                    for (var x = 0; x < img.Width; x++)
                        canvasAlpha[(frame.Y + y) * canvasWidth + frame.X + x] = 0;
                }
            }
        }

        return positions;
    }

    /// <summary>Cross-check with libwebp anim_dump. Differences are classified and verified pixel by pixel: hidden colors of
    /// fully transparent pixels, one unit of rounding on translucent OVER results (WebPAnimDecoder blends with 8-bit integer
    /// arithmetic and truncation; the reference rounds exactly), and lossy reconstruction differences (lossy frames only).</summary>
    private static Obj AnimCheck(Corpus<WebPTools> c, byte[] data, string name, List<Img> expectedFrames, HashSet<int> blended, int lossyTolerance = 0)
    {
        var (decoded, command) = AnimDump(c, data, name);
        var record = new Obj { ["decoder"] = $"{c.Tools.LibwebpLabel} anim_dump (WebPAnimDecoder, libwebp demux)", ["command"] = command };
        if (decoded.Count != expectedFrames.Count)
        {
            record["result"] = "differs";
            record["notes"] = $"anim_dump produced {CommonCorpus.Str(decoded.Count)} frames, {CommonCorpus.Str(expectedFrames.Count)} expected.";
            return record;
        }

        var worst = 0;
        var means = new List<double>();
        var explanations = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < expectedFrames.Count; index++)
        {
            Img expected = expectedFrames[index], actual = decoded[index];
            var (m, mean, _) = CommonCorpus.Compare(expected.Raw("rgba8"), actual.Raw("rgba8"), 1);
            worst = Math.Max(worst, m);
            means.Add(mean);
            var expectedRgba = expected.Rgba();
            var actualRgba = actual.Rgba();
            for (var position = 0; position < Math.Min(expectedRgba.Count, actualRgba.Count); position++)
            {
                Px e = expectedRgba[position], a = actualRgba[position];
                if (e == a)
                    continue;
                var difference = e.Zip(a, (x, y) => Math.Abs(x - y)).Max();
                if (e[3] == 0 && a[3] == 0)
                {
                    explanations.Add("hidden");
                }
                else if (blended.Contains(position) && difference <= 1)
                {
                    explanations.Add("rounding");
                }
                else if (lossyTolerance != 0 && e[3] == a[3] && difference <= lossyTolerance)
                {
                    explanations.Add("lossy");
                }
                else
                {
                    throw new InvalidOperationException($"{name} frame {CommonCorpus.Str(index)} pixel {CommonCorpus.Str(position)}: anim_dump {a}, reference {e} (unexplained difference)");
                }
            }
        }

        record["maxAbsoluteError"] = worst;
        record["meanAbsoluteError"] = Py.Round(FloatSum(means) / means.Count, 4);
        if (worst == 0)
        {
            record["result"] = "exact";
        }
        else if (explanations.SetEquals(["hidden"]))
        {
            record["result"] = "equivalent";
            record["notes"] = "Only the color of fully transparent pixels differs; every visible pixel and every alpha value agree.";
        }
        else
        {
            record["result"] = "differs";
            var notes = new List<string>();
            if (explanations.Contains("rounding"))
            {
                notes.Add("translucent OVER results differ by one unit (WebPAnimDecoder blends with 8-bit integer arithmetic " +
                          "and truncation; the reference rounds the exact result to nearest)");
            }

            if (explanations.Contains("hidden"))
                notes.Add("the color of some fully transparent pixels differs");
            if (explanations.Contains("lossy"))
                notes.Add($"lossy frames differ by up to {CommonCorpus.Str(lossyTolerance)} units (libwebp YUV->RGB conversion of the composed frames)");
            record["notes"] = "Verified pixel by pixel by the generator: " + string.Join("; ", notes) + ". Every other pixel and every alpha value agree.";
        }

        return record;
    }

    /// <summary>sum() of a non-empty list of floats as computed by the Python (3.13) that generated the committed WebP
    /// fixtures: since Python 3.12, sum() adds floats with Neumaier compensated summation (0 + first value, then
    /// compensated additions).</summary>
    private static double FloatSum(List<double> values)
    {
        double hi = 0 + values[0], lo = 0;
        for (var i = 1; i < values.Count; i++)
        {
            var x = values[i];
            var t = hi + x;
            if (Math.Abs(hi) >= Math.Abs(x))
                lo += (hi - t) + x;
            else
                lo += (x - t) + hi;
            hi = t;
        }

        return lo != 0 && double.IsFinite(lo) ? hi + lo : hi;
    }
}
