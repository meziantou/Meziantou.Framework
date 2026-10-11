using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.JpegXl;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the JPEG XL fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/jxl, invalid/jxl and
/// limit/jxl entries). This generator is the reviewed, reproducible recipe of every committed JPEG XL
/// fixture. It is NEVER run by the tests and does not use the library under test.
/// The inputs are written by the tools of libjxl (cjxl, jxl_from_tree) or assembled around their codestreams. The expected
/// pixels of a lossless fixture are the pattern given to the encoder, or the values its tree sets; those of a lossy fixture
/// are decoded by djxl. Every reference is cross-checked with jxl-oxide, a decoder that shares no code with libjxl, and a
/// decoded reference is only recorded when the two decoders agree within the tolerance the fixture states. The fields of
/// the files (boxes, image header, frame headers) are read by the independent reader of the test harness.
/// It only replaces the JPEG XL entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format "jxl") and
/// their files, and keeps every other entry and file unchanged.
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- jxl            # check: regenerate in a temporary directory and diff
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- jxl --write    # explicit regeneration (review the diff afterwards!)
/// Requirements: none, the tools are downloaded from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt
/// package (override a version difference with --accept-tool-versions after reviewing it). Any OS.
/// </summary>
internal static partial class JxlCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/JxlCorpus.cs";

    // Tool versions used to produce the committed JPEG XL fixtures. Regenerating with other versions is allowed only
    // explicitly (--accept-tool-versions) and must be reviewed: encoded inputs and decoded references may change.
    private const string PinnedLibjxl = "0.12.0";
    private const string PinnedJxlOxide = "0.12.6";

    // Measured disagreement of the library decoder with each djxl reference, in units of the 8-bit scale: (max absolute
    // error, mean absolute color error), printed by Tests/Conformance/JpegXlDecoderConformanceTests, which also fails when
    // a policy derived from these values is looser or tighter than the decoder needs. The values are reviewed measurements
    // of the library (never used as pixel references); a fixture without a measurement keeps the bound justified by the
    // cross-check alone.
    private static readonly Dictionary<string, (int Max, double Mean)> LibraryJxlMeasurements = new(StringComparer.Ordinal)
    {
        ["jxl/jpeg-420"] = (0, 0.0),
        ["jxl/jpeg-422"] = (1, 0.0022),
        ["jxl/jpeg-444"] = (1, 0.0009),
        ["jxl/lossy-gray8"] = (0, 0.0),
        ["jxl/lossy-lf-frame"] = (1, 0.0002),
        ["jxl/lossy-modular"] = (0, 0.0),
        ["jxl/lossy-no-filters"] = (1, 0.0013),
        ["jxl/lossy-noise"] = (0, 0.0),
        ["jxl/lossy-patches"] = (0, 0.0),
        ["jxl/lossy-progressive"] = (1, 0.0026),
        ["jxl/lossy-rgb16"] = (1, 0.0001),
        ["jxl/lossy-rgb8"] = (1, 0.0007),
        ["jxl/lossy-rgb8-fast"] = (0, 0.0),
        ["jxl/lossy-rgb8-smooth"] = (0, 0.0),
        ["jxl/lossy-rgba8"] = (1, 0.0009),
        ["jxl/lossy-splines"] = (1, 0.0783),
        ["jxl/lossy-strong-filter"] = (1, 0.002),
        ["jxl/lossy-upsampled"] = (1, 0.0004),
    };

    private static bool IsJxlFixture(Obj fixture) => (string?)fixture["format"] == "jxl";

    private static int Generate(FullPath outDir, JxlTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<JxlTools>(outDir, tools, ScriptPath))
        {
            BuildLossless(corpus);
            BuildMetadata(corpus);
            BuildContainers(corpus);
            BuildLossy(corpus);
            BuildAnimations(corpus);
            BuildInvalid(corpus);
            BuildUnsupported(corpus);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsJxlFixture(fixture), (string?)fixture["id"]);

        // Every other fixture and file of the committed corpus is kept unchanged
        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsJxlFixture);
        return fixtures.Count;
    }

    public static int Run(GeneratorOptions options)
    {
        var tools = new JxlTools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-jxl-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => $"{fixtures} JPEG XL fixtures; corpus: {files} files, {bytes} bytes",
            "The committed JPEG XL fixtures are reproducible.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Encoding
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>An encoded input and the command lines that wrote it.</summary>
    private sealed record Encoded(byte[] Data, List<string> Commands);

    /// <summary>Encodes an image with cjxl from a PAM file of the given maximum value.</summary>
    private static Encoded Cjxl(Corpus<JxlTools> c, Img img, IEnumerable<string> options, int? maxValue = null, IEnumerable<(string Key, byte[] Data, string Symbol)>? files = null)
    {
        var source = c.Scratch / "source.pam";
        File.WriteAllBytes(source, WritePam(img, maxValue ?? ((1 << img.Depth) - 1)));
        return CjxlFile(c, source, "{source}.pam", options, files);
    }

    /// <summary>Encodes a file with cjxl; the files of the <c>-x key=path</c> options are written first.</summary>
    private static Encoded CjxlFile(Corpus<JxlTools> c, FullPath source, string sourceSymbol, IEnumerable<string> options, IEnumerable<(string Key, byte[] Data, string Symbol)>? files = null)
    {
        var output = c.Scratch / "output.jxl";
        var substitutions = new List<(string Path, string Symbol)> { (source.Value, sourceSymbol), (output.Value, "{output}.jxl") };
        List<string> command = [c.Tools.Cjxl, source.Value, output.Value, .. options];
        foreach (var (key, data, symbol) in files ?? [])
        {
            var path = c.Scratch / ("cjxl-" + key);
            File.WriteAllBytes(path, data);
            command.Add("-x");
            command.Add($"{key}={path.Value}");
            substitutions.Add((path.Value, symbol));
        }

        command.AddRange(["--quiet", "--num_threads=0"]);
        Proc.Run(command);
        return new Encoded(File.ReadAllBytes(output), [CommonCorpus.Display(command, c.Tools, substitutions)]);
    }

    /// <summary>Writes a codestream with jxl_from_tree.</summary>
    private static Encoded FromTree(Corpus<JxlTools> c, string tree)
    {
        var source = c.Scratch / "source.tree";
        var output = c.Scratch / "output.jxl";
        File.WriteAllText(source, tree.ReplaceLineEndings("\n") + "\n");
        var command = new List<string> { c.Tools.JxlFromTree, source.Value, output.Value };
        Proc.Run(command, stderr: true);
        var text = string.Join(" / ", tree.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return new Encoded(File.ReadAllBytes(output), [CommonCorpus.Display(command, c.Tools, [(source.Value, "{tree}"), (output.Value, "{output}.jxl")]), "{tree}: " + text]);
    }

    private static byte[] WritePam(Img img, int maxValue)
    {
        var channels = Img.Channels(img.Model);
        var type = img.Model switch
        {
            "gray" => "GRAYSCALE",
            "graya" => "GRAYSCALE_ALPHA",
            "rgb" => "RGB",
            _ => "RGB_ALPHA",
        };
        var builder = new ByteBuilder();
        foreach (var value in Encoding.ASCII.GetBytes($"P7\nWIDTH {CommonCorpus.Str(img.Width)}\nHEIGHT {CommonCorpus.Str(img.Height)}\nDEPTH {CommonCorpus.Str(channels)}\nMAXVAL {CommonCorpus.Str(maxValue)}\nTUPLTYPE {type}\nENDHDR\n"))
            builder.U8(value);
        foreach (var pixel in img.Pixels)
        {
            foreach (var value in pixel)
            {
                Py.Assert(value <= maxValue, "sample above the maximum value");
                if (maxValue > 255)
                    builder.U16BE(value);
                else
                    builder.U8(value);
            }
        }

        return builder.ToArray();
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Decoding with the reference decoders
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The frames a decoder gives as floating-point samples (1 is the largest integer sample): a NumPy file of shape (frames, height, width, channels).</summary>
    private sealed record Decoded(int FrameCount, int Height, int Width, int Channels, float[] Samples, string Command);

    private static Decoded Djxl(Corpus<JxlTools> c, FullPath input)
    {
        var output = c.Scratch / "djxl.npy";
        var command = new List<string> { c.Tools.Djxl, input.Value, output.Value, "--quiet", "--num_threads=0" };
        Proc.Run(command);
        return ReadNpy(File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [(input.Value, "{input}"), (output.Value, "{output}.npy")]));
    }

    private static Decoded JxlOxide(Corpus<JxlTools> c, FullPath input)
    {
        var output = c.Scratch / "oxide.npy";
        var command = new List<string> { c.Tools.JxlOxide, "decode", input.Value, "-o", output.Value, "-f", "npy", "-j", "1" };
        Proc.Run(command, stderr: true);
        return ReadNpy(File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [(input.Value, "{input}"), (output.Value, "{output}.npy")]));
    }

    private static bool IsRejected(IReadOnlyList<string> command)
    {
        try
        {
            Proc.Run(command, stderr: true);
            return false;
        }
        catch (ToolException)
        {
            return true;
        }
    }

    private static Decoded ReadNpy(byte[] data, string command)
    {
        Py.Assert(data[0] == 0x93 && data.AsSpan(1, 5).SequenceEqual("NUMPY"u8), "NumPy signature");
        var headerLength = data[6] == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)) : checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)));
        var headerStart = data[6] == 1 ? 10 : 12;
        var header = Encoding.ASCII.GetString(data, headerStart, headerLength);
        Py.Assert(header.Contains("'<f4'", StringComparison.Ordinal) && header.Contains("'fortran_order': False", StringComparison.Ordinal), header);
        var shape = ShapeRegex().Match(header).Groups["shape"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        Py.Assert(shape.Length == 4, header);
        var samples = new float[shape[0] * shape[1] * shape[2] * shape[3]];
        Py.Assert(data.Length == headerStart + headerLength + (4 * samples.Length), "NumPy length");
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(headerStart + headerLength + (4 * i)));

        return new Decoded(shape[0], shape[1], shape[2], shape[3], samples, command);
    }

    /// <summary>Rounds the frames of a decoder to integer samples of a depth.</summary>
    private static List<Img> ToImages(Decoded decoded, int depth)
    {
        var maximum = (1 << depth) - 1;
        var model = decoded.Channels switch
        {
            1 => "gray",
            2 => "graya",
            3 => "rgb",
            _ => "rgba",
        };
        Py.Assert(decoded.Channels is >= 1 and <= 4, "channel count");
        var frames = new List<Img>();
        var pixelCount = decoded.Width * decoded.Height;
        for (var frame = 0; frame < decoded.FrameCount; frame++)
        {
            var pixels = new List<Px>(pixelCount);
            for (var i = 0; i < pixelCount; i++)
            {
                var offset = ((frame * pixelCount) + i) * decoded.Channels;
                int Sample(int channel) => (int)Math.Clamp(Math.Round((double)decoded.Samples[offset + channel] * maximum, MidpointRounding.AwayFromZero), 0, maximum);
                pixels.Add(decoded.Channels switch
                {
                    1 => new Px(Sample(0)),
                    2 => new Px(Sample(0), Sample(1)),
                    3 => new Px(Sample(0), Sample(1), Sample(2)),
                    _ => new Px(Sample(0), Sample(1), Sample(2), Sample(3)),
                });
            }

            frames.Add(new Img(decoded.Width, decoded.Height, model, depth, pixels));
        }

        return frames;
    }

    /// <summary>Compares the frames of a decoder with the expected ones and records the result.</summary>
    private static Obj Check(string decoder, Decoded decoded, IReadOnlyList<Img> expected, string differs, int orientation = 1, bool excluded = false)
    {
        var depth = expected[0].Depth;
        var layout = CommonCorpus.CanonicalLayout(expected[0]);

        // The decoders apply the orientation of the file; the fixtures expect the stored samples
        var actual = ToImages(decoded, depth).ConvertAll(frame => CommonCorpus.Unrotate(frame, orientation));
        Py.Assert(actual.Count == expected.Count && actual[0].Width == expected[0].Width && actual[0].Height == expected[0].Height, $"{decoder}: {actual.Count} frames of {actual[0].Width}x{actual[0].Height}");
        var check = new Obj { ["decoder"] = decoder, ["command"] = decoded.Command };
        CommonCorpus.CompareFrames(check, [.. expected.Select(frame => frame.Raw(layout))], [.. actual.Select(frame => frame.Raw(layout))], depth / 8,
            "the decoders agree on what is shown");
        if (depth == 16 && (string?)check["result"] != "exact")
        {
            // Like every tolerance of the corpus, the errors are in units of the 8-bit scale: 257 units of a 16-bit sample
            check["maxAbsoluteError"] = (Convert.ToInt32(check["maxAbsoluteError"], CultureInfo.InvariantCulture) + 256) / 257;
            check["meanAbsoluteError"] = Py.Round(Convert.ToDouble(check["meanAbsoluteError"], CultureInfo.InvariantCulture) / 257, 4);
        }

        if ((string?)check["result"] == "differs")
            check["notes"] = differs;
        if (excluded)
            check["excludedFromTolerance"] = true;
        return check;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>What a valid fixture expects besides its pixels.</summary>
    private sealed class Expectation
    {
        public int Orientation { get; init; } = 1;

        public byte[]? Icc { get; init; }

        public byte[]? Exif { get; init; }

        public byte[]? Xmp { get; init; }

        public Obj? Cicp { get; init; }

        public bool IsLinear { get; init; }

        /// <summary>The play count of an animation (0: forever), or <see langword="null"/> for a still image.</summary>
        public int? LoopCount { get; init; }

        /// <summary>The depth of the samples of the file, when it is not the depth of the reference frames.</summary>
        public int? BitsPerComponent { get; init; }

        /// <summary>The model of the file, when the reference decoders do not return it (a grayscale image returned as RGB).</summary>
        public string? Model { get; init; }
    }

    /// <summary>
    /// Adds a valid fixture. The fields of the file are read by the independent reader of the harness and must agree with
    /// what the generator expects; both reference decoders are run and compared with the expected frames.
    /// </summary>
    private static void AddValid(Corpus<JxlTools> c, string name, string function, Encoded encoded, IReadOnlyList<Img> frames, Obj reference, List<Obj> checks, Expectation expectation,
        Obj? parameters = null, string origin = "generated", Obj? comparison = null, string? notes = null, IEnumerable<string>? extraFeatures = null)
    {
        var fixtureId = "jxl/" + name;
        var file = ReferenceJpegXl.Parse(encoded.Data);
        var first = frames[0];
        var model = expectation.Model ?? first.Model;
        Py.Assert(file.Width == first.Width && file.Height == first.Height, $"{fixtureId}: size {file.Width}x{file.Height}");
        Py.Assert(file.Orientation == expectation.Orientation, $"{fixtureId}: orientation {file.Orientation}");
        Py.Assert(file.HasIccProfile == (expectation.Icc is not null), $"{fixtureId}: ICC profile");
        Py.Assert((file.Exif is null) == (expectation.Exif is null) && (file.Exif is null || file.Exif.Value.Span.SequenceEqual(expectation.Exif)), $"{fixtureId}: EXIF");
        Py.Assert((file.Xmp is null) == (expectation.Xmp is null) && (file.Xmp is null || file.Xmp.Value.Span.SequenceEqual(expectation.Xmp)), $"{fixtureId}: XMP");
        Py.Assert(file.HasAnimation == (expectation.LoopCount is not null), $"{fixtureId}: animation");
        Py.Assert((file.ColorSpace == 1) == model.StartsWith("gray", StringComparison.Ordinal), $"{fixtureId}: color space {file.ColorSpace}");
        var alphaChannels = file.ExtraChannels.Count(channel => channel.Type == 0);
        Py.Assert(alphaChannels == (model.EndsWith("a", StringComparison.Ordinal) ? 1 : 0) && file.ExtraChannels.Count == alphaChannels, $"{fixtureId}: extra channels");
        var bits = expectation.BitsPerComponent ?? first.Depth;
        Py.Assert(file.BitsPerSample == bits && !file.HasFloatSamples, $"{fixtureId}: {file.BitsPerSample} bits");

        var (pixelFormat, colorModel) = (model, first.Depth) switch
        {
            ("gray", 8) => ("Gray8", "Grayscale"),
            ("gray", 16) => ("Gray16", "Grayscale"),
            ("graya", 8) => ("Rgba32", "GrayscaleAlpha"),
            ("graya", 16) => ("Rgba64", "GrayscaleAlpha"),
            ("rgb", 8) => ("Rgb24", "Rgb"),
            ("rgb", 16) => ("Rgba64", "Rgb"),
            ("rgba", 8) => ("Rgba32", "Rgba"),
            _ => ("Rgba64", "Rgba"),
        };
        var layouts = new List<string> { CommonCorpus.CanonicalLayout(first) };
        if (first.Model is "rgb" or "gray" && !(first.Model == "rgb" && first.Depth == 16))
            layouts.Add(first.Model == "rgb" ? "rgb8" : first.Depth == 8 ? "gray8" : "gray16le");

        var expected = new Obj
        {
            ["width"] = first.Width,
            ["height"] = first.Height,
            ["pixelFormat"] = pixelFormat,
            ["colorModel"] = colorModel,
            ["bitsPerComponent"] = bits,
            ["orientation"] = expectation.Orientation,
            ["iccProfile"] = expectation.Icc is null ? "none" : model.StartsWith("gray", StringComparison.Ordinal) ? "gray" : "rgb",
        };
        if (expectation.Cicp is not null)
            expected["cicp"] = expectation.Cicp;
        if (expectation.IsLinear)
            expected["transferFunction"] = "linear";

        // The durations of the frames a viewer shows, read from the frame headers
        var durations = new List<string>();
        var delays = new List<object?>();
        var encodings = new List<string?>();
        if (expectation.LoopCount is { } loops)
        {
            Py.Assert(file.LoopCount == loops, $"{fixtureId}: {file.LoopCount} loops");
            var shown = file.GetDisplayedFrames();
            Py.Assert(shown.Count == frames.Count, $"{fixtureId}: {shown.Count} frames are shown");
            expected["animation"] = new Obj { ["totalPlays"] = loops == 0 ? null : loops, ["encodedLoopValue"] = loops };
            foreach (var frame in shown)
            {
                durations.Add(CommonCorpus.Normalize(frame.Duration * file.TicksPerSecondDenominator, file.TicksPerSecondNumerator));
                delays.Add(new Obj { ["ticks"] = frame.Duration, ["ticksPerSecondNumerator"] = file.TicksPerSecondNumerator, ["ticksPerSecondDenominator"] = file.TicksPerSecondDenominator });
                encodings.Add($"{(frame.IsModular ? "Modular" : "VarDCT")}, {CommonCorpus.Str(frame.Width)}x{CommonCorpus.Str(frame.Height)} at ({CommonCorpus.Str(frame.X)}, {CommonCorpus.Str(frame.Y)}), {BlendModeName(frame.BlendMode)}");
            }
        }
        else
        {
            Py.Assert(frames.Count == 1, $"{fixtureId}: a still image has one frame");
            expected["animation"] = null;
            durations.Add("0/1");
            delays.Add(null);
            encodings.Add(null);
        }

        expected["resolution"] = null;
        expected["profiles"] = new Obj
        {
            ["icc"] = expectation.Icc is not null ? CommonCorpus.ProfileRecord(expectation.Icc) : null,
            ["exif"] = expectation.Exif is not null ? CommonCorpus.ProfileRecord(expectation.Exif) : null,
            ["xmp"] = expectation.Xmp is not null ? CommonCorpus.ProfileRecord(expectation.Xmp) : null,
        };
        expected["text"] = new List<object?>();
        expected["frameCount"] = frames.Count;
        expected["frames"] = frames.Select((frame, i) => (object?)c.FrameEntry(fixtureId, "frame-" + CommonCorpus.Str(i), frame, durations[i], layouts, encodings[i], delays[i])).ToList();
        expected["poster"] = null;

        var input = c.Write(fixtureId + ".jxl", encoded.Data);
        input["role"] = "encoded";
        var features = Features(file);
        features.AddRange(extraFeatures ?? []);
        features.Sort(StringComparer.Ordinal);
        var fixture = new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = "jxl",
            ["input"] = input,
            ["provenance"] = c.Provenance(origin, function, parameters, ToolList(c), encoded.Commands),
            ["features"] = features,
            ["reference"] = reference,
            ["expected"] = expected,
            ["comparison"] = comparison ?? new Obj { ["mode"] = "exact" },
        };
        reference["crossChecks"] = checks;
        if (!string.IsNullOrEmpty(notes))
            fixture["notes"] = notes;
        c.Fixtures.Add(fixture);
    }

    private static List<string> ToolList(Corpus<JxlTools> c) =>
    [
        $"libjxl {c.Tools.LibjxlVersion} (cjxl, jxl_from_tree, djxl)",
        $"jxl-oxide {c.Tools.JxlOxideVersion} (cross-check)",
        ToolSet.RuntimeLabel,
    ];

    private static string BlendModeName(int mode) => mode switch
    {
        0 => "replace",
        1 => "add",
        2 => "blend",
        3 => "alpha-weighted add",
        _ => "multiply",
    };

    /// <summary>The feature tags of a file, from the fields the independent reader of the harness reads.</summary>
    private static List<string> Features(ReferenceJpegXl file)
    {
        var features = new SortedSet<string>(StringComparer.Ordinal)
        {
            file.IsContainer ? "jxl.file=container" : "jxl.file=codestream",
            "jxl.bits=" + CommonCorpus.Str(file.BitsPerSample),
            "jxl.colorSpace=" + (file.ColorSpace == 1 ? "gray" : "rgb"),
            "jxl.colorTransform=" + (file.IsXybEncoded ? "xyb" : "none"),
        };
        foreach (var box in file.BoxTypes.Where(type => type is not ("JXL " or "ftyp")))
            features.Add("jxl.box=" + box.Trim());

        // The boxes that follow the last part of the codestream: without them the file is complete too
        var boxes = file.BoxTypes.ToList();
        var lastPart = boxes.FindLastIndex(type => type is "jxlc" or "jxlp");
        if (lastPart >= 0 && lastPart < boxes.Count - 1)
        {
            features.Add("jxl.boxesAfterCodestream");
            if (boxes.Skip(lastPart + 1).Any(type => type is "Exif" or "xml " or "brob"))
                features.Add("jxl.metadataAfterCodestream");
        }
        if (file.ExtraChannels.Any(channel => channel.Type == 0))
            features.Add("jxl.alpha");
        if (file.Orientation != 1)
            features.Add("jxl.orientation=" + CommonCorpus.Str(file.Orientation));
        if (file.HasIccProfile)
        {
            features.Add("jxl.color=icc");
        }
        else if (file.Gamma is not null || file.TransferFunction != 13 || file.WhitePoint != 1 || (file.ColorSpace != 1 && file.Primaries != 1))
        {
            features.Add("jxl.color=enumerated");
        }

        if (file.HasAnimation)
        {
            features.Add("jxl.animation");
            features.Add(file.LoopCount == 0 ? "jxl.loop=infinite" : "jxl.loop=finite");
        }

        if (file.FrameHeaders.Count > 0)
        {
            long hidden = 0;
            var frames = file.FrameHeaders;
            IReadOnlyList<ReferenceJpegXlFrame> shown = file.Frames is null ? [] : file.GetDisplayedFrames();
            if (file.Frames is null)
                features.Add("jxl.toc=permuted");
            foreach (var frame in frames)
            {
                features.Add("jxl.encoding=" + (frame.IsModular ? "modular" : "vardct"));
                features.Add("jxl.blend=" + BlendModeName(frame.BlendMode).Replace(' ', '-'));
                if ((frame.Flags & 1) != 0)
                    features.Add("jxl.noise");
                if ((frame.Flags & 2) != 0)
                    features.Add("jxl.patches");
                if ((frame.Flags & 16) != 0)
                    features.Add("jxl.splines");
                if (frame.IsYCbCr)
                    features.Add("jxl.colorTransform=ycbcr");
                if (frame.Upsampling != 1)
                    features.Add("jxl.upsampling=" + CommonCorpus.Str(frame.Upsampling));
                if (frame.PassCount != 1)
                    features.Add("jxl.passes");
                if (frame.SectionCount != 1)
                    features.Add("jxl.sections=many");
                if (frame.Type != 0)
                    features.Add("jxl.frame=" + (frame.Type == 1 ? "lf" : frame.Type == 2 ? "reference" : "skip-progressive"));
                var isFull = frame.X == 0 && frame.Y == 0 && frame.Width == file.Width && frame.Height == file.Height;
                if (!isFull && frame.Type is 0 or 3)
                    features.Add("jxl.frame=partial");

                // The pixels the decoder produces without showing them: the frames that are not shown, and what a shown
                // frame has beyond the image
                var pixels = (long)frame.Width * frame.Height;
                hidden += shown.Contains(frame) ? Math.Max(0, pixels - ((long)file.Width * file.Height)) : pixels;
            }

            if (file.Frames is not null && frames.Count != shown.Count)
                features.Add("jxl.frame=hidden");
            if (file.Frames is not null && hidden != 0)
                features.Add("jxl.hiddenPixels=" + CommonCorpus.Str(hidden));
        }

        return [.. features];
    }

    private static string DjxlLabel(Corpus<JxlTools> c) => $"libjxl {c.Tools.LibjxlVersion} djxl";

    private static string OxideLabel(Corpus<JxlTools> c) => $"jxl-oxide {c.Tools.JxlOxideVersion}";

    /// <summary>Runs both decoders on an input and compares them with the expected frames.</summary>
    private static List<Obj> CrossChecks(Corpus<JxlTools> c, byte[] data, IReadOnlyList<Img> expected, bool lossless, int orientation = 1)
    {
        var path = c.Scratch / "check.jxl";
        File.WriteAllBytes(path, data);
        var djxl = Check(DjxlLabel(c), Djxl(c, path), expected, "The reference decoder differs from the expected samples.", orientation);
        var oxide = Check(OxideLabel(c), JxlOxide(c, path), expected,
            "An independent decoder: JPEG XL defines lossy decoding in floating point, so two conforming decoders round some samples differently.", orientation);
        if (lossless)
        {
            Py.Assert((string?)djxl["result"] == "exact", "djxl does not give the expected samples: " + PyJson.Dumps(djxl));
            Py.Assert((string?)oxide["result"] == "exact", "jxl-oxide does not give the expected samples: " + PyJson.Dumps(oxide));
        }

        return [djxl, oxide];
    }

    /// <summary>The comparison policy of a lossy fixture, in units of the 8-bit scale.</summary>
    private static Obj LossyPolicy(string name, IEnumerable<Obj> checks)
    {
        var (maxError, meanError, justifiedMax, justifiedMean) = CommonCorpus.JpegJustifiedTolerance(checks);

        // Decoders that agree on every sample leave no room for a tolerance: the fixture is compared exactly
        if (maxError == 0)
            return new Obj { ["mode"] = "exact" };

        var cross = $"the independent decoder differs from the reference by max {CommonCorpus.Str(maxError)}, mean {Py.FormatFixed(meanError, 4)}, which would justify max {CommonCorpus.Str(justifiedMax)}, mean {Py.FormatFixed(justifiedMean, 2)}";
        if (!LibraryJxlMeasurements.TryGetValue("jxl/" + name, out var measured))
        {
            return new Obj
            {
                ["mode"] = "tolerance",
                ["maxAbsoluteError"] = justifiedMax,
                ["maxMeanAbsoluteError"] = justifiedMean,
                ["justification"] = $"Lossy JPEG XL decoding is defined in floating point; the library is not measured yet: {cross}.",
            };
        }

        var max = Math.Min(justifiedMax, Math.Max(1, measured.Max));
        var mean = Math.Min(justifiedMean, Math.Ceiling(Math.Round((measured.Mean + 0.01) * 100, 6, MidpointRounding.AwayFromZero)) / 100);
        return new Obj
        {
            ["mode"] = "tolerance",
            ["maxAbsoluteError"] = max,
            ["maxMeanAbsoluteError"] = mean,
            ["justification"] = $"Lossy JPEG XL decoding is defined in floating point (measured: max {CommonCorpus.Str(measured.Max)}, mean {Py.FormatFixed(measured.Mean, 4)}). Allowed: the measured max (at least 1) and the measured mean + 0.01; {cross}.",
        };
    }

    [GeneratedRegex(@"'shape': \((?<shape>[^)]*)\)", RegexOptions.ExplicitCapture)]
    private static partial Regex ShapeRegex();

    /// <summary>The pinned tools of the JPEG XL generator, from the meziantou/prebuilt release.</summary>
    private sealed partial class JxlTools : ToolSet
    {
        public JxlTools(bool acceptVersions)
            : base(Download(PrebuiltTools.Ffmpeg))
        {
            Cjxl = Download(PrebuiltTools.Cjxl);
            Djxl = Download(PrebuiltTools.Djxl);
            JxlFromTree = Download(PrebuiltTools.JxlFromTree);
            JxlOxide = Download(PrebuiltTools.JxlOxide);
            var cjxl = LibjxlVersionRegex().Match(Proc.RunText([Cjxl, "--version"]));
            var djxl = LibjxlVersionRegex().Match(Proc.RunText([Djxl, "--version"]));
            if (!cjxl.Success || !djxl.Success || cjxl.Groups["version"].Value != djxl.Groups["version"].Value)
                throw new FatalException("Cannot read one libjxl version from cjxl and djxl.");

            var oxide = OxideVersionRegex().Match(Proc.RunText([JxlOxide, "--version"]));
            if (!oxide.Success)
                throw new FatalException("Cannot read the version of jxl-oxide.");

            LibjxlVersion = cjxl.Groups["version"].Value;
            JxlOxideVersion = oxide.Groups["version"].Value;
            var mismatches = new List<string>();
            if (LibjxlVersion != PinnedLibjxl)
                mismatches.Add($"libjxl {LibjxlVersion} (pinned {PinnedLibjxl})");
            if (JxlOxideVersion != PinnedJxlOxide)
                mismatches.Add($"jxl-oxide {JxlOxideVersion} (pinned {PinnedJxlOxide})");
            if (mismatches.Count > 0 && !acceptVersions)
                throw new FatalException("Tool versions differ from the pinned versions: " + string.Join(", ", mismatches) + ". Review the impact and rerun with --accept-tool-versions (then update the pinned versions).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Ffmpeg] = "ffmpeg",
                [Cjxl] = "cjxl",
                [Djxl] = "djxl",
                [JxlFromTree] = "jxl_from_tree",
                [JxlOxide] = "jxl-oxide",
            };
        }

        public string Cjxl { get; }

        public string Djxl { get; }

        public string JxlFromTree { get; }

        public string JxlOxide { get; }

        public string LibjxlVersion { get; }

        public string JxlOxideVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();

        [GeneratedRegex(@"^[cd]jxl v(?<version>\d+\.\d+\.\d+)\b", RegexOptions.ExplicitCapture | RegexOptions.Multiline)]
        private static partial Regex LibjxlVersionRegex();

        [GeneratedRegex(@"(?<version>\d+\.\d+\.\d+)", RegexOptions.ExplicitCapture)]
        private static partial Regex OxideVersionRegex();
    }
}
