using System;
using System.Threading.Tasks;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning
{
    // https://docs.microsoft.com/en-us/nuget/reference/nuspec
    public sealed class NuSpecDependencyScanner : DependencyScanner
    {
        private static readonly XNamespace s_nuspecXmlns = XNamespace.Get("http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd");
        private static readonly XName s_dependencyName = s_nuspecXmlns + "dependency";
        private static readonly XName s_idName = XName.Get("id");
        private static readonly XName s_versionName = XName.Get("version");

        public override bool ShouldScanFile(CandidateFileContext file)
        {
            return file.FileName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
        }

        public override async ValueTask ScanAsync(ScanFileContext context)
        {
            var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc == null || doc.Root == null)
                return;

            foreach (var dependency in doc.Descendants(s_dependencyName))
            {
                var id = dependency.Attribute(s_idName)?.Value;
                var version = dependency.Attribute(s_versionName)?.Value;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                {
                    await context.ReportDependency(new Dependency(id, version, DependencyType.NuGet, new XmlLocation(context.FullPath, dependency, "version"))).ConfigureAwait(false);
                }
            }
        }
    }
}
