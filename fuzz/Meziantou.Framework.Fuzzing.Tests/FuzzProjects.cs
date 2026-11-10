using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Fuzzing.Tests;

/// <summary>The SharpFuzz projects (fuzz/*.Fuzz) and their targets.</summary>
internal static class FuzzProjects
{
    public static IReadOnlyList<(string Project, IReadOnlyList<FuzzTarget> Targets)> All { get; } =
    [
        ("Meziantou.Framework.AtlassianDataFormat.Fuzz", AtlassianDataFormat.Fuzz.AtlassianDataFormatFuzzTargets.All),
        ("Meziantou.Framework.Barcodes.Fuzz", Barcodes.Fuzz.BarcodesFuzzTargets.All),
        ("Meziantou.Framework.Bencode.Fuzz", Bencode.Fuzz.BencodeFuzzTargets.All),
        ("Meziantou.Framework.Charts.Fuzz", Charts.Fuzz.ChartsFuzzTargets.All),
        ("Meziantou.Framework.CodeOwners.Fuzz", CodeOwners.Fuzz.CodeOwnersFuzzTargets.All),
        ("Meziantou.Framework.Csv.Fuzz", Csv.Fuzz.CsvFuzzTargets.All),
        ("Meziantou.Framework.DependencyScanning.Fuzz", DependencyScanning.Fuzz.DependencyScanningFuzzTargets.All),
        ("Meziantou.Framework.Diagrams.Fuzz", Diagrams.Fuzz.DiagramsFuzzTargets.All),
        ("Meziantou.Framework.Dns.Fuzz", DnsFuzz.DnsFuzzTargets.All),
        ("Meziantou.Framework.FastEnumGenerator.Fuzz", FastEnumGenerator.Fuzz.FastEnumGeneratorFuzzTargets.All),
        ("Meziantou.Framework.Fonts.Fuzz", Fonts.Fuzz.FontsFuzzTargets.All),
        ("Meziantou.Framework.Geo.Fuzz", Geo.Fuzz.GeoFuzzTargets.All),
        ("Meziantou.Framework.Geo.GeoPackage.Fuzz", GeoPackageFuzz.GeoPackageFuzzTargets.All),
        ("Meziantou.Framework.Geo.GeoParquet.Fuzz", GeoParquetFuzz.GeoParquetFuzzTargets.All),
        ("Meziantou.Framework.Geo.Maps.Fuzz", Geo.Maps.Fuzz.GeoMapsFuzzTargets.All),
        ("Meziantou.Framework.Globbing.Fuzz", Globbing.Fuzz.GlobbingFuzzTargets.All),
        ("Meziantou.Framework.Hashing.Fuzz", Hashing.Fuzz.HashingFuzzTargets.All),
        ("Meziantou.Framework.HtmlSanitizer.Fuzz", Sanitizers.Fuzz.HtmlSanitizerFuzzTargets.All),
        ("Meziantou.Framework.HtmlToMarkdown.Fuzz", HtmlToMarkdownFuzz.HtmlToMarkdownFuzzTargets.All),
        ("Meziantou.Framework.Http.Caching.Fuzz", Http.Caching.Fuzz.HttpCachingFuzzTargets.All),
        ("Meziantou.Framework.Http.Fuzz", Http.Fuzz.HttpFuzzTargets.All),
        ("Meziantou.Framework.Http.Htpasswd.Fuzz", Http.Htpasswd.Fuzz.HtpasswdFuzzTargets.All),
        ("Meziantou.Framework.JavaScriptEngine.Fuzz", JavaScriptEngine.Fuzz.JavaScriptEngineFuzzTargets.All),
        ("Meziantou.Framework.JsonPath.Fuzz", JsonPath.Fuzz.JsonPathFuzzTargets.All),
        ("Meziantou.Framework.Language.Fuzz", Language.Fuzz.LanguageFuzzTargets.All),
        ("Meziantou.Framework.Markdown.Fuzz", Markdown.Fuzz.MarkdownFuzzTargets.All),
        ("Meziantou.Framework.MediaTags.Fuzz", MediaTags.Fuzz.MediaTagsFuzzTargets.All),
        ("Meziantou.Framework.Ntp.Fuzz", Ntp.Fuzz.NtpFuzzTargets.All),
        ("Meziantou.Framework.PublicApiGenerator.Fuzz", PublicApiGenerator.Fuzz.PublicApiGeneratorFuzzTargets.All),
        ("Meziantou.Framework.RobotsTxt.Fuzz", RobotsTxt.Fuzz.RobotsTxtFuzzTargets.All),
        ("Meziantou.Framework.Scheduling.Fuzz", Scheduling.Fuzz.SchedulingFuzzTargets.All),
        ("Meziantou.Framework.SimpleQueryLanguage.Fuzz", SimpleQueryLanguage.Fuzz.SimpleQueryLanguageFuzzTargets.All),
        ("Meziantou.Framework.SyntaxHighlighting.Fuzz", SyntaxHighlighting.Fuzz.SyntaxHighlightingFuzzTargets.All),
        ("Meziantou.Framework.TdsServer.Fuzz", TdsServer.Fuzz.TdsServerFuzzTargets.All),
        ("Meziantou.Framework.TextDiff.Fuzz", TextDiffFuzz.TextDiffFuzzTargets.All),
        ("Meziantou.Framework.Toml.Fuzz", Toml.Fuzz.TomlFuzzTargets.All),
        ("Meziantou.Framework.Unicode.Fuzz", UnicodeFuzz.UnicodeFuzzTargets.All),
        ("Meziantou.Framework.Uri.Fuzz", UriFuzz.UriFuzzTargets.All),
        ("Meziantou.Framework.VectorGraphics.Fuzz", VectorGraphics.Fuzz.VectorGraphicsFuzzTargets.All),
        ("Meziantou.Framework.Versioning.Fuzz", Versioning.Fuzz.VersioningFuzzTargets.All),
        ("Meziantou.Framework.WebPlatform.Fuzz", WebPlatform.Fuzz.WebPlatformFuzzTargets.All),
        ("Meziantou.Framework.Yaml.Fuzz", Yaml.Fuzz.YamlFuzzTargets.All),
    ];

    public static (string Project, FuzzTarget Target) Get(string targetName)
        => All.SelectMany(project => project.Targets.Select(target => (project.Project, Target: target))).Single(item => item.Target.Name == targetName);
}
