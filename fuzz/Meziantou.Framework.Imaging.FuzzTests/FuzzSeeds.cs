using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>Seed groups of the fuzz tests: every corpus input (valid, invalid, unsupported and limit fixtures) of a codec variant.</summary>
internal static class FuzzSeeds
{
    public static IReadOnlyList<string> Groups { get; } = ["png", "apng", "gif", "jpeg-baseline", "jpeg-progressive", "webp-lossless", "webp-lossy", "webp-animated", "qoi", "bmp", "tga", "pnm", "avif-still", "avif-sequence", "av1-intra", "av1-inter", "jxl-lossless", "jxl-lossy", "jxl-animated", "jp2-reversible", "jp2-irreversible", "jp2-file", "jp2-ht"];

    public static IReadOnlyList<FuzzSeed> Get(string group)
    {
        // The AV1 vectors of the corpus are raw streams: each one is wrapped into an AVIF file, a still image or an image
        // sequence, so that it goes through the public API
        var seeds = group is "av1-intra" or "av1-inter"
            ? GoldenCorpus.Default.Av1Vectors
                .Where(vector => (vector.HasFeature("av1.frame_types=inter") ? "av1-inter" : "av1-intra") == group)
                .Select(vector => new FuzzSeed(vector.Id, FuzzAvifContainer.Wrap(File.ReadAllBytes(GoldenCorpus.Default.RootDirectory / vector.Input.Path), vector.Frames[0].Width, vector.Frames[0].Height, vector.BitDepth, vector.Subsampling)))
                .ToList()
            : GoldenCorpus.Default.Fixtures
                .Select(fixture => (Fixture: fixture, Data: fixture.ReadInput()))
                .Where(item => GetGroup(item.Fixture.Id, item.Fixture.Entry.Format, item.Data) == group)
                .Select(item => new FuzzSeed(item.Fixture.Id, item.Data))
                .ToList();
        if (seeds.Count == 0)
            throw new InvalidOperationException($"The fuzz seed group '{group}' has no corpus input.");

        return seeds;
    }

    /// <summary>A stable per-group salt (FNV-1a), so that each group explores its own mutation sequence.</summary>
    public static ulong GetGroupSalt(string group)
    {
        var hash = 0xCBF29CE484222325UL;
        foreach (var c in group)
        {
            hash = (hash ^ c) * 0x100000001B3UL;
        }

        return hash;
    }

    private static string GetGroup(string id, string format, byte[] data) => format switch
    {
        "avif" => FuzzAvifContainer.IsSequence(data) ? "avif-sequence" : "avif-still",
        "png" => id.Contains("apng", StringComparison.Ordinal) ? "apng" : "png",
        "gif" => "gif",
        "jpeg" => id.Contains("progressive", StringComparison.Ordinal) ? "jpeg-progressive" : "jpeg-baseline",
        "webp" => id.Contains("anim", StringComparison.Ordinal) ? "webp-animated" : id.Contains("lossy", StringComparison.Ordinal) ? "webp-lossy" : "webp-lossless",
        "jxl" => id.Contains("anim", StringComparison.Ordinal) ? "jxl-animated" : id.Contains("lossy", StringComparison.Ordinal) || id.Contains("jpeg", StringComparison.Ordinal) ? "jxl-lossy" : "jxl-lossless",
        // JPEG 2000: the high-throughput block coder, the boxes of a file, then the two wavelet transformations
        "jp2" => id.Contains("/ht-", StringComparison.Ordinal) ? "jp2-ht" : id.Contains("/file-", StringComparison.Ordinal) ? "jp2-file" : id.Contains("lossy", StringComparison.Ordinal) ? "jp2-irreversible" : "jp2-reversible",
        _ => format,
    };
}
