using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans MSBuild project files (.csproj, .fsproj, .vbproj, .proj, .props, .targets) for NuGet package references, target frameworks, and project references.</summary>
public sealed class MsBuildReferencesDependencyScanner : DependencyScanner
{
    private static readonly XName IncludeXName = XName.Get("Include");
    private static readonly XName UpdateXName = XName.Get("Update");
    private static readonly XName VersionXName = XName.Get("Version");
    private static readonly XName VersionOverrideXName = XName.Get("VersionOverride");
    private static readonly XName SdkXName = XName.Get("Sdk");
    private static readonly XName NameXName = XName.Get("Name");

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } =
        [
            DependencyType.NuGet,
            DependencyType.DotNetTargetFramework,
            DependencyType.MSBuildProjectReference,
        ];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasExtension([".csproj", ".fsproj", ".vbproj", ".proj", ".props", ".targets"], ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc is null || doc.Root is null)
            return;

        var ns = doc.Root.GetDefaultNamespace();
        var itemGroups = doc.Descendants(ns + "ItemGroup");
        foreach (var package in itemGroups.Elements(ns + "PackageReference").Concat(itemGroups.Elements(ns + "PackageDownload")).Concat(itemGroups.Elements(ns + "GlobalPackageReference")))
        {
            var nameAttribute = GetItemNameAttribute(package);
            if (nameAttribute is null)
                continue;

            var nameValue = nameAttribute.Value;
            var reported = false;
            var versionAttribute = package.Attribute(VersionXName);
            var versionAttributeValue = versionAttribute?.Value;
            if (!string.IsNullOrEmpty(versionAttributeValue))
            {
                context.ReportDependency(this, nameValue, versionAttributeValue, DependencyType.NuGet,
                    nameLocation: CreateLocation(context, nameValue, package, nameAttribute),
                    versionLocation: CreateLocation(context, versionAttributeValue, package, versionAttribute));
                reported = true;
            }
            else
            {
                var versionElement = package.Element(ns + "Version");
                if (!string.IsNullOrEmpty(versionElement?.Value))
                {
                    context.ReportDependency(this, nameValue, versionElement.Value, DependencyType.NuGet,
                        nameLocation: CreateLocation(context, nameValue, package, nameAttribute),
                        versionLocation: CreateLocation(context, versionElement.Value, versionElement, attribute: null));
                    reported = true;
                }
            }

            var versionOverrideAttribute = package.Attribute(VersionOverrideXName);
            var versionOverrideAttributeValue = versionOverrideAttribute?.Value;
            if (!string.IsNullOrEmpty(versionOverrideAttributeValue))
            {
                context.ReportDependency(this, nameValue, versionOverrideAttributeValue, DependencyType.NuGet,
                    nameLocation: CreateLocation(context, nameValue, package, nameAttribute),
                    versionLocation: CreateLocation(context, versionOverrideAttributeValue, package, versionOverrideAttribute));
                reported = true;
            }
            else
            {
                var versionOverrideElement = package.Element(ns + "VersionOverride");
                if (!string.IsNullOrEmpty(versionOverrideElement?.Value))
                {
                    context.ReportDependency(this, nameValue, versionOverrideElement.Value, DependencyType.NuGet,
                        nameLocation: CreateLocation(context, nameValue, package, nameAttribute),
                        versionLocation: CreateLocation(context, versionOverrideElement.Value, versionOverrideElement, attribute: null));
                    reported = true;
                }
            }

            if (!reported)
            {
                context.ReportDependency(this, nameValue, version: null, DependencyType.NuGet,
                    nameLocation: CreateLocation(context, nameValue, package, nameAttribute),
                    versionLocation: null);
            }
        }

        foreach (var package in itemGroups.Elements(ns + "PackageVersion"))
        {
            var packageNameAttr = GetItemNameAttribute(package);
            if (packageNameAttr is null)
                continue;

            var packageName = packageNameAttr.Value;
            var versionAttribute = package.Attribute(VersionXName);
            var versionAttributeValue = versionAttribute?.Value;
            if (!string.IsNullOrEmpty(versionAttributeValue))
            {
                context.ReportDependency(this, packageName, versionAttributeValue, DependencyType.NuGet,
                    nameLocation: CreateLocation(context, packageName, package, packageNameAttr),
                    versionLocation: CreateLocation(context, versionAttributeValue, package, versionAttribute));
            }
            else
            {
                var versionElement = package.Element(ns + "Version");
                if (!string.IsNullOrEmpty(versionElement?.Value))
                {
                    context.ReportDependency(this, packageName, versionElement.Value, DependencyType.NuGet,
                        nameLocation: CreateLocation(context, packageName, package, packageNameAttr),
                        versionLocation: CreateLocation(context, versionElement.Value, versionElement, attribute: null));
                }
            }
        }

        foreach (var sdk in doc.Descendants(ns + "Sdk"))
        {
            var nameAttribute = sdk.Attribute(NameXName);
            var name = nameAttribute?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            // An SDK that only declares a MinimumVersion has no exact version to report
            var versionAttribute = sdk.Attribute(VersionXName);
            var version = versionAttribute?.Value;
            if (string.IsNullOrEmpty(version))
                continue;

            context.ReportDependency(this, name, version, DependencyType.NuGet,
                nameLocation: CreateLocation(context, name, sdk, nameAttribute),
                versionLocation: CreateLocation(context, version, sdk, versionAttribute));
        }

        foreach (var element in doc.Descendants().Where(element => element.Name == ns + "Import" || element.Name == ns + "Project"))
        {
            var sdkAttribute = element.Attribute(SdkXName);
            if (sdkAttribute is null)
                continue;

            ReportSdkAttribute(context, ns, element, sdkAttribute);
        }

        foreach (var element in doc.Descendants(ns + "PropertyGroup"))
        {
            foreach (var targetFrameworkElement in element.Elements(ns + "TargetFrameworkVersion"))
            {
                ReportTargetFrameworks(context, targetFrameworkElement, isList: false);
            }

            foreach (var targetFrameworkElement in element.Elements(ns + "TargetFramework"))
            {
                ReportTargetFrameworks(context, targetFrameworkElement, isList: false);
            }

            foreach (var targetFrameworkElement in element.Elements(ns + "TargetFrameworks"))
            {
                ReportTargetFrameworks(context, targetFrameworkElement, isList: true);
            }
        }

        foreach (var projectReference in itemGroups.Elements(ns + "ProjectReference"))
        {
            var nameAttribute = projectReference.Attribute(IncludeXName);
            var nameValue = nameAttribute?.Value;
            if (string.IsNullOrEmpty(nameValue))
                continue;

            context.ReportDependency(this, nameValue, version: null, DependencyType.MSBuildProjectReference,
                nameLocation: CreateLocation(context, nameValue, projectReference, nameAttribute),
                versionLocation: null);
        }
    }

    private void ReportSdkAttribute(ScanFileContext context, XNamespace ns, XElement element, XAttribute sdkAttribute)
    {
        // MSBuild splits the attribute on ';' and each entry is "Name", "Name/Version" or "Name/min=Version"
        var value = sdkAttribute.Value;
        var entries = SplitList(value);
        foreach (var (entryStart, entryLength) in entries)
        {
            var separatorOffset = value.AsSpan(entryStart, entryLength).IndexOf('/');
            if (separatorOffset < 0)
            {
                // <Import Sdk="Name" Version="1.0.0" />
                if (entries.Count != 1 || element.Name != ns + "Import")
                    continue;

                var versionAttribute = element.Attribute(VersionXName);
                var version = versionAttribute?.Value;
                if (string.IsNullOrEmpty(version))
                    continue;

                context.ReportDependency(this, value.Substring(entryStart, entryLength), version, DependencyType.NuGet,
                    nameLocation: IsMsBuildExpression(value.Substring(entryStart, entryLength))
                        ? new NonUpdatableLocation(context)
                        : new XmlLocation(context.FileSystem, context.FullPath, element, sdkAttribute, entryStart, entryLength),
                    versionLocation: CreateLocation(context, version, element, versionAttribute));
                continue;
            }

            var separatorIndex = entryStart + separatorOffset;
            var (nameStart, nameLength) = Trim(value, entryStart, separatorOffset);
            var (versionStart, versionLength) = Trim(value, separatorIndex + 1, entryStart + entryLength - separatorIndex - 1);
            if (nameLength == 0 || versionLength == 0)
                continue;

            var versionValue = value.Substring(versionStart, versionLength);

            // "Name/min=Version" is a minimum version, not an exact version, and "A/B/C" is not a valid SDK reference
            if (versionValue.StartsWith("min=", StringComparison.OrdinalIgnoreCase) || versionValue.Contains('/', StringComparison.Ordinal))
                continue;

            context.ReportDependency(this, value.Substring(nameStart, nameLength), versionValue, DependencyType.NuGet,
                nameLocation: IsMsBuildExpression(value.Substring(nameStart, nameLength))
                    ? new NonUpdatableLocation(context)
                    : new XmlLocation(context.FileSystem, context.FullPath, element, sdkAttribute, nameStart, nameLength),
                versionLocation: IsMsBuildExpression(versionValue)
                    ? new NonUpdatableLocation(context)
                    : new XmlLocation(context.FileSystem, context.FullPath, element, sdkAttribute, versionStart, versionLength));
        }
    }

    private void ReportTargetFrameworks(ScanFileContext context, XElement element, bool isList)
    {
        var value = element.Value;
        var entries = isList ? SplitList(value) : [Trim(value, 0, value.Length)];
        foreach (var (start, length) in entries)
        {
            if (length == 0)
                continue;

            var targetFramework = value.Substring(start, length);
            context.ReportDependency(this, name: null, targetFramework, DependencyType.DotNetTargetFramework,
                nameLocation: null,
                versionLocation: IsMsBuildExpression(targetFramework)
                    ? new NonUpdatableLocation(context)
                    : new XmlLocation(context.FileSystem, context.FullPath, element, start, length));
        }
    }

    private static XAttribute? GetItemNameAttribute(XElement element)
    {
        var includeAttribute = element.Attribute(IncludeXName);
        if (!string.IsNullOrEmpty(includeAttribute?.Value))
            return includeAttribute;

        var updateAttribute = element.Attribute(UpdateXName);
        if (!string.IsNullOrEmpty(updateAttribute?.Value))
            return updateAttribute;

        return null;
    }

    private static Location CreateLocation(ScanFileContext context, string value, XElement element, XAttribute? attribute)
    {
        // Updating a property, item or metadata reference would replace the indirection with a literal value
        if (IsMsBuildExpression(value))
            return new NonUpdatableLocation(context);

        return new XmlLocation(context.FileSystem, context.FullPath, element, attribute);
    }

    private static bool IsMsBuildExpression(string value)
    {
        return value.Contains("$(", StringComparison.Ordinal)
            || value.Contains("@(", StringComparison.Ordinal)
            || value.Contains("%(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Splits a ';'-separated MSBuild list and returns the trimmed, non-empty entries as offsets into <paramref name="value"/>.
    /// Separators nested in parentheses, such as in property functions, do not split the list.
    /// </summary>
    private static List<(int Start, int Length)> SplitList(string value)
    {
        var result = new List<(int Start, int Length)>();
        var depth = 0;
        var entryStart = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length)
            {
                var c = value[i];
                if (c is '(')
                {
                    depth++;
                    continue;
                }

                if (c is ')')
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }

                if (c is not ';' || depth > 0)
                    continue;
            }

            var entry = Trim(value, entryStart, i - entryStart);
            if (entry.Length > 0)
            {
                result.Add(entry);
            }

            entryStart = i + 1;
        }

        return result;
    }

    private static (int Start, int Length) Trim(string value, int start, int length)
    {
        var end = start + length;
        while (start < end && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return (start, end - start);
    }
}
