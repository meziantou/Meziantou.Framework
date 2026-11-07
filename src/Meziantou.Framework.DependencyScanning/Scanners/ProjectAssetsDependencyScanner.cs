using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans NuGet project assets files (project.assets.json) for resolved package dependencies, including transitive dependencies.</summary>
public sealed class ProjectAssetsDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("project.assets.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root || !JsonNodeDocument.TryGetObject(root, "libraries", out var libraries))
                return;

            foreach (var library in JsonNodeDocument.GetProperties(libraries))
            {
                if (library.Value is not JsonObject libraryValue ||
                    !JsonNodeDocument.TryGetProperty(libraryValue, "type", out var typeValue) ||
                    !JsonNodeDocument.TryGetString(typeValue, out var type) ||
                    !string.Equals(type, "package", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = library.Name.LastIndexOf('/', StringComparison.Ordinal);
                if (separatorIndex <= 0 || separatorIndex == library.Name.Length - 1)
                    continue;

                var packageName = library.Name[..separatorIndex];
                var packageVersion = library.Name[(separatorIndex + 1)..];
                context.ReportDependency(this, packageName, packageVersion, DependencyType.NuGet,
                    nameLocation: new NonUpdatableLocation(context),
                    versionLocation: new NonUpdatableLocation(context));
            }
        }
        catch (JsonException)
        {
        }
    }
}
