using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Composer manifests (<c>composer.json</c>) for PHP package dependencies.</summary>
public sealed class ComposerDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.PhpPackage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("composer.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            foreach (var section in new[] { "require", "require-dev" })
            {
                if (!JsonNodeDocument.TryGetObject(root, section, out var dependencies))
                    continue;

                foreach (var dependency in dependencies)
                {
                    // Platform requirements, such as php or ext-json, are not packages. Packages are always named vendor/package.
                    if (!dependency.Key.Contains('/', StringComparison.Ordinal) || dependency.Value is null || !JsonNodeDocument.TryGetString(dependency.Value, out var version))
                        continue;

                    context.ReportDependency(this, dependency.Key, version, DependencyType.PhpPackage,
                        new NonUpdatableLocation(context), new JsonLocation(context, JsonNodeDocument.GetPath(dependency.Value)));
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}
