using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals;

internal static class MermaidWriterHelpers
{
    /// <summary>Writes the front matter: the title, the theme when it is one of the themes of Mermaid, and the configuration of the diagram.</summary>
    public static void WriteFrontMatter(StringBuilder builder, Diagram diagram, string section, List<string> configuration, bool includeTitle = true)
    {
        // An empty title is read back as no title
        var title = includeTitle && !string.IsNullOrEmpty(diagram.Title) ? diagram.Title : null;
        var themeName = diagram.Theme switch
        {
            var theme when ReferenceEquals(theme, DiagramTheme.Dark) => "dark",
            var theme when ReferenceEquals(theme, DiagramTheme.Forest) => "forest",
            var theme when ReferenceEquals(theme, DiagramTheme.Neutral) => "neutral",
            var theme when ReferenceEquals(theme, DiagramTheme.Base) => "base",
            _ => null,
        };

        if (title is null && themeName is null && configuration.Count is 0)
            return;

        builder.Append("---\n");
        if (title is not null)
        {
            builder.Append("title: ").Append(YamlDoubleQuoted(title)).Append('\n');
        }

        if (themeName is not null || configuration.Count > 0)
        {
            builder.Append("config:\n");
            if (themeName is not null)
            {
                builder.Append("  theme: ").Append(themeName).Append('\n');
            }

            if (configuration.Count > 0)
            {
                builder.Append("  ").Append(section).Append(":\n");
                foreach (var line in configuration)
                {
                    builder.Append("    ").Append(line).Append('\n');
                }
            }
        }

        builder.Append("---\n");
    }

    public static void WriteAccessibility(StringBuilder builder, Diagram diagram, int indent)
    {
        if (diagram.AccessibleTitle is { } title)
        {
            builder.AppendIndent(indent).Append("accTitle: ").Append(SingleLine(title, nameof(diagram.AccessibleTitle))).Append('\n');
        }

        if (diagram.Description is { } description)
        {
            if (description.Contains('\n', StringComparison.Ordinal))
            {
                if (description.Contains('}', StringComparison.Ordinal))
                    throw new ArgumentException("A description of several lines cannot contain '}' in Mermaid.", nameof(diagram));

                builder.AppendIndent(indent).Append("accDescr {\n");
                foreach (var line in description.Split('\n'))
                {
                    builder.AppendIndent(indent + 1).Append(SingleLine(line, nameof(diagram.Description))).Append('\n');
                }

                builder.AppendIndent(indent).Append("}\n");
            }
            else
            {
                builder.AppendIndent(indent).Append("accDescr: ").Append(SingleLine(description, nameof(diagram.Description))).Append('\n');
            }
        }
    }

    private static string SingleLine(string value, string name)
    {
        if (value.AsSpan().IndexOfAny('\r', '\n') >= 0 || value.AsSpan().IndexOfAny('\u2028', '\u2029', '\u0085') >= 0)
            throw new ArgumentException($"The {name} cannot contain a line break in Mermaid.", name);

        return value.Trim();
    }

    public static string YamlSingleQuoted(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>Writes a YAML double-quoted string, with escapes for the characters YAML does not allow as is.</summary>
    public static string YamlDoubleQuoted(string value)
    {
        var builder = new StringBuilder("\"");
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '%' when i > 0 && value[i - 1] is '%':
                    // %% starts a directive or a comment anywhere in the text, front matter included
                    builder.Append("\\u0025");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        builder.Append(c).Append(value[i + 1]);
                        i++;
                    }
                    else if (char.IsSurrogate(c))
                    {
                        // YAML cannot hold a lone surrogate, even escaped: it becomes the replacement character
                        builder.Append('\uFFFD');
                    }
                    else if (char.IsControl(c) || c is '\u2028' or '\u2029' or '\u0085' or '\uFEFF' or '\uFFFE' or '\uFFFF')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
