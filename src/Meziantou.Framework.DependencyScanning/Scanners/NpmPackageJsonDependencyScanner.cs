using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans npm package.json files for JavaScript package dependencies.</summary>
public sealed class NpmPackageJsonDependencyScanner : DependencyScanner
{
    private static readonly string[] DependencySectionPropertyNames =
    [
        "dependencies",
        "devDependencies",
        "peerDependencies",
        "optionaldependencies",
    ];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Npm];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("package.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            foreach (var dependencySectionPropertyName in DependencySectionPropertyNames)
            {
                if (doc.TryGetObject(root, dependencySectionPropertyName, "$", out var deps, out var depsPath))
                {
                    await ScanDependenciesAsync(context, doc, deps, depsPath).ConfigureAwait(false);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private ValueTask ScanDependenciesAsync(ScanFileContext context, JsonNodeDocument doc, JsonObject deps, string depsPath)
    {
        foreach (var dep in doc.GetProperties(deps, depsPath))
        {
            if (dep.Value is null)
                continue;

            var packageName = dep.Name;
            var valuePath = dep.Path;
            string? version = null;
            if (JsonNodeDocument.TryGetString(dep.Value, out var stringVersion))
            {
                version = stringVersion;
            }
            else if (dep.Value is JsonObject dependencyObject)
            {
                if (doc.TryGetProperty(dependencyObject, "version", dep.Path, out var versionNode, out var versionPath) && JsonNodeDocument.TryGetString(versionNode, out var objectVersion))
                {
                    version = objectVersion;
                    valuePath = versionPath;
                }
            }
            else
            {
                continue;
            }

            if (version is null)
                continue;

            if (dep.Value is not null)
            {
                context.ReportDependency(this, packageName, version, DependencyType.Npm,
                    nameLocation: new NonUpdatableLocation(context),
                    versionLocation: new JsonLocation(context, valuePath, doc.GetLineInfo(valuePath)));
            }
        }

        return ValueTask.CompletedTask;
    }
}
