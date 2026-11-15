using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Radiance;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the Radiance HDR fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/hdr, invalid/hdr and
/// limit/hdr entries). This generator is the reviewed, reproducible recipe of every committed Radiance HDR fixture. It is
/// NEVER run by the tests and does not use the library under test.
/// <para>
/// Most files are assembled by <see cref="RadianceFileBuilder"/> (test harness) from the Radiance file format documentation
/// ("Picture File Format") and "Real Pixels" (Graphics Gems II): RGBE patterns in the standard orientation, stored flat,
/// with the "old" or the "new" run-length encoding, in the eight orientations, with header variables. Their reference is
/// the pattern (hand-computed), read as the reference routines of Radiance read a pixel, <c>(m + 0.5) × 2^(e - 136)</c>;
/// <see cref="ReferenceRadiance"/>, an independent reader, must give the pattern back from the file. Other files are written
/// by the Radiance encoder of FFmpeg (the new run-length encoding), from floating-point patterns; their reference is the
/// RGBE pixels <see cref="ReferenceRadiance"/> reads, read the same way.
/// </para>
/// <para>
/// Every valid file is cross-checked with the Radiance decoder of FFmpeg, which reads a mantissa as the bottom of its
/// interval, <c>m × 2^(e - 136)</c>, and ignores the orientation of the resolution string: the check requires that FFmpeg
/// gives exactly the reference minus half a unit of each exponent, for the scanlines in file order, and records it as a
/// documented difference excluded from the tolerance. <c>pvalue</c> of the Radiance distribution (the reference
/// implementation, from the pinned <c>Meziantou.Prebuilt</c> release) must give exactly the reference, in file order, and
/// must reject every invalid fixture but the three it tolerates (<see cref="ToleratedByRadiance"/>). The cross-checks are a
/// list (<see cref="CrossChecks"/>), which other tools join
/// release provides them.
/// </para>
/// <para>
/// Malformed inputs are byte edits or hand-assembled defects, each confirmed by <see cref="ReferenceRadiance"/> (which
/// rejects it). XYZE files are unsupported fixtures: identified, rejected by a load.
/// </para>
/// It only replaces the Radiance HDR entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format "hdr") and
/// their files, and keeps every other entry and file unchanged.
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- hdr
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- hdr --write
/// Requirements: FFmpeg and pvalue from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt package. Any OS.
/// </summary>
internal static class RadianceCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/RadianceCorpus.cs";

    // The FFmpeg version of the committed fixtures: another version may write other RGBE bytes
    private const string PinnedFfmpeg = "9.0.2";

    // The Radiance version of the tools of the pinned meziantou/prebuilt release
    private const string PinnedRadiance = "6.1a+5b2a2154";

    // The invalid fixtures pvalue reads: Radiance clips a repetition past the end of a scanline, reads a code 0 as an
    // empty literal, and does not check the number of FORMAT lines. The library rejects them, as the specification says
    private static readonly Dictionary<string, string> ToleratedByRadiance = new(StringComparer.Ordinal)
    {
        ["old-rle-repetition-past-end"] = "pvalue reads the file: Radiance does not check that a repetition ends in its scanline.",
        ["rle-code-zero"] = "pvalue reads the file: Radiance reads a code 0 as an empty literal, which the documentation does not define (a literal has 1 to 128 bytes).",
        ["two-formats"] = "pvalue reads the file: Radiance does not check that the header has at most one FORMAT line, which the documentation requires.",
    };

    private static readonly (string Order, string Name)[] Orientations =
    [
        ("-Y -X", "minus-y-minus-x"),
        ("+Y -X", "plus-y-minus-x"),
        ("+Y +X", "plus-y-plus-x"),
        ("+X +Y", "plus-x-plus-y"),
        ("-X +Y", "minus-x-plus-y"),
        ("-X -Y", "minus-x-minus-y"),
        ("+X -Y", "plus-x-minus-y"),
    ];

    /// <summary>The independent decoders every valid fixture is compared with, in this order.</summary>
    private static readonly Func<Corpus<RadianceTools>, byte[], RadiancePicture, Obj?>[] CrossChecks = [PvalueCrossCheck, FfmpegCrossCheck];

    private static bool IsRadianceFixture(Obj fixture) => (string?)fixture["format"] == "hdr";

    public static int Run(GeneratorOptions options)
    {
        var tools = new RadianceTools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-hdr-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => $"{fixtures} Radiance HDR fixtures; corpus: {files} files, {bytes} bytes",
            "The committed Radiance HDR fixtures are reproducible.");
    }

    private static int Generate(FullPath outDir, RadianceTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<RadianceTools>(outDir, tools, ScriptPath))
        {
            BuildEncodings(corpus);
            BuildOrientations(corpus);
            BuildHeaders(corpus);
            BuildFfmpeg(corpus);
            BuildInvalid(corpus);
            BuildUnsupported(corpus);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsRadianceFixture(fixture), (string?)fixture["id"]);

        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsRadianceFixture);
        return fixtures.Count;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Patterns
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The colors of a pattern of high dynamic range: values from 1E-3 to 1E4, black pixels, runs of equal pixels (every
    /// fourth row) and runs of equal components (the exponents of neighbors), as floats.
    /// </summary>
    private static double[] Colors(int width, int height, int seed)
    {
        var colors = new double[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 3;
                if ((x + y + seed) % 11 == 0)
                    continue;

                var (u, v) = y % 4 == 3 ? (0, y) : (x, y);
                var scale = Math.Pow(10, (((u + (2 * v) + seed) % 8) - 3) / 2.0);
                colors[offset] = scale * (1 + (((u * 7) + seed) % 13 / 13.0));
                colors[offset + 1] = scale * (0.25 + (((v * 5) + seed) % 7 / 7.0));
                colors[offset + 2] = (u + seed) % 5 == 0 ? 0 : scale * (u % 3) / 3.0;
            }
        }

        return colors;
    }

    /// <summary>The RGBE pixels of a pattern, as "Real Pixels" converts colors.</summary>
    private static byte[] Rgbe(int width, int height, int seed)
    {
        var colors = Colors(width, height, seed);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            RadianceFileBuilder.ToRgbe(colors[i * 3], colors[(i * 3) + 1], colors[(i * 3) + 2]).CopyTo(pixels, i * 4);
        }

        return pixels;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildEncodings(Corpus<RadianceTools> c)
    {
        const string Function = nameof(BuildEncodings);
        foreach (var (encoding, name) in new[] { (RadianceScanlineEncoding.Flat, "flat"), (RadianceScanlineEncoding.OldRunLength, "old-rle"), (RadianceScanlineEncoding.NewRunLength, "new-rle") })
        {
            var pixels = Rgbe(13, 9, 1);
            AddBuilt(c, name, Function, 13, 9, pixels, RadianceFileBuilder.Create(13, 9, pixels, encoding),
                $"A 13x9 RGBE pattern of high dynamic range, {Describe(encoding)} scanlines, in the standard orientation.");
        }

        // The higher bytes of the count of a repetition, and runs longer than a code of the new encoding
        var runs = new byte[300 * 2 * 4];
        for (var i = 0; i < 600; i++)
        {
            byte[] pixel = i < 290 || (i >= 300 && i < 590) ? [200, 100, 50, 130] : [(byte)(128 + i % 100), 3, 0, 125];
            pixel.CopyTo(runs, i * 4);
        }

        AddBuilt(c, "old-rle-long-runs", Function, 300, 2, runs, RadianceFileBuilder.Create(300, 2, runs, RadianceScanlineEncoding.OldRunLength),
            "Two scanlines of 300 pixels, old encoding: runs of 289 repetitions, whose count takes two repetition pixels (the low byte first).");
        AddBuilt(c, "new-rle-long-runs", Function, 300, 2, runs, RadianceFileBuilder.Create(300, 2, runs, RadianceScanlineEncoding.NewRunLength),
            "Two scanlines of 300 pixels, new encoding: runs longer than 127 bytes take several codes, literals of up to 128 bytes.");

        // Each scanline chooses its encoding
        var mixedPixels = Rgbe(12, 6, 2);
        var mixed = RadianceFileBuilder.Create(12, 6, mixedPixels, RadianceScanlineEncoding.Flat);
        var scanlines = RadianceFileBuilder.GetScanlines(12, 6, mixedPixels, "-Y +X");
        RadianceScanlineEncoding[] encodings = [RadianceScanlineEncoding.Flat, RadianceScanlineEncoding.NewRunLength, RadianceScanlineEncoding.Flat, RadianceScanlineEncoding.OldRunLength, RadianceScanlineEncoding.NewRunLength, RadianceScanlineEncoding.Flat];
        for (var i = 0; i < scanlines.Count; i++)
        {
            mixed.Scanlines[i] = RadianceFileBuilder.Encode(scanlines[i], encodings[i]);
        }

        AddBuilt(c, "mixed-encodings", Function, 12, 6, mixedPixels, mixed, "Six scanlines of 12 pixels: flat, new, flat, old (a row of equal pixels), new and flat scanlines.");

        // Scanlines too short for the new encoding are flat; the exponents 1 and 255, the exponent 0 with mantissas
        byte[] extremes = [0, 1, 255, 1, 255, 254, 128, 255, 7, 8, 9, 0, 0, 0, 0, 200, 128, 0, 0, 136, 0, 0, 0, 0];
        AddBuilt(c, "extreme-exponents", Function, 3, 2, extremes, RadianceFileBuilder.Create(3, 2, extremes, RadianceScanlineEncoding.Flat),
            "Exponents of 1 (subnormal floats) and 255, the exponent 0 (black, whatever the mantissas), a mantissa 0 (half a unit), black.");
        AddBuilt(c, "one-pixel", Function, 1, 1, [180, 90, 45, 129], RadianceFileBuilder.Create(1, 1, [180, 90, 45, 129], RadianceScanlineEncoding.Flat), "One pixel.");
    }

    private static void BuildOrientations(Corpus<RadianceTools> c)
    {
        const string Function = nameof(BuildOrientations);
        foreach (var (order, name) in Orientations)
        {
            // 13x9: scanlines of 13 or 9 pixels, long enough for the new encoding
            var pixels = Rgbe(13, 9, 3);
            var encoding = order[1] == 'X' ? RadianceScanlineEncoding.NewRunLength : RadianceScanlineEncoding.Flat;
            AddBuilt(c, "orientation-" + name, Function, 13, 9, pixels, RadianceFileBuilder.Create(13, 9, pixels, encoding, order),
                $"The 13x9 pattern stored in the order '{order}' ({(order[1] == 'X' ? "columns" : "rows")}, {Describe(encoding)}): the picture is in the standard orientation.");
            if (order[1] == 'Y')
            {
                AddBuilt(c, "orientation-" + name + "-rle", Function, 13, 9, pixels, RadianceFileBuilder.Create(13, 9, pixels, RadianceScanlineEncoding.NewRunLength, order),
                    $"The 13x9 pattern stored in the order '{order}', new encoding.");
            }
        }
    }

    private static void BuildHeaders(Corpus<RadianceTools> c)
    {
        const string Function = nameof(BuildHeaders);
        var pixels = Rgbe(9, 8, 4);

        var rgbe = RadianceFileBuilder.Create(9, 8, pixels, RadianceScanlineEncoding.NewRunLength, format: null);
        rgbe.Signature = "#?RGBE";
        AddBuilt(c, "signature-rgbe", Function, 9, 8, pixels, rgbe, "The identifier '#?RGBE' and no FORMAT line: RGBE pixels.");

        var variables = RadianceFileBuilder.Create(9, 8, pixels, RadianceScanlineEncoding.NewRunLength);
        variables.HeaderLines.Insert(0, "rpict -vf scene.vf -x 9 -y 8 scene.oct");
        variables.HeaderLines.Insert(0, "# A comment");
        variables.HeaderLines.Add("EXPOSURE=2.5");
        variables.HeaderLines.Add("pfilt -e 2");
        variables.HeaderLines.Add("EXPOSURE= 2.000000e+00");
        variables.HeaderLines.Add("COLORCORR=1.1 1 0.9");
        variables.HeaderLines.Add("PIXASPECT=0.5");
        variables.HeaderLines.Add("GAMMA=2.2");
        variables.HeaderLines.Add("VIEW= -vtv -vp 0 0 1 -vd 0 1 0 -vu 0 0 1 -vh 45 -vv 45");
        variables.HeaderLines.Add("SOFTWARE=RADIANCE 5.4a");
        variables.HeaderLines.Add("CAPDATE=2026:11:13 12:00:00");
        AddBuilt(c, "header-variables", Function, 9, 8, pixels, variables,
            "Commands, a comment, two EXPOSURE lines (their product is 5), COLORCORR, PIXASPECT, GAMMA, VIEW, SOFTWARE and another variable: reported, never applied.",
            features: ["hdr.exposure", "hdr.colorCorrection", "hdr.pixelAspect", "hdr.gamma", "hdr.view", "hdr.software", "hdr.commands"]);

        var unreadable = RadianceFileBuilder.Create(9, 8, pixels, RadianceScanlineEncoding.NewRunLength);
        unreadable.HeaderLines.Add("PIXASPECT=inf");
        unreadable.HeaderLines.Add("EXPOSURE=bright");
        unreadable.HeaderLines.Add("EXPOSURE=0");
        unreadable.HeaderLines.Add("COLORCORR=1 1");
        unreadable.HeaderLines.Add("GAMMA=-2.2");
        unreadable.HeaderLines.Add("PRIMARIES=0.64 0.33 0.3 0.6 0.15 0.06 0.3127");
        unreadable.HeaderLines.Add("PRIMARIES=0.64 0.33 0.64 0.33 0.15 0.06 0.3127 0.329");
        AddBuilt(c, "header-unreadable-variables", Function, 9, 8, pixels, unreadable,
            "Variables whose values cannot be read: PIXASPECT=inf (as FFmpeg writes it), an EXPOSURE that is not a number and one of 0, a COLORCORR of two terms, a negative GAMMA, PRIMARIES of seven values and of two equal primaries. They are skipped.",
            features: ["hdr.unreadableVariables"]);

        foreach (var (name, primaries, cicp) in new[]
        {
            ("primaries-standard", "0.640 0.330 0.290 0.600 0.150 0.060 0.333 0.333", (int?)null),
            ("primaries-bt709", "0.6400 0.3300 0.3000 0.6000 0.1500 0.0600 0.3127 0.3290", null),
            ("primaries-bt2020", "0.708 0.292 0.170 0.797 0.131 0.046 0.3127 0.3290", 9),
            ("primaries-display-p3", "0.680 0.320 0.265 0.690 0.150 0.060 0.3127 0.3290", 12),
        })
        {
            var builder = RadianceFileBuilder.Create(9, 8, pixels, RadianceScanlineEncoding.NewRunLength);
            builder.HeaderLines.Add("PRIMARIES= " + primaries);
            AddBuilt(c, name, Function, 9, 8, pixels, builder, $"The PRIMARIES {primaries}.", features: ["hdr.primaries"],
                cicp: cicp is { } code ? new Obj { ["colorPrimaries"] = code, ["transferCharacteristics"] = 8, ["matrixCoefficients"] = 0, ["fullRange"] = true } : null);
        }
    }

    private static void BuildFfmpeg(Corpus<RadianceTools> c)
    {
        const string Function = nameof(BuildFfmpeg);
        foreach (var (name, width, height, seed) in new[] { ("ffmpeg-rle", 23, 17, 5), ("ffmpeg-narrow", 5, 4, 6) })
        {
            // The planes of FFmpeg: green, blue, red
            var colors = Colors(width, height, seed);
            var planes = new byte[width * height * 12];
            for (var i = 0; i < width * height; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(planes.AsSpan(i * 4), (float)colors[(i * 3) + 1]);
                BinaryPrimitives.WriteSingleLittleEndian(planes.AsSpan((width * height * 4) + (i * 4)), (float)colors[(i * 3) + 2]);
                BinaryPrimitives.WriteSingleLittleEndian(planes.AsSpan((width * height * 8) + (i * 4)), (float)colors[i * 3]);
            }

            var source = c.Scratch / "source.gbrpf32le";
            var output = c.Scratch / "ffmpeg.hdr";
            File.WriteAllBytes(source, planes);
            var command = c.Tools.FfmpegCmd(["-f", "rawvideo", "-pixel_format", "gbrpf32le", "-video_size", $"{width}x{height}", "-i", source.Value, "-frames:v", "1", "-c:v", "hdr", "-f", "image2", output.Value]);
            Proc.Run(command);
            var data = File.ReadAllBytes(output);
            var picture = ReferenceRadiance.Read(data);
            var display = CommonCorpus.Display(command, c.Tools, [(source.Value, "{pattern}.gbrpf32le"), (output.Value, "{output}")]);
            AddValid(c, name, Function, data, picture.Rgbe.ToArray(), [display],
                $"A {width}x{height} floating-point pattern of high dynamic range written by the Radiance encoder of FFmpeg ({(picture.Encodings[0] == RadianceScanlineEncoding.NewRunLength ? "new" : "flat")} scanlines). The RGBE pixels the file stores, read as the reference routines of Radiance read them.",
                patternCheck: false);
        }
    }

    private static void BuildInvalid(Corpus<RadianceTools> c)
    {
        const string Function = nameof(BuildInvalid);
        var pixels = Rgbe(9, 4, 7);
        var valid = RadianceFileBuilder.Create(9, 4, pixels, RadianceScanlineEncoding.NewRunLength).Build();
        var header = Encoding.ASCII.GetByteCount("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 4 +X 9\n");

        AddError(c, "invalid", "truncated-header", Function, valid[..20], Invalid(), "The file ends in its information header.");
        AddError(c, "invalid", "truncated-resolution", Function, valid[..(header - 3)], Invalid(), "The file ends in its resolution string.");
        AddError(c, "invalid", "truncated-scanline", Function, valid[..^5], Invalid(), "The last scanline is cut.");
        AddError(c, "invalid", "truncated-scanlines", Function, valid[..(header + 4)], Invalid(), "The file ends after the marker of the first scanline.");

        AddBuiltError(c, "two-formats", Function, pixels, builder => builder.HeaderLines.Add("FORMAT=32-bit_rle_rgbe"), Invalid(), "Two FORMAT lines.");
        AddBuiltError(c, "resolution-one-axis", Function, pixels, builder => builder.Resolution = "-Y 4", Invalid(), "A resolution string of one axis.");
        AddBuiltError(c, "resolution-same-axis", Function, pixels, builder => builder.Resolution = "-Y 4 +Y 9", Invalid(), "A resolution string whose two axes are Y.");
        AddBuiltError(c, "resolution-zero", Function, pixels, builder => builder.Resolution = "-Y 0 +X 9", Invalid(), "A height of 0.");
        AddBuiltError(c, "resolution-lowercase", Function, pixels, builder => builder.Resolution = "-y 4 +x 9", Invalid(), "Lowercase axes.");

        // Scanline defects, after a valid first scanline
        var scanlines = RadianceFileBuilder.GetScanlines(9, 4, pixels, "-Y +X");
        void AddScanline(string name, byte[] scanline, string notes)
        {
            var builder = RadianceFileBuilder.Create(9, 4, pixels, RadianceScanlineEncoding.NewRunLength);
            builder.Scanlines[1] = scanline;
            AddError(c, "invalid", name, Function, builder.Build(), Invalid(), notes);
        }

        var encoded = RadianceFileBuilder.EncodeNew(scanlines[1]);
        var wrongLength = encoded.ToArray();
        wrongLength[3] = 10;
        AddScanline("rle-length", wrongLength, "The marker of the second scanline declares 10 pixels; the scanlines have 9.");
        AddScanline("rle-run-past-end", [2, 2, 0, 9, 128 + 10, 5, .. encoded[4..]], "A run of 10 bytes in a scanline of 9 pixels.");
        AddScanline("rle-literal-past-end", [2, 2, 0, 9, 10, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, .. encoded[4..]], "A literal of 10 bytes in a scanline of 9 pixels.");
        AddScanline("rle-code-zero", [2, 2, 0, 9, 0, .. encoded[4..]], "A code 0.");
        AddScanline("old-rle-repetition-first", [1, 1, 1, 3, .. RadianceFileBuilder.Encode(scanlines[1], RadianceScanlineEncoding.Flat)], "The scanline starts with a repetition: there is no previous pixel.");
        AddScanline("old-rle-repetition-past-end", [.. scanlines[1].AsSpan(0, 8), 1, 1, 1, 8], "A repetition of 8 pixels after the second pixel of a scanline of 9.");
    }

    private static void BuildUnsupported(Corpus<RadianceTools> c)
    {
        const string Function = nameof(BuildUnsupported);
        var pixels = Rgbe(9, 4, 8);
        AddError(c, "unsupported", "xyze", Function, RadianceFileBuilder.Create(9, 4, pixels, RadianceScanlineEncoding.NewRunLength, format: "32-bit_rle_xyze").Build(), Unsupported("XYZE samples"),
            "CIE XYZ samples: identified, and rejected by a load, since no pixel format holds them and converting them to RGB is a color conversion.");
        var flat = RadianceFileBuilder.Create(9, 4, pixels, RadianceScanlineEncoding.Flat, "+X -Y", format: "32-bit_rle_xyze");
        AddError(c, "unsupported", "xyze-columns", Function, flat.Build(), Unsupported("XYZE samples"), "CIE XYZ samples stored in columns, flat.");
        AddError(c, "unsupported", "format-other", Function, RadianceFileBuilder.Create(9, 4, pixels, RadianceScanlineEncoding.Flat, format: "32-bit_rle_hsr").Build(), Unsupported("Pixel format 32-bit_rle_hsr"),
            "A FORMAT other than RGBE and XYZE.");
    }

    private static void BuildLimits(Corpus<RadianceTools> c)
    {
        const string Function = nameof(BuildLimits);
        var pixels = Rgbe(13, 9, 9);
        AddError(c, "limit", "frame-pixels-over-limit", Function, RadianceFileBuilder.Create(13, 9, pixels, RadianceScanlineEncoding.NewRunLength).Build(),
            new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "FramePixels" }, "The image has 117 pixels.", decodeOptions: new Obj { ["limits"] = new Obj { ["MaxFramePixels"] = 116 } });
        var builder = RadianceFileBuilder.Create(13, 9, pixels, RadianceScanlineEncoding.NewRunLength);
        builder.HeaderLines.Add("VIEW=" + string.Concat(Enumerable.Repeat(" -vh 45", 300)));
        AddError(c, "limit", "metadata-bytes-over-limit", Function, builder.Build(),
            new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "MetadataBytes" }, "The information header is more than 1,024 bytes.", decodeOptions: new Obj { ["limits"] = new Obj { ["MaxMetadataBytes"] = 1024 } });
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Entries
    // -----------------------------------------------------------------------------------------------------------------

    private static string Describe(RadianceScanlineEncoding encoding) => encoding switch
    {
        RadianceScanlineEncoding.Flat => "flat",
        RadianceScanlineEncoding.OldRunLength => "old run-length encoded",
        _ => "new run-length encoded",
    };

    private static void AddBuilt(Corpus<RadianceTools> c, string name, string function, int width, int height, byte[] pixels, RadianceFileBuilder builder, string description, IEnumerable<string>? features = null, Obj? cicp = null)
    {
        var data = builder.Build();
        AddValid(c, name, function, data, pixels, [], description, features, cicp, new Obj { ["width"] = width, ["height"] = height });
    }

    /// <summary>Records a valid fixture whose reference is the RGBE pixels <paramref name="pixels"/> (the standard orientation).</summary>
    private static void AddValid(Corpus<RadianceTools> c, string name, string function, byte[] data, byte[] pixels, List<string> commands, string description,
        IEnumerable<string>? features = null, Obj? cicp = null, Obj? parameters = null, bool patternCheck = true)
    {
        var fixtureId = "hdr/" + name;
        RadiancePicture picture;
        try
        {
            picture = ReferenceRadiance.Read(data);
        }
        catch (InvalidDataException error)
        {
            throw new FatalException(fixtureId + ": the reference reader rejects the file: " + error.Message);
        }

        var expectedPicture = picture with { Rgbe = pixels };
        var expected = new Obj
        {
            ["width"] = picture.Width,
            ["height"] = picture.Height,
            ["pixelFormat"] = "RgbaFloat",
            ["colorModel"] = "Rgb",
            ["bitsPerComponent"] = 32,
            ["orientation"] = 1,
            ["iccProfile"] = "none",
        };
        if (cicp is null)
        {
            expected["transferFunction"] = "linear";
        }
        else
        {
            expected["cicp"] = cicp;
        }

        expected["animation"] = null;
        expected["resolution"] = null;
        expected["profiles"] = new Obj { ["icc"] = null, ["exif"] = null, ["xmp"] = null };
        expected["text"] = new List<object?>();
        expected["frameCount"] = 1;
        expected["frames"] = new List<object?> { new Obj { ["duration"] = "0/1", ["buffers"] = new List<object?> { FloatBuffer(c, fixtureId + ".frame-0.rgbaf32le.raw", picture.Width, picture.Height, expectedPicture.GetFloatPixels()) } } };
        expected["poster"] = null;

        var checks = new List<object?>();
        if (patternCheck)
        {
            Py.Assert(picture.Rgbe.Span.SequenceEqual(pixels), fixtureId + ": the reference reader does not give the pattern back");
            checks.Add(new Obj
            {
                ["decoder"] = "ReferenceRadiance (test harness: an independent reader of the Radiance file format documentation)",
                ["result"] = "exact",
                ["maxAbsoluteError"] = 0,
                ["meanAbsoluteError"] = 0.0,
                ["notes"] = "The reader gives back the RGBE pattern the file was assembled from, byte for byte.",
            });
        }

        foreach (var crossCheck in CrossChecks)
        {
            if (crossCheck(c, data, picture) is { } check)
                checks.Add(check);
        }

        Py.Assert(checks.Count > 0, fixtureId + ": no independent cross-check");

        var input = c.Write(fixtureId + ".hdr", data);
        input["role"] = "encoded";
        var provenance = c.Provenance("generated", function, parameters, [$"Radiance {c.Tools.RadianceVersion} (pvalue, cross-check)", $"{c.Tools.FfmpegLabel} (cross-check{(commands.Count > 0 ? ", encoder" : "")})", ToolSet.RuntimeLabel], commands);
        var fixture = new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = "hdr",
            ["input"] = input,
            ["provenance"] = provenance,
            ["features"] = Features(picture, features),
            ["reference"] = new Obj
            {
                ["method"] = "hand-computed",
                ["description"] = description + " A pixel of mantissas m and exponent e is (m + 0.5) × 2^(e - 136), 0 for the exponent 0 (\"Real Pixels\", the reference routines of Radiance), with an alpha of 1, in the standard orientation.",
                ["crossChecks"] = checks,
            },
            ["expected"] = expected,
            ["comparison"] = new Obj { ["mode"] = "exact" },
        };
        c.Fixtures.Add(fixture);
    }

    private static List<string> Features(RadiancePicture picture, IEnumerable<string>? extra)
    {
        var features = new List<string> { "hdr.scanOrder=" + picture.ScanOrder.Replace(" ", "", StringComparison.Ordinal) };
        foreach (var encoding in picture.Encodings.Distinct())
        {
            features.Add("hdr.encoding=" + encoding switch
            {
                RadianceScanlineEncoding.Flat => "flat",
                RadianceScanlineEncoding.OldRunLength => "old-rle",
                _ => "new-rle",
            });
        }

        features.AddRange(extra ?? []);
        features.Sort(StringComparer.Ordinal);
        return [.. features.Distinct(StringComparer.Ordinal)];
    }

    private static Obj FloatBuffer(Corpus<RadianceTools> c, string path, int width, int height, byte[] bytes)
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

    /// <summary>Decodes a file with <c>pvalue -h -H -df</c>, which gives the colors in file order: they must be the reference.</summary>
    private static Obj? PvalueCrossCheck(Corpus<RadianceTools> c, byte[] data, RadiancePicture picture)
    {
        var path = c.Scratch / "pvalue-check.hdr";
        File.WriteAllBytes(path, data);
        List<string> command = [c.Tools.Pvalue, "-h", "-H", "-df", path.Value];
        var output = Proc.Run(command);
        var scanlines = RadianceFileBuilder.GetScanlines(picture.Width, picture.Height, picture.Rgbe.Span, picture.ScanOrder);
        Py.Assert(output.Length == picture.Width * picture.Height * 12, "pvalue gives every pixel");
        var differing = 0;
        var index = 0;
        foreach (var scanline in scanlines)
        {
            for (var j = 0; j < scanline.Length / 4; j++)
            {
                for (var component = 0; component < 3; component++)
                {
                    var value = BinaryPrimitives.ReadSingleLittleEndian(output.AsSpan(index++ * 4));
                    if (BitConverter.SingleToUInt32Bits(value) != BitConverter.SingleToUInt32Bits(ReferenceRadiance.ToFloat(scanline[(j * 4) + component], scanline[(j * 4) + 3])))
                        differing++;
                }
            }
        }

        Py.Assert(differing == 0, string.Create(CultureInfo.InvariantCulture, $"pvalue reads the file as the reference: {differing} samples differ"));
        return new Obj
        {
            ["decoder"] = $"Radiance {c.Tools.RadianceVersion} (pvalue, the reference implementation)",
            ["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]),
            ["result"] = "exact",
            ["maxAbsoluteError"] = 0,
            ["meanAbsoluteError"] = 0.0,
            ["notes"] = "pvalue gives the colors in the order of the scanlines of the file; reordered to the standard orientation, they are the reference, bit for bit.",
        };
    }

    /// <summary>
    /// Decodes a file with FFmpeg: it reads a mantissa as the bottom of its interval and gives the scanlines in file order,
    /// whatever the orientation. The check requires exactly that, sample by sample.
    /// </summary>
    private static Obj? FfmpegCrossCheck(Corpus<RadianceTools> c, byte[] data, RadiancePicture picture)
    {
        var path = c.Scratch / "ffmpeg-check.hdr";
        File.WriteAllBytes(path, data);
        var majorIsX = picture.ScanOrder[1] == 'X';
        var count = majorIsX ? picture.Width : picture.Height;
        var length = majorIsX ? picture.Height : picture.Width;
        var check = new Obj { ["decoder"] = $"{c.Tools.FfmpegLabel} (libavcodec hdr decoder)" };

        // FFmpeg decodes every valid fixture: a failure stops the generation instead of being recorded as a difference
        (var frames, var command) = CommonCorpus.FfmpegDecode(c.Tools, path, "gbrpf32le", length, count, 12, animated: false);
        var frame = frames[0];
        check["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]);

        // The scanlines in file order, as FFmpeg gives them
        var scanlines = RadianceFileBuilder.GetScanlines(picture.Width, picture.Height, picture.Rgbe.Span, picture.ScanOrder);
        var plane = count * length;
        var differing = 0;
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < length; j++)
            {
                var pixel = scanlines[i].AsSpan(j * 4, 4);
                foreach (var (component, planeIndex) in new[] { (1, 0), (2, 1), (0, 2) })
                {
                    var value = BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(((planeIndex * plane) + (i * length) + j) * 4));
                    if (BitConverter.SingleToUInt32Bits(value) != BitConverter.SingleToUInt32Bits(ReferenceRadiance.ToFloat(pixel[component], pixel[3], middle: false)))
                    {
                        differing++;
                    }
                }
            }
        }

        check["result"] = "differs";
        check["excludedFromTolerance"] = true;
        if (majorIsX)
        {
            // FFmpeg 9.0.2 takes the sizes of the axes but reads rows of the X size: the columns are misread
            Py.Assert(differing > 0, "FFmpeg reads the columns of a file whose resolution string gives the X axis first");
            check["notes"] = string.Create(CultureInfo.InvariantCulture, $"FFmpeg does not read scanlines that are columns (the resolution string gives the X axis first): it reads rows of the width of the picture, and {differing} of the {plane * 3} samples differ from the scanlines of the file.");
            return check;
        }

        Py.Assert(differing == 0, string.Create(CultureInfo.InvariantCulture, $"FFmpeg reads the file as expected: {differing} samples differ from the reference read as FFmpeg reads a mantissa (the bottom of its interval)"));
        check["notes"] = "FFmpeg reads a mantissa m as the bottom of its interval, m × 2^(e - 136), where Radiance reads its middle: every sample is the reference minus half a unit of its exponent (0 for the exponent 0), bit for bit" +
            (picture.ScanOrder == "-Y +X" ? "." : ". FFmpeg ignores the orientation of the resolution string: it gives the scanlines in file order, as rows.");
        return check;
    }

    private static void AddBuiltError(Corpus<RadianceTools> c, string name, string function, byte[] pixels, Action<RadianceFileBuilder> edit, Obj expectedError, string notes)
    {
        var builder = RadianceFileBuilder.Create(9, 4, pixels, RadianceScanlineEncoding.NewRunLength);
        edit(builder);
        AddError(c, "invalid", name, function, builder.Build(), expectedError, notes);
    }

    private static void AddError(Corpus<RadianceTools> c, string kind, string name, string function, byte[] data, Obj expectedError, string notes, IList<string>? features = null, Obj? decodeOptions = null)
    {
        var fixtureId = kind == "limit" ? "limit/hdr/" + name : "invalid/hdr/" + name;
        if (kind == "invalid")
        {
            // The defect is one the independent reader finds too, or a header variable it does not interpret
            var rejected = Rejects(data);
            Py.Assert(rejected || name is "two-formats" or "resolution-zero",
                fixtureId + ": the reference reader reads the file");

            // pvalue rejects the file too, unless Radiance is known to tolerate the defect
            var pvalueRejects = PvalueRejects(c, data);
            if (ToleratedByRadiance.TryGetValue(name, out var tolerated))
            {
                Py.Assert(!pvalueRejects, fixtureId + ": pvalue now rejects the file; remove it from ToleratedByRadiance");
                notes += " " + tolerated;
            }
            else
            {
                Py.Assert(pvalueRejects, fixtureId + ": pvalue reads the file");
            }
        }

        var provenance = c.Provenance("generated", function, null, kind == "invalid" ? [$"Radiance {c.Tools.RadianceVersion} (pvalue, cross-check)", ToolSet.RuntimeLabel] : [ToolSet.RuntimeLabel]);
        c.AddError(fixtureId, kind, fixtureId + ".hdr", data, "hdr", provenance, expectedError, features: features, decodeOptions: decodeOptions, notes: notes);
    }

    private static bool PvalueRejects(Corpus<RadianceTools> c, byte[] data)
    {
        var path = c.Scratch / "pvalue-error.hdr";
        File.WriteAllBytes(path, data);
        try
        {
            Proc.Run([c.Tools.Pvalue, "-h", "-H", "-df", path.Value]);
            return false;
        }
        catch (ToolException)
        {
            return true;
        }
    }

    private static bool Rejects(byte[] data)
    {
        try
        {
            ReferenceRadiance.Read(data);
            return false;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or ArgumentException)
        {
            return true;
        }
    }

    private static Obj Invalid() => new() { ["exception"] = "InvalidImageContentException", ["format"] = "Radiance" };

    private static Obj Unsupported(string feature) => new() { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Radiance", ["feature"] = feature };

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    private sealed class RadianceTools : ToolSet
    {
        public RadianceTools(bool acceptVersions)
            : base(PrebuiltTools.Ffmpeg.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            Pvalue = PrebuiltTools.Pvalue.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            RadianceVersion = PrebuiltTools.Pvalue.Version;
            if ((FfmpegVersion != PinnedFfmpeg || RadianceVersion != PinnedRadiance) && !acceptVersions)
                throw new FatalException($"Tool versions differ from the pinned versions: FFmpeg {FfmpegVersion} (pinned {PinnedFfmpeg}), Radiance {RadianceVersion} (pinned {PinnedRadiance}). Review the impact and rerun with --accept-tool-versions (then update the pinned versions).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [Pvalue] = "pvalue" };
        }

        public string Pvalue { get; }

        public string RadianceVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }
    }
}
