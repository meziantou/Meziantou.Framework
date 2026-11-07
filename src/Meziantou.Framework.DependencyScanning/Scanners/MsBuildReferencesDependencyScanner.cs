using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans MSBuild project files (.csproj, .fsproj, .vbproj, .proj, and any other *.*proj file, .props, .targets) for NuGet package references, target frameworks, and project references.</summary>
public sealed class MsBuildReferencesDependencyScanner : DependencyScanner
{
    private static readonly XName IncludeXName = XName.Get("Include");
    private static readonly XName UpdateXName = XName.Get("Update");
    private static readonly XName VersionXName = XName.Get("Version");
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
        if (context.HasExtension([".props", ".targets"], ignoreCase: true))
            return true;

        // Every MSBuild project type uses a *.*proj extension (.csproj, .vcxproj, .sqlproj, .wixproj, .esproj, ...).
        // Xcode (.pbxproj) and Visual Studio Installer (.vdproj) projects share the suffix, but they are not MSBuild files.
        return Path.GetExtension(context.FileName).EndsWith("proj", StringComparison.OrdinalIgnoreCase)
            && !context.HasExtension([".pbxproj", ".vdproj"], ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc is null || doc.Root is null)
            return;

        // MSBuild keywords (Project, ItemGroup, PropertyGroup, Include, Sdk, ...) are case-sensitive, but item types, metadata names and property names are not
        var ns = doc.Root.GetDefaultNamespace();
        var items = doc.Descendants(ns + "ItemGroup").Elements().Where(element => element.Name.Namespace == ns).ToArray();
        foreach (var package in items.Where(item => HasName(item, "PackageReference") || HasName(item, "PackageDownload") || HasName(item, "GlobalPackageReference")))
        {
            var names = GetItemNames(context, package);
            if (names.Count == 0)
                continue;

            var reported = false;
            if (TryGetMetadata(context, ns, package, "Version", names.Count, out var version, out var versionLocation))
            {
                ReportPackages(context, names, version, versionLocation);
                reported = true;
            }

            if (TryGetMetadata(context, ns, package, "VersionOverride", names.Count, out var versionOverride, out var versionOverrideLocation))
            {
                ReportPackages(context, names, versionOverride, versionOverrideLocation);
                reported = true;
            }

            if (!reported)
            {
                ReportPackages(context, names, version: null, versionLocation: null);
            }
        }

        foreach (var package in items.Where(item => HasName(item, "PackageVersion")))
        {
            var names = GetItemNames(context, package);
            if (names.Count == 0)
                continue;

            if (TryGetMetadata(context, ns, package, "Version", names.Count, out var version, out var versionLocation))
            {
                ReportPackages(context, names, version, versionLocation);
            }
        }

        foreach (var sdk in doc.Descendants(ns + "Sdk"))
        {
            if (!TryGetTrimmedValue(context, sdk, sdk.Attribute(NameXName), out var name, out var nameLocation))
                continue;

            // An SDK that only declares a MinimumVersion has no exact version to report
            if (!TryGetTrimmedValue(context, sdk, sdk.Attribute(VersionXName), out var version, out var versionLocation))
                continue;

            context.ReportDependency(this, name, version, DependencyType.NuGet, nameLocation, versionLocation);
        }

        foreach (var element in doc.Descendants().Where(element => element.Name == ns + "Import" || element.Name == ns + "Project"))
        {
            var sdkAttribute = element.Attribute(SdkXName);
            if (sdkAttribute is null)
                continue;

            ReportSdkAttribute(context, ns, element, sdkAttribute);
        }

        foreach (var element in doc.Descendants(ns + "PropertyGroup").Elements().Where(element => element.Name.Namespace == ns))
        {
            if (HasName(element, "TargetFrameworkVersion") || HasName(element, "TargetFramework"))
            {
                ReportTargetFrameworks(context, element, isList: false);
            }
            else if (HasName(element, "TargetFrameworks"))
            {
                ReportTargetFrameworks(context, element, isList: true);
            }
        }

        foreach (var projectReference in items.Where(item => HasName(item, "ProjectReference")))
        {
            var includeAttribute = projectReference.Attribute(IncludeXName);
            if (includeAttribute is null)
                continue;

            foreach (var (name, nameLocation) in GetListEntries(context, projectReference, includeAttribute))
            {
                context.ReportDependency(this, name, version: null, DependencyType.MSBuildProjectReference, nameLocation, versionLocation: null);
            }
        }
    }

    private void ReportPackages(ScanFileContext context, List<(string Name, Location Location)> names, string? version, Location? versionLocation)
    {
        foreach (var (name, nameLocation) in names)
        {
            context.ReportDependency(this, name, version, DependencyType.NuGet, nameLocation, versionLocation);
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

                if (!TryGetTrimmedValue(context, element, element.Attribute(VersionXName), out var version, out var versionLocation))
                    continue;

                var name = value.Substring(entryStart, entryLength);
                context.ReportDependency(this, name, version, DependencyType.NuGet,
                    nameLocation: CreateLocation(context, name, element, sdkAttribute, entryStart, entryLength, value.Length),
                    versionLocation);
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

            var nameValue = value.Substring(nameStart, nameLength);
            context.ReportDependency(this, nameValue, versionValue, DependencyType.NuGet,
                nameLocation: IsMsBuildExpression(nameValue)
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

    private static bool HasName(XElement element, string localName) => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the items declared by the Include attribute, or by the Update attribute when there is no Include. MSBuild splits both on ';' and trims each entry.</summary>
    private static List<(string Name, Location Location)> GetItemNames(ScanFileContext context, XElement element)
    {
        var includeAttribute = element.Attribute(IncludeXName);
        if (includeAttribute is not null && !string.IsNullOrWhiteSpace(includeAttribute.Value))
            return GetListEntries(context, element, includeAttribute);

        var updateAttribute = element.Attribute(UpdateXName);
        if (updateAttribute is not null && !string.IsNullOrWhiteSpace(updateAttribute.Value))
            return GetListEntries(context, element, updateAttribute);

        return [];
    }

    private static List<(string Name, Location Location)> GetListEntries(ScanFileContext context, XElement element, XAttribute attribute)
    {
        var value = attribute.Value;
        var result = new List<(string Name, Location Location)>();
        foreach (var (start, length) in SplitList(value))
        {
            var entry = value.Substring(start, length);
            result.Add((entry, CreateLocation(context, entry, element, attribute, start, length, value.Length)));
        }

        return result;
    }

    /// <summary>
    /// Gets the value of a metadata declared as an attribute or as a child element, trimmed as NuGet does.
    /// The location is not updatable when the item declares several packages, because they all share the value.
    /// </summary>
    private static bool TryGetMetadata(ScanFileContext context, XNamespace ns, XElement item, string metadataName, int itemCount, [NotNullWhen(true)] out string? value, [NotNullWhen(true)] out Location? location)
    {
        var attribute = item.Attributes().FirstOrDefault(attribute => attribute.Name.Namespace == XNamespace.None && attribute.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase));
        var element = item.Elements().FirstOrDefault(element => element.Name.Namespace == ns && HasName(element, metadataName));
        var found = TryGetTrimmedValue(context, item, attribute, out value, out location)
            || (element is not null && TryGetTrimmedValue(context, element, attribute: null, element.Value, out value, out location));

        if (found && itemCount > 1)
        {
            location = new NonUpdatableLocation(context);
        }

        return found;
    }

    private static bool TryGetTrimmedValue(ScanFileContext context, XElement element, XAttribute? attribute, [NotNullWhen(true)] out string? value, [NotNullWhen(true)] out Location? location)
    {
        if (attribute is null)
        {
            value = null;
            location = null;
            return false;
        }

        return TryGetTrimmedValue(context, element, attribute, attribute.Value, out value, out location);
    }

    /// <summary>Gets the trimmed value of <paramref name="attribute"/>, or of <paramref name="element"/> when <paramref name="attribute"/> is <see langword="null"/>.</summary>
    private static bool TryGetTrimmedValue(ScanFileContext context, XElement element, XAttribute? attribute, string rawValue, [NotNullWhen(true)] out string? value, [NotNullWhen(true)] out Location? location)
    {
        var (start, length) = Trim(rawValue, 0, rawValue.Length);
        if (length == 0)
        {
            value = null;
            location = null;
            return false;
        }

        value = rawValue.Substring(start, length);
        location = CreateLocation(context, value, element, attribute, start, length, rawValue.Length);
        return true;
    }

    private static Location CreateLocation(ScanFileContext context, string value, XElement element, XAttribute? attribute, int start, int length, int rawValueLength)
    {
        // Updating a property, item or metadata reference would replace the indirection with a literal value
        if (IsMsBuildExpression(value))
            return new NonUpdatableLocation(context);

        if (start == 0 && length == rawValueLength)
            return new XmlLocation(context.FileSystem, context.FullPath, element, attribute);

        return new XmlLocation(context.FileSystem, context.FullPath, element, attribute, start, length);
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
