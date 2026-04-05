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

            ScanDependencies(context, doc, EnumerateDependencyObjects(doc, root, "$"));
            if (doc.TryGetObject(root, "tools", "$", out var tools, out var toolsPath))
            {
                ScanDependencies(context, doc, [(Dependencies: tools, Path: toolsPath)]);
            }
        }
        catch (JsonException)
        {
        }
    }

    private void ScanDependencies(ScanFileContext context, JsonNodeDocument doc, IEnumerable<(JsonObject Dependencies, string Path)> dependencyObjects)
    {
        foreach (var depsMatch in dependencyObjects)
        {
            foreach (var dep in doc.GetProperties(depsMatch.Dependencies, depsMatch.Path))
            {
                var packageName = dep.Name;
                string? version;
                var versionPath = dep.Path;
                if (JsonNodeDocument.TryGetString(dep.Value, out var stringVersion))
                {
                    version = stringVersion;
                }
                else if (dep.Value is JsonObject dependencyObject && doc.TryGetProperty(dependencyObject, "version", dep.Path, out var versionNode, out var objectVersionPath) && JsonNodeDocument.TryGetString(versionNode, out var objectVersion))
                {
                    version = objectVersion;
                    versionPath = objectVersionPath;
                }
                else
                {
                    continue;
                }

                if (version is not null)
                {
                    context.ReportDependency(this, packageName, version, DependencyType.NuGet,
                        nameLocation: new NonUpdatableLocation(context),
                        versionLocation: new JsonLocation(context, versionPath, doc.GetLineInfo(versionPath)));
                }
            }
        }
    }

    private IEnumerable<(JsonObject Dependencies, string Path)> EnumerateDependencyObjects(JsonNodeDocument doc, JsonNode? node, string path)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in doc.GetProperties(jsonObject, path))
            {
                if (property.Name == "dependencies" && property.Value is JsonObject dependencies)
                {
                    yield return (dependencies, property.Path);
                }

                foreach (var child in EnumerateDependencyObjects(doc, property.Value, property.Path))
                {
                    yield return child;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in doc.GetArray(jsonArray, path))
            {
                foreach (var child in EnumerateDependencyObjects(doc, item.Value, item.Path))
                {
                    yield return child;
                }
            }
        }
    }
}
