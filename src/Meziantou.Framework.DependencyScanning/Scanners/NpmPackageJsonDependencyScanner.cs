using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans npm package.json files for JavaScript package dependencies.</summary>
public sealed class NpmPackageJsonDependencyScanner : DependencyScanner
{
    private static readonly string[] DependencySectionPaths =
    [
        "$.dependencies",
        "$.devDependencies",
        "$.peerDependencies",
        "$.optionaldependencies",
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
            foreach (var dependencySectionPath in DependencySectionPaths)
            {
                foreach (var depsMatch in doc.Select(dependencySectionPath))
                {
                    if (depsMatch.Node is JsonObject deps)
                    {
                        await ScanDependenciesAsync(context, doc, deps, depsMatch.Path).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private ValueTask ScanDependenciesAsync(ScanFileContext context, JsonNodeDocument doc, JsonObject deps, string depsPath)
    {
        foreach (var dep in deps)
        {
            if (dep.Value is null)
                continue;

            var packageName = dep.Key;
            var valuePath = JsonNodeDocument.AppendPropertyPath(depsPath, dep.Key);
            string? version = null;
            if (dep.Value is JsonValue dependencyValue && dependencyValue.TryGetValue<string>(out var stringVersion))
            {
                version = stringVersion;
            }
            else if (dep.Value is JsonObject dependencyObject)
            {
                if (dependencyObject.TryGetPropertyValue("version", out var versionNode) && versionNode is JsonValue versionValue)
                {
                    if (versionValue.TryGetValue<string>(out var objectVersion))
                    {
                        version = objectVersion;
                    }
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
