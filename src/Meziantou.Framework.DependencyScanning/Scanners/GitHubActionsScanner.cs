using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class GitHubActionsScanner : DependencyScanner
{
    private const string DockerPrefix = "docker://";

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // https://docs.github.com/en/free-pro-team@latest/actions/reference/workflow-syntax-for-github-actions#about-yaml-syntax-for-workflows
        if (context.HasExtension(".yml", ignoreCase: false) || context.HasExtension(".yaml", ignoreCase: false))
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

        foreach (var document in yaml.Documents)
        {
            if (document.RootNode is not YamlMappingNode rootNode)
                continue;

            var jobsNode = GetProperty(rootNode, "jobs", StringComparison.OrdinalIgnoreCase);
            if (jobsNode is YamlMappingNode jobs)
            {
                foreach (var job in jobs.Children) // Enumerate jobs
                {
                    if (job.Value is YamlMappingNode jobNode)
                    {
                        ExtractUsesProperty(context, jobNode);

                        var stepsNode = GetProperty(jobNode, "steps", StringComparison.OrdinalIgnoreCase);
                        if (stepsNode is YamlSequenceNode steps)
                        {
                            foreach (var step in steps.OfType<YamlMappingNode>())
                            {
                                ExtractUsesProperty(context, step);
                            }
                        }

                        var containerNode = GetProperty(jobNode, "container", StringComparison.OrdinalIgnoreCase);
                        if (containerNode is YamlMappingNode container)
                        {
                            var imageNode = GetProperty(container, "image", StringComparison.OrdinalIgnoreCase);
                            ReportDependencyWithSeparator<GitHubActionsScanner>(context, imageNode, DependencyType.DockerImage, ':');
                        }

                        var servicesNode = GetProperty(jobNode, "services", StringComparison.OrdinalIgnoreCase);
                        if (servicesNode is YamlMappingNode services)
                        {
                            foreach (var serviceNameNode in services.Children)
                            {
                                if (serviceNameNode.Value is YamlMappingNode serviceNode)
                                {
                                    var imageNode = GetProperty(serviceNode, "image", StringComparison.Ordinal);
                                    ReportDependencyWithSeparator<GitHubActionsScanner>(context, imageNode, DependencyType.DockerImage, ':');
                                }
                            }
                        }
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void ExtractUsesProperty(ScanFileContext context, YamlNode node)
    {
        var uses = GetProperty(node, "uses", StringComparison.OrdinalIgnoreCase);
        if (uses is YamlScalarNode usesValue && usesValue.Value is { } value)
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

                    context.ReportDependency<GitHubActionsScanner>(name, version, DependencyType.DockerImage, nameLocation, versionLocation);
                }
                else
                {
                    // no version
                    var name = value[DockerPrefix.Length..];
                    var nameLocation = GetLocation(context, usesValue, start: DockerPrefix.Length, length: name.Length);

                    context.ReportDependency<GitHubActionsScanner>(name, version: null, DependencyType.DockerImage, nameLocation, versionLocation: null);

                }
            }
            // use: action@v1
            else
            {
                ReportDependencyWithSeparator<GitHubActionsScanner>(context, usesValue, DependencyType.GitHubActions, '@');
            }
        }
    }
}
