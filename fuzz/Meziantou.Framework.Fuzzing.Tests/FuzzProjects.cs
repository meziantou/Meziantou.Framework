using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Fuzzing.Tests;

/// <summary>The SharpFuzz projects (fuzz/*.Fuzz) and their targets.</summary>
internal static class FuzzProjects
{
    public static IReadOnlyList<(string Project, IReadOnlyList<FuzzTarget> Targets)> All { get; } =
    [
        ("Meziantou.Framework.DependencyScanning.Fuzz", DependencyScanning.Fuzz.DependencyScanningFuzzTargets.All),
        ("Meziantou.Framework.Globbing.Fuzz", Globbing.Fuzz.GlobbingFuzzTargets.All),
        ("Meziantou.Framework.HtmlSanitizer.Fuzz", Sanitizers.Fuzz.HtmlSanitizerFuzzTargets.All),
        ("Meziantou.Framework.JsonPath.Fuzz", JsonPath.Fuzz.JsonPathFuzzTargets.All),
        ("Meziantou.Framework.Language.Fuzz", Language.Fuzz.LanguageFuzzTargets.All),
        ("Meziantou.Framework.SyntaxHighlighting.Fuzz", SyntaxHighlighting.Fuzz.SyntaxHighlightingFuzzTargets.All),
        ("Meziantou.Framework.TdsServer.Fuzz", TdsServer.Fuzz.TdsServerFuzzTargets.All),
        ("Meziantou.Framework.Toml.Fuzz", Toml.Fuzz.TomlFuzzTargets.All),
        ("Meziantou.Framework.Unicode.Fuzz", UnicodeFuzz.UnicodeFuzzTargets.All),
        ("Meziantou.Framework.Yaml.Fuzz", Yaml.Fuzz.YamlFuzzTargets.All),
    ];

    public static (string Project, FuzzTarget Target) Get(string targetName)
        => All.SelectMany(project => project.Targets.Select(target => (project.Project, Target: target))).Single(item => item.Target.Name == targetName);
}
