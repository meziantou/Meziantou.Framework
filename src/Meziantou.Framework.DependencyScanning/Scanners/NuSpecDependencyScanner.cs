using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans NuGet .nuspec files for package dependencies.</summary>
public sealed class NuSpecDependencyScanner : DependencyScanner
{
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

        // NuGet (NuspecReader) reads the metadata using the default namespace of the document, whatever it is (including no namespace)
        var ns = doc.Root.GetDefaultNamespace();
        foreach (var dependency in doc.Root.Descendants(ns + "dependency"))
        {
            var idAttribute = dependency.Attribute(IdXName);
            var id = idAttribute?.Value;
            if (string.IsNullOrEmpty(id))
                continue;

            Location nameLocation = IsReplacementToken(id)
                ? new NonUpdatableLocation(context)
                : new XmlLocation(context.FileSystem, context.FullPath, dependency, idAttribute);

            // The version is optional: a dependency without a version accepts any version of the package
            var versionAttribute = dependency.Attribute(VersionXName);
            var version = versionAttribute?.Value;
            if (string.IsNullOrEmpty(version))
            {
                context.ReportDependency(this, id, version: null, DependencyType.NuGet, nameLocation, versionLocation: null);
                continue;
            }

            context.ReportDependency(this, id, version, DependencyType.NuGet,
                nameLocation,
                versionLocation: IsReplacementToken(version)
                    ? new NonUpdatableLocation(context)
                    : new XmlLocation(context.FileSystem, context.FullPath, dependency, versionAttribute));
        }
    }

    // Replacement tokens such as $version$ are substituted by 'nuget pack' or MSBuild. Updating them would replace the token with a literal value.
    private static bool IsReplacementToken(string value) => value.Contains('$', StringComparison.Ordinal);
}
