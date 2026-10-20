using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Fuzzing.Tests;

/// <summary>The SharpFuzz projects (fuzz/*.Fuzz) and their targets.</summary>
internal static class FuzzProjects
{
    public static IReadOnlyList<(string Project, IReadOnlyList<FuzzTarget> Targets)> All { get; } =
    [
        ("Meziantou.Framework.Bencode.Fuzz", Bencode.Fuzz.BencodeFuzzTargets.All),
        ("Meziantou.Framework.CodeOwners.Fuzz", CodeOwners.Fuzz.CodeOwnersFuzzTargets.All),
        ("Meziantou.Framework.Csv.Fuzz", Csv.Fuzz.CsvFuzzTargets.All),
        ("Meziantou.Framework.DependencyScanning.Fuzz", DependencyScanning.Fuzz.DependencyScanningFuzzTargets.All),
        ("Meziantou.Framework.Dns.Fuzz", DnsFuzz.DnsFuzzTargets.All),
        ("Meziantou.Framework.Globbing.Fuzz", Globbing.Fuzz.GlobbingFuzzTargets.All),
        ("Meziantou.Framework.HtmlSanitizer.Fuzz", Sanitizers.Fuzz.HtmlSanitizerFuzzTargets.All),
        ("Meziantou.Framework.Http.Caching.Fuzz", Http.Caching.Fuzz.HttpCachingFuzzTargets.All),
        ("Meziantou.Framework.Http.Htpasswd.Fuzz", Http.Htpasswd.Fuzz.HtpasswdFuzzTargets.All),
        ("Meziantou.Framework.JsonPath.Fuzz", JsonPath.Fuzz.JsonPathFuzzTargets.All),
        ("Meziantou.Framework.Language.Fuzz", Language.Fuzz.LanguageFuzzTargets.All),
        ("Meziantou.Framework.Markdown.Fuzz", Markdown.Fuzz.MarkdownFuzzTargets.All),
        ("Meziantou.Framework.MediaTags.Fuzz", MediaTags.Fuzz.MediaTagsFuzzTargets.All),
        ("Meziantou.Framework.PostgreSqlServer.Fuzz", PostgreSql.Fuzz.PostgreSqlServerFuzzTargets.All),
        ("Meziantou.Framework.RobotsTxt.Fuzz", RobotsTxt.Fuzz.RobotsTxtFuzzTargets.All),
        ("Meziantou.Framework.Scheduling.Fuzz", Scheduling.Fuzz.SchedulingFuzzTargets.All),
        ("Meziantou.Framework.SimpleQueryLanguage.Fuzz", SimpleQueryLanguage.Fuzz.SimpleQueryLanguageFuzzTargets.All),
        ("Meziantou.Framework.SyntaxHighlighting.Fuzz", SyntaxHighlighting.Fuzz.SyntaxHighlightingFuzzTargets.All),
        ("Meziantou.Framework.TdsServer.Fuzz", TdsServer.Fuzz.TdsServerFuzzTargets.All),
        ("Meziantou.Framework.Toml.Fuzz", Toml.Fuzz.TomlFuzzTargets.All),
        ("Meziantou.Framework.Unicode.Fuzz", UnicodeFuzz.UnicodeFuzzTargets.All),
        ("Meziantou.Framework.Versioning.Fuzz", Versioning.Fuzz.VersioningFuzzTargets.All),
        ("Meziantou.Framework.WebPlatform.Fuzz", WebPlatform.Fuzz.WebPlatformFuzzTargets.All),
        ("Meziantou.Framework.Yaml.Fuzz", Yaml.Fuzz.YamlFuzzTargets.All),
    ];

    public static (string Project, FuzzTarget Target) Get(string targetName)
        => All.SelectMany(project => project.Targets.Select(target => (project.Project, Target: target))).Single(item => item.Target.Name == targetName);
}
