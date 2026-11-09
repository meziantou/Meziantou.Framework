using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.TimelineDiagrams;

/// <summary>Writes a <see cref="TimelineDiagram"/> as Mermaid: the periods outside the sections, then the sections, one period per line with its events.</summary>
internal static class TimelineDiagramWriter
{
    public static void Write(StringBuilder builder, TimelineDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.DisableMulticolor)
        {
            configuration.Add("disableMulticolor: true");
        }

        // The title is a statement of the diagram, which holds one line
        var titleStatement = diagram.Title is { Length: > 0 } title && title.AsSpan().IndexOfAny("\r\n\u2028\u2029\u0085") < 0 && title.Trim().Length == title.Length && !title.Contains("%%", StringComparison.Ordinal);
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "timeline", configuration, includeTitle: !titleStatement);
        builder.Append(diagram.Direction is TimelineDirection.TopToBottom ? "timeline TD\n" : "timeline\n");
        if (titleStatement)
        {
            builder.Append("    title ").Append(diagram.Title).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        foreach (var period in diagram.Periods)
        {
            WritePeriod(builder, period, 1);
        }

        foreach (var section in diagram.Sections)
        {
            builder.Append("    section ").Append(EncodeText(section.Title, "section title")).Append('\n');
            foreach (var period in section.Periods)
            {
                WritePeriod(builder, period, 2);
            }
        }
    }

    private static void WritePeriod(StringBuilder builder, TimelinePeriod period, int indent)
    {
        var label = EncodePeriod(period.Label.Text);
        builder.AppendIndent(indent).Append(label);

        // "title : event" would be a title: the colon follows the keyword
        var separator = label.Equals("title", StringComparison.OrdinalIgnoreCase) || label.Equals("section", StringComparison.OrdinalIgnoreCase) ? ": " : " : ";
        foreach (var item in period.Events)
        {
            builder.Append(separator).Append(EncodeText(item, "event"));
            separator = " : ";
        }

        builder.Append('\n');
    }

    /// <summary>
    /// Encodes a period. Mermaid reads a <c>#</c> in a period as a comment, so the period cannot use the entity codes
    /// of Mermaid: the characters that the decoding changes become named HTML entities.
    /// </summary>
    private static string EncodePeriod(string value)
    {
        if (value.Length is 0 || value.AsSpan().IndexOfAny(":#\r") >= 0 || value.Contains("%%", StringComparison.Ordinal))
            throw new ArgumentException($"The period '{value}' cannot be written as Mermaid: it is empty, or contains a colon, '#' or '%%'.", nameof(value));

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '\n':
                    builder.Append("<br>");
                    break;
                case '\u00A0':
                    builder.Append("&nbsp;");
                    break;
                case '\u2002':
                    builder.Append("&ensp;");
                    break;
                case '\u2003':
                    builder.Append("&emsp;");
                    break;
                case '\u2009':
                    builder.Append("&thinsp;");
                    break;
                default:
                    if ((i == 0 || i == value.Length - 1) && char.IsWhiteSpace(c))
                        throw new ArgumentException($"The period '{value}' cannot be written as Mermaid: it starts or ends with white space.", nameof(value));

                    builder.Append(c);
                    break;
            }
        }

        // A line that starts with a keyword is not a period
        var text = builder.ToString();
        if (text.StartsWith("timeline", StringComparison.OrdinalIgnoreCase) || text.StartsWith("accTitle", StringComparison.OrdinalIgnoreCase) || text.StartsWith("accDescr", StringComparison.OrdinalIgnoreCase)
            || IsKeywordStatement(text, "title") || IsKeywordStatement(text, "section"))
            throw new ArgumentException($"The period '{value}' cannot be written as Mermaid: it starts with a keyword.", nameof(value));

        return text;
    }

    private static bool IsKeywordStatement(string text, string keyword) => text.Length > keyword.Length && text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) && TimelineDiagramParser.IsKeywordSeparator(text[keyword.Length]);

    /// <summary>Encodes a section title or an event: the colons and the white space at the ends become entity codes.</summary>
    private static string EncodeText(DiagramText text, string kind)
    {
        var value = text.Text;
        if (value.Length is 0)
            throw new ArgumentException($"The {kind} cannot be written as Mermaid: its text is empty.", nameof(text));

        var start = 0;
        var end = value.Length;
        var prefix = string.Empty;
        var suffix = string.Empty;
        if (char.IsWhiteSpace(value[0]))
        {
            prefix = Entity(value[0]);
            start = 1;
        }

        if (end > start && char.IsWhiteSpace(value[^1]))
        {
            suffix = Entity(value[^1]);
            end--;
        }

        return prefix + MermaidText.EncodePlain(value[start..end]).Replace(":", "#58;", StringComparison.Ordinal) + suffix;
    }

    private static string Entity(char c) => "#" + ((int)c).ToString(CultureInfo.InvariantCulture) + ";";
}
