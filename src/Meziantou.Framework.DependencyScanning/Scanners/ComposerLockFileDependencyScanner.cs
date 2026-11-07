using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Composer lockfiles (<c>composer.lock</c>) for the resolved versions of PHP package dependencies.</summary>
/// <remarks>The versions are not updatable.</remarks>
public sealed class ComposerLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.PhpPackage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("composer.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            foreach (var section in new[] { "packages", "packages-dev" })
            {
                if (!JsonNodeDocument.TryGetArray(root, section, out var packages))
                    continue;

                foreach (var package in packages.OfType<JsonObject>())
                {
                    if (!JsonNodeDocument.TryGetString(package["name"], out var name) || !JsonNodeDocument.TryGetString(package["version"], out var version))
                        continue;

                    context.ReportDependency(this, name, version, DependencyType.PhpPackage,
                        new NonUpdatableLocation(context), new NonUpdatableLocation(context));
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}
