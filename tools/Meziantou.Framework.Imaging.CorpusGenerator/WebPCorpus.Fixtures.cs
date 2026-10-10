using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

internal static partial class WebPCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static List<string> ToolList(Corpus<WebPTools> c, params string[] extra) => [c.Tools.LibwebpLabel + " (cwebp)", .. extra, ToolSet.RuntimeLabel];

    private static Obj StillExpected(Img img, string pixelFormat, string colorModel) => new()
    {
        ["format"] = "webp",
        ["width"] = img.Width,
        ["height"] = img.Height,
        ["pixelFormat"] = pixelFormat,
        ["colorModel"] = colorModel,
        ["bitsPerComponent"] = 8,
        ["orientation"] = 1,
        ["iccProfile"] = "none",
        ["animation"] = null,
    };

    /// <summary>Alpha ramp with fully transparent pixels whose (hidden) colors are defined and distinct: lossless output must
    /// preserve them exactly (cwebp -exact).</summary>
    private static Img PatternAlphaHidden(int w, int h)
    {
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var alpha = new[] { 0, 1, 64, 128, 254, 255 }[(x + 2 * y) % 6];
                pixels.Add(new Px((x * 41 + 9) % 256, (y * 67 + 30 + x) % 256, (x * y * 23 + 200) % 256, alpha));
            }
        }

        return CommonCorpus.Named(new Img(w, h, "rgba", 8, pixels), "PatternAlphaHidden");
    }

    /// <summary>At most 256 colors (color indexing transform; 2, 4 and 16 colors are bundled several pixels per green sample).</summary>
    private static Img PatternPalette(int w, int h, IReadOnlyList<Px> colors)
    {
        var rng = new PyRandom(colors.Count * 7919 + w);
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
                pixels.Add(colors[(x * 3 + y * 5 + rng.RandInt(0, colors.Count - 1)) % colors.Count]);
        }

        (pixels[0], pixels[^1]) = (colors[0], colors[^1]);
        return CommonCorpus.Named(new Img(w, h, "rgba", 8, pixels), "PatternPalette");
    }

    /// <summary>Photographic-like content for lossless fixtures: smooth color gradients, a sinusoidal wave, sharp edges and
    /// mild seeded noise (every transform of a lossless encoder is useful; channels differ everywhere).</summary>
    private static Img PatternPhoto(int w, int h, int seed)
    {
        var rng = new PyRandom(seed);
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var edge = (x / 6 + y / 5) % 2 != 0 ? 60 : 0;
                var r = Py.Round(30 + 150.0 * x / Math.Max(1, w - 1)) + edge + rng.RandInt(-3, 3);
                var g = Py.Round(200 - 120.0 * y / Math.Max(1, h - 1)) + rng.RandInt(-3, 3);
                var b = Py.Round(120 + 70 * Math.Sin(x * 0.35) * Math.Cos(y * 0.3)) + rng.RandInt(-3, 3);
                pixels.Add(new Px(CommonCorpus.Clamp8(r), CommonCorpus.Clamp8(g), CommonCorpus.Clamp8(b)));
            }
        }

        return CommonCorpus.Named(new Img(w, h, "rgb", 8, pixels), "PatternPhoto");
    }

    /// <summary>Lossy content with an alpha gradient, a transparent band and opaque areas (ALPH chunk).</summary>
    private static Img PatternAlphaDetail(int w, int h, int seed)
    {
        var color = CommonCorpus.PatternDetailColor(w, h, seed);
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var alpha = x < 2 ? 0 : x >= w - 3 ? 255 : CommonCorpus.Clamp8(Py.Round(255.0 * (x - 2) / (w - 5)) + (y % 3) * 7);
                pixels.Add(color.Pixels[y * w + x].Append(alpha));
            }
        }

        return CommonCorpus.Named(new Img(w, h, "rgba", 8, pixels), "PatternAlphaDetail");
    }

    /// <summary>The image chunks of a container built around the cwebp output, and the commands (or descriptions) that
    /// produced it.</summary>
    private delegate (byte[] Data, List<string> Commands) ContainerBuilder(byte[] data);

    /// <summary>A losslessly encoded pattern: the pattern is the reference (exact), cross-checked with dwebp (libwebp) and FFmpeg.</summary>
    private static void LosslessStill(Corpus<WebPTools> c, string fixtureId, Img img, string[] cwebpArgs, string pixelFormat, List<string> layouts, string function,
        Obj parameters, ContainerBuilder? container = null, string? notes = null, int orientation = 1, string icc = "none", IReadOnlyList<string>? required = null)
    {
        var name = fixtureId.Replace('/', '-');
        var (data, command) = Cwebp(c, img, ["-lossless", "-exact", .. cwebpArgs], name);
        var commands = new List<string> { command };
        if (container is not null)
        {
            (data, var extra) = container(data);
            commands.AddRange(extra);
        }

        var (features, info) = WebPInspect(data);
        Py.Assert(features.Contains("webp.bitstream=vp8l") && info.Canvas == (img.Width, img.Height));
        Py.Assert((required ?? []).All(features.Contains), fixtureId);
        var reference = ToRgba(img);
        var (decoded, dcommand) = Dwebp(c, data, name);
        var (ff, fcommand) = FfmpegStill(c, data, name, img.Width, img.Height);
        List<Obj> checks =
        [
            CheckRecord($"{c.Tools.LibwebpLabel} dwebp (VP8L decoder)", dcommand, reference, decoded, "dwebp differs from the pattern."),
            CheckRecord($"{c.Tools.FfmpegLabel} (libavcodec webp decoder, VP8L)", fcommand, reference, ff, "FFmpeg differs from the pattern."),
        ];
        Py.Assert((string?)checks[0]["result"] == "exact", fixtureId);
        var colorModel = features.Contains("webp.alpha=vp8l") ? "Rgba" : "Rgb";
        Py.Assert((pixelFormat == "Rgba32") == (colorModel == "Rgba"));
        var expected = StillExpected(img, pixelFormat, colorModel);
        expected["orientation"] = orientation;
        expected["iccProfile"] = icc;
        var frame = !layouts.Contains("rgb8") ? img : ToRgb(img);
        c.AddValid(fixtureId, fixtureId + ".webp", data, [frame], expected,
            c.Provenance("generated", "BuildLossless " + function, parameters,
                ToolList(c, c.Tools.LibwebpLabel + " (dwebp, cross-check only)", c.Tools.FfmpegLabel + " (cross-check only)"), commands),
            CommonCorpus.HandReference("The pattern itself: encoded losslessly by cwebp -lossless -exact (every sample, including the " +
                                       "colors of fully transparent pixels, is preserved).", checks),
            features, layouts: layouts, notes: notes);
    }

    private static Obj LossyPolicy(string fixtureId, IEnumerable<Obj> checks)
    {
        var (maxError, meanError, justifiedMax, justifiedMean) = CommonCorpus.JpegJustifiedTolerance(checks);
        if (!LibraryWebPMeasurements.TryGetValue(fixtureId, out var measured))
        {
            return new Obj
            {
                ["mode"] = "tolerance",
                ["maxAbsoluteError"] = justifiedMax,
                ["maxMeanAbsoluteError"] = justifiedMean,
                ["justification"] =
                    "Lossy VP8 compared with an independent decoding of the same encoded input (libwebp dwebp). The VP8 " +
                    "reconstruction is exactly specified (RFC 6386); decoders differ in the YUV->RGB conversion and chroma " +
                    "upsampling, which WebP does not specify; measured disagreement with the reference (see reference.crossChecks): " +
                    $"max {CommonCorpus.Str(maxError)}, mean {Py.FormatFixed(meanError, 4)}. Allowed: measured max + 1 and 1.5 x measured mean + 0.1 (floors 2 and 0.5, rounded up to " +
                    "0.01). Alpha is compared exactly, and the policy is verified to reject channel swaps and flips of this " +
                    "reference. The library decoder must measure its own output and may only tighten this policy.",
            };
        }

        var (libraryMax, libraryMean) = measured;
        var allowedMax = Math.Min(justifiedMax, Math.Max(1, libraryMax));
        var allowedMean = Math.Min(justifiedMean, Math.Ceiling(Py.Round((libraryMean + 0.01) * 100, 6)) / 100);
        return new Obj
        {
            ["mode"] = "tolerance",
            ["maxAbsoluteError"] = allowedMax,
            ["maxMeanAbsoluteError"] = allowedMean,
            ["justification"] =
                "Lossy VP8 compared with an independent decoding of the same encoded input (libwebp dwebp: RFC 6386 " +
                "reconstruction, fancy chroma upsampling, 14-bit fixed-point BT.601 YUV->RGB). The library decoder (" +
                "the same exact VP8 reconstruction, centered bilinear 9-3-3-1 chroma upsampling, " +
                "16-bit fixed-point BT.601 conversion rounded to nearest) differs from it only by the rounding of the color " +
                $"conversion; measured: max {CommonCorpus.Str(libraryMax)}, mean {Py.FormatFixed(libraryMean, 4)}. Allowed: the measured max (at least 1) and the measured mean + 0.01 " +
                "(rounded up to 0.01), tighter than the disagreement between independent decoders (see reference.crossChecks: " +
                $"max {CommonCorpus.Str(maxError)}, mean {Py.FormatFixed(meanError, 4)}, which would justify max {CommonCorpus.Str(justifiedMax)} / mean {Py.FormatFixed(justifiedMean, 2)}). Alpha is compared exactly, and the policy is " +
                "verified to reject channel swaps and flips of this reference.",
        };
    }

    /// <summary>A lossy encoding: the reference is the dwebp decoding of the same input, cross-checked with FFmpeg's VP8 decoder.</summary>
    private static void LossyStill(Corpus<WebPTools> c, string fixtureId, Img img, string[] cwebpArgs, string function, Obj parameters, ContainerBuilder? container = null,
        string? notes = null, int orientation = 1, string icc = "none")
    {
        var name = fixtureId.Replace('/', '-');
        var (data, command) = Cwebp(c, img, cwebpArgs, name);
        var commands = new List<string> { command };
        if (container is not null)
        {
            (data, var extra) = container(data);
            commands.AddRange(extra);
        }

        var (features, _) = WebPInspect(data);
        Py.Assert(features.Contains("webp.bitstream=vp8"));
        var (reference, dcommand) = Dwebp(c, data, name);
        var hasAlpha = features.Contains("webp.alpha=alph");
        var checks = LossyCrossChecks(c, data, name, reference, hasAlpha, "");
        Py.Assert((string?)checks[0]["result"] is "differs" or "exact" && (int)checks[0]["maxAbsoluteError"]! <= 4, fixtureId);
        if ((string?)checks[0]["result"] == "exact")
            throw new InvalidOperationException($"{fixtureId}: no disagreement measured, a tolerance cannot be justified");
        var expected = StillExpected(img, hasAlpha ? "Rgba32" : "Rgb24", "YCbCr");
        expected["orientation"] = orientation;
        expected["iccProfile"] = icc;
        List<string> layouts = hasAlpha ? ["rgba8"] : ["rgba8", "rgb8"];
        var frame = hasAlpha ? reference : ToRgb(reference);
        c.AddValid(fixtureId, fixtureId + ".webp", data, [frame], expected,
            c.Provenance("generated", "BuildLossy " + function, parameters,
                ToolList(c, c.Tools.LibwebpLabel + " (dwebp)", c.Tools.FfmpegLabel + " (cross-check only)"), commands),
            new Obj
            {
                ["method"] = "decoded",
                ["description"] = "Decoded from the same encoded input by libwebp dwebp (default fancy chroma upsampling, straight RGBA).",
                ["decoder"] = $"{c.Tools.LibwebpLabel} dwebp",
                ["backend"] = $"{c.Tools.LibwebpLabel} (VP8 decoder of RFC 6386, fancy upsampling, 14-bit fixed-point BT.601 YUV->RGB, ALPH decoder)",
                ["command"] = dcommand,
                ["crossChecks"] = checks,
            },
            features, comparison: LossyPolicy(fixtureId, checks), layouts: layouts, notes: notes);
    }

    private static (byte[] Icc, byte[] Exif, byte[] Xmp) MetadataPayloads()
    {
        var icc = CommonCorpus.IccProfile("RGB ");
        var exif = CommonCorpus.ExifTiff(littleEndian: false, 6, pixelSize: (6, 4), software: "Lorem ipsum");
        var xmp = Bytes.Utf8("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF " +
                             "xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\" " +
                             "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>Lorem ipsum</dc:title></rdf:Description></rdf:RDF>" +
                             "</x:xmpmeta><?xpacket end=\"r\"?>");
        return (icc, exif, xmp);
    }

    private static void BuildLossless(Corpus<WebPTools> c)
    {
        LosslessStill(c, "webp/lossless-rgba-corner-markers", CommonCorpus.PatternCornerMarkers(5, 4), ["-z", "6"], "Rgba32", ["rgba8"],
            "PatternCornerMarkers", new Obj { ["width"] = 5, ["height"] = 4 });
        LosslessStill(c, "webp/lossless-rgb-odd-width", CommonCorpus.PatternRgbOdd(7, 3), ["-z", "6"], "Rgb24", ["rgba8", "rgb8"],
            "PatternRgbOdd", new Obj { ["width"] = 7, ["height"] = 3 });
        LosslessStill(c, "webp/lossless-alpha-hidden-colors", PatternAlphaHidden(6, 3), ["-z", "9"], "Rgba32", ["rgba8"],
            "PatternAlphaHidden", new Obj { ["width"] = 6, ["height"] = 3 },
            notes: "Fully transparent pixels keep defined, distinct colors: exact lossless decoding preserves them.");
        Px[] four = [new(255, 0, 0, 255), new(0, 128, 255, 255), new(20, 200, 40, 255), new(250, 250, 5, 0)];
        LosslessStill(c, "webp/lossless-palette-bundled", PatternPalette(9, 5, four), ["-z", "6"], "Rgba32", ["rgba8"],
            "PatternPalette", new Obj { ["width"] = 9, ["height"] = 5, ["colors"] = 4 }, required: ["webp.vp8l.transform=color-indexing"],
            notes: "Four colors: color indexing transform with four 2-bit indexes per green sample (odd width: a partial last bundle).");
        var rng = new PyRandom(256);
        var many = new List<Px>();
        for (var i = 0; i < 200; i++)
        {
            int r = rng.RandInt(0, 255), g = rng.RandInt(0, 255), b = rng.RandInt(0, 255);
            many.Add(new Px(r, g, b, 255));
        }

        LosslessStill(c, "webp/lossless-palette-large", PatternPalette(17, 13, many), ["-z", "6"], "Rgb24", ["rgba8", "rgb8"],
            "PatternPalette", new Obj { ["width"] = 17, ["height"] = 13, ["colors"] = 200, ["seed"] = 256 }, required: ["webp.vp8l.transform=color-indexing"],
            notes: "200 colors: color indexing transform without bundling (one index per green sample).");
        var detail = PatternPhoto(40, 24, 9001);
        LosslessStill(c, "webp/lossless-detail-transforms", detail, ["-z", "9"], "Rgb24", ["rgba8", "rgb8"],
            "PatternPhoto", new Obj { ["width"] = 40, ["height"] = 24, ["seed"] = 9001 }, required: ["webp.vp8l.transform=predictor"],
            notes: "Photographic-like content: predictor, cross-color and subtract-green transforms, LZ77 and color cache.");
        LosslessStill(c, "webp/lossless-1x1", new Img(1, 1, "rgba", 8, [new Px(10, 200, 30, 128)]), [], "Rgba32", ["rgba8"],
            "literal", new Obj { ["width"] = 1, ["height"] = 1, ["pixel"] = new List<object?> { 10, 200, 30, 128 } });

        var (icc, exif, xmp) = MetadataPayloads();

        (byte[] Data, List<string> Commands) ExtendedMetadata(byte[] data)
        {
            // Hand-assembled extended layout (WebP container specification, chunk order): VP8X, ICCP, image, EXIF, XMP
            var image = BitstreamChunks(data);
            var output = Riff([Vp8x(6, 4, icc: true, alpha: true, exif: true, xmp: true), Chunk("ICCP", icc), .. image.Select(f => Chunk(f.FourCC, f.Payload)),
                Chunk("EXIF", exif), Chunk("XMP ", xmp)]);
            return (output, ["(container assembled by the generator: VP8X, ICCP, VP8L from cwebp, EXIF (TIFF, no prefix), XMP)"]);
        }

        LosslessStill(c, "webp/lossless-extended-metadata", CommonCorpus.PatternCornerMarkers(6, 4), ["-z", "6"], "Rgba32", ["rgba8"],
            "PatternCornerMarkers", new Obj { ["width"] = 6, ["height"] = 4 }, container: ExtendedMetadata, orientation: 6, icc: "rgb",
            notes: "Extended layout with an RGB ICC profile, big-endian EXIF (orientation 6, metadata only: stored pixels are " +
                   "never rotated) and XMP; the EXIF chunk starts with the TIFF header as the specification requires.");

        static (byte[] Data, List<string> Commands) ExtendedOpaque(byte[] data)
        {
            var image = BitstreamChunks(data);
            var output = Riff([Vp8x(7, 3), .. image.Select(f => Chunk(f.FourCC, f.Payload)), Chunk("LOR ", Bytes.Ascii("lorem ipsum"))]);
            return (output, ["(container assembled by the generator: VP8X without flags, VP8L from cwebp, an unknown 'LOR ' chunk)"]);
        }

        LosslessStill(c, "webp/lossless-extended-opaque-unknown-chunk", CommonCorpus.PatternRgbOdd(7, 3), ["-z", "6"], "Rgb24", ["rgba8", "rgb8"],
            "PatternRgbOdd", new Obj { ["width"] = 7, ["height"] = 3 }, container: ExtendedOpaque,
            notes: "Extended layout without alpha or metadata flags, followed by an unknown chunk that decoders must skip.");
    }

    private static void BuildLossy(Corpus<WebPTools> c)
    {
        LossyStill(c, "webp/lossy-smooth-odd", CommonCorpus.PatternSmoothColor(17, 11), ["-q", "80", "-m", "4"],
            "PatternSmoothColor", new Obj { ["width"] = 17, ["height"] = 11 });
        LossyStill(c, "webp/lossy-detail-normal-filter", CommonCorpus.PatternDetailColor(32, 24, 4242),
            ["-q", "60", "-m", "6", "-strong", "-sharpness", "3", "-segments", "4", "-sns", "80"],
            "PatternDetailColor", new Obj { ["width"] = 32, ["height"] = 24, ["seed"] = 4242 },
            notes: "Segmentation with four segments, the normal (strong) loop filter with sharpness 3.");
        LossyStill(c, "webp/lossy-detail-simple-filter", CommonCorpus.PatternDetailColor(23, 19, 77), ["-q", "35", "-m", "2", "-nostrong", "-segments", "1"],
            "PatternDetailColor", new Obj { ["width"] = 23, ["height"] = 19, ["seed"] = 77 },
            notes: "The simple loop filter, odd dimensions (partial macroblocks, odd chroma planes).");
        LossyStill(c, "webp/lossy-no-filter-high-quality", CommonCorpus.PatternDetailColor(16, 16, 31337), ["-q", "95", "-f", "0", "-m", "3"],
            "PatternDetailColor", new Obj { ["width"] = 16, ["height"] = 16, ["seed"] = 31337 },
            notes: "Loop filter level 0 (no filtering), fine quantization: one macroblock.");
        LossyStill(c, "webp/lossy-tiny-3x2", CommonCorpus.PatternRgbOdd(3, 2), ["-q", "75"], "PatternRgbOdd", new Obj { ["width"] = 3, ["height"] = 2 },
            notes: "A 3x2 image: one partial macroblock, 2x1 chroma planes.");
        LossyStill(c, "webp/lossy-alpha-compressed", PatternAlphaDetail(18, 10, 5), ["-q", "70", "-alpha_method", "1", "-alpha_filter", "best", "-exact"],
            "PatternAlphaDetail", new Obj { ["width"] = 18, ["height"] = 10, ["seed"] = 5 },
            notes: "Lossy color with a losslessly compressed alpha plane (ALPH, VP8L image stream with a prediction filter).");
        LossyStill(c, "webp/lossy-alpha-raw", PatternAlphaDetail(14, 9, 6), ["-q", "50", "-alpha_method", "0", "-alpha_filter", "fast", "-exact"],
            "PatternAlphaDetail", new Obj { ["width"] = 14, ["height"] = 9, ["seed"] = 6 },
            notes: "Lossy color with an uncompressed, filtered alpha plane (ALPH compression 0).");

        var (icc, _, xmp) = MetadataPayloads();

        (byte[] Data, List<string> Commands) WebpmuxMetadata(byte[] data)
        {
            // webpmux assembles the extended layout; the EXIF payload keeps the "Exif\0\0" prefix some writers emit
            var source = c.Scratch / "lossy-metadata.source.webp";
            File.WriteAllBytes(source, data);
            (string Kind, byte[] Payload)[] files = [("icc", icc), ("exif", Bytes.Concat(Bytes.Ascii("Exif\0\0"), CommonCorpus.ExifTiff(littleEndian: true, 3))), ("xmp", xmp)];
            var commands = new List<string>();
            var current = source;
            for (var index = 0; index < files.Length; index++)
            {
                var (kind, payload) = files[index];
                var payloadPath = c.Scratch / ("lossy-metadata." + kind);
                File.WriteAllBytes(payloadPath, payload);
                var output = c.Scratch / ("lossy-metadata." + CommonCorpus.Str(index) + ".webp");
                List<string> command = [c.Tools.Webpmux, "-set", kind, payloadPath.Value, current.Value, "-o", output.Value];
                Proc.Run(command);
                commands.Add(Display(command, c.Tools, [(payloadPath.Value, "{" + kind + "}"), (current.Value, "{input}.webp"), (output.Value, "{output}.webp")]));
                current = output;
            }

            return (File.ReadAllBytes(current), commands);
        }

        LossyStill(c, "webp/lossy-metadata", CommonCorpus.PatternSmoothColor(12, 8), ["-q", "85"], "PatternSmoothColor", new Obj { ["width"] = 12, ["height"] = 8 },
            container: WebpmuxMetadata, orientation: 3, icc: "rgb",
            notes: "Extended layout written by webpmux: RGB ICC profile, little-endian EXIF (orientation 3) stored with the " +
                   "optional 'Exif\\0\\0' prefix (decoders strip it), XMP.");
    }

    /// <summary>Encodes one frame rectangle with cwebp and returns its image chunks (VP8L, or ALPH + VP8) and the command.</summary>
    private static (List<byte[]> Chunks, string Command, byte[] Data) FramePayload(Corpus<WebPTools> c, Img img, bool lossless, string name, IEnumerable<string> args)
    {
        var (data, command) = Cwebp(c, img, [.. (lossless ? ["-lossless", "-exact", "-z", "6"] : new[] { "-q", "70", "-exact" }), .. args], name);
        return ([.. BitstreamChunks(data).Select(f => Chunk(f.FourCC, f.Payload))], command, data);
    }

    private static Img LiteralFrame(int width, int height, int seed, int[] alphaValues)
    {
        var rng = new PyRandom(seed);
        var pixels = new List<Px>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int r = rng.RandInt(0, 255), g = rng.RandInt(0, 255), b = rng.RandInt(0, 255);
                pixels.Add(new Px(r, g, b, alphaValues[(x + y * 3 + seed) % alphaValues.Length]));
            }
        }

        return new Img(width, height, "rgba", 8, pixels);
    }

    /// <summary>Assembles an animation (VP8X, ANIM, ANMF frames with cwebp payloads), composes the expected canvases, and
    /// cross-checks them with anim_dump.</summary>
    private static void Animated(Corpus<WebPTools> c, string fixtureId, (int Width, int Height) canvas, List<AnimFrame> frames, int loopCount, Px background, string notes,
        string function, bool lossy = false)
    {
        var name = fixtureId.Replace('/', '-');
        var chunks = new List<byte[]> { Vp8x(canvas.Width, canvas.Height, alpha: true, animation: true), Anim(background, loopCount) };
        var commands = new List<string>();
        var references = new List<(int Index, string Command, byte[] Still)>();
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var frameName = name + "-" + CommonCorpus.Str(index);
            var (payload, command, still) = FramePayload(c, frame.Source, frame.Lossless, frameName, frame.Args);
            commands.Add(command);
            if (frame.Lossless)
            {
                frame.Img = frame.Source;
            }
            else
            {
                // The lossy rectangle as decoded by libwebp: the VP8 reconstruction is exact, the RGB conversion is libwebp's
                (frame.Img, var dcommand) = Dwebp(c, still, frameName);
                references.Add((index, dcommand, still));
            }

            chunks.Add(Anmf(frame.X, frame.Y, frame.Source.Width, frame.Source.Height, frame.Duration, frame.Blend, frame.Dispose, payload));
        }

        var data = Riff(chunks);
        var (features, info) = WebPInspect(data);
        Py.Assert(info.Frames.Select(f => f.Duration).SequenceEqual(frames.Select(f => f.Duration)));
        var expectedFrames = Compose(canvas.Width, canvas.Height, frames);
        var blended = BlendedPixels(canvas.Width, frames);
        var checks = new List<Obj> { AnimCheck(c, data, name, expectedFrames, blended, lossyTolerance: lossy ? 2 : 0) };
        var ffCommand = Display(c.Tools.FfmpegCmd("-i", "{input}", "-f", "rawvideo", "-pix_fmt", "rgba", "-"), c.Tools, []);
        var decodedByFfmpeg = false;
        try
        {
            var path = c.Scratch / (name + ".ffmpeg.webp");
            File.WriteAllBytes(path, data);
            CommonCorpus.FfmpegDecode(c.Tools, path, "rgba", canvas.Width, canvas.Height, 4, true);
            decodedByFfmpeg = true;
        }
        catch (ToolException)
        {
            checks.Add(new Obj
            {
                ["decoder"] = $"{c.Tools.FfmpegLabel} (libavcodec webp decoder)",
                ["command"] = ffCommand,
                ["result"] = "differs",
                ["notes"] = $"FFmpeg {c.Tools.FfmpegVersion} does not decode animated WebP (ANIM/ANMF chunks: 'image data not found'); it is no " +
                            "independent animation reference.",
            });
        }

        if (decodedByFfmpeg)
            throw new InvalidOperationException($"FFmpeg {c.Tools.FfmpegVersion} now decodes animated WebP: compare its frames instead of recording a failure.");

        var durations = frames.Select(f => CommonCorpus.Normalize(f.Duration, 1000)).ToList();
        var encodings = frames.Select(f => (string?)string.Format(CultureInfo.InvariantCulture, "ANMF {0} at ({1},{2}) {3}x{4}, {5}, {6}, {7} ms",
            f.Lossless ? "VP8L" : f.Source.Pixels.Any(p => p[3] < 255) ? "VP8+ALPH" : "VP8",
            f.X, f.Y, f.Source.Width, f.Source.Height,
            f.Blend ? "alpha-blend" : "no-blend",
            f.Dispose ? "dispose background" : "no dispose", f.Duration)).ToList();
        var expected = new Obj
        {
            ["format"] = "webp",
            ["width"] = canvas.Width,
            ["height"] = canvas.Height,
            ["pixelFormat"] = "Rgba32",
            ["colorModel"] = "Rgba",
            ["bitsPerComponent"] = 8,
            ["orientation"] = 1,
            ["iccProfile"] = "none",
            ["animation"] = new Obj { ["totalPlays"] = loopCount == 0 ? null : loopCount, ["encodedLoopValue"] = loopCount },
        };
        var parameters = new Obj
        {
            ["canvas"] = new List<object?> { canvas.Width, canvas.Height },
            ["loopCount"] = loopCount,
            ["background"] = background.Select(v => (object?)v).ToList(),
            ["frames"] = frames.Select(f => (object?)new Obj
            {
                ["x"] = f.X,
                ["y"] = f.Y,
                ["width"] = f.Source.Width,
                ["height"] = f.Source.Height,
                ["duration"] = f.Duration,
                ["blend"] = f.Blend,
                ["dispose"] = f.Dispose,
                ["lossless"] = f.Lossless,
                ["pattern"] = f.Pattern,
            }).ToList(),
        };
        var tools = ToolList(c, c.Tools.LibwebpLabel + " (anim_dump, cross-check only)", c.Tools.FfmpegLabel + " (cross-check only)");
        Obj reference;
        Obj? comparison;
        if (lossy)
        {
            tools.Insert(1, c.Tools.LibwebpLabel + " (dwebp)");
            reference = new Obj
            {
                ["method"] = "decoded",
                ["description"] = "Lossless frame rectangles are the hand-defined patterns; lossy rectangles are decoded by libwebp " +
                                  "dwebp from a still WebP holding the same ALPH/VP8 chunks (same bitstream); the canvases are composed " +
                                  "by the generator from the WebP container specification with the exact OVER rounding of " +
                                  "the library.",
                ["decoder"] = $"{c.Tools.LibwebpLabel} dwebp (lossy rectangles) + generator compositing",
                ["backend"] = $"{c.Tools.LibwebpLabel} (VP8 decoder of RFC 6386, fancy upsampling, 14-bit fixed-point BT.601 YUV->RGB, ALPH decoder)",
                ["command"] = string.Join("; ", references.Select(r => $"frame {CommonCorpus.Str(r.Index)}: {r.Command}")),
                ["crossChecks"] = checks,
            };
            foreach (var (index, _, still) in references)
            {
                var img = frames[index].Img!;
                var alpha = frames[index].Source.Pixels.Any(p => p[3] < 255);
                checks.AddRange(LossyCrossChecks(c, still, name + "-" + CommonCorpus.Str(index), img, alpha,
                    $" (frame {CommonCorpus.Str(index)} rectangle decoded as a still image with the same chunks)"));
            }

            comparison = LossyPolicy(fixtureId, checks.Where(check => check.ContainsKey("maxAbsoluteError") && ((string)check["decoder"]!).Contains("rectangle", StringComparison.Ordinal)));
        }
        else
        {
            reference = CommonCorpus.HandReference(
                "Frame rectangles are hand-defined patterns encoded losslessly (cwebp -lossless -exact); the canvases are composed by " +
                "the generator from the WebP container specification (disposal clears to transparent black, the ANIM background " +
                "color is never painted, the first frame is drawn with SOURCE) with the exact OVER rounding of the library.",
                checks);
            comparison = null;
        }

        c.AddValid(fixtureId, fixtureId + ".webp", data, expectedFrames, expected,
            c.Provenance("generated", function, parameters, tools, commands),
            reference, features, comparison: comparison, durations: durations, frameEncodings: encodings,
            encodedDelays: [.. frames.Select(f => (object?)new Obj { ["milliseconds"] = f.Duration })], notes: notes);
    }

    private static void BuildAnimations(Corpus<WebPTools> c)
    {
        int[] opaque = [255];
        int[] translucent = [255, 128, 64, 200, 0];
        Animated(c, "webp/anim-lossless-blend-dispose", (8, 6),
        [
            new() { X = 0, Y = 0, Source = LiteralFrame(8, 6, 1, opaque), Lossless = true, Blend = true, Dispose = false, Duration = 100, Pattern = "LiteralFrame seed 1, opaque" },
            new() { X = 2, Y = 2, Source = LiteralFrame(4, 3, 2, translucent), Lossless = true, Blend = true, Dispose = true, Duration = 7, Pattern = "LiteralFrame seed 2, translucent" },
            new() { X = 0, Y = 0, Source = LiteralFrame(3, 3, 3, [255, 90]), Lossless = true, Blend = true, Dispose = false, Duration = 0, Pattern = "LiteralFrame seed 3" },
            new() { X = 4, Y = 0, Source = LiteralFrame(4, 6, 4, [0, 255, 0, 30]), Lossless = true, Blend = false, Dispose = false, Duration = 655350, Pattern = "LiteralFrame seed 4, holes" },
            new() { X = 2, Y = 4, Source = LiteralFrame(5, 2, 5, [128, 255, 1]), Lossless = true, Blend = true, Dispose = true, Duration = 1000, Pattern = "LiteralFrame seed 5" },
        ], 3, new Px(255, 0, 255, 255), "Alpha blending over opaque, translucent and transparent canvas pixels; no-blend replacement creating " +
            "transparent holes with hidden colors; disposal to background of partial rectangles; durations 100, 7, 0, 655350 " +
            "(655.35 s, also exact in APNG and GIF delays) and 1000 ms; loop count 3 (three plays); an opaque magenta ANIM background that is never painted.",
            "BuildAnimations");
        Animated(c, "webp/anim-lossless-partial-first-infinite", (6, 4),
        [
            new() { X = 2, Y = 2, Source = LiteralFrame(3, 2, 11, [255, 0, 77]), Lossless = true, Blend = true, Dispose = false, Duration = 40, Pattern = "LiteralFrame seed 11" },
            new() { X = 0, Y = 0, Source = LiteralFrame(6, 4, 12, [0]), Lossless = true, Blend = false, Dispose = true, Duration = 40, Pattern = "LiteralFrame seed 12, fully transparent" },
            new() { X = 0, Y = 0, Source = LiteralFrame(2, 2, 13, [255, 180]), Lossless = true, Blend = true, Dispose = false, Duration = 33, Pattern = "LiteralFrame seed 13" },
        ], 0, new Px(0, 0, 0, 0), "A partial first frame (the rest of the canvas stays transparent black), a full-canvas fully transparent " +
            "no-blend frame with hidden colors that is disposed, then a frame drawn over cleared pixels; infinite loop (0).",
            "BuildAnimations");
        Animated(c, "webp/anim-single-frame", (5, 3),
        [
            new() { X = 0, Y = 0, Source = LiteralFrame(5, 3, 21, [255, 40]), Lossless = true, Blend = false, Dispose = false, Duration = 250, Pattern = "LiteralFrame seed 21" },
        ], 2, new Px(255, 255, 255, 255), "An animation with one frame (still animated: one displayed frame, two plays).", "BuildAnimations");
        Animated(c, "webp/anim-mixed-lossy", (16, 12),
        [
            new() { X = 0, Y = 0, Source = LiteralFrame(16, 12, 31, [255]), Lossless = true, Blend = false, Dispose = false, Duration = 50, Pattern = "LiteralFrame seed 31" },
            new() { X = 4, Y = 2, Source = CommonCorpus.Named(ToRgba(CommonCorpus.PatternDetailColor(8, 8, 32)), "PatternDetailColor"), Lossless = false, Blend = false, Dispose = false, Duration = 60, Pattern = "PatternDetailColor seed 32, opaque" },
            new() { X = 0, Y = 6, Source = PatternAlphaDetail(10, 6, 33), Lossless = false, Blend = true, Dispose = true, Duration = 70, Pattern = "PatternAlphaDetail seed 33" },
            new() { X = 8, Y = 0, Source = LiteralFrame(8, 7, 34, [255, 128, 0]), Lossless = true, Blend = true, Dispose = false, Duration = 80, Pattern = "LiteralFrame seed 34" },
        ], 1, new Px(0, 0, 0, 255), "Mixed payloads: lossless (VP8L) and lossy (VP8, VP8 + ALPH) frames, an opaque lossy no-blend rectangle, a " +
            "translucent lossy rectangle blended then disposed, a translucent lossless rectangle blended over lossy pixels; one play.",
            "BuildAnimations", lossy: true);
    }

    private static byte[] Corrupt(byte[] data, int offset, int value) =>
        Bytes.Concat(Bytes.Slice(data, 0, offset), [checked((byte)value)], Bytes.Slice(data, offset + 1));

    private static int FindChunk(byte[] data, string fourcc, int start = 12)
    {
        var offset = start;
        var expected = Bytes.Latin1(fourcc);
        while (offset < data.Length)
        {
            if (Bytes.Equal(Bytes.Slice(data, offset, offset + 4), expected))
                return offset;
            var size = (int)UnpackU32LE(data, offset + 4);
            offset += 8 + size + (size & 1);
        }

        throw new KeyNotFoundException(fourcc);
    }

    private static bool DwebpFails(Corpus<WebPTools> c, byte[] data, string name)
    {
        var path = c.Scratch / (name + ".webp");
        File.WriteAllBytes(path, data);
        var result = Proc.Exec([c.Tools.Dwebp, "-quiet", "-pam", path.Value, "-o", (c.Scratch / (name + ".pam")).Value]);
        return result.ExitCode != 0;
    }

    private static void BuildInvalid(Corpus<WebPTools> c)
    {
        var byId = c.Fixtures.ToDictionary(f => (string)f["id"]!, StringComparer.Ordinal);

        byte[] Read(string fixtureId) => File.ReadAllBytes(c.Out / (string)((Obj)byId[fixtureId]["input"]!)["path"]!);

        void Add(string fixtureId, byte[] data, string notes, Obj? parameters = null, string kind = "invalid", string exception = "InvalidImageContentException",
            string? feature = null, bool checkDwebp = true)
        {
            var name = fixtureId.Replace('/', '-');
            if (checkDwebp)
                Py.Assert(DwebpFails(c, data, name), $"{fixtureId}: dwebp (libwebp) accepts the input; the defect is not independently confirmed");
            var error = new Obj { ["exception"] = exception, ["format"] = "WebP" };
            if (!string.IsNullOrEmpty(feature))
                error["feature"] = feature;
            c.AddError(fixtureId, kind, fixtureId + ".webp", data, "webp",
                c.Provenance("hand-authored", "BuildInvalid", parameters, [c.Tools.LibwebpLabel + " (dwebp, confirms the defect)", ToolSet.RuntimeLabel]),
                error, features: WebPInspectLenient(data), notes: notes);
        }

        var detail = Read("webp/lossless-detail-transforms");
        Add("invalid/webp/lossless-truncated", Bytes.Slice(detail, 0, detail.Length / 2),
            "The file ends in the middle of the VP8L chunk (the RIFF and chunk sizes declare the full length): truncated input is " +
            "malformed data, never a clean end of input.", new Obj { ["keptBytes"] = detail.Length / 2, ["fileBytes"] = detail.Length });
        var vp8l = FindChunk(detail, "VP8L");
        Add("invalid/webp/lossless-bad-signature", Corrupt(detail, vp8l + 8, 0x2E), "The VP8L signature byte is 0x2E instead of 0x2F.",
            new Obj { ["offset"] = vp8l + 8, ["value"] = "0x2E" });
        Add("invalid/webp/lossless-bad-version", Corrupt(detail, vp8l + 12, detail[vp8l + 12] | 0x20),
            "The VP8L version field (3 bits after the alpha hint) is 1; only version 0 is defined.", new Obj { ["offset"] = vp8l + 12 });

        // A complete, valid container whose VP8L entropy-coded data is inconsistent (a prefix code that is not a complete code)
        (int Offset, int Mask, byte[] Candidate)? FirstRejectedCorruption()
        {
            for (var offset = vp8l + 14; offset < vp8l + 60; offset++)
            {
                foreach (var mask in new[] { 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80 })
                {
                    var candidate = Corrupt(detail, offset, detail[offset] ^ mask);
                    if (DwebpFails(c, candidate, "probe"))
                        return (offset, mask, candidate);
                }
            }

            return null;
        }

        var corrupted = FirstRejectedCorruption() ?? throw new InvalidOperationException("Assertion failed: no single-bit VP8L corruption rejected by dwebp");
        Add("invalid/webp/lossless-corrupt-entropy-code", corrupted.Candidate,
            "One bit of the VP8L prefix-code data is flipped (the first single-bit corruption, scanning from the start of the " +
            "entropy-coded data, that libwebp rejects): the container is valid, only decoding the image stream finds the defect.",
            new Obj { ["offset"] = corrupted.Offset, ["xorMask"] = corrupted.Mask });

        var lossy = Read("webp/lossy-detail-normal-filter");
        var vp8 = FindChunk(lossy, "VP8 ");
        Add("invalid/webp/lossy-bad-start-code", Corrupt(lossy, vp8 + 8 + 3, 0x9C), "The VP8 key frame start code is 9C 01 2A instead of 9D 01 2A.",
            new Obj { ["offset"] = vp8 + 11 });
        Add("invalid/webp/lossy-interframe", Corrupt(lossy, vp8 + 8, lossy[vp8 + 8] | 0x01),
            "The VP8 frame tag declares an interframe: WebP stores only key frames, so the library reports the unsupported VP8 feature.",
            new Obj { ["offset"] = vp8 + 8 }, kind: "unsupported", exception: "UnsupportedImageFeatureException");
        var payload = Bytes.Slice(lossy, vp8 + 8);
        var size = UnpackU32LE(lossy, vp8 + 4);
        var firstPartition = (payload[0] | (payload[1] << 8) | (payload[2] << 16)) >> 5;
        var keep = 10 + firstPartition + 4;
        var truncatedPayload = Bytes.Slice(payload, 0, keep);
        var truncated = Riff([Chunk("VP8 ", truncatedPayload)]);
        Add("invalid/webp/lossy-truncated-partition", truncated,
            "A valid container whose VP8 chunk keeps only 4 bytes of the token partition (the chunk size is consistent): only " +
            "decoding the macroblocks finds the missing data.", new Obj { ["vp8Bytes"] = keep, ["originalVp8Bytes"] = size });
        var tag = payload[0] | (payload[1] << 8) | (payload[2] << 16);
        var huge = tag | (0x7FFFF << 5);
        var overflow = Bytes.Concat(Bytes.Slice(lossy, 0, vp8 + 8), [(byte)(huge & 0xFF), (byte)((huge >> 8) & 0xFF), (byte)((huge >> 16) & 0xFF)], Bytes.Slice(lossy, vp8 + 11));
        Add("invalid/webp/lossy-first-partition-overflow", overflow,
            "The VP8 frame tag declares a first partition of 524287 bytes, larger than the chunk.", new Obj { ["firstPartitionSize"] = 0x7FFFF });

        var alpha = Read("webp/lossy-alpha-compressed");
        var alph = FindChunk(alpha, "ALPH");
        Add("invalid/webp/lossy-alpha-bad-compression", Corrupt(alpha, alph + 8, (alpha[alph + 8] & ~3) | 2),
            "The ALPH header declares compression method 2 (only 0, uncompressed, and 1, lossless, exist).", new Obj { ["offset"] = alph + 8 },
            checkDwebp: false);

        var (icc, _, _) = MetadataPayloads();
        Add("invalid/webp/extended-missing-image", Riff([Vp8x(4, 4, icc: true), Chunk("ICCP", icc)]),
            "An extended-layout file with an ICC profile but no image chunk.");
        var corner = Read("webp/lossless-rgba-corner-markers");
        var image = BitstreamChunks(corner);
        Add("invalid/webp/extended-canvas-mismatch", Riff([Vp8x(6, 4, alpha: true), .. image.Select(f => Chunk(f.FourCC, f.Payload))]),
            "The VP8X canvas is 6x4 but the VP8L bitstream is 5x4.");
        Add("invalid/webp/riff-size-exceeds-file", Bytes.Concat(Bytes.Slice(corner, 0, 4), new ByteBuilder().U32LE(corner.Length - 8 + 100).ToArray(), Bytes.Slice(corner, 8)),
            "The RIFF header declares 100 bytes more than the file contains.", checkDwebp: false);
        Add("invalid/webp/chunk-size-exceeds-riff", Bytes.Concat(Bytes.Slice(corner, 0, 16), new ByteBuilder().U32LE(corner.Length).ToArray(), Bytes.Slice(corner, 20)),
            "The VP8L chunk size extends past the end of the RIFF payload.");

        var animData = Read("webp/anim-lossless-blend-dispose");
        var chunks = RiffChunks(animData);
        var frames = Enumerable.Range(0, chunks.Count).Where(i => chunks[i].FourCC == "ANMF").ToList();
        var second = (byte[])chunks[frames[1]].Payload.Clone();
        ToBytes3(3).CopyTo(second, 0); // x = 6 with a 4-pixel-wide rectangle on an 8-pixel canvas
        var rebuilt = Riff(chunks.Select((f, i) => Chunk(f.FourCC, i == frames[1] ? second : f.Payload)));
        Add("invalid/webp/anim-frame-outside-canvas", rebuilt,
            "The second ANMF rectangle (x = 6, width 4) extends past the 8-pixel-wide canvas.", new Obj { ["frame"] = 1, ["x"] = 6 });
        var noAnim = Riff(chunks.Where(f => f.FourCC != "ANIM").Select(f => Chunk(f.FourCC, f.Payload)));
        Add("invalid/webp/anim-missing-anim-chunk", noAnim, "The VP8X animation flag is set and ANMF frames follow, but the ANIM chunk is missing.",
            checkDwebp: false);
        Add("invalid/webp/anim-truncated", Bytes.Slice(animData, 0, animData.Length - 40),
            "The file ends inside the last ANMF frame (sizes declare the full length).", new Obj { ["removedBytes"] = 40 }, checkDwebp: false);
    }

    private static List<string>? WebPInspectLenient(byte[] data)
    {
        try
        {
            return WebPInspect(data).Features;
        }
#pragma warning disable CA1031 // malformed inputs: features are best effort (any parsing failure means "no features")
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static void BuildLimits(Corpus<WebPTools> c)
    {
        var byId = c.Fixtures.ToDictionary(f => (string)f["id"]!, StringComparer.Ordinal);

        void Add(string fixtureId, string sourceId, Obj limits, string limitKind, string notes)
        {
            var source = byId[sourceId];
            var input = (Obj)source["input"]!;
            c.AddError(fixtureId, "limit", null, null, "webp",
                c.Provenance("hand-authored", "BuildLimits", null, [ToolSet.RuntimeLabel]),
                new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = limitKind },
                features: source.TryGetValue("features", out var features) ? (IList<string>?)features : null, decodeOptions: new Obj { ["limits"] = limits },
                existingInput: new Obj { ["path"] = input["path"], ["sha256"] = input["sha256"] }, notes: notes);
        }

        Add("limit/webp/lossless-width-over-limit", "webp/lossless-rgba-corner-markers", new Obj { ["MaxWidth"] = 4 }, "Width",
            "Reuses the 5x4 webp/lossless-rgba-corner-markers input with MaxWidth = 4 (checked from the VP8L header).");
        Add("limit/webp/lossy-frame-pixels-over-limit", "webp/lossy-smooth-odd", new Obj { ["MaxFramePixels"] = 186 }, "FramePixels",
            "Reuses the 17x11 (187 pixels) webp/lossy-smooth-odd input with MaxFramePixels = 186 (checked from the VP8 frame header).");
        Add("limit/webp/anim-frames-over-limit", "webp/anim-lossless-blend-dispose", new Obj { ["MaxFrames"] = 4 }, "Frames",
            "Reuses the 5-frame webp/anim-lossless-blend-dispose input with MaxFrames = 4: the limit is a safety bound, never a " +
            "successful truncation.");
        Add("limit/webp/anim-total-pixels-over-limit", "webp/anim-lossless-blend-dispose", new Obj { ["MaxTotalPixels"] = 239 }, "TotalPixels",
            "Reuses webp/anim-lossless-blend-dispose (5 full-canvas 8x6 displayed frames, 240 pixels) with MaxTotalPixels = 239: " +
            "displayed frames are charged with the full canvas when produced, so only decoding reaches the limit.");
        var meta = byId["webp/lossless-extended-metadata"];
        var iccLength = Convert.ToInt32(((Obj)((Obj)((Obj)meta["expected"]!)["profiles"]!)["icc"]!)["length"], CultureInfo.InvariantCulture);
        Add("limit/webp/lossless-metadata-over-limit", "webp/lossless-extended-metadata", new Obj { ["MaxMetadataBytes"] = iccLength - 1 }, "MetadataBytes",
            $"Reuses webp/lossless-extended-metadata with MaxMetadataBytes one byte below its ICC profile ({CommonCorpus.Str(iccLength)} bytes).");
    }
}
