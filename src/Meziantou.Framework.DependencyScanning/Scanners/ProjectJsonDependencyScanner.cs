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
            ScanDependencies(context, doc, "$..dependencies");
            ScanDependencies(context, doc, "$.tools");
        }
        catch (JsonException)
        {
        }
    }

    private void ScanDependencies(ScanFileContext context, JsonNodeDocument doc, string path)
    {
        foreach (var depsMatch in doc.Select(path))
        {
            if (depsMatch.Node is not JsonObject deps)
                continue;

            foreach (var dep in deps)
            {
                var valuePath = JsonNodeDocument.AppendPropertyPath(depsMatch.Path, dep.Key);
                var packageName = dep.Key;
                string? version;
                var versionPath = valuePath;
                if (dep.Value is JsonValue dependencyValue && dependencyValue.TryGetValue<string>(out var stringVersion))
                {
                    version = stringVersion;
                }
                else if (dep.Value is JsonObject dependencyObject && dependencyObject.TryGetPropertyValue("version", out var versionNode) && versionNode is JsonValue versionValue && versionValue.TryGetValue<string>(out var objectVersion))
                {
                    version = objectVersion;
                    versionPath = JsonNodeDocument.AppendPropertyPath(valuePath, "version");
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
}
