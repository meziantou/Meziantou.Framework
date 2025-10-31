using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans NuGet .nuspec files for package dependencies.</summary>
public sealed class NuSpecDependencyScanner : DependencyScanner
{
    // https://github.com/NuGet/NuGet.Client/blob/cabdb9886f3bc99c7a342ccc1661d393b14a0d1d/src/NuGet.Core/NuGet.Packaging/PackageCreation/Authoring/ManifestSchemaUtility.cs#L23
    private static readonly XNamespace SchemaVersionV1 = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";
    private static readonly XNamespace SchemaVersionV2 = "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd";
    private static readonly XNamespace SchemaVersionV3 = "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd";
    private static readonly XNamespace SchemaVersionV4 = "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd";
    private static readonly XNamespace SchemaVersionV5 = "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd";
    private static readonly XNamespace SchemaVersionV6 = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    private static readonly XNamespace[] Namespaces =
    [
        SchemaVersionV1,
        SchemaVersionV2,
        SchemaVersionV3,
        SchemaVersionV4,
        SchemaVersionV5,
        SchemaVersionV6,
    ];

    private static readonly XName IdXName = XName.Get("id");
    private static readonly XName VersionXName = XName.Get("version");

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasExtension(".nuspec", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc is null || doc.Root is null)
            return;

        foreach (var dependency in doc.Descendants().Where(element => element.Name.LocalName is "dependency" && Namespaces.Contains(element.Name.Namespace)))
        {
            var idAttribute = dependency.Attribute(IdXName);
            var id = idAttribute?.Value;
            var versionAttribute = dependency.Attribute(VersionXName);
            var version = versionAttribute?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
            {
                context.ReportDependency(this, id, version, DependencyType.NuGet,
                    nameLocation: new XmlLocation(context.FileSystem, context.FullPath, dependency, idAttribute),
                    versionLocation: new XmlLocation(context.FileSystem, context.FullPath, dependency, versionAttribute));
            }
        }
    }
}
