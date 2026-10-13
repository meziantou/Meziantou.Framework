using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the JPEG 2000 fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/jp2, invalid/jp2 and
/// limit/jp2 entries). This generator is the reviewed, reproducible recipe of every committed JPEG 2000 fixture. It is
/// NEVER run by the tests and does not use the library under test.
/// The codestreams are written by opj_compress (OpenJPEG) and, for the high-throughput block coder, by ojph_compress
/// (OpenJPH); the files of the JP2 family are written by opj_compress or assembled around these codestreams, and the
/// coding passes no encoder writes are authored by hand from Rec. ITU-T T.814 (Jp2Corpus.HighThroughput.cs). The expected
/// pixels of a lossless fixture are the pattern given to the encoder; those of a lossy fixture are decoded by
/// opj_decompress. Every reference is cross-checked with the decoders that read the fixture: opj_decompress, the native
/// JPEG 2000 decoder of FFmpeg, which shares no code with OpenJPEG, and ojph_expand for the high-throughput block coder.
/// The fields of the files (boxes, marker segments, tile-parts) are read by the independent reader of the test harness.
/// It only replaces the JPEG 2000 entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format "jp2") and
/// their files, and keeps every other entry and file unchanged.
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- jp2            # check: regenerate in a temporary directory and diff
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- jp2 --write    # explicit regeneration (review the diff afterwards!)
/// Requirements: none, the tools are downloaded from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt
/// package (override a version difference with --accept-tool-versions after reviewing it). Any OS.
/// </summary>
internal static partial class Jp2Corpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/Jp2Corpus.cs";

    // Tool versions used to produce the committed JPEG 2000 fixtures. Regenerating with other versions is allowed only
    // explicitly (--accept-tool-versions) and must be reviewed: encoded inputs and decoded references may change.
    private const string PinnedOpenJpeg = "2.5.4";
    private const string PinnedOpenJph = "0.32.0";

    // Measured disagreement of the library decoder with each decoded reference, in units of the 8-bit scale: (max absolute
    // error, mean absolute color error), printed by Jpeg2000DecoderConformanceTests, which also fails when a policy derived
    // from these values is looser or tighter than the decoder needs. The values are reviewed measurements of the library
    // (never used as pixel references); a fixture without a measurement keeps the bound justified by the cross-check alone.
    private static readonly Dictionary<string, (int Max, double Mean)> LibraryJp2Measurements = new(StringComparer.Ordinal)
    {
        ["jp2/file-sycc"] = (0, 0.0),
        ["jp2/file-sycc-420"] = (0, 0.0),
        ["jp2/ht-lossy-gray8"] = (1, 0.0018),
        ["jp2/ht-lossy-rgb8"] = (1, 0.0006),
        ["jp2/lossy-gray8"] = (1, 0.0018),
        ["jp2/lossy-layers"] = (1, 0.0006),
        ["jp2/lossy-region-of-interest"] = (1, 0.0018),
        ["jp2/lossy-rgb16"] = (1, 0.0001),
        ["jp2/lossy-rgb8"] = (1, 0.0018),
        ["jp2/lossy-rgb8-every-pass"] = (1, 0.0018),
        ["jp2/lossy-rgba8"] = (1, 0.0073),
        ["jp2/lossy-tiles"] = (1, 0.0018),
    };

    private static bool IsJp2Fixture(Obj fixture) => (string?)fixture["format"] == "jp2";

    private static int Generate(FullPath outDir, Jp2Tools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<Jp2Tools>(outDir, tools, ScriptPath))
        {
            BuildLossless(corpus);
            BuildCodestreams(corpus);
            BuildLossy(corpus);
            BuildFiles(corpus);
            BuildHighThroughput(corpus);
            BuildInvalid(corpus);
            BuildUnsupported(corpus);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsJp2Fixture(fixture), (string?)fixture["id"]);

        // Every other fixture and file of the committed corpus is kept unchanged
        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsJp2Fixture);
        return fixtures.Count;
    }

    public static int Run(GeneratorOptions options)
    {
        var tools = new Jp2Tools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-jp2-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => $"{fixtures} JPEG 2000 fixtures; corpus: {files} files, {bytes} bytes",
            "The committed JPEG 2000 fixtures are reproducible.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Encoding
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>An encoded input and the command lines that wrote it.</summary>
    private sealed record Encoded(byte[] Data, List<string> Commands);

    /// <summary>The planes given to an encoder or returned by a decoder: one array of samples per channel, at the depth of the file.</summary>
    private sealed record Planes(int Width, int Height, int Depth, IReadOnlyList<int[]> Channels, string Command = "");

    /// <summary>The planes of an image whose samples have the depth of the file: an <see cref="Img"/> only holds 8 and 16 bits.</summary>
    private static Planes ToPlanes(Img img, int? depth = null)
    {
        var channels = Enumerable.Range(0, Img.Channels(img.Model)).Select(channel => img.Pixels.Select(pixel => pixel[channel]).ToArray()).ToList();
        return new Planes(img.Width, img.Height, depth ?? img.Depth, channels);
    }

    /// <summary>A Netpbm file: PGM for one plane, PPM for three, PAM for two or four.</summary>
    private static (byte[] Data, string Extension) WriteNetpbm(Planes planes)
    {
        var maxValue = (1 << planes.Depth) - 1;
        var count = planes.Channels.Count;
        var builder = new ByteBuilder();
        var extension = count switch { 1 => ".pgm", 3 => ".ppm", _ => ".pam" };
        var header = count switch
        {
            1 => $"P5\n{CommonCorpus.Str(planes.Width)} {CommonCorpus.Str(planes.Height)}\n{CommonCorpus.Str(maxValue)}\n",
            3 => $"P6\n{CommonCorpus.Str(planes.Width)} {CommonCorpus.Str(planes.Height)}\n{CommonCorpus.Str(maxValue)}\n",
            _ => $"P7\nWIDTH {CommonCorpus.Str(planes.Width)}\nHEIGHT {CommonCorpus.Str(planes.Height)}\nDEPTH {CommonCorpus.Str(count)}\nMAXVAL {CommonCorpus.Str(maxValue)}\nTUPLTYPE {(count == 2 ? "GRAYSCALE_ALPHA" : "RGB_ALPHA")}\nENDHDR\n",
        };
        builder.Ascii(header);
        for (var i = 0; i < planes.Width * planes.Height; i++)
        {
            foreach (var channel in planes.Channels)
            {
                Py.Assert(channel[i] >= 0 && channel[i] <= maxValue, "sample above the maximum value");
                if (maxValue > 255)
                    builder.U16BE(channel[i]);
                else
                    builder.U8(channel[i]);
            }
        }

        return (builder.ToArray(), extension);
    }

    /// <summary>Encodes an image with opj_compress from a Netpbm file.</summary>
    private static Encoded Opj(Corpus<Jp2Tools> c, Img img, IEnumerable<string> options, string extension = ".j2k", int? depth = null)
        => Opj(c, ToPlanes(img, depth), options, extension);

    private static Encoded Opj(Corpus<Jp2Tools> c, Planes planes, IEnumerable<string> options, string extension = ".j2k")
    {
        // opj_compress reads the samples of a Netpbm file as 8 or 16 bits, whatever its maximum value
        if (planes.Depth is not (8 or 16))
            return OpjRaw(c, planes.Width, planes.Height, planes.Depth, isSigned: false, [.. planes.Channels.Select(channel => (channel, 1, 1))], options, extension);

        var (data, sourceExtension) = WriteNetpbm(planes);
        var source = c.Scratch / ("source" + sourceExtension);
        var output = c.Scratch / ("output" + extension);
        File.WriteAllBytes(source, data);
        List<string> command = [c.Tools.OpjCompress, "-i", source.Value, "-o", output.Value, .. options];
        Proc.Run(command);
        return new Encoded(File.ReadAllBytes(output), [CommonCorpus.Display(command, c.Tools, [(source.Value, "{source}" + sourceExtension), (output.Value, "{output}" + extension)])]);
    }

    /// <summary>
    /// Encodes planes that opj_compress reads as raw samples (<c>-F</c>): any depth, signed or not, each plane with its
    /// own separation on the reference grid.
    /// </summary>
    private static Encoded OpjRaw(Corpus<Jp2Tools> c, int width, int height, int depth, bool isSigned, IReadOnlyList<(int[] Samples, int Dx, int Dy)> components, IEnumerable<string> options, string extension = ".j2k")
    {
        var builder = new ByteBuilder();
        foreach (var (samples, _, _) in components)
        {
            foreach (var sample in samples)
            {
                if (depth > 8)
                    builder.U16BE(sample & 0xFFFF);
                else
                    builder.U8(sample & 0xFF);
            }
        }

        var source = c.Scratch / "source.raw";
        var output = c.Scratch / ("output" + extension);
        File.WriteAllBytes(source, builder.ToArray());
        var format = $"{CommonCorpus.Str(width)},{CommonCorpus.Str(height)},{CommonCorpus.Str(components.Count)},{CommonCorpus.Str(depth)},{(isSigned ? "s" : "u")}@" + string.Join(':', components.Select(component => $"{CommonCorpus.Str(component.Dx)}x{CommonCorpus.Str(component.Dy)}"));
        List<string> command = [c.Tools.OpjCompress, "-i", source.Value, "-o", output.Value, "-F", format, .. options];
        Proc.Run(command);
        return new Encoded(File.ReadAllBytes(output), [CommonCorpus.Display(command, c.Tools, [(source.Value, "{source}.raw"), (output.Value, "{output}" + extension)])]);
    }

    /// <summary>Encodes an image of one or three planes with ojph_compress: the high-throughput block coder.</summary>
    private static Encoded Ojph(Corpus<Jp2Tools> c, Planes planes, bool isReversible, IEnumerable<string> options)
    {
        Py.Assert(planes.Channels.Count is 1 or 3, "ojph_compress reads PGM and PPM files");
        var (data, sourceExtension) = WriteNetpbm(planes);
        var source = c.Scratch / ("source" + sourceExtension);
        var output = c.Scratch / "output.j2c";
        File.WriteAllBytes(source, data);
        List<string> command = [c.Tools.OjphCompress, "-i", source.Value, "-o", output.Value, "-reversible", isReversible ? "true" : "false", .. options];
        Proc.Run(command);
        return new Encoded(File.ReadAllBytes(output), [CommonCorpus.Display(command, c.Tools, [(source.Value, "{source}" + sourceExtension), (output.Value, "{output}.j2c")])]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Decoding with the reference decoders
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// opj_decompress to PGX files, one per channel: the samples at the depth of the file, after the palette and the
    /// channel definitions of a JP2 file, and after the conversion of sYCC components to RGB.
    /// </summary>
    private static Planes? OpjDecode(Corpus<Jp2Tools> c, byte[] data, string extension)
    {
        var directory = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-jp2-opj-").FullName);
        try
        {
            var input = directory / ("input" + extension);
            var output = directory / "output.pgx";
            File.WriteAllBytes(input, data);
            List<string> command = [c.Tools.OpjDecompress, "-i", input.Value, "-o", output.Value];
            var result = Proc.Exec(command);
            if (result.ExitCode != 0 || !File.Exists(directory / "output_0.pgx"))
                return null;

            var channels = new List<PgxImage>();
            for (var i = 0; File.Exists(directory / $"output_{CommonCorpus.Str(i)}.pgx"); i++)
                channels.Add(PgxImage.Parse(File.ReadAllBytes(directory / $"output_{CommonCorpus.Str(i)}.pgx")));

            var first = channels[0];
            Py.Assert(channels.All(channel => channel.Width == first.Width && channel.Height == first.Height && channel.Precision == first.Precision && !channel.IsSigned), "opj_decompress: the channels differ");
            return new Planes(first.Width, first.Height, first.Precision, [.. channels.Select(channel => channel.Samples.ToArray())],
                CommonCorpus.Display(command, c.Tools, [(input.Value, "{input}"), (output.Value, "{output}.pgx")]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The native JPEG 2000 decoder of FFmpeg, in the pixel format it gives to the file: no conversion. It stores samples of
    /// less than 8 or 16 bits in the most significant bits; the shift is undone.
    /// </summary>
    private static Planes? FfmpegDecode(Corpus<Jp2Tools> c, byte[] data, int width, int height, int channels, int depth)
    {
        var path = c.Scratch / "ffmpeg-input.j2k";
        File.WriteAllBytes(path, data);
        var wide = depth > 8;
        var pixelFormat = (channels, wide) switch
        {
            (1, false) => "gray",
            (1, true) => "gray16le",
            (2, false) => "ya8",
            (2, true) => "ya16le",
            (3, false) => "rgb24",
            (3, true) => "rgb48le",
            (4, false) => "rgba",
            _ => "rgba64le",
        };
        var command = c.Tools.FfmpegCmd(["-f", "j2k_pipe", "-i", path.Value, "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", pixelFormat, "-"]);
        var result = Proc.Exec(command);
        var sampleBytes = wide ? 2 : 1;

        // The decoder reports a malformed code-block on its error output and returns a frame nonetheless
        if (result.ExitCode != 0 || result.Stderr.Length != 0 || result.Stdout.Length != width * height * channels * sampleBytes)
            return null;

        var shift = (wide ? 16 : 8) - depth;
        var planes = Enumerable.Range(0, channels).Select(_ => new int[width * height]).ToList();
        for (var i = 0; i < width * height; i++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = ((i * channels) + channel) * sampleBytes;
                var value = wide ? Bytes.U16LE(result.Stdout, offset) : result.Stdout[offset];
                Py.Assert((value & ((1 << shift) - 1)) == 0, "ffmpeg: the low bits of a sample are not zero");
                planes[channel][i] = value >> shift;
            }
        }

        return new Planes(width, height, depth, planes, CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]));
    }

    /// <summary>ojph_expand to a PGM or a PPM file: one or three channels.</summary>
    private static Planes? OjphDecode(Corpus<Jp2Tools> c, byte[] data, int channels)
    {
        var input = c.Scratch / "ojph-input.j2c";
        var output = c.Scratch / ("ojph-output" + (channels == 1 ? ".pgm" : ".ppm"));
        File.WriteAllBytes(input, data);
        File.Delete(output);
        List<string> command = [c.Tools.OjphExpand, "-i", input.Value, "-o", output.Value];
        var result = Proc.Exec(command);
        if (result.ExitCode != 0 || !File.Exists(output))
            return null;

        return ReadNetpbm(File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [(input.Value, "{input}"), (output.Value, "{output}" + (channels == 1 ? ".pgm" : ".ppm"))]));
    }

    private static Planes ReadNetpbm(byte[] data, string command)
    {
        var position = 0;
        var fields = new List<string>();
        while (fields.Count < 4)
        {
            while (data[position] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
                position++;
            if (data[position] == (byte)'#')
            {
                while (data[position] != (byte)'\n')
                    position++;
                continue;
            }

            var start = position;
            while (data[position] is not ((byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t'))
                position++;
            fields.Add(Encoding.ASCII.GetString(data, start, position - start));
        }

        position++;
        Py.Assert(fields[0] is "P5" or "P6", "Netpbm type " + fields[0]);
        var (width, height, maxValue) = (int.Parse(fields[1], CultureInfo.InvariantCulture), int.Parse(fields[2], CultureInfo.InvariantCulture), int.Parse(fields[3], CultureInfo.InvariantCulture));
        var channels = fields[0] == "P5" ? 1 : 3;
        var sampleBytes = maxValue > 255 ? 2 : 1;
        Py.Assert(data.Length - position == width * height * channels * sampleBytes, "Netpbm length");
        var planes = Enumerable.Range(0, channels).Select(_ => new int[width * height]).ToList();
        for (var i = 0; i < width * height; i++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = position + (((i * channels) + channel) * sampleBytes);
                planes[channel][i] = sampleBytes == 2 ? (data[offset] << 8) | data[offset + 1] : data[offset];
            }
        }

        return new Planes(width, height, System.Numerics.BitOperations.Log2((uint)maxValue) + 1, planes, command);
    }

    /// <summary>
    /// What the library shows of the channels of a file: gray, gray and opacity, RGB or RGB and opacity, by the number of
    /// channels, at 8 bits when the file has at most 8 bits and at 16 bits otherwise. A sample of another depth is scaled
    /// exactly: (v * targetMax + max / 2) / max.
    /// </summary>
    private static Img Present(Planes planes)
    {
        var target = planes.Depth <= 8 ? 8 : 16;
        long maximum = (1 << planes.Depth) - 1;
        long targetMaximum = (1 << target) - 1;
        var model = planes.Channels.Count switch
        {
            1 => "gray",
            2 => "graya",
            3 => "rgb",
            4 => "rgba",
            _ => throw new ToolException($"{planes.Channels.Count} channels"),
        };
        var pixels = new List<Px>(planes.Width * planes.Height);
        for (var i = 0; i < planes.Width * planes.Height; i++)
            pixels.Add(Px.From(planes.Channels.Select(channel => (int)(((channel[i] * targetMaximum) + (maximum / 2)) / maximum))));

        return new Img(planes.Width, planes.Height, model, target, pixels);
    }

    /// <summary>Compares what a decoder returns with the expected frame and records the result.</summary>
    private static Obj Check(string decoder, Planes? decoded, Img expected, string differs, string refused, bool excluded = false)
    {
        var check = new Obj { ["decoder"] = decoder };
        if (decoded is null)
        {
            check["result"] = "differs";
            check["notes"] = refused;
            check["excludedFromTolerance"] = true;
            return check;
        }

        var actual = Present(decoded);
        Py.Assert(actual.Width == expected.Width && actual.Height == expected.Height && actual.Model == expected.Model && actual.Depth == expected.Depth,
            $"{decoder}: {actual.Width}x{actual.Height} {actual.Model} of {actual.Depth} bits");
        check["command"] = decoded.Command;
        var layout = CommonCorpus.CanonicalLayout(expected);
        CommonCorpus.CompareFrames(check, [expected.Raw(layout)], [actual.Raw(layout)], expected.Depth / 8, "the decoders agree on what is shown");
        if (expected.Depth == 16 && (string?)check["result"] != "exact")
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

    private static string OpjLabel(Corpus<Jp2Tools> c) => $"OpenJPEG {c.Tools.OpenJpegVersion} opj_decompress";

    private static string FfmpegLabel(Corpus<Jp2Tools> c) => $"{c.Tools.FfmpegLabel} (libavcodec jpeg2000 decoder)";

    private static string OjphLabel(Corpus<Jp2Tools> c) => $"OpenJPH {c.Tools.OpenJphVersion} ojph_expand";

    /// <summary>Which decoders read a fixture, when not every one does: the reason is recorded with the cross-check.</summary>
    private sealed class Readers
    {
        /// <summary>Why opj_decompress does not read the fixture as the Recommendation says, or <see langword="null"/>.</summary>
        public string? OpenJpegLimit { get; init; }

        /// <summary>Why the decoder of FFmpeg does not read the fixture as the Recommendation says, or <see langword="null"/>.</summary>
        public string? FfmpegLimit { get; init; }

        /// <summary>Whether ojph_expand is run: a codestream of the high-throughput block coder with one or three channels.</summary>
        public bool UsesOpenJph { get; init; }

        /// <summary>Why ojph_expand does not read the fixture, or <see langword="null"/>.</summary>
        public string? OpenJphLimit { get; init; }

        /// <summary>Whether FFmpeg returns other channels than the ones the library shows: its output is not compared.</summary>
        public bool SkipsFfmpeg { get; init; }
    }

    /// <summary>
    /// Runs the decoders on an input and compares them with the expected frame. A decoder that is expected to agree must
    /// agree within <paramref name="maxError"/>; a decoder with a known limit must show it, so that a tool upgrade that
    /// lifts the limit is noticed.
    /// </summary>
    private static List<Obj> CrossChecks(Corpus<Jp2Tools> c, string name, byte[] data, string extension, Img expected, int fileDepth, Readers? readers = null, int maxError = 0, bool includeOpenJpeg = true)
    {
        readers ??= new Readers();
        var checks = new List<Obj>();
        var channels = Img.Channels(expected.Model);
        const string Rounding = "An independent decoder: the irreversible transformations are defined on real numbers, so two conforming decoders round some samples differently.";

        void Add(string label, Planes? decoded, string? limit)
        {
            var check = Check(label, decoded, expected, limit ?? Rounding, limit ?? "The decoder refuses the file.", excluded: limit is not null);
            var agrees = decoded is not null && Convert.ToInt32(check["maxAbsoluteError"], CultureInfo.InvariantCulture) <= maxError;
            if (limit is null)
            {
                Py.Assert(agrees, $"jp2/{name}: {label} does not give the expected samples: {PyJson.Dumps(check)}");
            }
            else
            {
                Py.Assert(!agrees || maxError > 0, $"jp2/{name}: {label} reads the file, the limit that is recorded no longer holds ({limit})");
            }

            checks.Add(check);
        }

        if (includeOpenJpeg)
            Add(OpjLabel(c), OpjDecode(c, data, extension), readers.OpenJpegLimit);
        if (!readers.SkipsFfmpeg)
            Add(FfmpegLabel(c), FfmpegDecode(c, data, expected.Width, expected.Height, channels, fileDepth), readers.FfmpegLimit);
        if (readers.UsesOpenJph)
            Add(OjphLabel(c), OjphDecode(c, data, channels), readers.OpenJphLimit);

        Py.Assert(checks.Exists(check => !check.ContainsKey("excludedFromTolerance")), $"jp2/{name}: no decoder confirms the reference");
        return checks;
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

        /// <summary>The grid points per meter of the capture resolution box: horizontally, then vertically.</summary>
        public (int X, int Y)? PointsPerMeter { get; init; }

        /// <summary>The depth of the samples of the file, when it is not the depth of the reference frame.</summary>
        public int? BitsPerComponent { get; init; }

        /// <summary>The color model the library reports, when it is not the one of the reference frame: a palette, sYCC components.</summary>
        public string? ColorModel { get; init; }
    }

    private static Obj PatternReference(string description) => new() { ["method"] = "hand-computed", ["description"] = description };

    /// <summary>
    /// Adds a valid fixture. The fields of the file are read by the independent reader of the harness and must agree with
    /// what the generator expects.
    /// </summary>
    private static void AddValid(Corpus<Jp2Tools> c, string name, string function, Encoded encoded, string extension, Img frame, Obj reference, List<Obj> checks, Expectation? expectation = null,
        Obj? parameters = null, string origin = "generated", Obj? comparison = null, string? notes = null, IEnumerable<string>? extraFeatures = null)
    {
        expectation ??= new Expectation();
        var fixtureId = "jp2/" + name;
        var file = ReferenceJpeg2000.Parse(encoded.Data);
        Py.Assert(file.IsFile == (extension is not (".j2k" or ".j2c")), $"{fixtureId}: {extension}");
        Py.Assert(file.Width == frame.Width && file.Height == frame.Height, $"{fixtureId}: size {file.Width}x{file.Height}");
        Py.Assert(file.HasEndOfCodestream && file.TileParts.Count > 0, $"{fixtureId}: tile-parts");
        Py.Assert((file.IccProfile is null) == (expectation.Icc is null) && (file.IccProfile is null || file.IccProfile.Value.Span.SequenceEqual(expectation.Icc)), $"{fixtureId}: ICC profile");
        Py.Assert((file.Exif is null) == (expectation.Exif is null) && (file.Exif is null || file.Exif.Value.Span.SequenceEqual(expectation.Exif)), $"{fixtureId}: EXIF");
        Py.Assert((file.Xmp is null) == (expectation.Xmp is null) && (file.Xmp is null || file.Xmp.Value.Span.SequenceEqual(expectation.Xmp)), $"{fixtureId}: XMP");
        var bits = expectation.BitsPerComponent ?? frame.Depth;
        if (file.PaletteEntryCount == 0)
            Py.Assert(file.Components.All(component => component.Precision == bits && !component.IsSigned), $"{fixtureId}: {file.Components[0].Precision} bits");
        else
            Py.Assert(file.PaletteDepths.All(depth => depth == bits), $"{fixtureId}: palette of {file.PaletteDepths[0]} bits");

        var (pixelFormat, colorModel) = (frame.Model, frame.Depth) switch
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
        var layouts = new List<string> { CommonCorpus.CanonicalLayout(frame) };
        if (frame.Model is "rgb" or "gray" && !(frame.Model == "rgb" && frame.Depth == 16))
            layouts.Add(frame.Model == "rgb" ? "rgb8" : frame.Depth == 8 ? "gray8" : "gray16le");

        var expected = new Obj
        {
            ["width"] = frame.Width,
            ["height"] = frame.Height,
            ["pixelFormat"] = pixelFormat,
            ["colorModel"] = expectation.ColorModel ?? colorModel,
            ["bitsPerComponent"] = bits,
            ["orientation"] = expectation.Orientation,
            ["iccProfile"] = expectation.Icc is null ? "none" : frame.Model.StartsWith("gray", StringComparison.Ordinal) ? "gray" : "rgb",
            ["animation"] = null,
        };
        if (expectation.PointsPerMeter is { } resolution)
        {
            Py.Assert(file.CaptureResolution == (resolution.Y, 1, resolution.X, 1, 0, 0), $"{fixtureId}: capture resolution {file.CaptureResolution}");
            expected["resolution"] = new Obj
            {
                ["horizontalDpi"] = resolution.X * 0.0254,
                ["verticalDpi"] = resolution.Y * 0.0254,
                ["encoded"] = new Obj { ["unit"] = "meter", ["x"] = resolution.X, ["y"] = resolution.Y },
            };
        }
        else
        {
            Py.Assert(file.CaptureResolution is null, $"{fixtureId}: capture resolution");
            expected["resolution"] = null;
        }

        expected["profiles"] = new Obj
        {
            ["icc"] = expectation.Icc is not null ? CommonCorpus.ProfileRecord(expectation.Icc) : null,
            ["exif"] = expectation.Exif is not null ? CommonCorpus.ProfileRecord(expectation.Exif) : null,
            ["xmp"] = expectation.Xmp is not null ? CommonCorpus.ProfileRecord(expectation.Xmp) : null,
        };
        expected["text"] = new List<object?>();
        expected["frameCount"] = 1;
        expected["frames"] = new List<object?> { c.FrameEntry(fixtureId, "frame-0", frame, "0/1", layouts) };
        expected["poster"] = null;

        var input = c.Write(fixtureId + extension, encoded.Data);
        input["role"] = "encoded";
        var features = Features(file);
        features.AddRange(extraFeatures ?? []);
        features.Sort(StringComparer.Ordinal);
        reference["crossChecks"] = checks;
        var fixture = new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = "jp2",
            ["input"] = input,
            ["provenance"] = c.Provenance(origin, function, parameters, ToolList(c), encoded.Commands),
            ["features"] = features,
            ["reference"] = reference,
            ["expected"] = expected,
            ["comparison"] = comparison ?? new Obj { ["mode"] = "exact" },
        };
        if (!string.IsNullOrEmpty(notes))
            fixture["notes"] = notes;
        c.Fixtures.Add(fixture);
    }

    private static List<string> ToolList(Corpus<Jp2Tools> c) =>
    [
        $"OpenJPEG {c.Tools.OpenJpegVersion} (opj_compress, opj_decompress)",
        $"OpenJPH {c.Tools.OpenJphVersion} (ojph_compress, ojph_expand)",
        $"{c.Tools.FfmpegLabel} (cross-check)",
        ToolSet.RuntimeLabel,
    ];

    /// <summary>The feature tags of a file, from the fields the independent reader of the harness reads.</summary>
    private static List<string> Features(ReferenceJpeg2000 file)
    {
        var main = file.CodingStyles[0];
        var features = new SortedSet<string>(StringComparer.Ordinal)
        {
            file.IsFile ? "jp2.file=jp2" : "jp2.file=codestream",
            "jp2.components=" + CommonCorpus.Str(file.Components.Count),
            "jp2.bits=" + CommonCorpus.Str(file.Components[0].Precision),
            "jp2.transform=" + (main.IsReversible ? "5-3" : "9-7"),
            "jp2.progression=" + (file.ProgressionOrder switch { 0 => "lrcp", 1 => "rlcp", 2 => "rpcl", 3 => "pcrl", _ => "cprl" }),
            "jp2.levels=" + CommonCorpus.Str(main.DecompositionLevels),
            "jp2.quantization=" + (file.QuantizationStyle switch { 0 => "none", 1 => "derived", _ => "expounded" }),
        };
        if (file.UsesComponentTransform)
            features.Add("jp2.componentTransform");
        if (file.LayerCount > 1)
            features.Add("jp2.layers");
        if (file.TileCount > 1)
            features.Add("jp2.tiles");
        if (file.TileParts.Count > file.TileCount)
            features.Add("jp2.tileParts");
        if (file.ImageXOffset != 0 || file.ImageYOffset != 0 || file.TileXOffset != 0 || file.TileYOffset != 0)
            features.Add("jp2.offsets");
        if (file.Components.Any(component => component.XSubsampling != 1 || component.YSubsampling != 1))
            features.Add("jp2.subsampling");
        if (file.CodingStyles.Any(style => style.HasPrecincts))
            features.Add("jp2.precincts");
        if (file.UsesSop)
            features.Add("jp2.sop");
        if (file.UsesEph)
            features.Add("jp2.eph");
        if (file.HasPacketLengths)
            features.Add("jp2.plt");
        if (file.HasTilePartLengths)
            features.Add("jp2.tlm");
        if (file.HasPackedHeadersInMainHeader)
            features.Add("jp2.ppm");
        if (file.HasPackedHeadersInTilePartHeaders)
            features.Add("jp2.ppt");
        if (file.ProgressionChangeCount > 0)
            features.Add("jp2.poc");
        if (file.Regions.Count > 0)
            features.Add("jp2.roi");
        if (file.CodingStyles.Any(style => style.Component >= 0))
            features.Add("jp2.coc");
        if (file.ComponentQuantizationCount > 0)
            features.Add("jp2.qcc");
        if (file.CodingStyles.Any(style => style.IsInTilePartHeader))
            features.Add("jp2.tileCodingStyle");

        foreach (var style in file.CodingStyles)
        {
            if ((style.CodeBlockStyle & 0x40) != 0)
            {
                features.Add("jp2.ht");
                if ((style.CodeBlockStyle & 0x08) != 0)
                    features.Add("jp2.style=causal");
                continue;
            }

            foreach (var (bit, tag) in new[] { (0x01, "bypass"), (0x02, "reset"), (0x04, "termall"), (0x08, "causal"), (0x10, "pterm"), (0x20, "segsym") })
            {
                if ((style.CodeBlockStyle & bit) != 0)
                    features.Add("jp2.style=" + tag);
            }
        }

        if (file.IsFile)
        {
            foreach (var box in file.BoxTypes.Where(type => type is not ("jP  " or "ftyp" or "jp2h" or "ihdr" or "colr" or "jp2c")))
                features.Add("jp2.box=" + box.Trim());
            features.Add("jp2.brand=" + file.Brand!.Trim());
            features.Add(file.ColorMethod == 1 ? "jp2.color=enumerated" : "jp2.color=icc");
            if (file.ColorMethod == 1)
                features.Add("jp2.colorSpace=" + (file.EnumeratedColorSpace switch { 16 => "srgb", 17 => "gray", 18 => "sycc", 12 => "cmyk", var other => CommonCorpus.Str(other ?? -1) }));
            if (file.PaletteEntryCount > 0)
                features.Add("jp2.palette");
            if (file.ChannelDefinitions.Any(channel => channel.Type == 1))
                features.Add("jp2.opacity");
            var boxes = file.BoxTypes.ToList();
            // The boxes that follow the codestream: without them the file is complete too
            var codestream = boxes.IndexOf("jp2c");
            if (codestream < boxes.Count - 1)
            {
                features.Add("jp2.boxesAfterCodestream");
                if (boxes.Skip(codestream + 1).Contains("uuid", StringComparer.Ordinal))
                    features.Add("jp2.metadataAfterCodestream");
            }
        }

        return [.. features];
    }

    /// <summary>The comparison policy of a fixture whose reference is decoded, in units of the 8-bit scale.</summary>
    private static Obj LossyPolicy(string name, IEnumerable<Obj> checks)
    {
        var (maxError, meanError, justifiedMax, justifiedMean) = CommonCorpus.JpegJustifiedTolerance(checks);

        // Decoders that agree on every sample leave no room for a tolerance: the fixture is compared exactly
        if (maxError == 0)
            return new Obj { ["mode"] = "exact" };

        var cross = $"the independent decoder differs from the reference by max {CommonCorpus.Str(maxError)}, mean {Py.FormatFixed(meanError, 4)}, which would justify max {CommonCorpus.Str(justifiedMax)}, mean {Py.FormatFixed(justifiedMean, 2)}";
        if (!LibraryJp2Measurements.TryGetValue("jp2/" + name, out var measured))
        {
            return new Obj
            {
                ["mode"] = "tolerance",
                ["maxAbsoluteError"] = justifiedMax,
                ["maxMeanAbsoluteError"] = justifiedMean,
                ["justification"] = $"The irreversible transformations of JPEG 2000 are defined on real numbers; the library is not measured yet: {cross}.",
            };
        }

        var max = Math.Min(justifiedMax, Math.Max(1, measured.Max));
        var mean = Math.Min(justifiedMean, Math.Ceiling(Math.Round((measured.Mean + 0.01) * 100, 6, MidpointRounding.AwayFromZero)) / 100);
        return new Obj
        {
            ["mode"] = "tolerance",
            ["maxAbsoluteError"] = max,
            ["maxMeanAbsoluteError"] = mean,
            ["justification"] = $"The irreversible transformations of JPEG 2000 are defined on real numbers (measured: max {CommonCorpus.Str(measured.Max)}, mean {Py.FormatFixed(measured.Mean, 4)}). Allowed: the measured max (at least 1) and the measured mean + 0.01; {cross}.",
        };
    }

    /// <summary>The pinned tools of the JPEG 2000 generator, from the meziantou/prebuilt release.</summary>
    private sealed partial class Jp2Tools : ToolSet
    {
        public Jp2Tools(bool acceptVersions)
            : base(Download(PrebuiltTools.Ffmpeg))
        {
            OpjCompress = Download(PrebuiltTools.OpjCompress);
            OpjDecompress = Download(PrebuiltTools.OpjDecompress);
            OjphCompress = Download(PrebuiltTools.OjphCompress);
            OjphExpand = Download(PrebuiltTools.OjphExpand);

            // The tools print their usage, with the version of the library, when they have no argument
            var compress = Proc.Exec([OpjCompress, "-h"]);
            var decompress = Proc.Exec([OpjDecompress, "-h"]);
            var openJpeg = OpenJpegVersionRegex().Match(Encoding.UTF8.GetString(compress.Stdout) + Encoding.UTF8.GetString(compress.Stderr));
            var openJpegDecoder = OpenJpegVersionRegex().Match(Encoding.UTF8.GetString(decompress.Stdout) + Encoding.UTF8.GetString(decompress.Stderr));
            if (!openJpeg.Success || !openJpegDecoder.Success || openJpeg.Groups["version"].Value != openJpegDecoder.Groups["version"].Value)
                throw new FatalException("Cannot read one OpenJPEG version from opj_compress and opj_decompress.");

            OpenJpegVersion = openJpeg.Groups["version"].Value;

            // ojph_compress does not print its version: it writes it in the comment of every codestream
            OpenJphVersion = PrebuiltTools.OjphCompress.Version;
            Py.Assert(PrebuiltTools.OjphExpand.Version == OpenJphVersion, "one OpenJPH version");
            var mismatches = new List<string>();
            if (OpenJpegVersion != PinnedOpenJpeg)
                mismatches.Add($"OpenJPEG {OpenJpegVersion} (pinned {PinnedOpenJpeg})");
            if (OpenJphVersion != PinnedOpenJph)
                mismatches.Add($"OpenJPH {OpenJphVersion} (pinned {PinnedOpenJph})");
            if (mismatches.Count > 0 && !acceptVersions)
                throw new FatalException("Tool versions differ from the pinned versions: " + string.Join(", ", mismatches) + ". Review the impact and rerun with --accept-tool-versions (then update the pinned versions).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Ffmpeg] = "ffmpeg",
                [OpjCompress] = "opj_compress",
                [OpjDecompress] = "opj_decompress",
                [OjphCompress] = "ojph_compress",
                [OjphExpand] = "ojph_expand",
            };
        }

        public string OpjCompress { get; }

        public string OpjDecompress { get; }

        public string OjphCompress { get; }

        public string OjphExpand { get; }

        public string OpenJpegVersion { get; }

        public string OpenJphVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();

        [GeneratedRegex(@"openjp2 library v(?<version>\d+\.\d+\.\d+)\b", RegexOptions.ExplicitCapture)]
        private static partial Regex OpenJpegVersionRegex();
    }
}
