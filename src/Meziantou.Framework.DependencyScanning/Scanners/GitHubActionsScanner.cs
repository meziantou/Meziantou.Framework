using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

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

                var jobsNode = GetProperty(rootNode, "jobs", StringComparison.OrdinalIgnoreCase);
                if (jobsNode is YamlMappingNode jobs)
                {
                    foreach (var child in jobs.Children) // Enumerate jobs
                    {
                        if (child.Value is YamlMappingNode jobNode)
                        {
                            var stepsNode = GetProperty(jobNode, "steps", StringComparison.OrdinalIgnoreCase);
                            if (stepsNode is YamlSequenceNode steps)
                            {
                                foreach (var step in steps.OfType<YamlMappingNode>())
                                {
                                    var uses = GetProperty(step, "uses", StringComparison.OrdinalIgnoreCase);
                                    if (uses is YamlScalarNode usesValue && usesValue.Value is not null)
                                    {
                                        var value = usesValue.Value;

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
                                            ReportDependencyWithSeparator(context, usesValue, DependencyType.GitHubActions, '@');
                                        }
                                    }
                                }
                            }

                            var containerNode = GetProperty(jobNode, "container", StringComparison.OrdinalIgnoreCase);
                            if (containerNode is YamlMappingNode container)
                            {
                                var imageNode = GetProperty(container, "image", StringComparison.OrdinalIgnoreCase);
                                ReportDependencyWithSeparator(context, imageNode, DependencyType.DockerImage, ':');
                            }

                            var servicesNode = GetProperty(jobNode, "services", StringComparison.OrdinalIgnoreCase);
                            if (servicesNode is YamlMappingNode services)
                            {
                                foreach (var serviceNameNode in services.Children)
                                {
                                    if (serviceNameNode.Value is YamlMappingNode serviceNode)
                                    {
                                        var imageNode = GetProperty(serviceNode, "image", StringComparison.Ordinal);
                                        ReportDependencyWithSeparator(context, imageNode, DependencyType.DockerImage, ':');
                                    }
                                }
                            }
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

    private static YamlNode? GetProperty(YamlMappingNode node, string propertyName, StringComparison stringComparison)
    {
        foreach (var child in node.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, propertyName, stringComparison))
                return child.Value;
        }

        return null;
    }

    private static void ReportDependencyWithSeparator(ScanFileContext context, YamlNode node, DependencyType dependencyType, char versionSeparator)
    {
        var value = GetScalarValue(node);
        if (value is null)
            return;

        var index = value.IndexOf(versionSeparator, StringComparison.Ordinal);
        if (index < 0)
        {
            context.ReportDependency<GitHubActionsScanner>(name: value, version: null, dependencyType, nameLocation: GetLocation(context, node), versionLocation: null);
        }
        else
        {
            context.ReportDependency<GitHubActionsScanner>(
                name: value[..index],
                version: value[(index + 1)..],
                dependencyType,
                nameLocation: GetLocation(context, node, start: 0, length: index),
                versionLocation: GetLocation(context, node, start: index + 1, length: value.Length - index - 1));
        }
    }

    private static string? GetScalarValue(YamlNode node)
    {
        if (node is YamlScalarNode scalar && scalar.Value is not null)
        {
            return scalar.Value;
        }

        return null;
    }

    private static TextLocation GetLocation(ScanFileContext context, YamlNode node, int? start = null, int? length = null)
    {
        start ??= 0;

        var line = (int)node.Start.Line;
        var column = (int)(node.Start.Column + start);
        if (node is YamlScalarNode { Style: ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted })
        {
            column += 1;
        }

        if (length is null)
        {
            if (node is YamlScalarNode scalarNode)
            {
                length = scalarNode.Value.Length - start;
            }
            else
            {
                length = (int)(node.End.Column - node.Start.Column - start);
            }
        }

        return new TextLocation(context.FileSystem, context.FullPath, line, column, length.Value);
    }
}
