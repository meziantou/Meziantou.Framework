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
        return context.FileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || context.FileName.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || context.FileName.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc == null || doc.Root == null)
            return;

        var ns = doc.Root.GetDefaultNamespace();
        foreach (var package in doc.Descendants(ns + "PackageReference"))
        {
            var packageName = package.Attribute(IncludeXName)?.Value;
            if (string.IsNullOrEmpty(packageName))
                continue;

            var versionAttribute = package.Attribute(VersionXName)?.Value;
            if (!string.IsNullOrEmpty(versionAttribute))
            {
                await context.ReportDependency(new Dependency(packageName, versionAttribute, DependencyType.NuGet, new XmlLocation(context.FullPath, package, VersionXName.LocalName))).ConfigureAwait(false);
            }
            else
            {
                var versionElement = package.Element(ns + "Version");
                if (!string.IsNullOrEmpty(versionElement?.Value))
                {
                    await context.ReportDependency(new Dependency(packageName, versionElement.Value, DependencyType.NuGet, new XmlLocation(context.FullPath, versionElement))).ConfigureAwait(false);
                }
            }

            var versionOverrideAttribute = package.Attribute(VersionOverrideXName)?.Value;
            if (!string.IsNullOrEmpty(versionOverrideAttribute))
            {
                await context.ReportDependency(new Dependency(packageName, versionOverrideAttribute, DependencyType.NuGet, new XmlLocation(context.FullPath, package, VersionOverrideXName.LocalName))).ConfigureAwait(false);
            }
            else
            {
                var versionOverrideElement = package.Element(ns + "VersionOverride");
                if (!string.IsNullOrEmpty(versionOverrideElement?.Value))
                {
                    await context.ReportDependency(new Dependency(packageName, versionOverrideElement.Value, DependencyType.NuGet, new XmlLocation(context.FullPath, versionOverrideElement))).ConfigureAwait(false);
                }
            }
        }

        foreach (var package in doc.Descendants(ns + "PackageVersion"))
        {
            var packageName = package.Attribute(IncludeXName)?.Value;
            if (string.IsNullOrEmpty(packageName))
                continue;

            var versionAttribute = package.Attribute(VersionXName)?.Value;
            if (!string.IsNullOrEmpty(versionAttribute))
            {
                await context.ReportDependency(new Dependency(packageName, versionAttribute, DependencyType.NuGet, new XmlLocation(context.FullPath, package, VersionXName.LocalName))).ConfigureAwait(false);
            }
            else
            {
                var versionElement = package.Element(ns + "Version");
                if (!string.IsNullOrEmpty(versionElement?.Value))
                {
                    await context.ReportDependency(new Dependency(packageName, versionElement.Value, DependencyType.NuGet, new XmlLocation(context.FullPath, versionElement))).ConfigureAwait(false);
                }
            }
        }

        foreach (var sdk in doc.Descendants(ns + "Sdk"))
        {
            var name = sdk.Attribute(NameXName)?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var version = sdk.Attribute(VersionXName)?.Value;
            if (string.IsNullOrEmpty(version))
                continue;

            await context.ReportDependency(new Dependency(name, version, DependencyType.NuGet, new XmlLocation(context.FullPath, sdk, VersionXName.LocalName))).ConfigureAwait(false);
        }

        foreach (var sdk in doc.Descendants().Where(element => element.Name == ns + "Import" || element.Name == ns + "Project"))
        {
            var value = sdk.Attribute(SdkXName)?.Value;
            if (string.IsNullOrEmpty(value))
                continue;

            var index = value.IndexOf('/', StringComparison.Ordinal);
            if (index > 0)
            {
                var packageName = value[..index];
                var version = value[(index + 1)..];

                await context.ReportDependency(new Dependency(packageName, version, DependencyType.NuGet, new XmlLocation(context.FullPath, sdk, SdkXName.LocalName, column: index + 1, value.Length - index - 1))).ConfigureAwait(false);
            }
        }
    }
}
