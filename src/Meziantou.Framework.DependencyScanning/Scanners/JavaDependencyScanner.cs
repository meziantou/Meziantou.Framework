using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

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
        var fileName = Path.GetFileName(context.FullPath);
        if (fileName.Equals("libs.versions.toml", StringComparison.Ordinal))
        {
            await ScanVersionCatalogAsync(context).ConfigureAwait(false);
            return;
        }

        var isMaven = fileName.Equals("pom.xml", StringComparison.Ordinal);
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        string? mavenElement = null;
        var inMavenExclusions = false;
        string? mavenGroup = null;
        string? mavenArtifact = null;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            if (isMaven)
            {
                // Only the coordinates of dependencies, plugins and the parent are dependencies, not the ones of the project itself
                if (MavenElementRegex().Match(line) is { Success: true } elementMatch)
                {
                    var isClosing = elementMatch.Groups["close"].Success;
                    var element = elementMatch.Groups["name"].Value;
                    if (element is "exclusions")
                    {
                        inMavenExclusions = !isClosing;
                    }
                    else
                    {
                        mavenElement = isClosing ? null : element;

                        // Maven uses this group when a plugin does not specify one
                        mavenGroup = !isClosing && element is "plugin" ? "org.apache.maven.plugins" : null;
                        mavenArtifact = null;
                    }

                    continue;
                }

                if (mavenElement is null || inMavenExclusions)
                    continue;

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

                // A property reference, such as ${junit.version}, must be updated where the property is defined
                var mavenVersion = mavenVersionMatch.Groups["value"];
                Location mavenVersionLocation = mavenVersion.Value.Contains("${", StringComparison.Ordinal)
                    ? new NonUpdatableLocation(context)
                    : new TextLocation(context.FileSystem, context.FullPath, lineNumber, mavenVersion.Index + 1, mavenVersion.Length);
                context.ReportDependency(this, $"{mavenGroup}:{mavenArtifact}", mavenVersion.Value, DependencyType.JavaPackage,
                    new NonUpdatableLocation(context), mavenVersionLocation);
                mavenGroup = null;
                mavenArtifact = null;
                continue;
            }

            var match = GradleDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            var version = match.Groups["version"];
            context.ReportDependency(this, match.Groups["name"].Value, version.Value, DependencyType.JavaPackage,
                new NonUpdatableLocation(context),
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length));
        }
    }

    private async ValueTask ScanVersionCatalogAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        var tree = TomlSyntaxTree.ParseText(text, context.FullPath);
        var versions = new Dictionary<string, (string Version, TextLocation Location)>(StringComparer.Ordinal);
        var versionReferences = new List<(string Name, string VersionReference)>();
        string? section = null;
        foreach (var entry in tree.GetRoot().Entries)
        {
            if (entry is TomlTableSyntax table)
            {
                section = table.Name;
                continue;
            }

            if (entry is not TomlPropertySyntax property || !property.ValueNode.IsToken)
                continue;

            var value = property.Value;
            var valueStart = property.ValueNode.AsToken().Span.Start;
            if (section is "versions")
            {
                if (TomlUtilities.TryGetString(value, out var version, out var offset))
                {
                    versions[property.Key] = (version, TomlUtilities.CreateLocation(context, tree, new TextSpan(valueStart + offset, version.Length)));
                }
            }
            else if (section is "libraries")
            {
                if (TomlUtilities.TryGetString(value, out var notation, out var offset))
                {
                    // guava = "com.google.guava:guava:33.0.0"
                    var separator = notation.LastIndexOf(':', StringComparison.Ordinal);
                    if (separator <= 0 || notation.AsSpan(0, separator).IndexOf(':') < 0)
                        continue;

                    context.ReportDependency(this, notation[..separator], notation[(separator + 1)..], DependencyType.JavaPackage,
                        new NonUpdatableLocation(context),
                        TomlUtilities.CreateLocation(context, tree, new TextSpan(valueStart + offset + separator + 1, notation.Length - separator - 1)));
                    continue;
                }

                // guava = { module = "com.google.guava:guava", version = "33.0.0" }
                // guava = { group = "com.google.guava", name = "guava", version.ref = "guava" }
                string name;
                if (TomlUtilities.TryGetInlineTableString(value, "module", out var module, out _))
                {
                    name = module;
                }
                else if (TomlUtilities.TryGetInlineTableString(value, "group", out var group, out _) && TomlUtilities.TryGetInlineTableString(value, "name", out var artifact, out _))
                {
                    name = group + ":" + artifact;
                }
                else
                {
                    continue;
                }

                if (TomlUtilities.TryGetInlineTableString(value, "version", out var version, out offset))
                {
                    context.ReportDependency(this, name, version, DependencyType.JavaPackage,
                        new NonUpdatableLocation(context),
                        TomlUtilities.CreateLocation(context, tree, new TextSpan(valueStart + offset, version.Length)));
                }
                else if (TomlUtilities.TryGetInlineTableString(value, "version.ref", out var versionReference, out _))
                {
                    versionReferences.Add((name, versionReference));
                }
                else
                {
                    context.ReportDependency(this, name, version: null, DependencyType.JavaPackage, new NonUpdatableLocation(context), new NonUpdatableLocation(context));
                }
            }
        }

        foreach (var (name, versionReference) in versionReferences)
        {
            if (!versions.TryGetValue(versionReference, out var version))
            {
                context.ReportDependency(this, name, version: null, DependencyType.JavaPackage, new NonUpdatableLocation(context), new NonUpdatableLocation(context));
                continue;
            }

            // A version shared by several libraries cannot be updated for one of them only
            var isShared = versionReferences.Count(item => item.VersionReference == versionReference) > 1;
            context.ReportDependency(this, name, version.Version, DependencyType.JavaPackage,
                new NonUpdatableLocation(context),
                isShared ? new NonUpdatableLocation(context) : version.Location);
        }
    }

    [GeneratedRegex("""^\s*<groupId>(?<value>[^<]+)</groupId>\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MavenGroupRegex();

    [GeneratedRegex("""^\s*<artifactId>(?<value>[^<]+)</artifactId>\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MavenArtifactRegex();

    [GeneratedRegex("""^\s*<version>(?<value>[^<]+)</version>\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MavenVersionRegex();

    [GeneratedRegex("""^\s*<(?<close>/)?(?<name>dependency|plugin|parent|exclusions)>\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MavenElementRegex();

    // group:artifact:version[:classifier][@extension]. Interpolated versions, such as $fooVersion, are not matched.
    [GeneratedRegex("""["'](?<name>[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+):(?<version>[^"'$:@]+)(?::[^"'$:@]+)?(?:@[^"'$]+)?["']""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GradleDependencyRegex();
}
