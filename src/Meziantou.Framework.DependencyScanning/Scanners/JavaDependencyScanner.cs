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
        var versions = new Dictionary<string, (string Version, Location Location)>(StringComparer.Ordinal);
        var versionReferences = new List<(string Name, string VersionReference)>();

        // A table can be written with a header, with dotted keys, or as an inline table, so the pairs are matched on
        // the full key they define rather than on the header they are under.
        foreach (var pair in tree.GetRoot().GetKeyValues())
        {
            if (pair.Names.Count < 2 || pair.Table is { IsArrayOfTables: true } || TomlUtilities.IsInInlineTable(pair))
                continue;

            var key = string.Join('.', pair.Names.Skip(1));
            if (pair.Names[0] is "versions")
            {
                if (TomlUtilities.GetString(pair.Value) is { } version)
                {
                    versions[key] = (version.Value, TomlUtilities.CreateLocation(context, tree, version));
                }
            }
            else if (pair.Names[0] is "libraries")
            {
                if (TomlUtilities.GetString(pair.Value) is { } notation)
                {
                    // guava = "com.google.guava:guava:33.0.0"
                    var separator = notation.Value.LastIndexOf(':', StringComparison.Ordinal);
                    if (separator <= 0 || notation.Value.AsSpan(0, separator).IndexOf(':') < 0)
                        continue;

                    // The version is found in the value, which is at the same offsets in the text only when the string has
                    // no escape sequence.
                    Location versionLocation = new NonUpdatableLocation(context);
                    if (TomlUtilities.IsVerbatim(notation) && TomlUtilities.CreateLocation(context, tree, notation) is TextLocation notationLocation)
                    {
                        versionLocation = new TextLocation(context.FileSystem, context.FullPath, notationLocation.LineNumber, notationLocation.LinePosition + separator + 1, notation.Value.Length - separator - 1);
                    }

                    context.ReportDependency(this, notation.Value[..separator], notation.Value[(separator + 1)..], DependencyType.JavaPackage, new NonUpdatableLocation(context), versionLocation);
                    continue;
                }

                // guava = { module = "com.google.guava:guava", version = "33.0.0" }
                // guava = { group = "com.google.guava", name = "guava", version.ref = "guava" }
                string name;
                if (TomlUtilities.GetInlineTableString(pair.Value, "module") is { } module)
                {
                    name = module.Value;
                }
                else if (TomlUtilities.GetInlineTableString(pair.Value, "group") is { } group && TomlUtilities.GetInlineTableString(pair.Value, "name") is { } artifact)
                {
                    name = group.Value + ":" + artifact.Value;
                }
                else
                {
                    continue;
                }

                if (TomlUtilities.GetInlineTableString(pair.Value, "version") is { } libraryVersion)
                {
                    context.ReportDependency(this, name, libraryVersion.Value, DependencyType.JavaPackage,
                        new NonUpdatableLocation(context), TomlUtilities.CreateLocation(context, tree, libraryVersion));
                }
                else if (TomlUtilities.GetInlineTableString(pair.Value, "version.ref") is { } versionReference)
                {
                    versionReferences.Add((name, versionReference.Value));
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
