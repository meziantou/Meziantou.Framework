using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// Scans Claude Code and GitHub Copilot plugin marketplaces (<c>.claude-plugin/marketplace.json</c> and <c>.github/plugin/marketplace.json</c>)
/// for the sources of their plugins, and Claude Code and GitHub Copilot settings files (<c>.claude/settings.json</c> and
/// <c>.github/copilot/settings.json</c>, and their <c>settings.local.json</c> counterparts) for the marketplaces declared in <c>extraKnownMarketplaces</c>.
/// </summary>
/// <remarks>
/// <para>
/// A plugin hosted in a git repository is reported as <see cref="DependencyType.AgentPlugin"/>, named after the GitHub repository (<c>owner/repo</c>)
/// or the git URL. Its version is the commit <c>sha</c> when there is one, and the <c>ref</c> otherwise. A plugin published to npm is reported as
/// <see cref="DependencyType.Npm"/>. Plugins stored in the marketplace repository (relative paths), zip archives and commands are not reported.
/// The plugin name is available in <see cref="Dependency.Metadata"/> under the <c>plugin</c> key, and the <c>ref</c>, <c>path</c> and npm <c>registry</c>
/// under the keys of the same name.
/// </para>
/// <para>
/// A marketplace hosted in a git repository or at a URL is reported as <see cref="DependencyType.AgentPluginMarketplace"/>, with its <c>ref</c> as the version.
/// The marketplace name is available in <see cref="Dependency.Metadata"/> under the <c>marketplace</c> key. Local directories and files are not reported.
/// </para>
/// </remarks>
public sealed class AgentPluginDependencyScanner : DependencyScanner
{
    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.AgentPlugin, DependencyType.AgentPluginMarketplace, DependencyType.Npm];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        var directory = context.Directory.TrimEnd(DirectorySeparators);
        var directoryName = Path.GetFileName(directory);
        if (context.HasFileName("marketplace.json", ignoreCase: false))
            return directoryName is ".claude-plugin" || (directoryName is "plugin" && GetParentDirectoryName(directory) is ".github");

        if (context.HasFileName("settings.json", ignoreCase: false) || context.HasFileName("settings.local.json", ignoreCase: false))
            return directoryName is ".claude" || (directoryName is "copilot" && GetParentDirectoryName(directory) is ".github");

        return false;

        static ReadOnlySpan<char> GetParentDirectoryName(ReadOnlySpan<char> directory)
        {
            return Path.GetFileName(Path.GetDirectoryName(directory));
        }
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            // ShouldScanFileCore only accepts marketplace.json and settings files
            if (Path.GetFileName(context.FullPath) is "marketplace.json")
            {
                ScanMarketplace(context, root);
            }
            else
            {
                ScanSettings(context, root);
            }
        }
        catch (JsonException)
        {
        }
    }

    private void ScanMarketplace(ScanFileContext context, JsonObject root)
    {
        if (!JsonNodeDocument.TryGetArray(root, "plugins", out var plugins))
            return;

        foreach (var plugin in JsonNodeDocument.GetArray(plugins))
        {
            if (plugin is not JsonObject pluginObject || !JsonNodeDocument.TryGetProperty(pluginObject, "source", out var source))
                continue;

            var pluginName = GetString(pluginObject, "name");
            if (source is JsonObject sourceObject)
            {
                ScanPluginSource(context, pluginName, sourceObject);
            }
            else if (JsonNodeDocument.TryGetString(source, out var sourceValue))
            {
                ScanPluginSource(context, pluginName, source!, sourceValue);
            }
        }
    }

    private void ScanPluginSource(ScanFileContext context, string? pluginName, JsonObject source)
    {
        switch (GetString(source, "source"))
        {
            case "github":
                ReportGitDependency(context, DependencyType.AgentPlugin, "plugin", pluginName, source, nameProperty: "repo");
                break;

            case "url" or "git-subdir":
                ReportGitDependency(context, DependencyType.AgentPlugin, "plugin", pluginName, source, nameProperty: "url");
                break;

            case "npm":
                if (!TryGetStringNode(source, "package", out var packageNode, out var package))
                    break;

                TryGetStringNode(source, "version", out var versionNode, out var version);
                context.ReportDependency(
                    this,
                    package,
                    version,
                    DependencyType.Npm,
                    nameLocation: new JsonLocation(context, JsonNodeDocument.GetPath(packageNode)),
                    versionLocation: versionNode is null ? null : new JsonLocation(context, JsonNodeDocument.GetPath(versionNode)),
                    tags: [],
                    metadata: [
                        KeyValuePair.Create<string, object?>("plugin", pluginName),
                        KeyValuePair.Create<string, object?>("registry", GetString(source, "registry")),
                    ]);
                break;
        }
    }

    private void ScanPluginSource(ScanFileContext context, string? pluginName, JsonNode sourceNode, string source)
    {
        // A string is a path relative to the marketplace, unless it is a git URL or an "owner/repo#ref" GitHub shorthand.
        // "owner/repo" alone cannot be told apart from a relative path, so it is not reported.
        if (source.Contains("://", StringComparison.Ordinal) || source.StartsWith("git@", StringComparison.Ordinal))
        {
            context.ReportDependency(
                this,
                source,
                version: null,
                DependencyType.AgentPlugin,
                nameLocation: new JsonLocation(context, JsonNodeDocument.GetPath(sourceNode)),
                versionLocation: null,
                tags: [],
                metadata: [
                    KeyValuePair.Create<string, object?>("plugin", pluginName),
                    KeyValuePair.Create<string, object?>("ref", null),
                    KeyValuePair.Create<string, object?>("path", null),
                ]);
            return;
        }

        var hashIndex = source.IndexOf('#', StringComparison.Ordinal);
        if (hashIndex <= 0 || hashIndex == source.Length - 1 || source[0] is '.')
            return;

        var name = source[..hashIndex];
        var version = source[(hashIndex + 1)..];
        var sourcePath = JsonNodeDocument.GetPath(sourceNode);
        context.ReportDependency(
            this,
            name,
            version,
            DependencyType.AgentPlugin,
            nameLocation: new JsonLocation(context, sourcePath, 0, name.Length),
            versionLocation: new JsonLocation(context, sourcePath, hashIndex + 1, version.Length),
            tags: [],
            metadata: [
                KeyValuePair.Create<string, object?>("plugin", pluginName),
                KeyValuePair.Create<string, object?>("ref", version),
                KeyValuePair.Create<string, object?>("path", null),
            ]);
    }

    private void ScanSettings(ScanFileContext context, JsonObject root)
    {
        if (!JsonNodeDocument.TryGetObject(root, "extraKnownMarketplaces", out var marketplaces))
            return;

        foreach (var (marketplaceName, value) in JsonNodeDocument.GetProperties(marketplaces))
        {
            if (value is not JsonObject marketplace)
                continue;

            // Claude Code nests the source ({ "source": { "source": "github", "repo": "..." } }), a flat form is also accepted
            var source = JsonNodeDocument.TryGetObject(marketplace, "source", out var nestedSource) ? nestedSource : marketplace;
            switch (GetString(source, "source"))
            {
                case "github":
                    ReportGitDependency(context, DependencyType.AgentPluginMarketplace, "marketplace", marketplaceName, source, nameProperty: "repo");
                    break;

                case "git" or "url":
                    ReportGitDependency(context, DependencyType.AgentPluginMarketplace, "marketplace", marketplaceName, source, nameProperty: "url");
                    break;
            }
        }
    }

    private void ReportGitDependency(ScanFileContext context, DependencyType type, string ownerMetadataKey, string? ownerName, JsonObject source, string nameProperty)
    {
        if (!TryGetStringNode(source, nameProperty, out var nameNode, out var name))
            return;

        TryGetStringNode(source, "ref", out var refNode, out var gitRef);
        TryGetStringNode(source, "sha", out var shaNode, out var sha);
        var versionNode = shaNode ?? refNode;
        context.ReportDependency(
            this,
            name,
            sha ?? gitRef,
            type,
            nameLocation: new JsonLocation(context, JsonNodeDocument.GetPath(nameNode)),
            versionLocation: versionNode is null ? null : new JsonLocation(context, JsonNodeDocument.GetPath(versionNode)),
            tags: [],
            metadata: [
                KeyValuePair.Create<string, object?>(ownerMetadataKey, ownerName),
                KeyValuePair.Create<string, object?>("ref", gitRef),
                KeyValuePair.Create<string, object?>("path", GetString(source, "path")),
            ]);
    }

    private static string? GetString(JsonObject obj, string propertyName)
    {
        return TryGetStringNode(obj, propertyName, out _, out var value) ? value : null;
    }

    private static bool TryGetStringNode(JsonObject obj, string propertyName, [NotNullWhen(true)] out JsonNode? node, [NotNullWhen(true)] out string? value)
    {
        if (JsonNodeDocument.TryGetProperty(obj, propertyName, out var propertyValue) && JsonNodeDocument.TryGetString(propertyValue, out value) && !string.IsNullOrEmpty(value))
        {
            node = propertyValue!;
            return true;
        }

        node = null;
        value = null;
        return false;
    }
}
