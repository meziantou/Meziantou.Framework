using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class AzureDevOpsScanner : DependencyScanner
{
    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // https://docs.github.com/en/free-pro-team@latest/actions/reference/workflow-syntax-for-github-actions#about-yaml-syntax-for-workflows
        if (context.HasExtension(".yml", ignoreCase: false) || context.HasExtension(".yaml", ignoreCase: false))
        {
            return true;
        }

        return false;
    }

    private static string? GetScalarValue(YamlNode node)
    {
        if (node is YamlScalarNode scalar && scalar.Value is not null)
        {
            return scalar.Value;
        }

        return null;
    }

    private static void ReportDependency(ScanFileContext context, YamlNode node, DependencyType dependencyType, char versionSeparator)
    {
        var value = GetScalarValue(node);
        if (value is null)
            return;

        var index = value.IndexOf(versionSeparator, StringComparison.Ordinal);
        if (index < 0)
        {
            context.ReportDependency<AzureDevOpsScanner>(name: value, version: null, dependencyType, nameLocation: GetLocation(context, node), versionLocation: null);
        }
        else
        {
            context.ReportDependency<AzureDevOpsScanner>(
                name: value[..index],
                version: value[(index + 1)..],
                dependencyType,
                nameLocation: GetLocation(context, node, start: 0, length: index),
                versionLocation: GetLocation(context, node, start: index + 1, length: value.Length - index - 1));
        }
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

    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/pool?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private static void ScanPool(ScanFileContext context, YamlNode node)
    {
        var poolNode = GetProperty(node, "pool", StringComparison.Ordinal);
        var vmImageNode = GetProperty(poolNode, "vmImage", StringComparison.Ordinal);
        var value = GetScalarValue(vmImageNode);
        if (value is not null)
        {
            context.ReportDependency<AzureDevOpsScanner>(name: null, version: value, DependencyType.AzureDevOpsVMPool, nameLocation: null, versionLocation: GetLocation(context, vmImageNode));
        }
    }

    private static void ScanStep(ScanFileContext context, YamlNode node)
    {
        var taskNode = GetProperty(node, "task", StringComparison.Ordinal);
        ReportDependency(context, taskNode, DependencyType.AzureDevOpsTask, '@');
    }

    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/stages?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private static void ScanStages(ScanFileContext context, YamlMappingNode node)
    {
        var stagesNode = GetProperty(node, "stages", StringComparison.Ordinal);
        if (stagesNode is YamlSequenceNode stages)
        {
            foreach (var stage in stages)
            {
                ScanPool(context, stage);
                ScanJobs(context, stage);
            }
        }
    }

    private static void ScanJobs(ScanFileContext context, YamlNode node)
    {
        var jobsNode = GetProperty(node, "jobs", StringComparison.Ordinal);
        if (jobsNode is YamlSequenceNode jobs)
        {
            foreach (var job in jobs)
            {
                ScanPool(context, job);
                ScanSteps(context, job);
                ScanJobContainers(context, job);
            }
        }
    }

    private static void ScanSteps(ScanFileContext context, YamlNode node)
    {
        var stepsNode = GetProperty(node, "steps", StringComparison.Ordinal);
        if (stepsNode is YamlSequenceNode steps)
        {
            foreach (var step in steps)
            {
                ScanStep(context, step);
            }
        }
    }

    private static void ScanJobContainers(ScanFileContext context, YamlNode node)
    {
        var containerNode = GetProperty(node, "container", StringComparison.Ordinal);
        if (containerNode is YamlMappingNode container)
        {
            containerNode = GetProperty(container, "image", StringComparison.Ordinal);
        }

        ReportDependency(context, containerNode, DependencyType.DockerImage, ':');
    }

    private static void ScanResources(ScanFileContext context, YamlNode node)
    {
        if (GetProperty(node, "resources", StringComparison.Ordinal) is YamlMappingNode resources)
        {
            if (GetProperty(resources, "containers", StringComparison.Ordinal) is YamlSequenceNode containers)
            {
                foreach (var container in containers)
                {
                    var imageNode = GetProperty(container, "image", StringComparison.Ordinal);
                    ReportDependency(context, imageNode, DependencyType.DockerImage, ':');
                }
            }

            if (GetProperty(resources, "repositories", StringComparison.Ordinal) is YamlSequenceNode repositories)
            {
                foreach (var repository in repositories)
                {
                    var type = GetProperty(repository, "type", StringComparison.Ordinal);
                    if (string.Equals(GetScalarValue(type), "git", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = GetProperty(repository, "name", StringComparison.Ordinal);
                        var version = GetProperty(repository, "ref", StringComparison.Ordinal);
                        if (name is not null)
                        {
                            context.ReportDependency<AzureDevOpsScanner>(name: GetScalarValue(name), version: GetScalarValue(version), DependencyType.GitReference, nameLocation: GetLocation(context, name), versionLocation: GetLocation(context, version));
                        }
                    }
                }
            }
        }
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

                ScanPool(context, rootNode);
                ScanStages(context, rootNode);
                ScanJobs(context, rootNode);
                ScanSteps(context, rootNode);
                ScanJobContainers(context, rootNode);
                ScanResources(context, rootNode);
            }
        }
        catch
        {
        }

        return ValueTask.CompletedTask;
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
}
