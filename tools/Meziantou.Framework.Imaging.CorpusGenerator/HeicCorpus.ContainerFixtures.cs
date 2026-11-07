using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Heif;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The valid HEIC fixtures: files written by heif-enc (BuildStills, BuildSequences), files assembled around a stream that
// x265 codes from a pattern (BuildWrapped), and files assembled around the payloads of those for the layouts no encoder
// writes (BuildLayouts). Each recipe lists the feature tags it exists for (requires): they are read back from the bytes,
// so a recipe whose encoder stops writing the feature fails the generation.
//
// Traps (each one cost a fixture during the selection):
// - libheif codes an image smaller than 64x64 as a 64x64 picture (the source is extended by repeating its last column
//   and its last row) and declares the image with a clap property: there is no conformance window, and every small
//   heif-enc fixture costs the decoding of 4096 pixels. The tiles of a grid get the same clap, which the library rejects
//   on a tile (the transformations of a grid apply to the assembled image): the grids have tiles of 64x64 and more;
// - a sequence written by heif-enc from images smaller than 64x64 has pictures of 64x64 under a sample entry of the
//   size of the images, without conformance window: the library rejects it. Its sequences are 64x64; the small ones are
//   assembled around x265 streams, which do have a conformance window;
// - heif-enc -S reads every file of the directory whose name follows the numbered pattern of its argument, and heif-dec
//   -S writes the repetitions of a sequence one after the other, without end for an infinite one, unless it is given
//   --ignore-editlist;
// - heif-enc leaves the alpha plane of a tiled image out (--cut-tiles), writes one rotation and one mirror for any
//   combination of --rotate-cw and --flip-h/-v, writes no edit list for a sequence that is played once, declares the
//   arrays of its configuration records incomplete (array_completeness = 0) although the samples hold no parameter set,
//   and stores a grid descriptor in the idat box;
// - x265 does not reproduce its source for an inter picture that is one 64x64 coding tree block, even when it is asked
//   for lossless coding (the three decoders agree with each other, not with the source): the lossless sequence with
//   inter pictures is 72 samples wide. The references never depend on it: they are what the decoders output;
// - the x265 that FFmpeg wraps refuses a picture smaller than 16x16 and always writes a video signal description; the
//   stand-alone x265 of the same release, which the generator runs, writes one only when a color parameter is given;
// - "-p x265:name=value" passes a parameter to x265: "chromaloc" is the only way to make libheif signal where its
//   chroma samples are (it converts RGB to 4:2:0 as if they were centered, whatever the bitstream says);
// - x265 does not code a picture smaller than its smallest coding tree block (16x16): the smallest pictures with a
//   conformance window are 24x24 (a size that is not a multiple of 8 is extended to one);
// - dec265 fails on monochrome pictures above 8 bits whose conformance window crops the right side, and writes other
//   samples than it decodes for a left or top crop above 8 bits (HevcCorpus.Vectors.cs): such recipes declare it left
//   out;
// - a valid fixture costs test time in proportion to its pixels (every generic suite decodes, resizes and convolves it):
//   the patterns are 8x8 unless the feature needs more, and their content is smooth: the references are also the sources
//   of the encoder suites, whose palette criteria (GIF) are calibrated on images of few colors;
// - heif-dec applies clap, irot and imir to what it writes: its output is compared with the oriented reference.
internal static partial class HeicCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // Patterns
    // -----------------------------------------------------------------------------------------------------------------

    private static byte[] Png(Img img) => CommonCorpus.PngFile(img.Width, img.Height, img.Depth, img.Model switch { "gray" => 0, "rgb" => 2, "graya" => 4, _ => 6 }, img.Pixels);

    /// <summary>A sample of <paramref name="bits"/> bits as the 16-bit PNG sample that converts back to it exactly.</summary>
    private static int Deep(long value, int bits)
    {
        var maximum = (1L << bits) - 1;
        return (int)(((Py.Mod(value, maximum + 1) * 65535 * 2) + maximum) / (2 * maximum));
    }

    /// <summary>Strongly colored content whose channels and whose halves all differ, at 10 or 12 bits: every low bit is used.</summary>
    private static Img DeepColor(int w, int h, int bits) =>
        new(w, h, "rgb", 16, CommonCorpus.Grid(w, h, (x, y) => new Px(
            Deep((((1L << bits) - 1) * x / Math.Max(1, w - 1)) - (3 * y), bits),
            Deep(((1L << bits) - 1) - (((1L << bits) - 1) * y / Math.Max(1, h - 1)) + x, bits),
            Deep(((1L << bits) / 3) + (37 * x) + (61 * y), bits))));

    private static Img DeepGray(int w, int h, int bits) =>
        new(w, h, "gray", 16, CommonCorpus.Grid(w, h, (x, y) => new Px(Deep((((1L << bits) - 1) * (x + (2 * y)) / Math.Max(1, w - 1 + (2 * (h - 1)))) + (5 * x), bits))));

    private static Img Gray(int w, int h) =>
        new(w, h, "gray", 8, CommonCorpus.Grid(w, h, (x, y) => new Px(((23 * x) + (61 * y) + 7) % 256)));

    /// <summary>A smooth gray ramp: the content of the lossy monochrome fixtures.</summary>
    private static Img SmoothGray(int w, int h) =>
        new(w, h, "gray", 8, CommonCorpus.Grid(w, h, (x, y) => new Px(30 + (190 * (x + (2 * y)) / Math.Max(1, w - 1 + (2 * (h - 1)))))));

    /// <summary>Colors under an alpha ramp that includes 0 (hidden colors) and 255.</summary>
    private static Img WithAlpha(Img rgb, int maximum = 255) =>
        new(rgb.Width, rgb.Height, "rgba", rgb.Depth, rgb.Pixels.Select((p, i) => p.Append((((i % rgb.Width) + (2 * (i / rgb.Width))) % 5) switch { 0 => 0, 4 => maximum, var step => maximum * step / 4 })));

    /// <summary>Deep colors under an alpha ramp of the same depth that includes 0 (hidden colors) and the maximum.</summary>
    private static Img WithDeepAlpha(Img rgb, int bits) =>
        new(rgb.Width, rgb.Height, "rgba", 16, rgb.Pixels.Select((p, i) => p.Append((((i % rgb.Width) + (2 * (i / rgb.Width))) % 5) switch { 0 => 0, 4 => 65535, var step => Deep((((1L << bits) - 1) * step / 4) + 1, bits) })));

    private static Img GrayAlpha(int w, int h) =>
        new(w, h, "graya", 8, CommonCorpus.Grid(w, h, (x, y) => new Px(((29 * x) + (13 * y) + 3) % 256, (x + y) % 4 == 0 ? 0 : 255 - (30 * ((x + (2 * y)) % 6)))));

    /// <summary>An 8-bit pattern as 16-bit samples (each sample times 257): the source of a lossy 10 or 12-bit encode.</summary>
    private static Img Widen(Img img) => new(img.Width, img.Height, img.Model, 16, img.Pixels.Select(p => p.Select(v => v * 257)));

    /// <summary>A frame of a sequence: smooth gradients that drift by a few columns per frame and a row every other frame.</summary>
    private static Img Drifting(int w, int h, int frame, int step = 1) =>
        new(w, h, "rgb", 8, CommonCorpus.Grid(w, h, (x, y) =>
        {
            var (column, row) = ((x + (step * frame)) % w, (y + (step * frame / 2)) % h);
            return new Px(30 + (200 * column / Math.Max(1, w - 1)), 220 - (180 * row / Math.Max(1, h - 1)), 60 + (150 * (column + row) / Math.Max(1, w + h - 2)));
        }));

    /// <summary>Large flat areas with a few markers: cheap to code and to store, and every quadrant differs.</summary>
    private static Img Blocks(int w, int h, bool alpha)
    {
        var pixels = CommonCorpus.Grid(w, h, (x, y) =>
        {
            var (cx, cy) = (x * 4 / w, y * 4 / h);
            var marker = (x % 32 == 5 && y % 16 == 3) || (x == w - 2 && y == h - 3);
            var color = marker ? new Px(255, 255, 0) : new Px(40 + (50 * cx), 220 - (45 * cy), 30 + (25 * ((cx + (2 * cy)) % 8)));
            return alpha ? color.Append(cx == cy ? 0 : 255 - (40 * ((cx + cy) % 4))) : color;
        });
        return new Img(w, h, alpha ? "rgba" : "rgb", 8, pixels);
    }

    /// <summary>
    /// The planes x265 is given for a pattern, in an FFmpeg planar pixel format (<c>gray</c>, <c>gbrp</c>, <c>yuv420p</c>,
    /// <c>yuv422p</c>, <c>yuv444p</c>, with <c>10le</c> or <c>12le</c>): the samples of a gray or of an identity-matrix
    /// format are those of the pattern; YCbCr planes are computed here with the BT.601 equations, the chroma of a block
    /// being its mean. The conversion only shapes the content: the reference of a fixture is what the decoders output.
    /// </summary>
    /// <param name="fullRange">Whether the gray and YCbCr samples use the full range. A stream that says nothing of its
    /// range is given limited-range samples: that is how it is read (ITU-T H.265, clause E.3.1).</param>
    private static byte[] Planar(Img img, string pixelFormat, bool fullRange = true)
    {
        var bits = pixelFormat.EndsWith("10le", StringComparison.Ordinal) ? 10 : pixelFormat.EndsWith("12le", StringComparison.Ordinal) ? 12 : 8;
        var name = bits == 8 ? pixelFormat : pixelFormat[..^4];
        var maximum = (1 << bits) - 1;
        var sourceMaximum = img.Depth == 8 ? 255 : 65535;
        int Scale(int value) => (int)((((long)value * maximum * 2) + sourceMaximum) / (2 * sourceMaximum));
        var (w, h) = (img.Width, img.Height);
        var planes = new List<int[]>();
        if (name == "gray")
        {
            // Limited range: 16 to 235, scaled to the bit depth
            int Range(int sample) => fullRange ? sample : (16 << (bits - 8)) + (int)((((long)sample * (219 << (bits - 8)) * 2) + maximum) / (2 * maximum));
            planes.Add([.. img.Pixels.Select(p => Range(Scale(img.Model.StartsWith("gray", StringComparison.Ordinal) ? p[0] : ((299 * p[0]) + (587 * p[1]) + (114 * p[2]) + 500) / 1000)))]);
        }
        else if (name == "gbrp")
        {
            planes.Add([.. img.Pixels.Select(p => Scale(p[1]))]);
            planes.Add([.. img.Pixels.Select(p => Scale(p[2]))]);
            planes.Add([.. img.Pixels.Select(p => Scale(p[0]))]);
        }
        else
        {
            var (subX, subY) = name switch { "yuv420p" => (2, 2), "yuv422p" => (2, 1), "yuv444p" => (1, 1), _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat)) };
            var luma = new double[w * h];
            var cb = new double[w * h];
            var cr = new double[w * h];
            for (var i = 0; i < w * h; i++)
            {
                var p = img.Pixels[i];
                var (r, g, b) = ((double)p[0] / sourceMaximum, (double)p[1] / sourceMaximum, (double)p[2] / sourceMaximum);
                luma[i] = (0.299 * r) + (0.587 * g) + (0.114 * b);
                cb[i] = (b - luma[i]) / 1.772;
                cr[i] = (r - luma[i]) / 1.402;
            }

            var unit = 1 << (bits - 8);
            int Luma(double value) => Math.Clamp((int)Math.Round(fullRange ? value * maximum : (16 + (219 * value)) * unit, MidpointRounding.AwayFromZero), 0, maximum);
            int Chroma(double value) => Math.Clamp((int)Math.Round(fullRange ? (value * maximum) + (1 << (bits - 1)) : (128 + (224 * value)) * unit, MidpointRounding.AwayFromZero), 0, maximum);
            planes.Add([.. luma.Select(Luma)]);
            foreach (var plane in new[] { cb, cr })
            {
                var samples = new List<int>();
                for (var y = 0; y < h; y += subY)
                {
                    for (var x = 0; x < w; x += subX)
                    {
                        var sum = 0.0;
                        var count = 0;
                        for (var dy = 0; dy < subY && y + dy < h; dy++)
                        {
                            for (var dx = 0; dx < subX && x + dx < w; dx++)
                            {
                                sum += plane[((y + dy) * w) + x + dx];
                                count++;
                            }
                        }

                        samples.Add(Chroma(sum / count));
                    }
                }

                planes.Add([.. samples]);
            }
        }

        var builder = new ByteBuilder();
        foreach (var sample in planes.SelectMany(plane => plane))
        {
            if (bits == 8)
                builder.U8(sample);
            else
                builder.U8(sample & 0xFF).U8(sample >> 8);
        }

        return builder.ToArray();
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Encoders
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Runs heif-enc. A token that starts with <c>@</c> names an input file; the output follows <c>-o</c>.</summary>
    private static (byte[] Data, string Command) HeifEnc(ContainerContext context, string name, IReadOnlyDictionary<string, byte[]> files, string arguments, string extension = ".heic")
    {
        var scratch = context.NewScratch("enc-" + name);
        foreach (var (file, content) in files)
            File.WriteAllBytes(scratch / file, content);

        var output = scratch / (name + extension);
        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> command = [context.Tools.HeifEnc, .. tokens.Select(token => token[0] == '@' ? (scratch / token[1..]).Value : token), "-o", output.Value];
        var result = Proc.Exec(command);
        if (result.ExitCode != 0 || !File.Exists(output))
            throw new FatalException($"heic/{name}: heif-enc failed: {Encoding.UTF8.GetString(result.Stderr).Trim()} {Encoding.UTF8.GetString(result.Stdout).Replace('\r', '\n').Trim().Split('\n')[^1]}");

        return (File.ReadAllBytes(output), CommonCorpus.Display(command, context.Tools, [(scratch.Value + Path.DirectorySeparatorChar, ""), (name + extension, "{output}" + extension)]));
    }

    private static List<string> EncoderTools(ContainerContext context) => [context.Tools.HeifEncLabel, context.Tools.Hevc.DecodersLabel, ToolSet.RuntimeLabel];

    private static List<string> X265Tools(ContainerContext context) => [context.Tools.Hevc.X265Label + " (payloads)", context.Tools.Hevc.DecodersLabel, ToolSet.RuntimeLabel];

    /// <summary>A still image written by heif-enc from one PNG source.</summary>
    /// <param name="expected">The pattern the decoded image must equal (lossless content), or <see langword="null"/>.</param>
    private static HeicReference Still(ContainerContext context, string name, Img source, string arguments, string[] requires, Img? expected = null, string? notes = null, ExpectedExclusion? excluded = null)
    {
        var (data, command) = HeifEnc(context, name, new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["source.png"] = Png(source) }, arguments + " @source.png");
        var reference = AddValidHeic(context, name, data, "generated", "BuildStills", EncoderTools(context), [command], null, notes, requires, excluded: excluded);
        if (expected is not null)
            AssertDecodesTo(name, reference, expected);

        return reference;
    }

    /// <summary>Lossless content: the reference derived from the file must be the source pattern, sample for sample.</summary>
    private static void AssertDecodesTo(string name, HeicReference reference, Img expected, int frame = 0)
    {
        var layout = expected.Depth == 8 ? "rgba8" : "rgba16le";
        var (decoded, source) = (RgbaRaw(reference.Frames[frame], layout), RgbaRaw(expected, layout));
        if (!decoded.AsSpan().SequenceEqual(source))
        {
            var first = decoded.AsSpan().CommonPrefixLength(source);
            var bytesPerPixel = expected.Depth == 8 ? 4 : 8;
            var pixel = first / bytesPerPixel;
            throw new FatalException(Inv($"heic/{name}: the lossless content does not decode to its source pattern (first difference at ({pixel % expected.Width}, {pixel / expected.Width}): decoded {Convert.ToHexString(decoded.AsSpan(pixel * bytesPerPixel, bytesPerPixel))}, source {Convert.ToHexString(source.AsSpan(pixel * bytesPerPixel, bytesPerPixel))})."));
        }
    }

    /// <summary>What x265 writes for a lossless image smaller than 64x64 (heif-enc) or 32x32, which HM does not read.</summary>
    private static readonly ExpectedExclusion SmallLossless = new("hm", "HM 18.0 rejects a 16x16 coding tree block from level 5 (the limit of clause A.4.1); x265 codes a small picture with 16x16 coding tree blocks (libheif asks for them, and a picture smaller than 32x32 has no other size) and signals level 8.5 for a lossless stream.");

    private const string Bt709Limited = "--matrix_coefficients 1 --colour_primaries 1 --transfer_characteristic 13 --full_range_flag 0";

    private const string Bt2020Pq = "--matrix_coefficients 9 --colour_primaries 9 --transfer_characteristic 16";

    private static void BuildStills(ContainerContext context)
    {
        // Lossless: identity matrix (the planes are G, B and R), monochrome, and their alpha planes. The decoded samples
        // must equal the source pattern. Every image is smaller than 64x64: a 64x64 picture and a clap property
        var detail = CommonCorpus.PatternDetailColor(12, 9, 1);
        Still(context, "lossless-rgb8", detail, "-L", ["heic.lossless", "heic.matrix=0", "heic.bitDepth=8", "heic.subsampling=4:4:4", "heic.clap", "heic.majorBrand=heix"], detail, excluded: SmallLossless);
        var deep10 = DeepColor(8, 8, 10);
        Still(context, "lossless-rgb10", deep10, "-L -b 10", ["heic.lossless", "heic.matrix=0", "heic.bitDepth=10"], deep10, excluded: SmallLossless);
        var deep12 = DeepColor(7, 6, 12);
        Still(context, "lossless-rgb12", deep12, "-L -b 12", ["heic.lossless", "heic.matrix=0", "heic.bitDepth=12"], deep12, excluded: SmallLossless);
        var gray = Gray(9, 7);
        Still(context, "lossless-gray8", gray, "-L", ["heic.lossless", "heic.subsampling=4:0:0", "heic.bitDepth=8"], gray, excluded: SmallLossless);
        var gray10 = DeepGray(7, 9, 10);
        Still(context, "lossless-gray10", gray10, "-L -b 10", ["heic.lossless", "heic.subsampling=4:0:0", "heic.bitDepth=10"], gray10, excluded: SmallLossless);
        var gray12 = DeepGray(9, 7, 12);
        Still(context, "lossless-gray12", gray12, "-L -b 12", ["heic.lossless", "heic.subsampling=4:0:0", "heic.bitDepth=12"], gray12, excluded: SmallLossless);
        var rgba = WithAlpha(detail);
        Still(context, "lossless-rgba8", rgba, "-L", ["heic.lossless", "heic.alpha", "heic.alpha.urn=hevc", "heic.alpha.subsampling=4:0:0", "heic.matrix=0"], rgba,
            notes: "The alpha ramp includes 0: the colors under it are kept (hidden colors). The alpha plane is a monochrome picture, an auxiliary image of type urn:mpeg:hevc:2015:auxid:1.", excluded: SmallLossless);
        var grayAlpha = GrayAlpha(8, 6);
        Still(context, "lossless-gray-alpha8", grayAlpha, "-L", ["heic.lossless", "heic.alpha", "heic.subsampling=4:0:0"], grayAlpha, excluded: SmallLossless);
        var rgba10 = WithDeepAlpha(DeepColor(8, 8, 10), 10);
        Still(context, "lossless-rgba10", rgba10, "-L -b 10", ["heic.lossless", "heic.alpha", "heic.bitDepth=10", "heic.matrix=0"], rgba10, excluded: SmallLossless);
        var rgba12 = WithDeepAlpha(DeepColor(7, 6, 12), 12);
        Still(context, "lossless-rgba12", rgba12, "-L -b 12", ["heic.lossless", "heic.alpha", "heic.bitDepth=12", "heic.matrix=0"], rgba12, excluded: SmallLossless);

        // Sizes: one sample, and a picture that is exactly a coding tree block (no clap property)
        var one = CommonCorpus.PatternSmoothColor(1, 1);
        Still(context, "lossless-rgb8-1x1", one, "-L", ["heic.lossless", "heic.clap"], one, excluded: SmallLossless);
        var smooth64 = CommonCorpus.PatternSmoothColor(64, 64);
        Still(context, "lossless-rgb8-64x64", smooth64, "-L", ["heic.lossless", "heic.matrix=0"], smooth64);

        // Lossy 4:4:4: no chroma sample position takes part. The matrices and both ranges, at 8, 10 and 12 bits
        var smooth = CommonCorpus.PatternSmoothColor(8, 8);
        var color10 = DeepColor(8, 8, 10);
        var color12 = DeepColor(8, 6, 12);
        Still(context, "lossy-444-bt601-full", smooth, "-q 70 -p chroma=444", ["heic.matrix=6", "heic.range=full", "heic.subsampling=4:4:4", "heic.colr=nclx"]);
        Still(context, "lossy-444-bt709-limited", smooth, "-q 70 -p chroma=444 " + Bt709Limited, ["heic.matrix=1", "heic.range=limited", "heic.subsampling=4:4:4"]);
        Still(context, "lossy-444-bt2020-full-10bit", color10, "-q 70 -b 10 -p chroma=444 " + Bt2020Pq, ["heic.matrix=9", "heic.range=full", "heic.bitDepth=10", "heic.subsampling=4:4:4"],
            notes: "An HDR color description (BT.2020 primaries, PQ transfer): the samples are returned as stored.");
        Still(context, "lossy-444-bt470bg-limited-12bit", color12, "-q 70 -b 12 -p chroma=444 --matrix_coefficients 5 --colour_primaries 5 --transfer_characteristic 6 --full_range_flag 0", ["heic.matrix=5", "heic.range=limited", "heic.bitDepth=12"]);
        Still(context, "lossy-444-ycgco-full", smooth, "-q 70 -p chroma=444 --matrix_coefficients 8", ["heic.matrix=8", "heic.range=full"]);
        Still(context, "lossy-444-unspecified-limited", smooth, "-q 70 -p chroma=444 --matrix_coefficients 2 --colour_primaries 2 --transfer_characteristic 2 --full_range_flag 0", ["heic.matrix=2", "heic.range=limited", "heic.colr=nclx"],
            notes: "Unspecified matrix coefficients convert as BT.601.");
        Still(context, "lossy-444-alpha", WithAlpha(smooth), "-q 70 -p chroma=444", ["heic.alpha", "heic.matrix=6", "heic.subsampling=4:4:4"]);
        Still(context, "lossy-444-alpha-10bit", WithDeepAlpha(color10, 10), "-q 70 -b 10 -p chroma=444 " + Bt709Limited, ["heic.alpha", "heic.bitDepth=10", "heic.range=limited"],
            notes: "The colors are in limited range and the alpha plane in full range.");

        // Monochrome, lossy: no matrix; the limited range scales the samples
        Still(context, "lossy-gray8", SmoothGray(9, 7), "-q 60", ["heic.subsampling=4:0:0", "heic.bitDepth=8", "heic.range=full"]);
        Still(context, "lossy-gray10", DeepGray(8, 8, 10), "-q 60 -b 10", ["heic.subsampling=4:0:0", "heic.bitDepth=10"]);
        Still(context, "lossy-gray8-limited", SmoothGray(9, 7), "-q 60 --full_range_flag 0", ["heic.subsampling=4:0:0", "heic.bitDepth=8", "heic.range=limited"],
            notes: "The samples are scaled from 16..235 to the full range and clipped (ITU-T H.273).");

        // 4:2:0 whose bitstream says where its chroma samples are: in the middle of four luma samples (type 1), or on
        // the top-left one (type 2)
        const string Center = "-p chroma=420 -p x265:chromaloc=1";
        const string Colocated = "-p chroma=420 -p x265:chromaloc=2";
        Still(context, "lossy-420-center-bt601", smooth, "-q 70 " + Center, ["heic.chromaLoc=1", "heic.matrix=6", "heic.subsampling=4:2:0", "heic.majorBrand=heic"]);
        Still(context, "lossy-420-center-bt601-odd", CommonCorpus.PatternSmoothColor(7, 5), "-q 70 " + Center, ["heic.chromaLoc=1", "heic.subsampling=4:2:0", "heic.clap"]);
        Still(context, "lossy-420-center-bt2020-limited-10bit", color10, "-q 70 -b 10 " + Center + " " + Bt2020Pq + " --full_range_flag 0", ["heic.chromaLoc=1", "heic.matrix=9", "heic.range=limited", "heic.bitDepth=10"]);
        Still(context, "lossy-420-center-bt709-limited-67x65", CommonCorpus.PatternSmoothColor(67, 65), "-q 50 " + Center + " " + Bt709Limited, ["heic.chromaLoc=1", "heic.matrix=1", "heic.range=limited"],
            notes: "Larger than a 64x64 coding tree block in both directions, with odd dimensions.");
        Still(context, "lossy-420-colocated-bt601", smooth, "-q 70 " + Colocated, ["heic.chromaLoc=2", "heic.matrix=6", "heic.subsampling=4:2:0"]);
        Still(context, "lossy-420-colocated-bt2020-10bit", color10, "-q 70 -b 10 " + Colocated + " " + Bt2020Pq, ["heic.chromaLoc=2", "heic.matrix=9", "heic.bitDepth=10"]);

        // 4:2:0 as every phone and heif-enc write it by default: nothing says where the chroma samples are, so they are at
        // the left of the luma samples and between two rows of them (chroma_sample_loc_type 0)
        const string Left = "-p chroma=420";
        Still(context, "lossy-420-bt601", smooth, "-q 70 " + Left, ["heic.chromaLoc=default", "heic.matrix=6", "heic.subsampling=4:2:0", "heic.majorBrand=heic"]);
        Still(context, "lossy-420-bt601-odd", CommonCorpus.PatternSmoothColor(7, 5), "-q 70 " + Left, ["heic.chromaLoc=default", "heic.subsampling=4:2:0"]);
        Still(context, "lossy-420-bt709-limited", smooth, "-q 70 " + Left + " " + Bt709Limited, ["heic.chromaLoc=default", "heic.matrix=1", "heic.range=limited"]);
        Still(context, "lossy-420-bt2020-limited-10bit", color10, "-q 70 -b 10 " + Left + " " + Bt2020Pq + " --full_range_flag 0", ["heic.chromaLoc=default", "heic.matrix=9", "heic.bitDepth=10", "heic.majorBrand=heix"],
            notes: "A 10-bit HDR image as a phone writes it (heix brand, BT.2020 primaries, PQ transfer): the samples are returned as stored.");
        Still(context, "lossy-420-bt601-12bit", color12, "-q 70 -b 12 " + Left, ["heic.chromaLoc=default", "heic.bitDepth=12"]);
        Still(context, "lossy-420-bt601-33x17", CommonCorpus.PatternSmoothColor(33, 17), "-q 60 " + Left, ["heic.chromaLoc=default", "heic.subsampling=4:2:0"]);
        Still(context, "lossy-420-alpha", WithAlpha(smooth), "-q 70 " + Left, ["heic.alpha", "heic.chromaLoc=default", "heic.subsampling=4:2:0"]);

        // 4:2:2: the chroma samples are on the luma samples of even columns
        const string Half = "-p chroma=422";
        Still(context, "lossy-422-bt601", smooth, "-q 70 " + Half, ["heic.subsampling=4:2:2", "heic.matrix=6"]);
        Still(context, "lossy-422-bt601-odd", CommonCorpus.PatternSmoothColor(9, 5), "-q 70 " + Half, ["heic.subsampling=4:2:2"]);
        Still(context, "lossy-422-bt709-limited-10bit", color10, "-q 70 -b 10 " + Half + " " + Bt709Limited, ["heic.subsampling=4:2:2", "heic.bitDepth=10", "heic.range=limited"]);
        Still(context, "lossy-422-bt2020-full-12bit", color12, "-q 70 -b 12 " + Half + " " + Bt2020Pq, ["heic.subsampling=4:2:2", "heic.bitDepth=12", "heic.matrix=9"]);
        Still(context, "lossy-422-alpha", WithAlpha(smooth), "-q 70 " + Half, ["heic.subsampling=4:2:2", "heic.alpha"]);

        // Grids: tiles of 64x64, the smallest ones heif-enc writes without a clap property on each tile
        var blocks = Blocks(128, 128, alpha: false);
        Still(context, "grid-2x2-lossless", blocks, "-L --cut-tiles 64", ["heic.grid=2x2", "heic.lossless"], blocks);
        var cropped = Blocks(130, 70, alpha: false);
        Still(context, "grid-3x2-cropped-lossless", cropped, "-L --cut-tiles 64", ["heic.grid=3x2", "heic.lossless"], cropped,
            notes: "A 130x70 image in three columns and two rows of 64x64 tiles: the tiles of the last column and of the last row exceed the image.");
        Still(context, "grid-2x1-444-10bit", Widen(Blocks(128, 64, alpha: false)), "-q 70 -b 10 -p chroma=444 " + Bt2020Pq + " --cut-tiles 64", ["heic.grid=2x1", "heic.bitDepth=10", "heic.matrix=9"]);
        Still(context, "grid-2x1-420-center", Blocks(128, 64, alpha: false), "-q 80 " + Center + " --cut-tiles 64", ["heic.grid=2x1", "heic.chromaLoc=1", "heic.subsampling=4:2:0"],
            notes: "The planes of the tiles are placed in the grid before the chroma is upsampled: the conversion interpolates across the tile boundary.");
        Still(context, "grid-2x1-420", Blocks(128, 64, alpha: false), "-q 80 " + Left + " --cut-tiles 64", ["heic.grid=2x1", "heic.chromaLoc=default", "heic.subsampling=4:2:0"]);

        // Transformations: reported as the orientation, never applied. heif-enc writes irot and imir properties
        var oriented = CommonCorpus.PatternDetailColor(8, 6, 2);
        foreach (var (name, arguments, requires) in new (string, string, string[])[]
        {
            ("irot-1", "--rotate-cw 270", ["heic.irot=1"]),
            ("irot-3", "--rotate-cw 90", ["heic.irot=3"]),
            ("imir-0", "--flip-v", ["heic.imir=0"]),
            ("imir-1", "--flip-h", ["heic.imir=1"]),
            ("irot-3-imir-0", "--rotate-cw 270 --flip-h", ["heic.irot=3", "heic.imir=0"]),
        })
        {
            Still(context, name, oriented, "-L " + arguments, [.. requires, "heic.clap"], oriented, excluded: SmallLossless);
        }

        // A thumbnail: another coded image, which a reader of the primary item ignores
        Still(context, "thumbnail", smooth64, "-L -t 16", ["heic.thumbnail", "heic.lossless"], smooth64,
            notes: "Written by heif-enc -t 16: a 16x16 thumbnail item, linked to the primary item by a thmb reference, is not read.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Streams coded by x265 and wrapped box by box
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A stream x265 codes from patterns, with its decoded description.</summary>
    private sealed record CodedStream(byte[] Stream, HevcCorpus.Payload Payload, string Command);

    private static CodedStream X265(ContainerContext context, string name, List<Img> frames, string pixelFormat, string parameters, bool fullRange = true)
    {
        var scratch = context.NewScratch("x265-" + name);
        try
        {
            var (stream, command) = context.Tools.Hevc.EncodeX265(scratch, name, [.. frames.SelectMany(frame => Planar(frame, pixelFormat, fullRange))], pixelFormat, frames[0].Width, frames[0].Height, frames.Count, parameters);
            return new CodedStream(stream, context.Tools.Hevc.Decode(scratch, name, stream), command);
        }
        catch (ToolException error)
        {
            throw new FatalException($"heic/{name}: {error.Message}");
        }
    }

    /// <summary>Removes the emulation prevention bytes of a NAL unit (clause 7.4.2): the 03 of each 00 00 03.</summary>
    private static byte[] Unescape(byte[] unit)
    {
        var result = new List<byte>(unit.Length);
        for (var i = 0; i < unit.Length; i++)
        {
            if (i >= 2 && unit[i] == 3 && unit[i - 1] == 0 && unit[i - 2] == 0)
                continue;

            result.Add(unit[i]);
        }

        return [.. result];
    }

    /// <summary>Adds the emulation prevention bytes of a NAL unit: a 03 before a byte of at most 3 that follows two zero bytes.</summary>
    private static byte[] Escape(byte[] unit)
    {
        var result = new List<byte>(unit.Length + 4);
        var zeros = 0;
        foreach (var value in unit)
        {
            if (zeros >= 2 && value <= 3)
            {
                result.Add(3);
                zeros = 0;
            }

            result.Add(value);
            zeros = value == 0 ? zeros + 1 : 0;
        }

        return [.. result];
    }

    /// <summary>
    /// The copy of a parameter set under another identifier: the <c>ue(v)</c> code at a bit position of its payload is
    /// replaced, and the trailing bits are aligned again. The identifier of a picture parameter set is its first syntax
    /// element (clause 7.3.2.3.1); the one of a sequence parameter set follows a byte and profile_tier_level(), 96 bits
    /// when the stream has one sub-layer (clause 7.3.2.2.1).
    /// </summary>
    private static byte[] Renumber(byte[] parameterSet, int identifier)
    {
        var data = Unescape(parameterSet);
        var bits = new List<bool>();
        foreach (var value in data)
        {
            for (var bit = 7; bit >= 0; bit--)
                bits.Add(((value >> bit) & 1) != 0);
        }

        // rbsp_trailing_bits(): a one, then zeros up to the end of the byte
        var stop = bits.LastIndexOf(true);
        bits.RemoveRange(stop, bits.Count - stop);
        var position = 16;
        if (HevcCorpus.NalUnitType(parameterSet) == 33)
        {
            Py.Assert(((data[2] >> 1) & 7) == 0, "one sub-layer");
            position += 8 + 96;
        }
        else
        {
            Py.Assert(HevcCorpus.NalUnitType(parameterSet) == 34, "a sequence or a picture parameter set");
        }

        var zeros = 0;
        while (!bits[position + zeros])
            zeros++;

        bits.RemoveRange(position, (2 * zeros) + 1);
        var code = identifier + 1;
        var length = 32 - int.LeadingZeroCount(code);
        var replacement = Enumerable.Repeat(false, length - 1).Concat(Enumerable.Range(0, length).Select(i => ((code >> (length - 1 - i)) & 1) != 0));
        bits.InsertRange(position, replacement);
        bits.Add(true);
        while (bits.Count % 8 != 0)
            bits.Add(false);

        var result = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
                result[i / 8] |= (byte)(0x80 >> (i % 8));
        }

        return Escape(result);
    }

    /// <summary>How a stream is laid out in a file: the size of the lengths of its NAL units, and where its parameter sets go.</summary>
    /// <param name="InBand">Whether the parameter sets are in the data of the item or of the first sample.</param>
    /// <param name="InRecord">Whether the parameter sets are in the configuration record.</param>
    /// <param name="Complete">The <c>array_completeness</c> of the arrays of the record.</param>
    /// <param name="ExtraSets">Whether the record also holds a copy of the sequence and of the picture parameter sets under an identifier no slice refers to.</param>
    private sealed record StreamLayout(int LengthSize = 4, bool InBand = false, bool InRecord = true, bool Complete = true, bool ExtraSets = false);

    /// <summary>The 12 bytes of the general profile, tier and level of a sequence parameter set, for the configuration record.</summary>
    private static byte[] ProfileTierLevel(byte[] sps) => Unescape(sps)[3..15];

    private static byte[] LengthPrefixed(IEnumerable<byte[]> units, int lengthSize)
    {
        var builder = new ByteBuilder();
        foreach (var unit in units)
        {
            Py.Assert(lengthSize == 4 || unit.Length < 1L << (8 * lengthSize), "a NAL unit does not fit the size of its length");
            for (var i = lengthSize - 1; i >= 0; i--)
                builder.U8((unit.Length >> (8 * i)) & 0xFF);

            builder.Bytes(unit);
        }

        return builder.ToArray();
    }

    /// <summary>The configuration record of a stream and the data of each of its access units.</summary>
    private static (byte[] Record, List<byte[]> Samples) Package(CodedStream coded, StreamLayout layout)
    {
        var units = HevcCorpus.SplitAccessUnits(coded.Stream);
        var sets = units[0].Where(unit => HevcCorpus.IsParameterSet(HevcCorpus.NalUnitType(unit))).ToList();
        var sps = sets.Single(unit => HevcCorpus.NalUnitType(unit) == 33);
        var arrays = new List<(int, byte[][])>();
        if (layout.InRecord)
        {
            foreach (var type in new[] { 32, 33, 34 })
            {
                var ofType = sets.Where(unit => HevcCorpus.NalUnitType(unit) == type).ToList();
                if (layout.ExtraSets && type != 32)
                    ofType.Add(Renumber(ofType[0], 1));

                arrays.Add((type, [.. ofType]));
            }
        }

        var payload = coded.Payload;
        var record = HeifFileBuilder.HevcConfiguration(payload.ChromaFormatIdc, payload.BitDepth, payload.BitDepth, arrays, layout.LengthSize, ProfileTierLevel(sps), layout.Complete);
        var samples = new List<byte[]>();
        for (var i = 0; i < units.Count; i++)
        {
            // The parameter sets of the first access unit are in the record; an encoder repeats the same ones before a
            // later picture, where a sample of an hvc1 track does not hold them
            var repeated = units[i].Where(unit => HevcCorpus.IsParameterSet(HevcCorpus.NalUnitType(unit))).ToList();
            Py.Assert(repeated.All(unit => sets.Any(set => set.AsSpan().SequenceEqual(unit))), "the parameter sets do not change inside the stream");
            samples.Add(LengthPrefixed(units[i].Where(unit => (layout.InBand && i == 0) || !HevcCorpus.IsParameterSet(HevcCorpus.NalUnitType(unit))), layout.LengthSize));
        }

        return (record, samples);
    }

    /// <summary>The brand of an HEVC image: <c>heic</c> for the Main and Main Still Picture profiles (8 bits, 4:2:0),
    /// <c>heix</c> for the range extensions and for 10 bits.</summary>
    private static string ImageBrand(HevcCorpus.Payload payload) => payload is { BitDepth: 8, ChromaFormatIdc: 1 } ? "heic" : "heix";

    /// <summary>A file of one coded image item around a stream of one picture.</summary>
    private static HeifFileBuilder WrapStill(CodedStream coded, StreamLayout? layout = null, byte[]? colr = null)
    {
        var (record, samples) = Package(coded, layout ?? new StreamLayout());
        Py.Assert(samples.Count == 1, "one picture");
        var payload = coded.Payload;
        var builder = new HeifFileBuilder { MajorBrand = ImageBrand(payload), PrimaryItemId = 1 };
        builder.CompatibleBrands.Clear();
        foreach (var brand in new[] { "mif1", ImageBrand(payload), "miaf" })
            builder.CompatibleBrands.Add(brand);

        var item = builder.AddItem(1, "hvc1", samples[0], -builder.AddProperty(record), builder.AddProperty(HeifFileBuilder.ImageSize((uint)payload.Outputs[0].Width, (uint)payload.Outputs[0].Height)));
        if (colr is not null)
            item.Associations.Add((builder.AddProperty(colr), false));

        return builder;
    }

    /// <summary>Adds a coded image item around a stream of one picture to a file, with its own properties.</summary>
    private static HeifFileBuilder.Item AddCodedItem(HeifFileBuilder builder, uint id, CodedStream coded, bool hidden)
    {
        var (record, samples) = Package(coded, new StreamLayout());
        var item = builder.AddItem(id, "hvc1", samples[0], -builder.AddProperty(record), builder.AddProperty(HeifFileBuilder.ImageSize((uint)coded.Payload.Outputs[0].Width, (uint)coded.Payload.Outputs[0].Height)));
        item.IsHidden = hidden;
        return item;
    }

    /// <summary>Makes a coded image the alpha plane of the primary item.</summary>
    private static void AddAlpha(HeifFileBuilder builder, CodedStream alpha, string auxiliaryType)
    {
        var item = AddCodedItem(builder, 2, alpha, hidden: true);
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.AuxiliaryType(auxiliaryType)), true));
        builder.References.Add(("auxl", 2, [1]));
    }

    /// <summary>A valid file assembled around streams that x265 coded for it.</summary>
    /// <param name="second">The stream of the alpha plane, when the file has one.</param>
    /// <param name="payloadsOf">The valid fixture that holds the same stream in a layout heif-dec reads, or <see langword="null"/>.</param>
    private static HeicReference Wrapped(ContainerContext context, string name, CodedStream coded, HeifFileBuilder builder, string[] requires, string notes, CodedStream? second = null, ExpectedExclusion? excluded = null, string function = "BuildWrapped", string? payloadsOf = null)
    {
        List<string> commands = second is null ? [coded.Command] : [coded.Command, second.Command];
        return AddValidHeic(context, name, builder.Build(), "hand-authored", function, X265Tools(context), commands, payloadsOf is null ? null : new Obj { ["payloadsOf"] = "heic/" + payloadsOf }, notes, requires, payloadsOf, excluded);
    }

    private static void BuildWrapped(ContainerContext context)
    {
        // Pictures whose size is not a multiple of 8, which x265 codes as a 24x24 picture with a conformance window: the
        // image is the cropped picture, and the ispe property declares its size. (x265 does not code a picture smaller
        // than its smallest coding tree block, 16x16.)
        var smooth = CommonCorpus.PatternSmoothColor(16, 16);
        var windowed = CommonCorpus.PatternSmoothColor(18, 18);
        var detail = CommonCorpus.PatternDetailColor(20, 17, 3);
        var rgb = X265(context, "window-rgb8", [detail], "gbrp", "keyint=1:lossless=1:range=full");
        var reference = Wrapped(context, "window-rgb8-lossless", rgb, WrapStill(rgb), ["heic.conformanceWindow", "heic.lossless", "heic.matrix=0", "heic.colr=none", "heic.videoSignal=colour"],
            "A 20x17 image coded by x265 as a 24x24 picture with a conformance window; no colr box: the video signal description of the bitstream says the planes are G, B and R.", excluded: SmallLossless);
        AssertDecodesTo("window-rgb8-lossless", reference, detail);

        var deep = DeepColor(18, 17, 10);
        var rgb10 = X265(context, "window-rgb10", [deep], "gbrp10le", "keyint=1:lossless=1:range=full");
        reference = Wrapped(context, "window-rgb10-lossless", rgb10, WrapStill(rgb10), ["heic.conformanceWindow", "heic.lossless", "heic.bitDepth=10", "heic.matrix=0"],
            "An 18x17 10-bit image coded by x265 as a 24x24 picture with a conformance window.", excluded: SmallLossless);
        AssertDecodesTo("window-rgb10-lossless", reference, deep);

        var gray = Gray(19, 17);
        var mono = X265(context, "window-gray8", [gray], "gray", "keyint=1:lossless=1", fullRange: false);
        Wrapped(context, "window-gray8-lossless", mono, WrapStill(mono), ["heic.conformanceWindow", "heic.lossless", "heic.subsampling=4:0:0", "heic.videoSignal=none", "heic.colr=none", "heic.range=limited"],
            "A monochrome 19x17 image with a conformance window; the bitstream has no video signal description and the file no colr box: the samples use the limited range (16 to 235), which they are scaled from. libheif returns monochrome samples as stored, whatever their range.", excluded: SmallLossless);

        // A conformance window that removes more than the padding of the coded picture: a 32x32 picture of x265 whose
        // sequence parameter set is rewritten to crop it to 20x18. The samples outside the window are decoded all the same
        var whole = X265(context, "window-beyond-padding", [CommonCorpus.PatternSmoothColor(32, 32)], "yuv444p", "keyint=1:qp=20:range=full:colorprim=bt709:transfer=iec61966-2-1:colormatrix=bt470bg");
        var (croppedStream, cropCommand) = context.Tools.Hevc.Filter(context.NewScratch("crop-window-beyond-padding"), "window-beyond-padding", whole.Stream, "hevc_metadata=crop_right=12:crop_bottom=14");
        var croppedPicture = new CodedStream(croppedStream, context.Tools.Hevc.Decode(context.NewScratch("decode-window-beyond-padding"), "window-beyond-padding", croppedStream), whole.Command + " && " + cropCommand);
        Wrapped(context, "window-beyond-padding-444", croppedPicture, WrapStill(croppedPicture), ["heic.conformanceWindow", "heic.checkedSize=32x32", "heic.subsampling=4:4:4"],
            "A 20x18 image that is the top-left corner of a 32x32 picture: its conformance window (rewritten by the hevc_metadata filter of FFmpeg) removes 12 columns and 14 rows, more than the padding of a coded picture to a whole 8x8 coding block. The limits of the canvas then apply to the 32x32 picture that is decoded.");

        // The rule holds in each direction: a 32x18 source, which x265 codes as a 32x24 picture, cropped to 20 columns. The
        // window removes the padding of the rows (6) and more than the padding of the columns (12)
        var columns = X265(context, "window-beyond-padding-columns", [CommonCorpus.PatternSmoothColor(32, 18)], "yuv444p", "keyint=1:qp=20:range=full:colorprim=bt709:transfer=iec61966-2-1:colormatrix=bt470bg");
        var (columnsStream, columnsCommand) = context.Tools.Hevc.Filter(context.NewScratch("crop-window-beyond-padding-columns"), "window-beyond-padding-columns", columns.Stream, "hevc_metadata=crop_right=12:crop_bottom=6");
        var columnsPicture = new CodedStream(columnsStream, context.Tools.Hevc.Decode(context.NewScratch("decode-window-beyond-padding-columns"), "window-beyond-padding-columns", columnsStream), columns.Command + " && " + columnsCommand);
        Wrapped(context, "window-beyond-padding-columns-444", columnsPicture, WrapStill(columnsPicture), ["heic.conformanceWindow", "heic.checkedSize=32x18", "heic.subsampling=4:4:4"],
            "A 20x18 image in a 32x24 picture: its conformance window removes 6 rows, the padding of the picture to whole 8x8 coding blocks, and 12 columns, more than the padding. The limits of the canvas apply to the coded width (32) and to the height of the image (18).");

        // No video signal description and no colr box: limited range (video_full_range_flag is inferred to be 0: ITU-T
        // H.265, clause E.3.1), unspecified matrix coefficients (converted as BT.601)
        var plain = X265(context, "no-signal-444", [smooth], "yuv444p", "keyint=1:qp=20", fullRange: false);
        Wrapped(context, "no-colr-no-signal-444", plain, WrapStill(plain), ["heic.colr=none", "heic.videoSignal=none", "heic.matrix=2", "heic.range=limited", "heic.subsampling=4:4:4"],
            "Neither a colr box nor a video signal description in the bitstream: there is no color description at all, the samples use the limited range (ITU-T H.265, clause E.3.1) and convert as BT.601.");

        // The video signal description of the bitstream, without colr box: limited range, BT.709
        var limited = X265(context, "vui-bt709-limited", [smooth], "yuv444p", "keyint=1:qp=20:range=limited:colorprim=bt709:transfer=bt709:colormatrix=bt709", fullRange: false);
        Wrapped(context, "no-colr-bitstream-bt709-limited", limited, WrapStill(limited), ["heic.colr=none", "heic.videoSignal=colour", "heic.matrix=1", "heic.range=limited"],
            "No colr box: the video signal description of the sequence parameter set (BT.709, limited range) applies.");
        Wrapped(context, "nclx-overrides-bitstream", limited, WrapStill(limited, colr: HeifFileBuilder.ColorNclx(1, 13, 6, fullRange: true)), ["heic.colr=nclx", "heic.matrix=6", "heic.range=full"],
            "The bitstream signals BT.709 limited range and the colr box BT.601 full range: the colr box describes the colors.");

        // A video signal description that only gives the range
        var rangeOnly = X265(context, "vui-range-limited", [smooth], "yuv444p", "keyint=1:qp=20:range=limited", fullRange: false);
        Wrapped(context, "no-colr-bitstream-limited-range", rangeOnly, WrapStill(rangeOnly), ["heic.colr=none", "heic.videoSignal=range", "heic.matrix=2", "heic.range=limited"],
            "No colr box, and a video signal description that gives the range (limited) without color description: unspecified matrix coefficients, converted as BT.601.");

        // Every chroma sample location of a frame, signalled by the bitstream of an 18x18 4:2:0 picture
        foreach (var type in new[] { 0, 1, 2 })
        {
            var located = X265(context, Inv($"chroma-loc-{type}"), [windowed], "yuv420p", Inv($"keyint=1:qp=20:chromaloc={type}"), fullRange: false);
            Wrapped(context, Inv($"chroma-loc-{type}-420"), located, WrapStill(located), [Inv($"heic.chromaLoc={type}"), "heic.subsampling=4:2:0", "heic.conformanceWindow", "heic.majorBrand=heic"],
                type switch
                {
                    0 => "chroma_sample_loc_type 0, signalled: the chroma samples are at the left of their luma samples and between two rows of them.",
                    1 => "chroma_sample_loc_type 1: the chroma samples are in the middle of four luma samples.",
                    _ => "chroma_sample_loc_type 2: the chroma samples are on the top-left luma sample.",
                });
        }

        var left10 = X265(context, "left-420-10bit", [DeepColor(20, 18, 10)], "yuv420p10le", "keyint=1:qp=20:range=limited:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc", fullRange: false);
        Wrapped(context, "window-420-bt2020-limited-10bit", left10, WrapStill(left10), ["heic.chromaLoc=default", "heic.bitDepth=10", "heic.matrix=9", "heic.range=limited", "heic.conformanceWindow", "heic.majorBrand=heix"],
            "A 20x18 10-bit 4:2:0 image with a conformance window, described by its bitstream only (BT.2020, PQ, limited range).");
        var half = X265(context, "window-422", [CommonCorpus.PatternSmoothColor(20, 17)], "yuv422p", "keyint=1:qp=20", fullRange: false);
        Wrapped(context, "window-422", half, WrapStill(half), ["heic.subsampling=4:2:2", "heic.conformanceWindow"],
            "A 20x17 4:2:2 image with a conformance window.");

        // Alpha planes heif-enc does not write: a 4:2:0 picture whose luma plane is the alpha plane, the auxiliary type of
        // MPEG-B, and a limited-range alpha plane
        var color = X265(context, "alpha-color", [smooth], "yuv444p", "keyint=1:qp=20", fullRange: false);
        var alphaRamp = new Img(16, 16, "gray", 8, CommonCorpus.Grid(16, 16, (x, y) => new Px(((x + (2 * y)) % 5) switch { 0 => 0, 4 => 255, var step => 255 * step / 4 })));
        var alpha420 = X265(context, "alpha-420", [new Img(16, 16, "rgb", 8, alphaRamp.Pixels.Select(p => new Px(p[0], p[0], p[0])))], "yuv420p", "keyint=1:qp=4");
        var alphaBuilder = WrapStill(color);
        AddAlpha(alphaBuilder, alpha420, ReferenceHeif.HevcAlphaAuxiliaryType);
        Wrapped(context, "alpha-420-picture", color, alphaBuilder, ["heic.alpha", "heic.alpha.subsampling=4:2:0", "heic.alpha.urn=hevc"],
            "The alpha auxiliary image is a 4:2:0 picture: its luma plane is the alpha plane and its chroma planes are ignored. Neither bitstream has a video signal description: the colors use the limited range and the alpha plane the full range.", alpha420);
        var alphaMono = X265(context, "alpha-400", [alphaRamp], "gray", "keyint=1:qp=4");
        alphaBuilder = WrapStill(color);
        AddAlpha(alphaBuilder, alphaMono, ReferenceHeif.AlphaAuxiliaryType);
        Wrapped(context, "alpha-mpegb-urn", color, alphaBuilder, ["heic.alpha", "heic.alpha.urn=mpegB", "heic.alpha.subsampling=4:0:0"],
            "The alpha auxiliary image has the type of MPEG-B (urn:mpeg:mpegB:cicp:systems:auxiliary:alpha), which HEIF also allows for HEVC.", alphaMono);
        var alphaLimited = X265(context, "alpha-limited", [new Img(16, 16, "gray", 8, alphaRamp.Pixels.Select(p => new Px(16 + (219 * p[0] / 255))))], "gray", "keyint=1:qp=4:range=limited");
        alphaBuilder = WrapStill(color);
        AddAlpha(alphaBuilder, alphaLimited, ReferenceHeif.HevcAlphaAuxiliaryType);
        Wrapped(context, "alpha-limited-range", color, alphaBuilder, ["heic.alpha", "heic.alpha.range=limited"],
            "The bitstream of the alpha plane signals the limited range: its samples are scaled from 16..235 to the full range.", alphaLimited);

        // Configuration records and data layouts no encoder of the corpus writes
        Wrapped(context, "hvcc-length-size-1", plain, WrapStill(plain, new StreamLayout(LengthSize: 1)), ["heic.hvcc.lengthSize=1"],
            "The NAL units of the item are preceded by a length of one byte (lengthSizeMinusOne = 0).", payloadsOf: "no-colr-no-signal-444");
        Wrapped(context, "hvcc-length-size-2", plain, WrapStill(plain, new StreamLayout(LengthSize: 2)), ["heic.hvcc.lengthSize=2"],
            "The NAL units of the item are preceded by a length of two bytes (lengthSizeMinusOne = 1).", payloadsOf: "no-colr-no-signal-444");
        Wrapped(context, "hvcc-several-parameter-sets", plain, WrapStill(plain, new StreamLayout(ExtraSets: true)), ["heic.hvcc.severalParameterSets"],
            "The configuration record holds two sequence parameter sets and two picture parameter sets: the ones of the picture, and a copy of each under identifier 1, which no slice refers to.");
        Wrapped(context, "hvcc-incomplete-arrays", plain, WrapStill(plain, new StreamLayout(InBand: true, Complete: false)), ["heic.hvcc.incompleteArrays", "heic.parameterSets=in-band"],
            "The arrays of the configuration record are not complete (array_completeness = 0) and the parameter sets are repeated in the data of the item.");
        Wrapped(context, "parameter-sets-in-band", plain, WrapStill(plain, new StreamLayout(InBand: true, InRecord: false, Complete: false)), ["heic.parameterSets=in-band"],
            "The configuration record has no array: the parameter sets are in the data of the item only, before the slice.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Assembled layouts
    // -----------------------------------------------------------------------------------------------------------------

    private static string PropertyType(byte[] box) => Bytes.Latin1(box.AsSpan(4, 4));

    /// <summary>The index in the associations of an item of its first property of a type, or -1.</summary>
    private static int FindAssociation(HeifFileBuilder builder, HeifFileBuilder.Item item, string type)
    {
        for (var i = 0; i < item.Associations.Count; i++)
        {
            if (PropertyType(builder.Properties[item.Associations[i].PropertyIndex - 1]) == type)
                return i;
        }

        return -1;
    }

    private static void ReplaceProperty(HeifFileBuilder builder, HeifFileBuilder.Item item, string type, byte[] box)
    {
        var index = FindAssociation(builder, item, type);
        Py.Assert(index >= 0, type);
        item.Associations[index] = (builder.AddProperty(box), item.Associations[index].IsEssential);
    }

    private static void RemoveProperty(HeifFileBuilder builder, HeifFileBuilder.Item item, string type)
    {
        var index = FindAssociation(builder, item, type);
        Py.Assert(index >= 0, type);
        item.Associations.RemoveAt(index);
    }

    /// <summary>A valid file assembled from the description of a valid fixture.</summary>
    private static HeicReference Assembled(ContainerContext context, string name, string source, Action<HeifFileBuilder> change, string[] requires, string notes, string function = "BuildLayouts")
    {
        var builder = HeifFileBuilder.From(context.Inputs[source]);
        change(builder);
        return AddValidHeic(context, name, builder.Build(), "hand-authored", function, [context.PayloadTools[source], context.Tools.Hevc.DecodersLabel, ToolSet.RuntimeLabel], null, new Obj { ["payloadsOf"] = "heic/" + source }, notes, requires, source, context.Exclusions[source]);
    }

    private static void MediaFirst(HeifFileBuilder builder)
    {
        var boxes = builder.Layout.Where(box => box != HeifFileBuilder.MediaDataBox && box != HeifFileBuilder.FileTypeBox).ToList();
        builder.Layout.Clear();
        builder.Layout.Add(HeifFileBuilder.FileTypeBox);
        builder.Layout.Add(HeifFileBuilder.MediaDataBox);
        foreach (var box in boxes)
            builder.Layout.Add(box);
    }

    private static void SetBrands(HeifFileBuilder builder, string major, params string[] compatible)
    {
        builder.MajorBrand = major;
        builder.CompatibleBrands.Clear();
        foreach (var brand in compatible)
            builder.CompatibleBrands.Add(brand);
    }

    private static readonly byte[] XmpPacket = Bytes.Utf8("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>");

    private static byte[] ExifPayload => CommonCorpus.ExifTiff(littleEndian: true, orientation: 6, software: "corpus generator");

    private static void BuildLayouts(ContainerContext context)
    {
        const string Source = "lossless-rgb8";
        Assembled(context, "layout-mdat-first", Source, MediaFirst, ["heic.layout=media-first"], "The mdat box precedes the meta box: a forward-only reader keeps the media data until the items are known.");
        Assembled(context, "layout-idat", Source, builder =>
        {
            builder.GetItem(builder.PrimaryItemId!.Value).Storage = HeifFileBuilder.ItemStorage.ItemData;
            builder.Layout.Remove(HeifFileBuilder.MediaDataBox);
        }, ["heic.idat"], "The data of the primary item is in the idat box of the meta box (construction method 1); the file has no mdat box.");
        Assembled(context, "layout-extents", Source, builder =>
        {
            var item = builder.GetItem(builder.PrimaryItemId!.Value);
            foreach (var length in new[] { 7, 1, item.Data.Length - 8 })
                item.ExtentLengths.Add(length);
        }, ["heic.extents=3"], "The data of the primary item is in three extents that are not contiguous (7 bytes, 1 byte, the rest): the first one ends inside the first NAL unit.");
        Assembled(context, "layout-free-unknown-trailing", Source, builder =>
        {
            builder.ExtraBoxes["free1"] = HeifFileBuilder.Box("free", new byte[5]);
            builder.ExtraBoxes["unknown"] = HeifFileBuilder.Box("zzzz", Bytes.Ascii("not an image"));
            builder.ExtraBoxes["free2"] = HeifFileBuilder.Box("free", new byte[11]);
            builder.Layout.Clear();
            foreach (var box in new[] { HeifFileBuilder.FileTypeBox, "free1", HeifFileBuilder.MetaBox, "unknown", HeifFileBuilder.MediaDataBox, "free2" })
                builder.Layout.Add(box);
        }, ["heic.boxes=free", "heic.boxes=unknown", "heic.trailingData"], "A free box before the meta box, an unknown box before the mdat box and a free box after it, which a forward-only reader never reads.");
        Assembled(context, "layout-mif1-large-size-to-end", Source, builder =>
        {
            SetBrands(builder, "mif1", "mif1", "heix", "miaf");
            builder.LargeSizeBoxes.Add(HeifFileBuilder.MetaBox);
            builder.MediaDataExtendsToEnd = true;
        }, ["heic.majorBrand=mif1", "heic.boxes=large-size", "heic.boxes=to-end"], "The major brand is mif1 and heix is a compatible brand; the meta box has a 64-bit size; the size field of the mdat box is 0 (it extends to the end of the file).");
        Assembled(context, "brand-miaf-major", Source, builder => SetBrands(builder, "miaf", "miaf", "mif1", "heix"),
            ["heic.majorBrand=miaf"], "The major brand is miaf, which names no codec: the compatible brand heix makes the file an HEIC image.");
        Assembled(context, "brands-avif-and-heic", Source, builder => SetBrands(builder, "mif1", "mif1", "avif", "heix", "miaf"),
            ["heic.majorBrand=mif1", "heic.brands=avif"], "The file declares the brands of AVIF and of HEIC: its format is the one of the codec of its primary item, an hvc1 item.");

        // Color descriptions of an image heif-enc wrote: it puts the same description in the colr box and in the bitstream
        Assembled(context, "no-colr-identity", Source, builder => RemoveProperty(builder, builder.GetItem(builder.PrimaryItemId!.Value), "colr"),
            ["heic.colr=none", "heic.matrix=0", "heic.range=full"], "No colr box: the sequence parameter set says sRGB primaries and transfer with the identity matrix, so the planes are G, B and R.");
        Assembled(context, "no-colr-identity-mdat-first", Source, builder =>
        {
            RemoveProperty(builder, builder.GetItem(builder.PrimaryItemId!.Value), "colr");
            MediaFirst(builder);
        }, ["heic.colr=none", "heic.matrix=0", "heic.layout=media-first"], "No colr box and the mdat box before the meta box: the configuration record of the item still holds the sequence parameter set, so a header identification tells the color model.");
        Assembled(context, "no-colr-bitstream-bt709", "lossy-444-bt709-limited", builder => RemoveProperty(builder, builder.GetItem(builder.PrimaryItemId!.Value), "colr"),
            ["heic.colr=none", "heic.matrix=1", "heic.range=limited"], "No colr box: the video signal description that heif-enc writes in the bitstream (BT.709, limited range) applies.");

        // Metadata: the Exif orientation (6) is not the orientation of the image, which its irot property gives
        var icc = CommonCorpus.IccProfile("RGB ");
        void AddMetadata(HeifFileBuilder builder, bool keepNclx)
        {
            var primary = builder.GetItem(builder.PrimaryItemId!.Value);
            if (!keepNclx)
                RemoveProperty(builder, primary, "colr");

            primary.Associations.Add((builder.AddProperty(HeifFileBuilder.ColorProfile(icc)), false));
            primary.Associations.Add((builder.AddProperty(HeifFileBuilder.Rotation(2)), true));
            var exifItem = builder.AddItem(100, "Exif", HeifFileBuilder.ExifItemData(ExifPayload));
            var xmpItem = builder.AddItem(101, "mime", XmpPacket);
            xmpItem.ContentType = "application/rdf+xml";
            builder.References.Add(("cdsc", 100, [primary.Id]));
            builder.References.Add(("cdsc", 101, [primary.Id]));
        }

        Assembled(context, "metadata-exif-xmp-icc", Source, builder => AddMetadata(builder, keepNclx: true), ["heic.metadata=exif", "heic.metadata=xmp", "heic.colr=icc", "heic.colr=nclx", "heic.irot=2"],
            "Exif and XMP items, an ICC profile beside the nclx description, and a half turn after the clap property. The Exif orientation tag is 6: the orientation is 3 (the format takes it from irot and imir only).");
        Assembled(context, "icc-without-nclx", Source, builder => AddMetadata(builder, keepNclx: false), ["heic.colr=icc", "heic.matrix=0"],
            "A colr box of type prof and none of type nclx: the profile is returned and the bitstream gives the matrix coefficients (identity).");

        // A grid whose image is smaller than its tiles: the last column and the last row are cropped
        Assembled(context, "grid-2x2-cropped", "grid-2x2-lossless", builder =>
        {
            var grid = builder.GetItem(builder.PrimaryItemId!.Value);
            grid.Data = HeifFileBuilder.GridDescriptor(2, 2, 100, 90);
            ReplaceProperty(builder, grid, "ispe", HeifFileBuilder.ImageSize(100, 90));
        }, ["heic.grid=2x2"], "The grid of heic/grid-2x2-lossless declared as 100x90: the tiles of the last column and of the last row exceed the image.");

        // Clean apertures on pictures that have a conformance window: the aperture applies to the cropped picture
        var smooth = CommonCorpus.PatternSmoothColor(20, 18);
        var apertured = X265(context, "clap-444", [smooth], "yuv444p", "keyint=1:qp=20", fullRange: false);
        var clapBuilder = WrapStill(apertured);
        clapBuilder.GetItem(1).Associations.Add((clapBuilder.AddProperty(HeifFileBuilder.CleanAperture(6, 1, 4, 1, 1, 1, 2, 1)), true));
        clapBuilder.GetItem(1).Associations.Add((clapBuilder.AddProperty(HeifFileBuilder.Rotation(1)), true));
        Wrapped(context, "clap-irot", apertured, clapBuilder, ["heic.clap", "heic.irot=1", "heic.conformanceWindow"],
            "A 6x4 clean aperture that is not centered in a 20x18 picture, which x265 codes as 24x24 with a conformance window, then a quarter turn: the aperture applies to the 20x18 picture, and the rotation is reported, not applied.", function: "BuildLayouts");
        var apertured420 = X265(context, "clap-420", [smooth], "yuv420p", "keyint=1:qp=20:chromaloc=1", fullRange: false);
        clapBuilder = WrapStill(apertured420);
        clapBuilder.GetItem(1).Associations.Add((clapBuilder.AddProperty(HeifFileBuilder.CleanAperture(5, 1, 5, 1, 3, 2, -3, 2)), true));
        Wrapped(context, "clap-420-odd", apertured420, clapBuilder, ["heic.clap", "heic.subsampling=4:2:0", "heic.chromaLoc=1"],
            "A 5x5 clean aperture at an odd position of a 4:2:0 picture: the crop applies to the converted image (the chroma of the neighbors is used).", function: "BuildLayouts");

        // A grid with an alpha plane that is a grid too, which heif-enc does not write (it leaves the alpha plane of a
        // tiled image out): two 16x16 color tiles and two 16x16 monochrome tiles, coded one by one
        var wide = CommonCorpus.PatternSmoothColor(32, 16);
        var gridBuilder = new HeifFileBuilder { PrimaryItemId = 1 };
        SetBrands(gridBuilder, "heix", "mif1", "heix", "miaf");
        var nclx = gridBuilder.AddProperty(HeifFileBuilder.ColorNclx(1, 13, 6, fullRange: true));
        gridBuilder.AddItem(1, "grid", HeifFileBuilder.GridDescriptor(1, 2, 32, 16), gridBuilder.AddProperty(HeifFileBuilder.ImageSize(32, 16)), nclx);
        var alphaGrid = gridBuilder.AddItem(2, "grid", HeifFileBuilder.GridDescriptor(1, 2, 32, 16), gridBuilder.AddProperty(HeifFileBuilder.ImageSize(32, 16)), -gridBuilder.AddProperty(HeifFileBuilder.AuxiliaryType(ReferenceHeif.HevcAlphaAuxiliaryType)));
        alphaGrid.IsHidden = true;
        var gridStreams = new List<CodedStream>();
        for (var i = 0; i < 2; i++)
        {
            var colorTile = X265(context, Inv($"grid-alpha-color{i}"), [new Img(16, 16, "rgb", 8, CommonCorpus.Grid(16, 16, (x, y) => wide.Pixels[(y * 32) + (16 * i) + x]))], "yuv444p", "keyint=1:qp=20");
            var alphaTile = X265(context, Inv($"grid-alpha-alpha{i}"), [new Img(16, 16, "gray", 8, CommonCorpus.Grid(16, 16, (x, y) => new Px(((x + (16 * i) + (2 * y)) % 9) switch { 0 => 0, 8 => 255, var step => 255 * step / 8 })))], "gray", "keyint=1:qp=4");
            AddCodedItem(gridBuilder, (uint)(10 + i), colorTile, hidden: true).Associations.Add((nclx, false));
            AddCodedItem(gridBuilder, (uint)(20 + i), alphaTile, hidden: true);
            gridStreams.Add(colorTile);
            gridStreams.Add(alphaTile);
        }

        gridBuilder.References.Add(("dimg", 1, [10, 11]));
        gridBuilder.References.Add(("dimg", 2, [20, 21]));
        gridBuilder.References.Add(("auxl", 2, [1]));
        AddValidHeic(context, "grid-2x1-alpha", gridBuilder.Build(), "hand-authored", "BuildLayouts", X265Tools(context), [.. gridStreams.Select(stream => stream.Command)], new Obj { ["tileWidth"] = 16, ["tileHeight"] = 16, ["columns"] = 2, ["rows"] = 1 },
            "A grid of two 16x16 tiles whose alpha plane is a grid of two monochrome tiles.", ["heic.grid=2x1", "heic.alpha", "heic.alpha.subsampling=4:0:0"]);

        BuildPhoneLayout(context);
    }

    /// <summary>
    /// The layout of the files a phone writes: a grid of equal tiles, a rotation, an Exif item, an ICC profile, a
    /// thumbnail and an auxiliary image that is not an alpha plane (a depth map), all hidden but the grid. The tiles are
    /// coded one by one by x265 with the settings of one picture.
    /// </summary>
    private static void BuildPhoneLayout(ContainerContext context)
    {
        foreach (var (name, pixelFormat, parameters, requires, notes) in new (string, string, string, string[], string)[]
        {
            ("phone-grid-420-center", "yuv420p", "chromaloc=1", ["heic.chromaLoc=1", "heic.majorBrand=heic"], "8-bit 4:2:0 tiles whose bitstream signals centered chroma samples"),
            ("phone-grid-420", "yuv420p", "", ["heic.chromaLoc=default", "heic.majorBrand=heic"], "8-bit 4:2:0 tiles, as a phone writes them"),
            ("phone-grid-420-10bit", "yuv420p10le", "", ["heic.chromaLoc=default", "heic.bitDepth=10", "heic.majorBrand=heix"], "10-bit 4:2:0 tiles under the heix brand, as a phone writes its HDR pictures"),
        })
        {
            // A 64x24 image in 2x2 tiles of 32x16: the last row of tiles exceeds the image by 8 rows
            var deep = pixelFormat.EndsWith("10le", StringComparison.Ordinal);
            var picture = CommonCorpus.PatternSmoothColor(64, 32);
            if (deep)
                picture = Widen(picture);

            var x265 = "keyint=1:qp=22:ctu=16:range=full:colorprim=smpte432:transfer=iec61966-2-1:colormatrix=bt470bg" + (parameters.Length > 0 ? ":" + parameters : "");
            var tiles = new List<CodedStream>();
            for (var i = 0; i < 4; i++)
            {
                var (left, top) = (i % 2 * 32, i / 2 * 16);
                tiles.Add(X265(context, Inv($"{name}-tile{i}"), [new Img(32, 16, "rgb", picture.Depth, CommonCorpus.Grid(32, 16, (x, y) => picture.Pixels[((top + y) * 64) + left + x]))], pixelFormat, x265));
            }

            var small = new Img(16, 16, "rgb", picture.Depth, CommonCorpus.Grid(16, 16, (x, y) => picture.Pixels[(y * 2 * 64) + (x * 4)]));
            var thumbnail = X265(context, name + "-thumbnail", [small], pixelFormat, x265);
            var depth = X265(context, name + "-depth", [SmoothGray(16, 16)], "gray", "keyint=1:qp=22");

            var brand = ImageBrand(tiles[0].Payload);
            var builder = new HeifFileBuilder { PrimaryItemId = 1 };
            SetBrands(builder, brand, "mif1", brand, "miaf", "MiHB");
            var colr = builder.AddProperty(HeifFileBuilder.ColorProfile(File.ReadAllBytes(CorpusDriver.CorpusDir / "icc" / "profiles" / "sRGB-v2-nano.icc")));
            var grid = builder.AddItem(1, "grid", HeifFileBuilder.GridDescriptor(2, 2, 64, 24), builder.AddProperty(HeifFileBuilder.ImageSize(64, 24)), colr, -builder.AddProperty(HeifFileBuilder.Rotation(3)));
            grid.Storage = HeifFileBuilder.ItemStorage.ItemData;
            var ids = new List<uint>();
            for (var i = 0; i < tiles.Count; i++)
            {
                var tile = AddCodedItem(builder, (uint)(10 + i), tiles[i], hidden: true);
                tile.Associations.Add((colr, false));
                ids.Add(tile.Id);
            }

            builder.References.Add(("dimg", 1, [.. ids]));
            var thumbnailItem = AddCodedItem(builder, 20, thumbnail, hidden: true);
            thumbnailItem.Associations.Add((colr, false));
            builder.References.Add(("thmb", 20, [1]));
            var depthItem = AddCodedItem(builder, 21, depth, hidden: true);
            depthItem.Associations.Add((builder.AddProperty(HeifFileBuilder.AuxiliaryType("urn:mpeg:hevc:2015:auxid:2")), true));
            builder.References.Add(("auxl", 21, [1]));
            builder.AddItem(30, "Exif", HeifFileBuilder.ExifItemData(ExifPayload));
            builder.References.Add(("cdsc", 30, [1]));
            AddValidHeic(context, name, builder.Build(), "hand-authored", "BuildLayouts", X265Tools(context), [.. tiles.Select(tile => tile.Command), thumbnail.Command, depth.Command],
                new Obj { ["tileWidth"] = 32, ["tileHeight"] = 16, ["columns"] = 2, ["rows"] = 2, ["iccProfile"] = "icc/profiles/sRGB-v2-nano.icc" },
                "The layout of a phone: a 64x24 grid of 2x2 tiles of 32x16 (the last row exceeds the image), a rotation, an ICC profile, an Exif item, and a hidden 16x16 thumbnail and a hidden depth map (an auxiliary image of type urn:mpeg:hevc:2015:auxid:2), neither of which is read: " + notes + ".",
                [.. requires, "heic.grid=2x2", "heic.thumbnail", "heic.auxiliary=other", "heic.colr=icc", "heic.irot=3", "heic.metadata=exif", "heic.idat"]);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Image sequences
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A sequence written by heif-enc from numbered PNG files.</summary>
    private static HeicReference Sequence(ContainerContext context, string name, List<Img> frames, string arguments, string[] requires, string? notes = null, bool lossless = false)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var i = 0; i < frames.Count; i++)
            files[Inv($"frame-{i:D2}.png")] = Png(frames[i]);

        var (data, command) = HeifEnc(context, name, files, arguments + " -S @frame-00.png", ".heics");
        var reference = AddValidHeic(context, name, data, "generated", "BuildSequences", EncoderTools(context), [command], new Obj { ["frames"] = frames.Count }, notes, ["heic.sequence", .. requires]);
        if (lossless)
        {
            for (var i = 0; i < frames.Count; i++)
                AssertDecodesTo(name, reference, frames[i], i);
        }

        return reference;
    }

    /// <summary>
    /// A sequence assembled around a stream of several pictures: one sample per access unit, in decoding order, and the
    /// composition offsets that present the images in the output order of the bitstream. The first image is presented at
    /// the decoding time of the sample that is decoded when it is output, which the edit list names as its start.
    /// </summary>
    /// <param name="durations">The duration of each image, in presentation order.</param>
    /// <param name="plays">The number of plays: 0 for an edit list that repeats forever, -1 for no edit list.</param>
    private static HeifFileBuilder WrapSequence(CodedStream coded, uint timescale, uint[] durations, int plays, string sampleEntry = "hvc1", StreamLayout? layout = null)
    {
        var (record, samples) = Package(coded, layout ?? new StreamLayout());
        var payload = coded.Payload;
        Py.Assert(samples.Count == payload.Outputs.Count && samples.Count == durations.Length, "one image per sample");
        var builder = new HeifFileBuilder { MovieTimescale = timescale };
        var brand = payload is { BitDepth: 8, ChromaFormatIdc: 1 } ? "hevc" : "hevx";
        SetBrands(builder, brand, brand, "msf1", "iso8");
        builder.Layout.Clear();
        foreach (var box in new[] { HeifFileBuilder.FileTypeBox, HeifFileBuilder.MovieBox, HeifFileBuilder.MediaDataBox })
            builder.Layout.Add(box);

        // Presentation times in output order; the decoding time of a sample is the sum of the durations of the samples
        // before it, whose durations are those of the images taken in decoding order of their presentation slots
        var presentation = new long[samples.Count];
        long time = 0;
        for (var i = 0; i < payload.Outputs.Count; i++)
        {
            presentation[payload.Outputs[i].DecodeIndex] = time;
            time += durations[i];
        }

        var total = time;
        var sampleDurations = durations.ToArray();
        var decoding = new long[samples.Count];
        for (var i = 1; i < samples.Count; i++)
            decoding[i] = decoding[i - 1] + sampleDurations[i - 1];

        var delay = Enumerable.Range(0, samples.Count).Max(i => decoding[i] - presentation[i]);
        var track = new HeifFileBuilder.Track(1, "pict", payload.Outputs[0].Width, payload.Outputs[0].Height) { SampleEntryType = sampleEntry, Timescale = timescale, Duration = (ulong)(plays > 0 ? total * plays : total) };
        track.SampleEntryBoxes.Add(record);
        for (var i = 0; i < samples.Count; i++)
            track.Samples.Add((samples[i], sampleDurations[i]));

        if (payload.IsReordered)
            track.CompositionOffsets = [.. Enumerable.Range(0, samples.Count).Select(i => checked((int)(presentation[i] + delay - decoding[i])))];

        if (plays >= 0)
        {
            track.EditList = new HeifFileBuilder.EditList { IsRepeating = plays != 1 };
            track.EditList.Entries.Add(((ulong)total, payload.IsReordered ? delay : 0, 1, 0));
            if (plays == 0)
                track.Duration = ulong.MaxValue;
        }

        builder.Tracks.Add(track);
        return builder;
    }

    /// <summary>Adds the alpha track of the color track of a sequence: an auxiliary video track of the same samples
    /// count, durations and edit list, whose pictures are the alpha planes.</summary>
    private static void AddAlphaTrack(HeifFileBuilder builder, CodedStream alpha)
    {
        var color = builder.Tracks[0];
        var (record, samples) = Package(alpha, new StreamLayout());
        Py.Assert(samples.Count == color.Samples.Count && !alpha.Payload.IsReordered, "one alpha plane per image, in the order of the images");
        var track = new HeifFileBuilder.Track(2, "auxv", color.Width, color.Height) { SampleEntryType = "hvc1", Timescale = color.Timescale, Duration = color.Duration, EditList = color.EditList };
        track.SampleEntryBoxes.Add(record);
        track.SampleEntryBoxes.Add(HeifFileBuilder.FullBox("auxi", 0, 0, Bytes.Utf8(ReferenceHeif.HevcAlphaAuxiliaryType), [0]));
        track.References.Add(("auxl", [color.Id]));
        for (var i = 0; i < samples.Count; i++)
            track.Samples.Add((samples[i], color.Samples[i].Duration));

        builder.Tracks.Add(track);
    }

    private static void BuildSequences(ContainerContext context)
    {
        // Written by heif-enc: pictures of 64x64 (see the traps). An intra-only sequence, played three times
        var frames = Enumerable.Range(0, 3).Select(i => Drifting(64, 64, i, 5)).ToList();
        Sequence(context, "sequence-lossless-3-plays", frames, "-L --timebase 10 --duration 2 --gop-structure intra-only --repetitions 3", ["heic.lossless", "heic.seq.editList=repeat", "heic.seq.sampleEntry=hvc1"],
            "Three lossless images of 2/10 s, each one an intra picture; three plays.", lossless: true);
        Sequence(context, "sequence-low-delay-lossless", [.. Enumerable.Range(0, 4).Select(i => Drifting(72, 64, i, 3))], "-L --timebase 1000 --duration 40 --gop-structure low-delay --repetitions infinite",
            ["heic.seq.interFrames", "heic.lossless", "heic.matrix=0", "heic.seq.editList=repeat-indefinite"], "Four lossless images of 40 ms, predicted from the images before them (P pictures); repeated forever.", lossless: true);
        Sequence(context, "sequence-reordered-444", [.. Enumerable.Range(0, 5).Select(i => Drifting(64, 64, i, 3))], "-q 70 -p chroma=444 --timebase 600 --duration 25 --gop-structure unrestricted",
            ["heic.seq.interFrames", "heic.seq.reordered", "heic.seq.compositionOffsets", "heic.seq.editList=none"], "Five images of 25/600 s with B pictures: the samples are in decoding order, and their composition offsets give the order of the images. heif-enc writes no edit list for one play: the sequence repeats forever.");
        Sequence(context, "sequence-alpha-lossless", [.. Enumerable.Range(0, 2).Select(i => WithAlpha(Drifting(64, 64, i, 7)))], "-L --timebase 10 --duration 1 --gop-structure intra-only",
            ["heic.alpha", "heic.lossless"], "Two images with an alpha plane: an auxiliary track of monochrome pictures.");
        Sequence(context, "sequence-444-10bit", [.. Enumerable.Range(0, 3).Select(i => Widen(Drifting(64, 64, i, 4)))], "-q 70 -b 10 -p chroma=444 " + Bt2020Pq + " --timebase 30000 --duration 1001 --gop-structure low-delay",
            ["heic.bitDepth=10", "heic.matrix=9"], "Ten bits, 1001/30000 s per image.");
        Sequence(context, "sequence-gray", [.. Enumerable.Range(0, 3).Select(i => new Img(64, 64, "gray", 8, Drifting(64, 64, i, 4).Pixels.Select(p => new Px(p[1]))))], "-q 60 --timebase 8 --duration 1 --gop-structure low-delay",
            ["heic.subsampling=4:0:0", "heic.seq.interFrames"], "A monochrome sequence: gray samples, compared exactly.");
        Sequence(context, "sequence-420-center", [.. Enumerable.Range(0, 3).Select(i => Drifting(64, 64, i, 6))], "-q 70 -p chroma=420 -p x265:chromaloc=1 --timebase 12 --duration 1 --gop-structure low-delay",
            ["heic.chromaLoc=1", "heic.subsampling=4:2:0", "heic.seq.interFrames"]);
        Sequence(context, "sequence-420", [.. Enumerable.Range(0, 3).Select(i => Drifting(64, 64, i, 6))], "-q 70 -p chroma=420 --timebase 12 --duration 1 --gop-structure unrestricted",
            ["heic.chromaLoc=default", "heic.subsampling=4:2:0", "heic.seq.interFrames"], "4:2:0 as heif-enc writes it by default.");
        Sequence(context, "sequence-422", [.. Enumerable.Range(0, 3).Select(i => Drifting(64, 64, i, 6))], "-q 70 -p chroma=422 --timebase 12 --duration 1 --gop-structure low-delay",
            ["heic.subsampling=4:2:2", "heic.seq.interFrames"]);

        // Assembled around x265 streams of 24x24 pictures (20x18 images, with a conformance window; identity matrix, so
        // that no tolerance applies: heif-dec does not decode the last image of the stream, and measures nothing): eight images whose
        // B pictures are decoded after the pictures they are shown before, three durations, and a finite number of plays
        var moving = Enumerable.Range(0, 8).Select(i => Drifting(20, 18, i)).ToList();
        var reordered = X265(context, "sequence-b-frames", moving, "gbrp", "keyint=8:min-keyint=8:bframes=2:b-adapt=0:ref=2:qp=14:range=full:ctu=16:scenecut=0:rc-lookahead=4");
        uint[] durations = [2, 3, 1, 2, 3, 1, 2, 3];
        Wrapped(context, "sequence-window-reordered-2-plays", reordered, WrapSequence(reordered, 10, durations, plays: 2),
            ["heic.sequence", "heic.seq.reordered", "heic.seq.compositionOffsets", "heic.seq.editMediaTime", "heic.seq.editList=repeat", "heic.conformanceWindow", "heic.seq.interFrames"],
            "Eight 20x18 identity-matrix images in 24x24 pictures with a conformance window, with B pictures: the composition offsets reorder the samples, the images last 2, 3 and 1 tenths of a second in turn, and the edit list starts at the presentation time of the first image; two plays.", function: "BuildSequences");
        Wrapped(context, "sequence-hev1-in-band", reordered, WrapSequence(reordered, 10, durations, plays: 0, sampleEntry: "hev1", layout: new StreamLayout(InBand: true, InRecord: false, Complete: false)),
            ["heic.sequence", "heic.seq.sampleEntry=hev1", "heic.parameterSets=in-band", "heic.seq.editList=repeat-indefinite"],
            "An hev1 track: the configuration record has no parameter set, which are in the first sample, before its slice.", function: "BuildSequences");
        Wrapped(context, "sequence-no-edit-list-reordered", reordered, WrapSequence(reordered, 10, durations, plays: -1),
            ["heic.sequence", "heic.seq.editList=none", "heic.seq.reordered"],
            "No edit list: the number of repetitions is not known, the sequence repeats forever, and the presentation starts at the first image although its composition time is not zero.", function: "BuildSequences");
        Wrapped(context, "sequence-length-size-2", reordered, WrapSequence(reordered, 10, durations, plays: 1, layout: new StreamLayout(LengthSize: 2)),
            ["heic.sequence", "heic.hvcc.lengthSize=2", "heic.seq.editList=once"],
            "The NAL units of the samples are preceded by a length of two bytes; played once.", function: "BuildSequences");
        var left = X265(context, "sequence-420-left", moving.Take(4).ToList(), "yuv420p", "keyint=8:bframes=0:qp=22:ctu=16:scenecut=0", fullRange: false);
        Wrapped(context, "sequence-window-420", left, WrapSequence(left, 25, [1, 1, 1, 1], plays: 0),
            ["heic.sequence", "heic.chromaLoc=default", "heic.conformanceWindow", "heic.majorBrand=hevc"],
            "Four 20x18 4:2:0 images in 24x24 pictures with a conformance window, under the hevc brand.", function: "BuildSequences");

        // Four identity-matrix images, each predicted from the one before it: the sequence the next layouts are assembled
        // around (heif-dec reads it, which it does not for the sequence with B pictures above)
        var plainFrames = moving.Take(4).ToList();
        var plainStream = X265(context, "sequence-rgb", plainFrames, "gbrp", "keyint=8:bframes=0:qp=12:range=full:scenecut=0");
        Wrapped(context, "sequence-window-rgb", plainStream, WrapSequence(plainStream, 30, [1, 2, 1, 4], plays: 0),
            ["heic.sequence", "heic.matrix=0", "heic.seq.interFrames", "heic.conformanceWindow", "heic.seq.editList=repeat-indefinite", "heic.majorBrand=hevx"],
            "Four 20x18 identity-matrix images in 24x24 pictures with a conformance window, P pictures after the first one; durations of 1, 2, 1 and 4 thirtieths of a second, repeated forever.", function: "BuildSequences");

        // A clean aperture in the sample entry, which crops every image of the track: an odd size at the top-left corner of
        // 4:2:0 pictures (what an encoder writes for an image of odd size), and an aperture at an offset, on a color track
        // and on its alpha track
        var oddBuilder = WrapSequence(left, 25, [1, 1, 1, 1], plays: 0);
        oddBuilder.Tracks[0].SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(19, 1, 17, 1, -1, 2, -1, 2));
        Wrapped(context, "sequence-clap-odd-420", left, oddBuilder, ["heic.sequence", "heic.clap", "heic.conformanceWindow", "heic.subsampling=4:2:0"],
            "Four 19x17 images: the top-left 19x17 samples of 20x18 4:2:0 pictures, which a clap box of the sample entry selects (the pictures are 24x24 before their conformance window).", function: "BuildSequences");
        var alphaFrames = Enumerable.Range(0, 4).Select(i => new Img(20, 18, "gray", 8, CommonCorpus.Grid(20, 18, (x, y) => new Px(((x + (2 * y) + i) % 9) switch { 0 => 0, 8 => 255, var step => 255 * step / 8 })))).ToList();
        var alphaStream = X265(context, "sequence-clap-alpha", alphaFrames, "gray", "keyint=8:bframes=0:qp=4:scenecut=0");
        var offsetBuilder = WrapSequence(plainStream, 30, [1, 2, 1, 4], plays: 0);
        AddAlphaTrack(offsetBuilder, alphaStream);
        foreach (var track in offsetBuilder.Tracks)
            track.SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(12, 1, 10, 1, 1, 1, 0, 1));

        Wrapped(context, "sequence-clap-offset-alpha", plainStream, offsetBuilder, ["heic.sequence", "heic.clap", "heic.alpha", "heic.conformanceWindow", "heic.matrix=0"],
            "Four 12x10 images with an alpha plane: the samples from (5, 4) of 20x18 pictures, which the same clap box selects in the sample entry of the color track and of its alpha track.", alphaStream, function: "BuildSequences");

        // Sample tables, layouts and brands heif-enc does not write
        const string Plain = "sequence-window-rgb";
        Assembled(context, "sequence-mdat-first", Plain, MediaFirst, ["heic.layout=media-first"], "The mdat box precedes the moov box.", "BuildSequences");
        Assembled(context, "sequence-co64-stz2", Plain, builder =>
        {
            foreach (var track in builder.Tracks)
            {
                track.ChunkOffsetBox = "co64";
                track.SampleSizeBox = "stz2";
                track.SamplesPerChunk = 1;
                track.HeaderVersion = 0;
            }
        }, ["heic.seq.chunkOffsets=co64", "heic.seq.sampleSizes=stz2"], "64-bit chunk offsets (co64), compact 16-bit sample sizes (stz2), one sample per chunk and version 0 track and media headers.", "BuildSequences");
        Assembled(context, "sequence-chunks-reversed", "sequence-reordered-444", builder =>
        {
            foreach (var track in builder.Tracks)
                track.SamplesPerChunk = 1;

            builder.ChunksInReverseOrder = true;
        }, ["heic.seq.samplesOutOfOrder", "heic.seq.reordered"], "One sample per chunk, the chunks written in the mdat box from the last one to the first one: the first sample is the last one in the file.", "BuildSequences");
        Assembled(context, "sequence-msf1-major", Plain, builder => SetBrands(builder, "msf1", "msf1", "hevx", "iso8"),
            ["heic.majorBrand=msf1"], "The major brand is msf1, which names no codec: the compatible brand hevx makes the file an HEIC image sequence.", "BuildSequences");
        Assembled(context, "sequence-brands-avis-and-hevc", Plain, builder => SetBrands(builder, "msf1", "msf1", "avis", "hevx", "iso8"),
            ["heic.brands=avif", "heic.majorBrand=msf1"], "The file declares the brands of the AVIF and of the HEIC image sequences: its format is the one of the codec of its track.", "BuildSequences");

        // Exif and XMP of a sequence: the meta box of the file before the moov box, or the meta box of the track
        void AddFileMetadata(HeifFileBuilder builder, HeifFileBuilder.ItemStorage storage)
        {
            var exifItem = builder.AddItem(100, "Exif", HeifFileBuilder.ExifItemData(ExifPayload));
            var xmpItem = builder.AddItem(101, "mime", XmpPacket);
            xmpItem.ContentType = "application/rdf+xml";
            exifItem.Storage = xmpItem.Storage = storage;
            if (!builder.Layout.Contains(HeifFileBuilder.MetaBox))
                builder.Layout.Insert(1, HeifFileBuilder.MetaBox);
        }

        void AddTrackMetadata(HeifFileBuilder builder, HeifFileBuilder.ItemStorage storage)
        {
            var track = builder.Tracks.First(candidate => candidate.HandlerType == "pict");
            track.MetaItems.Add(new HeifFileBuilder.Item(1, "Exif", HeifFileBuilder.ExifItemData(ExifPayload)) { Storage = storage });
            track.MetaItems.Add(new HeifFileBuilder.Item(2, "mime", XmpPacket) { ContentType = "application/rdf+xml", Storage = storage });
        }

        Assembled(context, "sequence-file-meta-after-samples", Plain, builder =>
        {
            AddFileMetadata(builder, HeifFileBuilder.ItemStorage.MediaData);
            builder.ItemDataFollowsSamples = true;
        }, ["heic.metadata=exif", "heic.metadata=xmp", "heic.seq.metadataSource=file", "heic.seq.metadataAfterSamples"], "The data of the Exif and XMP items of the meta box of the file follow the samples in the mdat box: the last image is complete once they are read.", "BuildSequences");
        Assembled(context, "sequence-track-meta-idat", Plain, builder => AddTrackMetadata(builder, HeifFileBuilder.ItemStorage.ItemData),
            ["heic.metadata=exif", "heic.metadata=xmp", "heic.seq.metadataSource=track", "heic.idat"], "Exif and XMP items in the meta box of the track, their data in the idat box of that meta box.", "BuildSequences");
    }
}
