using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed partial class JavaDependencyScanner
{
    private async ValueTask ScanVersionCatalogAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        var tree = TomlSyntaxTree.ParseText(text, context.FullPath);
        var versions = new Dictionary<string, (string Version, Location Location)>(StringComparer.Ordinal);
        var versionReferences = new List<CatalogVersionReference>();
        foreach (var table in tree.GetRoot().Tables)
        {
            switch (TomlUtilities.GetName(table.Key))
            {
                case "versions":
                    foreach (var property in table.Properties)
                    {
                        if (TomlUtilities.GetString(property.Value) is { } version)
                        {
                            versions[TomlUtilities.GetName(property.Key)] = (version.Value, CreateCatalogLocation(context, tree, version));
                        }
                    }

                    break;

                case "libraries":
                    foreach (var property in table.Properties)
                    {
                        ScanCatalogLibrary(context, tree, property.Value, versionReferences);
                    }

                    break;

                case "plugins":
                    foreach (var property in table.Properties)
                    {
                        ScanCatalogPlugin(context, tree, property.Value, versionReferences);
                    }

                    break;
            }
        }

        var referenceCounts = versionReferences.CountBy(item => item.VersionReference, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);
        foreach (var reference in versionReferences)
        {
            var metadata = reference.PluginId is null ? [] : new[] { KeyValuePair.Create<string, object?>(PluginIdMetadataKey, reference.PluginId) };
            if (!versions.TryGetValue(reference.VersionReference, out var version))
            {
                context.ReportDependency(this, reference.Name, version: null, DependencyType.JavaPackage, new NonUpdatableLocation(context), new NonUpdatableLocation(context), tags: [], metadata);
                continue;
            }

            // A version shared by several libraries or plugins cannot be updated for one of them only
            var isShared = referenceCounts[reference.VersionReference] > 1;
            context.ReportDependency(this, reference.Name, version.Version, DependencyType.JavaPackage,
                new NonUpdatableLocation(context),
                isShared ? new NonUpdatableLocation(context) : version.Location,
                tags: [], metadata);
        }
    }

    private void ScanCatalogLibrary(ScanFileContext context, TomlSyntaxTree tree, TomlValueSyntax value, List<CatalogVersionReference> versionReferences)
    {
        if (TomlUtilities.GetString(value) is { } notation)
        {
            // guava = "com.google.guava:guava:33.0.0"
            var separator = notation.Value.LastIndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || notation.Value.AsSpan(0, separator).IndexOf(':') < 0)
                return;

            context.ReportDependency(this, notation.Value[..separator], notation.Value[(separator + 1)..], DependencyType.JavaPackage,
                new NonUpdatableLocation(context),
                CreateCatalogLocation(context, tree, notation, separator + 1, notation.Value.Length - separator - 1));
            return;
        }

        // guava = { module = "com.google.guava:guava", version = "33.0.0" }
        // guava = { group = "com.google.guava", name = "guava", version.ref = "guava" }
        string name;
        if (TomlUtilities.GetInlineTableString(value, "module") is { } module)
        {
            name = module.Value;
        }
        else if (TomlUtilities.GetInlineTableString(value, "group") is { } group && TomlUtilities.GetInlineTableString(value, "name") is { } artifact)
        {
            name = group.Value + ":" + artifact.Value;
        }
        else
        {
            return;
        }

        ScanCatalogVersion(context, tree, value, name, pluginId: null, versionReferences);
    }

    private void ScanCatalogPlugin(ScanFileContext context, TomlSyntaxTree tree, TomlValueSyntax value, List<CatalogVersionReference> versionReferences)
    {
        if (TomlUtilities.GetString(value) is { } notation)
        {
            // kotlin = "org.jetbrains.kotlin.jvm:1.9.0"
            var separator = notation.Value.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == notation.Value.Length - 1 || notation.Value.IndexOf(':', separator + 1, StringComparison.Ordinal) >= 0)
                return;

            var id = notation.Value[..separator];
            context.ReportDependency(this, GetGradlePluginMarkerName(id), notation.Value[(separator + 1)..], DependencyType.JavaPackage,
                new NonUpdatableLocation(context),
                CreateCatalogLocation(context, tree, notation, separator + 1, notation.Value.Length - separator - 1),
                tags: [], metadata: [KeyValuePair.Create<string, object?>(PluginIdMetadataKey, id)]);
            return;
        }

        // kotlin = { id = "org.jetbrains.kotlin.jvm", version.ref = "kotlin" }
        if (TomlUtilities.GetInlineTableString(value, "id") is { } pluginId)
        {
            ScanCatalogVersion(context, tree, value, GetGradlePluginMarkerName(pluginId.Value), pluginId.Value, versionReferences);
        }
    }

    private void ScanCatalogVersion(ScanFileContext context, TomlSyntaxTree tree, TomlValueSyntax value, string name, string? pluginId, List<CatalogVersionReference> versionReferences)
    {
        // version = "1.0", version.ref = "x", or version = { ref = "x" }. Rich versions, such as { strictly = "1.0" }, are not read.
        var versionValue = GetInlineTableValue(value, "version");
        var versionReference = TomlUtilities.GetInlineTableString(value, "version.ref") ?? (versionValue is null ? null : TomlUtilities.GetInlineTableString(versionValue, "ref"));
        if (versionReference is not null)
        {
            versionReferences.Add(new CatalogVersionReference(name, versionReference.Value, pluginId));
            return;
        }

        string? version = null;
        Location location = new NonUpdatableLocation(context);
        if (versionValue is not null && TomlUtilities.GetString(versionValue) is { } versionString)
        {
            version = versionString.Value;
            location = CreateCatalogLocation(context, tree, versionString);
        }

        if (pluginId is null)
        {
            context.ReportDependency(this, name, version, DependencyType.JavaPackage, new NonUpdatableLocation(context), location);
        }
        else
        {
            context.ReportDependency(this, name, version, DependencyType.JavaPackage, new NonUpdatableLocation(context), location,
                tags: [], metadata: [KeyValuePair.Create<string, object?>(PluginIdMetadataKey, pluginId)]);
        }
    }

    private static TomlValueSyntax? GetInlineTableValue(TomlValueSyntax value, string key)
    {
        if (value is TomlInlineTableSyntax table)
        {
            foreach (var property in table.Properties)
            {
                if (TomlUtilities.GetName(property.Key) == key)
                    return property.Value;
            }
        }

        return null;
    }

    private static Location CreateCatalogLocation(ScanFileContext context, TomlSyntaxTree tree, TomlStringSyntax value)
    {
        return CreateCatalogLocation(context, tree, value, 0, value.Value.Length);
    }

    /// <summary>Creates the location of a part of the value of a string.</summary>
    /// <remarks>An offset in the value is only an offset in the text of the string when the string has no escape sequences.</remarks>
    private static Location CreateCatalogLocation(ScanFileContext context, TomlSyntaxTree tree, TomlStringSyntax value, int start, int length)
    {
        var token = value.StringToken;
        var tokenText = token.Text;
        if (tokenText.Length < 2 || !tokenText.AsSpan(1, tokenText.Length - 2).SequenceEqual(value.Value))
            return new NonUpdatableLocation(context);

        var lineSpan = tree.GetLineSpan(new TextSpan(token.SpanStart + 1 + start, length));
        return new TextLocation(context.FileSystem, context.FullPath, lineSpan.Start.Line + 1, lineSpan.Start.Character + 1, length);
    }

    private sealed record CatalogVersionReference(string Name, string VersionReference, string? PluginId);
}
