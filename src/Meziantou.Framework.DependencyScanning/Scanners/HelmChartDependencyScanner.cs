using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Meziantou.Framework.DependencyScanning.Scanners;
public sealed class HelmChartDependencyScanner : DependencyScanner
{
    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Chart.yaml", ignoreCase: true) || context.HasFileName("Chart.yml", ignoreCase: true);
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            using var textReader = new StreamReader(context.Content, leaveOpen: true);
            var reader = new MergingParser(new Parser(textReader));
            var yaml = new YamlStream();
            yaml.Load(reader);

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
        }
        catch
        {
        }

        return ValueTask.CompletedTask;
    }
    private static TextLocation GetLocation(ScanFileContext context, YamlNode node, int? start = null, int? length = null)
    {
        var line = (int)node.Start.Line;
        var column = (int)node.Start.Column + (start ?? 0);
        if (node is YamlScalarNode { Style: ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted })
        {
            column += 1;
        }

        if (length is null)
        {
            if (node is YamlScalarNode scalarNode)
            {
                length = scalarNode.Value.Length;
            }
            else
            {
                length = (int)(node.End.Column - node.Start.Column);
            }
        }

        return new TextLocation(context.FileSystem, context.FullPath, line, column, length.Value);
    }

    private static YamlNode? GetProperty(YamlMappingNode node, string propertyName, StringComparison stringComparison)
    {
        foreach (var child in node.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, propertyName, stringComparison))
                return child.Value;
        }

        return null;
    }

    private static YamlNode? GetProperty(YamlNode? node, string propertyName, StringComparison stringComparison)
    {
        if (node is not YamlMappingNode mappingNode)
            return null;

        foreach (var child in mappingNode.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, propertyName, stringComparison))
                return child.Value;
        }

        return null;
    }

    private static string? GetScalarValue(YamlNode node)
    {
        if (node is YamlScalarNode scalar && scalar.Value is not null)
        {
            return scalar.Value;
        }

        return null;
    }
}
