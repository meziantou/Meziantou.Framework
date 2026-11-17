using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Dng;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the DNG fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/dng, invalid/dng and
/// limit/dng entries). This generator is the reviewed, reproducible recipe of every committed DNG fixture. It is NEVER run
/// by the tests and does not use the library under test.
/// <para>
/// The files are the scenes of <see cref="DngScenes"/> (test harness), assembled by <see cref="DngFileBuilder"/> from the
/// DNG specification 1.7.1: smooth color fields seen through a color filter array, or as linear raw data, with one feature
/// of the format each. Two more are those files rewritten by <c>dng_validate</c> of the Adobe DNG SDK (lossless JPEG and
/// JPEG XL tiles), whose dates are normalized. The reference of a file is its rendering by <see cref="ReferenceDng"/>
/// (test harness), an independent implementation of the processing model the library documents: the linear values of
/// chapter 5, the opcodes, a bilinear demosaicing and the color transform of chapter 6 to linear BT.709 (D65).
/// </para>
/// <para>
/// Every valid file is cross-checked with <c>dng_validate</c> 1.7.1 of the pinned <c>Meziantou.Prebuilt</c> release: its
/// stage 1 (the stored samples) must be the samples of the scene, its stage 2 (the linear values) must be the one of the
/// reference to within its 16-bit rounding (or a measured, documented difference of an opcode), and its rendering
/// (<c>-32 -tif</c>, sRGB-encoded, made linear by the identity <c>ProfileToneCurve</c> and <c>DefaultBlackRender</c> 1 of
/// the scenes) is compared with the reference: the demosaicing algorithms differ, so the difference is measured and
/// recorded, excluded from the tolerance.
/// </para>
/// It only replaces the DNG entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format "dng") and their
/// files, and keeps every other entry and file unchanged.
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- dng
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- dng --write
/// Requirements: dng_validate and FFmpeg from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt package. Any OS.
/// </summary>
internal static partial class DngCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/DngCorpus.cs";

    // The DNG SDK version of the tools of the pinned meziantou/prebuilt release
    private const string PinnedDngSdk = "1.7.1";

    private static bool IsDngFixture(Obj fixture) => (string?)fixture["format"] == "dng";

    public static int Run(GeneratorOptions options)
    {
        var tools = new DngTools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-dng-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => $"{fixtures} DNG fixtures; corpus: {files} files, {bytes} bytes",
            "The committed DNG fixtures are reproducible.");
    }

    private static int Generate(FullPath outDir, DngTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<DngTools>(outDir, tools, ScriptPath))
        {
            foreach (var (name, description, builder) in DngScenes.All)
            {
                AddValid(corpus, name, nameof(DngScenes), builder.Build(), builder, description, Features(name, builder));
            }

            BuildRewritten(corpus);
            BuildInvalid(corpus);
            BuildUnsupported(corpus);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsDngFixture(fixture), (string?)fixture["id"]);

        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsDngFixture);
        return fixtures.Count;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The files of the scene <c>bayer-rggb</c> rewritten by the DNG SDK: lossless JPEG tiles, and JPEG XL tiles of interleaved fields.</summary>
    private static void BuildRewritten(Corpus<DngTools> c)
    {
        const string Function = nameof(BuildRewritten);
        var builder = DngScenes.Get("bayer-rggb");
        var source = c.Scratch / "rewrite-source.dng";
        File.WriteAllBytes(source, builder.Build());
        foreach (var (name, options, description, features) in new[]
        {
            ("sdk-lossless-jpeg", Array.Empty<string>(), "The scene bayer-rggb rewritten by the DNG SDK (dng_validate -dng): lossless JPEG tiles of two components, an 8-bit RGB preview in IFD 0.", new[] { "dng.compression=lossless-jpeg", "dng.preview" }),
            ("sdk-jpeg-xl", ["-losslessJXL"], "The scene bayer-rggb rewritten by the DNG SDK with -losslessJXL: DNG 1.7.1, a lossless JPEG XL tile whose CFA is stored as four sub-images (RowInterleaveFactor and ColumnInterleaveFactor 2), a JPEG preview in IFD 0.", ["dng.compression=jpeg-xl", "dng.interleave", "dng.preview"]),
        })
        {
            var output = c.Scratch / name;
            List<string> command = [c.Tools.DngValidate, .. options, "-dng", output.Value, source.Value];
            DngTools.Run(command);
            var data = NormalizeDates(File.ReadAllBytes(output.Value + ".dng"));
            var display = CommonCorpus.Display(command, c.Tools, [(source.Value, "{bayer-rggb}"), (output.Value, "{output}")]);
            AddValid(c, name, Function, data, builder, description, [.. features, "dng.cfa=2x2", "dng.bits=16", "dng.photometric=cfa"], commands: [display]);
        }
    }

    private static void BuildInvalid(Corpus<DngTools> c)
    {
        const string Function = nameof(BuildInvalid);
        AddError(c, "invalid", "truncated-samples", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.UInt32s(273, (uint)builder.Build().Length - 100))), Invalid(), "The strip of the raw image starts 100 bytes before the end of the file, which it runs past.");
        AddError(c, "invalid", "missing-cfa-pattern", Function, Edit(builder => builder.OmittedRawTags.Add(33422)), Invalid(), "A color filter array without its CFAPattern tag.");
        AddError(c, "invalid", "cfa-pattern-color-unlisted", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.Bytes(50710, [0, 1, 3]))), Invalid(), "The CFAPattern uses the color 2 (blue), which CFAPlaneColor (0, 1, 3) does not list.");
        AddError(c, "invalid", "active-area-outside", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.UInt32s(50829, 0, 0, 40, 48))), Invalid(), "An ActiveArea of 40 rows in an image of 32.");
        AddError(c, "invalid", "default-crop-outside", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.Rationals(50720, [49, 32]))), Invalid(), "A DefaultCropSize wider than the active area.");
        AddError(c, "invalid", "white-below-black", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.Rationals(50714, [5000]))), Invalid(), "A black level of 5000 above the white level of 4000.");
        AddError(c, "invalid", "opcode-list-truncated", Function, Edit(builder => builder.ExtraRawTags.Add(new DngTag(51009, 7, 20, new byte[] { 0, 0, 0, 1, 0, 0, 0, 12, 1, 3, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0 }))), Invalid(), "An OpcodeList2 whose opcode declares 256 bytes of parameters, which the list does not have.");
        AddError(c, "invalid", "color-matrix-singular", Function, Copy(DngScenes.Get("bayer-rggb"), colorMatrix1: [1, 0, 0, 1, 0, 0, 0, 0, 1]).Build(), Invalid(), "A ColorMatrix1 whose rows are not independent: it cannot be inverted.");
        AddError(c, "invalid", "dng-version-zero", Function, Copy(DngScenes.Get("bayer-rggb"), dngVersion: [0, 0, 0, 0]).Build(), Invalid(), "A DNGVersion of 0.0.0.0.");

        static byte[] Edit(Action<DngFileBuilder> edit)
        {
            var builder = Copy(DngScenes.Get("bayer-rggb"));
            edit(builder);
            return builder.Build();
        }
    }

    private static void BuildUnsupported(Corpus<DngTools> c)
    {
        const string Function = nameof(BuildUnsupported);
        AddError(c, "unsupported", "cfa-layout-staggered", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.Shorts(50711, 2))), Unsupported("CFALayout: 2"), "A staggered color filter array (CFALayout 2, the Fujifilm SuperCCD), which is not interpolated.");
        AddError(c, "unsupported", "default-scale", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.Rationals(50718, [2, 1]))), Unsupported("DefaultScale"), "Non-square pixels (DefaultScale 2, 1), which are not resampled.");
        AddError(c, "unsupported", "backward-version", Function, Edit(builder => builder.ExtraIfd0Tags.Add(DngTag.Bytes(50707, [2, 0, 0, 0]))), Unsupported("DNGBackwardVersion: 2.0.0.0"), "A DNGBackwardVersion of 2.0.0.0: the file needs a newer reader.");
        AddError(c, "unsupported", "required-opcode", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.Undefined(51022, new DngOpcodeWriter().Add(6, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 32, 0, 0, 0, 48]).ToArray()))), Unsupported("Opcode: TrimBounds"),
            "An OpcodeList3 with a required TrimBounds opcode, which is not implemented (a lenient decoding skips it).");
        AddError(c, "unsupported", "signed-samples", Function, Edit(builder => builder.ExtraRawTags.Add(DngTag.Shorts(339, 2))), Unsupported("SampleFormat: signed integers in a raw IFD"), "Signed integer samples, which DNG does not use.");
        AddError(c, "unsupported", "third-illuminant", Function, Edit(builder => builder.ExtraIfd0Tags.Add(DngTag.Shorts(52529, 23))), Unsupported("CalibrationIlluminant3"), "A third calibration (CalibrationIlluminant3, DNG 1.6).");

        static byte[] Edit(Action<DngFileBuilder> edit)
        {
            var builder = Copy(DngScenes.Get("bayer-rggb"));
            edit(builder);
            return builder.Build();
        }
    }

    private static void BuildLimits(Corpus<DngTools> c)
    {
        const string Function = nameof(BuildLimits);
        var data = DngScenes.Get("bayer-rggb").Build();
        AddError(c, "limit", "frame-pixels-over-limit", Function, data, new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "FramePixels" }, "The raw image has 1,536 pixels.",
            decodeOptions: new Obj { ["limits"] = new Obj { ["MaxFramePixels"] = 1535 } });
        var opcodes = new DngOpcodeWriter().MapTable(new DngOpcodeArea(0, 0, 32, 48), [.. Enumerable.Range(0, 65536).Select(value => (ushort)value)]).ToArray();
        var builder = Copy(DngScenes.Get("bayer-rggb"));
        builder.ExtraRawTags.Add(DngTag.Undefined(51009, opcodes));
        AddError(c, "limit", "metadata-bytes-over-limit", Function, builder.Build(), new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "MetadataBytes" }, "An OpcodeList2 of 131,124 bytes (a MapTable of 65,536 entries).",
            decodeOptions: new Obj { ["limits"] = new Obj { ["MaxMetadataBytes"] = 65536 } });
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Entries
    // -----------------------------------------------------------------------------------------------------------------

    private static List<string> Features(string name, DngFileBuilder builder)
    {
        var features = new List<string>
        {
            "dng.photometric=" + (builder.Photometric == 32803 ? "cfa" : "linear-raw"),
            "dng.bits=" + builder.BitsPerSample.ToString(CultureInfo.InvariantCulture) + (builder.IsFloatingPoint ? "-float" : ""),
            "dng.compression=" + (builder.Compression == 8 ? "deflate" : "none"),
        };

        if (builder.Photometric == 32803)
        {
            features.Add(string.Create(CultureInfo.InvariantCulture, $"dng.cfa={builder.CfaRows}x{builder.CfaColumns}"));
        }

        if (builder.Predictor != 1)
        {
            features.Add("dng.predictor=" + builder.Predictor.ToString(CultureInfo.InvariantCulture));
        }

        if (builder.Tile is not null)
        {
            features.Add("dng.tiles");
        }

        if (builder.IsBigEndian)
        {
            features.Add("dng.big-endian");
        }

        if (builder.LinearizationTable is not null)
        {
            features.Add("dng.linearization");
        }

        if (builder.BlackLevelRepeatDim is not null || builder.BlackLevelDeltaH is not null)
        {
            features.Add("dng.black-level-pattern");
        }

        if (builder.ActiveArea is not null || builder.DefaultCropOrigin is not null)
        {
            features.Add("dng.active-area");
        }

        if (builder.ColorMatrix2 is not null)
        {
            features.Add("dng.two-illuminants");
        }

        if (builder.ForwardMatrix1 is not null)
        {
            features.Add("dng.forward-matrix");
        }

        if (builder.AsShotWhiteXY is not null)
        {
            features.Add("dng.white-xy");
        }

        if (builder.BaselineExposure is not null)
        {
            features.Add("dng.baseline-exposure");
        }

        for (var i = 0; i < 3; i++)
        {
            if ((i switch { 0 => builder.OpcodeList1, 1 => builder.OpcodeList2, _ => builder.OpcodeList3 }) is not null)
            {
                features.Add("dng.opcodes=" + (i + 1).ToString(CultureInfo.InvariantCulture));
            }
        }

        if (builder.ThumbnailWidth > 0)
        {
            features.Add("dng.preview");
            features.Add("dng.orientation");
        }

        if (builder.Xmp is not null)
        {
            features.Add("dng.xmp");
        }

        _ = name;
        features.Sort(StringComparer.Ordinal);
        return features;
    }

    /// <summary>Records a valid fixture whose reference is the rendering of <see cref="ReferenceDng"/> from the samples of the scene.</summary>
    private static void AddValid(Corpus<DngTools> c, string name, string function, byte[] data, DngFileBuilder builder, string description, List<string> features, List<string>? commands = null)
    {
        var fixtureId = "dng/" + name;
        var rendering = ReferenceDng.Render(data, builder.Samples);
        var checks = new List<object?>
        {
            new Obj
            {
                ["decoder"] = "ReferenceDng (test harness: an independent implementation of the processing model of the DNG specification 1.7.1)",
                ["result"] = "exact",
                ["maxAbsoluteError"] = 0,
                ["meanAbsoluteError"] = 0.0,
                ["notes"] = "The reference renders the samples of the scene with the tags of the file.",
            },
        };

        checks.AddRange(SdkCrossChecks(c, data, builder, rendering, features));
        var input = c.Write(fixtureId + ".dng", data);
        input["role"] = "encoded";
        var expected = new Obj
        {
            ["width"] = rendering.Width,
            ["height"] = rendering.Height,
            ["pixelFormat"] = IsMonochrome(builder) ? "GrayFloat" : "RgbaFloat",
            ["colorModel"] = IsMonochrome(builder) ? "Grayscale" : "Rgb",
            ["bitsPerComponent"] = 32,
            ["orientation"] = builder.Orientation,
            ["iccProfile"] = "none",
            ["transferFunction"] = "linear",
            ["animation"] = null,
            ["resolution"] = null,
            ["profiles"] = new Obj
            {
                ["icc"] = null,
                ["exif"] = DngExifReference.Build(data) is { } exif ? new Obj { ["sha256"] = Bytes.Sha256Hex(exif), ["length"] = exif.Length } : null,
                ["xmp"] = new TiffFile(data) is var file && file.ReadDirectory(file.FirstDirectory).TryBytes(700) is { } xmp ? new Obj { ["sha256"] = Bytes.Sha256Hex(xmp), ["length"] = xmp.Length } : null,
            },
            ["text"] = new List<object?>(),
            ["frameCount"] = 1,
        };

        var pixels = new byte[rendering.Rgba.Length * 4];
        for (var i = 0; i < rendering.Rgba.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(pixels.AsSpan(i * 4), rendering.Rgba[i]);
        }

        expected["frames"] = new List<object?> { new Obj { ["duration"] = "0/1", ["buffers"] = new List<object?> { FloatBuffer(c, fixtureId + ".frame-0.rgbaf32le.raw", rendering.Width, rendering.Height, pixels) } } };
        expected["poster"] = null;
        var provenance = c.Provenance("generated", function, new Obj { ["scene"] = builder == DngScenes.Get("bayer-rggb") && name != "bayer-rggb" ? "bayer-rggb" : name }, [$"Adobe DNG SDK {c.Tools.DngSdkVersion} (dng_validate, cross-check{(commands is null ? "" : ", encoder")})", ToolSet.RuntimeLabel], commands);
        c.Fixtures.Add(new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = "dng",
            ["input"] = input,
            ["provenance"] = provenance,
            ["features"] = features,
            ["reference"] = new Obj
            {
                ["method"] = "hand-computed",
                ["description"] = description + " The reference is the rendering of the default crop to scene-referred linear light: the linear values of chapter 5, the opcodes, the bilinear demosaicing, and the camera to XYZ (D50) transform of chapter 6 adapted to linear BT.709 (D65) by Bradford, with the baseline exposure; an alpha of 1.",
                ["crossChecks"] = checks,
            },
            ["expected"] = expected,
            ["comparison"] = new Obj { ["mode"] = "exact" },
        });
    }

    /// <summary>The cross-checks with dng_validate: its stage 1 and stage 2, and its rendering.</summary>
    private static IEnumerable<Obj> SdkCrossChecks(Corpus<DngTools> c, byte[] data, DngFileBuilder builder, DngRendering rendering, List<string> features)
    {
        var path = c.Scratch / "check.dng";
        File.WriteAllBytes(path, data);
        var stage1 = c.Scratch / "stage1";
        var stage2 = c.Scratch / "stage2";
        var render = c.Scratch / "render";
        List<string> command = [c.Tools.DngValidate, "-1", stage1.Value, "-2", stage2.Value, "-32", "-tif", render.Value, path.Value];
        DngTools.Run(command);
        var display = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}"), (stage1.Value, "{stage1}"), (stage2.Value, "{stage2}"), (render.Value, "{render}")]);

        // Stage 1: the stored samples (before opcode list 1), in the layout of the stored image; the floating-point ones are rounded to their bits
        var (_, _, _, s1) = ReadUncompressedTiff(File.ReadAllBytes(stage1.Value + ".tif"));
        var hasList1 = builder.OpcodeList1 is not null;
        var stage1Differs = 0;
        if (!hasList1)
        {
            for (var i = 0; i < Math.Min(s1.Length, builder.Samples.Length); i++)
            {
                var expected = builder.IsFloatingPoint ? (builder.BitsPerSample == 16 ? (double)(Half)builder.Samples[i] : (float)builder.Samples[i]) : builder.Samples[i];
                if (s1[i] != expected)
                    stage1Differs++;
            }

            Py.Assert(stage1Differs == 0 && s1.Length == builder.Samples.Length, "dng_validate reads the stored samples of the scene");
        }

        // Stage 2: 16-bit integers of the linear values (floats for floating-point samples)
        var (_, _, _, s2) = ReadUncompressedTiff(File.ReadAllBytes(stage2.Value + ".tif"));
        Py.Assert(s2.Length == rendering.Stage2.Length, "dng_validate's stage 2 is the active area");
        var scale = builder.IsFloatingPoint ? 1 : 65535.0;
        var max2 = 0.0;
        for (var i = 0; i < s2.Length; i++)
        {
            max2 = Math.Max(max2, Math.Abs((rendering.Stage2[i] * scale) - s2[i]) * (65535 / scale));
        }

        var stage2Agrees = max2 <= 0.501 || (builder.IsFloatingPoint && max2 <= 0.01);
        var stage2Check = new Obj
        {
            ["decoder"] = $"Adobe DNG SDK {c.Tools.DngSdkVersion} (dng_validate -1 and -2: the stored samples and the linear values of the active area, before demosaicing)",
            ["command"] = display,
            ["result"] = stage2Agrees ? "equivalent" : "differs",
            ["maxAbsoluteError"] = 0,
            ["excludedFromTolerance"] = true,
            ["notes"] = (hasList1 ? "Opcode list 1 changes the stored samples. " : "Its stage 1 is the samples of the scene, exactly. ")
                + string.Create(CultureInfo.InvariantCulture, $"Its stage 2 differs from the linear values of the reference by at most {max2:0.###} units of 1/65535")
                + (stage2Agrees
                    ? (builder.IsFloatingPoint ? "." : ": the rounding of its 16-bit samples (and the single precision of the reference).")
                    : (features.Contains("dng.opcodes=1")
                        ? ": the DNG SDK repairs bad pixels with its own interpolation, which the specification leaves to the reader (the library uses the mean of the good neighbors of the same color)."
                        : features.Contains("dng.black-level-pattern")
                            ? ": the DNG SDK rounds the black levels of BlackLevelDeltaH and BlackLevelDeltaV differently."
                            : ": dng_validate 1.7.1 leaves the samples unchanged with DeltaPerRow and DeltaPerColumn (measured with each opcode alone), where the specification adds the deltas; the other opcodes of the list agree.")),
        };

        if (!stage2Agrees)
        {
            stage2Check.Remove("maxAbsoluteError");
        }

        yield return stage2Check;

        // The rendering: sRGB-encoded floats, compared after encoding the reference the same way
        var (w, h, s, r) = ReadUncompressedTiff(File.ReadAllBytes(render.Value + ".tif"));
        var rotated = builder.Orientation != 1;
        double max = 0, sum = 0;
        var count = 0;
        if (!rotated && w == rendering.Width && h == rendering.Height)
        {
            for (var i = 0; i < w * h; i++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var difference = Math.Abs(EncodeSrgb(rendering.Rgba[(i * 4) + channel]) - r[(i * s) + Math.Min(channel, s - 1)]) * 255;
                    max = Math.Max(max, difference);
                    sum += difference;
                    count++;
                }
            }
        }

        var renderCheck = new Obj
        {
            ["decoder"] = $"Adobe DNG SDK {c.Tools.DngSdkVersion} (dng_validate -32 -tif: the rendering, sRGB-encoded)",
            ["command"] = display,
            ["result"] = "differs",
            ["excludedFromTolerance"] = true,
        };

        if (count > 0)
        {
            renderCheck["maxAbsoluteError"] = (int)Math.Ceiling(max);
            renderCheck["meanAbsoluteError"] = Math.Round(sum / count, 4, MidpointRounding.AwayFromZero);
            renderCheck["notes"] = string.Create(CultureInfo.InvariantCulture, $"Compared with the reference encoded to sRGB and clipped to [0, 1], in 8-bit units: max {max:0.##}, mean {sum / count:0.####}. The DNG SDK demosaics with its own algorithm (the library interpolates bilinearly), and rolls off the highlights its exposure raises; the identity ProfileToneCurve and DefaultBlackRender 1 of the scene keep its rendering colorimetric.");
        }
        else
        {
            renderCheck["notes"] = rotated
                ? "The DNG SDK applies the orientation of the file to its rendering, which the library reports and does not apply: not compared pixel by pixel."
                : "The DNG SDK renders monochrome data as one gray channel of gamma 2.2: not compared pixel by pixel.";
        }

        yield return renderCheck;
    }

    private static double EncodeSrgb(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value <= 0.0031308 ? 12.92 * value : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
    }

    /// <summary>Reads an uncompressed little-endian TIFF of one strip, as dng_validate writes its stage images.</summary>
    private static (int Width, int Height, int Samples, double[] Values) ReadUncompressedTiff(byte[] data)
    {
        var file = new TiffFile(data);
        var directory = file.ReadDirectory(file.FirstDirectory);
        var width = (int)directory.Number(256);
        var height = (int)directory.Number(257);
        var samples = (int)(directory.TryNumber(277) ?? 1);
        var bits = (int)directory.Numbers(258)[0];
        var isFloat = directory.TryNumber(339) == 3;
        var offsets = directory.Numbers(273);
        var result = new double[width * height * samples];
        var bytes = bits / 8;
        var position = (int)offsets[0];
        for (var i = 0; i < result.Length; i++)
        {
            var sample = data.AsSpan(position + (i * bytes));
            result[i] = (bits, isFloat) switch
            {
                (32, true) => BinaryPrimitives.ReadSingleLittleEndian(sample),
                (16, true) => (double)BinaryPrimitives.ReadHalfLittleEndian(sample),
                (16, _) => BinaryPrimitives.ReadUInt16LittleEndian(sample),
                (8, _) => sample[0],
                _ => throw new FatalException("Unexpected stage image layout."),
            };
        }

        return (width, height, samples, result);
    }

    private static Obj FloatBuffer(Corpus<DngTools> c, string path, int width, int height, byte[] bytes)
    {
        var entry = c.Write(path, bytes);
        entry["layout"] = "rgbaf32le";
        entry["width"] = width;
        entry["height"] = height;
        entry["rowBytes"] = width * 16;
        entry["byteLength"] = bytes.Length;
        entry["alpha"] = "straight";
        entry["rowOrder"] = "top-down";
        entry["compression"] = "none";
        return entry;
    }

    private static void AddError(Corpus<DngTools> c, string kind, string name, string function, byte[] data, Obj expectedError, string notes, Obj? decodeOptions = null)
    {
        var fixtureId = kind == "limit" ? "limit/dng/" + name : "invalid/dng/" + name;
        if (kind == "invalid")
        {
            // dng_validate rejects the file too, or reports the defect as an error
            var path = c.Scratch / "error.dng";
            File.WriteAllBytes(path, data);
            var result = Proc.Exec([c.Tools.DngValidate, path.Value]);
            var output = Encoding.UTF8.GetString(result.Stdout) + Encoding.UTF8.GetString(result.Stderr);
            if (result.ExitCode == 0 && !output.Contains("*** Error", StringComparison.Ordinal))
            {
                notes += " dng_validate accepts the file.";
            }
        }

        var provenance = c.Provenance("generated", function, null, kind == "invalid" ? [$"Adobe DNG SDK {c.Tools.DngSdkVersion} (dng_validate, cross-check)", ToolSet.RuntimeLabel] : [ToolSet.RuntimeLabel]);
        c.AddError(fixtureId, kind, fixtureId + ".dng", data, "dng", provenance, expectedError, decodeOptions: decodeOptions, notes: notes);
    }

    /// <summary>Copies the samples and the levels of a builder, with another color matrix or DNG version.</summary>
    /// <summary>Gets a value indicating whether a raw image is monochrome: one linear raw sample per pixel, whose rendering is a gray level.</summary>
    private static bool IsMonochrome(DngFileBuilder builder) => builder.Photometric != 32803 && builder.SamplesPerPixel == 1;

    private static DngFileBuilder Copy(DngFileBuilder builder, double[]? colorMatrix1 = null, byte[]? dngVersion = null) => new()
    {
        Width = builder.Width,
        Height = builder.Height,
        Samples = builder.Samples,
        BlackLevel = builder.BlackLevel,
        WhiteLevel = builder.WhiteLevel,
        ColorMatrix1 = colorMatrix1 ?? builder.ColorMatrix1,
        AsShotNeutral = builder.AsShotNeutral,
        DngVersion = dngVersion ?? builder.DngVersion,
    };

    /// <summary>Replaces the dates dng_validate writes (DateTime and the XMP dates) with a fixed one of the same length.</summary>
    private static byte[] NormalizeDates(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data);
        text = TiffDateRegex().Replace(text, "2026:01:01 00:00:00");
        text = XmpDateRegex().Replace(text, "2026-01-01T00:00:00");
        var result = Encoding.Latin1.GetBytes(text);
        Py.Assert(result.Length == data.Length);
        return result;
    }

    [GeneratedRegex(@"\d{4}:\d{2}:\d{2} \d{2}:\d{2}:\d{2}")]
    private static partial Regex TiffDateRegex();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}")]
    private static partial Regex XmpDateRegex();

    private static Obj Invalid() => new() { ["exception"] = "InvalidImageContentException", ["format"] = "Dng" };

    private static Obj Unsupported(string feature) => new() { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Dng", ["feature"] = feature };

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    private sealed class DngTools : ToolSet
    {
        public DngTools(bool acceptVersions)
            : base(PrebuiltTools.Ffmpeg.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            DngValidate = PrebuiltTools.DngValidate.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            DngSdkVersion = PrebuiltTools.DngValidate.Version;
            if (DngSdkVersion != PinnedDngSdk && !acceptVersions)
                throw new FatalException($"Tool versions differ from the pinned versions: DNG SDK {DngSdkVersion} (pinned {PinnedDngSdk}). Review the impact and rerun with --accept-tool-versions (then update the pinned versions).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [DngValidate] = "dng_validate" };
        }

        public string DngValidate { get; }

        public string DngSdkVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        /// <summary>Runs dng_validate in UTC, so that the dates it writes do not depend on the time zone.</summary>
        public static void Run(IReadOnlyList<string> command)
        {
            var previous = Environment.GetEnvironmentVariable("TZ");
            Environment.SetEnvironmentVariable("TZ", "UTC");
            try
            {
                var result = Proc.Exec(command);
                if (result.ExitCode != 0)
                    throw new FatalException($"dng_validate failed ({result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {Encoding.UTF8.GetString(result.Stdout)}{Encoding.UTF8.GetString(result.Stderr)}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TZ", previous);
            }
        }
    }
}
