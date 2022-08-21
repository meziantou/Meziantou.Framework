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
            if (directoryName.Equals("workflows", StringComparison.Ordinal))
            {
                var parentDirectory = Path.GetDirectoryName(context.Directory);
                directoryName = Path.GetFileName(parentDirectory);
                if (directoryName.Equals(".github", StringComparison.Ordinal))
                {
                    return Path.GetDirectoryName(parentDirectory).Equals(context.RootDirectory, StringComparison.Ordinal);
                }
            }
        }

        return false;
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        using var textReader = new StreamReader(context.Content);
        var reader = new MergingParser(new Parser(textReader));
        var yaml = new YamlStream();
        yaml.Load(reader);

        foreach (var document in yaml.Documents)
        {
            var rootNode = (YamlMappingNode)document.RootNode;
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
                                if (uses is YamlScalarNode usesValue && usesValue.Value != null)
                                {
                                    var value = usesValue.Value;

                                    // uses: docker://alpine:3.8
                                    if (value.StartsWith(DockerPrefix, StringComparison.OrdinalIgnoreCase)) // https://docs.github.com/en/free-pro-team@latest/actions/reference/workflow-syntax-for-github-actions#example-using-a-docker-hub-action
                                    {
                                        var index = value.AsSpan()[DockerPrefix.Length..].LastIndexOf(':');
                                        if (index > 0)
                                        {
                                            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, usesValue.Start.Line, usesValue.Start.Column + DockerPrefix.Length, index);
                                            var versionLocation = new TextLocation(context.FileSystem, context.FullPath, usesValue.Start.Line, usesValue.Start.Column + DockerPrefix.Length + index + 1, value.Length - DockerPrefix.Length - index - 1);
                                            context.ReportDependency(new Dependency(value[DockerPrefix.Length..(DockerPrefix.Length + index)], value[(DockerPrefix.Length + index + 1)..], DependencyType.DockerImage, nameLocation, versionLocation));
                                        }
                                        else
                                        {
                                            // no version
                                            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, usesValue.Start.Line, usesValue.Start.Column + DockerPrefix.Length, value.Length - DockerPrefix.Length);
                                            context.ReportDependency(new Dependency(value[DockerPrefix.Length..], version: null, DependencyType.DockerImage, nameLocation, versionLocation: null));

                                        }
                                    }
                                    // use: action@v1
                                    else
                                    {
                                        var index = value.IndexOf('@', StringComparison.Ordinal);
                                        if (index > 0)
                                        {
                                            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, usesValue.Start.Line, usesValue.Start.Column, index);
                                            var versionLocation = new TextLocation(context.FileSystem, context.FullPath, usesValue.Start.Line, usesValue.Start.Column + index + 1, value.Length - index - 1);
                                            context.ReportDependency(new Dependency(value[0..index], value[(index + 1)..], DependencyType.GitHubActions, nameLocation, versionLocation));
                                        }
                                        else if (!value.StartsWith("./", StringComparison.Ordinal))
                                        {
                                            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, usesValue.Start.Line, usesValue.Start.Column, value.Length);
                                            context.ReportDependency(new Dependency(value, version: null, DependencyType.GitHubActions, nameLocation, versionLocation: null));
                                        }
                                    }
                                }
                            }
                        }

                        var containerNode = GetProperty(jobNode, "container", StringComparison.OrdinalIgnoreCase);
                        if (containerNode is YamlMappingNode container)
                        {
                            var imageNode = GetProperty(container, "image", StringComparison.OrdinalIgnoreCase);
                            if (imageNode is YamlScalarNode image && image.Value != null)
                            {
                                var value = image.Value;
                                var index = value.LastIndexOf(':');
                                if (index > 0)
                                {
                                    var nameLocation = new TextLocation(context.FileSystem, context.FullPath, image.Start.Line, image.Start.Column, index);
                                    var versionLocation = new TextLocation(context.FileSystem, context.FullPath, image.Start.Line, image.Start.Column + index + 1, value.Length - index - 1);
                                    context.ReportDependency(new Dependency(value[0..index], value[(index + 1)..], DependencyType.DockerImage, nameLocation, versionLocation));
                                }
                                else
                                {
                                    var nameLocation = new TextLocation(context.FileSystem, context.FullPath, image.Start.Line, image.Start.Column, value.Length);
                                    context.ReportDependency(new Dependency(value, version: null, DependencyType.DockerImage, nameLocation, versionLocation: null));
                                }
                            }
                        }

                        var servicesNode = GetProperty(jobNode, "services", StringComparison.OrdinalIgnoreCase);
                        if (servicesNode is YamlMappingNode services)
                        {
                            foreach (var serviceNameNode in services.Children)
                            {
                                if (serviceNameNode.Value is YamlMappingNode serviceNode)
                                {
                                    var imageNode = GetProperty(serviceNode, "image", StringComparison.OrdinalIgnoreCase);
                                    if (imageNode is YamlScalarNode image && image.Value != null)
                                    {
                                        var value = image.Value;
                                        var index = value.LastIndexOf(':');
                                        if (index > 0)
                                        {
                                            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, image.Start.Line, image.Start.Column, index);
                                            var versionLocation = new TextLocation(context.FileSystem, context.FullPath, image.Start.Line, image.Start.Column + index + 1, value.Length - index - 1);
                                            context.ReportDependency(new Dependency(value[0..index], value[(index + 1)..], DependencyType.DockerImage, nameLocation, versionLocation));
                                        }
                                        else
                                        {
                                            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, image.Start.Line, image.Start.Column, value.Length);
                                            context.ReportDependency(new Dependency(value, version: null, DependencyType.DockerImage, nameLocation, versionLocation: null));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
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
}
