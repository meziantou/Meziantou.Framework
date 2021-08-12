using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class MsBuildReferencesDependencyScanner : DependencyScanner
{
    private static readonly XName s_includeName = XName.Get("Include");
    private static readonly XName s_versionName = XName.Get("Version");
    private static readonly XName s_sdkName = XName.Get("Sdk");
    private static readonly XName s_nameName = XName.Get("Name");

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
            var packageName = package.Attribute(s_includeName)?.Value;
            if (string.IsNullOrEmpty(packageName))
                continue;

            var versionAttribute = package.Attribute(s_versionName)?.Value;
            if (!string.IsNullOrEmpty(versionAttribute))
            {
                await context.ReportDependency(new Dependency(packageName, versionAttribute, DependencyType.NuGet, new XmlLocation(context.FullPath, package, s_versionName.LocalName))).ConfigureAwait(false);
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
            var name = sdk.Attribute(s_nameName)?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var version = sdk.Attribute(s_versionName)?.Value;
            if (string.IsNullOrEmpty(version))
                continue;

            await context.ReportDependency(new Dependency(name, version, DependencyType.NuGet, new XmlLocation(context.FullPath, sdk, s_versionName.LocalName))).ConfigureAwait(false);
        }

        foreach (var sdk in doc.Descendants().Where(element => element.Name == ns + "Import" || element.Name == ns + "Project"))
        {
            var value = sdk.Attribute(s_sdkName)?.Value;
            if (string.IsNullOrEmpty(value))
                continue;

            var index = value.IndexOf('/', StringComparison.Ordinal);
            if (index > 0)
            {
                var packageName = value.Substring(0, index);
                var version = value[(index + 1)..];

                await context.ReportDependency(new Dependency(packageName, version, DependencyType.NuGet, new XmlLocation(context.FullPath, sdk, s_sdkName.LocalName, column: index + 1, value.Length - index - 1))).ConfigureAwait(false);
            }
        }
    }
}
