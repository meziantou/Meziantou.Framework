using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Maven and Gradle files for Java package dependencies.</summary>
public sealed partial class JavaDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.JavaPackage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("pom.xml", ignoreCase: false)
            || context.HasFileName("build.gradle", ignoreCase: false)
            || context.HasFileName("build.gradle.kts", ignoreCase: false)
            || context.HasFileName("libs.versions.toml", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        string? mavenGroup = null;
        string? mavenArtifact = null;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            var isMaven = Path.GetFileName(context.FullPath).Equals("pom.xml", StringComparison.Ordinal);
            if (isMaven)
            {
                if (MavenGroupRegex().Match(line) is { Success: true } groupMatch)
                {
                    mavenGroup = groupMatch.Groups["value"].Value;
                    continue;
                }

                if (MavenArtifactRegex().Match(line) is { Success: true } artifactMatch)
                {
                    mavenArtifact = artifactMatch.Groups["value"].Value;
                    continue;
                }

                if (mavenGroup is null || mavenArtifact is null)
                    continue;

                var mavenVersionMatch = MavenVersionRegex().Match(line);
                if (!mavenVersionMatch.Success)
                    continue;

                var mavenVersion = mavenVersionMatch.Groups["value"];
                context.ReportDependency(this, $"{mavenGroup}:{mavenArtifact}", mavenVersion.Value, DependencyType.JavaPackage,
                    new NonUpdatableLocation(context),
                    new TextLocation(context.FileSystem, context.FullPath, lineNumber, mavenVersion.Index + 1, mavenVersion.Length));
                mavenGroup = null;
                mavenArtifact = null;
                continue;
            }

            var match = GradleDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["name"];
            var version = match.Groups["version"];
            if (!name.Success)
            {
                name = match.Groups["name2"];
                version = match.Groups["version2"];
            }
            context.ReportDependency(this, name.Value, version.Value, DependencyType.JavaPackage,
                new NonUpdatableLocation(context),
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length));
        }
    }

    [GeneratedRegex("""^\s*<groupId>(?<value>[^<]+)</groupId>\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MavenGroupRegex();

    [GeneratedRegex("""^\s*<artifactId>(?<value>[^<]+)</artifactId>\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MavenArtifactRegex();

    [GeneratedRegex("""^\s*<version>(?<value>[^<]+)</version>\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MavenVersionRegex();
    [GeneratedRegex(@"(?:(?:implementation|api|compileOnly|runtimeOnly|testImplementation|testRuntimeOnly)\s+)?[""'](?<name>[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+):(?<version>[^""']+)[""']|(?<name2>[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+)\s*=\s*""(?<version2>[^""]+)""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GradleDependencyRegex();
}
