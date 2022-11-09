using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class MsBuildReferencesDependencyScanner : DependencyScanner
{
    private static readonly XName IncludeXName = XName.Get("Include");
    private static readonly XName VersionXName = XName.Get("Version");
    private static readonly XName VersionOverrideXName = XName.Get("VersionOverride");
    private static readonly XName SdkXName = XName.Get("Sdk");
    private static readonly XName NameXName = XName.Get("Name");

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasExtension(".csproj", ignoreCase: true)
            || context.HasExtension(".props", ignoreCase: true)
            || context.HasExtension(".targets", ignoreCase: true);
    }

    [SuppressMessage("Security", "MA0009:Add regex evaluation timeout", Justification = "The regex has no backtracking")]
    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc == null || doc.Root == null)
            return;

        var ns = doc.Root.GetDefaultNamespace();
        var itemGroups = doc.Descendants(ns + "ItemGroup");
        foreach (var package in itemGroups.Elements(ns + "PackageReference").Concat(itemGroups.Elements(ns + "PackageDownload")).Concat(itemGroups.Elements(ns + "GlobalPackageReference")))
        {
            var nameAttribute = package.Attribute(IncludeXName);
            var nameValue = nameAttribute?.Value;
            if (string.IsNullOrEmpty(nameValue))
                continue;

            var versionAttribute = package.Attribute(VersionXName);
            var versionAttributeValue = versionAttribute?.Value;
            if (!string.IsNullOrEmpty(versionAttributeValue))
            {
                context.ReportDependency(new Dependency(nameValue, versionAttributeValue, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionAttribute)));
            }
            else
            {
                var versionElement = package.Element(ns + "Version");
                if (!string.IsNullOrEmpty(versionElement?.Value))
                {
                    context.ReportDependency(new Dependency(nameValue, versionElement.Value, DependencyType.NuGet,
                        nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                        versionLocation: new XmlLocation(context.FileSystem, context.FullPath, versionElement)));
                }
            }

            var versionOverrideAttribute = package.Attribute(VersionOverrideXName);
            var versionOverrideAttributeValue = versionOverrideAttribute?.Value;
            if (!string.IsNullOrEmpty(versionOverrideAttributeValue))
            {
                context.ReportDependency(new Dependency(nameValue, versionOverrideAttributeValue, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionOverrideAttribute)));
            }
            else
            {
                var versionOverrideElement = package.Element(ns + "VersionOverride");
                if (!string.IsNullOrEmpty(versionOverrideElement?.Value))
                {
                    context.ReportDependency(new Dependency(nameValue, versionOverrideElement.Value, DependencyType.NuGet,
                        nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, nameAttribute),
                        versionLocation: new XmlLocation(context.FileSystem, context.FullPath, versionOverrideElement)));
                }
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
                context.ReportDependency(new Dependency(packageName, versionAttributeValue, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, packageNameAttr),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionAttribute)));
            }
            else
            {
                var versionElement = package.Element(ns + "Version");
                if (!string.IsNullOrEmpty(versionElement?.Value))
                {
                    context.ReportDependency(new Dependency(packageName, versionElement.Value, DependencyType.NuGet,
                        nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, packageNameAttr),
                        versionLocation: new XmlLocation(context.FileSystem, context.FullPath, versionElement)));
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

            context.ReportDependency(new Dependency(name, version, DependencyType.NuGet,
                nameLocation: new XmlLocation(context.FileSystem, context.FullPath, sdk, nameAttribute),
                versionLocation: new XmlLocation(context.FileSystem, context.FullPath, sdk, versionAttribute)));
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

                context.ReportDependency(new Dependency(packageName, version, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, sdk, sdkAttribute, column: 0, index),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, sdk, sdkAttribute, column: index + 1, value.Length - index - 1)));
            }
        }

        foreach (var element in doc.Descendants(ns + "PropertyGroup"))
        {
            foreach (var targetFrameworkElement in element.Elements("TargetFrameworkVersion"))
            {
                context.ReportDependency(new Dependency(name: null, targetFrameworkElement.Value.Trim(), DependencyType.DotNetTargetFramework,
                    nameLocation: null,
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, targetFrameworkElement)));
            }

            foreach (var targetFrameworkElement in element.Elements("TargetFramework"))
            {
                context.ReportDependency(new Dependency(name: null, targetFrameworkElement.Value.Trim(), DependencyType.DotNetTargetFramework,
                    nameLocation: null,
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, targetFrameworkElement)));
            }

            foreach (var targetFrameworkElement in element.Elements("TargetFrameworks"))
            {
#if NET7_0_OR_GREATER
                const RegexOptions Options = RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking;
#else
                const RegexOptions Options = RegexOptions.ExplicitCapture;
#endif
                foreach (Match match in Regex.Matches(targetFrameworkElement.Value, @"\s*(?<version>.+?)\s*(;|$)", Options))
                {
                    var group = match.Groups["version"];
                    context.ReportDependency(new Dependency(name: null, group.Value, DependencyType.DotNetTargetFramework,
                        nameLocation: null,
                        versionLocation: new XmlLocation(context.FileSystem, context.FullPath, targetFrameworkElement, group.Index, group.Length)));
                }
            }
        }
    }
}
