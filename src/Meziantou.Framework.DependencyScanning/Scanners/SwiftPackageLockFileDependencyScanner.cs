using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Swift Package Manager lockfiles (<c>Package.resolved</c>) for the pinned versions of Swift package dependencies.</summary>
/// <remarks>SwiftPM checks out the pinned revision, so only the version of a pin that has a revision and no version or branch is updatable.</remarks>
public sealed class SwiftPackageLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.SwiftPackage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Package.resolved", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            foreach (var pin in EnumeratePins(root))
            {
                var dependencyName = GetDependencyName(pin, out var dependencyNamePath);
                var dependencyVersion = GetDependencyVersion(pin, out var dependencyVersionPath, out var isVersionUpdatable);
                if (dependencyName is null && dependencyVersion is null)
                    continue;

                Location? versionLocation = null;
                if (dependencyVersionPath is not null)
                {
                    versionLocation = isVersionUpdatable ? new JsonLocation(context, dependencyVersionPath) : new NonUpdatableLocation(context);
                }

                context.ReportDependency(this, dependencyName, dependencyVersion, DependencyType.SwiftPackage,
                    nameLocation: dependencyNamePath is null ? null : new JsonLocation(context, dependencyNamePath),
                    versionLocation: versionLocation);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static IEnumerable<JsonObject> EnumeratePins(JsonObject root)
    {
        if (JsonNodeDocument.TryGetArray(root, "pins", out var pinsV2))
        {
            foreach (var pin in JsonNodeDocument.GetArray(pinsV2).OfType<JsonObject>())
            {
                yield return pin;
            }
        }

        if (JsonNodeDocument.TryGetObject(root, "object", out var rootObject) &&
            JsonNodeDocument.TryGetArray(rootObject, "pins", out var pinsV1))
        {
            foreach (var pin in JsonNodeDocument.GetArray(pinsV1).OfType<JsonObject>())
            {
                yield return pin;
            }
        }
    }

    private static string? GetDependencyName(JsonObject pin, out string? dependencyNamePath)
    {
        if (TryGetString(pin, "identity", out var dependencyName, out dependencyNamePath))
            return dependencyName;

        if (TryGetString(pin, "package", out dependencyName, out dependencyNamePath))
            return dependencyName;

        if (TryGetString(pin, "location", out dependencyName, out dependencyNamePath))
            return dependencyName;

        if (TryGetString(pin, "repositoryURL", out dependencyName, out dependencyNamePath))
            return dependencyName;

        dependencyNamePath = null;
        return null;
    }

    private static string? GetDependencyVersion(JsonObject pin, out string? dependencyVersionPath, out bool isUpdatable)
    {
        dependencyVersionPath = null;
        isUpdatable = false;

        if (!JsonNodeDocument.TryGetObject(pin, "state", out var state))
            return null;

        // SwiftPM checks out the pinned revision, so rewriting the version or the branch alone would leave the pin
        // pointing at the old commit. Only a revision-only pin can be updated in place.
        if (TryGetString(state, "version", out var version, out dependencyVersionPath))
            return version;

        if (TryGetString(state, "branch", out version, out dependencyVersionPath))
            return version;

        if (TryGetString(state, "revision", out version, out dependencyVersionPath))
        {
            isUpdatable = true;
            return version;
        }

        return null;
    }

    private static bool TryGetString(JsonObject jsonObject, string propertyName, out string? value, out string? valuePath)
    {
        if (JsonNodeDocument.TryGetProperty(jsonObject, propertyName, out var node) &&
            node is not null &&
            JsonNodeDocument.TryGetString(node, out var stringValue))
        {
            value = stringValue;
            valuePath = JsonNodeDocument.GetPath(node);
            return true;
        }

        value = null;
        valuePath = null;
        return false;
    }
}
