using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Azure DevOps pipeline YAML files for VM pool images, tasks, templates, container images, and repository and package resources.</summary>
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
        DependencyType.Npm,
        DependencyType.NuGet,
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
                ScanVariables(yaml, stage);
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
                ScanVariables(yaml, job);
                ScanSteps(yaml, job);
                ScanJobContainers(yaml, job, containerAliases, scanServices: true);
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

    private void ScanJobContainers(YamlFile yaml, YamlElement node, HashSet<string> containerAliases, bool scanServices)
    {
        ScanContainer(yaml, GetProperty(node, "container", StringComparison.Ordinal), containerAliases);

        // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/jobs-job?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
        // services: { <name>: <container resource alias | image | container> }
        if (scanServices && GetProperty(node, "services", StringComparison.Ordinal) is YamlMapping services)
        {
            foreach (var service in services)
            {
                ScanContainer(yaml, service.Value, containerAliases);
            }
        }
    }

    private void ScanContainer(YamlFile yaml, YamlElement? containerNode, HashSet<string> containerAliases)
    {
        if (containerNode is YamlMapping container)
        {
            yaml.ReportDockerImage(this, GetProperty(container, "image", StringComparison.Ordinal));
            return;
        }

        // container: <alias> references resources.containers[].container, whose image is reported from the resource.
        // The resource may be declared in another file (the pipeline that includes a template), and the value may be an
        // expression, so a value is only reported when it is certainly an image: it has a tag, a digest or a path.
        if (GetScalarValue(containerNode) is { } alias && containerAliases.Contains(alias))
            return;

        yaml.ReportDockerImage(this, containerNode, requireQualifiedReference: true);
    }

    // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/variables-template?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
    private void ScanVariables(YamlFile yaml, YamlElement? node)
    {
        if (GetProperty(node, "variables", StringComparison.Ordinal) is YamlSequence variables)
        {
            foreach (var variable in variables)
            {
                ScanTemplate(yaml, variable);
            }
        }
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

            // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/resources-packages-package?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
            if (GetProperty(resources, "packages", StringComparison.Ordinal) is YamlSequence packages)
            {
                foreach (var package in packages)
                {
                    var type = GetScalarValue(GetProperty(package, "type", StringComparison.Ordinal));
                    DependencyType? dependencyType = type switch
                    {
                        _ when string.Equals(type, "npm", StringComparison.OrdinalIgnoreCase) => DependencyType.Npm,
                        _ when string.Equals(type, "NuGet", StringComparison.OrdinalIgnoreCase) => DependencyType.NuGet,
                        _ => null,
                    };

                    if (dependencyType is null)
                        continue;

                    // name is <repository>/<package> on GitHub Packages
                    var name = GetProperty(package, "name", StringComparison.Ordinal);
                    if (GetScalarValue(name) is not { } nameValue || !yaml.TryMarkAsReported(name))
                        continue;

                    var version = GetProperty(package, "version", StringComparison.Ordinal);
                    yaml.Context.ReportDependency(
                        this,
                        name: nameValue,
                        version: GetScalarValue(version),
                        dependencyType.Value,
                        nameLocation: yaml.GetLocation(name),
                        versionLocation: yaml.GetLocation(version),
                        tags: [],
                        metadata: [
                            KeyValuePair.Create<string, object?>("package", GetScalarValue(GetProperty(package, "package", StringComparison.Ordinal))),
                            KeyValuePair.Create<string, object?>("connection", GetScalarValue(GetProperty(package, "connection", StringComparison.Ordinal))),
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
            ScanVariables(yaml, rootNode);
            ScanStages(yaml, rootNode, containerAliases);
            ScanJobs(yaml, rootNode, containerAliases);
            ScanSteps(yaml, rootNode);
            // A pipeline with a single implicit job declares its steps and services at the root. A docker-compose file also
            // has a root services mapping, but no steps.
            ScanJobContainers(yaml, rootNode, containerAliases, scanServices: GetProperty(rootNode, "steps", StringComparison.Ordinal) is not null);
            ScanResources(yaml, rootNode);

            // https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/extends?view=azure-pipelines&WT.mc_id=DT-MVP-5003978
            ScanTemplate(yaml, GetProperty(rootNode, "extends", StringComparison.Ordinal));
        }

        return ValueTask.CompletedTask;
    }
}
