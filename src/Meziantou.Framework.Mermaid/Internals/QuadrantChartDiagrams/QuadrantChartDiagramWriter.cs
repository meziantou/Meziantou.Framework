using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.QuadrantChartDiagrams;

/// <summary>
/// Writes a <see cref="QuadrantChart"/> as Mermaid: its configuration in the front matter, its colors in a
/// directive, then the axes, the quadrants, the points and the classes. A text is written as is when it reads back the
/// same, between quotes otherwise; Mermaid has no escapes, so a text with a double quote cannot be written.
/// </summary>
internal static class QuadrantChartDiagramWriter
{
    private static readonly QuadrantChart Defaults = new();

    public static void Write(StringBuilder builder, QuadrantChart diagram)
    {
        var configuration = new List<string>();

        AddNumber("chartWidth", diagram.Width, Defaults.Width);
        AddNumber("chartHeight", diagram.Height, Defaults.Height);
        AddNumber("quadrantPadding", diagram.QuadrantPadding, Defaults.QuadrantPadding);
        AddNumber("quadrantTextTopPadding", diagram.QuadrantTextTopPadding, Defaults.QuadrantTextTopPadding);
        AddNumber("quadrantLabelFontSize", diagram.QuadrantLabelFontSize, Defaults.QuadrantLabelFontSize);
        AddNumber("quadrantInternalBorderStrokeWidth", diagram.InternalBorderWidth, Defaults.InternalBorderWidth);
        AddNumber("quadrantExternalBorderStrokeWidth", diagram.ExternalBorderWidth, Defaults.ExternalBorderWidth);
        AddNumber("xAxisLabelPadding", diagram.XAxisLabelPadding, Defaults.XAxisLabelPadding);
        AddNumber("xAxisLabelFontSize", diagram.XAxisLabelFontSize, Defaults.XAxisLabelFontSize);
        AddNumber("yAxisLabelPadding", diagram.YAxisLabelPadding, Defaults.YAxisLabelPadding);
        AddNumber("yAxisLabelFontSize", diagram.YAxisLabelFontSize, Defaults.YAxisLabelFontSize);
        AddNumber("pointTextPadding", diagram.PointTextPadding, Defaults.PointTextPadding);
        AddNumber("pointLabelFontSize", diagram.PointLabelFontSize, Defaults.PointLabelFontSize);
        AddNumber("pointRadius", diagram.PointRadius, Defaults.PointRadius);
        if (diagram.XAxisPosition is QuadrantChartXAxisPosition.Bottom)
        {
            configuration.Add("xAxisPosition: bottom");
        }

        if (diagram.YAxisPosition is QuadrantChartYAxisPosition.Right)
        {
            configuration.Add("yAxisPosition: right");
        }

        var colors = diagram.Colors;
        var variables = new List<string>();
        AddColor("quadrant1Fill", colors.Quadrant1Fill);
        AddColor("quadrant2Fill", colors.Quadrant2Fill);
        AddColor("quadrant3Fill", colors.Quadrant3Fill);
        AddColor("quadrant4Fill", colors.Quadrant4Fill);
        AddColor("quadrant1TextFill", colors.Quadrant1TextFill);
        AddColor("quadrant2TextFill", colors.Quadrant2TextFill);
        AddColor("quadrant3TextFill", colors.Quadrant3TextFill);
        AddColor("quadrant4TextFill", colors.Quadrant4TextFill);
        AddColor("quadrantPointFill", colors.PointFill);
        AddColor("quadrantPointTextFill", colors.PointTextFill);
        AddColor("quadrantXAxisTextFill", colors.XAxisTextFill);
        AddColor("quadrantYAxisTextFill", colors.YAxisTextFill);
        AddColor("quadrantInternalBorderStrokeFill", colors.InternalBorderStrokeFill);
        AddColor("quadrantExternalBorderStrokeFill", colors.ExternalBorderStrokeFill);

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "quadrantChart", configuration);
        if (variables.Count > 0)
        {
            builder.Append("%%{init: {\"themeVariables\": {").Append(string.Join(", ", variables)).Append("}}}%%\n");
        }

        builder.Append("quadrantChart\n");
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        WriteAxis(builder, "x-axis", diagram.XAxisLeftLabel, diagram.XAxisRightLabel);
        WriteAxis(builder, "y-axis", diagram.YAxisBottomLabel, diagram.YAxisTopLabel);
        WriteQuadrant(builder, "quadrant-1", diagram.Quadrant1Label);
        WriteQuadrant(builder, "quadrant-2", diagram.Quadrant2Label);
        WriteQuadrant(builder, "quadrant-3", diagram.Quadrant3Label);
        WriteQuadrant(builder, "quadrant-4", diagram.Quadrant4Label);
        foreach (var point in diagram.Points)
        {
            builder.Append("    ").Append(FormatText(point.Label, allowEmpty: true));
            if (point.ClassName is { } className)
            {
                if (className.Length is 0 || !className.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    throw new ArgumentException($"The class name '{className}' cannot be written in Mermaid: it must be made of letters, digits and underscores.", nameof(diagram));

                builder.Append(":::").Append(className);
            }

            builder.Append(": [").Append(FormatCoordinate(point.X)).Append(", ").Append(FormatCoordinate(point.Y)).Append(']');
            if (!point.Style.IsEmpty)
            {
                builder.Append(' ').Append(FormatStyle(point.Style));
            }

            builder.Append('\n');
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            // A class without style changes nothing, and Mermaid needs a style after its name
            if (style.IsEmpty)
                continue;

            if (!IsClassName(name))
                throw new ArgumentException($"The class name '{name}' cannot be written in Mermaid.", nameof(diagram));

            builder.Append("    classDef ").Append(name).Append(' ').Append(FormatStyle(style)).Append('\n');
        }

        void AddNumber(string name, double value, double defaultValue)
        {
            if (value != defaultValue)
            {
                configuration.Add(name + ": " + MermaidStyle.Number(value));
            }
        }

        void AddColor(string name, SceneColor? color)
        {
            if (color is { } value)
            {
                variables.Add("\"" + name + "\": \"" + value.ToString() + "\"");
            }
        }
    }

    private static void WriteAxis(StringBuilder builder, string keyword, DiagramText? first, DiagramText? second)
    {
        var hasFirst = first is not null && QuadrantChartLexer.Trim(first.Text).Length > 0;
        var hasSecond = second is not null && QuadrantChartLexer.Trim(second.Text).Length > 0;
        if (!hasFirst && !hasSecond)
            return;

        // The second text needs a first one: a blank string reads as an empty text
        builder.Append("    ").Append(keyword).Append(' ').Append(hasFirst ? FormatText(first!, allowEmpty: false) : "\" \"");
        if (hasSecond)
        {
            builder.Append(" --> ").Append(FormatText(second!, allowEmpty: false));
        }

        builder.Append('\n');
    }

    private static void WriteQuadrant(StringBuilder builder, string keyword, DiagramText? label)
    {
        if (label is not null && QuadrantChartLexer.Trim(label.Text).Length > 0)
        {
            builder.Append("    ").Append(keyword).Append(' ').Append(FormatText(label, allowEmpty: false)).Append('\n');
        }
    }

    private static string FormatStyle(QuadrantChartPointStyle style)
    {
        var parts = new List<string>();
        if (style.Radius is { } radius)
        {
            parts.Add("radius: " + radius.ToString(CultureInfo.InvariantCulture));
        }

        if (style.Color is { } color)
        {
            parts.Add("color: " + FormatColor(color));
        }

        if (style.StrokeColor is { } strokeColor)
        {
            parts.Add("stroke-color: " + FormatColor(strokeColor));
        }

        if (style.StrokeWidth is { } strokeWidth)
        {
            parts.Add("stroke-width: " + strokeWidth.ToString(CultureInfo.InvariantCulture) + "px");
        }

        return string.Join(", ", parts);
    }

    private static string FormatColor(SceneColor color)
    {
        if (!color.IsOpaque)
            throw new ArgumentException($"The color {color} cannot be written in Mermaid: the points accept only opaque hexadecimal colors.", nameof(color));

        return color.ToHexRgb();
    }

    /// <summary>Writes a coordinate as Mermaid reads it: 0, 1, or 0 followed by a decimal point and digits, without exponent.</summary>
    internal static string FormatCoordinate(double value)
    {
        if (value <= 0)
            return "0";

        if (value >= 1)
            return "1";

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.Contains('E', StringComparison.Ordinal))
        {
            text = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            if (text.Contains('.', StringComparison.Ordinal))
            {
                text = text.TrimEnd('0').TrimEnd('.');
            }
        }

        return text is "0" or "" ? "0" : text;
    }

    /// <summary>Writes a text: as is when it reads back as the same text, between quotes or as a markdown string otherwise.</summary>
    private static string FormatText(DiagramText label, bool allowEmpty)
    {
        var text = QuadrantChartLexer.Trim(label.Text);
        if (text.Length is 0)
        {
            if (!allowEmpty)
                throw new ArgumentException("The text cannot be empty.", nameof(label));

            // A blank string reads as an empty text
            return "\" \"";
        }

        if (label.Format is DiagramTextFormat.Markdown)
        {
            if (text.Contains('`', StringComparison.Ordinal) || text.Contains('"', StringComparison.Ordinal) || !CanQuote(text))
                throw new ArgumentException($"The markdown text '{label.Text}' cannot be written in Mermaid: it cannot contain a backquote nor a double quote.", nameof(label));

            return "\"`" + text + "`\"";
        }

        if (IsPlainText(text))
            return text;

        // Mermaid reads "%% as a comment, and "` as the start of a markdown string
        if (text.Contains('"', StringComparison.Ordinal) || text.StartsWith('`', StringComparison.Ordinal) || text.StartsWith("%%", StringComparison.Ordinal) || !CanQuote(text))
            throw new ArgumentException($"The text '{label.Text}' cannot be written in Mermaid: it cannot contain a double quote, nor start with a backquote or '%%' when it needs quotes.", nameof(label));

        return "\"" + text + "\"";
    }

    /// <summary>Indicates whether a text reads back the same as the words of a text, without quotes.</summary>
    private static bool IsPlainText(string text)
    {
        var tokens = QuadrantChartLexer.Tokenize(text, 0, text.Length);
        if (tokens.Count < 2 || !tokens[0].IsAlphaNumeric)
            return false;

        var position = 0;
        foreach (var token in tokens)
        {
            if (token.Kind is QuadrantChartTokenKind.End)
                return position == text.Length;

            if (!token.IsText || token.Start != position)
                return false;

            position = token.End;
        }

        return false;
    }

    /// <summary>Indicates whether a class name reads back as the same name.</summary>
    private static bool IsClassName(string name)
    {
        var tokens = QuadrantChartLexer.Tokenize(name, 0, name.Length);
        var position = 0;
        foreach (var token in tokens)
        {
            if (token.Kind is QuadrantChartTokenKind.End)
                return position == name.Length && position > 0;

            if (!token.IsClassNamePart || token.Start != position)
                return false;

            position = token.End;
        }

        return false;
    }

    /// <summary>Indicates whether a text can be written between quotes: the line breaks are normalized, and the directives and the comment lines are removed before the parsing.</summary>
    private static bool CanQuote(string text)
    {
        if (text.Contains('\r', StringComparison.Ordinal) || text.Contains("%%{", StringComparison.Ordinal))
            return false;

        foreach (var line in text.Split('\n').Skip(1))
        {
            if (line.TrimStart(' ', '\t').StartsWith("%%", StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
