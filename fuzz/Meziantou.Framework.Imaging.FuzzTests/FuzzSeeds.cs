using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>Seed groups of the fuzz tests: every corpus input (valid, invalid, unsupported and limit fixtures) of a codec variant.</summary>
internal static class FuzzSeeds
{
    public static IReadOnlyList<string> Groups { get; } = ["png", "apng", "gif", "jpeg-baseline", "jpeg-progressive", "webp-lossless", "webp-lossy", "webp-animated", "qoi", "bmp", "tga", "pnm"];

    public static IReadOnlyList<FuzzSeed> Get(string group)
    {
        var seeds = GoldenCorpus.Default.Fixtures
            .Where(fixture => GetGroup(fixture.Id, fixture.Entry.Format) == group)
            .Select(fixture => new FuzzSeed(fixture.Id, fixture.ReadInput()))
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

    private static string GetGroup(string id, string format) => format switch
    {
        "png" => id.Contains("apng", StringComparison.Ordinal) ? "apng" : "png",
        "gif" => "gif",
        "jpeg" => id.Contains("progressive", StringComparison.Ordinal) ? "jpeg-progressive" : "jpeg-baseline",
        "webp" => id.Contains("anim", StringComparison.Ordinal) ? "webp-animated" : id.Contains("lossy", StringComparison.Ordinal) ? "webp-lossy" : "webp-lossless",
        _ => format,
    };
}
