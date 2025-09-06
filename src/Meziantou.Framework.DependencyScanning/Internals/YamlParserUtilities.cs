using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class YamlParserUtilities
{
    public static YamlStream? LoadYamlDocument(Stream stream)
    {
        try
        {
            using var textReader = new StreamReader(stream, leaveOpen: true);
            var reader = new MergingParser(new Parser(textReader));
            var yaml = new YamlStream();
            yaml.Load(reader);
            return yaml;
        }
        catch
        {
            return null;
        }
    }

    public static YamlNode? GetProperty(YamlNode node, string propertyName, StringComparison stringComparison)
    {
        if (node is YamlMappingNode mapping)
        {
            foreach (var child in mapping.Children)
            {
                if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, propertyName, stringComparison))
                    return child.Value;
            }
        }

        return null;
    }

    public static void ReportDependency(DependencyScanner scanner, ScanFileContext context, YamlNode node, DependencyType dependencyType)
    {
        var value = GetScalarValue(node);
        if (value is null)
            return;

        context.ReportDependency(scanner, name: value, version: null, dependencyType, nameLocation: GetLocation(context, node), versionLocation: null);
    }

    public static void ReportDependencyWithSeparator(DependencyScanner scanner, ScanFileContext context, YamlNode node, DependencyType dependencyType, char versionSeparator)
    {
        var value = GetScalarValue(node);
        if (value is null)
            return;

        var index = value.IndexOf(versionSeparator, StringComparison.Ordinal);
        if (index < 0)
        {
            context.ReportDependency(scanner, name: value, version: null, dependencyType, nameLocation: GetLocation(context, node), versionLocation: null);
        }
        else
        {
            context.ReportDependency(
                scanner,
                name: value[..index],
                version: value[(index + 1)..],
                dependencyType,
                nameLocation: GetLocation(context, node, start: 0, length: index),
                versionLocation: GetLocation(context, node, start: index + 1, length: value.Length - index - 1));
        }
    }

    public static string? GetScalarValue(YamlNode? node)
    {
        if (node is YamlScalarNode scalar && scalar.Value is not null)
            return scalar.Value;

        return null;
    }

    [return: NotNullIfNotNull(nameof(node))]
    public static TextLocation? GetLocation(ScanFileContext context, YamlNode? node, int? start = null, int? length = null)
    {
        if (node is null)
            return null;

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
