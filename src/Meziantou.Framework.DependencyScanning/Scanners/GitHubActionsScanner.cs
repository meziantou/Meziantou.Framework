using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans GitHub Actions workflow files and action metadata files (<c>action.yml</c>) for action references and Docker images.</summary>
public sealed class GitHubActionsScanner : DependencyScanner
{
    private const string DockerPrefix = "docker://";

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.DockerImage, DependencyType.GitHubActions];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // https://docs.github.com/en/actions/sharing-automations/creating-actions/metadata-syntax-for-github-actions
        if (context.HasFileName("action.yml", ignoreCase: false) || context.HasFileName("action.yaml", ignoreCase: false))
            return true;

        // https://docs.github.com/en/free-pro-team@latest/actions/reference/workflow-syntax-for-github-actions#about-yaml-syntax-for-workflows
        if (context.HasExtension([".yml", ".yaml"], ignoreCase: false))
        {
            return context.RelativeDirectory is ".github/workflows" or ".github\\workflows";
        }

        return false;
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

            // Workflow
            var jobsNode = GetProperty(rootNode, "jobs", StringComparison.OrdinalIgnoreCase);
            if (jobsNode is YamlMapping jobs)
            {
                foreach (var job in jobs) // Enumerate jobs
                {
                    if (job.Value is YamlMapping jobNode)
                    {
                        ExtractUsesProperty(yaml, jobNode);
                        ExtractSteps(yaml, jobNode);

                        var containerNode = GetProperty(jobNode, "container", StringComparison.OrdinalIgnoreCase);
                        if (containerNode is YamlMapping container)
                        {
                            containerNode = GetProperty(container, "image", StringComparison.OrdinalIgnoreCase);
                        }

                        // container: node:18
                        yaml.ReportDockerImage(this, containerNode);

                        var servicesNode = GetProperty(jobNode, "services", StringComparison.OrdinalIgnoreCase);
                        if (servicesNode is YamlMapping services)
                        {
                            foreach (var serviceNameNode in services)
                            {
                                if (serviceNameNode.Value is YamlMapping serviceNode)
                                {
                                    var imageNode = GetProperty(serviceNode, "image", StringComparison.Ordinal);
                                    yaml.ReportDockerImage(this, imageNode);
                                }
                            }
                        }
                    }
                }
            }

            // Action metadata file (action.yml)
            var runsNode = GetProperty(rootNode, "runs", StringComparison.Ordinal);
            if (runsNode is YamlMapping runs)
            {
                // Composite action
                ExtractSteps(yaml, runs);

                // Docker container action: image is either a Dockerfile path or docker://image:tag
                var imageNode = GetProperty(runs, "image", StringComparison.Ordinal);
                if (GetScalarValue(imageNode) is { } image && image.StartsWith(DockerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    yaml.ReportDockerImage(this, imageNode, DockerPrefix.Length);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private void ExtractSteps(YamlFile yaml, YamlMapping node)
    {
        var stepsNode = GetProperty(node, "steps", StringComparison.OrdinalIgnoreCase);
        if (stepsNode is YamlSequence steps)
        {
            foreach (var step in steps.OfType<YamlMapping>())
            {
                ExtractUsesProperty(yaml, step);
            }
        }
    }

    private void ExtractUsesProperty(YamlFile yaml, YamlElement node)
    {
        var uses = GetProperty(node, "uses", StringComparison.OrdinalIgnoreCase);
        if (uses is YamlValue usesValue && usesValue.Value is { } value)
        {
            // uses: docker://alpine:3.8
            if (value.StartsWith(DockerPrefix, StringComparison.OrdinalIgnoreCase)) // https://docs.github.com/en/free-pro-team@latest/actions/reference/workflow-syntax-for-github-actions#example-using-a-docker-hub-action
            {
                yaml.ReportDockerImage(this, usesValue, DockerPrefix.Length);
            }
            // use: action@v1
            // Local actions and reusable workflows (uses: ./.github/actions/local) are part of the repository, not dependencies
            else if (!value.StartsWith("./", StringComparison.Ordinal) && !value.StartsWith("../", StringComparison.Ordinal))
            {
                yaml.ReportDependencyWithSeparator(this, usesValue, DependencyType.GitHubActions, '@');
            }
        }
    }
}
