using System.Text;
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Mermaid.Internals;

/// <summary>
/// Reads the parts of a Mermaid text that every diagram shares: the YAML front matter, the <c>%%{init: ...}%%</c>
/// directives and the <c>%%</c> comments, which it replaces by spaces so the positions of the rest do not change.
/// </summary>
internal static class MermaidPreprocessor
{
    public static ParseContext Process(string text, MermaidParseOptions options)
    {
        if (text.Length > options.MaxTextSize)
            throw new MermaidParseException(new MermaidParseError($"The text is longer than {options.MaxTextSize} characters.", 1, 1));

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var context = new ParseContext(normalized, options);
        var buffer = new StringBuilder(normalized);
        ReadFrontMatter(context, buffer);
        ReadDirectives(context, buffer);
        RemoveComments(buffer);
        context.Text = buffer.ToString();
        return context;
    }

    private static void ReadFrontMatter(ParseContext context, StringBuilder buffer)
    {
        var text = context.Text;
        var position = 0;
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        if (!IsFenceAt(text, position, out var afterOpening))
            return;

        // The closing fence: a line of three dashes
        var lineStart = afterOpening;
        while (lineStart < text.Length)
        {
            if (IsFenceAt(text, lineStart, out var afterClosing))
            {
                var yaml = text[afterOpening..lineStart];
                Blank(buffer, 0, afterClosing);
                ApplyFrontMatter(context, yaml, afterOpening);
                return;
            }

            var next = text.IndexOfOrdinal('\n', lineStart);
            if (next < 0)
                break;

            lineStart = next + 1;
        }

        throw context.Error(position, "The front matter is not closed by a line '---'.");
    }

    /// <summary>Indicates whether a line of three dashes, and nothing but spaces, starts at a position.</summary>
    private static bool IsFenceAt(string text, int position, out int end)
    {
        end = position;
        if (position + 3 > text.Length || string.CompareOrdinal(text, position, "---", 0, 3) != 0)
            return false;

        var i = position + 3;
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        if (i < text.Length && text[i] != '\n')
            return false;

        end = Math.Min(i + 1, text.Length);
        return true;
    }

    private static void ApplyFrontMatter(ParseContext context, string yaml, int offset)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return;

        var root = LoadYaml(context, yaml, offset);
        if (root is not Dictionary<string, object?> map)
        {
            if (root is not null)
                throw context.Error(offset, "The front matter must be a YAML mapping.");

            return;
        }

        if (map.TryGetValue("title", out var title) && title is string titleText)
        {
            context.Title = titleText;
        }

        if (map.TryGetValue("config", out var config) && config is Dictionary<string, object?> configMap)
        {
            context.Configuration.Merge(configMap);
        }

        if (map.TryGetValue("displayMode", out var displayMode) && displayMode is string mode)
        {
            context.Configuration.Merge(new Dictionary<string, object?>(StringComparer.Ordinal) { ["gantt"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["displayMode"] = mode } });
        }
    }

    private static object? LoadYaml(ParseContext context, string yaml, int offset)
    {
        try
        {
            var stream = YamlStream.Load(new StringReader(yaml));
            return stream.Count is 0 ? null : MermaidConfiguration.FromYaml(stream[0].Contents);
        }
        catch (Exception exception) when (exception is YamlException or InvalidOperationException or ArgumentException or FormatException or OverflowException or InvalidCastException or NotSupportedException or KeyNotFoundException)
        {
            throw context.Error(offset, "The YAML is not valid: " + exception.Message);
        }
    }

    /// <summary>Reads the directives <c>%%{type: arguments}%%</c>: <c>init</c>, <c>initialize</c> and <c>config</c> set the configuration, <c>wrap</c> wraps the texts.</summary>
    private static void ReadDirectives(ParseContext context, StringBuilder buffer)
    {
        var text = context.Text;
        var position = 0;
        while ((position = text.IndexOf("%%{", position, StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf("}%%", position + 3, StringComparison.Ordinal);
            if (end < 0)
                throw context.Error(position, "The directive is not closed by '}%%'.");

            var content = text[(position + 3)..end].Trim();
            var colon = content.IndexOf(':', StringComparison.Ordinal);
            var type = (colon < 0 ? content : content[..colon]).Trim();
            var arguments = colon < 0 ? string.Empty : content[(colon + 1)..].Trim();
            switch (type)
            {
                case "init" or "initialize" or "config":
                    if (arguments.Length > 0)
                    {
                        if (LoadYaml(context, arguments, position + 3) is Dictionary<string, object?> map)
                        {
                            // The keys of the old configuration of the directives: themeVariables, theme... are at the root
                            context.Configuration.Merge(map);
                        }
                        else
                        {
                            throw context.Error(position, "The arguments of the directive must be a mapping.");
                        }
                    }

                    break;
                case "wrap":
                    context.Configuration.Merge(new Dictionary<string, object?>(StringComparer.Ordinal) { ["wrap"] = "true" });
                    break;
                default:
                    context.Warn(position, $"The directive '{type}' is not supported.");
                    break;
            }

            Blank(buffer, position, end + 3);
            position = end + 3;
        }
    }

    /// <summary>Replaces the lines that start with <c>%%</c> by spaces.</summary>
    private static void RemoveComments(StringBuilder buffer)
    {
        var lineStart = 0;
        for (var i = 0; i <= buffer.Length; i++)
        {
            if (i < buffer.Length && buffer[i] != '\n')
                continue;

            var j = lineStart;
            while (j < i && buffer[j] is ' ' or '\t')
            {
                j++;
            }

            if (j + 1 < i && buffer[j] == '%' && buffer[j + 1] == '%')
            {
                Blank(buffer, j, i);
            }

            lineStart = i + 1;
        }
    }

    /// <summary>Replaces a range by spaces, keeping the line feeds.</summary>
    private static void Blank(StringBuilder buffer, int start, int end)
    {
        for (var i = start; i < end && i < buffer.Length; i++)
        {
            if (buffer[i] != '\n')
            {
                buffer[i] = ' ';
            }
        }
    }
}
