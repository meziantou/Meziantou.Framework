using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.XyChartDiagrams;

/// <summary>
/// Writes an <see cref="XyChartDiagram"/> as Mermaid: its options in the front matter, its colors in a directive, then
/// the title, the axes and the series.
/// </summary>
internal static class XyChartDiagramWriter
{
    public static void Write(StringBuilder builder, XyChartDiagram diagram)
    {
        Validate(diagram);
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        AddNumber(configuration, "width", diagram.Width, 700);
        AddNumber(configuration, "height", diagram.Height, 500);
        AddNumber(configuration, "titleFontSize", diagram.TitleFontSize, 20);
        AddNumber(configuration, "titlePadding", diagram.TitlePadding, 10);
        AddBoolean(configuration, "showTitle", diagram.ShowTitle, defaultValue: true);
        AddBoolean(configuration, "showLegend", diagram.ShowLegend, defaultValue: true);
        AddNumber(configuration, "legendFontSize", diagram.LegendFontSize, 14);
        AddNumber(configuration, "legendPadding", diagram.LegendPadding, 10);
        AddNumber(configuration, "plotReservedSpacePercent", diagram.PlotReservedSpacePercent, 50);
        AddBoolean(configuration, "showDataLabel", diagram.ShowDataLabels, defaultValue: false);
        AddBoolean(configuration, "showDataLabelOutsideBar", diagram.DataLabelsOutsideBars, defaultValue: false);
        AddAxis(configuration, "xAxis", diagram.XAxis);
        AddAxis(configuration, "yAxis", diagram.YAxis);
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "xyChart", configuration, includeTitle: false);

        var variables = new List<string>();
        AddColor(variables, "backgroundColor", diagram.BackgroundColor);
        AddColor(variables, "titleColor", diagram.TitleColor);
        AddColor(variables, "dataLabelColor", diagram.DataLabelColor);
        AddColor(variables, "legendTextColor", diagram.LegendTextColor);
        foreach (var (prefix, axis) in new[] { ("xAxis", diagram.XAxis), ("yAxis", diagram.YAxis) })
        {
            AddColor(variables, prefix + "TitleColor", axis.TitleColor);
            AddColor(variables, prefix + "LabelColor", axis.LabelColor);
            AddColor(variables, prefix + "TickColor", axis.TickColor);
            AddColor(variables, prefix + "LineColor", axis.LineColor);
        }

        if (diagram.PlotColors.Count > 0)
        {
            variables.Add("\"plotColorPalette\": \"" + string.Join(", ", diagram.PlotColors.Select(color => color.ToString())) + "\"");
        }

        if (variables.Count > 0)
        {
            // The colors of the chart are theme variables, which a directive sets
            builder.Append("%%{init: {\"themeVariables\": {\"xyChart\": {").Append(string.Join(", ", variables)).Append("}}}}%%\n");
        }

        builder.Append("xychart");
        if (diagram.Orientation is XyChartOrientation.Horizontal)
        {
            builder.Append(" horizontal");
        }

        builder.Append('\n');
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        if (!string.IsNullOrEmpty(diagram.Title))
        {
            builder.Append("    title ").Append(XyChartStrings.Format(diagram.Title, allowDigits: true)).Append('\n');
        }

        WriteXAxis(builder, diagram.XAxis);
        WriteYAxis(builder, diagram.YAxis);
        foreach (var series in diagram.Series)
        {
            WriteSeries(builder, series);
        }
    }

    private static void Validate(XyChartDiagram diagram)
    {
        if (diagram.YAxis.Categories.Count > 0)
            throw new ArgumentException("The y axis of an XY chart cannot have categories in Mermaid.", nameof(diagram));

        foreach (var range in new[] { diagram.XAxis.Categories.Count is 0 ? diagram.XAxis.Range : null, diagram.YAxis.Range })
        {
            if (range is { } value && !(double.IsFinite(value.Start) && double.IsFinite(value.End)))
                throw new ArgumentException("The range of an axis of an XY chart must be finite numbers in Mermaid.", nameof(diagram));
        }

        foreach (var series in diagram.Series)
        {
            if (series.Values.Count is 0)
                throw new ArgumentException("A series of an XY chart must have at least one value in Mermaid.", nameof(diagram));

            if (!series.Values.All(double.IsFinite))
                throw new ArgumentException("The values of an XY chart must be finite numbers in Mermaid.", nameof(diagram));
        }
    }

    private static void WriteXAxis(StringBuilder builder, XyChartAxis axis)
    {
        if (string.IsNullOrEmpty(axis.Title) && axis.Categories.Count is 0 && axis.Range is null)
            return;

        builder.Append("    x-axis");
        AppendAxisTitle(builder, axis);
        if (axis.Categories.Count > 0)
        {
            builder.Append(" [").Append(string.Join(", ", axis.Categories.Select(category => XyChartStrings.Format(category, allowDigits: true)))).Append(']');
        }
        else if (axis.Range is { } range)
        {
            AppendRange(builder, range);
        }

        builder.Append('\n');
    }

    private static void WriteYAxis(StringBuilder builder, XyChartAxis axis)
    {
        if (string.IsNullOrEmpty(axis.Title) && axis.Range is null)
            return;

        builder.Append("    y-axis");
        AppendAxisTitle(builder, axis);
        if (axis.Range is { } range)
        {
            AppendRange(builder, range);
        }

        builder.Append('\n');
    }

    private static void AppendAxisTitle(StringBuilder builder, XyChartAxis axis)
    {
        if (!string.IsNullOrEmpty(axis.Title))
        {
            // After x-axis and y-axis, digits are numbers
            builder.Append(' ').Append(XyChartStrings.Format(axis.Title, allowDigits: false));
        }
    }

    private static void AppendRange(StringBuilder builder, XyChartAxisRange range) => builder.Append(' ').Append(FormatNumber(range.Start)).Append(" --> ").Append(FormatNumber(range.End));

    private static void WriteSeries(StringBuilder builder, XyChartSeries series)
    {
        builder.Append(series is XyChartLineSeries ? "    line" : "    bar");
        if (!string.IsNullOrEmpty(series.Name))
        {
            builder.Append(' ').Append(XyChartStrings.Format(series.Name, allowDigits: true));
        }

        builder.Append(" [");
        for (var i = 0; i < series.Values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(FormatNumber(series.Values[i]));
            if (series is XyChartLineSeries line && i < line.PointLabels.Count && line.PointLabels[i] is { Length: > 0 } label)
            {
                builder.Append(' ').Append(XyChartStrings.Quote(label));
            }
        }

        builder.Append("]\n");
    }

    /// <summary>Writes a number as Mermaid reads it: digits and a decimal point, without exponent.</summary>
    internal static string FormatNumber(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var e = text.IndexOf('E', StringComparison.Ordinal);
        if (e < 0)
            return text;

        // Expand the exponent: 1E+21 is 1000000000000000000000, 1.5E-07 is 0.00000015
        var exponent = int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = text[..e];
        var negative = mantissa.StartsWith('-', StringComparison.Ordinal);
        if (negative)
        {
            mantissa = mantissa[1..];
        }

        var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        var point = (dot < 0 ? mantissa.Length : dot) + exponent;
        string result;
        if (point <= 0)
        {
            result = "0." + new string('0', -point) + digits;
        }
        else if (point >= digits.Length)
        {
            result = digits + new string('0', point - digits.Length);
        }
        else
        {
            result = digits[..point] + "." + digits[point..];
        }

        return negative ? "-" + result : result;
    }

    private static void AddNumber(List<string> configuration, string name, double value, double defaultValue)
    {
        if (value != defaultValue)
        {
            configuration.Add(name + ": " + MermaidStyle.Number(value));
        }
    }

    private static void AddBoolean(List<string> configuration, string name, bool value, bool defaultValue)
    {
        if (value != defaultValue)
        {
            configuration.Add(name + ": " + (value ? "true" : "false"));
        }
    }

    private static void AddAxis(List<string> configuration, string name, XyChartAxis axis)
    {
        var lines = new List<string>();
        AddBoolean(lines, "showLabel", axis.ShowLabels, defaultValue: true);
        AddNumber(lines, "labelFontSize", axis.LabelFontSize, 14);
        AddNumber(lines, "labelPadding", axis.LabelPadding, 5);
        AddBoolean(lines, "showTitle", axis.ShowTitle, defaultValue: true);
        AddNumber(lines, "titleFontSize", axis.TitleFontSize, 16);
        AddNumber(lines, "titlePadding", axis.TitlePadding, 5);
        AddBoolean(lines, "showTick", axis.ShowTicks, defaultValue: true);
        AddNumber(lines, "tickLength", axis.TickLength, 5);
        AddNumber(lines, "tickWidth", axis.TickWidth, 2);
        AddBoolean(lines, "showAxisLine", axis.ShowLine, defaultValue: true);
        AddNumber(lines, "axisLineWidth", axis.LineWidth, 2);
        AddNumber(lines, "labelRotation", axis.LabelRotation, 0);
        if (lines.Count > 0)
        {
            configuration.Add(name + ":");
            configuration.AddRange(lines.Select(line => "  " + line));
        }
    }

    private static void AddColor(List<string> variables, string name, SceneColor? color)
    {
        if (color is { } value)
        {
            variables.Add("\"" + name + "\": \"" + value.ToString() + "\"");
        }
    }
}
