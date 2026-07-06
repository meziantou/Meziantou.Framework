using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class YamlParserUtilities
{
    public static YamlStream? LoadYamlDocument(Stream stream)
    {
        try
        {
            using var textReader = new StreamReader(stream, leaveOpen: true);
            return YamlStream.Load(textReader);
        }
        catch
        {
            return null;
        }
    }

    public static YamlElement? GetProperty(YamlElement? node, string propertyName, StringComparison stringComparison)
    {
        if (node is YamlMapping mapping)
        {
            foreach (var child in mapping)
            {
                if (child.Key is YamlValue scalar && string.Equals(scalar.Value, propertyName, stringComparison))
                    return child.Value;
            }
        }

        return null;
    }

    public static void ReportDependency(DependencyScanner scanner, ScanFileContext context, YamlElement node, DependencyType dependencyType)
    {
        var value = GetScalarValue(node);
        if (value is null)
            return;

        context.ReportDependency(scanner, name: value, version: null, dependencyType, nameLocation: GetLocation(context, node), versionLocation: null);
    }

    public static void ReportDependencyWithSeparator(DependencyScanner scanner, ScanFileContext context, YamlElement? node, DependencyType dependencyType, char versionSeparator)
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

    public static string? GetScalarValue(YamlElement? node)
    {
        if (node is YamlValue scalar)
            return scalar.Value;

        return null;
    }

    public static TextLocation? GetLocation(ScanFileContext context, YamlElement? node, int? start = null, int? length = null)
    {
        if (node is null)
            return null;

        start ??= 0;

        var span = GetSpan(node);
        if (span is null)
            return null;

        var (spanStart, spanEnd) = span.Value;
        var line = spanStart.Line + 1;
        var column = spanStart.Column + 1 + start.Value;
        if (node is YamlValue { Style: ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted })
        {
            column += 1;
        }

        if (length is null)
        {
            if (node is YamlValue { Value: { } nodeValue })
            {
                length = Math.Max(0, nodeValue.Length - start.Value);
            }
            else
            {
                length = Math.Max(0, spanEnd.Column - spanStart.Column - start.Value);
            }
        }

        return new TextLocation(context.FileSystem, context.FullPath, line, column, length.Value);
    }

    private static (Mark Start, Mark End)? GetSpan(YamlElement node)
    {
        ParsingEvent? first = null;
        ParsingEvent? last = null;

        foreach (var yamlEvent in node.EnumerateEvents())
        {
            if (yamlEvent is StreamStart or StreamEnd or DocumentStart or DocumentEnd)
            {
                continue;
            }

            first ??= yamlEvent;
            last = yamlEvent;
        }

        if (first is null || last is null)
            return null;

        return (first.Start, last.End);
    }
}
