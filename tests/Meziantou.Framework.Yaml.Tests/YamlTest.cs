using System.Reflection;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Yaml.Tests;

public partial class YamlTest
{
    protected static TextReader YamlFile(string name)
    {
        var fromType = typeof(YamlTest);
        var assembly = fromType.Assembly;
        var stream = assembly.GetManifestResourceStream(name) ??
                     assembly.GetManifestResourceStream(fromType.Namespace + ".files." + name);
        if (stream == null)
        {
            throw new ArgumentException($"Resource '{name}' not found.", nameof(name));
        }

        return new StreamReader(stream);
    }

    protected static TextReader YamlText(string yaml)
    {
        var lines = yaml
            .Split('\n')
            .Select(line => line.TrimEnd('\r', '\n'))
            .SkipWhile(line => line.Trim(' ', '\t').Length is 0)
            .ToList();

        while (lines is { Count: > 0 } && lines[lines.Count - 1].Trim(' ', '\t').Length is 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines is { Count: > 0 })
        {
            var indent = LeadingTabsRegex.Match(lines[0]);
            if (!indent.Success)
            {
                throw new ArgumentException("Invalid indentation", nameof(yaml));
            }

            lines = lines
                .Select(line => line.Substring(indent.Value.Length))
                .ToList();
        }

        return new StringReader(string.Join('\n', lines));
    }

    [GeneratedRegex(@"^\t+", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LeadingTabsRegex { get; }
}
