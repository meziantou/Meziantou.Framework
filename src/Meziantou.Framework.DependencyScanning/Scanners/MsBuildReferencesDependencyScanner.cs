using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed partial class MsBuildReferencesDependencyScanner : DependencyScanner
{
    private static readonly XName IncludeXName = XName.Get("Include");
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
        return context.HasExtension([".csproj", ".props", ".targets"], ignoreCase: true);
    }

    [SuppressMessage("Security", "MA0009:Add regex evaluation timeout", Justification = "The regex has no backtracking")]
    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc is null || doc.Root is null)
            return;

        var ns = doc.Root.GetDefaultNamespace();
        var itemGroups = doc.Descendants(ns + "ItemGroup");
        foreach (var package in itemGroups.Elements(ns + "PackageReference").Concat(itemGroups.Elements(ns + "PackageDownload")).Concat(itemGroups.Elements(ns + "GlobalPackageReference")))
        {
            var nameAttribute = package.Attribute(IncludeXName);
            var nameValue = nameAttribute?.Value;
            if (string.IsNullOrEmpty(nameValue))
                continue;

            var reported = false;
            var versionAttribute = package.Attribute(VersionXName);
            var versionAttributeValue = versionAttribute?.Value;
            if (!string.IsNullOrEmpty(versionAttributeValue))
            {
                context.ReportDependency(this, nameValue, versionAttributeValue, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionAttribute));
                reported = true;
            }
            else
            {
                var versionElement = package.Element(ns + "Version");
                if (!string.IsNullOrEmpty(versionElement?.Value))
                {
                    context.ReportDependency(this, nameValue, versionElement.Value, DependencyType.NuGet,
                        nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                        versionLocation: new XmlLocation(context.FileSystem, context.FullPath, versionElement));
                    reported = true;
                }
            }

            var versionOverrideAttribute = package.Attribute(VersionOverrideXName);
            var versionOverrideAttributeValue = versionOverrideAttribute?.Value;
            if (!string.IsNullOrEmpty(versionOverrideAttributeValue))
            {
                context.ReportDependency(this, nameValue, versionOverrideAttributeValue, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionOverrideAttribute));
                reported = true;
            }
            else
            {
                var versionOverrideElement = package.Element(ns + "VersionOverride");
                if (!string.IsNullOrEmpty(versionOverrideElement?.Value))
                {
                    context.ReportDependency(this, nameValue, versionOverrideElement.Value, DependencyType.NuGet,
                        nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                        versionLocation: new XmlLocation(context.FileSystem, context.FullPath, versionOverrideElement));
                    reported = true;
                }
            }

            if (!reported)
            {
                context.ReportDependency(this, nameValue, version: null, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                    versionLocation: null);
            }
        }

        foreach (var package in itemGroups.Elements(ns + "PackageVersion"))
        {
            var packageNameAttr = package.Attribute(IncludeXName);
            var packageName = packageNameAttr?.Value;
            if (string.IsNullOrEmpty(packageName))
                continue;

            var versionAttribute = package.Attribute(VersionXName);
            var versionAttributeValue = versionAttribute?.Value;
            if (!string.IsNullOrEmpty(versionAttributeValue))
            {
                context.ReportDependency(this, packageName, versionAttributeValue, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, packageNameAttr),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionAttribute));
            }
            else
            {
                var versionElement = package.Element(ns + "Version");
                if (!string.IsNullOrEmpty(versionElement?.Value))
                {
                    context.ReportDependency(this, packageName, versionElement.Value, DependencyType.NuGet,
                        nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, packageNameAttr),
                        versionLocation: new XmlLocation(context.FileSystem, context.FullPath, versionElement));
                }
            }
        }

        foreach (var sdk in doc.Descendants(ns + "Sdk"))
        {
            var nameAttribute = sdk.Attribute(NameXName);
            var name = nameAttribute?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var versionAttribute = sdk.Attribute(VersionXName);
            var version = versionAttribute?.Value;
            if (string.IsNullOrEmpty(version))
                continue;

            context.ReportDependency(this, name, version, DependencyType.NuGet,
                nameLocation: new XmlLocation(context.FileSystem, context.FullPath, sdk, nameAttribute),
                versionLocation: new XmlLocation(context.FileSystem, context.FullPath, sdk, versionAttribute));
        }

        foreach (var sdk in doc.Descendants().Where(element => element.Name == ns + "Import" || element.Name == ns + "Project"))
        {
            var sdkAttribute = sdk.Attribute(SdkXName);
            var value = sdkAttribute?.Value;
            if (string.IsNullOrEmpty(value))
                continue;

            var index = value.IndexOf('/', StringComparison.Ordinal);
            if (index > 0)
            {
                var packageName = value[..index];
                var version = value[(index + 1)..];

                context.ReportDependency(this, packageName, version, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, sdk, sdkAttribute, column: 0, index),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, sdk, sdkAttribute, column: index + 1, value.Length - index - 1));
            }
        }

        foreach (var element in doc.Descendants(ns + "PropertyGroup"))
        {
            foreach (var targetFrameworkElement in element.Elements("TargetFrameworkVersion"))
            {
                context.ReportDependency(this, name: null, targetFrameworkElement.Value.Trim(), DependencyType.DotNetTargetFramework,
                    nameLocation: null,
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, targetFrameworkElement));
            }

            foreach (var targetFrameworkElement in element.Elements("TargetFramework"))
            {
                context.ReportDependency(this, name: null, targetFrameworkElement.Value.Trim(), DependencyType.DotNetTargetFramework,
                    nameLocation: null,
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, targetFrameworkElement));
            }

            foreach (var targetFrameworkElement in element.Elements("TargetFrameworks"))
            {
                foreach (Match match in TargetFrameworksRegex().Matches(targetFrameworkElement.Value))
                {
                    var group = match.Groups["version"];
                    context.ReportDependency(this, name: null, group.Value, DependencyType.DotNetTargetFramework,
                        nameLocation: null,
                        versionLocation: new XmlLocation(context.FileSystem, context.FullPath, targetFrameworkElement, group.Index, group.Length));
                }
            }
        }

        foreach (var projectReference in itemGroups.Elements(ns + "ProjectReference"))
        {
            var nameAttribute = projectReference.Attribute(IncludeXName);
            var nameValue = nameAttribute?.Value;
            if (string.IsNullOrEmpty(nameValue))
                continue;

            context.ReportDependency(this, nameValue, version: null, DependencyType.MSBuildProjectReference,
                nameLocation: new XmlLocation(context.FileSystem, context.FullPath, projectReference, nameAttribute),
                versionLocation: null);
        }
    }

    [GeneratedRegex(@"\s*(?<version>.+?)\s*(;|$)", RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: -1)]
    private static partial Regex TargetFrameworksRegex();
}
