using System.Security.Cryptography;
using System.Text.Json;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Replays the committed fuzz regressions (<c>fuzz/Meziantou.Framework.Imaging.FuzzTests/FuzzRegressions/regressions.json</c>): the manifest is validated (hashes,
/// licenses, provenance, no orphan or missing file), every image passes the whole <see cref="FuzzOracle"/> under every reader
/// variant, and identification and loading end with the recorded outcome. A regression of <c>"kind": "font"</c> is a font
/// file, checked by the oracle of <see cref="FontFuzzTests"/>; its <c>expectedLoad</c> is the outcome of loading it.
/// </summary>
public sealed class FuzzRegressionTests
{
    private static readonly string[] AllowedLicenses = ["CC0-1.0", "MIT", "Unlicense", "CC-BY-4.0", "Libpng", "Zlib"];
    private static readonly string[] AllowedOrigins = ["fuzz-minimized", "hand-authored"];
    private static readonly string[] AllowedKinds = ["image", "font"];

    public static TheoryData<string> Ids => [.. Load().Where(regression => regression.Kind == "image").Select(regression => regression.Id)];

    public static TheoryData<string> FontIds => [.. Load().Where(regression => regression.Kind == "font").Select(regression => regression.Id)];

    [Fact]
    public void ManifestIsValid()
    {
        var regressions = Load();
        Assert.NotEmpty(regressions);
        Assert.HasCount(regressions.Count, regressions.Select(regression => regression.Id).Distinct(StringComparer.Ordinal));
        foreach (var regression in regressions)
        {
            Assert.Contains(regression.License, AllowedLicenses);
            Assert.Contains(regression.Origin, AllowedOrigins);
            Assert.Contains(regression.Kind, AllowedKinds);
            Assert.True(regression.Kind == "font" || regression.ExpectedIdentify is not null, $"{regression.Id}: missing expectedIdentify");
            Assert.False(string.IsNullOrWhiteSpace(regression.FoundBy), regression.Id);
            Assert.False(string.IsNullOrWhiteSpace(regression.Defect), regression.Id);
            Assert.DoesNotContain("..", regression.File, StringComparison.Ordinal);
            var path = Root / regression.File;
            Assert.True(File.Exists(path), $"{regression.Id}: missing file {regression.File}");
            Assert.Equal(regression.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }

        var listed = regressions.Select(regression => regression.File).Append("regressions.json").ToHashSet(StringComparer.Ordinal);
        var orphans = Directory.EnumerateFiles(Root).Select(Path.GetFileName).Where(name => !listed.Contains(name!)).ToList();
        Assert.Empty(orphans);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void RegressionInputsSatisfyTheFuzzProperties(string id)
    {
        var regression = Load().Single(item => item.Id == id);
        var data = File.ReadAllBytes(Root / regression.File);
        // Every combination of the reader choices, with whole frames and with every refinement of a frame
        foreach (var refinements in new[] { 0, 128 })
        {
            for (var variant = refinements; variant < refinements + 32; variant++)
            {
                var failure = MutationFuzzTests.RunWithWatchdog(data, variant, out _);
                Assert.Null(failure, $"{id} (reader variant {variant}): {failure?.Message}");
            }
        }

        Assert.Equal(regression.ExpectedIdentify, Outcome(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan })));
        Assert.Equal(regression.ExpectedLoad, Outcome(() => Image.Load(data)));
    }

    [Theory]
    [MemberData(nameof(FontIds))]
    public void FontRegressionInputsSatisfyTheFuzzProperties(string id)
    {
        var regression = Load().Single(item => item.Id == id);
        var failure = FontFuzzTests.RunWithWatchdog(File.ReadAllBytes(Root / regression.File), out var result);
        Assert.Null(failure, $"{id}: {failure?.Message}");
        Assert.Equal(regression.ExpectedLoad, result.Outcome == "loaded" ? "success" : result.Outcome);
    }

    private static FullPath Root => FullPath.FromPath(AppContext.BaseDirectory) / "FuzzRegressions";

    private static string Outcome(Func<object> action)
    {
        try
        {
            var result = action();
            (result as IDisposable)?.Dispose();
            return "success";
        }
        catch (ImageException exception)
        {
            return exception.GetType().Name;
        }
    }

    private static List<FuzzRegression> Load()
    {
        using var stream = File.OpenRead(Root / "regressions.json");
        using var document = JsonDocument.Parse(stream);
        return [.. document.RootElement.GetProperty("regressions").EnumerateArray().Select(item => new FuzzRegression(
            item.GetProperty("id").GetString()!,
            item.GetProperty("file").GetString()!,
            item.GetProperty("sha256").GetString()!,
            item.GetProperty("origin").GetString()!,
            item.GetProperty("license").GetString()!,
            item.TryGetProperty("kind", out var kind) ? kind.GetString()! : "image",
            item.GetProperty("foundBy").GetString()!,
            item.GetProperty("defect").GetString()!,
            item.TryGetProperty("expectedIdentify", out var expectedIdentify) ? expectedIdentify.GetString() : null,
            item.GetProperty("expectedLoad").GetString()!))];
    }

    private sealed record FuzzRegression(string Id, string File, string Sha256, string Origin, string License, string Kind, string FoundBy, string Defect, string? ExpectedIdentify, string ExpectedLoad);
}
