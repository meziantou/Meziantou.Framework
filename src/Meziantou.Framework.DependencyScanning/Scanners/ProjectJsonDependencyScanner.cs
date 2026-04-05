using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans legacy project.json files for NuGet package dependencies.</summary>
public sealed class ProjectJsonDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("project.json", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            ScanDependencies(context, EnumerateDependencyObjects(root));
            if (JsonNodeDocument.TryGetObject(root, "tools", out var tools))
            {
                ScanDependencies(context, [tools]);
            }
        }
        catch (JsonException)
        {
        }
    }

    private void ScanDependencies(ScanFileContext context, IEnumerable<JsonObject> dependencyObjects)
    {
        foreach (var dependencies in dependencyObjects)
        {
            foreach (var dep in dependencies)
            {
                var packageName = dep.Key;
                string? version;
                string? versionPath;
                if (JsonNodeDocument.TryGetString(dep.Value, out var stringVersion) && dep.Value is not null)
                {
                    version = stringVersion;
                    versionPath = dep.Value.GetPath();
                }
                else if (dep.Value is JsonObject dependencyObject && JsonNodeDocument.TryGetProperty(dependencyObject, "version", out var versionNode) && versionNode is not null && JsonNodeDocument.TryGetString(versionNode, out var objectVersion))
                {
                    version = objectVersion;
                    versionPath = versionNode.GetPath();
                }
                else
                {
                    continue;
                }

                if (version is not null && versionPath is not null)
                {
                    context.ReportDependency(this, packageName, version, DependencyType.NuGet,
                        nameLocation: new NonUpdatableLocation(context),
                        versionLocation: new JsonLocation(context, versionPath));
                }
            }
        }
    }

    private static IEnumerable<JsonObject> EnumerateDependencyObjects(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            if (JsonNodeDocument.TryGetObject(jsonObject, "dependencies", out var dependencies))
            {
                yield return dependencies;
            }

            foreach (var property in JsonNodeDocument.GetProperties(jsonObject))
            {
                foreach (var child in EnumerateDependencyObjects(property.Value))
                {
                    yield return child;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in JsonNodeDocument.GetArray(jsonArray))
            {
                foreach (var child in EnumerateDependencyObjects(item))
                {
                    yield return child;
                }
            }
        }
    }
}
