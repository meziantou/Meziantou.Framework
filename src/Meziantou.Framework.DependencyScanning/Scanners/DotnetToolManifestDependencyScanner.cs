using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans .NET tool manifest files (dotnet-tools.json) for local tool dependencies.</summary>
public sealed class DotNetToolManifestDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("dotnet-tools.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            if (!JsonNodeDocument.TryGetObject(root, "tools", out var tools))
                return;

            foreach (var dep in JsonNodeDocument.GetProperties(tools))
            {
                var packageName = dep.Name;
                string? version;
                string? versionPath = null;

                if (dep.Value is not null && JsonNodeDocument.TryGetString(dep.Value, out var stringVersion))
                {
                    version = stringVersion;
                    versionPath = dep.Value.GetPath();
                }
                else if (dep.Value is JsonObject dependencyObject)
                {
                    if (!JsonNodeDocument.TryGetProperty(dependencyObject, "version", out var versionNode))
                    {
                        continue;
                    }

                    if (versionNode is null || !JsonNodeDocument.TryGetString(versionNode, out var objectVersion))
                    {
                        continue;
                    }

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
        catch (JsonException)
        {
        }
    }
}
