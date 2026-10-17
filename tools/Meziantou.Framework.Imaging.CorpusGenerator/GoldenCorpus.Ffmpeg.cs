using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// ---------------------------------------------------------------------------------------------------------------------
// Decoder inputs written by an independent encoder (FFmpeg): every PNG pixel format with every prediction mode, progressive
// and Adam7; APNG animations in every pixel format; GIF animations with FFmpeg's partial rectangles and transparent
// unchanged pixels. FFmpeg chooses the encoding decisions (filters, delta rectangles, dispose/blend operations), so these
// inputs exercise decoder paths that the hand-authored fixtures do not choose. Expected pixels are the seeded source
// samples; FFmpeg's own decoding is recorded as a cross-check (and must agree exactly, except where noted).
// ---------------------------------------------------------------------------------------------------------------------
internal static partial class GoldenCorpus
{
    /// <summary>FFmpeg PNG pixel formats: encoder input format, image model and depth, expected default pixel format, color
    /// model, bits per component and the native layouts cross-checked with FFmpeg.</summary>
    private static readonly (string Format, string InputFormat, string Model, int Depth, string PixelFormat, string ColorModel, int Bits, string[] NativeLayouts)[] PngFfmpegFormats =
    [
        ("rgb24", "rgb24", "rgb", 8, "Rgb24", "Rgb", 8, ["rgb8"]),
        ("rgba", "rgba", "rgba", 8, "Rgba32", "Rgba", 8, []),
        ("rgb48be", "rgb48be", "rgb", 16, "Rgba64", "Rgb", 16, []),
        ("rgba64be", "rgba64be", "rgba", 16, "Rgba64", "Rgba", 16, []),
        ("gray", "gray", "gray", 8, "Gray8", "Grayscale", 8, ["gray8"]),
        ("gray16be", "gray16be", "gray", 16, "Gray16", "Grayscale", 16, ["gray16le"]),
        ("ya8", "ya8", "graya", 8, "Rgba32", "GrayscaleAlpha", 8, []),
        ("ya16be", "ya16be", "graya", 16, "Rgba64", "GrayscaleAlpha", 16, []),
        // 1-bit gray from black and white samples; palette from RGB samples of the fixed 3-3-2 palette FFmpeg converts to
        ("monob", "gray", "gray", 8, "Gray8", "Grayscale", 1, ["gray8"]),
        ("pal8", "rgb24", "rgb", 8, "Rgba32", "Indexed", 8, []),
    ];

    // Every filter type alone on progressive images, and FFmpeg's per-row choice (mixed) on Adam7 images: each pass is a
    // separate reduced image with its own filtered rows. Progressive mixed prediction is covered by BuildPngFfmpeg.
    private static readonly (string Prediction, bool Interlaced)[] PngFfmpegPredictions =
        [("none", false), ("sub", false), ("up", false), ("avg", false), ("paeth", false), ("mixed", true)];

    private static void BuildPngFfmpegDecoding(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        const int Width = 13; // odd sizes with every Adam7 pass non-empty
        const int Height = 9;
        var seed = 0;
        foreach (var (format, inputFormat, model, depth, pixelFormat, colorModel, bits, nativeLayouts) in PngFfmpegFormats)
        {
            foreach (var (prediction, interlaced) in PngFfmpegPredictions)
            {
                seed++;
                var fixtureId = $"png/ffmpeg-{format}-{prediction}-{(interlaced ? "adam7" : "progressive")}";
                var img = PngFfmpegSource(format, model, depth, Width, Height, seed);
                var path = c.Scratch / (fixtureId.Replace('/', '_') + ".png");
                var source = CommonCorpus.WithSuffix(path, ".source.raw");
                File.WriteAllBytes(source, img.FfmpegRaw().Data);
                var command = t.FfmpegCmd(["-f", "rawvideo", "-pixel_format", inputFormat, "-video_size", $"{CommonCorpus.Str(Width)}x{CommonCorpus.Str(Height)}", "-i", source.Value,
                    "-frames:v", "1", "-c:v", "png", "-pred", prediction, .. (interlaced ? ["-flags", "+ildct"] : Array.Empty<string>()), "-pix_fmt", format, path.Value]);
                Proc.Run(command);
                File.Delete(source);
                var data = File.ReadAllBytes(path);
                var (info, features) = CommonCorpus.PngInspect(data);
                Py.Assert(info.Interlace == (interlaced ? 1 : 0), fixtureId + ": FFmpeg did not honor the interlace request");
                var layout = CommonCorpus.CanonicalLayout(img);
                var (check, _) = c.FfmpegCrossCheck(path, [img], layout, animated: false, "png");
                Py.Assert((string)check["result"]! == "exact", fixtureId + ": FFmpeg does not decode the source samples");
                var checks = new List<Obj> { check };
                foreach (var native in nativeLayouts)
                {
                    var (fmt, bpp) = native switch
                    {
                        "gray8" => ("gray", 1),
                        "gray16le" => ("gray16le", 2),
                        "rgb8" => ("rgb24", 3),
                        _ => throw new InvalidOperationException(native),
                    };
                    var (decoded, cmd) = CommonCorpus.FfmpegDecode(t, path, fmt, img.Width, img.Height, bpp, animated: false);
                    var exact = decoded.Count == 1 && decoded[0].AsSpan().SequenceEqual(img.Raw(native));
                    Py.Assert(exact, fixtureId + ": FFmpeg does not decode the native " + native + " samples");
                    checks.Add(new Obj
                    {
                        ["decoder"] = $"{t.FfmpegLabel} (libavcodec png decoder, native {fmt})",
                        ["command"] = CommonCorpus.Display(cmd, t, [(path.Value, "{input}")]),
                        ["result"] = "exact",
                    });
                }

                c.AddValid(
                    fixtureId, fixtureId + ".png", data, [img], CommonCorpus.StillExpected("png", img, pixelFormat, colorModel, bits),
                    c.Provenance("generated", "BuildPngFfmpegDecoding PngFfmpegSource",
                        new Obj { ["width"] = Width, ["height"] = Height, ["seed"] = seed, ["pixelFormat"] = format, ["prediction"] = prediction, ["interlaced"] = interlaced },
                        [t.FfmpegLabel, ToolSet.RuntimeLabel],
                        [CommonCorpus.Display(command, t, [(source.Value, "{source}.raw"), (path.Value, "{input}")])]),
                    CommonCorpus.HandReference(
                        $"PNG encoded by FFmpeg ({format}, prediction {prediction}{(interlaced ? ", Adam7" : "")}) from seeded samples; FFmpeg chooses the filters recorded in the features. The expected pixels are the source samples.",
                        checks),
                    features, layouts: [layout, .. nativeLayouts]);
            }
        }
    }

    /// <summary>Seeded samples representable exactly in the FFmpeg pixel format: full-range noise, black and white for monob,
    /// colors of FFmpeg's fixed 3-3-2 palette (red and green levels are multiples of 36, blue of 85) for pal8.</summary>
    private static Img PngFfmpegSource(string format, string model, int depth, int width, int height, int seed)
    {
        var random = new PyRandom(seed);
        var maximum = (1 << depth) - 1;
        var channels = Img.Channels(model);
        return CommonCorpus.Named(new Img(width, height, model, depth, CommonCorpus.Grid(width, height, (_, _) => format switch
        {
            "monob" => new Px(random.RandRange(2) * 255),
            "pal8" => new Px(random.RandRange(8) * 36, random.RandRange(8) * 36, random.RandRange(4) * 85),
            _ => channels switch
            {
                1 => new Px(random.RandInt(0, maximum)),
                2 => new Px(random.RandInt(0, maximum), random.RandInt(0, maximum)),
                3 => new Px(random.RandInt(0, maximum), random.RandInt(0, maximum), random.RandInt(0, maximum)),
                _ => new Px(random.RandInt(0, maximum), random.RandInt(0, maximum), random.RandInt(0, maximum), random.RandInt(0, maximum)),
            },
        })), "PngFfmpegSource");
    }

    private static void BuildApngFfmpegDecoding(Corpus<GoldenTools> c)
    {
        (string Format, string Model, int Depth)[] formats =
        [
            ("rgba", "rgba", 8), ("rgb24", "rgb", 8), ("rgba64be", "rgba", 16), ("rgb48be", "rgb", 16),
            ("gray", "gray", 8), ("gray16be", "gray", 16), ("ya8", "graya", 8), ("ya16be", "graya", 16),
        ];
        var seed = 100;
        foreach (var (format, model, depth) in formats)
        {
            seed++;
            var frames = AnimationSourceFrames(16, 12, 6, seed, model, depth, random => NoisePixel(random, model, depth));
            var sixteen = depth == 16 && model is "rgba" or "graya";
            AddApngFfmpeg(c, "apng/ffmpeg-" + format, frames, plays: 0, rate: "10", "BuildApngFfmpegDecoding AnimationSourceFrames",
                new Obj { ["width"] = 16, ["height"] = 12, ["frames"] = frames.Count, ["seed"] = seed, ["pixelFormat"] = format, ["rate"] = "10", ["plays"] = 0 },
                $"APNG encoded by FFmpeg ({format}) from seeded full-canvas frames: each frame edits a rectangle of the previous one, frame 3 reverts to frame 1 and frame 5 repeats frame 4, so FFmpeg chooses delta rectangles, BACKGROUND/PREVIOUS disposal and OVER blending (recorded in the frame encodings). Alpha is 0 or the maximum. The expected displayed frames are the source frames.",
                sixteen ? "FFmpeg's APNG decoder does not implement OVER blending at 16 bits (it stops at the first such frame): the source frames, which the lossless encoder reproduces, are the only reference." : null);
        }
    }

    private static void BuildGifFfmpegDecoding(Corpus<GoldenTools> c)
    {
        var colors = Enumerable.Range(0, 64).Select(i => new Px(i * 4, 255 - (i * 3), (i * 37) % 256)).ToList();

        // An explicit 256-entry palette (palettegen may move a color by one unit): the 64 colors, padding copies of the
        // first one, and the transparent entry FFmpeg uses for unchanged pixels (transdiff) last
        var palette = new Img(16, 16, "rgba", 8, [.. colors.Select(p => new Px(p[0], p[1], p[2], 255)), .. Enumerable.Repeat(new Px(colors[0][0], colors[0][1], colors[0][2], 255), 191), new Px(0, 255, 0, 0)]);
        (string Flags, int Loop)[] cases = [("+offsetting+transdiff", 0), ("+offsetting-transdiff", 3), ("-offsetting+transdiff", 1)];
        var seed = 200;
        foreach (var (flags, loop) in cases)
        {
            seed++;
            var frames = AnimationSourceFrames(29, 19, 7, seed, "rgb", 8, random => colors[random.RandRange(colors.Count)]);
            var name = (flags.Contains("+offsetting", StringComparison.Ordinal) ? "offsetting" : "full") + "-" + (flags.Contains("+transdiff", StringComparison.Ordinal) ? "transdiff" : "opaque");
            AddGifFfmpeg(c, "gif/ffmpeg-" + name, frames, "10", "rgb24", "[0:v][1:v]paletteuse=dither=none", flags, loop, "BuildGifFfmpegDecoding AnimationSourceFrames",
                new Obj { ["width"] = 29, ["height"] = 19, ["frames"] = frames.Count, ["seed"] = seed, ["colors"] = colors.Count, ["gifflags"] = flags, ["rate"] = 10, ["loop"] = loop },
                $"GIF encoded by FFmpeg (explicit palette of the 64 colors, no dithering, -gifflags {flags}) from seeded opaque frames: each frame edits a rectangle of the previous one, frame 3 reverts to frame 1 and frame 5 repeats frame 4, so FFmpeg chooses partial rectangles{(flags.Contains("+transdiff", StringComparison.Ordinal) ? " and marks unchanged pixels with a transparent index" : "")} (recorded in the frame encodings). The expected displayed frames are the source frames.",
                loop == 0 ? null : $"NETSCAPE loop count {CommonCorpus.Str(loop)} stores repetitions: totalPlays = {CommonCorpus.Str(loop + 1)}.",
                palette);
        }
    }

    /// <summary>Full-canvas animation frames: frame 0 is seeded noise; each later frame edits a random rectangle of the
    /// previous one, frame 3 reverts to frame 1 and frame 5 repeats frame 4.</summary>
    private static List<Img> AnimationSourceFrames(int width, int height, int count, int seed, string model, int depth, Func<PyRandom, Px> pixel)
    {
        var random = new PyRandom(seed);
        var frames = new List<List<Px>> { CommonCorpus.Grid(width, height, (_, _) => pixel(random)) };
        for (var i = 1; i < count; i++)
        {
            var frame = new List<Px>(i switch { 3 => frames[1], 5 => frames[4], _ => frames[i - 1] });
            if (i is not 3 and not 5)
            {
                var x = random.RandRange(width - 4);
                var y = random.RandRange(height - 4);
                var w = random.RandRange(1, width - x);
                var h = random.RandRange(1, height - y);
                for (var row = y; row < y + h; row++)
                {
                    for (var column = x; column < x + w; column++)
                        frame[(row * width) + column] = pixel(random);
                }
            }

            frames.Add(frame);
        }

        return [.. frames.Select(f => CommonCorpus.Named(new Img(width, height, model, depth, f), "AnimationSourceFrames"))];
    }

    /// <summary>Seeded noise; alpha is 0 or the maximum (FFmpeg's APNG decoder truncates translucent OVER composites where
    /// the library rounds to nearest).</summary>
    private static Px NoisePixel(PyRandom random, string model, int depth)
    {
        var maximum = (1 << depth) - 1;
        int Sample() => random.RandInt(0, maximum);
        int Alpha() => random.RandRange(2) * maximum;
        return model switch
        {
            "gray" => new Px(Sample()),
            "graya" => new Px(Sample(), Alpha()),
            "rgb" => new Px(Sample(), Sample(), Sample()),
            _ => new Px(Sample(), Sample(), Sample(), Alpha()),
        };
    }

    /// <summary>An APNG encoded by FFmpeg from full-canvas frames; the expected displayed frames are the source frames.</summary>
    private static void AddApngFfmpeg(Corpus<GoldenTools> c, string fixtureId, List<Img> frames, int plays, string rate, string function, Obj parameters, string description, string? notes = null)
    {
        var t = c.Tools;
        var path = c.Scratch / (fixtureId.Replace('/', '_') + ".png");
        var source = CommonCorpus.WithSuffix(path, ".source.raw");
        var fmt = frames[0].FfmpegRaw().Format;
        File.WriteAllBytes(source, Bytes.Concat(frames.Select(f => f.FfmpegRaw().Data)));
        var command = t.FfmpegCmd("-f", "rawvideo", "-pixel_format", fmt, "-video_size", $"{CommonCorpus.Str(frames[0].Width)}x{CommonCorpus.Str(frames[0].Height)}",
            "-framerate", rate, "-i", source.Value, "-c:v", "apng", "-pix_fmt", fmt, "-plays", CommonCorpus.Str(plays),
            "-f", "apng", path.Value);
        Proc.Run(command);
        File.Delete(source);
        var data = File.ReadAllBytes(path);
        var (info, features) = CommonCorpus.PngInspect(data);
        // Durations are read from the encoded fcTL fields, never from FFmpeg timestamps
        var durations = new List<string>();
        foreach (var f in info.FcTL)
        {
            var (num, den) = (f[5], f[6] != 0 ? f[6] : 100);
            durations.Add(CommonCorpus.Normalize(num, den));
        }

        Py.Assert(info.NumFrames == frames.Count && durations.Count == frames.Count, fixtureId);
        Py.Assert(durations.All(d => d == CommonCorpus.Normalize(1, int.Parse(rate, CultureInfo.InvariantCulture))), fixtureId + " " + string.Join(", ", durations));
        var layout = CommonCorpus.CanonicalLayout(frames[0]);
        var (check, _) = c.FfmpegCrossCheck(path, frames, layout, animated: true, "apng");
        Py.Assert(notes is not null || (string)check["result"]! is "exact" or "equivalent", fixtureId + ": FFmpeg does not decode the source frames");
        var depth = frames[0].Depth;
        var expected = new Obj
        {
            ["format"] = "png",
            ["width"] = frames[0].Width,
            ["height"] = frames[0].Height,
            ["pixelFormat"] = depth == 8 ? "Rgba32" : "Rgba64",
            ["colorModel"] = frames[0].Model switch { "rgba" => "Rgba", "rgb" => "Rgb", "gray" => "Grayscale", _ => "GrayscaleAlpha" },
            ["bitsPerComponent"] = depth,
            ["orientation"] = 1,
            ["iccProfile"] = "none",
            ["animation"] = new Obj { ["totalPlays"] = plays == 0 ? null : plays, ["encodedLoopValue"] = info.NumPlays },
        };
        string[] disposeNames = ["NONE", "BACKGROUND", "PREVIOUS"];
        string[] blendNames = ["SOURCE", "OVER"];
        var encodings = info.FcTL.Select(f => string.Create(CultureInfo.InvariantCulture,
            $"{f[1]}x{f[2]} at ({f[3]},{f[4]}), delay {f[5]}/{f[6]}, dispose {disposeNames[f[7]]}, blend {blendNames[f[8]]}")).ToList();
        c.AddValid(fixtureId, fixtureId + ".png", data, frames, expected,
            c.Provenance("generated", function, parameters,
                [t.FfmpegLabel, ToolSet.RuntimeLabel],
                [CommonCorpus.Display(command, t, [(source.Value, "{source}.raw"), (path.Value, "{input}")])]),
            CommonCorpus.HandReference(description, [check]),
            features, durations: durations, frameEncodings: encodings, encodedDelays: ApngEncodedDelays(info), notes: notes);
    }

    /// <summary>A GIF encoded by FFmpeg with an exact palette from opaque full-canvas frames; the expected displayed frames
    /// are the source frames.</summary>
    private static void AddGifFfmpeg(Corpus<GoldenTools> c, string fixtureId, List<Img> frames, string rate, string inputFormat, string filter, string? gifFlags, int loop,
        string function, Obj parameters, string description, string? notes, Img? palette = null)
    {
        var t = c.Tools;
        var (width, height) = (frames[0].Width, frames[0].Height);
        var path = c.Scratch / (fixtureId.Replace('/', '_') + ".gif");
        var source = CommonCorpus.WithSuffix(path, ".source.raw");
        var paletteSource = CommonCorpus.WithSuffix(path, ".palette.raw");
        File.WriteAllBytes(source, Bytes.Concat(frames.Select(f => inputFormat == "rgba" ? f.Raw("rgba8") : f.FfmpegRaw().Data)));
        List<string> paletteInput = [];
        if (palette is not null)
        {
            File.WriteAllBytes(paletteSource, palette.Raw("rgba8"));
            paletteInput = ["-f", "rawvideo", "-pixel_format", "rgba", "-video_size", "16x16", "-i", paletteSource.Value];
        }

        var command = t.FfmpegCmd(["-f", "rawvideo", "-pixel_format", inputFormat, "-video_size", $"{CommonCorpus.Str(width)}x{CommonCorpus.Str(height)}", "-framerate", rate,
            "-i", source.Value, .. paletteInput, "-filter_complex", filter, .. (gifFlags is null ? Array.Empty<string>() : ["-gifflags", gifFlags]),
            "-loop", CommonCorpus.Str(loop), "-f", "gif", path.Value]);
        Proc.Run(command);
        File.Delete(source);
        if (palette is not null)
            File.Delete(paletteSource);
        var data = File.ReadAllBytes(path);
        var (info, features) = CommonCorpus.GifInspect(data);
        Py.Assert(info.Frames.Count == frames.Count && info.Loop == loop, fixtureId);
        List<string> durations = [.. info.Frames.Select(f => f.Gce is not null ? CommonCorpus.Normalize(f.Gce.Delay, 100) : "0/1")];
        List<string?> encodings = [.. info.Frames.Select(f => string.Create(CultureInfo.InvariantCulture,
            $"{f.Rect.W}x{f.Rect.H} at ({f.Rect.X},{f.Rect.Y}), disposal {(f.Gce is not null ? CommonCorpus.Str(f.Gce.Disposal) : "-")}, delay {(f.Gce is not null ? CommonCorpus.Str(f.Gce.Delay) : "-")}, transparent index {(f.Gce is not null ? Py.Repr((object?)f.Gce.TransparentIndex) : "-")}"))];
        var (check, _) = c.FfmpegCrossCheck(path, frames, "rgba8", true, "gif");
        Py.Assert((string)check["result"]! == "exact", fixtureId + ": FFmpeg does not decode the source frames");
        var expected = new Obj
        {
            ["format"] = "gif",
            ["width"] = width,
            ["height"] = height,
            ["pixelFormat"] = "Rgba32",
            ["colorModel"] = "Indexed",
            ["bitsPerComponent"] = 8,
            ["orientation"] = 1,
            ["iccProfile"] = "none",
            ["animation"] = GifAnimation(loop == 0 ? null : loop + 1, loop),
        };
        c.AddValid(fixtureId, fixtureId + ".gif", data, frames, expected,
            c.Provenance("generated", function, parameters,
                [t.FfmpegLabel, ToolSet.RuntimeLabel],
                [CommonCorpus.Display(command, t, [(source.Value, "{source}.raw"), (paletteSource.Value, "{palette}.raw"), (path.Value, "{input}")])]),
            CommonCorpus.HandReference(description, [check]),
            features, durations: durations, frameEncodings: encodings, encodedDelays: GifEncodedDelays(info), notes: notes);
    }
}
