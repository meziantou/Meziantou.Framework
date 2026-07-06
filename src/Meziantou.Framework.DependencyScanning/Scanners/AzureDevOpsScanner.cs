using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Azure DevOps pipeline YAML files for VM pool images, tasks, templates, and repository references.</summary>
public sealed class AzureDevOpsScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } =
    [
        DependencyType.AzureDevOpsVMPool,
        DependencyType.AzureDevOpsTask,
        DependencyType.AzureDevOpsTemplate,
        DependencyType.GitReference,
        DependencyType.DockerImage,
    ];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // https://docs.github.com/en/free-pro-team@latest/actions/reference/workflow-syntax-for-github-actions#about-yaml-syntax-for-workflows
        if (context.HasExtension([".yml", ".yaml"], ignoreCase: false))
        {
            return true;
        }

        return false;
    }

    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/pool?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private void ScanPool(ScanFileContext context, YamlElement node)
    {
        var poolNode = GetProperty(node, "pool", StringComparison.Ordinal);
        var vmImageNode = GetProperty(poolNode, "vmImage", StringComparison.Ordinal);
        var value = GetScalarValue(vmImageNode);
        if (value is not null)
        {
            context.ReportDependency(this, name: null, version: value, DependencyType.AzureDevOpsVMPool, nameLocation: null, versionLocation: GetLocation(context, vmImageNode));
        }
    }

    private void ScanStep(ScanFileContext context, YamlElement node)
    {
        var taskNode = GetProperty(node, "task", StringComparison.Ordinal);
        ReportDependencyWithSeparator(this, context, taskNode, DependencyType.AzureDevOpsTask, '@');

        var templateNode = GetProperty(node, "template", StringComparison.Ordinal);
        ReportDependencyWithSeparator(this, context, templateNode, DependencyType.AzureDevOpsTemplate, '@');
    }

    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/stages?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private void ScanStages(ScanFileContext context, YamlMapping node)
    {
        var stagesNode = GetProperty(node, "stages", StringComparison.Ordinal);
        if (stagesNode is YamlSequence stages)
        {
            foreach (var stage in stages)
            {
                ScanPool(context, stage);
                ScanJobs(context, stage);
            }
        }
    }

    private void ScanJobs(ScanFileContext context, YamlElement node)
    {
        var jobsNode = GetProperty(node, "jobs", StringComparison.Ordinal);
        if (jobsNode is YamlSequence jobs)
        {
            foreach (var job in jobs)
            {
                ScanPool(context, job);
                ScanSteps(context, job);
                ScanJobContainers(context, job);
            }
        }
    }

    private void ScanSteps(ScanFileContext context, YamlElement node)
    {
        var stepsNode = GetProperty(node, "steps", StringComparison.Ordinal);
        if (stepsNode is YamlSequence steps)
        {
            foreach (var step in steps)
            {
                ScanStep(context, step);
            }
        }
    }

    private void ScanJobContainers(ScanFileContext context, YamlElement node)
    {
        var containerNode = GetProperty(node, "container", StringComparison.Ordinal);
        if (containerNode is YamlMapping container)
        {
            containerNode = GetProperty(container, "image", StringComparison.Ordinal);
        }

        ReportDependencyWithSeparator(this, context, containerNode, DependencyType.DockerImage, ':');
    }

    private void ScanResources(ScanFileContext context, YamlElement node)
    {
        if (GetProperty(node, "resources", StringComparison.Ordinal) is YamlMapping resources)
        {
            if (GetProperty(resources, "containers", StringComparison.Ordinal) is YamlSequence containers)
            {
                foreach (var container in containers)
                {
                    var imageNode = GetProperty(container, "image", StringComparison.Ordinal);
                    ReportDependencyWithSeparator(this, context, imageNode, DependencyType.DockerImage, ':');
                }
            }

            if (GetProperty(resources, "repositories", StringComparison.Ordinal) is YamlSequence repositories)
            {
                foreach (var repository in repositories)
                {
                    var type = GetProperty(repository, "type", StringComparison.Ordinal);
                    if (string.Equals(GetScalarValue(type), "git", StringComparison.OrdinalIgnoreCase))
                    {
                        var endpoint = GetProperty(repository, "endpoint", StringComparison.Ordinal);
                        var alias = GetProperty(repository, "repository", StringComparison.Ordinal);
                        var name = GetProperty(repository, "name", StringComparison.Ordinal);
                        var version = GetProperty(repository, "ref", StringComparison.Ordinal);
                        if (name is not null)
                        {
                            context.ReportDependency(
                                this,
                                name: GetScalarValue(name),
                                version: GetScalarValue(version),
                                DependencyType.GitReference,
                                nameLocation: GetLocation(context, name),
                                versionLocation: GetLocation(context, version),
                                tags: [],
                                metadata: [
                                    KeyValuePair.Create<string, object?>("endpoint", endpoint),
                                    KeyValuePair.Create<string, object?>("repository", alias),
                                ]);
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
            var yaml = LoadYamlDocument(context.Content);
            if (yaml is not null)
            {
                foreach (var document in yaml)
                {
                    if (document.Contents is not YamlMapping rootNode)
                        continue;

                    ScanPool(context, rootNode);
                    ScanStages(context, rootNode);
                    ScanJobs(context, rootNode);
                    ScanSteps(context, rootNode);
                    ScanJobContainers(context, rootNode);
                    ScanResources(context, rootNode);
                }
            }
        }
        catch
        {
        }

        return ValueTask.CompletedTask;
    }
}
