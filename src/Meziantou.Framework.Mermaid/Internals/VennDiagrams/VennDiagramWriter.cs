using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.VennDiagrams;

/// <summary>
/// Writes a <see cref="VennDiagram"/> as Mermaid: the sets and the unions in order, each followed by its texts as indented
/// <c>text</c> lines, then the other texts and the styles. The texts are quoted strings with entity codes for the
/// characters Mermaid would read differently.
/// </summary>
internal static class VennDiagramWriter
{
    private static readonly string[] Keywords = ["set", "union", "text", "style", "venn-beta"];

    public static void Write(StringBuilder builder, VennDiagram diagram)
    {
        // The grammar has no accessibility statements
        if (diagram.AccessibleTitle is not null || diagram.Description is not null)
            throw new ArgumentException("A Venn diagram cannot have an accessible title nor a description in Mermaid.", nameof(diagram));

        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.Width != 800)
        {
            configuration.Add("width: " + MermaidStyle.Number(diagram.Width));
        }

        if (diagram.Height != 450)
        {
            configuration.Add("height: " + MermaidStyle.Number(diagram.Height));
        }

        var variables = new List<string>();
        foreach (var (index, color) in diagram.SetColors.OrderBy(entry => entry.Key))
        {
            if (index is >= 0 and < 8)
            {
                variables.Add("\"venn" + (index + 1).ToString(CultureInfo.InvariantCulture) + "\": \"" + color + "\"");
            }
        }

        if (diagram.TextColor is { } textColor)
        {
            variables.Add("\"vennSetTextColor\": \"" + textColor + "\"");
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "venn", configuration, includeTitle: false);
        if (variables.Count > 0)
        {
            // The colors of the sets are theme variables, which a directive sets
            builder.Append("%%{init: {\"themeVariables\": {").Append(string.Join(", ", variables)).Append("}}}%%\n");
        }

        builder.Append("venn-beta\n");
        if (!string.IsNullOrEmpty(diagram.Title))
        {
            var title = diagram.Title;
            WriteLine(builder, "    ", encodeColon => "title " + Encode(title, encodeColon, trimmed: true, title: true));
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);
        var textIndex = 0;
        foreach (var area in diagram.Areas)
        {
            if (area.IsSet)
            {
                declared.Add(area.Sets[0]);
                if (declared.Count > VennDiagramParser.MaxSets)
                    throw new ArgumentException($"A Venn diagram can have at most {VennDiagramParser.MaxSets} sets in Mermaid.", nameof(diagram));

                WriteLine(builder, "    ", encodeColon => "set " + Identifier(area.Sets[0], encodeColon) + LabelAndSize(area, encodeColon));
            }
            else
            {
                if (area.Sets.Count > VennDiagramParser.MaxSets)
                    throw new ArgumentException($"A union can have at most {VennDiagramParser.MaxSets} sets in Mermaid.", nameof(diagram));

                if (area.Sets.FirstOrDefault(set => !declared.Contains(set)) is { } unknown)
                    throw new ArgumentException($"The set '{unknown}' of a union must be declared by a set before the union in Mermaid.", nameof(diagram));

                WriteLine(builder, "    ", encodeColon => "union " + Identifiers(area.Sets, encodeColon) + LabelAndSize(area, encodeColon));
            }

            // The texts of this area that come next are indented under it
            while (textIndex < diagram.TextNodes.Count && diagram.TextNodes[textIndex].Sets.SequenceEqual(area.Sets, StringComparer.Ordinal))
            {
                var node = diagram.TextNodes[textIndex++];
                WriteLine(builder, "        ", encodeColon => "text " + Identifier(node.Id, encodeColon) + Label(node.Label, encodeColon));
            }
        }

        // The other texts are not indented: an indented text after a set would be one of its texts
        for (; textIndex < diagram.TextNodes.Count; textIndex++)
        {
            var node = diagram.TextNodes[textIndex];
            WriteLine(builder, "", encodeColon => "text " + Identifiers(node.Sets, encodeColon) + " " + Identifier(node.Id, encodeColon) + Label(node.Label, encodeColon));
        }

        foreach (var rule in diagram.Styles)
        {
            var properties = FormatStyle(rule.Style);
            if (properties.Length > 0)
            {
                WriteLine(builder, "    ", encodeColon => "style " + Identifiers(rule.Targets, encodeColon) + " " + properties);
            }
        }
    }

    /// <summary>Writes a line; when Mermaid would remove one of its semicolons (a line with <c>style</c>, a colon and a <c>#</c>), its colons are written as entity codes.</summary>
    private static void WriteLine(StringBuilder builder, string indent, Func<bool, string> line)
    {
        var text = indent + line(false);
        if (VennDiagramLexer.IsAlteredByEncoding(text))
        {
            text = indent + line(true);
        }

        builder.Append(text).Append('\n');
    }

    private static string LabelAndSize(VennArea area, bool encodeColon)
    {
        var result = Label(area.Label, encodeColon);
        return area.Size is { } size ? result + ":" + FormatNumber(size) : result;
    }

    /// <summary>Writes a label in brackets; an empty label is a space between quotes, which Mermaid trims to an empty label.</summary>
    private static string Label(string? label, bool encodeColon) => label switch
    {
        null => "",
        "" => "[\" \"]",
        _ => "[\"" + Encode(label, encodeColon, trimmed: true, title: false) + "\"]",
    };

    private static string Identifiers(IEnumerable<string> identifiers, bool encodeColon) => string.Join(",", identifiers.Select(identifier => Identifier(identifier, encodeColon)));

    /// <summary>Writes an identifier as a word when the lexer reads it as one, and as a quoted string otherwise.</summary>
    private static string Identifier(string identifier, bool encodeColon) => IsWord(identifier) ? identifier : "\"" + Encode(identifier, encodeColon, trimmed: false, title: false) + "\"";

    private static bool IsWord(string value)
    {
        if (value.Length is 0 || !(char.IsAsciiLetter(value[0]) || value[0] == '_'))
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
                return false;
        }

        // A keyword at the start of a word is read as the keyword, and "title" followed by a space as a title
        foreach (var keyword in Keywords)
        {
            if (value.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) && (value.Length == keyword.Length || !VennDiagramLexer.IsWordCharacter(value[keyword.Length])))
                return false;
        }

        return !value.Equals("title", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Encodes a text as entity codes where Mermaid would read it differently: the quotes, the entity codes, <c>%%</c>
    /// (a comment), the line breaks and the other white space but the spaces, and the spaces at the ends of a text that
    /// Mermaid trims. A title cannot hold <c>#</c> nor <c>;</c>.
    /// </summary>
    private static string Encode(string value, bool encodeColon, bool trimmed, bool title)
    {
        var start = 0;
        var end = value.Length;
        if (trimmed)
        {
            while (start < end && VennDiagramLexer.IsWhiteSpace(value[start]))
            {
                start++;
            }

            while (end > start && VennDiagramLexer.IsWhiteSpace(value[end - 1]))
            {
                end--;
            }
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"' when !title:
                    builder.Append("#quot;");
                    break;
                case '#':
                    builder.Append("#35;");
                    break;
                case ';' when title:
                    builder.Append("#59;");
                    break;
                case '&':
                    builder.Append("#amp;");
                    break;
                case '%' when i + 1 < value.Length && value[i + 1] == '%':
                    builder.Append("#37;");
                    break;
                case ':' when encodeColon:
                    builder.Append("#58;");
                    break;
                case ' ' when i >= start && i < end:
                    builder.Append(' ');
                    break;
                default:
                    if ((VennDiagramLexer.IsWhiteSpace(c) || char.IsControl(c)) && TryEncodeNumeric(c, out var entity))
                    {
                        builder.Append(entity);
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Returns the numeric entity code of a character, when the parser decodes it back to the character.</summary>
    private static bool TryEncodeNumeric(char c, out string entity)
    {
        entity = "#" + ((int)c).ToString(CultureInfo.InvariantCulture) + ";";
        return MermaidText.DecodeEntities(entity) == c.ToString();
    }

    /// <summary>Writes the properties of a style that Mermaid reads.</summary>
    private static string FormatStyle(VennStyle style)
    {
        var properties = new List<string>();
        if (style.Fill is { } fill)
        {
            properties.Add("fill: " + fill);
        }

        if (style.FillOpacity is { } fillOpacity && double.IsFinite(fillOpacity))
        {
            properties.Add("fill-opacity: " + FormatNumber(fillOpacity));
        }

        if (style.Stroke is { } stroke)
        {
            properties.Add("stroke: " + stroke);
        }

        if (style.StrokeWidth is { } strokeWidth && strokeWidth >= 0 && double.IsFinite(strokeWidth))
        {
            properties.Add("stroke-width: " + FormatNumber(strokeWidth));
        }

        if (style.TextColor is { } textColor)
        {
            properties.Add("color: " + textColor);
        }

        return string.Join(", ", properties);
    }

    /// <summary>Writes a number in decimal notation, without exponent, which the lexer of Mermaid reads as one number.</summary>
    internal static string FormatNumber(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex < 0)
            return text;

        var mantissa = text[..exponentIndex];
        var exponent = int.Parse(text[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var negative = mantissa.StartsWith('-', StringComparison.Ordinal);
        if (negative)
        {
            mantissa = mantissa[1..];
        }

        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        var newPoint = (point < 0 ? mantissa.Length : point) + exponent;
        string result;
        if (newPoint <= 0)
        {
            result = "0." + new string('0', -newPoint) + digits;
        }
        else if (newPoint >= digits.Length)
        {
            result = digits + new string('0', newPoint - digits.Length);
        }
        else
        {
            result = digits[..newPoint] + "." + digits[newPoint..];
        }

        return negative ? "-" + result : result;
    }
}
