using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans npm package.json files and package-lock.json lockfiles for JavaScript package dependencies.</summary>
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
    private const string NodeModulesSegment = "node_modules/";

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Npm];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("package.json", ignoreCase: false)
            || context.HasFileName("package-lock.json", ignoreCase: false)
            || context.HasFileName("npm-shrinkwrap.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            if (!Path.GetFileName(context.FullPath).Equals("package.json", StringComparison.Ordinal))
            {
                ScanLockFile(context, root);
                return;
            }

            foreach (var dependencySectionPropertyName in DependencySectionPropertyNames)
            {
                if (JsonNodeDocument.TryGetObject(root, dependencySectionPropertyName, out var deps))
                {
                    ScanDependencies(context, deps);
                }
            }

            ScanPackageManager(context, root);

            if (JsonNodeDocument.TryGetObject(root, "overrides", out var overrides))
            {
                ScanNpmOverrides(context, overrides);
            }

            if (JsonNodeDocument.TryGetObject(root, "resolutions", out var resolutions))
            {
                ScanOverrides(context, resolutions, GetYarnResolutionPackageName);
            }

            if (JsonNodeDocument.TryGetObject(root, "pnpm", out var pnpm) && JsonNodeDocument.TryGetObject(pnpm, "overrides", out var pnpmOverrides))
            {
                ScanOverrides(context, pnpmOverrides, GetPnpmOverridePackageName);
            }
        }
        catch (JsonException)
        {
        }
    }

    private void ScanDependencies(ScanFileContext context, JsonObject deps)
    {
        foreach (var dep in deps)
        {
            if (dep.Value is null)
                continue;

            if (JsonNodeDocument.TryGetString(dep.Value, out var stringVersion))
            {
                ReportDependency(context, dep.Key, stringVersion, dep.Value);
            }
            else if (dep.Value is JsonObject dependencyObject)
            {
                if (JsonNodeDocument.TryGetProperty(dependencyObject, "version", out var versionNode) && versionNode is not null && JsonNodeDocument.TryGetString(versionNode, out var objectVersion))
                {
                    ReportDependency(context, dep.Key, objectVersion, versionNode);
                }
            }
        }
    }

    /// <summary>Scans <c>"packageManager": "pnpm@9.1.0+sha512.abc"</c>, the package manager Corepack installs.</summary>
    private void ScanPackageManager(ScanFileContext context, JsonObject root)
    {
        if (!JsonNodeDocument.TryGetProperty(root, "packageManager", out var node) || node is null || !JsonNodeDocument.TryGetString(node, out var value))
            return;

        var versionStart = IndexOfVersionSeparator(value);
        if (versionStart <= 0 || versionStart == value.Length - 1)
            return;

        versionStart++;
        var hashStart = value.IndexOf('+', versionStart, StringComparison.Ordinal);
        var versionEnd = hashStart < 0 ? value.Length : hashStart;
        if (versionEnd == versionStart)
            return;

        // Corepack checks the hash that follows the version, so a new version would not match it anymore
        context.ReportDependency(this, value[..(versionStart - 1)], value[versionStart..versionEnd], DependencyType.Npm,
            nameLocation: new NonUpdatableLocation(context),
            versionLocation: hashStart < 0 ? new JsonLocation(context, JsonNodeDocument.GetPath(node), versionStart, versionEnd - versionStart) : new NonUpdatableLocation(context));
    }

    /// <summary>Scans npm overrides, which nest: <c>"foo": { ".": "1.0.0", "bar": "2.0.0" }</c> overrides foo, and bar when foo depends on it.</summary>
    private void ScanNpmOverrides(ScanFileContext context, JsonObject overrides)
    {
        foreach (var (key, value) in overrides)
        {
            if (key is "." || value is null)
                continue;

            var packageName = RemoveVersionSelector(key);
            if (packageName.Length is 0)
                continue;

            if (JsonNodeDocument.TryGetString(value, out var version))
            {
                ReportDependency(context, packageName, version, value);
            }
            else if (value is JsonObject nestedOverrides)
            {
                if (JsonNodeDocument.TryGetProperty(nestedOverrides, ".", out var selfNode) && selfNode is not null && JsonNodeDocument.TryGetString(selfNode, out var selfVersion))
                {
                    ReportDependency(context, packageName, selfVersion, selfNode);
                }

                ScanNpmOverrides(context, nestedOverrides);
            }
        }
    }

    /// <summary>Scans a flat object whose keys select a package, such as Yarn resolutions or pnpm overrides.</summary>
    private void ScanOverrides(ScanFileContext context, JsonObject overrides, Func<string, string> getPackageName)
    {
        foreach (var (key, value) in overrides)
        {
            // pnpm removes a dependency overridden with "-"
            if (value is null || !JsonNodeDocument.TryGetString(value, out var version) || version is "-")
                continue;

            var packageName = getPackageName(key);
            if (packageName.Length is 0)
                continue;

            ReportDependency(context, packageName, version, value);
        }
    }

    private void ReportDependency(ScanFileContext context, string packageName, string version, JsonNode valueNode)
    {
        var valuePath = JsonNodeDocument.GetPath(valueNode);

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

            return;
        }

        // Protocols (workspace:, file:, link:, portal:, git+https:, github:, http(s)://...) and paths or GitHub shorthands (owner/repo#tag)
        // are not registry versions, so they cannot be updated as such. Semver ranges never contain ':' or '/'. An override can also
        // reference the version of a dependency, as in "$foo".
        var isRegistryVersion = !version.AsSpan().ContainsAny(':', '/') && !version.StartsWith('$', StringComparison.Ordinal);
        context.ReportDependency(this, packageName, version, DependencyType.Npm,
            nameLocation: new NonUpdatableLocation(context),
            versionLocation: isRegistryVersion ? new JsonLocation(context, valuePath) : new NonUpdatableLocation(context));
    }

    /// <summary>Scans the <c>packages</c> of a lockfile, whose keys are install paths such as <c>node_modules/a/node_modules/@scope/b</c>.</summary>
    /// <remarks>The lockfile v1 <c>dependencies</c> tree is not scanned: npm 7 and later write <c>packages</c>.</remarks>
    private void ScanLockFile(ScanFileContext context, JsonObject root)
    {
        if (!JsonNodeDocument.TryGetObject(root, "packages", out var packages))
            return;

        foreach (var (key, value) in packages)
        {
            // The root project ("") and the workspace folders are not installed from a registry
            var nodeModulesIndex = key.LastIndexOf(NodeModulesSegment, StringComparison.Ordinal);
            if (nodeModulesIndex < 0 || (nodeModulesIndex > 0 && key[nodeModulesIndex - 1] is not '/') || value is not JsonObject entry)
                continue;

            // A link points to a workspace folder, which has its own entry
            if (entry["link"] is JsonValue link && link.TryGetValue<bool>(out var isLink) && isLink)
                continue;

            if (!JsonNodeDocument.TryGetString(entry["version"], out var version))
                continue;

            // The name is only written when it differs from the install folder, which is an alias
            var folderName = key[(nodeModulesIndex + NodeModulesSegment.Length)..];
            var hasName = JsonNodeDocument.TryGetString(entry["name"], out var name) && !string.Equals(name, folderName, StringComparison.Ordinal);

            // The version is not updatable as the integrity of the package would not match anymore
            context.ReportDependency(this, hasName ? name : folderName, version, DependencyType.Npm,
                nameLocation: new NonUpdatableLocation(context),
                versionLocation: new NonUpdatableLocation(context),
                tags: [],
                metadata: hasName ? [KeyValuePair.Create<string, object?>("alias", folderName)] : []);
        }
    }

    /// <summary>Removes the version selector of a package, such as <c>@^1.0.0</c> in <c>@scope/foo@^1.0.0</c>.</summary>
    private static string RemoveVersionSelector(string value)
    {
        var index = IndexOfVersionSeparator(value);
        return index > 0 ? value[..index] : value;
    }

    /// <summary>Gets the index of the <c>@</c> that follows a package name, which can start with one, as in <c>@scope/foo@1.0.0</c>.</summary>
    private static int IndexOfVersionSeparator(string value) => value.Length > 1 ? value.IndexOf('@', 1, StringComparison.Ordinal) : -1;

    /// <summary>Gets the package a Yarn resolution applies to, which is the last one of its path, such as <c>c</c> in <c>a/**/@scope/b/c</c>.</summary>
    private static string GetYarnResolutionPackageName(string key)
    {
        var segments = key.Split('/');
        var name = segments.Length >= 2 && segments[^2].StartsWith('@', StringComparison.Ordinal) ? segments[^2] + "/" + segments[^1] : segments[^1];
        name = RemoveVersionSelector(name);
        return name is "**" or "*" ? "" : name;
    }

    /// <summary>Gets the package a pnpm override applies to, which is the last one of its path, such as <c>bar</c> in <c>foo@1>bar@^2</c>.</summary>
    private static string GetPnpmOverridePackageName(string key)
    {
        return RemoveVersionSelector(key[(key.LastIndexOf('>', StringComparison.Ordinal) + 1)..]);
    }
}
