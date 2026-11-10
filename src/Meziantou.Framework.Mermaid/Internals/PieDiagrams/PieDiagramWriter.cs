using System.Globalization;
using System.Text;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.PieDiagrams;

/// <summary>Writes a <see cref="PieChart"/> as Mermaid: its configuration in the front matter, its colors in a directive, then a line per slice.</summary>
internal static class PieDiagramWriter
{
    public static void Write(StringBuilder builder, PieChart diagram)
    {
        var configuration = new List<string>();

        if (diagram.TextPosition != 0.75)
        {
            configuration.Add("textPosition: " + MermaidStyle.Number(diagram.TextPosition));
        }

        if (diagram.DonutHole != 0)
        {
            configuration.Add("donutHole: " + MermaidStyle.Number(diagram.DonutHole));
        }

        if (diagram.LegendPosition is not PieLegendPosition.Right)
        {
            configuration.Add("legendPosition: " + diagram.LegendPosition switch
            {
                PieLegendPosition.Left => "left",
                PieLegendPosition.Top => "top",
                PieLegendPosition.Bottom => "bottom",
                PieLegendPosition.Center => "center",
                _ => throw new ArgumentException($"The legend position '{diagram.LegendPosition}' is not valid.", nameof(diagram)),
            });
        }

        if (diagram.HighlightedSlice is { Length: > 0 } highlightedSlice)
        {
            if (highlightedSlice is "hover")
                throw new ArgumentException("The slice 'hover' cannot be highlighted in Mermaid: the value highlights the slice under the mouse.", nameof(diagram));

            configuration.Add("highlightSlice: " + MermaidWriterHelpers.YamlDoubleQuoted(highlightedSlice));
        }

        var variables = new List<string>();
        foreach (var (index, color) in diagram.SliceColors.OrderBy(item => item.Key))
        {
            if (index is < 0 or > 11)
                throw new ArgumentException($"The color of the slice {index} cannot be written as Mermaid: the slices have 12 colors.", nameof(diagram));

            AddVariable("pie" + (index + 1).ToString(CultureInfo.InvariantCulture), color.ToString());
        }

        AddVariable("pieStrokeColor", diagram.StrokeColor?.ToString());
        AddVariable("pieStrokeWidth", Size(diagram.StrokeWidth));
        AddVariable("pieOuterStrokeColor", diagram.OuterStrokeColor?.ToString());
        AddVariable("pieOuterStrokeWidth", Size(diagram.OuterStrokeWidth));
        AddVariable("pieOpacity", diagram.Opacity is { } opacity ? MermaidStyle.Number(opacity) : null);
        AddVariable("pieSectionTextColor", diagram.SectionTextColor?.ToString());
        AddVariable("pieSectionTextSize", Size(diagram.SectionTextSize));
        AddVariable("pieLegendTextColor", diagram.LegendTextColor?.ToString());
        AddVariable("pieLegendTextSize", Size(diagram.LegendTextSize));
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "pie", configuration);
        if (variables.Count > 0)
        {
            // The colors and the sizes of the pie are theme variables, which a directive sets
            builder.Append("%%{init: {\"themeVariables\": {").Append(string.Join(", ", variables)).Append("}}}%%\n");
        }

        builder.Append("pie");
        if (diagram.ShowData)
        {
            builder.Append(" showData");
        }

        builder.Append('\n');
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slice in diagram.Slices)
        {
            if (!labels.Add(slice.Label))
                throw new ArgumentException($"The slice \"{slice.Label}\" is defined twice: Mermaid keeps the first one only.", nameof(diagram));

            builder.Append("    ").Append(GitGraphLangium.Quote(slice.Label)).Append(" : ").Append(PieNumber.Format(slice.Value)).Append('\n');
        }

        void AddVariable(string name, string? value)
        {
            if (value is not null)
            {
                variables.Add("\"" + name + "\": \"" + value + "\"");
            }
        }

        static string? Size(double? value) => value is { } number ? MermaidStyle.Number(number) + "px" : null;
    }
}
