using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans GitHub Actions workflow YAML files for action references and Docker images.</summary>
public sealed class GitHubActionsScanner : DependencyScanner
{
    private const string DockerPrefix = "docker://";

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.DockerImage, DependencyType.GitHubActions];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // https://docs.github.com/en/free-pro-team@latest/actions/reference/workflow-syntax-for-github-actions#about-yaml-syntax-for-workflows
        if (context.HasExtension([".yml", ".yaml"], ignoreCase: false))
        {
            var directoryName = Path.GetFileName(context.Directory);
            if (directoryName is "workflows")
            {
                var parentDirectory = Path.GetDirectoryName(context.Directory);
                directoryName = Path.GetFileName(parentDirectory);
                if (directoryName is ".github")
                {
                    return Path.GetDirectoryName(parentDirectory).Equals(context.RootDirectory, StringComparison.Ordinal);
                }
            }
        }

        return false;
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        var yaml = LoadYamlDocument(context.Content);
        if (yaml is null)
            return ValueTask.CompletedTask;

        foreach (var document in yaml)
        {
            if (document.Contents is not YamlMapping rootNode)
                continue;

            var jobsNode = GetProperty(rootNode, "jobs", StringComparison.OrdinalIgnoreCase);
            if (jobsNode is YamlMapping jobs)
            {
                foreach (var job in jobs) // Enumerate jobs
                {
                    if (job.Value is YamlMapping jobNode)
                    {
                        ExtractUsesProperty(context, jobNode);

                        var stepsNode = GetProperty(jobNode, "steps", StringComparison.OrdinalIgnoreCase);
                        if (stepsNode is YamlSequence steps)
                        {
                            foreach (var step in steps.OfType<YamlMapping>())
                            {
                                ExtractUsesProperty(context, step);
                            }
                        }

                        var containerNode = GetProperty(jobNode, "container", StringComparison.OrdinalIgnoreCase);
                        if (containerNode is YamlMapping container)
                        {
                            var imageNode = GetProperty(container, "image", StringComparison.OrdinalIgnoreCase);
                            ReportDependencyWithSeparator(this, context, imageNode, DependencyType.DockerImage, ':');
                        }

                        var servicesNode = GetProperty(jobNode, "services", StringComparison.OrdinalIgnoreCase);
                        if (servicesNode is YamlMapping services)
                        {
                            foreach (var serviceNameNode in services)
                            {
                                if (serviceNameNode.Value is YamlMapping serviceNode)
                                {
                                    var imageNode = GetProperty(serviceNode, "image", StringComparison.Ordinal);
                                    ReportDependencyWithSeparator(this, context, imageNode, DependencyType.DockerImage, ':');
                                }
                            }
                        }
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private void ExtractUsesProperty(ScanFileContext context, YamlElement node)
    {
        var uses = GetProperty(node, "uses", StringComparison.OrdinalIgnoreCase);
        if (uses is YamlValue usesValue && usesValue.Value is { } value)
        {
            // uses: docker://alpine:3.8
            if (value.StartsWith(DockerPrefix, StringComparison.OrdinalIgnoreCase)) // https://docs.github.com/en/free-pro-team@latest/actions/reference/workflow-syntax-for-github-actions#example-using-a-docker-hub-action
            {
                var index = value.AsSpan()[DockerPrefix.Length..].LastIndexOf(':');
                if (index > 0)
                {
                    var name = value[DockerPrefix.Length..(DockerPrefix.Length + index)];
                    var version = value[(DockerPrefix.Length + index + 1)..];

                    var nameLocation = GetLocation(context, usesValue, start: DockerPrefix.Length, length: name.Length);
                    var versionLocation = GetLocation(context, usesValue, start: DockerPrefix.Length + index + 1, length: version.Length);

                    context.ReportDependency(this, name, version, DependencyType.DockerImage, nameLocation, versionLocation);
                }
                else
                {
                    // no version
                    var name = value[DockerPrefix.Length..];
                    var nameLocation = GetLocation(context, usesValue, start: DockerPrefix.Length, length: name.Length);

                    context.ReportDependency(this, name, version: null, DependencyType.DockerImage, nameLocation, versionLocation: null);

                }
            }
            // use: action@v1
            else
            {
                ReportDependencyWithSeparator(this, context, usesValue, DependencyType.GitHubActions, '@');
            }
        }
    }
}
