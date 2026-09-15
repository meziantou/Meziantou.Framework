using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans npm package.json files for JavaScript package dependencies.</summary>
public sealed class NpmPackageJsonDependencyScanner : DependencyScanner
{
    private static readonly string[] DependencySectionPropertyNames =
    [
        "dependencies",
        "devDependencies",
        "peerDependencies",
        "optionalDependencies",
    ];

    private const string NpmAliasPrefix = "npm:";

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Npm];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("package.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            foreach (var dependencySectionPropertyName in DependencySectionPropertyNames)
            {
                if (JsonNodeDocument.TryGetObject(root, dependencySectionPropertyName, out var deps))
                {
                    await ScanDependenciesAsync(context, deps).ConfigureAwait(false);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private ValueTask ScanDependenciesAsync(ScanFileContext context, JsonObject deps)
    {
        foreach (var dep in deps)
        {
            if (dep.Value is null)
                continue;

            var packageName = dep.Key;
            string? valuePath = null;
            string? version = null;
            if (JsonNodeDocument.TryGetString(dep.Value, out var stringVersion))
            {
                version = stringVersion;
                valuePath = JsonNodeDocument.GetPath(dep.Value);
            }
            else if (dep.Value is JsonObject dependencyObject)
            {
                if (JsonNodeDocument.TryGetProperty(dependencyObject, "version", out var versionNode) && versionNode is not null && JsonNodeDocument.TryGetString(versionNode, out var objectVersion))
                {
                    version = objectVersion;
                    valuePath = JsonNodeDocument.GetPath(versionNode);
                }
            }
            else
            {
                continue;
            }

            if (version is null || valuePath is null)
                continue;

            // "alias": "npm:package@range" installs another package under the alias name
            if (version.StartsWith(NpmAliasPrefix, StringComparison.Ordinal))
            {
                var aliased = version[NpmAliasPrefix.Length..];
                var versionSeparator = aliased.LastIndexOf('@', StringComparison.Ordinal);
                if (versionSeparator > 0 && versionSeparator < aliased.Length - 1)
                {
                    context.ReportDependency(this, aliased[..versionSeparator], aliased[(versionSeparator + 1)..], DependencyType.Npm,
                        nameLocation: new NonUpdatableLocation(context),
                        versionLocation: new JsonLocation(context, valuePath, NpmAliasPrefix.Length + versionSeparator + 1, aliased.Length - versionSeparator - 1),
                        tags: [],
                        metadata: [KeyValuePair.Create<string, object?>("alias", packageName)]);
                }
                else if (aliased.Length > 0 && versionSeparator <= 0)
                {
                    context.ReportDependency(this, aliased, version: null, DependencyType.Npm,
                        nameLocation: new NonUpdatableLocation(context),
                        versionLocation: null,
                        tags: [],
                        metadata: [KeyValuePair.Create<string, object?>("alias", packageName)]);
                }

                continue;
            }

            // Protocols (workspace:, file:, link:, portal:, git+https:, github:, http(s)://...) and paths or GitHub shorthands (owner/repo#tag)
            // are not registry versions, so they cannot be updated as such. Semver ranges never contain ':' or '/'.
            var isRegistryVersion = !version.AsSpan().ContainsAny(':', '/');
            context.ReportDependency(this, packageName, version, DependencyType.Npm,
                nameLocation: new NonUpdatableLocation(context),
                versionLocation: isRegistryVersion ? new JsonLocation(context, valuePath) : new NonUpdatableLocation(context));
        }

        return ValueTask.CompletedTask;
    }
}
