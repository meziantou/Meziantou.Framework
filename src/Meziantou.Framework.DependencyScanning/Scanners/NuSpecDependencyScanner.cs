using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

// https://docs.microsoft.com/en-us/nuget/reference/nuspec
public sealed class NuSpecDependencyScanner : DependencyScanner
{
    private static readonly XNamespace NuspecXmlns = XNamespace.Get("http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd");
    private static readonly XName DependencyXName = NuspecXmlns + "dependency";
    private static readonly XName IdXName = XName.Get("id");
    private static readonly XName VersionXName = XName.Get("version");

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasExtension(".nuspec", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc == null || doc.Root == null)
            return;

        foreach (var dependency in doc.Descendants(DependencyXName))
        {
            var idAttribute = dependency.Attribute(IdXName);
            var id = idAttribute?.Value;
            var versionAttribute = dependency.Attribute(VersionXName);
            var version = versionAttribute?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
            {
                context.ReportDependency(new Dependency(id, version, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, dependency, idAttribute),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, dependency, versionAttribute)));
            }
        }
    }
}
