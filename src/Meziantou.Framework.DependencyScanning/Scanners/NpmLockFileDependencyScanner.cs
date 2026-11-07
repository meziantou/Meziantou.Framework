using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans npm lockfiles (<c>package-lock.json</c> and <c>npm-shrinkwrap.json</c>) for the packages installed in <c>node_modules</c>.</summary>
/// <remarks>
/// The versions are not updatable, as the integrity of each package would no longer match. A package installed under another name is
/// reported as the installed package, with the install folder name in <see cref="Dependency.Metadata"/> under the <c>alias</c> key.
/// </remarks>
public sealed class NpmLockFileDependencyScanner : DependencyScanner
{
    private const string NodeModulesSegment = "node_modules/";

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Npm];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("package-lock.json", ignoreCase: false)
            || context.HasFileName("npm-shrinkwrap.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            // The keys of packages are install paths, such as node_modules/a/node_modules/@scope/b. The lockfile v1 dependencies
            // tree is not scanned: npm 7 and later write packages.
            if (doc.GetRootObject() is not JsonObject root || !JsonNodeDocument.TryGetObject(root, "packages", out var packages))
                return;

            foreach (var (key, value) in packages)
            {
                // The root project ("") and the workspace folders are not installed from a registry
                var nodeModulesIndex = key.LastIndexOf(NodeModulesSegment, StringComparison.Ordinal);
                if (nodeModulesIndex < 0 || (nodeModulesIndex > 0 && key[nodeModulesIndex - 1] is not '/') || value is not JsonObject entry)
                    continue;

                // A link points to a workspace folder, which has its own entry
                if (entry["link"] is JsonValue link && link.TryGetValue<bool>(out var isLink) && isLink)
                    continue;

                if (!JsonNodeDocument.TryGetString(entry["version"], out var version))
                    continue;

                // The name is only written when it differs from the install folder, which is an alias
                var folderName = key[(nodeModulesIndex + NodeModulesSegment.Length)..];
                var hasName = JsonNodeDocument.TryGetString(entry["name"], out var name) && !string.Equals(name, folderName, StringComparison.Ordinal);

                // The version is not updatable as the integrity of the package would not match anymore
                context.ReportDependency(this, hasName ? name : folderName, version, DependencyType.Npm,
                    nameLocation: new NonUpdatableLocation(context),
                    versionLocation: new NonUpdatableLocation(context),
                    tags: [],
                    metadata: hasName ? [KeyValuePair.Create<string, object?>("alias", folderName)] : []);
            }
        }
        catch (JsonException)
        {
        }
    }
}
