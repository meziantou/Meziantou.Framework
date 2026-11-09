using System.Globalization;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Mermaid.Internals;

/// <summary>The configuration of a diagram: the <c>config</c> of the front matter and the <c>init</c> directives, merged.</summary>
internal sealed class MermaidConfiguration
{
    public Dictionary<string, object?> Root { get; } = new(StringComparer.Ordinal);

    /// <summary>Merges a configuration: the values of <paramref name="source"/> replace the existing ones, and the mappings merge.</summary>
    public void Merge(Dictionary<string, object?> source) => Merge(Root, source);

    public object? Get(params ReadOnlySpan<string> path)
    {
        object? current = Root;
        foreach (var key in path)
        {
            if (current is not Dictionary<string, object?> map || !map.TryGetValue(key, out current))
                return null;
        }

        return current;
    }

    public string? GetString(params ReadOnlySpan<string> path) => Get(path) switch
    {
        string text => text,
        null => null,
        var value => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    public double? GetNumber(params ReadOnlySpan<string> path)
    {
        var text = GetString(path);
        if (text is null)
            return null;

        text = text.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2];
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    }

    public bool? GetBoolean(params ReadOnlySpan<string> path) => GetString(path)?.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    public Dictionary<string, object?>? GetMap(params ReadOnlySpan<string> path) => Get(path) as Dictionary<string, object?>;

    private static void Merge(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var (key, value) in source)
        {
            if (value is Dictionary<string, object?> map && target.TryGetValue(key, out var existing) && existing is Dictionary<string, object?> existingMap)
            {
                Merge(existingMap, map);
            }
            else
            {
                target[key] = value;
            }
        }
    }

    /// <summary>Converts a YAML element to dictionaries, lists and strings.</summary>
    public static object? FromYaml(YamlElement? element, int depth = 0)
    {
        if (element is null || depth > 32)
            return null;

        switch (element)
        {
            case YamlMapping mapping:
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (key, value) in mapping)
                {
                    if (key is YamlValue { Value: { } name })
                    {
                        result[name] = FromYaml(value, depth + 1);
                    }
                }

                return result;

            case YamlSequence sequence:
                return sequence.Select(item => FromYaml(item, depth + 1)).ToList();

            case YamlValue scalar:
                return scalar.Value;

            default:
                return null;
        }
    }
}
