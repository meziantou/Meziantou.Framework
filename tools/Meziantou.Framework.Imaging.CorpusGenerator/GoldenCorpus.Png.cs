using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>A PNG fixture encoded by FFmpeg: the pattern, its expected pixel format and the native layouts cross-checked.</summary>
internal sealed record PngFfmpegCase(string Id, Img Img, string PixelFormat, string ColorModel, int Bits, bool Interlaced, string[] ExtraLayouts, string Function, Obj Parameters);

/// <summary>An APNG fixture encoded by FFmpeg from full-canvas frames.</summary>
internal sealed record ApngFfmpegCase(string Id, int Depth, List<Img> Frames, int Plays, string Rate);

/// <summary>Why an independent decoder disagrees with an APNG reference: "rounding", "frames" (only the listed frames
/// differ), "failure" or "invalid".</summary>
internal sealed record ApngExplanation(string Kind, string Text, int[]? Frames = null);

// PNG and APNG fixtures
internal static partial class GoldenCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // PNG fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildPngFfmpeg(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        PngFfmpegCase[] cases =
        [
            // id, image, pixelFormat, colorModel, bits, interlaced, extra layouts, function/parameters
            new("png/rgba8-corner-markers", CommonCorpus.PatternCornerMarkers(5, 4), "Rgba32", "Rgba", 8, false, [], "PatternCornerMarkers", new Obj { ["width"] = 5, ["height"] = 4 }),
            new("png/rgb8-odd-width", CommonCorpus.PatternRgbOdd(7, 3), "Rgb24", "Rgb", 8, false, ["rgb8"], "PatternRgbOdd", new Obj { ["width"] = 7, ["height"] = 3 }),
            new("png/gray8-checkerboard", CommonCorpus.PatternCheckerboardGray(9, 9), "Gray8", "Grayscale", 8, false, ["gray8"], "PatternCheckerboardGray", new Obj { ["width"] = 9, ["height"] = 9, ["low"] = 32, ["high"] = 224 }),
            new("png/gray16-low-bit-gradient", CommonCorpus.PatternGray16LowBits(8, 2), "Gray16", "Grayscale", 16, false, ["gray16le"], "PatternGray16LowBits", new Obj { ["width"] = 8, ["height"] = 2 }),
            new("png/rgba16-low-bit-gradient", CommonCorpus.PatternRgba16LowBits(4, 4), "Rgba64", "Rgba", 16, false, [], "PatternRgba16LowBits", new Obj { ["width"] = 4, ["height"] = 4 }),
            new("png/graya8-alpha-ramp", CommonCorpus.PatternGrayAlphaRamp(8, 2), "Rgba32", "GrayscaleAlpha", 8, false, [], "PatternGrayAlphaRamp", new Obj { ["width"] = 8, ["height"] = 2 }),
            new("png/single-pixel", new Img(1, 1, "rgba", 8, [new Px(12, 34, 56, 78)]), "Rgba32", "Rgba", 8, false, [], "literal", new Obj { ["pixel"] = new List<int> { 12, 34, 56, 78 } }),
            new("png/rgba8-adam7", CommonCorpus.PatternRgbaAdam7(9, 9), "Rgba32", "Rgba", 8, true, [], "PatternRgbaAdam7", new Obj { ["width"] = 9, ["height"] = 9 }),
            new("png/rgb16-adam7-empty-passes", CommonCorpus.PatternRgb16(3, 2), "Rgba64", "Rgb", 16, true, [], "PatternRgb16", new Obj { ["width"] = 3, ["height"] = 2 }),
            // Remaining color type/bit depth combinations and Adam7 edge cases
            new("png/graya16-low-bit-alpha", CommonCorpus.PatternGraya16LowBits(5, 3), "Rgba64", "GrayscaleAlpha", 16, false, [], "PatternGraya16LowBits", new Obj { ["width"] = 5, ["height"] = 3 }),
            new("png/rgb16-odd-width", CommonCorpus.PatternRgb16(5, 3), "Rgba64", "Rgb", 16, false, [], "PatternRgb16", new Obj { ["width"] = 5, ["height"] = 3 }),
            new("png/rgba16-adam7-odd", CommonCorpus.PatternRgba16LowBits(5, 7), "Rgba64", "Rgba", 16, true, [], "PatternRgba16LowBits", new Obj { ["width"] = 5, ["height"] = 7 }),
            new("png/gray8-adam7-1x9", CommonCorpus.PatternGrayColumn(1, 9), "Gray8", "Grayscale", 8, true, ["gray8"], "PatternGrayColumn", new Obj { ["width"] = 1, ["height"] = 9 }),
            new("png/rgb8-adam7-9x1", CommonCorpus.PatternRgbOdd(9, 1), "Rgb24", "Rgb", 8, true, ["rgb8"], "PatternRgbOdd", new Obj { ["width"] = 9, ["height"] = 1 }),
        ];
        foreach (var (fixtureId, img, pixelFormat, colorModel, bits, interlaced, extraLayouts, function, parameters) in cases)
        {
            var name = fixtureId + ".png";
            var path = c.Scratch / (fixtureId.Replace('/', '_') + ".png");
            var command = CommonCorpus.FfmpegEncodeStill(t, img, path, interlaced ? ["-flags", "+ildct"] : []);
            var data = File.ReadAllBytes(path);
            var (info, features) = CommonCorpus.PngInspect(data);
            Py.Assert(info.Interlace == (interlaced ? 1 : 0), fixtureId + ": FFmpeg did not honor the interlace request");
            var layout = CommonCorpus.CanonicalLayout(img);
            var (check, _) = c.FfmpegCrossCheck(path, [img], layout, animated: false, "png");
            var nativeChecks = new List<Obj>();
            foreach (var native in extraLayouts)
            {
                var (fmt, bpp) = native switch
                {
                    "gray8" => ("gray", 1),
                    "gray16le" => ("gray16le", 2),
                    "rgb8" => ("rgb24", 3),
                    _ => throw new InvalidOperationException(native),
                };
                var (decoded, cmd) = CommonCorpus.FfmpegDecode(t, path, fmt, img.Width, img.Height, bpp, animated: false);
                nativeChecks.Add(new Obj
                {
                    ["decoder"] = $"{t.FfmpegLabel} (libavcodec png decoder, native {fmt})",
                    ["command"] = CommonCorpus.Display(cmd, t, [(path.Value, "{input}")]),
                    ["result"] = decoded.Count == 1 && decoded[0].AsSpan().SequenceEqual(img.Raw(native)) ? "exact" : "differs",
                });
            }

            c.AddValid(
                fixtureId, name, data, [img], CommonCorpus.StillExpected("png", img, pixelFormat, colorModel, bits),
                c.Provenance("generated", function, parameters, [t.FfmpegLabel, ToolSet.RuntimeLabel],
                    [CommonCorpus.Display(command, t, [(CommonCorpus.WithSuffix(path, ".source.raw").Value, "{source}.raw"), (path.Value, "{input}")])]),
                CommonCorpus.HandReference("Lossless PNG encoding (FFmpeg) of the hand-defined pattern; the expected pixels are the pattern itself.",
                    [check, .. nativeChecks]),
                features, layouts: [layout, .. extraLayouts]);
        }
    }

    private static byte[] PaletteBytes(IEnumerable<Px> palette) => [.. palette.SelectMany(p => p).Select(v => (byte)v)];

    private static byte[] IntBytes(IEnumerable<int> values) => [.. values.Select(v => checked((byte)v))];

    private static void BuildPngHand(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        List<string> tools = [t.FfmpegLabel + " (cross-check only)", ToolSet.RuntimeLabel];

        void Add(string fixtureId, byte[] data, Img img, string pixelFormat, string colorModel, int bits, string function, Obj parameters, IReadOnlyList<string>? layouts = null,
            string? notes = null, int orientation = 1, string icc = "none", string? ffmpegNotes = null)
        {
            var path = c.Scratch / (fixtureId.Replace('/', '_') + ".png");
            File.WriteAllBytes(path, data);
            var (_, features) = CommonCorpus.PngInspect(data);
            var layout = CommonCorpus.CanonicalLayout(img);
            var (check, _) = c.FfmpegCrossCheck(path, [img], layout, animated: false, "png");
            var checks = new List<Obj> { check };
            if ((string)check["result"]! != "exact")
            {
                // Disagreements are recorded and arbitrated by a second independent decoder, never hidden
                Py.Assert(!string.IsNullOrEmpty(ffmpegNotes), fixtureId + ": FFmpeg disagrees with the reference; explain why (ffmpeg_notes)");
                check["notes"] = ffmpegNotes;
                var sips = SipsCrossCheck(c, path, img);
                Py.Assert((string)sips["result"]! == "exact", fixtureId + ": Apple ImageIO disagrees with the reference too");
                checks.Add(sips);
            }

            var expected = CommonCorpus.StillExpected("png", img, pixelFormat, colorModel, bits, orientation);
            expected["iccProfile"] = icc;
            c.AddValid(fixtureId, fixtureId + ".png", data, [img],
                expected,
                c.Provenance("hand-authored", function, parameters, [.. tools, .. (checks.Count > 1 ? [$"Apple ImageIO {t.MacOS} {t.SipsVersion} (cross-check only)"] : Array.Empty<string>())]),
                CommonCorpus.HandReference("Hand-authored PNG (stored deflate blocks, filter types cycling 0-4); expected pixels follow the PNG specification sample scaling.", checks),
                features, layouts: layouts is { Count: > 0 } ? layouts : [layout], notes: notes);
        }

        // 1-bit grayscale, odd width (sub-byte packing with padding bits)
        var w = 5;
        var h = 3;
        var bits1 = CommonCorpus.Grid(w, h, (x, y) => y < 2 ? new Px((x + y) % 2) : new Px(1));
        bits1[0] = new Px(1);
        var img = new Img(w, h, "gray", 8, bits1.Select(v => new Px(v[0] * 255)));
        Add("png/gray1-odd-width", CommonCorpus.PngFile(w, h, 1, 0, bits1), img, "Gray8", "Grayscale", 1, "PngFile gray1",
            new Obj { ["width"] = w, ["height"] = h, ["samples"] = "checkerboard rows 0-1, white row 2, (0,0)=1" }, ["rgba8", "gray8"]);

        // 2-bit grayscale, interlaced with empty passes and odd dimensions
        (w, h) = (7, 5);
        var bits2 = CommonCorpus.Grid(w, h, (x, y) => new Px((x + 2 * y) % 4));
        img = new Img(w, h, "gray", 8, bits2.Select(v => new Px(v[0] * 85)));
        Add("png/gray2-adam7", CommonCorpus.PngFile(w, h, 2, 0, bits2, interlace: true), img, "Gray8", "Grayscale", 2,
            "PngFile gray2 adam7", new Obj { ["width"] = w, ["height"] = h, ["samples"] = "(x + 2y) mod 4" }, ["rgba8", "gray8"]);

        // 4-bit grayscale, split IDAT including an empty IDAT chunk
        (w, h) = (5, 3);
        var bits4 = CommonCorpus.Grid(w, h, (x, y) => new Px((x * 3 + y * 5) % 16));
        img = new Img(w, h, "gray", 8, bits4.Select(v => new Px(v[0] * 17)));
        Add("png/gray4-split-idat", CommonCorpus.PngFile(w, h, 4, 0, bits4, idatSplits: 3, emptyIdat: true), img, "Gray8", "Grayscale", 4,
            "PngFile gray4", new Obj { ["width"] = w, ["height"] = h, ["samples"] = "(3x + 5y) mod 16", ["idatChunks"] = "3 + 1 empty" }, ["rgba8", "gray8"]);

        // 4-bit palette with partial tRNS (entries without alpha are opaque)
        List<Px> palette = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 0), new(0, 255, 255), new(255, 0, 255), new(128, 64, 32),
            new(32, 64, 128), new(200, 100, 50), new(10, 20, 30), new(250, 240, 230), new(90, 180, 45)];
        List<int> trns = [0, 64, 128, 200, 255];
        (w, h) = (5, 3);
        var idx = CommonCorpus.Grid(w, h, (x, y) => new Px((x + y * 5) % 12));
        img = new Img(w, h, "rgba", 8, idx.Select(i => palette[i[0]].Append(i[0] < trns.Count ? trns[i[0]] : 255)));
        var plte = CommonCorpus.PngChunk("PLTE", PaletteBytes(palette));
        Add("png/palette4-trns", CommonCorpus.PngFile(w, h, 4, 3, idx, beforeIdat: [plte, CommonCorpus.PngChunk("tRNS", IntBytes(trns))]), img,
            "Rgba32", "Indexed", 4, "PngFile palette4 tRNS", new Obj { ["width"] = w, ["height"] = h, ["palette"] = palette, ["trns"] = trns });

        // 8-bit palette using all 256 entries (seeded colors)
        var rng = new PyRandom(35);
        palette = [];
        for (var i = 0; i < 256; i++)
        {
            var r = rng.RandRange(256);
            var g = rng.RandRange(256);
            var b = rng.RandRange(256);
            palette.Add(new Px(r, g, b));
        }

        (w, h) = (16, 16);
        idx = CommonCorpus.Grid(w, h, (x, y) => new Px((y * 16 + x) * 7 % 256));
        img = new Img(w, h, "rgba", 8, idx.Select(i => palette[i[0]].Append(255)));
        plte = CommonCorpus.PngChunk("PLTE", PaletteBytes(palette));
        Add("png/palette8-256-colors", CommonCorpus.PngFile(w, h, 8, 3, idx, beforeIdat: [plte]), img, "Rgba32", "Indexed", 8,
            "PngFile palette8", new Obj { ["width"] = w, ["height"] = h, ["paletteSeed"] = 35, ["index"] = "(16y + x) * 7 mod 256" });

        // 8-bit grayscale with a tRNS key: the key becomes transparent but keeps its gray value
        (w, h) = (4, 2);
        var gray = CommonCorpus.Grid(w, h, (x, y) => new Px((x + 4 * y) * 50 % 256));
        var key = 100;
        img = new Img(w, h, "rgba", 8, gray.Select(g => new Px(g[0], g[0], g[0], g[0] == key ? 0 : 255)));
        Add("png/gray8-trns-key", CommonCorpus.PngFile(w, h, 8, 0, gray, beforeIdat: [CommonCorpus.PngChunk("tRNS", new ByteBuilder().U16BE(key).ToArray())]), img,
            "Rgba32", "Grayscale", 8, "PngFile gray8 tRNS", new Obj { ["width"] = w, ["height"] = h, ["key"] = key },
            notes: "Transparent pixels keep their defined gray value (hidden RGB values are compared exactly).");

        // 8-bit RGB with a tRNS key color
        (w, h) = (3, 2);
        List<Px> rgb = [new(10, 20, 30), new(40, 50, 60), new(10, 20, 30), new(70, 80, 90), new(10, 20, 31), new(100, 110, 120)];
        img = new Img(w, h, "rgba", 8, rgb.Select(p => p.Append(p == new Px(10, 20, 30) ? 0 : 255)));
        Add("png/rgb8-trns-key", CommonCorpus.PngFile(w, h, 8, 2, rgb, beforeIdat: [CommonCorpus.PngChunk("tRNS", new ByteBuilder().U16BE(10).U16BE(20).U16BE(30).ToArray())]),
            img, "Rgba32", "Rgb", 8, "PngFile rgb8 tRNS", new Obj { ["width"] = w, ["height"] = h, ["key"] = new List<int> { 10, 20, 30 } });

        // Metadata: RGB ICC profile (iCCP), physical resolution (pHYs, different X/Y), big-endian EXIF with orientation 8,
        // pixel dimensions and a thumbnail directory, XMP (iTXt), tEXt / zTXt / international iTXt, and a text chunk after IDAT
        (w, h) = (3, 2);
        rgb = [new(200, 10, 20), new(30, 180, 40), new(50, 60, 170), new(90, 100, 110), new(250, 240, 5), new(0, 128, 255)];
        img = new Img(w, h, "rgb", 8, rgb);
        var iccRgb = CommonCorpus.IccProfile("RGB ");
        var exif = CommonCorpus.ExifTiff(littleEndian: false, 8, pixelSize: (w, h), thumbnail: Bytes.Ascii("stale-thumbnail-placeholder!"), software: "corpus generator");
        var xmp = Bytes.Utf8("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\" " +
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Corpus</rdf:li>" +
            "</rdf:Alt></dc:title></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"r\"?>");
        List<byte[]> before =
        [
            CommonCorpus.PngChunk("iCCP", Bytes.Concat(Bytes.Ascii("Lorem ipsum RGB\0\0"), CommonCorpus.ZlibStored(iccRgb))),
            CommonCorpus.PngChunk("pHYs", new ByteBuilder().U32BE(3780).U32BE(2835).U8(1).ToArray()),
            CommonCorpus.PngChunk("eXIf", exif),
            CommonCorpus.PngChunk("iTXt", Bytes.Concat(Bytes.Ascii("XML:com.adobe.xmp\0\0\0\0\0"), xmp)),
            CommonCorpus.PngChunk("tEXt", Bytes.Ascii("Title\0Golden corpus metadata")),
            CommonCorpus.PngChunk("zTXt", Bytes.Concat(Bytes.Ascii("Comment\0\0"), CommonCorpus.ZlibStored(Bytes.Latin1("Stored-deflate zTXt, Latin-1: café")))),
            CommonCorpus.PngChunk("iTXt", Bytes.Concat(Bytes.Ascii("Title\0\0\0fr-CA\0"), Bytes.Utf8("Titre"), [0], Bytes.Utf8("Été — métadonnées"))),
        ];
        List<byte[]> after = [CommonCorpus.PngChunk("tEXt", Bytes.Ascii("Author\0Lorem ipsum dolor sit amet"))];
        Add("png/metadata-rgb8-profiles", CommonCorpus.PngFile(w, h, 8, 2, rgb, beforeIdat: before, afterIdat: after), img, "Rgb24", "Rgb", 8,
            "PngFile rgb8 metadata", new Obj
            {
                ["width"] = w,
                ["height"] = h,
                ["pHYs"] = new List<object?> { 3780, 2835, "meter" },
                ["exifOrientation"] = 8,
                ["exif"] = "big-endian, Software, Exif IFD (PixelXDimension SHORT, PixelYDimension LONG), IFD1 thumbnail placeholder",
            },
            layouts: ["rgba8", "rgb8"], orientation: 8, icc: "rgb",
            notes: "Metadata expectations are parsed from the encoded chunks: exact hashes of the uncompressed ICC profile, the " +
                   "EXIF data (from the TIFF header) and the XMP packet; pHYs pixels per meter converted with dpi = ppm x 0.0254. " +
                   "The EXIF thumbnail is placeholder bytes (decoders never decode thumbnails). Orientation 8 is metadata only.");

        // Metadata: gray ICC profile, aspect-ratio-only pHYs (unit 0: no physical resolution), little-endian EXIF orientation 3
        (w, h) = (4, 2);
        gray = CommonCorpus.Grid(w, h, (x, y) => new Px((x * 60 + y * 20) % 256));
        img = new Img(w, h, "gray", 8, gray);
        before =
        [
            CommonCorpus.PngChunk("iCCP", Bytes.Concat(Bytes.Ascii("Lorem ipsum gray\0\0"), CommonCorpus.ZlibStored(CommonCorpus.IccProfile("GRAY")))),
            CommonCorpus.PngChunk("pHYs", new ByteBuilder().U32BE(2).U32BE(1).U8(0).ToArray()),
            CommonCorpus.PngChunk("eXIf", CommonCorpus.ExifTiff(littleEndian: true, 3)),
        ];
        Add("png/metadata-gray8-icc", CommonCorpus.PngFile(w, h, 8, 0, gray, beforeIdat: before), img, "Gray8", "Grayscale", 8,
            "PngFile gray8 metadata", new Obj { ["width"] = w, ["height"] = h, ["pHYs"] = new List<object?> { 2, 1, "unknown (aspect ratio only)" }, ["exifOrientation"] = 3 },
            layouts: ["rgba8", "gray8"], orientation: 3, icc: "gray",
            notes: "pHYs unit 0 only gives an aspect ratio: no physical resolution is expected (resolution null).");

        // Every remaining palette depth, tRNS keys at every gray depth class and 16 bits, all filters on 4-byte
        // pixels, and the smallest Adam7 image (passes 2-7 empty)
        (w, h) = (9, 2);
        palette = [new(10, 200, 30), new(250, 5, 128)];
        idx = CommonCorpus.Grid(w, h, (x, y) => new Px(x < 8 ? (x + y) % 2 : 1 - y));
        idx[0] = new Px(1);
        img = new Img(w, h, "rgba", 8, idx.Select(i => palette[i[0]].Append(255)));
        Add("png/palette1-odd-width", CommonCorpus.PngFile(w, h, 1, 3, idx, beforeIdat: [CommonCorpus.PngChunk("PLTE", PaletteBytes(palette))]),
            img, "Rgba32", "Indexed", 1, "PngFile palette1", new Obj { ["width"] = w, ["height"] = h, ["palette"] = palette });

        (w, h) = (6, 5);
        palette = [new(255, 0, 0), new(0, 128, 255), new(20, 30, 40), new(240, 230, 10)];
        trns = [0, 128];
        idx = CommonCorpus.Grid(w, h, (x, y) => new Px((x + 2 * y) % 4));
        img = new Img(w, h, "rgba", 8, idx.Select(i => palette[i[0]].Append(i[0] < trns.Count ? trns[i[0]] : 255)));
        Add("png/palette2-adam7-trns",
            CommonCorpus.PngFile(w, h, 2, 3, idx, interlace: true, beforeIdat: [CommonCorpus.PngChunk("PLTE", PaletteBytes(palette)), CommonCorpus.PngChunk("tRNS", IntBytes(trns))]),
            img, "Rgba32", "Indexed", 2, "PngFile palette2 adam7 tRNS", new Obj { ["width"] = w, ["height"] = h, ["palette"] = palette, ["trns"] = trns },
            notes: "Transparent palette entry 0 keeps its defined color (hidden RGB values are compared exactly).");

        (w, h) = (5, 2);
        key = 7;
        gray = CommonCorpus.Grid(w, h, (x, y) => new Px((3 * x + 7 * y) % 16));
        img = new Img(w, h, "rgba", 8, gray.Select(g => new Px(g[0] * 17, g[0] * 17, g[0] * 17, g[0] == key ? 0 : 255)));
        Add("png/gray4-trns-key", CommonCorpus.PngFile(w, h, 4, 0, gray, beforeIdat: [CommonCorpus.PngChunk("tRNS", new ByteBuilder().U16BE(key).ToArray())]), img,
            "Rgba32", "Grayscale", 4, "PngFile gray4 tRNS", new Obj { ["width"] = w, ["height"] = h, ["key"] = key },
            notes: "The key is compared with the encoded 4-bit sample (before scaling to 8 bits).",
            ffmpegNotes: "FFmpeg (libavcodec png decoder) ignores tRNS for grayscale below 8 bits and decodes every pixel opaque; " +
                         "the colors agree. Apple ImageIO applies the key like the PNG specification (exact).");

        (w, h) = (4, 2);
        key = 0x1234;
        gray = [.. new[] { 0x1234, 0x1235, 0x3412, 0x0000, 0xFFFF, 0x1234, 0x00FF, 0xFF00 }.Select(v => new Px(v))];
        img = new Img(w, h, "rgba", 16, gray.Select(g => new Px(g[0], g[0], g[0], g[0] == key ? 0 : 65535)));
        Add("png/gray16-trns-key", CommonCorpus.PngFile(w, h, 16, 0, gray, beforeIdat: [CommonCorpus.PngChunk("tRNS", new ByteBuilder().U16BE(key).ToArray())]), img,
            "Rgba64", "Grayscale", 16, "PngFile gray16 tRNS", new Obj { ["width"] = w, ["height"] = h, ["key"] = key },
            notes: "Only the exact 16-bit key is transparent: 0x1235 (low bit) and 0x3412 (byte swap) stay opaque.");

        (w, h) = (3, 2);
        var rgbKey = new Px(0x0102, 0x0304, 0x0506);
        rgb = [new(0x0102, 0x0304, 0x0506), new(0x0102, 0x0304, 0x0507), new(0xFFFF, 0x0000, 0x8000),
            new(0x0102, 0x0304, 0x0506), new(0x0201, 0x0403, 0x0605), new(0x0001, 0x0002, 0x0003)];
        img = new Img(w, h, "rgba", 16, rgb.Select(p => p.Append(p == rgbKey ? 0 : 65535)));
        Add("png/rgb16-trns-key", CommonCorpus.PngFile(w, h, 16, 2, rgb, beforeIdat: [CommonCorpus.PngChunk("tRNS", new ByteBuilder().U16BE(rgbKey[0]).U16BE(rgbKey[1]).U16BE(rgbKey[2]).ToArray())]), img,
            "Rgba64", "Rgb", 16, "PngFile rgb16 tRNS", new Obj { ["width"] = w, ["height"] = h, ["key"] = rgbKey.ToArray().ToList() });

        (w, h) = (7, 6);
        img = new Img(w, h, "rgba", 8, CommonCorpus.Grid(w, h, (x, y) => new Px((x * 37 + y * 11) % 256, (x * 5 + y * 71 + 3) % 256, ((x * y) * 23 + 200) % 256,
            Py.Mod(255 - x * 13 - y * 29, 256))));
        Add("png/rgba8-all-filters", CommonCorpus.PngFile(w, h, 8, 6, img.Pixels), img, "Rgba32", "Rgba", 8, "PngFile rgba8",
            new Obj { ["width"] = w, ["height"] = h, ["filters"] = "row y uses filter type y mod 5" });

        img = new Img(1, 1, "gray", 16, [new Px(0x8001)]);
        Add("png/gray16-adam7-1x1", CommonCorpus.PngFile(1, 1, 16, 0, img.Pixels, interlace: true), img, "Gray16", "Grayscale", 16,
            "PngFile gray16 adam7", new Obj { ["width"] = 1, ["height"] = 1, ["sample"] = 0x8001 }, layouts: ["rgba16le", "gray16le"],
            notes: "A 1x1 Adam7 image has only pass 1: passes 2-7 are empty and have no scanline (not even a filter byte).");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // APNG fixtures: encoded instructions and expected displayed frames are both written literally below. The expected
    // frames were computed by hand from the APNG specification (they are not produced by any compositor) and are
    // cross-checked with FFmpeg's APNG decoder.
    // -----------------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<char, Px> ApngColors = new()
    {
        ['R'] = new(255, 0, 0, 255),
        ['G'] = new(0, 255, 0, 255),
        ['B'] = new(0, 0, 255, 255),
        ['Y'] = new(255, 255, 0, 255),
        ['W'] = new(255, 255, 255, 255),
        ['T'] = new(0, 0, 0, 0),
        ['h'] = new(0, 0, 255, 128),
        ['K'] = new(0, 0, 0, 255),
    };

    private static ApngFrame Frame(Img img, int x, int y, int num, int den, string dispose, string blend) => new(img, x, y, num, den, dispose, blend);

    private static void BuildApngHand(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        List<string> tools = [t.FfmpegLabel + " (cross-check only)", ToolSet.RuntimeLabel];
        static Img G(params string[] rows) => CommonCorpus.GridImage(rows, ApngColors);

        void Add(string fixtureId, byte[] data, List<Img> frames, string[] durations, string[] encodings, int? totalPlays, int numPlays, Img? poster = null, string? notes = null,
            Obj? parameters = null)
        {
            var path = c.Scratch / (fixtureId.Replace('/', '_') + ".png");
            File.WriteAllBytes(path, data);
            var (info, features) = CommonCorpus.PngInspect(data);
            var (check, _) = c.FfmpegCrossCheck(path, frames, "rgba8", animated: true, "apng", posterExpected: poster is not null);
            var expected = new Obj
            {
                ["format"] = "png",
                ["width"] = frames[0].Width,
                ["height"] = frames[0].Height,
                ["pixelFormat"] = "Rgba32",
                ["colorModel"] = "Rgba",
                ["bitsPerComponent"] = 8,
                ["orientation"] = 1,
                ["iccProfile"] = "none",
                ["animation"] = new Obj { ["totalPlays"] = totalPlays, ["encodedLoopValue"] = numPlays },
            };
            c.AddValid(fixtureId, fixtureId + ".png", data, frames, expected,
                c.Provenance("hand-authored", "BuildApngHand", parameters, tools),
                CommonCorpus.HandReference("Hand-authored APNG; displayed frames hand-computed from the APNG specification (written literally in the generator).", [check]),
                features, durations: durations, poster: poster, frameEncodings: encodings, notes: notes,
                encodedDelays: ApngEncodedDelays(info));
        }

        // SOURCE and OVER blending with dispose NONE; the default image is frame 0
        var f0 = G("RRRR", "RRRR", "RRRR", "RRRT");
        var d1 = G("GT", "TB");
        var d2 = G("YTT", "TYh");
        var e0 = f0;
        var e1 = G("RRRR", "RGTR", "RTBR", "RRRT");
        var e2 = G("RRRR", "RGTR", "RYBR", "RRYh");
        var data = CommonCorpus.ApngFile(4, 4, 0,
        [
            Frame(f0, 0, 0, 1, 10, "none", "source"),
            Frame(d1, 1, 1, 3, 30, "none", "source"),
            Frame(d2, 1, 2, 0, 0, "none", "over"),
        ]);
        Add("apng/blend-source-over", data, [e0, e1, e2], ["1/10", "1/10", "0/1"],
            ["full canvas, delay 1/10, dispose NONE, blend SOURCE (default image)",
             "2x2 at (1,1), delay 3/30, dispose NONE, blend SOURCE (transparent pixels replace)",
             "3x2 at (1,2), delay 0/0 (den 0 means 1/100, zero), dispose NONE, blend OVER (transparent pixels keep the canvas; semi-transparent over transparent black is copied)"],
            null, 0, notes: "num_plays 0 means infinite (totalPlays null).");

        // Dispose BACKGROUND and PREVIOUS, finite plays, exact rational delays
        f0 = G("BBBB", "BBBB", "BBBB", "BBBB");
        d1 = G("RR", "RR");
        d2 = G("GG", "GG");
        var d3 = G("Y");
        e0 = f0;
        e1 = G("RRBB", "RRBB", "BBBB", "BBBB");
        e2 = G("TTBB", "TTBB", "BBGG", "BBGG");
        var e3 = G("TTBY", "TTBB", "BBBB", "BBBB");
        data = CommonCorpus.ApngFile(4, 4, 2,
        [
            Frame(f0, 0, 0, 50, 100, "none", "source"),
            Frame(d1, 0, 0, 1, 4, "background", "source"),
            Frame(d2, 2, 2, 1, 3, "previous", "over"),
            Frame(d3, 3, 0, 7, 1000, "none", "source"),
        ]);
        Add("apng/dispose-background-previous", data, [e0, e1, e2, e3], ["1/2", "1/4", "1/3", "7/1000"],
            ["full canvas, delay 50/100, dispose NONE, blend SOURCE (default image)",
             "2x2 at (0,0), delay 1/4, dispose BACKGROUND (cleared to transparent black after display)",
             "2x2 at (2,2), delay 1/3, dispose PREVIOUS (restored after display), blend OVER",
             "1x1 at (3,0), delay 7/1000, dispose NONE, blend SOURCE"],
            2, 2);

        // Separate poster (IDAT without fcTL), first frame with dispose PREVIOUS (treated as BACKGROUND by the spec)
        var poster = G("YYY", "YYY");
        f0 = G("RGR", "GRG");
        d1 = G("B");
        e0 = f0;
        e1 = G("TTT", "TTB");
        data = CommonCorpus.ApngFile(3, 2, 3,
        [
            Frame(f0, 0, 0, 1, 10, "previous", "source"),
            Frame(d1, 2, 1, 1, 5, "none", "source"),
        ], poster: poster);
        Add("apng/separate-poster", data, [e0, e1], ["1/10", "1/5"],
            ["full canvas, delay 1/10, dispose PREVIOUS on the first frame (must be treated as BACKGROUND)",
             "1x1 at (2,1), delay 1/5, dispose NONE, blend SOURCE"],
            3, 3, poster: poster,
            notes: "The IDAT image is a separate poster (no fcTL before IDAT): exposed as the poster frame, not counted in frames.");
    }

    // Literal checks of the OVER contract (worked by hand): half-transparent red over opaque blue, and two translucent pixels
    private static void CheckOverContract()
    {
        Py.Assert(CommonCorpus.Over(new Px(255, 0, 0, 128), new Px(0, 0, 255, 255), 8) == new Px(128, 0, 127, 255));
        Py.Assert(CommonCorpus.Over(new Px(0, 255, 0, 100), new Px(255, 0, 0, 50), 8) == new Px(59, 196, 0, 130));
        Py.Assert(CommonCorpus.Over(new Px(7, 8, 9, 0), new Px(1, 2, 3, 4), 8) == new Px(1, 2, 3, 4));
        Py.Assert(CommonCorpus.Over(new Px(1, 2, 3, 40), new Px(0, 0, 0, 0), 8) == new Px(1, 2, 3, 40));
    }

    /// <summary>APNG compositing fixtures: both poster layouts, partial rectangles, SOURCE transparent replacement, OVER
    /// with translucent pixels (8 and 16 bits), every disposal (consecutive PREVIOUS, BACKGROUND then OVER on the cleared area,
    /// a partial first frame after a poster, OVER on the first frame), Adam7 frame regions with split fdAT chunks, gray + tRNS
    /// and palette sample layouts, a one-frame animation and extreme exact delays. Every expected frame is a literal grid: each
    /// pixel is written by hand from the frame instructions; translucent OVER results are named palette entries computed with
    /// the Over() contract.</summary>
    private static void BuildApngCompositing(Corpus<GoldenTools> c)
    {
        CheckOverContract();
        var t = c.Tools;
        List<string> tools = [t.FfmpegLabel + " (cross-check only)", ToolSet.RuntimeLabel];

        void Add(string fixtureId, byte[] data, List<Img> frames, string[] durations, string[] encodings, int? totalPlays, int numPlays, int depth = 8, Img? poster = null,
            string? notes = null, Dictionary<string, ApngExplanation>? explanations = null, string colorModel = "Rgba", Obj? parameters = null, IEnumerable<Px>? rounding = null,
            int[]? unverified = null)
        {
            var path = c.Scratch / (fixtureId.Replace('/', '_') + ".png");
            File.WriteAllBytes(path, data);
            var (info, features) = CommonCorpus.PngInspect(data);
            var layout = depth == 8 ? "rgba8" : "rgba16le";
            var sample = depth == 8 ? 1 : 2;
            List<(Obj Check, List<byte[]>? Decoded)> results =
            [
                c.FfmpegCrossCheck(path, frames, layout, animated: true, "apng", posterExpected: poster is not null, allowFailure: true),
                ImageioCrossCheck(c, path, frames, layout),
            ];
            var checks = results.Select(r => r.Check).ToList();
            var expectedRaw = frames.Select(f => f.Raw(layout)).ToList();
            var roundingValues = (rounding ?? []).Select(v =>
            {
                var b = new ByteBuilder();
                foreach (var s in v)
                {
                    if (depth == 16)
                        b.U16LE(s);
                    else
                        b.U8(s);
                }

                return b.ToArray();
            }).ToList();
            var verified = new HashSet<int>();
            explanations ??= new Dictionary<string, ApngExplanation>(StringComparer.Ordinal);
            foreach (var ((check, decoded), key) in results.Zip(["ffmpeg", "imageio"]))
            {
                var agreeing = new HashSet<int>();
                var roundingFrames = new HashSet<int>();
                if (decoded is not null && decoded.Count == expectedRaw.Count)
                {
                    for (var i = 0; i < expectedRaw.Count; i++)
                    {
                        var (e, a) = (expectedRaw[i], decoded[i]);
                        if (CommonCorpus.Compare(e, a, sample).Max == 0 || CommonCorpus.DiffersOnlyInHiddenColors(e, a, sample))
                            agreeing.Add(i);
                        else if (CommonCorpus.DiffersOnlyByRounding(e, a, sample, roundingValues))
                            roundingFrames.Add(i);
                    }
                }

                verified.UnionWith(agreeing);
                if ((string)check["result"]! == "exact")
                {
                    Py.Assert(!explanations.ContainsKey(key), fixtureId + ": " + key + " now agrees exactly; remove its explanation");
                    continue;
                }

                // Disagreements are recorded, explained and verified by the generator, never hidden
                Py.Assert(explanations.ContainsKey(key), fixtureId + ": " + key + " disagrees with the reference; explain why: " + PyJson.Dumps(check));
                var explanation = explanations[key];
                var text = explanation.Text;
                var allFrames = Enumerable.Range(0, expectedRaw.Count).ToHashSet();
                if (explanation.Kind == "rounding")
                {
                    // Visible differences are at most one unit, only on the translucent OVER results (truncating arithmetic)
                    Py.Assert(agreeing.Union(roundingFrames).ToHashSet().SetEquals(allFrames) && roundingFrames.Count > 0, $"{fixtureId} {key}: rounding claim does not hold");
                    verified.UnionWith(roundingFrames);
                }
                else if (explanation.Kind == "frames")
                {
                    Py.Assert(agreeing.SetEquals(allFrames.Except(explanation.Frames!)), $"{fixtureId} {key}: only the listed frames may differ");
                }
                else if (explanation.Kind == "failure")
                {
                    Py.Assert(decoded is null || decoded.Count != expectedRaw.Count, $"{fixtureId} {key}: the decoder did not fail");
                }
                else
                {
                    Py.Assert(explanation.Kind == "invalid", explanation.Kind); // results checked to be wrong (documented), never used as a reference
                }

                check["notes"] = ((check.TryGetValue("notes", out var existing) ? (string)existing! : "") + " " + text).Trim();
            }

            // Every displayed frame is confirmed by at least one independent decoder, unless explicitly documented
            var missing = Enumerable.Range(0, expectedRaw.Count).Except(verified).ToHashSet();
            Py.Assert(missing.SetEquals(unverified ?? []), fixtureId + ": frames without independent confirmation: " + string.Join(',', missing.Order()));
            var expected = new Obj
            {
                ["format"] = "png",
                ["width"] = frames[0].Width,
                ["height"] = frames[0].Height,
                ["pixelFormat"] = depth == 8 ? "Rgba32" : "Rgba64",
                ["colorModel"] = colorModel,
                ["bitsPerComponent"] = info.BitDepth,
                ["orientation"] = 1,
                ["iccProfile"] = "none",
                ["animation"] = new Obj { ["totalPlays"] = totalPlays, ["encodedLoopValue"] = numPlays },
            };
            c.AddValid(fixtureId, fixtureId + ".png", data, frames, expected,
                c.Provenance("hand-authored", "BuildApngCompositing", parameters, tools),
                CommonCorpus.HandReference("Hand-authored APNG; displayed frames written literally in the generator from the APNG " +
                                           "specification (translucent OVER pixels: the rounding contract of the library).", checks),
                features, durations: durations, poster: poster, frameEncodings: encodings, notes: notes,
                encodedDelays: ApngEncodedDelays(info), layouts: [layout]);
        }

        // --- 8-bit OVER with translucent pixels, every disposal, default image as frame 0 (blend OVER on frame 0) ---------
        var p = new Dictionary<char, Px>(ApngColors)
        {
            ['g'] = new(0, 255, 0, 100),
            ['r'] = new(255, 0, 0, 50),
            ['x'] = new(10, 20, 30, 0),
            ['w'] = new(255, 255, 255, 200),
        };
        p['1'] = CommonCorpus.Over(p['h'], p['R'], 8);
        p['2'] = CommonCorpus.Over(p['g'], p['g'], 8);
        p['3'] = CommonCorpus.Over(p['h'], p['h'], 8);
        p['4'] = CommonCorpus.Over(p['w'], p['B'], 8);
        p['5'] = CommonCorpus.Over(p['h'], p['r'], 8);
        Img G(params string[] rows) => CommonCorpus.GridImage(rows, p);
        List<ApngFrame> frames =
        [
            // default image with blend OVER: the first frame is drawn with SOURCE (same displayed image on the cleared canvas;
            // the encoded colors of fully transparent pixels are kept, as FFmpeg and Apple ImageIO do)
            Frame(G("RRgx", "rhBT", "xGwr"), 0, 0, 1, 10, "none", "over"),
            Frame(G("hgx", "hwR"), 1, 0, 1, 3, "background", "over"),
            Frame(G("gr", "hx", "Rh"), 2, 0, 65535, 1000, "none", "over"),
            Frame(G("xG", "Th"), 0, 1, 1, 1000, "previous", "source"),
            Frame(G("w"), 1, 1, 7, 0, "previous", "over"),
        ];
        List<Img> expectedFrames =
        [
            G("RRgx", "rhBT", "xGwr"),
            G("R12x", "r34R", "xGwr"), // a transparent source pixel keeps the canvas pixel (hidden color included)
            G("RTgr", "rThT", "xGR5"), // frame 1 region cleared to transparent black, then OVER (copies onto it)
            G("RTgr", "xGhT", "ThR5"), // SOURCE replaces: the transparent x keeps its hidden color
            G("RTgr", "rwhT", "xGR5"), // frame 3 region restored; the last frame's PREVIOUS has no visible effect
        ];
        Add("apng/over-alpha-rgba8", CommonCorpus.ApngFile(4, 3, 1, frames), expectedFrames, ["1/10", "1/3", "13107/200", "1/1000", "7/100"],
            ["full canvas (default image), delay 1/10, dispose NONE, blend OVER (first frame: drawn with SOURCE)",
             "3x2 at (1,0), delay 1/3, dispose BACKGROUND, blend OVER (translucent over opaque and translucent pixels)",
             "2x3 at (2,0), delay 65535/1000, dispose NONE, blend OVER (onto the cleared area and over a translucent pixel)",
             "2x2 at (0,1), delay 1/1000, dispose PREVIOUS, blend SOURCE (transparent pixels replace, hidden color kept)",
             "1x1 at (1,1), delay 7/0 (den 0 means 1/100), dispose PREVIOUS (last frame), blend OVER"],
            1, 1, rounding: "12345".Select(k => p[k]),
            explanations: new Dictionary<string, ApngExplanation>(StringComparer.Ordinal)
            {
                ["ffmpeg"] = new("rounding", "Visible differences are at most 1, only on translucent-over-translucent OVER results: " +
                                             "FFmpeg truncates its integer divisions (FAST_DIV255, division by 255 x alpha) where the " +
                                             "reference rounds the exact composite to nearest; e.g. " +
                                             "(0,255,0,100) over itself is exactly green 255 (FFmpeg: 254)."),
                ["imageio"] = new("rounding", "Visible differences are at most 1, only on translucent-over-translucent OVER results: " +
                                              "ImageIO truncates the composite alpha (e.g. 41000/255 = 160.78 gives 160, the contract " +
                                              "rounds to 161) and colors."),
            });

        // --- 16-bit SOURCE frames after a separate poster: partial rectangles, PREVIOUS on the first frame, consecutive PREVIOUS
        var q = new Dictionary<char, Px>
        {
            ['A'] = new(0x1234, 0x5678, 0x9ABC, 0xFFFF),
            ['C'] = new(0x0001, 0x0100, 0xFFFE, 0xFFFF),
            ['D'] = new(0xFFFF, 0x8000, 0x0001, 0x7FFF),
            ['E'] = new(0x00FF, 0xFF00, 0x0F0F, 0x8001),
            ['z'] = new(0x0102, 0x0304, 0x0506, 0x0000),
            ['T'] = new(0, 0, 0, 0),
        };
        Img G16(IReadOnlyDictionary<char, Px> colors, params string[] rows) => new(rows[0].Length, rows.Length, "rgba", 16, rows.SelectMany(r => r.Select(ch => colors[ch])));
        var poster = G16(q, "ACD", "EAC", "DEA");
        frames =
        [
            Frame(G16(q, "Az", "CD"), 1, 1, 1, 2, "background", "source"),
            Frame(G16(q, "EDz"), 0, 0, 0, 1, "none", "source"),
            Frame(G16(q, "CA", "zE"), 0, 1, 2, 4, "previous", "source"),
            Frame(G16(q, "A", "C", "D"), 2, 0, 3, 1, "previous", "source"),
            Frame(G16(q, "DC"), 1, 2, 1, 65535, "background", "source"),
        ];
        expectedFrames =
        [
            G16(q, "TTT", "TAz", "TCD"), // the animation starts transparent black: the poster is not part of it
            G16(q, "EDz", "TTT", "TTT"), // the first frame's region was cleared to transparent black
            G16(q, "EDz", "CAT", "zET"),
            G16(q, "EDA", "TTC", "TTD"), // frame 2 region restored, then frame 3 (consecutive PREVIOUS)
            G16(q, "EDz", "TTT", "TDC"),
        ];
        Add("apng/poster-rgba16-dispose", CommonCorpus.ApngFile(3, 3, 0, frames, poster: poster, bitDepth: 16), expectedFrames,
            ["1/2", "0/1", "1/2", "3/1", "1/65535"],
            ["2x2 at (1,1) (first animation frame, after the separate poster), delay 1/2, dispose BACKGROUND, blend SOURCE",
             "3x1 at (0,0), delay 0/1 (zero kept), dispose NONE, blend SOURCE",
             "2x2 at (0,1), delay 2/4, dispose PREVIOUS, blend SOURCE (transparent 16-bit hidden colors replace)",
             "1x3 at (2,0), delay 3/1, dispose PREVIOUS (consecutive), blend SOURCE",
             "2x1 at (1,2), delay 1/65535, dispose BACKGROUND (last frame), blend SOURCE"],
            null, 0, depth: 16, poster: poster,
            notes: "Separate poster (no fcTL before IDAT) with 16-bit low-bit samples: exposed as the poster frame, not counted in frames. " +
                   "Frame 0 (a partial region after the poster) has no independent confirmation: FFmpeg rejects the layout and ImageIO " +
                   "returns another frame at index 0; apng/poster-partial-single-frame confirms the same layout with ImageIO.",
            explanations: new Dictionary<string, ApngExplanation>(StringComparer.Ordinal)
            {
                ["ffmpeg"] = new("failure", "FFmpeg's APNG demuxer rejects a first animation frame that does not cover the canvas " +
                                            "(legal when the default image is a separate poster)."),
                ["imageio"] = new("frames", "Apple ImageIO returns the second animation frame at index 0 when the APNG has a " +
                                            "separate poster and several frames (same with apng/separate-poster); every later " +
                                            "frame agrees exactly.", [0]),
            },
            unverified: [0]);

        // --- 16-bit OVER with translucent pixels (FFmpeg does not blend 16-bit APNG frames) --------------------------------
        var u = new Dictionary<char, Px>
        {
            ['P'] = new(0xFFFF, 0, 0, 0xFFFF),
            ['Q'] = new(0, 0xFFFF, 0x8000, 0x8000),
            ['S'] = new(0x1234, 0x4321, 0xABCD, 0x0101),
            ['U'] = new(0x0001, 0x0002, 0x0003, 0xFFFE),
            ['z'] = new(0x0A0B, 0x0C0D, 0x0E0F, 0),
            ['T'] = new(0, 0, 0, 0),
        };
        u['1'] = CommonCorpus.Over(u['U'], u['Q'], 16);
        u['2'] = CommonCorpus.Over(u['Q'], u['S'], 16);
        u['3'] = CommonCorpus.Over(u['Q'], u['P'], 16);
        u['4'] = CommonCorpus.Over(u['Q'], u['U'], 16);
        frames =
        [
            Frame(G16(u, "PQS", "UTz"), 0, 0, 1, 25, "none", "source"),
            Frame(G16(u, "UQ", "Sz"), 1, 0, 2, 25, "none", "over"),
            Frame(G16(u, "Q", "Q"), 0, 0, 3, 25, "background", "over"),
        ];
        expectedFrames =
        [
            G16(u, "PQS", "UTz"),
            G16(u, "P12", "USz"), // a transparent source pixel keeps the canvas pixel, hidden color included
            G16(u, "312", "4Sz"),
        ];
        Add("apng/over-alpha-rgba16", CommonCorpus.ApngFile(3, 2, 4, frames, bitDepth: 16), expectedFrames, ["1/25", "2/25", "3/25"],
            ["full canvas (default image), delay 1/25, dispose NONE, blend SOURCE",
             "2x2 at (1,0), delay 2/25, dispose NONE, blend OVER (16-bit translucent over translucent and transparent pixels)",
             "1x2 at (0,0), delay 3/25, dispose BACKGROUND (last frame), blend OVER (translucent over opaque pixels)"],
            4, 4, depth: 16, unverified: [1, 2],
            notes: "The translucent 16-bit OVER results follow the library contract (computed with exact rationals by over()); " +
                   "no available decoder implements 16-bit OVER correctly, so frames 1 and 2 have no independent confirmation " +
                   "(the 8-bit contract is cross-checked by apng/over-alpha-rgba8).",
            explanations: new Dictionary<string, ApngExplanation>(StringComparer.Ordinal)
            {
                ["ffmpeg"] = new("failure", "FFmpeg's APNG decoder does not implement OVER blending for 16-bit pixel formats: it stops after " +
                                            "the first frame."),
                ["imageio"] = new("invalid", "Apple ImageIO's 16-bit OVER output is not an alpha composite (e.g. (0,65535,32768,32768) " +
                                             "over opaque red gives (0,65535,0,65535)); frame 0 (SOURCE) agrees exactly."),
            });

        // --- Gray 8-bit with a tRNS key, Adam7 frame regions of odd sizes, fdAT split into three chunks ---------------------
        var levels = new Dictionary<char, int> { ['a'] = 10, ['b'] = 60, ['c'] = 120, ['d'] = 200, ['e'] = 255, ['k'] = 64 };
        Img Gray(params string[] rows) => new(rows[0].Length, rows.Length, "gray", 8, rows.SelectMany(r => r.Select(ch => new Px(levels[ch]))));
        Img Rgba(params string[] rows) => new(rows[0].Length, rows.Length, "rgba", 8,
            rows.SelectMany(r => r.Select(ch => new Px(levels[ch], levels[ch], levels[ch], ch == 'k' ? 0 : 255))));
        frames =
        [
            Frame(Gray("abcde", "edcba", "akaka", "ccccc", "deded"), 0, 0, 1, 2, "none", "source"),
            Frame(Gray("kek", "bkd", "eee", "kak"), 2, 1, 2, 3, "none", "over"),
            Frame(Gray("kabc", "ekek"), 0, 3, 3, 4, "none", "source"),
        ];
        expectedFrames =
        [
            Rgba("abcde", "edcba", "akaka", "ccccc", "deded"), // key pixels: alpha 0, gray 64 kept
            Rgba("abcde", "edcea", "akbkd", "cceee", "dedad"), // OVER: key pixels keep the canvas (a hidden key stays hidden)
            Rgba("abcde", "edcea", "akbkd", "kabce", "ekekd"),
        ];
        Add("apng/gray8-trns-adam7", CommonCorpus.ApngFile(5, 5, 0, frames, bitDepth: 8, colorType: 0, interlace: true,
                beforeIdat: [CommonCorpus.PngChunk("tRNS", new ByteBuilder().U16BE(levels['k']).ToArray())], fdatSplits: 3),
            expectedFrames, ["1/2", "2/3", "3/4"],
            ["full canvas (default image), Adam7, delay 1/2, dispose NONE, blend SOURCE",
             "3x4 at (2,1), Adam7 (empty passes), 3 fdAT chunks, delay 2/3, dispose NONE, blend OVER (tRNS key pixels keep the canvas)",
             "4x2 at (0,3), Adam7, 3 fdAT chunks, delay 3/4, dispose NONE, blend SOURCE (key pixels replace)"],
            null, 0, colorModel: "Grayscale");

        // --- Separate poster and a single partial first frame -------------------------------------------------------------
        static Img GA(params string[] rows) => CommonCorpus.GridImage(rows, ApngColors);
        poster = GA("GGG", "GGG");
        frames = [Frame(GA("RB"), 1, 1, 5, 10, "background", "source")];
        Add("apng/poster-partial-single-frame", CommonCorpus.ApngFile(3, 2, 2, frames, poster: poster), [GA("TTT", "TRB")], ["1/2"],
            ["2x1 at (1,1) (the only animation frame, after the separate poster), delay 5/10, dispose BACKGROUND, blend SOURCE"],
            2, 2, poster: poster,
            explanations: new Dictionary<string, ApngExplanation>(StringComparer.Ordinal)
            {
                ["ffmpeg"] = new("failure", "FFmpeg's APNG demuxer rejects a first animation frame that does not cover the " +
                                            "canvas (legal when the default image is a separate poster)."),
            },
            notes: "One displayed frame plus a separate poster: still animated (poster and acTL), the canvas outside the region is transparent black.");

        // --- Palette with tRNS, one-frame animation (default image is frame 0) ---------------------------------------------
        List<Px> palette = [new(255, 0, 0), new(0, 0, 255), new(10, 200, 30), new(90, 80, 70)];
        List<int> alphas = [255, 128, 0];
        int[] indices = [0, 1, 2, 3];
        var img = new Img(4, 1, "rgba", 8, indices.Select(i => palette[i].Append(i < alphas.Count ? alphas[i] : 255)));
        frames = [Frame(new Img(4, 1, "gray", 8, indices.Select(i => new Px(i))), 0, 0, 0, 0, "background", "over")];
        Add("apng/palette-single-frame", CommonCorpus.ApngFile(4, 1, 1, frames, colorType: 3,
                beforeIdat: [CommonCorpus.PngChunk("PLTE", PaletteBytes(palette)), CommonCorpus.PngChunk("tRNS", IntBytes(alphas))]),
            [img], ["0/1"],
            ["full canvas (default image, the only frame), delay 0/0 (zero), dispose BACKGROUND, blend OVER (first frame: drawn with SOURCE)"],
            1, 1, colorModel: "Indexed",
            notes: "A one-frame APNG (num_frames 1, num_plays 1) is animated: it keeps its animation settings. The fully transparent palette entry keeps its color (first frame drawn with SOURCE).");
    }

    private static void BuildApngFfmpeg(Corpus<GoldenTools> c)
    {
        var lowBits = CommonCorpus.PatternRgba16LowBits(3, 3);
        ApngFfmpegCase[] cases =
        [
            new("apng/ffmpeg-rgba8", 8,
            [
                CommonCorpus.GridImage(["RRGG", "RRGG", "BBYY"], ApngColors),
                CommonCorpus.GridImage(["RRGG", "RTTG", "BBYY"], ApngColors),
                CommonCorpus.GridImage(["WRGG", "RTTG", "BBYK"], ApngColors),
            ], 2, "4"),
            new("apng/ffmpeg-rgba16", 16,
            [
                CommonCorpus.PatternRgba16LowBits(3, 3),
                new Img(3, 3, "rgba", 16, lowBits.Pixels.Select((px, i) => i != 4 ? px : new Px(1, 2, 3, 0))),
            ], 0, "5"),
        ];
        foreach (var (fixtureId, _, frames, plays, rate) in cases)
        {
            AddApngFfmpeg(c, fixtureId, frames, plays, rate, "BuildApngFfmpeg", new Obj { ["frames"] = frames.Count, ["rate"] = rate, ["plays"] = plays },
                "APNG encoded by FFmpeg from hand-defined full-canvas frames (FFmpeg chooses the delta rectangles, dispose and blend operations recorded in the frame encodings); the expected displayed frames are the source frames.");
        }
    }

    /// <summary>Raw fcTL delay fields read from the encoded input (delay_den 0 is kept as encoded: it means 1/100).</summary>
    private static List<object?> ApngEncodedDelays(PngInfo info) =>
        [.. info.FcTL.Select(f => (object?)new Obj { ["numerator"] = f[5], ["denominator"] = f[6] })];

    // -----------------------------------------------------------------------------------------------------------------
    // Invalid PNG and APNG fixtures
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Deterministic zlib stream (one fixed-Huffman deflate block, RFC 1951 section 3.2.6) decompressing to
    /// 1 + 258 * repeats copies of one byte: the literal, then length-258/distance-1 copies. Used for decompression-limit
    /// fixtures: a few hundred compressed bytes inflate to tens of kilobytes, independently of any zlib implementation.</summary>
    private static (byte[] Stream, int InflatedLength) ZlibFixedRepeat(int value, int repeats)
    {
        var bits = new List<int>();

        void Put(int code, int length) // Huffman codes are packed most significant bit first
        {
            for (var i = 0; i < length; i++)
                bits.Add((code >> (length - 1 - i)) & 1);
        }

        bits.AddRange([1, 1, 0]); // BFINAL = 1, BTYPE = 01 (fixed Huffman), header bits are packed LSB first
        if (value < 144)
            Put(0x30 + value, 8);
        else
            Put(0x190 + value - 144, 9);
        for (var r = 0; r < repeats; r++)
        {
            Put(0xC0 + (285 - 280), 8); // length 258: literal/length code 285, no extra bits
            Put(0, 5); // distance code 0: distance 1
        }

        Put(0, 7); // end of block (code 256)
        var output = new ByteBuilder();
        for (var i = 0; i < bits.Count; i += 8)
        {
            var b = 0;
            var group = bits.Skip(i).Take(8).ToList();
            for (var j = 0; j < group.Count; j++)
                b += group[j] << j;
            output.U8(b);
        }

        var data = Enumerable.Repeat((byte)value, 1 + 258 * repeats).ToArray();
        var stream = Bytes.Concat([0x78, 0x01], output.ToArray(), new ByteBuilder().U32BE(Bytes.Adler32(data)).ToArray());
        Py.Assert(Bytes.ZlibDecompress(stream).AsSpan().SequenceEqual(data));
        return (stream, data.Length);
    }

    private static void BuildInvalidPng(Corpus<GoldenTools> c)
    {
        const int Width = 4;
        const int Height = 2;
        var gray = CommonCorpus.Grid(Width, Height, (x, y) => new Px((x + y) * 30));
        var good = CommonCorpus.PngFile(Width, Height, 8, 0, gray);
        var raw = CommonCorpus.PngImageData(Width, Height, CommonCorpus.Rows(gray, Width, Height), 1, 8, interlace: false);
        const int RowBytes = 1 + Width;

        static byte[] WithStream(byte[] stream, IEnumerable<byte[]>? before = null, int bitDepth = 8, int colorType = 0) =>
            Bytes.Concat([CommonCorpus.PngSignature, CommonCorpus.PngIhdr(Width, Height, bitDepth, colorType, interlace: false), .. before ?? [], CommonCorpus.PngChunk("IDAT", stream),
                CommonCorpus.PngChunk("IEND", [])]);

        void Add(string fixtureId, byte[] data, Obj parameters, Obj? expected = null, string? notes = null, string kind = "invalid")
        {
            c.AddError(fixtureId, kind, fixtureId + ".png", data, "png",
                c.Provenance("hand-authored", "BuildInvalidPng", parameters, [ToolSet.RuntimeLabel]),
                expected ?? new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Png" }, notes: notes);
        }

        // Defects detected by a header identification (before the first IDAT)
        var badCrc = (byte[])good.Clone();
        badCrc[8 + 8 + 13] ^= 0x01; // first byte of the IHDR CRC
        Add("invalid/png/bad-ihdr-crc", badCrc, new Obj { ["flippedBit"] = "IHDR CRC byte 0, bit 0" });
        Add("invalid/png/ihdr-not-first", Bytes.Concat(CommonCorpus.PngSignature, CommonCorpus.PngChunk("tEXt", Bytes.Ascii("Title\0first")), Bytes.Slice(good, 8)),
            new Obj { ["defect"] = "tEXt chunk before IHDR" });
        Add("invalid/png/illegal-bit-depth", Bytes.Concat(CommonCorpus.PngSignature, CommonCorpus.PngIhdr(Width, Height, 4, 2, interlace: false), Bytes.Slice(good, 8 + 25)),
            new Obj { ["defect"] = "IHDR color type 2 (RGB) with bit depth 4" });
        Add("invalid/png/chunk-length-overflow", Bytes.Concat(Bytes.Slice(good, 0, 8 + 25), new ByteBuilder().U32BE(0x80000000).ToArray(), Bytes.Ascii("tEXt"), new byte[16]),
            new Obj { ["defect"] = "tEXt chunk length 2^31 (above the PNG limit 2^31 - 1)" });
        Add("invalid/png/unknown-critical-chunk", Bytes.Concat(Bytes.Slice(good, 0, 8 + 25), CommonCorpus.PngChunk("CgBI", [0x50, 0x00, 0x20, 0x02]), Bytes.Slice(good, 8 + 25)),
            new Obj { ["defect"] = "unknown critical chunk CgBI after IHDR" },
            new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Png", ["feature"] = "PNG critical chunk CgBI" },
            notes: "A critical chunk the decoder does not understand is an unsupported requirement, never skipped.", kind: "unsupported");

        // Defects detected by a full scan (container structure after the first IDAT)
        var idat = Bytes.Find(good, "IDAT"u8);
        Py.Assert(idat >= 0);
        Add("invalid/png/truncated-idat", Bytes.Slice(good, 0, idat + 4 + 10), new Obj { ["truncatedAfter"] = "10 bytes of IDAT data" },
            notes: "Truncated input must be reported as malformed data, never as a clean end of input.");
        var idatLength = (int)Bytes.U32BE(good, idat - 4);
        var badIdatCrc = (byte[])good.Clone();
        badIdatCrc[idat + 4 + idatLength] ^= 0x80;
        Add("invalid/png/bad-idat-crc", badIdatCrc, new Obj { ["flippedBit"] = "IDAT CRC byte 0, bit 7" });
        var stream = CommonCorpus.ZlibStored(raw);
        Add("invalid/png/non-consecutive-idat",
            Bytes.Concat(CommonCorpus.PngSignature, CommonCorpus.PngIhdr(Width, Height, 8, 0, interlace: false), CommonCorpus.PngChunk("IDAT", Bytes.Slice(stream, 0, 8)),
                CommonCorpus.PngChunk("tEXt", Bytes.Ascii("Title\0between")), CommonCorpus.PngChunk("IDAT", Bytes.Slice(stream, 8)), CommonCorpus.PngChunk("IEND", [])),
            new Obj { ["defect"] = "tEXt chunk between two IDAT chunks" });
        var rgb = CommonCorpus.Grid(Width, Height, (x, y) => new Px(x * 40, y * 90, 7));
        Add("invalid/png/plte-after-idat", CommonCorpus.PngFile(Width, Height, 8, 2, rgb, afterIdat: [CommonCorpus.PngChunk("PLTE", [1, 2, 3, 4, 5, 6])]),
            new Obj { ["defect"] = "PLTE chunk (suggested palette of an RGB image) after IDAT" });
        Add("invalid/png/missing-iend", Bytes.Slice(good, 0, -12), new Obj { ["defect"] = "the file ends after the IDAT chunk" },
            notes: "A missing IEND is truncation, never a clean end of input.");

        // Defects only pixel decoding detects (valid container: CRCs, order, lengths; full scans succeed)
        Add("invalid/png/truncated-zlib", WithStream(Bytes.Slice(stream, 0, -7)), new Obj { ["defect"] = "zlib datastream cut inside its stored block (valid IDAT CRC)" },
            notes: "The BCL inflater reports truncated data as a clean end: the decoder checks the scanline count.");
        Add("invalid/png/missing-adler32", WithStream(Bytes.Slice(stream, 0, -4)), new Obj { ["defect"] = "complete deflate data without the zlib Adler-32 trailer" },
            notes: "Every scanline is present; only the zlib trailer is missing.");
        var badAdler = (byte[])stream.Clone();
        badAdler[^1] ^= 0x01;
        Add("invalid/png/bad-adler32", WithStream(badAdler), new Obj { ["flippedBit"] = "zlib Adler-32 byte 3, bit 0" });
        Add("invalid/png/extra-bytes-after-zlib", WithStream(Bytes.Concat(stream, new byte[4])), new Obj { ["defect"] = "4 zero bytes after the zlib datastream in IDAT" });
        Add("invalid/png/extra-image-data", WithStream(CommonCorpus.ZlibStored(Bytes.Concat(raw, Bytes.Slice(raw, 0, RowBytes)))),
            new Obj { ["defect"] = "the zlib datastream decompresses to one scanline more than the image" },
            notes: "Decompression is bounded by the image size: data beyond the last scanline is rejected, never buffered.");
        Add("invalid/png/too-little-image-data", WithStream(CommonCorpus.ZlibStored(Bytes.Slice(raw, 0, -RowBytes))),
            new Obj { ["defect"] = "valid zlib datastream one scanline shorter than the image" });
        var badFilter = (byte[])raw.Clone();
        badFilter[RowBytes] = 5;
        Add("invalid/png/bad-filter-type", WithStream(CommonCorpus.ZlibStored(badFilter)), new Obj { ["defect"] = "filter type 5 on scanline 1" });
        var indices = CommonCorpus.Grid(Width, Height, (x, y) => new Px(x % 2));
        indices[^1] = new Px(3);
        List<byte[]> palette = [CommonCorpus.PngChunk("PLTE", [255, 0, 0, 0, 0, 255])];
        Add("invalid/png/palette-index-out-of-range",
            WithStream(CommonCorpus.ZlibStored(CommonCorpus.PngImageData(Width, Height, CommonCorpus.Rows(indices, Width, Height), 1, 2, interlace: false)), palette, bitDepth: 2, colorType: 3),
            new Obj { ["defect"] = "2-bit palette index 3 with a 2-entry PLTE" });

        // Decompression limit: a zTXt chunk whose 171-byte zlib stream inflates to 25,801 bytes, decoded with MaxMetadataBytes = 4096
        var (bomb, inflated) = ZlibFixedRepeat('A', 100);
        var data = CommonCorpus.PngFile(Width, Height, 8, 0, gray, beforeIdat: [CommonCorpus.PngChunk("zTXt", Bytes.Concat(Bytes.Ascii("Comment\0\0"), bomb))]);
        var entry = c.Write("invalid/png/ztxt-over-metadata-limit.png", data);
        c.AddError("limit/png/ztxt-over-metadata-limit", "limit", null, null, "png",
            c.Provenance("hand-authored", "BuildInvalidPng (ZlibFixedRepeat)", new Obj { ["compressedBytes"] = bomb.Length, ["inflatedBytes"] = inflated }, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "MetadataBytes" },
            decodeOptions: new Obj { ["limits"] = new Obj { ["MaxMetadataBytes"] = 4096 } }, existingInput: entry,
            notes: "Decompressed text counts toward MaxMetadataBytes and is charged before it is retained (no unbounded inflation).");
    }

    /// <summary>Malformed APNG control data and frame payloads. Every input is a hand-assembled 2x2 RGBA APNG whose only
    /// defect is the one named; detection stages are classified in Tests/Conformance/ErrorFixtureStages.</summary>
    private static void BuildInvalidApng(Corpus<GoldenTools> c)
    {
        static Img G(params string[] rows) => CommonCorpus.GridImage(rows, ApngColors);
        var (full, part) = (G("RG", "BY"), G("W"));
        var head = Bytes.Concat(CommonCorpus.PngSignature, CommonCorpus.PngIhdr(2, 2, 8, 6, interlace: false));
        var iend = CommonCorpus.PngChunk("IEND", []);

        static byte[] Actl(long frames, long plays = 0) => CommonCorpus.PngChunk("acTL", new ByteBuilder().U32BE(frames).U32BE(plays).ToArray());

        static byte[] Fc(int seq, int w, int h, int x = 0, int y = 0, int dispose = 0, int blend = 0) =>
            CommonCorpus.PngChunk("fcTL", new ByteBuilder().U32BE(seq).U32BE(w).U32BE(h).U32BE(x).U32BE(y).U16BE(1).U16BE(10).U8(dispose).U8(blend).ToArray());

        static byte[] Fd(int seq, byte[] data) => CommonCorpus.PngChunk("fdAT", Bytes.Concat(new ByteBuilder().U32BE(seq).ToArray(), data));

        var idat = CommonCorpus.PngChunk("IDAT", CommonCorpus.RgbaRowsStream(full));
        var one = CommonCorpus.RgbaRowsStream(part);

        void Add(string fixtureId, byte[] data, Obj parameters, string? notes = null)
        {
            c.AddError(fixtureId, "invalid", fixtureId + ".png", data, "png",
                c.Provenance("hand-authored", "BuildInvalidApng", parameters, [ToolSet.RuntimeLabel]),
                new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Png" }, features: ["apng"], notes: notes);
        }

        // Defects before the first IDAT (a header identification reports them)
        Add("invalid/png/apng-zero-frames", Bytes.Concat(head, Actl(0), Fc(0, 2, 2), idat, iend), new Obj { ["defect"] = "acTL num_frames 0" });
        Add("invalid/png/apng-num-plays-overflow", Bytes.Concat(head, Actl(1, 0x80000000), Fc(0, 2, 2), idat, iend),
            new Obj { ["defect"] = "acTL num_plays 2^31 (above the PNG integer limit)" });
        Add("invalid/png/apng-default-fctl-partial", Bytes.Concat(head, Actl(1), Fc(0, 1, 1), idat, iend),
            new Obj { ["defect"] = "the fcTL of the default image (before IDAT) covers 1x1 of the 2x2 canvas" },
            notes: "The default image is frame zero: its region must be the whole canvas.");
        Add("invalid/png/apng-invalid-dispose-op", Bytes.Concat(head, Actl(1), Fc(0, 2, 2, dispose: 3), idat, iend), new Obj { ["defect"] = "dispose_op 3" });
        Add("invalid/png/apng-fdat-before-idat", Bytes.Concat(head, Actl(2), Fc(0, 2, 2), Fd(1, one), idat, iend),
            new Obj { ["defect"] = "an fdAT chunk precedes the IDAT chunk" });

        // Defects after the first IDAT (a full scan reports them; never a clean end or a static image)
        Add("invalid/png/apng-sequence-gap", Bytes.Concat(head, Actl(2), Fc(0, 2, 2), idat, Fc(1, 1, 1, 1, 1), Fd(3, one), iend),
            new Obj { ["defect"] = "fdAT sequence number 3 where 2 is expected" });
        Add("invalid/png/apng-region-out-of-bounds", Bytes.Concat(head, Actl(2), Fc(0, 2, 2), idat, Fc(1, 2, 1, 1, 1), Fd(2, CommonCorpus.RgbaRowsStream(G("WW"))), iend),
            new Obj { ["defect"] = "frame region 2x1 at (1,1) exceeds the 2x2 canvas" });
        Add("invalid/png/apng-frame-count-short", Bytes.Concat(head, Actl(3), Fc(0, 2, 2), idat, Fc(1, 1, 1, 1, 1), Fd(2, one), iend),
            new Obj { ["defect"] = "acTL declares 3 frames, the file has 2" },
            notes: "A missing frame is malformed data, never a clean end of the animation.");
        Add("invalid/png/apng-frame-count-excess", Bytes.Concat(head, Actl(1), Fc(0, 2, 2), idat, Fc(1, 1, 1, 1, 1), Fd(2, one), iend),
            new Obj { ["defect"] = "acTL declares 1 frame, the file has 2" });
        Add("invalid/png/apng-fctl-without-fdat", Bytes.Concat(head, Actl(2), Fc(0, 2, 2), idat, Fc(1, 1, 1, 1, 1), iend),
            new Obj { ["defect"] = "the last fcTL has no fdAT chunk" });
        Add("invalid/png/apng-invalid-blend-op", Bytes.Concat(head, Actl(2), Fc(0, 2, 2), idat, Fc(1, 1, 1, 1, 1, blend: 2), Fd(2, one), iend),
            new Obj { ["defect"] = "blend_op 2 on the second frame" });
        Add("invalid/png/apng-fdat-without-sequence", Bytes.Concat(head, Actl(2), Fc(0, 2, 2), idat, Fc(1, 1, 1, 1, 1), CommonCorpus.PngChunk("fdAT", [0x00, 0x00]), iend),
            new Obj { ["defect"] = "a 2-byte fdAT chunk (shorter than its sequence number)" });

        // Defects inside an fdAT datastream (valid container: a full scan succeeds, decoding fails)
        Add("invalid/png/apng-fdat-truncated-zlib", Bytes.Concat(head, Actl(2), Fc(0, 2, 2), idat, Fc(1, 1, 1, 1, 1), Fd(2, Bytes.Slice(one, 0, -6)), iend),
            new Obj { ["defect"] = "the zlib datastream of frame 1 is cut inside its stored block (valid fdAT CRC)" });
        var raw = CommonCorpus.PngImageData(1, 1, [[ApngColors['W']]], 4, 8, interlace: false);
        Add("invalid/png/apng-fdat-extra-scanline", Bytes.Concat(head, Actl(2), Fc(0, 2, 2), idat, Fc(1, 1, 1, 1, 1), Fd(2, CommonCorpus.ZlibStored(Bytes.Concat(raw, raw))), iend),
            new Obj { ["defect"] = "the datastream of the 1x1 frame 1 decompresses to two scanlines" },
            notes: "Frame datastreams are bounded by their region size: data beyond the last scanline is rejected.");
    }
}
