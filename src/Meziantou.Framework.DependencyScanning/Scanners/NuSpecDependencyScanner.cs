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
        return context.FileName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc == null || doc.Root == null)
            return;

        foreach (var dependency in doc.Descendants(DependencyXName))
        {
            var id = dependency.Attribute(IdXName)?.Value;
            var version = dependency.Attribute(VersionXName)?.Value;

            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
            {
                await context.ReportDependency(new Dependency(id, version, DependencyType.NuGet, new XmlLocation(context.FullPath, dependency, "version"))).ConfigureAwait(false);
            }
        }
    }
}
