using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Re-decodes the committed corpus with the independent tools of the meziantou/prebuilt release pinned by the
/// <c>Meziantou.Prebuilt</c> package (the binaries of the interop tests) and compares the result with the committed
/// references: detects reference drift and the impact of a tool upgrade before regenerating. Read-only; the tests never run it.
/// <list type="bullet">
/// <item><description>FFmpeg: every valid non-JPEG fixture whose generation-time FFmpeg cross-check agreed (exactly, or
/// except hidden colors of fully transparent pixels). JPEG references are decoded by libjpeg-turbo and only
/// cross-checked at generation time (FFmpeg's swscale conversion varies across versions).</description></item>
/// <item><description>libwebp: every valid WebP fixture with dwebp (stills, within the fixture policy) and anim_dump
/// (animations, visible pixels within the policy plus one unit, alpha exact).</description></item>
/// </list>
/// </summary>
internal static class CorpusVerifier
{
    public static int Run()
    {
        var tools = new VerifyTools();
        Console.WriteLine($"Tools (meziantou/prebuilt {PrebuiltTools.ReleaseVersion}): {tools.FfmpegLabel}, {tools.DwebpLabel}, anim_dump");
        var failures = new List<string>();
        var verified = 0;
        var scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-corpus-verify-").FullName);
        try
        {
            foreach (var fixture in CorpusDriver.Fixtures(CorpusDriver.LoadCommittedManifest()).Where(f => (string)f["kind"]! == "valid"))
            {
                var id = (string)fixture["id"]!;
                var format = (string)fixture["format"]!;
                List<string> errors;
                if (format == "webp")
                {
                    errors = VerifyWebP(tools, scratch, fixture);
                }
                else if (format != "jpeg" && FfmpegCheck(fixture) is { } check)
                {
                    errors = VerifyFfmpeg(tools, fixture, (string)check["result"]! == "equivalent");
                }
                else
                {
                    continue;
                }

                verified++;
                failures.AddRange(errors.Select(error => $"{id}: {error}"));
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        foreach (var failure in failures)
            Console.WriteLine("  " + failure);
        Console.WriteLine($"{verified} fixtures verified, {failures.Count} disagreement(s).");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>The generation-time FFmpeg cross-check of a fixture decoded to straight RGBA that agreed with the reference.</summary>
    private static Obj? FfmpegCheck(Obj fixture)
    {
        var reference = fixture.TryGetValue("reference", out var value) ? value as Obj : null;
        var checks = reference is not null && reference.TryGetValue("crossChecks", out var list) && list is List<object?> items ? items.Cast<Obj>() : [];
        return checks.FirstOrDefault(check =>
            ((string)check["decoder"]!).StartsWith("ffmpeg ", StringComparison.Ordinal)
            && check.TryGetValue("command", out var command) && ((string)command!).Contains("-pix_fmt rgba", StringComparison.Ordinal)
            && (string)check["result"]! is "exact" or "equivalent");
    }

    private static List<string> VerifyFfmpeg(VerifyTools tools, Obj fixture, bool hiddenColorsMayDiffer)
    {
        var expected = (Obj)fixture["expected"]!;
        var (width, height) = (Convert.ToInt32(expected["width"], CultureInfo.InvariantCulture), Convert.ToInt32(expected["height"], CultureInfo.InvariantCulture));
        var frames = ((List<object?>)expected["frames"]!).Cast<Obj>().ToList();
        var layout = Buffers(frames[0]).Any(b => (string)b["layout"]! == "rgba16le") ? "rgba16le" : "rgba8";
        var (pixelFormat, bytesPerPixel) = layout == "rgba16le" ? ("rgba64le", 8) : ("rgba", 4);

        // One pass, explicit raw format, no rotation; FFmpeg never exposes the separate APNG poster as a frame
        var animated = expected["animation"] is not null && (string)fixture["format"]! is "png" or "gif";
        var input = CorpusDriver.CorpusDir / (string)((Obj)fixture["input"]!)["path"]!;
        List<byte[]> decoded;
        try
        {
            (decoded, _) = CommonCorpus.FfmpegDecode(tools, input, pixelFormat, width, height, bytesPerPixel, animated);
        }
        catch (ToolException error)
        {
            return ["FFmpeg failed: " + error.Message.Split('\n')[0]];
        }

        if (decoded.Count != frames.Count)
            return [$"FFmpeg decodes {decoded.Count} frames, {frames.Count} expected."];

        var errors = new List<string>();
        for (var i = 0; i < frames.Count; i++)
        {
            var reference = File.ReadAllBytes(CorpusDriver.CorpusDir / (string)Buffers(frames[i]).Single(b => (string)b["layout"]! == layout)["path"]!);
            var sample = layout == "rgba16le" ? 2 : 1;
            if (!reference.AsSpan().SequenceEqual(decoded[i]) && !(hiddenColorsMayDiffer && CommonCorpus.DiffersOnlyInHiddenColors(reference, decoded[i], sample)))
            {
                var (max, mean, _) = CommonCorpus.Compare(reference, decoded[i], sample);
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"frame {i}: {tools.FfmpegLabel} decodes different samples (max {max}, mean {mean:0.####})."));
            }
        }

        return errors;
    }

    private static List<string> VerifyWebP(VerifyTools tools, FullPath scratch, Obj fixture)
    {
        var expected = (Obj)fixture["expected"]!;
        var frames = ((List<object?>)expected["frames"]!).Cast<Obj>().ToList();
        var comparison = (Obj)fixture["comparison"]!;
        var exact = (string)comparison["mode"]! == "exact";
        var maxError = exact ? 0 : Convert.ToInt32(comparison["maxAbsoluteError"], CultureInfo.InvariantCulture);
        var maxMean = exact ? 0 : Convert.ToDouble(comparison["maxMeanAbsoluteError"], CultureInfo.InvariantCulture);
        var input = CorpusDriver.CorpusDir / (string)((Obj)fixture["input"]!)["path"]!;
        var name = ((string)fixture["id"]!).Replace('/', '_');
        var errors = new List<string>();
        if (expected["animation"] is not null)
        {
            // WebPAnimDecoder blends with integer truncation (one unit on translucent results) and may keep other hidden
            // colors; lossy rectangles also differ by the conversion rounding: visible pixels within one unit, alpha exact
            var folder = scratch / name;
            Directory.CreateDirectory(folder);
            Proc.Run([tools.AnimDump, "-folder", folder.Value, "-prefix", "frame_", "-pam", input.Value]);
            var dumped = Directory.EnumerateFiles(folder, "frame_*.pam").Order(StringComparer.Ordinal).Select(p => WebPCorpus.ReadPam(File.ReadAllBytes(p)).Raw("rgba8")).ToList();
            if (dumped.Count != frames.Count)
                return [$"anim_dump decodes {dumped.Count} frames, {frames.Count} expected."];

            for (var i = 0; i < frames.Count; i++)
            {
                if (VisibleDifference(Rgba8Reference(frames[i]), dumped[i], exact ? 1 : maxError + 1) is { } difference)
                    errors.Add($"frame {i}: {tools.AnimDumpLabel} {difference}");
            }

            return errors;
        }

        var output = scratch / (name + ".pam");
        Proc.Run([tools.Dwebp, "-quiet", "-pam", input.Value, "-o", output.Value]);
        var actual = WebPCorpus.ReadPam(File.ReadAllBytes(output)).Raw("rgba8");
        if ((string)expected["pixelFormat"]! == "Rgb24")
        {
            // The reference of an opaque image is opaque; dwebp reports the stored alpha of a lossless image without the alpha hint
            for (var i = 3; i < actual.Length; i += 4)
                actual[i] = 255;
        }

        var reference = Rgba8Reference(frames[0]);
        if (reference.Length != actual.Length)
            return [$"{tools.DwebpLabel} decodes {actual.Length / 4} pixels, {reference.Length / 4} expected."];

        var (max, mean, alpha) = CommonCorpus.Compare(reference, actual, 1);
        if (alpha != 0 || max > maxError || mean > maxMean)
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{tools.DwebpLabel} decodes different samples (max {max}, mean {mean:0.####}, alpha max {alpha}; policy max {maxError}, mean {maxMean})."));
        return errors;
    }

    /// <summary>Alpha must match exactly and visible color samples within <paramref name="maxError"/>; hidden colors are ignored.</summary>
    private static string? VisibleDifference(byte[] expected, byte[] actual, int maxError)
    {
        if (expected.Length != actual.Length)
            return $"decodes {actual.Length / 4} pixels, {expected.Length / 4} expected.";
        for (var i = 0; i < expected.Length; i += 4)
        {
            if (expected[i + 3] != actual[i + 3])
                return $"decodes alpha {actual[i + 3]} at pixel {i / 4}, expected {expected[i + 3]}.";
            if (expected[i + 3] == 0)
                continue; // hidden colors have no visible effect
            for (var c = 0; c < 3; c++)
            {
                if (Math.Abs(expected[i + c] - actual[i + c]) > maxError)
                    return $"decodes {actual[i + c]} at pixel {i / 4} channel {c}, expected {expected[i + c]} (max error {maxError}).";
            }
        }

        return null;
    }

    private static IEnumerable<Obj> Buffers(Obj frame) => ((List<object?>)frame["buffers"]!).Cast<Obj>();

    private static byte[] Rgba8Reference(Obj frame) => File.ReadAllBytes(CorpusDriver.CorpusDir / (string)Buffers(frame).Single(b => (string)b["layout"]! == "rgba8")["path"]!);

    /// <summary>
    /// The tools of the pinned release, whatever their versions compared with the manifest (the point is to measure the
    /// impact of a version change, such as a Renovate update of the release).
    /// </summary>
    private sealed class VerifyTools : ToolSet
    {
        public VerifyTools()
            : base(Download(PrebuiltTools.Ffmpeg))
        {
            Dwebp = Download(PrebuiltTools.Dwebp);
            AnimDump = Download(PrebuiltTools.WebpAnimDump);
            DwebpLabel = "dwebp " + Proc.RunText([Dwebp, "-version"]).Trim();
            AnimDumpLabel = "anim_dump " + Proc.RunText([AnimDump, "-version"]).Trim();
            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [Dwebp] = "dwebp" };
        }

        public string Dwebp { get; }

        public string AnimDump { get; }

        public string DwebpLabel { get; }

        public string AnimDumpLabel { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
