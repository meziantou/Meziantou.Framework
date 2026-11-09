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

            // A property reference, such as ${junit.version}, must be updated where the property is defined
            if (version.Contains("${", StringComparison.Ordinal))
            {
                context.ReportDependency(this, name, version, properties.Resolve(version, GetMavenPropertyReferences(version)), DependencyType.JavaPackage, new NonUpdatableLocation(context), CreateMavenLocation(context, versionElement));
                continue;
            }

            context.ReportDependency(this, name, version, DependencyType.JavaPackage, new NonUpdatableLocation(context), CreateMavenLocation(context, versionElement));
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
    /// A property is only resolved when the project defines it once, outside any profile, as a profile can override it,
    /// and when its value does not reference another property.
    /// </remarks>
    private static VariableDefinitions GetMavenProperties(ScanFileContext context, XElement root, XNamespace ns)
    {
        var result = new VariableDefinitions(StringComparer.Ordinal);
        var definitions = root.Elements(ns + "properties").Elements()
            .Concat(root.Elements(ns + "profiles").Elements(ns + "profile").Elements(ns + "properties").Elements());

        foreach (var element in definitions)
        {
            if (element.Name.Namespace != ns)
                continue;

            var value = element.Parent?.Parent == root ? GetMavenValue(element) : null;
            if (value is not null && value.Contains("${", StringComparison.Ordinal))
            {
                value = null;
            }

            result.Add(element.Name.LocalName, value, () => CreateMavenLocation(context, element));
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

    /// <summary>Gets the property references of a value, such as <c>${junit.version}</c>.</summary>
    private static List<VariableReference> GetMavenPropertyReferences(string value)
    {
        var result = new List<VariableReference>();
        var index = 0;
        while ((index = value.IndexOf("${", index, StringComparison.Ordinal)) >= 0)
        {
            var end = value.IndexOf('}', index + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                result.Add(new VariableReference(index, value.Length - index, Name: null));
                break;
            }

            var name = value[(index + 2)..end];
            var isName = name.Length > 0 && !name.Contains('{', StringComparison.Ordinal);
            result.Add(new VariableReference(index, end + 1 - index, isName ? name : null));
            index = end + 1;
        }

        return result;
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
