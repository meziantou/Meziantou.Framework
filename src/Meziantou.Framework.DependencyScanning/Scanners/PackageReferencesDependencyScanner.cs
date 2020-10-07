using System;
using System.Threading.Tasks;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning
{
    public sealed class PackageReferencesDependencyScanner : DependencyScanner
    {
        private static readonly XName s_includeName = XName.Get("Include");
        private static readonly XName s_versionName = XName.Get("Version");

        public override bool ShouldScanFile(CandidateFileContext context)
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
                    await context.ReportDependency(new Dependency(packageName, versionAttribute, DependencyType.NuGet, new XmlLocation(context.FullPath, package, "Version"))).ConfigureAwait(false);
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
        }
    }
}
