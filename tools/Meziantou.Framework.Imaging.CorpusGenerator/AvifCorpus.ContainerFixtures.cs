using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Heif;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The valid AVIF fixtures: files written by avifenc (BuildStills, BuildSequences) and files assembled around their
// payloads for the layouts avifenc does not write (BuildLayouts). Each recipe lists the feature tags it exists for
// (requires): they are read back from the bytes, so a recipe whose encoder stops writing the feature fails the generation.
//
// Traps (each one cost a fixture during the selection):
// - libavif keeps the temporal delimiter in the item data and in the samples; a second one makes FFmpeg's obu demuxer
//   emit an empty packet, which libaom rejects;
// - avifenc takes the creation and modification times of a sequence from the clock unless both are given;
// - avifenc needs tiles of 64 samples or more for a grid, and rejects matrix coefficients 10, 13 and 14, and YCgCo (8) in
//   limited range;
// - avifenc crashes (exit code 139, no file) when several input frames are combined with --exif, --irot, --imir or
//   --crop; --xmp and --icc work for a sequence. The sequences with Exif data are assembled (HeifFileBuilder);
// - avifenc always writes a full-range alpha plane: a limited-range one cannot be obtained from it;
// - avifenc repeats the irot, imir and clap properties of a grid on its tiles, where the clap box is the one of the
//   grid (its own strict check then reports an invalid crop for the tiles);
// - a valid fixture costs test time in proportion to its pixels (every generic suite decodes, resizes and convolves it):
//   the patterns are 8x8 unless the feature needs more;
// - avifdec applies clap, irot and imir to what it writes: its output is compared with the oriented reference.
internal static partial class AvifCorpus
{
    private const string StandardEncoder = "-j 1 -s 6";

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

    /// <summary>Colors under an alpha ramp that includes 0 (hidden colors) and 255.</summary>
    private static Img WithAlpha(Img rgb, int maximum = 255) =>
        new(rgb.Width, rgb.Height, "rgba", rgb.Depth, rgb.Pixels.Select((p, i) => p.Append((((i % rgb.Width) + (2 * (i / rgb.Width))) % 5) switch { 0 => 0, 4 => maximum, var step => maximum * step / 4 })));

    /// <summary>Deep colors under an alpha ramp of the same depth that includes 0 (hidden colors) and the maximum.</summary>
    private static Img WithDeepAlpha(Img rgb, int bits) =>
        new(rgb.Width, rgb.Height, "rgba", 16, rgb.Pixels.Select((p, i) => p.Append((((i % rgb.Width) + (2 * (i / rgb.Width))) % 5) switch { 0 => 0, 4 => 65535, var step => Deep((((1L << bits) - 1) * step / 4) + 1, bits) })));

    private static Img DeepGrayAlpha(int w, int h, int bits) =>
        new(w, h, "graya", 16, CommonCorpus.Grid(w, h, (x, y) => new Px(
            Deep((((1L << bits) - 1) * (x + (2 * y)) / Math.Max(1, w - 1 + (2 * (h - 1)))) + (5 * x), bits),
            (x + y) % 4 == 0 ? 0 : Deep(((1L << bits) - 1) - (((1L << bits) / 7) * ((x + (2 * y)) % 6)), bits))));

    /// <summary>An 8-bit pattern as 16-bit samples (each sample times 257): the source of a lossy 10 or 12-bit encode.</summary>
    private static Img Widen(Img img) => new(img.Width, img.Height, img.Model, 16, img.Pixels.Select(p => p.Select(v => v * 257)));

    /// <summary>A frame of a longer sequence: gradients that drift by one column per frame and one row every other frame.</summary>
    private static Img Drifting(int w, int h, int frame) =>
        new(w, h, "rgb", 8, CommonCorpus.Grid(w, h, (x, y) =>
        {
            var (column, row) = ((x + frame) % w, (y + (frame / 2)) % h);
            return new Px(30 + (200 * column / Math.Max(1, w - 1)), 220 - (180 * row / Math.Max(1, h - 1)), 60 + (150 * (column + row) / Math.Max(1, w + h - 2)));
        }));

    /// <summary>A gray frame of a sequence.</summary>
    private static Img MovingGray(int w, int h, int frame) =>
        new(w, h, "gray", 8, CommonCorpus.Grid(w, h, (x, y) => new Px(((23 * ((x + frame) % w)) + (31 * y) + (40 * frame) + 7) % 256)));

    private static Img GrayAlpha(int w, int h) =>
        new(w, h, "graya", 8, CommonCorpus.Grid(w, h, (x, y) => new Px(((29 * x) + (13 * y) + 3) % 256, (x + y) % 4 == 0 ? 0 : 255 - (30 * ((x + (2 * y)) % 6)))));

    /// <summary>A frame of a sequence: the pattern moves with the frame index.</summary>
    private static Img Moving(int w, int h, int frame) =>
        new(w, h, "rgb", 8, CommonCorpus.Grid(w, h, (x, y) => new Px((40 + (25 * ((x + frame) % w))) % 256, (200 - (20 * y) + (30 * frame)) % 256, (60 + (9 * x * (frame + 1)) + (17 * y)) % 256)));

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

    /// <summary>A YUV4MPEG2 frame of full-range 4:2:0 samples: avifenc keeps the planes and the chroma sample position.</summary>
    private static byte[] Y4m(int w, int h, string chroma)
    {
        var builder = new ByteBuilder().Ascii(Inv($"YUV4MPEG2 W{w} H{h} F1:1 Ip A1:1 C{chroma} XCOLORRANGE=FULL\nFRAME\n"));
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
                builder.U8(30 + ((190 * (x + (2 * y))) / (w - 1 + (2 * (h - 1)))));
        }

        // Chroma that changes fast in both directions: the upsampling positions give different results
        foreach (var plane in new[] { 0, 1 })
        {
            for (var y = 0; y < (h + 1) / 2; y++)
            {
                for (var x = 0; x < (w + 1) / 2; x++)
                    builder.U8(plane == 0 ? 60 + (45 * ((x + (2 * y)) % 4)) : 210 - (50 * ((2 * x + y) % 4)));
            }
        }

        return builder.ToArray();
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Encoders
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Runs avifenc. A token that starts with <c>@</c> names an input file; the output is the last argument.</summary>
    private static (byte[] Data, string Command) Avifenc(ContainerContext context, string name, IReadOnlyDictionary<string, byte[]> files, string arguments)
    {
        try
        {
            return RunAvifenc(context.Tools, context.NewScratch("enc-" + name), name, files, arguments);
        }
        catch (ToolException error)
        {
            throw new FatalException($"avif/{name}: {error.Message}");
        }
    }

    private static List<string> EncoderTools(ContainerContext context) => [context.Tools.AvifencLabel, context.Tools.DecodersLabel, ToolSet.RuntimeLabel];

    private static List<string> AssemblyTools(ContainerContext context) => [context.Tools.AvifencLabel + " (payloads)", context.Tools.DecodersLabel, ToolSet.RuntimeLabel];

    /// <summary>A still image written by avifenc from one source file.</summary>
    /// <param name="source">The source: a PNG file, unless <paramref name="sourceName"/> has another extension.</param>
    /// <param name="expected">The pattern the decoded image must equal (lossless content), or <see langword="null"/>.</param>
    private static AvifReference Still(ContainerContext context, string name, byte[] source, string arguments, string[] requires, Img? expected = null, string sourceName = "source.png",
        IReadOnlyDictionary<string, byte[]>? files = null, string? notes = null, Obj? parameters = null)
    {
        var inputs = new Dictionary<string, byte[]>(files ?? new Dictionary<string, byte[]>(StringComparer.Ordinal), StringComparer.Ordinal) { [sourceName] = source };
        var (data, command) = Avifenc(context, name, inputs, arguments + " @" + sourceName);
        var reference = AddValidAvif(context, name, data, "generated", "BuildStills", EncoderTools(context), [command], parameters, notes, requires);
        if (expected is not null)
            AssertDecodesTo(name, reference, expected);

        return reference;
    }

    /// <summary>Lossless content: the reference derived from the file must be the source pattern, sample for sample.</summary>
    private static void AssertDecodesTo(string name, AvifReference reference, Img expected)
    {
        var layout = expected.Depth == 8 ? "rgba8" : "rgba16le";
        var (decoded, source16) = (RgbaRaw(reference.Frames[0], layout), RgbaRaw(expected, layout));
        if (!decoded.AsSpan().SequenceEqual(source16))
        {
            var first = decoded.AsSpan().CommonPrefixLength(source16);
            var bytesPerPixel = expected.Depth == 8 ? 4 : 8;
            var pixel = first / bytesPerPixel;
            throw new FatalException(Inv($"avif/{name}: the lossless content does not decode to its source pattern (first difference at ({pixel % expected.Width}, {pixel / expected.Width}): decoded {Convert.ToHexString(decoded.AsSpan(pixel * bytesPerPixel, bytesPerPixel))}, source {Convert.ToHexString(source16.AsSpan(pixel * bytesPerPixel, bytesPerPixel))})."));
        }
    }

    private static void BuildStills(ContainerContext context)
    {
        // Lossless: identity matrix (the planes are G, B and R), monochrome, and their alpha planes. The decoded samples
        // must equal the source pattern
        var detail = CommonCorpus.PatternDetailColor(12, 9, 1);
        Still(context, "lossless-rgb8", Png(detail), "-l", ["avif.lossless", "avif.matrix=0", "avif.bitDepth=8", "avif.subsampling=4:4:4"], detail);
        var deep10 = DeepColor(8, 8, 10);
        Still(context, "lossless-rgb10", Png(deep10), "-l -d 10", ["avif.lossless", "avif.matrix=0", "avif.bitDepth=10"], deep10);
        var deep12 = DeepColor(7, 6, 12);
        Still(context, "lossless-rgb12", Png(deep12), "-l -d 12", ["avif.lossless", "avif.matrix=0", "avif.bitDepth=12"], deep12);
        var gray = Gray(9, 7);
        Still(context, "lossless-gray8", Png(gray), "-l", ["avif.lossless", "avif.subsampling=4:0:0", "avif.bitDepth=8"], gray);
        var rgba = WithAlpha(detail);
        Still(context, "lossless-rgba8", Png(rgba), "-l", ["avif.lossless", "avif.alpha", "avif.matrix=0"], rgba,
            notes: "The alpha ramp includes 0: the colors under it are kept (hidden colors).");
        var grayAlpha = GrayAlpha(8, 6);
        Still(context, "lossless-gray-alpha8", Png(grayAlpha), "-l", ["avif.lossless", "avif.alpha", "avif.subsampling=4:0:0"], grayAlpha);

        // Lossy, matrixed: every subsampling, the matrices and both ranges
        var smooth = CommonCorpus.PatternSmoothColor(8, 8);
        Still(context, "lossy-444-bt601-full", Png(smooth), "-q 70 -y 444 --cicp 1/13/6", ["avif.matrix=6", "avif.range=full", "avif.subsampling=4:4:4"]);
        Still(context, "lossy-422-bt709-limited", Png(smooth), "-q 70 -y 422 --cicp 1/13/1 -r limited", ["avif.matrix=1", "avif.range=limited", "avif.subsampling=4:2:2"]);
        Still(context, "lossy-420-bt601-odd", Png(CommonCorpus.PatternSmoothColor(7, 5)), "-q 70 -y 420 --cicp 1/13/6", ["avif.matrix=6", "avif.subsampling=4:2:0"]);
        Still(context, "lossy-420-bt2020-limited-10bit", Png(DeepColor(8, 8, 10)), "-q 70 -d 10 -y 420 --cicp 9/16/9 -r limited", ["avif.matrix=9", "avif.range=limited", "avif.bitDepth=10", "avif.subsampling=4:2:0"],
            notes: "An HDR color description (BT.2020 primaries, PQ transfer): the samples are returned as stored.");
        Still(context, "lossy-422-bt709-full-12bit", Png(DeepColor(8, 6, 12)), "-q 70 -d 12 -y 422 --cicp 1/13/1", ["avif.matrix=1", "avif.bitDepth=12", "avif.subsampling=4:2:2"]);
        Still(context, "lossy-420-chroma-vertical", Y4m(8, 8, "420mpeg2"), "-q 70 --cicp 1/13/6", ["avif.chromaSamplePosition=vertical"], sourceName: "source.y4m");
        Still(context, "lossy-420-chroma-colocated", Y4m(8, 8, "420paldv"), "-q 70 --cicp 1/13/6", ["avif.chromaSamplePosition=colocated"], sourceName: "source.y4m");
        Still(context, "lossy-420-alpha", Png(WithAlpha(smooth)), "-q 70 --qalpha 70 -y 420 --cicp 1/13/6", ["avif.alpha", "avif.matrix=6", "avif.subsampling=4:2:0"]);

        // (Matrixed content is subsampled wherever avifdec converts 4:4:4 samples exactly: a tolerance needs a measured
        // disagreement of an independent decoder, and the 4:4:4 conversions are checked matrix by matrix, within one unit,
        // by Tests/Conformance/AvifContainerConformanceTests.)
        // The other matrices of the documented conversion: FCC (4), BT.470BG (5), SMPTE 240M (7), YCgCo (8, full range
        // only: see the traps) and BT.2020 (9) in both ranges, BT.709 and BT.601 in the range the first fixtures left out,
        // and unspecified coefficients (2), which convert as BT.601. Every subsampling and bit depth takes part
        var color10 = DeepColor(8, 8, 10);
        var color12 = DeepColor(8, 6, 12);
        foreach (var (name, source, arguments, requires) in new (string, Img, string, string[])[]
        {
            ("lossy-422-fcc-full", smooth, "-y 422 --cicp 1/13/4", ["avif.matrix=4", "avif.range=full", "avif.subsampling=4:2:2"]),
            ("lossy-420-fcc-limited-10bit", color10, "-d 10 -y 420 --cicp 1/13/4 -r limited", ["avif.matrix=4", "avif.range=limited", "avif.bitDepth=10", "avif.subsampling=4:2:0"]),
            ("lossy-420-bt470bg-full", smooth, "-y 420 --cicp 5/6/5", ["avif.matrix=5", "avif.range=full", "avif.subsampling=4:2:0"]),
            ("lossy-420-bt470bg-limited-12bit", color12, "-d 12 -y 420 --cicp 5/6/5 -r limited", ["avif.matrix=5", "avif.range=limited", "avif.bitDepth=12", "avif.subsampling=4:2:0"]),
            ("lossy-420-smpte240m-full-odd", CommonCorpus.PatternSmoothColor(9, 7), "-y 420 --cicp 7/7/7", ["avif.matrix=7", "avif.range=full", "avif.subsampling=4:2:0"]),
            ("lossy-422-smpte240m-limited-10bit", color10, "-d 10 -y 422 --cicp 7/7/7 -r limited", ["avif.matrix=7", "avif.range=limited", "avif.bitDepth=10", "avif.subsampling=4:2:2"]),
            ("lossy-422-ycgco-full", smooth, "-y 422 --cicp 1/13/8", ["avif.matrix=8", "avif.range=full", "avif.subsampling=4:2:2"]),
            ("lossy-422-ycgco-full-10bit", color10, "-d 10 -y 422 --cicp 1/13/8", ["avif.matrix=8", "avif.bitDepth=10", "avif.subsampling=4:2:2"]),
            ("lossy-420-ycgco-full-12bit", color12, "-d 12 -y 420 --cicp 1/13/8", ["avif.matrix=8", "avif.bitDepth=12", "avif.subsampling=4:2:0"]),
            ("lossy-444-bt2020-full", smooth, "-y 444 --cicp 9/14/9", ["avif.matrix=9", "avif.range=full", "avif.bitDepth=8", "avif.subsampling=4:4:4"]),
            ("lossy-420-bt2020-limited", smooth, "-y 420 --cicp 9/14/9 -r limited", ["avif.matrix=9", "avif.range=limited", "avif.bitDepth=8", "avif.subsampling=4:2:0"]),
            ("lossy-422-bt2020-full-12bit", color12, "-d 12 -y 422 --cicp 9/16/9", ["avif.matrix=9", "avif.range=full", "avif.bitDepth=12", "avif.subsampling=4:2:2"]),
            ("lossy-422-bt709-limited-10bit", color10, "-d 10 -y 422 --cicp 1/13/1 -r limited", ["avif.matrix=1", "avif.range=limited", "avif.bitDepth=10", "avif.subsampling=4:2:2"]),
            ("lossy-420-bt709-full", smooth, "-y 420 --cicp 1/13/1", ["avif.matrix=1", "avif.range=full", "avif.subsampling=4:2:0"]),
            ("lossy-420-bt601-limited", smooth, "-y 420 --cicp 1/13/6 -r limited", ["avif.matrix=6", "avif.range=limited", "avif.subsampling=4:2:0"]),
            ("lossy-420-unspecified", smooth, "-y 420 --cicp 2/2/2", ["avif.matrix=2", "avif.colr=nclx", "avif.range=full", "avif.subsampling=4:2:0"]),
            ("lossy-444-unspecified-limited", smooth, "-y 444 --cicp 2/2/2 -r limited", ["avif.matrix=2", "avif.colr=nclx", "avif.range=limited"]),
        })
        {
            Still(context, name, Png(source), "-q 70 " + arguments, requires);
        }

        // Monochrome: lossless at 10 and 12 bits, lossy, and the limited range, which scales the samples without matrix
        var gray10 = DeepGray(7, 9, 10);
        Still(context, "lossless-gray10", Png(gray10), "-l -d 10", ["avif.lossless", "avif.subsampling=4:0:0", "avif.bitDepth=10"], gray10);
        var gray12 = DeepGray(9, 7, 12);
        Still(context, "lossless-gray12", Png(gray12), "-l -d 12", ["avif.lossless", "avif.subsampling=4:0:0", "avif.bitDepth=12"], gray12);
        Still(context, "lossy-gray8", Png(Gray(9, 7)), "-q 60", ["avif.subsampling=4:0:0", "avif.bitDepth=8", "avif.range=full"]);
        Still(context, "lossy-gray10", Png(DeepGray(8, 8, 10)), "-q 60 -d 10", ["avif.subsampling=4:0:0", "avif.bitDepth=10"]);
        Still(context, "lossy-gray8-limited", Png(Gray(9, 7)), "-q 60 -r limited", ["avif.subsampling=4:0:0", "avif.bitDepth=8", "avif.range=limited"],
            notes: "The samples are scaled from 16..235 to the full range and clipped (ITU-T H.273). avifdec and FFmpeg return 8-bit monochrome samples as stored, without the range (the measured difference); at 12 bits (avif/lossy-gray12-limited) avifdec applies it and agrees exactly.");
        Still(context, "lossy-gray12-limited", Png(DeepGray(8, 6, 12)), "-q 60 -d 12 -r limited", ["avif.subsampling=4:0:0", "avif.bitDepth=12", "avif.range=limited"],
            notes: "The samples are scaled from 256..3760 to the full range and clipped (ITU-T H.273); avifdec agrees exactly, FFmpeg returns the samples as stored.");

        // Sizes: one sample, one row, one column (the chroma planes of a subsampled image are then one sample wide or
        // high), odd sizes at 10 and 12 bits, and images of several coding blocks
        Still(context, "lossy-420-bt601-1x1", Png(CommonCorpus.PatternSmoothColor(1, 1)), "-q 70 -y 420 --cicp 1/13/6", ["avif.subsampling=4:2:0"]);
        Still(context, "lossy-420-bt601-5x1", Png(CommonCorpus.PatternSmoothColor(5, 1)), "-q 70 -y 420 --cicp 1/13/6", ["avif.subsampling=4:2:0"]);
        Still(context, "lossy-422-bt601-1x5", Png(CommonCorpus.PatternSmoothColor(1, 5)), "-q 70 -y 422 --cicp 1/13/6", ["avif.subsampling=4:2:2"]);
        Still(context, "lossy-422-bt601-odd", Png(CommonCorpus.PatternSmoothColor(9, 5)), "-q 70 -y 422 --cicp 1/13/6", ["avif.subsampling=4:2:2"]);
        Still(context, "lossy-420-bt601-odd-10bit", Png(DeepColor(9, 7, 10)), "-q 70 -d 10 -y 420 --cicp 1/13/6", ["avif.subsampling=4:2:0", "avif.bitDepth=10"]);
        Still(context, "lossy-420-bt709-odd-12bit", Png(DeepColor(7, 9, 12)), "-q 70 -d 12 -y 420 --cicp 1/13/1 -r limited", ["avif.subsampling=4:2:0", "avif.bitDepth=12", "avif.range=limited"]);
        Still(context, "lossy-420-bt601-33x17", Png(CommonCorpus.PatternDetailColor(33, 17, 4)), "-q 60 -y 420 --cicp 1/13/6", ["avif.subsampling=4:2:0"]);
        // Smooth content: the references are also the sources of the encoder suites, whose palette criteria (GIF) are
        // calibrated on images of few colors
        var smooth64 = CommonCorpus.PatternSmoothColor(64, 64);
        Still(context, "lossless-rgb8-64x64", Png(smooth64), "-l", ["avif.lossless", "avif.matrix=0"], smooth64);
        Still(context, "lossy-420-bt709-limited-67x65", Png(CommonCorpus.PatternSmoothColor(67, 65)), "-q 50 -y 420 --cicp 1/13/1 -r limited", ["avif.subsampling=4:2:0", "avif.matrix=1", "avif.range=limited"],
            notes: "Larger than a 64x64 superblock in both directions, with odd dimensions: the last column and the last row of the chroma planes are half a sample pair.");

        // Alpha at 10 and 12 bits, lossless and lossy, under lossless and lossy colors. The alpha plane is always full
        // range (see the traps), whatever the range of the colors
        var rgba10 = WithDeepAlpha(DeepColor(8, 8, 10), 10);
        Still(context, "lossless-rgba10", Png(rgba10), "-l -d 10", ["avif.lossless", "avif.alpha", "avif.bitDepth=10", "avif.matrix=0"], rgba10);
        var rgba12 = WithDeepAlpha(DeepColor(7, 6, 12), 12);
        Still(context, "lossless-rgba12", Png(rgba12), "-l -d 12", ["avif.lossless", "avif.alpha", "avif.bitDepth=12", "avif.matrix=0"], rgba12);
        Still(context, "lossy-420-alpha-12bit", Png(WithDeepAlpha(DeepColor(8, 6, 12), 12)), "-q 70 --qalpha 60 -d 12 -y 420 --cicp 1/13/1", ["avif.alpha", "avif.bitDepth=12", "avif.matrix=1", "avif.subsampling=4:2:0"]);
        Still(context, "lossy-422-alpha-10bit-limited", Png(WithDeepAlpha(DeepColor(8, 8, 10), 10)), "-q 70 --qalpha 80 -d 10 -y 422 --cicp 1/13/6 -r limited", ["avif.alpha", "avif.bitDepth=10", "avif.range=limited", "avif.subsampling=4:2:2"],
            notes: "The colors are in limited range and the alpha plane in full range.");
        Still(context, "lossy-gray-alpha10", Png(DeepGrayAlpha(8, 6, 10)), "-q 60 --qalpha 60 -d 10", ["avif.alpha", "avif.bitDepth=10", "avif.subsampling=4:0:0"]);
        Still(context, "identity-lossy-alpha", Png(rgba), "-q 100 --qalpha 40 -y 444 --cicp 1/13/0", ["avif.alpha", "avif.matrix=0"],
            notes: "Lossless identity-matrix colors under a lossy alpha plane: both are compared exactly.");

        // Grids
        var blocks = Blocks(128, 128, alpha: false);
        Still(context, "grid-2x2-lossless", Png(blocks), "-l -g 2x2", ["avif.grid=2x2", "avif.lossless"], blocks);
        Still(context, "grid-2x1-alpha-420", Png(Blocks(128, 64, alpha: true)), "-q 80 --qalpha 80 -y 420 --cicp 1/13/6 -g 2x1", ["avif.grid=2x1", "avif.alpha", "avif.subsampling=4:2:0"],
            notes: "The planes of the tiles are placed in the grid before the chroma is upsampled: the conversion interpolates across the tile boundary, as libavif does.");
        Still(context, "grid-3x1-420", Png(Blocks(192, 64, alpha: false)), "-q 70 -y 420 --cicp 1/13/1 -r limited -g 3x1", ["avif.grid=3x1", "avif.subsampling=4:2:0", "avif.range=limited"]);
        var tall = Blocks(64, 192, alpha: true);
        Still(context, "grid-1x3-alpha-lossless", Png(tall), "-l -g 1x3", ["avif.grid=1x3", "avif.alpha", "avif.lossless"], tall,
            notes: "The alpha plane is a grid of three tiles too.");
        Still(context, "grid-2x1-10bit-444", Png(Widen(Blocks(128, 64, alpha: false))), "-q 70 -d 10 -y 444 --cicp 9/16/9 -g 2x1", ["avif.grid=2x1", "avif.bitDepth=10", "avif.matrix=9"]);
        var wide = Blocks(128, 64, alpha: false);
        Still(context, "grid-2x1-transformed", Png(wide), "-l -g 2x1 --irot 1 --imir 1 --crop 10,6,100,40", ["avif.grid=2x1", "avif.gridTileTransforms", "avif.clap", "avif.irot=1", "avif.imir=1"],
            new Img(100, 40, "rgb", 8, CommonCorpus.Grid(100, 40, (x, y) => wide.Pixels[((y + 6) * 128) + x + 10])),
            notes: "Written by avifenc, which repeats the clap, irot and imir properties of the grid on its two tiles: they apply once, to the assembled image (the aperture is the one of the 128x64 grid, not of a 64x64 tile).");

        // Transformations: reported as the orientation, never applied
        var oriented = CommonCorpus.PatternDetailColor(8, 6, 2);
        foreach (var (name, arguments, requires) in new (string, string, string[])[]
        {
            ("irot-0", "--irot 0", ["avif.irot=0"]),
            ("irot-1", "--irot 1", ["avif.irot=1"]),
            ("irot-2", "--irot 2", ["avif.irot=2"]),
            ("irot-3", "--irot 3", ["avif.irot=3"]),
            ("imir-0", "--imir 0", ["avif.imir=0"]),
            ("imir-1", "--imir 1", ["avif.imir=1"]),
            ("irot-1-imir-1", "--irot 1 --imir 1", ["avif.irot=1", "avif.imir=1"]),
        })
        {
            Still(context, name, Png(oriented), "-l " + arguments, requires, oriented);
        }

        // Clean apertures: the crop applies to the converted image (the chroma of the neighbors is used), then the rotation
        Still(context, "clap-irot", Png(oriented), "-l --crop 2,1,5,4 --irot 1", ["avif.clap", "avif.irot=1"],
            new Img(5, 4, "rgb", 8, CommonCorpus.Grid(5, 4, (x, y) => oriented.Pixels[((y + 1) * 8) + x + 2])));
        Still(context, "clap-420", Png(smooth), "-q 70 -y 420 --cicp 1/13/6 --crop 2,2,4,4", ["avif.clap", "avif.subsampling=4:2:0"]);

        // Metadata: the Exif orientation (6) is not the orientation of the image, which its irot property gives (avifenc
        // derives irot and imir from the Exif orientation unless they are given)
        var exif = CommonCorpus.ExifTiff(littleEndian: true, orientation: 6, software: "corpus generator");
        var xmp = Bytes.Utf8("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>");
        Still(context, "metadata-exif-xmp-icc", Png(oriented), "-l --irot 2 --exif @exif.bin --xmp @xmp.xml --icc @profile.icc", ["avif.metadata=exif", "avif.metadata=xmp", "avif.colr=icc", "avif.irot=2"], oriented,
            files: new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["exif.bin"] = exif, ["xmp.xml"] = xmp, ["profile.icc"] = CommonCorpus.IccProfile("RGB ") },
            notes: "The Exif orientation tag is 6 and the irot property a half turn: the orientation is 3 (the format takes it from irot and imir only).");

        // Film grain: synthesized by the decoder, part of the reference
        Still(context, "film-grain-420", Png(CommonCorpus.PatternSmoothColor(32, 32)), "-q 60 -y 420 --cicp 1/13/6 -a film-grain-test=1", ["avif.filmGrain", "avif.subsampling=4:2:0"]);
        Still(context, "film-grain-400-10bit", Png(DeepGray(32, 32, 10)), "-q 60 -d 10 -a film-grain-test=2", ["avif.filmGrain", "avif.subsampling=4:0:0", "avif.bitDepth=10"]);
        Still(context, "film-grain-422-10bit-limited", Png(DeepColor(16, 16, 10)), "-q 60 -d 10 -y 422 --cicp 1/13/1 -r limited -a film-grain-test=9", ["avif.filmGrain", "avif.subsampling=4:2:2", "avif.bitDepth=10", "avif.range=limited"]);
        Still(context, "film-grain-444-12bit", Png(DeepColor(16, 16, 12)), "-q 60 -d 12 -y 444 --cicp 1/13/6 -a film-grain-test=15", ["avif.filmGrain", "avif.subsampling=4:4:4", "avif.bitDepth=12"],
            notes: "The grain of the chroma planes is scaled from the luma plane (chroma_scaling_from_luma).");
        Still(context, "film-grain-420-odd", Png(CommonCorpus.PatternSmoothColor(17, 13)), "-q 60 -y 420 --cicp 1/13/6 -a film-grain-test=11", ["avif.filmGrain", "avif.subsampling=4:2:0"]);
        Still(context, "film-grain-420-alpha", Png(WithAlpha(CommonCorpus.PatternSmoothColor(16, 16))), "-q 60 --qalpha 60 -y 420 --cicp 1/13/6 -a color:film-grain-test=4", ["avif.filmGrain", "avif.alpha", "avif.subsampling=4:2:0"],
            notes: "Film grain on the color image only; the alpha plane has none.");
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

    /// <summary>A valid file assembled from the description of a file written by an encoder.</summary>
    /// <param name="sourceLayer">The spatial layer of the source that is the image of the assembled file (see <see cref="AddValidAvif"/>), or <see langword="null"/>.</param>
    private static AvifReference Assembled(ContainerContext context, string name, string source, Action<HeifFileBuilder> change, string[] requires, string notes, string function = "BuildLayouts", int? sourceLayer = null)
    {
        var builder = HeifFileBuilder.From(context.Inputs[source]);
        change(builder);
        return AddValidAvif(context, name, builder.Build(), "hand-authored", function, AssemblyTools(context), null, new Obj { ["payloadsOf"] = "avif/" + source }, notes, requires, source, sourceLayer);
    }

    private static void BuildLayouts(ContainerContext context)
    {
        const string Source = "lossless-rgb8";
        Assembled(context, "layout-mdat-first", Source, builder =>
        {
            builder.Layout.Clear();
            foreach (var box in new[] { HeifFileBuilder.FileTypeBox, HeifFileBuilder.MediaDataBox, HeifFileBuilder.MetaBox })
                builder.Layout.Add(box);
        }, ["avif.layout=media-first"], "The mdat box precedes the meta box: a forward-only reader keeps the media data until the items are known.");
        Assembled(context, "layout-idat", Source, builder =>
        {
            builder.GetItem(1).Storage = HeifFileBuilder.ItemStorage.ItemData;
            builder.Layout.Remove(HeifFileBuilder.MediaDataBox);
        }, ["avif.idat"], "The data of the primary item is in the idat box of the meta box (construction method 1); the file has no mdat box.");
        Assembled(context, "layout-extents", Source, builder =>
        {
            var item = builder.GetItem(1);
            foreach (var length in new[] { 7, 1, item.Data.Length - 8 })
                item.ExtentLengths.Add(length);
        }, ["avif.extents=3"], "The data of the primary item is in three extents that are not contiguous (7 bytes, 1 byte, the rest).");
        Assembled(context, "layout-free-unknown-trailing", Source, builder =>
        {
            builder.ExtraBoxes["free1"] = HeifFileBuilder.Box("free", new byte[5]);
            builder.ExtraBoxes["unknown"] = HeifFileBuilder.Box("zzzz", Bytes.Ascii("not an image"));
            builder.ExtraBoxes["free2"] = HeifFileBuilder.Box("free", new byte[11]);
            builder.Layout.Clear();
            foreach (var box in new[] { HeifFileBuilder.FileTypeBox, "free1", HeifFileBuilder.MetaBox, "unknown", HeifFileBuilder.MediaDataBox, "free2" })
                builder.Layout.Add(box);
        }, ["avif.boxes=free", "avif.boxes=unknown", "avif.trailingData"], "A free box before the meta box, an unknown box before the mdat box and a free box after it, which a forward-only reader never reads.");
        Assembled(context, "layout-mif1-large-size-to-end", Source, builder =>
        {
            builder.MajorBrand = "mif1";
            builder.CompatibleBrands.Clear();
            foreach (var brand in new[] { "mif1", "avif", "miaf" })
                builder.CompatibleBrands.Add(brand);
            builder.LargeSizeBoxes.Add(HeifFileBuilder.MetaBox);
            builder.MediaDataExtendsToEnd = true;
        }, ["avif.majorBrand=mif1", "avif.boxes=large-size", "avif.boxes=to-end"], "The major brand is mif1 and avif is a compatible brand; the meta box has a 64-bit size; the size field of the mdat box is 0 (it extends to the end of the file).");

        // Color descriptions: the colr box wins over the bitstream, which describes the colors when there is no colr box
        Assembled(context, "nclx-overrides-bitstream", "lossy-422-bt709-limited", builder =>
            ReplaceProperty(builder, builder.GetItem(1), "colr", HeifFileBuilder.ColorNclx(1, 13, 6, fullRange: true)),
            ["avif.matrix=6", "avif.range=full"], "The bitstream signals BT.709 limited range and the colr box BT.601 full range: the colr box describes the colors.");
        Assembled(context, "no-colr-bitstream-bt709", "lossy-422-bt709-limited", builder => RemoveProperty(builder, builder.GetItem(1), "colr"),
            ["avif.colr=none", "avif.matrix=1", "avif.range=limited"], "No colr box: the color description of the AV1 sequence header (BT.709, limited range) applies.");

        // Without colr box, a header identification reads on to the sequence header of the bitstream, which tells RGB
        // planes (identity matrix) from YCbCr ones; when the bitstream precedes the meta box it has already gone by
        void MediaFirst(HeifFileBuilder builder)
        {
            builder.Layout.Clear();
            foreach (var box in new[] { HeifFileBuilder.FileTypeBox, HeifFileBuilder.MediaDataBox, HeifFileBuilder.MetaBox })
                builder.Layout.Add(box);
        }

        Assembled(context, "no-colr-identity", Source, builder => RemoveProperty(builder, builder.GetItem(1), "colr"),
            ["avif.colr=none", "avif.matrix=0", "avif.range=full"], "No colr box: the sequence header says sRGB primaries and transfer with the identity matrix, so the planes are G, B and R.");
        Assembled(context, "no-colr-identity-mdat-first", Source, builder =>
        {
            RemoveProperty(builder, builder.GetItem(1), "colr");
            MediaFirst(builder);
        }, ["avif.colr=none", "avif.matrix=0", "avif.layout=media-first"], "No colr box and the mdat box before the meta box: a header identification cannot tell the color model (it is unknown), a full scan and a load can.");
        Assembled(context, "no-colr-bt709-mdat-first", "lossy-422-bt709-limited", builder =>
        {
            RemoveProperty(builder, builder.GetItem(1), "colr");
            MediaFirst(builder);
        }, ["avif.colr=none", "avif.matrix=1", "avif.range=limited", "avif.layout=media-first"], "No colr box and the mdat box before the meta box: the color description of the AV1 sequence header (BT.709, limited range) applies; a header identification cannot tell the color model.");
        Assembled(context, "no-colr-unspecified", "lossy-420-unspecified", builder => RemoveProperty(builder, builder.GetItem(1), "colr"),
            ["avif.colr=none", "avif.matrix=2"], "No colr box and a sequence header without color description: there is no color description at all, and the samples convert as BT.601.");
        Assembled(context, "icc-without-nclx", "metadata-exif-xmp-icc", builder =>
        {
            // avifenc writes the ICC profile and the nclx description: only the profile is kept
            var item = builder.GetItem(1);
            for (var i = item.Associations.Count - 1; i >= 0; i--)
            {
                var box = builder.Properties[item.Associations[i].PropertyIndex - 1];
                if (PropertyType(box) == "colr" && Bytes.Latin1(box.AsSpan(8, 4)) == "nclx")
                    item.Associations.RemoveAt(i);
            }
        }, ["avif.colr=icc", "avif.matrix=0"], "A colr box of type prof and none of type nclx: the profile is returned and the bitstream gives the matrix coefficients (identity).");

        // A grid whose image is smaller than its tiles: the last column and the last row are cropped
        Assembled(context, "grid-2x2-cropped", "grid-2x2-lossless", builder =>
        {
            var grid = builder.GetItem(1);
            grid.Data = HeifFileBuilder.GridDescriptor(2, 2, 100, 90);
            ReplaceProperty(builder, grid, "ispe", HeifFileBuilder.ImageSize(100, 90));
        }, ["avif.grid=2x2"], "The grid of avif/grid-2x2-lossless declared as 100x90: the tiles of the last column and of the last row exceed the image.");

        // A grid of three columns and two rows, assembled from six 16x16 images that avifenc coded one by one. The tiles
        // are smaller than the 64 samples MIAF asks for (ISO/IEC 23000-22, section 7.3.11.4.2), which is why avifenc does
        // not write such a grid and avifdec rejects it; ISO/IEC 23008-12 sets no minimum, and a 3x2 grid of 64-sample
        // tiles would cost every generic suite a 192x128 image
        var mosaic = CommonCorpus.PatternSmoothColor(48, 32);
        var tiles = new List<HeifFileBuilder>();
        var tileCommands = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var (left, top) = (i % 3 * 16, i / 3 * 16);
            var tile = new Img(16, 16, "rgb", 8, CommonCorpus.Grid(16, 16, (x, y) => mosaic.Pixels[((top + y) * 48) + left + x]));
            var (tileData, tileCommand) = Avifenc(context, Inv($"grid-3x2-tile{i}"), new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["source.png"] = Png(tile) }, "-l @source.png");
            tiles.Add(HeifFileBuilder.From(tileData));
            tileCommands.Add(tileCommand);
        }

        var grid = tiles[0];
        var firstTile = grid.GetItem(1);
        firstTile.Id = 2;
        firstTile.IsHidden = true;
        var gridItem = grid.AddItem(1, "grid", HeifFileBuilder.GridDescriptor(2, 3, 48, 32), grid.AddProperty(HeifFileBuilder.ImageSize(48, 32)));
        foreach (var (index, _) in firstTile.Associations.Where(association => PropertyType(grid.Properties[association.PropertyIndex - 1]) is "colr" or "pixi"))
            gridItem.Associations.Add((index, false));

        for (var i = 1; i < 6; i++)
        {
            // The six images have the same size and were coded with the same settings: they share their properties
            var source = tiles[i];
            Py.Assert(source.Properties.Select(Convert.ToHexString).SequenceEqual(tiles[0].Properties.Take(source.Properties.Count).Select(Convert.ToHexString)), "tile properties");
            var copy = grid.AddItem((uint)(2 + i), "av01", source.GetItem(1).Data);
            copy.IsHidden = true;
            foreach (var association in firstTile.Associations)
                copy.Associations.Add(association);
        }

        grid.References.Add(("dimg", 1, [2, 3, 4, 5, 6, 7]));
        var gridReference = AddValidAvif(context, "grid-3x2-small-tiles", grid.Build(), "hand-authored", "BuildLayouts", AssemblyTools(context), tileCommands, new Obj { ["tileWidth"] = 16, ["tileHeight"] = 16, ["columns"] = 3, ["rows"] = 2 },
            "Three columns and two rows of 16x16 tiles, coded one by one and assembled: smaller than the 64 samples MIAF asks for, so avifdec rejects the file.", ["avif.grid=3x2", "avif.lossless"]);
        if (!RgbaRaw(gridReference.Frames[0], "rgba8").AsSpan().SequenceEqual(RgbaRaw(mosaic, "rgba8")))
            throw new FatalException("avif/grid-3x2-small-tiles: the grid does not decode to its source pattern.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Image sequences
    // -----------------------------------------------------------------------------------------------------------------

    private static AvifReference Sequence(ContainerContext context, string name, List<Img> frames, string arguments, List<int> durations, string[] requires, string? notes = null, IReadOnlyDictionary<string, byte[]>? extraFiles = null)
    {
        var files = new Dictionary<string, byte[]>(extraFiles ?? new Dictionary<string, byte[]>(StringComparer.Ordinal), StringComparer.Ordinal);
        var inputs = new StringBuilder();
        for (var i = 0; i < frames.Count; i++)
        {
            files[Inv($"frame{i}.png")] = Png(frames[i]);
            inputs.Append(CultureInfo.InvariantCulture, $" --duration {durations[i]} @frame{i}.png");
        }

        // Fixed times: avifenc reads the clock otherwise
        var (data, command) = Avifenc(context, name, files, arguments + " --creation-time 1 --modification-time 1" + inputs);
        return AddValidAvif(context, name, data, "generated", "BuildSequences", EncoderTools(context), [command], null, notes, ["avif.sequence", .. requires]);
    }

    private static void BuildSequences(ContainerContext context)
    {
        var frames = Enumerable.Range(0, 3).Select(i => Moving(8, 8, i)).ToList();
        Sequence(context, "sequence-lossless-infinite", frames, "-l --timescale 10", [2, 3, 1], ["avif.seq.editList=repeat-indefinite", "avif.lossless"],
            "Durations of 2, 3 and 1 tenths of a second; avifenc repeats forever by default (a repeating edit list and an indefinite track duration).");
        Sequence(context, "sequence-alpha-repeat-2", [WithAlpha(Moving(8, 8, 0)), WithAlpha(Moving(8, 8, 2))], "-q 70 --qalpha 70 -y 444 --cicp 1/13/6 --timescale 1000 --repetition-count 2", [40, 1000],
            ["avif.seq.editList=repeat", "avif.alpha"], "Two repetitions: three plays.");
        Sequence(context, "sequence-10bit-once", [DeepColor(8, 8, 10), Shift(DeepColor(8, 8, 10))], "-q 70 -d 10 -y 420 --cicp 9/16/9 -r limited --timescale 30000 --repetition-count 0", [1001, 1001],
            ["avif.seq.editList=once", "avif.bitDepth=10"], "No repetition: an edit list that does not repeat, played once. The duration of a frame is 1001/30000 s.");

        // The FFmpeg avif muxer: another writer of the moov box
        var scratch = context.NewScratch("enc-sequence-ffmpeg");
        var source = scratch / "frames.raw";
        File.WriteAllBytes(source, [.. frames.Take(2).SelectMany(frame => frame.Raw("rgb8"))]);
        var output = scratch / "sequence-ffmpeg.avif";
        var command = context.Tools.FfmpegCmd(["-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", "8x8", "-framerate", "5", "-i", source.Value, "-pix_fmt", "yuv444p", "-color_range", "pc",
            "-c:v", "libaom-av1", "-crf", "20", "-cpu-used", "6", "-row-mt", "0", "-threads", "1", "-flags", "+bitexact", "-fflags", "+bitexact", "-f", "avif", output.Value]);
        Proc.Run(command);
        AddValidAvif(context, "sequence-ffmpeg", File.ReadAllBytes(output), "generated", "BuildSequences", [context.Tools.FfmpegLabel + " (avif muxer, " + context.Tools.Av1.LibaomLabel + ")", context.Tools.DecodersLabel, ToolSet.RuntimeLabel],
            [CommonCorpus.Display(command, context.Tools, [(scratch.Value + Path.DirectorySeparatorChar, "")])], null, "Written by the FFmpeg avif muxer.", ["avif.sequence"]);

        // Longer sequences: inter frames that refer to several earlier images, at several encoder speeds and qualities, with
        // key frames in the middle, every subsampling, 8 to 12 bits, and a lossy alpha track
        static List<int> Repeat(int count, int duration) => [.. Enumerable.Repeat(duration, count)];
        Sequence(context, "sequence-12-frames-420", [.. Enumerable.Range(0, 12).Select(i => Drifting(16, 16, i))], "-q 60 -y 420 --cicp 1/13/6 --timescale 12", Repeat(12, 1),
            ["avif.seq.interFrames", "avif.subsampling=4:2:0", "avif.seq.editList=repeat-indefinite"], "Twelve images of 1/12 s.");
        Sequence(context, "sequence-8-frames-444-slow", [.. Enumerable.Range(0, 8).Select(i => Drifting(16, 12, i))], "-s 2 -q 85 -y 444 --cicp 1/13/1 -r limited --timescale 25 --repetition-count 4", [1, 2, 1, 2, 1, 2, 1, 3],
            ["avif.seq.interFrames", "avif.subsampling=4:4:4", "avif.range=limited", "avif.seq.editList=repeat"], "Encoder speed 2, high quality; four repetitions: five plays.");
        Sequence(context, "sequence-8-frames-422-key-frames", [.. Enumerable.Range(0, 8).Select(i => Drifting(16, 16, 2 * i))], "-s 9 -q 30 -y 422 --cicp 1/13/6 -k 3 --timescale 5", Repeat(8, 1),
            ["avif.seq.interFrames", "avif.subsampling=4:2:2"], "Encoder speed 9, low quality, a key frame at least every three images.");
        Sequence(context, "sequence-8-frames-gray", [.. Enumerable.Range(0, 8).Select(i => MovingGray(12, 12, i))], "-q 50 --timescale 8", Repeat(8, 1),
            ["avif.seq.interFrames", "avif.subsampling=4:0:0"], "A monochrome sequence: gray samples, compared exactly.");
        Sequence(context, "sequence-9-frames-rgb12-lossless", [.. Enumerable.Range(0, 9).Select(i => Shift(DeepColor(8, 8, 12), i))], "-l -d 12 --timescale 30 --repetition-count 0", Repeat(9, 1),
            ["avif.lossless", "avif.bitDepth=12", "avif.matrix=0", "avif.seq.editList=once"], "Lossless 12-bit identity-matrix images, played once.");
        Sequence(context, "sequence-8-frames-420-10bit", [.. Enumerable.Range(0, 8).Select(i => Shift(DeepColor(12, 12, 10), i))], "-s 4 -q 50 -d 10 -y 420 --cicp 9/16/9 --timescale 24000", Repeat(8, 1001),
            ["avif.seq.interFrames", "avif.bitDepth=10", "avif.subsampling=4:2:0", "avif.matrix=9"], "Ten bits, 4:2:0, 1001/24000 s per image.");
        Sequence(context, "sequence-8-frames-alpha-lossy", [.. Enumerable.Range(0, 8).Select(i => WithAlpha(Drifting(16, 16, i)))], "-q 60 --qalpha 40 -y 420 --cicp 1/13/6 --timescale 10", Repeat(8, 1),
            ["avif.seq.interFrames", "avif.alpha", "avif.subsampling=4:2:0"], "A lossy alpha track with inter frames of its own.");
        Sequence(context, "sequence-alpha-10bit", [.. Enumerable.Range(0, 4).Select(i => WithDeepAlpha(Shift(DeepColor(8, 8, 10), i), 10))], "-q 60 --qalpha 60 -d 10 -y 444 --cicp 1/13/6 --timescale 10", Repeat(4, 1),
            ["avif.alpha", "avif.bitDepth=10"], "Ten-bit colors and a ten-bit alpha track.");

        // Metadata of a sequence. avifenc stores the XMP packet as an item of the meta box of the file and, for the
        // sequence, of a meta box in the trak box (the same bytes); the ICC profile is in the sample entry
        var xmp = Bytes.Utf8("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>");
        var exif = CommonCorpus.ExifTiff(littleEndian: true, orientation: 6, software: "corpus generator");
        var four = Enumerable.Range(0, 4).Select(i => Moving(8, 8, i)).ToList();
        Sequence(context, "sequence-xmp", four, "-l --xmp @xmp.xml --timescale 10", Repeat(4, 1), ["avif.metadata=xmp", "avif.seq.metadataSource=track"],
            "Written by avifenc --xmp: the XMP item is in the meta box of the track.", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["xmp.xml"] = xmp });
        Sequence(context, "sequence-icc", four.Take(3).ToList(), "-l --icc @profile.icc --timescale 10", Repeat(3, 1), ["avif.colr=icc"],
            "Written by avifenc --icc: the profile is in the sample entry of the track.", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["profile.icc"] = CommonCorpus.IccProfile("RGB ") });

        // Exif and XMP of a sequence in the places avifenc does not write (it crashes on --exif with several frames):
        // the meta box of the file before the moov box, item data in the mdat or in the idat box, before or after the
        // samples; the meta box of the track. The Exif orientation (6) is not the orientation of the images
        const string Plain = "sequence-lossless-infinite";
        var otherXmp = Bytes.Utf8("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"file\"/></rdf:RDF></x:xmpmeta>");
        void AddFileMetadata(HeifFileBuilder builder, HeifFileBuilder.ItemStorage storage, byte[] packet)
        {
            // As avifenc links its XMP item: content descriptions of the first image, which is the primary item
            var exifItem = builder.AddItem(100, "Exif", HeifFileBuilder.ExifItemData(exif));
            var xmpItem = builder.AddItem(101, "mime", packet);
            xmpItem.ContentType = "application/rdf+xml";
            exifItem.Storage = xmpItem.Storage = storage;
            builder.References.Add(("cdsc", 100, [builder.PrimaryItemId!.Value]));
            builder.References.Add(("cdsc", 101, [builder.PrimaryItemId!.Value]));
        }

        void AddTrackMetadata(HeifFileBuilder builder, HeifFileBuilder.ItemStorage storage, bool withExif = true)
        {
            var track = builder.Tracks.Single(candidate => candidate.HandlerType == "pict");
            if (withExif)
                track.MetaItems.Add(new HeifFileBuilder.Item(1, "Exif", HeifFileBuilder.ExifItemData(exif)) { Storage = storage });

            track.MetaItems.Add(new HeifFileBuilder.Item(2, "mime", xmp) { ContentType = "application/rdf+xml", Storage = storage });
        }

        Assembled(context, "sequence-file-meta-exif-xmp", Plain, builder => AddFileMetadata(builder, HeifFileBuilder.ItemStorage.MediaData, xmp),
            ["avif.metadata=exif", "avif.metadata=xmp", "avif.seq.metadataSource=file"], "Exif and XMP items in the meta box of the file, which precedes the moov box; their data are in the mdat box, before the samples.", "BuildSequences");
        Assembled(context, "sequence-file-meta-idat", Plain, builder => AddFileMetadata(builder, HeifFileBuilder.ItemStorage.ItemData, xmp),
            ["avif.metadata=exif", "avif.metadata=xmp", "avif.seq.metadataSource=file", "avif.idat"], "Exif and XMP items in the meta box of the file, their data in its idat box.", "BuildSequences");
        Assembled(context, "sequence-file-meta-after-samples", Plain, builder =>
        {
            AddFileMetadata(builder, HeifFileBuilder.ItemStorage.MediaData, xmp);
            builder.ItemDataFollowsSamples = true;
        }, ["avif.metadata=exif", "avif.metadata=xmp", "avif.seq.metadataSource=file", "avif.seq.metadataAfterSamples"], "The data of the Exif and XMP items of the meta box of the file follow the samples in the mdat box: the last image is complete once they are read.", "BuildSequences");
        Assembled(context, "sequence-track-meta-exif-xmp", Plain, builder => AddTrackMetadata(builder, HeifFileBuilder.ItemStorage.MediaData),
            ["avif.metadata=exif", "avif.metadata=xmp", "avif.seq.metadataSource=track"], "Exif and XMP items in the meta box of the track (hdlr, iloc and iinf boxes, as libavif writes it for XMP); their data are in the mdat box.", "BuildSequences");
        Assembled(context, "sequence-track-meta-idat", Plain, builder => AddTrackMetadata(builder, HeifFileBuilder.ItemStorage.ItemData),
            ["avif.metadata=exif", "avif.metadata=xmp", "avif.seq.metadataSource=track", "avif.idat"], "Exif and XMP items in the meta box of the track, their data in the idat box of that meta box.", "BuildSequences");
        Assembled(context, "sequence-track-meta-after-samples", Plain, builder =>
        {
            AddTrackMetadata(builder, HeifFileBuilder.ItemStorage.MediaData);
            builder.ItemDataFollowsSamples = true;
        }, ["avif.metadata=exif", "avif.metadata=xmp", "avif.seq.metadataSource=track", "avif.seq.metadataAfterSamples"], "The data of the items of the meta box of the track follow the samples in the mdat box.", "BuildSequences");
        Assembled(context, "sequence-track-meta-wins", Plain, builder =>
        {
            AddFileMetadata(builder, HeifFileBuilder.ItemStorage.MediaData, otherXmp);
            AddTrackMetadata(builder, HeifFileBuilder.ItemStorage.MediaData, withExif: false);
        }, ["avif.metadata=xmp", "avif.seq.metadataSource=track", "avif.seq.fileMetadataNotRead"], "The meta box of the file has an Exif item and an XMP item, the meta box of the track another XMP item: the items of the track are the metadata of the sequence, so it has no Exif data.", "BuildSequences");
        Assembled(context, "sequence-file-meta-after-moov", Plain, builder =>
        {
            AddFileMetadata(builder, HeifFileBuilder.ItemStorage.MediaData, xmp);
            builder.Layout.Clear();
            foreach (var box in new[] { HeifFileBuilder.FileTypeBox, HeifFileBuilder.MovieBox, HeifFileBuilder.MetaBox, HeifFileBuilder.MediaDataBox })
                builder.Layout.Add(box);
        }, ["avif.seq.fileMetadataNotRead"], "The meta box of the file, which has Exif and XMP items, follows the moov box: a sequence only takes its metadata from a meta box of the file that precedes the moov box (libavif never reads it for a sequence), so this one has none.", "BuildSequences");

        // The chunks of a track may be anywhere in the file: one sample per chunk, written from the last one to the first
        Assembled(context, "sequence-chunks-reversed", "sequence-8-frames-gray", builder =>
        {
            foreach (var track in builder.Tracks)
                track.SamplesPerChunk = 1;

            builder.ChunksInReverseOrder = true;
        }, ["avif.seq.samplesOutOfOrder", "avif.seq.interFrames"], "One sample per chunk, the chunks written in the mdat box from the last one to the first one: the first image is the last one in the file, and every image is decoded from samples that precede it in the file.", "BuildSequences");

        // The FFmpeg avif muxer around SVT-AV1 (low delay: every frame is shown; \"lp=2\" as for the AV1 vectors)
        var svtScratch = context.NewScratch("enc-sequence-ffmpeg-svt");
        var svtSource = svtScratch / "frames.raw";
        File.WriteAllBytes(svtSource, [.. Enumerable.Range(0, 2).SelectMany(i => Drifting(64, 64, 3 * i).Raw("rgb8"))]);
        var svtOutput = svtScratch / "sequence-ffmpeg-svt.avif";
        var svtCommand = context.Tools.FfmpegCmd(["-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", "64x64", "-framerate", "5", "-i", svtSource.Value, "-pix_fmt", "yuv420p",
            "-c:v", "libsvtav1", "-preset", "8", "-crf", "40", "-svtav1-params", "lp=2:pred-struct=1", "-threads", "1", "-flags", "+bitexact", "-fflags", "+bitexact", "-f", "avif", svtOutput.Value]);
        Proc.Run(svtCommand);
        AddValidAvif(context, "sequence-ffmpeg-svt", File.ReadAllBytes(svtOutput), "generated", "BuildSequences", [context.Tools.FfmpegLabel + " (avif muxer, " + context.Tools.Av1.SvtLabel + ")", context.Tools.DecodersLabel, ToolSet.RuntimeLabel],
            [CommonCorpus.Display(svtCommand, context.Tools, [(svtScratch.Value + Path.DirectorySeparatorChar, "")])], null, "Written by the FFmpeg avif muxer from SVT-AV1 frames (64x64 is the smallest size SVT-AV1 encodes, hence two images only).", ["avif.sequence", "avif.seq.interFrames"]);

        // Sample tables and layouts avifenc does not write
        const string Source = "sequence-alpha-repeat-2";
        Assembled(context, "sequence-no-edit-list", Source, builder =>
        {
            foreach (var track in builder.Tracks)
                track.EditList = null;
        }, ["avif.seq.editList=none"], "No edit list: the number of repetitions is not known, the sequence repeats forever.", "BuildSequences");
        Assembled(context, "sequence-mdat-first", Source, builder =>
        {
            builder.Layout.Clear();
            foreach (var box in new[] { HeifFileBuilder.FileTypeBox, HeifFileBuilder.MediaDataBox, HeifFileBuilder.MetaBox, HeifFileBuilder.MovieBox })
                builder.Layout.Add(box);
        }, ["avif.layout=media-first"], "The mdat box precedes the moov box.", "BuildSequences");
        Assembled(context, "sequence-co64-stz2", Source, builder =>
        {
            foreach (var track in builder.Tracks)
            {
                track.ChunkOffsetBox = "co64";
                track.SampleSizeBox = "stz2";
                track.SamplesPerChunk = 1;
                track.HeaderVersion = 0;
            }
        }, ["avif.seq.chunkOffsets=co64", "avif.seq.sampleSizes=stz2"], "64-bit chunk offsets (co64), compact 16-bit sample sizes (stz2), one sample per chunk and version 0 track and media headers.", "BuildSequences");
    }

    /// <summary>The same content moved by a number of samples in both directions: the next images of a sequence.</summary>
    private static Img Shift(Img img, int count = 1) =>
        new(img.Width, img.Height, img.Model, img.Depth, CommonCorpus.Grid(img.Width, img.Height, (x, y) => img.Pixels[(((y + count) % img.Height) * img.Width) + ((x + count) % img.Width)]));
}
