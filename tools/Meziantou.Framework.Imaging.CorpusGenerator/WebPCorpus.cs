using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the WebP fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/webp, invalid/webp, limit/webp entries).
/// This generator is the reviewed, reproducible recipe of every committed WebP fixture. It
/// is NEVER run by the tests and does not use the library under test: expected pixels are either hand-defined (patterns
/// and frames written below, encoded losslessly by libwebp cwebp with -exact; animation canvases composed by the
/// transcription of the WebP container specification below) or decoded by libwebp dwebp (lossy VP8 payloads), and are
/// cross-checked with genuinely independent decoders (FFmpeg/libavcodec for still images, libwebp anim_dump for
/// animations).
/// It complements GoldenCorpus, which needs macOS tools: this generator only replaces the WebP entries of
/// tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format "webp") and their files, and keeps every other entry and file unchanged;
/// GoldenCorpus keeps the WebP entries and files when it regenerates the rest of the corpus.
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- webp            # check: regenerate in a temporary directory and diff
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- webp --write    # explicit regeneration (review the diff afterwards!)
/// Requirements: libwebp tools (cwebp, dwebp, webpmux, anim_dump) and FFmpeg at the pinned versions below (override with
/// --accept-tool-versions after reviewing the differences). Any OS.
/// </summary>
internal static partial class WebPCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/WebPCorpus.cs";

    // Tool versions used to produce the committed WebP fixtures. Regenerating with other versions is allowed only explicitly
    // (--accept-tool-versions) and must be reviewed: encoded inputs and decoded references may change.
    private static readonly (string Name, string Version)[] PinnedTools =
    [
        ("libwebp", "1.3.2"),
        ("ffmpeg", "6.1.1-3ubuntu5"),
    ];

    // Measured disagreement of the library decoder with each dwebp reference: (max absolute
    // error, mean absolute color error), printed by Tests/Conformance/WebPDecoderConformanceTests, which also fails when a
    // policy derived from these values is looser or tighter than the decoder needs. The values are reviewed measurements of
    // the library (never used as pixel references); a fixture without a measurement keeps the bound justified by the
    // independent cross-checks alone.
    private static readonly Dictionary<string, (int Max, double Mean)> LibraryWebPMeasurements = new(StringComparer.Ordinal)
    {
        ["webp/anim-mixed-lossy"] = (1, 0.0017),
        ["webp/lossy-alpha-compressed"] = (1, 0.0019),
        ["webp/lossy-alpha-raw"] = (0, 0.0),
        ["webp/lossy-detail-normal-filter"] = (1, 0.0048),
        ["webp/lossy-detail-simple-filter"] = (1, 0.0053),
        ["webp/lossy-metadata"] = (1, 0.0035),
        ["webp/lossy-no-filter-high-quality"] = (1, 0.0026),
        ["webp/lossy-smooth-odd"] = (1, 0.0071),
        ["webp/lossy-tiny-3x2"] = (0, 0.0),
    };

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Readable command line for the manifest (tool names instead of absolute paths, symbolic file names). Unlike
    /// <see cref="CommonCorpus.Display"/>, the substitutions apply longest path first (ties keep the given order).</summary>
    private static string Display(IEnumerable<string> command, WebPTools tools, IEnumerable<(string Path, string Symbol)> substitutions)
    {
        var names = tools.DisplayNames;
        var ordered = substitutions.OrderByDescending(item => item.Path.Length).ToList();
        var parts = new List<string>();
        foreach (var argument in command)
        {
            var part = names.TryGetValue(argument, out var name) ? name : argument;
            foreach (var (path, symbol) in ordered)
                part = part.Replace(path, symbol, StringComparison.Ordinal);
            parts.Add(part);
        }

        return string.Join(' ', parts);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Driver
    // -----------------------------------------------------------------------------------------------------------------

    private static bool IsWebPFixture(Obj fixture) => (string?)fixture["format"] == "webp";

    private static int Generate(FullPath outDir, WebPTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<WebPTools>(outDir, tools, ScriptPath))
        {
            BuildLossless(corpus);
            BuildLossy(corpus);
            BuildAnimations(corpus);
            BuildInvalid(corpus);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsWebPFixture(fixture), (string?)fixture["id"]);

        // Every other fixture and file of the committed corpus is kept unchanged (GoldenCorpus owns them)
        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsWebPFixture);
        return fixtures.Count;
    }

    public static int Run(GeneratorOptions options)
    {
        var tools = new WebPTools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-webp-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => $"{fixtures} WebP fixtures; corpus: {files} files, {bytes} bytes",
            "The committed WebP fixtures are reproducible.");
    }

    /// <summary>The pinned tools of the WebP generator.</summary>
    private sealed class WebPTools : ToolSet
    {
        private static readonly string[] Names = ["cwebp", "dwebp", "webpmux", "anim_dump", "ffmpeg"];

        public WebPTools(bool acceptVersions)
            : base(RequireAll())
        {
            Cwebp = ExecutableFinder.GetFullExecutablePath("cwebp")!;
            Dwebp = ExecutableFinder.GetFullExecutablePath("dwebp")!;
            Webpmux = ExecutableFinder.GetFullExecutablePath("webpmux")!;
            AnimDump = ExecutableFinder.GetFullExecutablePath("anim_dump")!;
            LibwebpVersion = FirstLine(Proc.RunText([Cwebp, "-version"]));
            foreach (var tool in new[] { Dwebp, Webpmux, AnimDump })
            {
                var version = FirstLine(Proc.RunText([tool, "-version"]));
                if (version.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1] != LibwebpVersion)
                    throw new FatalException($"{tool} reports version {version}, cwebp {LibwebpVersion}: use one libwebp installation.");
            }

            var actual = new Dictionary<string, string>(StringComparer.Ordinal) { ["libwebp"] = LibwebpVersion, ["ffmpeg"] = FfmpegVersion };
            var mismatches = PinnedTools.Where(p => actual[p.Name] != p.Version).Select(p => $"{p.Name} {actual[p.Name]} (pinned {p.Version})").ToList();
            if (mismatches.Count > 0 && !acceptVersions)
            {
                throw new FatalException("Tool versions differ from the pinned versions: " + string.Join(", ", mismatches) +
                    ". Review the impact and rerun with --accept-tool-versions (then update PinnedTools).");
            }

            LibwebpLabel = "libwebp " + LibwebpVersion;
            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Cwebp] = "cwebp",
                [Dwebp] = "dwebp",
                [Webpmux] = "webpmux",
                [AnimDump] = "anim_dump",
                [Ffmpeg] = "ffmpeg",
            };
        }

        public string Cwebp { get; }

        public string Dwebp { get; }

        public string Webpmux { get; }

        public string AnimDump { get; }

        public string LibwebpVersion { get; }

        public string LibwebpLabel { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        /// <summary>Checks every tool at once (one message lists all the missing ones) and returns the FFmpeg path.</summary>
        private static string RequireAll()
        {
            var missing = Names.Where(n => ExecutableFinder.GetFullExecutablePath(n) is null).ToList();
            if (missing.Count > 0)
                throw new FatalException("Missing required tools: " + string.Join(", ", missing));
            return ExecutableFinder.GetFullExecutablePath("ffmpeg")!;
        }

        // str.splitlines()[0].strip(): every line boundary of str.splitlines
        private static string FirstLine(string text) =>
            text.Split(['\n', '\r', '\v', '\f', (char)0x1C, (char)0x1D, (char)0x1E, (char)0x85, (char)0x2028, (char)0x2029])[0].Trim();
    }
}
