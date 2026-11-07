using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed partial class JavaDependencyScanner
{
    // Maven uses this group when a plugin does not specify one
    private const string DefaultMavenPluginGroupId = "org.apache.maven.plugins";

    // The elements that can contain the coordinates of a dependency, a plugin, an extension or the parent. Anything else,
    // such as a plugin <configuration> or a dependency <exclusions>, holds coordinates that are not dependencies.
    private static readonly HashSet<string> MavenStructuralElementNames = new(StringComparer.Ordinal)
    {
        "project",
        "profiles",
        "profile",
        "dependencyManagement",
        "dependencies",
        "build",
        "pluginManagement",
        "plugins",
        "plugin",
        "extensions",
        "reporting",
    };

    private async ValueTask ScanMavenAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc?.Root is not { } root || root.Name.LocalName is not "project")
            return;

        var ns = root.Name.Namespace;
        var properties = GetMavenProperties(context, root, ns);
        foreach (var element in root.Descendants())
        {
            if (!IsMavenCoordinateElement(root, element, ns))
                continue;

            var artifactId = GetMavenValue(element.Element(ns + "artifactId"));
            if (artifactId is null)
                continue;

            var groupId = GetMavenValue(element.Element(ns + "groupId"));
            if (groupId is null)
            {
                if (element.Name.LocalName is not "plugin")
                    continue;

                groupId = DefaultMavenPluginGroupId;
            }

            var name = groupId + ":" + artifactId;
            var versionElement = element.Element(ns + "version");
            var version = GetMavenValue(versionElement);
            if (versionElement is null || version is null)
            {
                // The version is managed elsewhere, such as by a BOM imported in <dependencyManagement> or by the parent
                context.ReportDependency(this, name, version: null, DependencyType.JavaPackage, new NonUpdatableLocation(context), versionLocation: null);
                continue;
            }

            if (GetMavenPropertyReference(version) is { } propertyName && properties.TryGetValue(propertyName, out var property))
            {
                context.ReportDependency(this, name, property.Value, DependencyType.JavaPackage,
                    new NonUpdatableLocation(context), property.Location,
                    tags: [], metadata: [KeyValuePair.Create<string, object?>("property", propertyName)]);
                continue;
            }

            // A property reference, such as ${junit.version}, must be updated where the property is defined
            var versionLocation = version.Contains("${", StringComparison.Ordinal)
                ? new NonUpdatableLocation(context)
                : CreateMavenLocation(context, versionElement);
            context.ReportDependency(this, name, version, DependencyType.JavaPackage, new NonUpdatableLocation(context), versionLocation);
        }
    }

    private static bool IsMavenCoordinateElement(XElement root, XElement element, XNamespace ns)
    {
        if (element.Name.Namespace != ns || element.Parent is not { } parent)
            return false;

        var isCoordinateElement = (element.Name.LocalName, parent.Name.LocalName) switch
        {
            ("parent", _) => parent == root,
            ("dependency", "dependencies") => true,
            ("plugin", "plugins") => true,
            ("extension", "extensions") => true,
            _ => false,
        };

        if (!isCoordinateElement)
            return false;

        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Name.Namespace != ns || !MavenStructuralElementNames.Contains(ancestor.Name.LocalName))
                return false;
        }

        return true;
    }

    /// <summary>Gets the properties of the project that a version can be resolved from, with the location to update them.</summary>
    /// <remarks>
    /// A property is only resolved when the project defines it once, outside any profile, as a profile can override it.
    /// It is updatable only when the pom references it once, so that updating one dependency does not change another one.
    /// </remarks>
    private static Dictionary<string, (string Value, Location Location)> GetMavenProperties(ScanFileContext context, XElement root, XNamespace ns)
    {
        var result = new Dictionary<string, (string Value, Location Location)>(StringComparer.Ordinal);
        var definitions = root.Elements(ns + "properties").Elements()
            .Concat(root.Elements(ns + "profiles").Elements(ns + "profile").Elements(ns + "properties").Elements())
            .GroupBy(element => element.Name);

        Dictionary<string, int>? referenceCounts = null;
        foreach (var group in definitions)
        {
            if (group.Key.Namespace != ns)
                continue;

            var element = group.First();
            if (element.Parent?.Parent != root || group.Skip(1).Any())
                continue;

            var value = GetMavenValue(element);
            if (value is null || value.Contains("${", StringComparison.Ordinal))
                continue;

            referenceCounts ??= CountMavenPropertyReferences(root);
            var propertyName = element.Name.LocalName;
            var isReferencedOnce = referenceCounts.TryGetValue(propertyName, out var count) && count == 1;
            result[propertyName] = (value, isReferencedOnce ? CreateMavenLocation(context, element) : new NonUpdatableLocation(context));
        }

        return result;
    }

    private static Location CreateMavenLocation(ScanFileContext context, XElement element)
    {
        // The value of an element holding a comment or a CDATA section does not map onto a single run of text
        if (element.Nodes().Any(node => node.NodeType is not XmlNodeType.Text))
            return new NonUpdatableLocation(context);

        // Maven trims the value, so the location covers the trimmed value only
        var value = element.Value;
        var trimmedStart = value.Length - value.AsSpan().TrimStart().Length;
        var trimmedLength = value.AsSpan().Trim().Length;
        return new XmlLocation(context.FileSystem, context.FullPath, element, trimmedStart, trimmedLength);
    }

    private static Dictionary<string, int> CountMavenPropertyReferences(XElement root)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case XText text:
                    Count(text.Value);
                    break;

                case XElement element:
                    foreach (var attribute in element.Attributes())
                    {
                        Count(attribute.Value);
                    }

                    break;
            }
        }

        foreach (var attribute in root.Attributes())
        {
            Count(attribute.Value);
        }

        return result;

        void Count(string text)
        {
            var index = 0;
            while ((index = text.IndexOf("${", index, StringComparison.Ordinal)) >= 0)
            {
                var end = text.IndexOf('}', index + 2, StringComparison.Ordinal);
                if (end < 0)
                    return;

                var name = text[(index + 2)..end];
                result[name] = result.GetValueOrDefault(name) + 1;
                index = end + 1;
            }
        }
    }

    /// <summary>Gets the value of <c>${name}</c>, or <see langword="null"/> when the version is not exactly a property reference.</summary>
    private static string? GetMavenPropertyReference(string version)
    {
        if (version is ['$', '{', .. var name, '}'] && name.Length > 0 && !name.Contains('{', StringComparison.Ordinal) && !name.Contains('}', StringComparison.Ordinal))
            return name;

        return null;
    }

    /// <summary>Gets the value of an element as Maven reads it, trimmed, or <see langword="null"/> when it is missing or empty.</summary>
    private static string? GetMavenValue(XElement? element)
    {
        if (element is null || element.HasElements)
            return null;

        var value = element.Value.Trim();
        return value.Length == 0 ? null : value;
    }
}
