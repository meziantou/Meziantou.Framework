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

            if (!doc.TryGetObject(root, "tools", "$", out var tools, out var toolsPath))
                return;

            foreach (var dep in doc.GetProperties(tools, toolsPath))
            {
                var packageName = dep.Name;
                string? version;
                var versionPath = dep.Path;

                if (JsonNodeDocument.TryGetString(dep.Value, out var stringVersion))
                {
                    version = stringVersion;
                }
                else if (dep.Value is JsonObject dependencyObject)
                {
                    if (!doc.TryGetProperty(dependencyObject, "version", dep.Path, out var versionNode, out versionPath))
                    {
                        continue;
                    }

                    if (!JsonNodeDocument.TryGetString(versionNode, out var objectVersion))
                    {
                        continue;
                    }

                    version = objectVersion;
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
        catch (JsonException)
        {
        }
    }
}
