using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A format of the hand-assembled generators: its manifest name, its library name and its transcribed reader.</summary>
internal sealed record HandAssembledFormat(string Name, string LibraryName, Func<byte[], (Img Image, List<string> Features)> Read);

/// <summary>
/// The shared driver and helpers of the generators whose inputs are assembled byte by byte and cross-checked with FFmpeg
/// (BmpCorpus, TgaCorpus and PnmCorpus).
/// </summary>
internal static class HandAssembledCorpus
{
    // ---------------------------------------------------------------------------------------------------------------------
    // Fixture assembly
    // ---------------------------------------------------------------------------------------------------------------------

    public static Img RgbaImage(int width, int height, IEnumerable<Px> pixels, int depth = 8) => new(width, height, "rgba", depth, pixels);

    private static Img ToRgba(Img img) => new(img.Width, img.Height, "rgba", img.Depth, img.Rgba());

    /// <summary>The raw reference layouts of an image: the canonical RGBA layout and the native layout of its model.</summary>
    private static List<string> NativeLayouts(Img img)
    {
        var canonical = CommonCorpus.CanonicalLayout(img);
        var native = (img.Model, img.Depth) switch
        {
            ("gray", 8) => "gray8",
            ("gray", 16) => "gray16le",
            ("rgb", 8) => "rgb8",
            _ => null,
        };
        return native is null ? [canonical] : [canonical, native];
    }

    /// <summary>Checks the input against the transcribed specification and FFmpeg, then records the fixture.</summary>
    public static void AddFixture(Corpus<FfmpegTools> c, HandAssembledFormat format, string fixtureId, string filename, byte[] data, Img reference, string pixelFormat, string colorModel, int bits,
        string function, Obj parameters, string description, IList<string>? commands = null, string? notes = null, IReadOnlyList<string>? required = null,
        string crossCheck = "exact", string? crossCheckNotes = null)
    {
        commands ??= [];
        required ??= [];
        var (decoded, features) = format.Read(data);
        Py.Assert(ToRgba(decoded).Rgba().SequenceEqual(ToRgba(reference).Rgba()), fixtureId);
        Py.Assert(decoded.Width == reference.Width && decoded.Height == reference.Height, fixtureId);
        Py.Assert(required.All(features.Contains), $"{fixtureId}: {string.Join(", ", required.Except(features, StringComparer.Ordinal).Order(StringComparer.Ordinal))}");
        var path = c.Scratch / (fixtureId.Replace('/', '-') + "." + filename[(filename.LastIndexOf('.', StringComparison.Ordinal) + 1)..]);
        File.WriteAllBytes(path, data);
        var (check, _) = c.FfmpegCrossCheck(path, [reference], CommonCorpus.CanonicalLayout(reference), animated: false, format.Name, allowFailure: true);
        if (!string.IsNullOrEmpty(crossCheckNotes))
            check["notes"] = crossCheckNotes;
        Py.Assert((string?)check["result"] == crossCheck, $"{fixtureId}: {PyJson.Dumps(check)}");
        List<string> tools = [c.Tools.FfmpegLabel + (commands.Count > 0 ? "" : " (cross-check only)"), ToolSet.RuntimeLabel];
        c.AddValid(fixtureId, filename, data, [reference], CommonCorpus.StillExpected(format.Name, reference, pixelFormat, colorModel, bits),
            c.Provenance(commands.Count > 0 ? "generated" : "hand-authored", function, parameters, tools, commands),
            CommonCorpus.HandReference(description, [check]), features, layouts: NativeLayouts(reference), notes: notes);
    }

    /// <summary>Records a malformed, unsupported or over-limit input; the defect is confirmed by the transcribed specification.</summary>
    public static void AddError(Corpus<FfmpegTools> c, HandAssembledFormat format, string fixtureId, string filename, byte[] data, string exception, string notes, string? feature = null,
        string? limitKind = null, Obj? decodeOptions = null, string function = "BuildErrors", Obj? parameters = null, bool confirm = true, IList<string>? features = null)
    {
        if (confirm)
        {
            var rejected = false;
            try
            {
                format.Read(data);
            }
            catch (FormatError)
            {
                rejected = true;
            }

            if (!rejected)
                throw new InvalidOperationException($"{fixtureId}: the transcribed specification accepted the input");
        }

        var expected = new Obj
        {
            ["exception"] = exception,
            ["format"] = format.LibraryName,
        };
        if (!string.IsNullOrEmpty(feature))
            expected["feature"] = feature;
        if (!string.IsNullOrEmpty(limitKind))
        {
            expected["limitKind"] = limitKind;
            expected.Remove("format");
        }

        var kind = exception switch
        {
            "ImageResourceLimitException" => "limit",
            "UnsupportedImageFeatureException" => "unsupported",
            _ => "invalid",
        };
        c.AddError(fixtureId, kind, filename, data, format.Name,
            c.Provenance("hand-authored", function, parameters, [ToolSet.RuntimeLabel]), expected,
            features: features, decodeOptions: decodeOptions, notes: notes);
    }

    public static void AddLimit(Corpus<FfmpegTools> c, HandAssembledFormat format, string fixtureId, string sourceId, Obj limits, string limitKind, string notes)
    {
        var source = c.Fixtures.First(f => (string)f["id"]! == sourceId);
        var input = (Obj)source["input"]!;
        c.AddError(fixtureId, "limit", null, null, format.Name,
            c.Provenance("hand-authored", "BuildLimits", null, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = limitKind },
            features: source.GetValueOrDefault("features") as IList<string>, decodeOptions: new Obj { ["limits"] = limits },
            existingInput: new Obj { ["path"] = input["path"], ["sha256"] = input["sha256"] }, notes: notes);
    }

    /// <summary>The committed valid inputs generated so far, by fixture id: the sources of the byte edits of the error fixtures.</summary>
    public static Dictionary<string, byte[]> ReadValidInputs(Corpus<FfmpegTools> c) =>
        c.Fixtures.Where(f => (string)f["kind"]! == "valid")
            .ToDictionary(f => (string)f["id"]!, f => File.ReadAllBytes(c.Out / (string)((Obj)f["input"]!)["path"]!), StringComparer.Ordinal);

    public static List<List<T>> ChunkRows<T>(IReadOnlyList<T> pixels, int width) =>
        [.. Enumerable.Range(0, pixels.Count / width).Select(y => pixels.Skip(y * width).Take(width).ToList())];

    /// <summary>Encodes a hand-defined pattern with FFmpeg's independent encoder of the format.</summary>
    public static (byte[] Data, string Command) FfmpegEncode(Corpus<FfmpegTools> c, Img img, string name, string extension, IEnumerable<string>? extra = null)
    {
        var (raw, pixelFormat) = img.FfmpegRaw();
        var source = c.Scratch / (name + ".src.raw");
        var output = c.Scratch / (name + "." + extension);
        File.WriteAllBytes(source, raw);
        var command = c.Tools.FfmpegCmd(["-f", "rawvideo", "-pixel_format", pixelFormat,
            "-video_size", Inv($"{img.Width}x{img.Height}"), "-i", source.Value,
            "-frames:v", "1", "-update", "1", .. extra ?? [], output.Value]);
        Proc.Run(command);
        var shown = CommonCorpus.Display(command, c.Tools, [(source.Value, "{source}.raw"), (output.Value, "{output}." + extension)]);
        return (File.ReadAllBytes(output), shown);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Small helpers (Python bytes operations)
    // ---------------------------------------------------------------------------------------------------------------------

    public static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>bytes.replace(old, new, 1).</summary>
    public static byte[] ReplaceFirst(byte[] data, byte[] oldValue, byte[] newValue)
    {
        var index = Bytes.Find(data, oldValue);
        return index < 0 ? data : Bytes.Concat(data[..index], newValue, data[(index + oldValue.Length)..]);
    }

    /// <summary>data[:offset] + raw + data[offset + len(raw):].</summary>
    public static byte[] Splice(byte[] data, int offset, byte[] raw) =>
        Bytes.Concat(Bytes.Slice(data, null, offset), raw, Bytes.Slice(data, offset + raw.Length));

    public static byte[] U16(int value) => new ByteBuilder().U16LE(value).ToArray();

    public static byte[] I32(int value) => new ByteBuilder().I32LE(value).ToArray();

    public static byte[] U32(params long[] values)
    {
        var b = new ByteBuilder();
        foreach (var value in values)
            b.U32LE(value);
        return b.ToArray();
    }

    /// <summary>sorted(set(values)).</summary>
    public static List<string> SortedSet(IEnumerable<string> values) => [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The exact expansion of an n-bit channel to 8 bits: round(value * 255 / (2^n - 1)).</summary>
    public static int ScaleChannel(long value, int bits)
    {
        var maximum = (1L << bits) - 1;
        return (int)((value * 255 + maximum / 2) / maximum);
    }

    /// <summary>round(v / 255 * n): an 8-bit sample reduced to an n-level channel.</summary>
    public static int Reduce(int value, int maximum) => (int)Py.Round((double)value / 255 * maximum);

    // ---------------------------------------------------------------------------------------------------------------------
    // Driver
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Generates the fixtures of one format and merges them into the committed manifest.</summary>
    /// <param name="build">Adds the fixtures of the format to the corpus.</param>
    public static int Run(GeneratorOptions options, HandAssembledFormat format, string scriptPath, Action<Corpus<FfmpegTools>> build)
    {
        var tools = new FfmpegTools(options.AcceptToolVersions);
        var label = format.LibraryName.ToUpperInvariant();
        return CorpusDriver.Run(options, $"mfi-{format.Name}-corpus-", output => Generate(output, tools, format, scriptPath, build),
            (fixtures, files, bytes) => Inv($"{fixtures} {label} fixtures; corpus: {files} files, {bytes} bytes"),
            $"The committed {label} fixtures are reproducible.");
    }

    private static int Generate(FullPath outDir, FfmpegTools tools, HandAssembledFormat format, string scriptPath, Action<Corpus<FfmpegTools>> build)
    {
        using var corpus = new Corpus<FfmpegTools>(outDir, tools, scriptPath);
        build(corpus);
        foreach (var fixture in corpus.Fixtures)
            Py.Assert((string)fixture["format"]! == format.Name, (string)fixture["id"]!);

        // Every other fixture and file of the committed corpus is kept unchanged (the other generators own them)
        CorpusDriver.MergeIntoCommittedManifest(outDir, corpus.Fixtures, f => (string)f["format"]! == format.Name);
        return corpus.Fixtures.Count;
    }

    /// <summary>A defect of the input: the transcribed specification rejects it.</summary>
    public sealed class FormatError(string message) : Exception(message);
}

/// <summary>The pinned tools of the BMP, TGA and Netpbm generators.</summary>
internal sealed class FfmpegTools : ToolSet
{
    // Tool versions used to produce the committed fixtures. Regenerating with other versions is allowed only explicitly
    // (--accept-tool-versions) and must be reviewed.
    private static readonly (string Name, string Version)[] PinnedTools = [("ffmpeg", "9.0.2")];

    public FfmpegTools(bool acceptVersions)
        : base(Require("ffmpeg"))
    {
        var pinned = PinnedTools.Single(p => p.Name == "ffmpeg").Version;
        if (FfmpegVersion != pinned && !acceptVersions)
        {
            throw new FatalException($"FFmpeg {FfmpegVersion} differs from the pinned version {pinned}. Review the impact and rerun with --accept-tool-versions " +
                "(then update PinnedTools).");
        }

        DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg" };
    }

    public override IReadOnlyDictionary<string, string> DisplayNames { get; }
}
