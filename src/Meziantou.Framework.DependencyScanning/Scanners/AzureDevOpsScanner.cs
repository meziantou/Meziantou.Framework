using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Azure DevOps pipeline YAML files for VM pool images, tasks, templates, and repository references.</summary>
public sealed class AzureDevOpsScanner : DependencyScanner
{
    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/jobs-deployment-strategy?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private static readonly string[] DeploymentStrategies = ["runOnce", "rolling", "canary"];
    private static readonly string[] DeploymentLifecycleHooks = ["preDeploy", "deploy", "routeTraffic", "postRouteTraffic"];
    private static readonly string[] DeploymentOnHooks = ["success", "failure"];

    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/resources-repositories-repository?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private static readonly string[] RepositoryTypes = ["git", "github", "githubenterprise", "bitbucket"];

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
    private void ScanPool(YamlFile yaml, YamlElement node)
    {
        var poolNode = GetProperty(node, "pool", StringComparison.Ordinal);
        var vmImageNode = GetProperty(poolNode, "vmImage", StringComparison.Ordinal);
        var value = GetScalarValue(vmImageNode);
        if (value is not null && yaml.TryMarkAsReported(vmImageNode))
        {
            yaml.Context.ReportDependency(this, name: null, version: value, DependencyType.AzureDevOpsVMPool, nameLocation: null, versionLocation: yaml.GetLocation(vmImageNode));
        }
    }

    private void ScanTemplate(YamlFile yaml, YamlElement? node)
    {
        var templateNode = GetProperty(node, "template", StringComparison.Ordinal);
        yaml.ReportDependencyWithSeparator(this, templateNode, DependencyType.AzureDevOpsTemplate, '@');
    }

    private void ScanStep(YamlFile yaml, YamlElement node)
    {
        var taskNode = GetProperty(node, "task", StringComparison.Ordinal);
        yaml.ReportDependencyWithSeparator(this, taskNode, DependencyType.AzureDevOpsTask, '@');

        ScanTemplate(yaml, node);
    }

    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/stages?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private void ScanStages(YamlFile yaml, YamlMapping node, HashSet<string> containerAliases)
    {
        var stagesNode = GetProperty(node, "stages", StringComparison.Ordinal);
        if (stagesNode is YamlSequence stages)
        {
            foreach (var stage in stages)
            {
                ScanTemplate(yaml, stage);
                ScanPool(yaml, stage);
                ScanJobs(yaml, stage, containerAliases);
            }
        }
    }

    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/jobs?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private void ScanJobs(YamlFile yaml, YamlElement node, HashSet<string> containerAliases)
    {
        var jobsNode = GetProperty(node, "jobs", StringComparison.Ordinal);
        if (jobsNode is YamlSequence jobs)
        {
            foreach (var job in jobs)
            {
                ScanTemplate(yaml, job);
                ScanPool(yaml, job);
                ScanSteps(yaml, job);
                ScanJobContainers(yaml, job, containerAliases);
                ScanDeploymentStrategy(yaml, job);
            }
        }
    }

    // strategy: runOnce|rolling|canary -> preDeploy|deploy|routeTraffic|postRouteTraffic|on.success|on.failure -> steps
    private void ScanDeploymentStrategy(YamlFile yaml, YamlElement node)
    {
        if (GetProperty(node, "strategy", StringComparison.Ordinal) is not YamlMapping strategy)
            return;

        foreach (var strategyName in DeploymentStrategies)
        {
            var strategyNode = GetProperty(strategy, strategyName, StringComparison.Ordinal);
            if (strategyNode is not YamlMapping)
                continue;

            foreach (var hookName in DeploymentLifecycleHooks)
            {
                ScanDeploymentHook(yaml, GetProperty(strategyNode, hookName, StringComparison.Ordinal));
            }

            var onNode = GetProperty(strategyNode, "on", StringComparison.Ordinal);
            foreach (var hookName in DeploymentOnHooks)
            {
                ScanDeploymentHook(yaml, GetProperty(onNode, hookName, StringComparison.Ordinal));
            }
        }
    }

    private void ScanDeploymentHook(YamlFile yaml, YamlElement? hook)
    {
        if (hook is null)
            return;

        ScanPool(yaml, hook);
        ScanSteps(yaml, hook);
    }

    private void ScanSteps(YamlFile yaml, YamlElement node)
    {
        var stepsNode = GetProperty(node, "steps", StringComparison.Ordinal);
        if (stepsNode is YamlSequence steps)
        {
            foreach (var step in steps)
            {
                ScanStep(yaml, step);
            }
        }
    }

    private void ScanJobContainers(YamlFile yaml, YamlElement node, HashSet<string> containerAliases)
    {
        var containerNode = GetProperty(node, "container", StringComparison.Ordinal);
        if (containerNode is YamlMapping container)
        {
            containerNode = GetProperty(container, "image", StringComparison.Ordinal);
        }
        else if (GetScalarValue(containerNode) is { } alias && containerAliases.Contains(alias))
        {
            // container: <alias> references resources.containers[].container, whose image is reported from the resource
            return;
        }

        yaml.ReportDockerImage(this, containerNode);
    }

    private static HashSet<string> GetContainerAliases(YamlElement node)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (GetProperty(node, "resources", StringComparison.Ordinal) is YamlMapping resources &&
            GetProperty(resources, "containers", StringComparison.Ordinal) is YamlSequence containers)
        {
            foreach (var container in containers)
            {
                if (GetScalarValue(GetProperty(container, "container", StringComparison.Ordinal)) is { } alias)
                {
                    result.Add(alias);
                }
            }
        }

        return result;
    }

    private void ScanResources(YamlFile yaml, YamlElement node)
    {
        if (GetProperty(node, "resources", StringComparison.Ordinal) is YamlMapping resources)
        {
            if (GetProperty(resources, "containers", StringComparison.Ordinal) is YamlSequence containers)
            {
                foreach (var container in containers)
                {
                    var imageNode = GetProperty(container, "image", StringComparison.Ordinal);
                    yaml.ReportDockerImage(this, imageNode);
                }
            }

            if (GetProperty(resources, "repositories", StringComparison.Ordinal) is YamlSequence repositories)
            {
                foreach (var repository in repositories)
                {
                    var type = GetScalarValue(GetProperty(repository, "type", StringComparison.Ordinal));
                    if (type is null || !RepositoryTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                        continue;

                    // name is the repository name (project/repository for Azure Repos, owner/repository for GitHub and Bitbucket), ref is refs/heads/<branch> or refs/tags/<tag>
                    var name = GetProperty(repository, "name", StringComparison.Ordinal);
                    if (GetScalarValue(name) is not { } nameValue || !yaml.TryMarkAsReported(name))
                        continue;

                    var endpoint = GetScalarValue(GetProperty(repository, "endpoint", StringComparison.Ordinal));
                    var alias = GetScalarValue(GetProperty(repository, "repository", StringComparison.Ordinal));
                    var version = GetProperty(repository, "ref", StringComparison.Ordinal);
                    yaml.Context.ReportDependency(
                        this,
                        name: nameValue,
                        version: GetScalarValue(version),
                        DependencyType.GitReference,
                        nameLocation: yaml.GetLocation(name),
                        versionLocation: yaml.GetLocation(version),
                        tags: [],
                        metadata: [
                            KeyValuePair.Create<string, object?>("endpoint", endpoint),
                            KeyValuePair.Create<string, object?>("repository", alias),
                            KeyValuePair.Create<string, object?>("type", type),
                        ]);
                }
            }
        }
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        var yaml = LoadYamlFile(context);
        if (yaml is null)
            return ValueTask.CompletedTask;

        foreach (var document in yaml.Stream)
        {
            if (document.Contents is not YamlMapping rootNode)
                continue;

            var containerAliases = GetContainerAliases(rootNode);
            ScanPool(yaml, rootNode);
            ScanStages(yaml, rootNode, containerAliases);
            ScanJobs(yaml, rootNode, containerAliases);
            ScanSteps(yaml, rootNode);
            ScanJobContainers(yaml, rootNode, containerAliases);
            ScanResources(yaml, rootNode);

            // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/extends?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
            ScanTemplate(yaml, GetProperty(rootNode, "extends", StringComparison.Ordinal));
        }

        return ValueTask.CompletedTask;
    }
}
