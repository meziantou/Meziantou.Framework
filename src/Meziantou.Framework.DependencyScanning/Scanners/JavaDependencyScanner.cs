namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Maven and Gradle files for Java package dependencies.</summary>
/// <remarks>
/// Gradle plugins, declared in a <c>plugins</c> block or in the <c>[plugins]</c> table of a version catalog, are reported with
/// the coordinates of their plugin marker artifact, <c>&lt;id&gt;:&lt;id&gt;.gradle.plugin</c>, which is the artifact Gradle resolves
/// the plugin from. The plugin id is available in <see cref="Dependency.Metadata"/> under the <c>pluginId</c> key.
/// </remarks>
public sealed partial class JavaDependencyScanner : DependencyScanner
{
    private const string PluginIdMetadataKey = "pluginId";

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
        }
        else if (fileName.Equals("pom.xml", StringComparison.Ordinal))
        {
            await ScanMavenAsync(context).ConfigureAwait(false);
        }
        else
        {
            await ScanGradleAsync(context, isKotlin: fileName.EndsWith(".kts", StringComparison.Ordinal)).ConfigureAwait(false);
        }
    }

    /// <summary>Gets the coordinates of the marker artifact Gradle resolves a plugin from.</summary>
    private static string GetGradlePluginMarkerName(string pluginId) => pluginId + ":" + pluginId + ".gradle.plugin";
}
