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
                if (JsonNodeDocument.TryGetObject(root, dependencySectionPropertyName, out var deps))
                {
                    await ScanDependenciesAsync(context, deps).ConfigureAwait(false);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private ValueTask ScanDependenciesAsync(ScanFileContext context, JsonObject deps)
    {
        foreach (var dep in deps)
        {
            if (dep.Value is null)
                continue;

            var packageName = dep.Key;
            string? valuePath = null;
            string? version = null;
            if (JsonNodeDocument.TryGetString(dep.Value, out var stringVersion))
            {
                version = stringVersion;
                valuePath = dep.Value.GetPath();
            }
            else if (dep.Value is JsonObject dependencyObject)
            {
                if (JsonNodeDocument.TryGetProperty(dependencyObject, "version", out var versionNode) && versionNode is not null && JsonNodeDocument.TryGetString(versionNode, out var objectVersion))
                {
                    version = objectVersion;
                    valuePath = versionNode.GetPath();
                }
            }
            else
            {
                continue;
            }

            if (version is null)
                continue;

            if (valuePath is not null)
            {
                context.ReportDependency(this, packageName, version, DependencyType.Npm,
                    nameLocation: new NonUpdatableLocation(context),
                    versionLocation: new JsonLocation(context, valuePath));
            }
        }

        return ValueTask.CompletedTask;
    }
}
