using Meziantou.Framework.DependencyScanning.Internals;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;
public sealed class HelmChartDependencyScanner : DependencyScanner
{
    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Chart.yaml", ignoreCase: true) || context.HasFileName("Chart.yml", ignoreCase: true);
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        var yaml = LoadYamlDocument(context.Content);
        if (yaml is null)
            return ValueTask.CompletedTask;

        foreach (var document in yaml.Documents)
        {
            if (document.RootNode is not YamlMappingNode rootNode)
                continue;

            // find dependencies
            var dependenciesNode = GetProperty(rootNode, "dependencies", StringComparison.Ordinal);
            if (dependenciesNode is YamlSequenceNode dependencies)
            {
                foreach (var dependency in dependencies)
                {
                    var versionNode = GetProperty(dependency, "version", StringComparison.Ordinal);
                    var repositoryNode = GetProperty(dependency, "repository", StringComparison.Ordinal);
                    var version = GetScalarValue(versionNode);
                    var repository = GetScalarValue(repositoryNode);
                    if (repository is not null)
                    {
                        context.ReportDependency<HelmChartDependencyScanner>(repository, version, DependencyType.HelmChart, nameLocation: GetLocation(context, repositoryNode), versionLocation: GetLocation(context, versionNode));
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}
