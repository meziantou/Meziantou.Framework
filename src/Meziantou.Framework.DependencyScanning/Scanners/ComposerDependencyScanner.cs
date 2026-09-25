using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Composer manifests and lockfiles for PHP package dependencies.</summary>
public sealed class ComposerDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.PhpPackage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("composer.json", ignoreCase: false)
            || context.HasFileName("composer.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            var isLockFile = Path.GetFileName(context.FullPath).Equals("composer.lock", StringComparison.Ordinal);
            var sections = isLockFile ? new[] { "packages", "packages-dev" } : new[] { "require", "require-dev" };
            foreach (var section in sections)
            {
                if (isLockFile && JsonNodeDocument.TryGetArray(root, section, out var packages))
                {
                    foreach (var package in packages.OfType<JsonObject>())
                    {
                        if (!JsonNodeDocument.TryGetString(package["name"], out var name) || !JsonNodeDocument.TryGetString(package["version"], out var version))
                            continue;

                        context.ReportDependency(this, name, version, DependencyType.PhpPackage,
                            new NonUpdatableLocation(context), new NonUpdatableLocation(context));
                    }
                }
                else if (!isLockFile && JsonNodeDocument.TryGetObject(root, section, out var dependencies))
                {
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
        }
        catch (JsonException)
        {
        }
    }
}
