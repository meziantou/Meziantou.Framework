using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.JpegXl;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

internal static partial class JxlCorpus
{
    private static readonly byte[] ContainerStart =
    [
        0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A,
        .. Box("ftyp", [.. "jxl "u8, 0, 0, 0, 0, .. "jxl "u8]),
    ];

    private static readonly byte[] XmpPacket = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>\n");

    /// <summary>Splits a container into its boxes; a last box that extends to the end of the file is given its size.</summary>
    private static List<(string Type, byte[] Bytes)> SplitBoxes(byte[] data)
    {
        var boxes = new List<(string Type, byte[] Bytes)>();
        var position = 0;
        while (position < data.Length)
        {
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            Py.Assert(size != 1, "no box of the container has a 64-bit size");
            var box = data.AsSpan(position, size == 0 ? data.Length - position : (int)size).ToArray();
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            boxes.Add((Encoding.Latin1.GetString(box, 4, 4), box));
            position += box.Length;
        }

        return boxes;
    }

    private static byte[] Box(string type, ReadOnlySpan<byte> payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.Latin1.GetBytes(type, box.AsSpan(4));
        payload.CopyTo(box.AsSpan(8));
        return box;
    }

    private static byte[] Part(uint index, ReadOnlySpan<byte> codestream)
    {
        var payload = new byte[4 + codestream.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, index);
        codestream.CopyTo(payload.AsSpan(4));
        return Box("jxlp", payload);
    }

    private static Obj PatternReference(string description) => new() { ["method"] = "hand-computed", ["description"] = description };

    private static byte[] Input(Corpus<JxlTools> c, string name)
    {
        var fixture = c.Fixtures.First(f => (string)f["id"]! == "jxl/" + name);
        return File.ReadAllBytes(c.Out / (string)((Obj)fixture["input"]!)["path"]!);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Lossless images: the reference is the pattern given to the encoder
    // -----------------------------------------------------------------------------------------------------------------

    private static void AddLossless(Corpus<JxlTools> c, string name, string function, Img pattern, string description, IReadOnlyList<string> options, Expectation? expectation = null,
        IEnumerable<(string Key, byte[] Data, string Symbol)>? files = null, string? notes = null)
    {
        var encoded = Cjxl(c, pattern, options, files: files);
        AddValid(c, name, function, encoded, [pattern], PatternReference("The pattern given to the encoder: " + description), CrossChecks(c, encoded.Data, [pattern], lossless: true),
            expectation ?? new Expectation(), new Obj { ["width"] = pattern.Width, ["height"] = pattern.Height }, notes: notes);
    }

    private static void BuildLossless(Corpus<JxlTools> c)
    {
        const string Function = nameof(BuildLossless);
        AddLossless(c, "lossless-rgb8", Function, CommonCorpus.PatternRgbOdd(13, 9), "PatternRgbOdd.", ["-d", "0", "-e", "7"]);
        AddLossless(c, "lossless-rgba8", Function, CommonCorpus.PatternCornerMarkers(12, 9), "PatternCornerMarkers, whose alpha varies.", ["-d", "0", "-e", "7"]);
        AddLossless(c, "lossless-gray8", Function, CommonCorpus.PatternCheckerboardGray(11, 7), "PatternCheckerboardGray.", ["-d", "0", "-e", "3"]);
        AddLossless(c, "lossless-graya8", Function, CommonCorpus.PatternGrayAlphaRamp(8, 2), "PatternGrayAlphaRamp: a fully transparent pixel keeps its gray value.", ["-d", "0"]);
        AddLossless(c, "lossless-gray16", Function, CommonCorpus.PatternGray16LowBits(8, 8), "PatternGray16LowBits: the low byte of every sample is significant.", ["-d", "0"]);
        AddLossless(c, "lossless-graya16", Function, CommonCorpus.PatternGraya16LowBits(8, 6), "PatternGraya16LowBits.", ["-d", "0"]);
        AddLossless(c, "lossless-rgb16", Function, CommonCorpus.PatternRgb16(9, 7), "PatternRgb16.", ["-d", "0"]);
        AddLossless(c, "lossless-rgba16", Function, CommonCorpus.PatternRgba16LowBits(8, 8), "PatternRgba16LowBits.", ["-d", "0"]);
        AddLossless(c, "lossless-effort1", Function, CommonCorpus.PatternDetailColor(24, 16, 7), "PatternDetailColor(seed 7).", ["-d", "0", "-e", "1"],
            notes: "Effort 1 codes with prefix codes and a fixed tree.");
        AddLossless(c, "lossless-effort9", Function, CommonCorpus.PatternDetailColor(24, 16, 7), "PatternDetailColor(seed 7).", ["-d", "0", "-e", "9"],
            notes: "Effort 9 searches the tree, the predictors and the color transforms.");
        AddLossless(c, "lossless-few-colors", Function, FewColors(24, 16), "Six colors in diagonal stripes.", ["-d", "0", "-e", "7"],
            notes: "An image of few colors: the encoder stores a palette.");
        AddLossless(c, "lossless-squeeze", Function, CommonCorpus.PatternSmoothColor(40, 30), "PatternSmoothColor.", ["-d", "0", "--responsive=1"],
            notes: "A responsive lossless image is stored with the squeeze transform.");
        AddLossless(c, "lossless-groups", Function, CommonCorpus.PatternSmoothColor(140, 132), "PatternSmoothColor.", ["-d", "0", "-e", "2", "--modular_group_size=0"],
            notes: "Groups of 128x128 samples: four groups, each in its own section.");
        AddLossless(c, "lossless-container", Function, CommonCorpus.PatternRgbOdd(13, 9), "PatternRgbOdd.", ["-d", "0", "--container=1"]);

        // Twelve-bit samples: the library scales them exactly to sixteen bits
        var twelve = new Img(7, 5, "rgb", 16, CommonCorpus.Grid(7, 5, (x, y) => new Px((x * 683) % 4096, (4095 - (y * 1000)) % 4096, ((x * y * 331) + 5) % 4096)));
        var scaled = new Img(7, 5, "rgb", 16, twelve.Pixels.Select(p => new Px(Scale(p[0]), Scale(p[1]), Scale(p[2]))));
        var encoded = Cjxl(c, twelve, ["-d", "0"], maxValue: 4095);
        AddValid(c, "lossless-rgb12", Function, encoded, [scaled],
            PatternReference("Samples (683x mod 4096, (4095 - 1000y) mod 4096, (331xy + 5) mod 4096) of twelve bits, scaled to sixteen bits: (v * 65535 + 2047) / 4095."),
            CrossChecks(c, encoded.Data, [scaled], lossless: true), new Expectation { BitsPerComponent = 12 }, new Obj { ["width"] = 7, ["height"] = 5, ["maxValue"] = 4095 });

        static int Scale(int value) => ((value * 65535) + 2047) / 4095;
    }

    /// <summary>Rows of three small glyphs repeated on a light background, as text is.</summary>
    private static Img Glyphs(int width, int height)
    {
        string[][] glyphs =
        [
            ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
            ["11110", "10001", "11110", "10001", "10001", "10001", "11110"],
            ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
        ];
        return new Img(width, height, "rgb", 8, CommonCorpus.Grid(width, height, (x, y) =>
        {
            var (column, cellX) = (x / 7, x % 7);
            var (row, cellY) = (y / 9, y % 9);
            var glyph = glyphs[(column + (2 * row)) % glyphs.Length];
            var ink = cellX is >= 1 and <= 5 && cellY is >= 1 and <= 7 && glyph[cellY - 1][cellX - 1] == '1';
            return ink ? new Px(20, 24, 40) : new Px(236, 232, 220);
        }));
    }

    private static Img FewColors(int width, int height)
    {
        Px[] colors = [new(255, 0, 0), new(0, 160, 0), new(0, 0, 255), new(255, 255, 0), new(16, 16, 16), new(255, 255, 255)];
        return new Img(width, height, "rgb", 8, CommonCorpus.Grid(width, height, (x, y) => colors[((x + (2 * y)) / 3) % colors.Length]));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Metadata and color encodings
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildMetadata(Corpus<JxlTools> c)
    {
        const string Function = nameof(BuildMetadata);
        var exif = CommonCorpus.ExifTiff(littleEndian: true, orientation: 1, software: "Meziantou.Framework.Imaging corpus");
        var rgb = CommonCorpus.PatternRgbOdd(13, 9);
        (string Key, byte[] Data, string Symbol)[] boxes = [("exif", exif, "{exif}.tiff"), ("xmp", XmpPacket, "{xmp}.xml")];
        AddLossless(c, "metadata-exif-xmp", Function, rgb, "PatternRgbOdd.", ["-d", "0", "--compress_boxes=0"], new Expectation { Exif = exif, Xmp = XmpPacket }, boxes,
            notes: "The EXIF and XMP payloads are stored as they are, in Exif and xml boxes that precede the frames.");
        AddLossless(c, "metadata-exif-xmp-compressed", Function, rgb, "PatternRgbOdd.", ["-d", "0", "--compress_boxes=1"], new Expectation { Exif = exif, Xmp = XmpPacket }, boxes,
            notes: "The EXIF and XMP payloads are compressed with Brotli, in brob boxes.");

        // ICC profiles of the corpus: they describe the samples, which are stored as they are
        var displayP3 = File.ReadAllBytes(CorpusDriver.CorpusDir / "icc" / "profiles" / "DisplayP3-v4.icc");
        AddLossless(c, "icc-rgb", Function, rgb, "PatternRgbOdd.", ["-d", "0", "--patches=0"], new Expectation { Icc = displayP3 }, [("icc_pathname", displayP3, "{icc/profiles/DisplayP3-v4.icc}")],
            notes: "The profile is stored in the codestream, compressed; the library returns its bytes.");
        var gray = File.ReadAllBytes(CorpusDriver.CorpusDir / "icc" / "profiles" / "sGrey-v4.icc");
        AddLossless(c, "icc-gray", Function, CommonCorpus.PatternCheckerboardGray(11, 7), "PatternCheckerboardGray.", ["-d", "0", "--patches=0"], new Expectation { Icc = gray },
            [("icc_pathname", gray, "{icc/profiles/sGrey-v4.icc}")]);

        // Enumerated color encodings: labels, the samples are stored as they are
        AddLossless(c, "color-linear", Function, rgb, "PatternRgbOdd.", ["-d", "0", "-x", "color_space=RGB_D65_SRG_Rel_Lin"], new Expectation { IsLinear = true },
            notes: "The sRGB primaries with a linear transfer function: the linear label of the library.");
        AddLossless(c, "color-display-p3", Function, rgb, "PatternRgbOdd.", ["-d", "0", "-x", "color_space=RGB_D65_DCI_Rel_SRG"], new Expectation { Cicp = Cicp(12, 13) },
            notes: "The P3 primaries with the D65 white point and the sRGB transfer function: code points 12 and 13 of ITU-T H.273.");
        AddLossless(c, "color-rec2100-pq", Function, CommonCorpus.PatternRgb16(9, 7), "PatternRgb16.", ["-d", "0", "-x", "color_space=RGB_D65_202_Rel_PeQ"], new Expectation { Cicp = Cicp(9, 16) },
            notes: "The BT.2100 primaries with the PQ transfer function: the samples are returned as stored, never tone mapped.");
        AddLossless(c, "color-gray-bt709", Function, CommonCorpus.PatternCheckerboardGray(11, 7), "PatternCheckerboardGray.", ["-d", "0", "-x", "color_space=Gra_D65_Rel_709"], new Expectation { Cicp = Cicp(1, 1) });

        // The orientation of the image header: reported, never applied
        var oriented = new Img(6, 4, "rgb", 8, CommonCorpus.Grid(6, 4, (x, y) => x > 2 ? new Px(200, 200, 200) : new Px(50, 50, 50)));
        var tree = FromTree(c, """
            Width 6
            Height 4
            Bitdepth 8
            Orientation 6
            if x > 2
              - Set 200
              - Set 50
            """);
        AddValid(c, "orientation-6", Function, tree, [oriented], PatternReference("The tree sets 200 where x > 2 and 50 elsewhere, in the stored orientation."),
            CrossChecks(c, tree.Data, [oriented], lossless: true, orientation: 6), new Expectation { Orientation = 6 }, origin: "hand-authored",
            notes: "The image header says the image is rotated 90 degrees clockwise for display; the reference decoders apply it, the fixture expects the stored samples.");

        static Obj Cicp(int primaries, int transfer) => new() { ["colorPrimaries"] = primaries, ["transferCharacteristics"] = transfer, ["matrixCoefficients"] = 0, ["fullRange"] = true };
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Containers assembled around a codestream of cjxl
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildContainers(Corpus<JxlTools> c)
    {
        const string Function = nameof(BuildContainers);
        var pattern = CommonCorpus.PatternCornerMarkers(12, 9);
        var codestream = Input(c, "lossless-rgba8");
        Py.Assert(codestream[0] == 0xFF && codestream[1] == 0x0A, "lossless-rgba8 is a bare codestream");
        var exif = CommonCorpus.ExifTiff(littleEndian: false, orientation: 1);

        void Add(string name, byte[] data, string layout, Expectation? expectation = null, IEnumerable<string>? features = null)
        {
            var encoded = new Encoded(data, []);
            AddValid(c, name, Function, encoded, [pattern], PatternReference("PatternCornerMarkers: the codestream is the one of jxl/lossless-rgba8."), CrossChecks(c, data, [pattern], lossless: true),
                expectation ?? new Expectation(), new Obj { ["codestream"] = "jxl/lossless-rgba8", ["layout"] = layout }, origin: "hand-authored", extraFeatures: features);
        }

        Add("container-parts", [.. ContainerStart, .. Part(0, codestream.AsSpan(0, 5)), .. Part(1, codestream.AsSpan(5, 30)), .. Part(0x80000002, codestream.AsSpan(35))],
            "ftyp, jxlp (5 bytes: inside the image header), jxlp (30 bytes: to the middle of the frame), jxlp (the rest, marked last)");
        byte[] large = [0, 0, 0, 1, .. "jxlc"u8, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt64BigEndian(large.AsSpan(8), (ulong)(16 + codestream.Length));
        Add("container-large-size", [.. ContainerStart, .. large, .. codestream], "ftyp, jxlc with a 64-bit size");
        Add("container-box-to-end", [.. ContainerStart, 0, 0, 0, 0, .. "jxlc"u8, .. codestream], "ftyp, jxlc of size 0: the box extends to the end of the file");
        Add("container-unknown-boxes", [.. ContainerStart, .. Box("free", [1, 2, 3, 4, 5]), .. Box("jxlc", codestream), .. Box("abcd", [9, 8, 7])],
            "ftyp, free, jxlc, a box of an unknown type");
        Add("container-metadata-after", [.. ContainerStart, .. Box("jxlc", codestream), .. Box("Exif", [0, 0, 0, 0, .. exif]), .. Box("xml ", XmpPacket)],
            "ftyp, jxlc, Exif, xml: the metadata follows the image", new Expectation { Exif = exif, Xmp = XmpPacket });
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Lossy images: the reference is decoded by djxl
    // -----------------------------------------------------------------------------------------------------------------

    private static void AddLossy(Corpus<JxlTools> c, string name, string function, Encoded encoded, int depth, Obj parameters, string? notes = null, string origin = "generated",
        IEnumerable<string>? extraFeatures = null)
    {
        var path = c.Scratch / "lossy.jxl";
        File.WriteAllBytes(path, encoded.Data);
        var decoded = Djxl(c, path);
        var frames = ToImages(decoded, depth);
        var reference = new Obj
        {
            ["method"] = "decoded",
            ["description"] = "Decoded from the same encoded input by djxl to floating-point samples, rounded to the nearest integer.",
            ["decoder"] = DjxlLabel(c),
            ["backend"] = $"libjxl {c.Tools.LibjxlVersion}",
            ["command"] = decoded.Command,
        };
        var oxide = Check(OxideLabel(c), JxlOxide(c, path), frames,
            "An independent decoder: JPEG XL defines lossy decoding in floating point, so two conforming decoders round some samples differently.");
        AddValid(c, name, function, encoded, frames, reference, [oxide], new Expectation(), parameters, origin, LossyPolicy(name, [oxide]), notes, extraFeatures);
    }

    private static void BuildLossy(Corpus<JxlTools> c)
    {
        const string Function = nameof(BuildLossy);
        void Add(string name, Img pattern, string description, IReadOnlyList<string> options, string? notes = null, IEnumerable<string>? extraFeatures = null)
            => AddLossy(c, name, Function, Cjxl(c, pattern, options), pattern.Depth, new Obj { ["pattern"] = description, ["width"] = pattern.Width, ["height"] = pattern.Height }, notes, extraFeatures: extraFeatures);

        Add("lossy-rgb8", CommonCorpus.PatternDetailColor(32, 16, 3), "PatternDetailColor(seed 3)", ["-d", "1"]);
        Add("lossy-rgb8-fast", CommonCorpus.PatternDetailColor(32, 16, 3), "PatternDetailColor(seed 3)", ["-d", "3", "-e", "3"],
            notes: "Effort 3 only uses the DCT of 8x8 samples.");
        Add("lossy-rgb8-smooth", CommonCorpus.PatternSmoothColor(96, 80), "PatternSmoothColor", ["-d", "2", "-e", "7"],
            notes: "A smooth image: the encoder chooses the larger transforms.");
        Add("lossy-rgba8", CommonCorpus.PatternRgbaAdam7(24, 16), "PatternRgbaAdam7", ["-d", "1"], notes: "The alpha channel is stored losslessly, in a Modular channel of the VarDCT frame.");
        Add("lossy-gray8", CommonCorpus.PatternDetailGray(48, 40, 5), "PatternDetailGray(seed 5)", ["-d", "1"]);
        var smooth = CommonCorpus.PatternSmoothColor(24, 16);
        Add("lossy-rgb16", new Img(24, 16, "rgb", 16, smooth.Pixels.Select((p, i) => new Px((p[0] * 257) + (i % 64), (p[1] * 257) - (i % 32), (p[2] * 257) + ((i * 7) % 128)))),
            "PatternSmoothColor scaled to sixteen bits, with low bits that vary", ["-d", "0.5"]);
        // The table of contents of the first frame, an LF frame of 4x2 samples (an eighth of the image), is permuted: the
        // reader of the harness stops there, and the pixels of that frame, which is never shown, are stated here
        Add("lossy-progressive", CommonCorpus.PatternDetailColor(32, 16, 3), "PatternDetailColor(seed 3)", ["-d", "1", "-p"],
            notes: "Several passes refine the coefficients; the LF image is a frame of its own and the sections of the frames are permuted.", extraFeatures: ["jxl.hiddenPixels=8"]);
        Add("lossy-noise", CommonCorpus.PatternSmoothColor(64, 48), "PatternSmoothColor", ["-d", "1", "--photon_noise_iso=3200"],
            notes: "Noise is synthesized by the decoder from the parameters of the frame.");
        Add("lossy-upsampled", CommonCorpus.PatternSmoothColor(64, 48), "PatternSmoothColor", ["-d", "2", "--resampling=2"], notes: "The frame is stored at half its size and upsampled.");
        Add("lossy-modular", CommonCorpus.PatternDetailColor(32, 16, 9), "PatternDetailColor(seed 9)", ["-d", "1", "-m", "1"],
            notes: "A lossy Modular frame: XYB samples quantized through the squeeze transform.");
        Add("lossy-lf-frame", CommonCorpus.PatternSmoothColor(64, 48), "PatternSmoothColor", ["-d", "1", "--progressive_dc=1"],
            notes: "The LF image is a frame of its own, an eighth of the size, that is decoded and never shown.");
        Add("lossy-patches", Glyphs(120, 72), "A row pattern of the same 5x7 glyphs, dark on a light background", ["-d", "1", "--patches=1"],
            notes: "The encoder stores the repeated shapes once, in a frame that is not shown, and the image refers to it.");
        Add("lossy-no-filters", CommonCorpus.PatternDetailColor(32, 16, 9), "PatternDetailColor(seed 9)", ["-d", "3", "--gaborish=0", "--epf=0"]);
        Add("lossy-strong-filter", CommonCorpus.PatternDetailColor(32, 16, 9), "PatternDetailColor(seed 9)", ["-d", "6", "--epf=3"]);

        // JPEG files of the corpus, recompressed without loss: their coefficients are stored as they are
        foreach (var (name, jpeg, options) in new (string, string, string[])[]
        {
            ("jpeg-420", "jpeg/ffmpeg-mjpeg-420-37x23.jpg", []),
            ("jpeg-422", "jpeg/baseline-422-odd-restart.jpg", []),
            ("jpeg-444", "jpeg/baseline-444-detail.jpg", ["--jpeg_reconstruction_cfl=0"]),
            ("jpeg-gray", "jpeg/progressive-gray-odd-refinement.jpg", []),
        })
        {
            var encoded = CjxlFile(c, CorpusDriver.CorpusDir / jpeg, "{" + jpeg + "}", ["--lossless_jpeg=1", .. options]);
            AddLossy(c, name, Function, encoded, 8, new Obj { ["source"] = jpeg },
                notes: "A JPEG file recompressed by cjxl: the frame is YCbCr with the quantization tables of the JPEG file. The data that rebuild the JPEG file (jbrd) are read by JpegXlTranscoder, not by a load.");
            if (name == "jpeg-420")
            {
                // The same file with the reconstruction data after the codestream: no encoder writes that, jxl-oxide
                // rebuilds the JPEG file from it and djxl does not
                var boxes = SplitBoxes(encoded.Data);
                byte[] moved = [.. boxes.Where(box => box.Type != "jbrd").SelectMany(box => box.Bytes), .. boxes.Where(box => box.Type == "jbrd").SelectMany(box => box.Bytes)];
                Py.Assert(moved.Length == encoded.Data.Length && !moved.AsSpan().SequenceEqual(encoded.Data), "jpeg-420 has reconstruction data before the end of its codestream");
                AddLossy(c, "jpeg-420-reconstruction-after", Function, new Encoded(moved, encoded.Commands), 8,
                    new Obj { ["source"] = jpeg, ["layout"] = "the boxes of jxl/jpeg-420, the jbrd box moved after the last jxlp box" },
                    notes: "A JPEG file recompressed by cjxl, whose reconstruction data (jbrd) were moved after the codestream: JpegXlTranscoder rebuilds the JPEG file all the same.", origin: "hand-authored");
            }
        }

        // Splines, which cjxl does not write
        static string Coefficients(params double[] values) => string.Join(' ', Enumerable.Range(0, 32).Select(i => (i < values.Length ? values[i] : 0).ToString(CultureInfo.InvariantCulture)));
        var splines = FromTree(c, $"""
            Width 64
            Height 48
            Bitdepth 8
            XYB
            Spline
            {Coefficients(0.005, 0.002)}
            {Coefficients(0.5, 0.2, -0.1)}
            {Coefficients(0.4, -0.2)}
            {Coefficients(3.0, 1.0, 0.5)}
            5 8 25 38 55 15 45 45
            EndSpline
            - Set 40
            """);
        AddLossy(c, "lossy-splines", Function, splines, 8, new Obj { ["width"] = 64, ["height"] = 48 }, origin: "hand-authored",
            notes: "A spline drawn on a flat XYB image. The library evaluates the error function accurately, the reference decoders approximate it: the samples differ by up to half a percent along the spline.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Animations
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildAnimations(Corpus<JxlTools> c)
    {
        const string Function = nameof(BuildAnimations);
        static Img Flat(int width, int height, Func<int, int, int> value) => new(width, height, "rgb", 8, CommonCorpus.Grid(width, height, (x, y) => new Px(value(x, y), value(x, y), value(x, y))));

        void AddTree(string name, string tree, IReadOnlyList<Img> frames, string description, string notes)
        {
            var encoded = FromTree(c, tree);
            AddValid(c, name, Function, encoded, frames, PatternReference(description), CrossChecks(c, encoded.Data, frames, lossless: true), new Expectation { LoopCount = 0 },
                origin: "hand-authored", notes: notes);
        }

        AddTree("anim-replace", """
            Width 8
            Height 6
            Bitdepth 8
            Animation
            AnimationFPS 10 1
            Duration 2
            NotLast
            - Set 30
            Duration 5
            NotLast
            - Set 120
            Duration 1
            - Set 210
            """, [Flat(8, 6, (_, _) => 30), Flat(8, 6, (_, _) => 120), Flat(8, 6, (_, _) => 210)], "Each frame sets every sample: 30, 120, then 210.",
            "Three full frames at 10 ticks per second, of 2, 5 and 1 ticks.");
        AddTree("anim-ntsc", """
            Width 6
            Height 4
            Bitdepth 8
            Animation
            AnimationFPS 30000 1001
            Duration 1
            NotLast
            - Set 60
            Duration 2
            - Set 180
            """, [Flat(6, 4, (_, _) => 60), Flat(6, 4, (_, _) => 180)], "Each frame sets every sample: 60, then 180.",
            "30000/1001 ticks per second: the durations are 1001/30000 s and 1001/15000 s, exactly.");
        AddTree("anim-layers", """
            Width 4
            Height 4
            Bitdepth 8
            Animation
            AnimationFPS 10 1
            Duration 3
            NotLast
            - Set 100
            Width 2
            Height 2
            FramePos 0 0
            BlendMode kAdd
            Duration 0
            NotLast
            - Set 20
            FramePos 2 2
            BlendMode kReplace
            Duration 5
            - Set 200
            """, [Flat(4, 4, (_, _) => 100), Flat(4, 4, (x, y) => x < 2 && y < 2 ? 120 : x >= 2 && y >= 2 ? 200 : 100)],
            "The first frame sets 100. A frame of 2x2 samples at (0, 0) adds 20 and has no duration: it is a layer of the next frame, which replaces the 2x2 samples at (2, 2) with 200.",
            "Three coded frames, two shown: a frame without duration is composed with the one that follows it, and its pixels count toward the total without being a frame.");
        AddTree("anim-multiply", """
            Width 6
            Height 4
            Bitdepth 8
            Animation
            AnimationFPS 4 1
            Duration 1
            NotLast
            if x > 2
              - Set 255
              - Set 102
            BlendMode kMul
            Duration 1
            - Set 153
            """, [Flat(6, 4, (x, _) => x > 2 ? 255 : 102), Flat(6, 4, (x, _) => x > 2 ? 153 : 61)],
            "The first frame sets 255 where x > 2 and 102 elsewhere; the second multiplies it by 153/255: 153 and 61 (102 * 153 / 255 = 61.2).",
            "The second frame is multiplied with the first one, which stays in a reference slot.");

        // An animation with a finite play count and an alpha channel, converted from an APNG file whose frames replace the image
        Img[] frames = [CommonCorpus.PatternCornerMarkers(10, 8), CommonCorpus.PatternRgbaAdam7(10, 8), CommonCorpus.PatternCornerMarkers(10, 8)];
        var apng = CommonCorpus.ApngFile(10, 8, 3, [new ApngFrame(frames[0], 0, 0, 1, 10, "none", "source"), new ApngFrame(frames[1], 0, 0, 3, 20, "none", "source"), new ApngFrame(frames[2], 0, 0, 1, 4, "none", "source")]);
        var source = c.Scratch / "source.png";
        File.WriteAllBytes(source, apng);
        var written = CjxlFile(c, source, "{source}.png", ["-d", "0"]);
        var (patched, loopOffset) = SetLoopCount(written.Data, 3);
        var converted = new Encoded(patched, [.. written.Commands, $"num_loops of the image header set to 3 (selector 1 and three bits, in place of selector 0) at bit {CommonCorpus.Str(loopOffset)}; the frames are copied"]);
        AddValid(c, "anim-alpha-three-plays", Function, converted, frames, PatternReference("The frames of the APNG source, each of which replaces the image: PatternCornerMarkers, PatternRgbaAdam7, PatternCornerMarkers."),
            CrossChecks(c, converted.Data, frames, lossless: true), new Expectation { LoopCount = 3 }, new Obj { ["delays"] = "1/10, 3/20, 1/4", ["plays"] = 3 },
            notes: "Played three times: no tool of libjxl writes a play count, so the field of the image header is rewritten. The encoder stores the frames as it sees fit (cropped, blended), the shown frames are those of the source.");
    }

    /// <summary>
    /// Rewrites the <c>num_loops</c> field of a bare codestream that repeats forever: the field becomes the selector 1 and
    /// three bits, the rest of the image header moves by three bits, and the frames, which start on a byte, are copied.
    /// </summary>
    private static (byte[] Data, long BitOffset) SetLoopCount(byte[] codestream, int loops)
    {
        Py.Assert(loops is >= 1 and <= 7, "a play count of three bits");
        var file = ReferenceJpegXl.Parse(codestream);
        Py.Assert(!file.IsContainer && file.LoopCount == 0 && file.LoopCountBitOffset is not null && file.HeaderEndBitOffset is not null, "a bare codestream that repeats forever");
        var field = file.LoopCountBitOffset!.Value;
        var end = file.HeaderEndBitOffset!.Value;
        var bits = new List<bool>();
        for (long i = 0; i < field; i++)
            bits.Add(Bit(i));

        // U32: the selector 1 (two bits, least significant first), then the value on three bits
        bits.AddRange([true, false, (loops & 1) != 0, (loops & 2) != 0, (loops & 4) != 0]);
        Py.Assert(!Bit(field) && !Bit(field + 1), "num_loops is stored with the selector 0");
        for (var i = field + 2; i < end; i++)
            bits.Add(Bit(i));

        while (bits.Count % 8 != 0)
            bits.Add(false);

        var header = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
                header[i / 8] |= (byte)(1 << (i % 8));
        }

        return ([.. header, .. codestream.AsSpan((int)((end + 7) / 8))], field);

        bool Bit(long index) => ((codestream[index / 8] >> (int)(index % 8)) & 1) != 0;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Files that are not decoded
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildInvalid(Corpus<JxlTools> c)
    {
        var codestream = Input(c, "lossless-rgb8");
        var features = (IList<string>)c.Fixtures.First(f => (string)f["id"]! == "jxl/lossless-rgb8")["features"]!;

        void Add(string name, byte[] data, string notes, Obj? parameters = null)
        {
            // Both reference decoders reject the file too: the defect is not a matter of interpretation
            var path = c.Scratch / "invalid.jxl";
            File.WriteAllBytes(path, data);
            Py.Assert(IsRejected([c.Tools.Djxl, path.Value, (c.Scratch / "invalid.ppm").Value, "--quiet", "--num_threads=0"]), $"djxl accepts {name}");
            Py.Assert(IsRejected([c.Tools.JxlOxide, "decode", path.Value, "-o", (c.Scratch / "invalid.png").Value, "-j", "1"]), $"jxl-oxide accepts {name}");
            c.AddError("invalid/jxl/" + name, "invalid", "invalid/jxl/" + name + ".jxl", data, "jxl",
                c.Provenance("hand-authored", nameof(BuildInvalid), parameters, [$"libjxl {c.Tools.LibjxlVersion} (djxl, confirms the defect)", $"jxl-oxide {c.Tools.JxlOxideVersion} (confirms the defect)", ToolSet.RuntimeLabel]),
                new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "JpegXl" }, features: features, notes: notes);
        }

        // The first pair of bytes whose inversion both decoders reject, from the start of the frame (its header and its table
        // of contents) and from the middle of the codestream (the entropy-coded samples)
        int FirstRejected(int start) => Enumerable.Range(start, codestream.Length - start - 2).First(i =>
        {
            var path = c.Scratch / "probe.jxl";
            File.WriteAllBytes(path, Invert(codestream, i));
            return IsRejected([c.Tools.Djxl, path.Value, (c.Scratch / "probe.ppm").Value, "--quiet", "--num_threads=0"])
                && IsRejected([c.Tools.JxlOxide, "decode", path.Value, "-o", (c.Scratch / "probe.png").Value, "-j", "1"]);
        });
        var headerOffset = FirstRejected(12);
        Add("frame-header-corrupt", Invert(codestream, headerOffset), "Two bytes at the start of the frame of jxl/lossless-rgb8 are inverted: its header and its table of contents are not valid.",
            new Obj { ["offset"] = headerOffset });
        var dataOffset = FirstRejected(codestream.Length / 2);
        Add("frame-data-corrupt", Invert(codestream, dataOffset), "Two bytes of the coded samples of jxl/lossless-rgb8 are inverted: the entropy-coded stream is not valid.",
            new Obj { ["offset"] = dataOffset });
        Add("codestream-signature", [.. ContainerStart, .. Box("jxlc", [0xFF, 0x0B, .. codestream.AsSpan(2)])], "The codestream of the jxlc box does not start with FF 0A.");
        Add("container-without-codestream", [.. ContainerStart, .. Box("Exif", [0, 0, 0, 0, .. CommonCorpus.ExifTiff(littleEndian: true, orientation: 1)])],
            "The container has an Exif box and no codestream.");
        Add("container-two-codestreams", [.. ContainerStart, .. Box("jxlc", codestream), .. Box("jxlc", codestream)], "The container has two jxlc boxes.");
        Add("container-parts-out-of-order", [.. ContainerStart, .. Part(1, codestream.AsSpan(0, 10)), .. Part(0x80000000, codestream.AsSpan(10))],
            "The first jxlp box has the index 1 and the last one the index 0.");
        Add("container-box-size", [.. ContainerStart, 0, 0, 0, 4, .. "jxlc"u8, .. codestream], "The jxlc box declares a size of 4 bytes, less than its own header.");

        static byte[] Invert(byte[] data, int offset)
        {
            var copy = (byte[])data.Clone();
            copy[offset] ^= 0xFF;
            copy[offset + 1] ^= 0xFF;
            return copy;
        }
    }

    private static void BuildUnsupported(Corpus<JxlTools> c)
    {
        void Add(string name, Encoded encoded, string feature, string notes, Func<ReferenceJpegXl, bool> isAsIntended)
        {
            var file = ReferenceJpegXl.Parse(encoded.Data);
            Py.Assert(isAsIntended(file), $"invalid/jxl/{name} does not have the feature it is named after");
            c.AddError("invalid/jxl/" + name, "unsupported", "invalid/jxl/" + name + ".jxl", encoded.Data, "jxl",
                c.Provenance("generated", nameof(BuildUnsupported), null, ToolList(c), encoded.Commands),
                new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "JpegXl", ["feature"] = feature }, features: Features(file), notes: notes);
        }

        var rgb = CommonCorpus.PatternRgbOdd(13, 9);

        // A PFM file: three 32-bit floating-point samples per pixel, rows from the bottom
        var pfm = new ByteBuilder();
        foreach (var value in Encoding.ASCII.GetBytes("PF\n13 9\n-1.0\n"))
            pfm.U8(value);
        Span<byte> sample = stackalloc byte[4];
        for (var y = rgb.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < rgb.Width; x++)
            {
                foreach (var value in rgb.Pixels[(y * rgb.Width) + x])
                {
                    BinaryPrimitives.WriteSingleLittleEndian(sample, value / 255f);
                    foreach (var b in sample)
                        pfm.U8(b);
                }
            }
        }

        var source = c.Scratch / "source.pfm";
        File.WriteAllBytes(source, pfm.ToArray());
        Add("float-samples", CjxlFile(c, source, "{source}.pfm", ["-d", "0"]), "floating-point samples", "32-bit floating-point samples: an image of the library holds integers of 8 or 16 bits.", file => file.HasFloatSamples);
        Add("premultiplied-alpha", Cjxl(c, CommonCorpus.PatternCornerMarkers(12, 9), ["-d", "0", "--premultiply=1"]), "premultiplied alpha",
            "The color channels are premultiplied by the alpha channel: an image of the library holds straight alpha.", file => file.ExtraChannels is [{ Type: 0, IsAlphaAssociated: true }]);
        Add("pq-white-point-e", Cjxl(c, rgb, ["-d", "0", "-x", "color_space=RGB_0.3333;0.3333_202_Rel_PeQ"]), "PQ or HLG with a custom color space",
            "The PQ transfer function with the white point E: neither code points nor the parametric curve of an ICC profile describe it.", file => file is { WhitePoint: 10, TransferFunction: 16, Gamma: null });
        var profile = File.ReadAllBytes(CorpusDriver.CorpusDir / "icc" / "profiles" / "ProPhoto-v4.icc");
        Add("xyb-icc", Cjxl(c, CommonCorpus.PatternDetailColor(32, 24, 3), ["-d", "1"], files: [("icc_pathname", profile, "{icc/profiles/ProPhoto-v4.icc}")]), "XYB image with a color profile other than sRGB",
            "A lossy image whose samples are to be converted from XYB to the color space of an ICC profile that is not sRGB.", file => file is { IsXybEncoded: true, HasIccProfile: true });
    }

    private static void BuildLimits(Corpus<JxlTools> c)
    {
        void Add(string name, string sourceName, Obj limits, string limitKind, string notes)
        {
            var source = c.Fixtures.First(f => (string)f["id"]! == "jxl/" + sourceName);
            var input = (Obj)source["input"]!;
            c.AddError("limit/jxl/" + name, "limit", null, null, "jxl", c.Provenance("hand-authored", nameof(BuildLimits), null, [ToolSet.RuntimeLabel]),
                new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = limitKind }, features: source["features"] as IList<string>,
                decodeOptions: new Obj { ["limits"] = limits }, existingInput: new Obj { ["path"] = input["path"], ["sha256"] = input["sha256"] }, notes: notes);
        }

        Add("width-over-limit", "lossless-rgb8", new Obj { ["MaxWidth"] = 12 }, "Width", "The image is 13 pixels wide.");
        Add("height-over-limit", "lossless-rgb8", new Obj { ["MaxHeight"] = 8 }, "Height", "The image is 9 pixels high.");
        Add("frame-pixels-over-limit", "lossless-rgb8", new Obj { ["MaxFramePixels"] = 116 }, "FramePixels", "The image has 117 pixels.");
        Add("frames-over-limit", "anim-replace", new Obj { ["MaxFrames"] = 2 }, "Frames", "The animation has three frames.");
        Add("total-pixels-hidden-layer", "anim-layers", new Obj { ["MaxTotalPixels"] = 35 }, "TotalPixels",
            "Two frames of 16 pixels are shown and the layer between them has 4 pixels: 36 pixels are decoded.");
        Add("metadata-bytes-over-limit", "metadata-exif-xmp", new Obj { ["MaxMetadataBytes"] = 64 }, "MetadataBytes", "The EXIF and XMP payloads are larger than 64 bytes together.");
    }
}
