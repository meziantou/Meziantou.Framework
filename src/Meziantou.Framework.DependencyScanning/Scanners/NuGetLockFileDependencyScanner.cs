using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans NuGet lock files (packages.lock.json and packages.&lt;project&gt;.lock.json) for the resolved versions of direct and transitive package dependencies.</summary>
/// <remarks>The versions are not updatable, as the content hash of each package would no longer match.</remarks>
public sealed class NuGetLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("packages.lock.json", ignoreCase: true)
            || (context.FileName.StartsWith("packages.", StringComparison.OrdinalIgnoreCase) && context.FileName.EndsWith(".lock.json", StringComparison.OrdinalIgnoreCase) && context.FileName.Length > "packages..lock.json".Length);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root || !JsonNodeDocument.TryGetObject(root, "dependencies", out var targets))
                return;

            // The same package is listed once per target framework and runtime identifier
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in JsonNodeDocument.GetProperties(targets))
            {
                if (target.Value is not JsonObject packages)
                    continue;

                foreach (var package in JsonNodeDocument.GetProperties(packages))
                {
                    if (package.Value is not JsonObject packageValue)
                        continue;

                    // Project references have no resolved version
                    if (JsonNodeDocument.TryGetProperty(packageValue, "type", out var typeNode) && JsonNodeDocument.TryGetString(typeNode, out var type) && type.Equals("Project", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!JsonNodeDocument.TryGetProperty(packageValue, "resolved", out var resolvedNode) || !JsonNodeDocument.TryGetString(resolvedNode, out var resolved) || string.IsNullOrWhiteSpace(resolved))
                        continue;

                    if (!reported.Add(package.Name + "/" + resolved))
                        continue;

                    context.ReportDependency(this, package.Name, resolved, DependencyType.NuGet,
                        nameLocation: new NonUpdatableLocation(context),
                        versionLocation: new NonUpdatableLocation(context));
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}
