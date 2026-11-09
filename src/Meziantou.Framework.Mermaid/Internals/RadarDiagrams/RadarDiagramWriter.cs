using System.Globalization;
using System.Text;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;
using Meziantou.Framework.Mermaid.Internals.PieDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.RadarDiagrams;

/// <summary>Writes a <see cref="RadarChart"/> as Mermaid: its configuration in the front matter, its style in a directive, the axes, the curves and the options.</summary>
internal static class RadarDiagramWriter
{
    public static void Write(StringBuilder builder, RadarChart diagram)
    {
        var configuration = new List<string>();

        Add("width", diagram.Width, 600);
        Add("height", diagram.Height, 600);
        Add("marginTop", diagram.MarginTop, 50);
        Add("marginRight", diagram.MarginRight, 50);
        Add("marginBottom", diagram.MarginBottom, 50);
        Add("marginLeft", diagram.MarginLeft, 50);
        Add("axisScaleFactor", diagram.AxisScaleFactor, 1);
        Add("axisLabelFactor", diagram.AxisLabelFactor, 1.05);
        Add("curveTension", diagram.CurveTension, 0.17);

        var variables = new List<string>();
        AddVariable("axisColor", diagram.AxisColor?.ToString());
        AddVariable("axisStrokeWidth", Number(diagram.AxisStrokeWidth));
        AddVariable("axisLabelFontSize", Number(diagram.AxisLabelFontSize));
        AddVariable("curveOpacity", Number(diagram.CurveOpacity));
        AddVariable("curveStrokeWidth", Number(diagram.CurveStrokeWidth));
        AddVariable("graticuleColor", diagram.GraticuleColor?.ToString());
        AddVariable("graticuleStrokeWidth", Number(diagram.GraticuleStrokeWidth));
        AddVariable("graticuleOpacity", Number(diagram.GraticuleOpacity));
        AddVariable("legendFontSize", Number(diagram.LegendFontSize));
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "radar", configuration);
        if (variables.Count > 0)
        {
            // The style of the radar charts is in the theme variables, which a directive sets
            builder.Append("%%{init: {\"themeVariables\": {\"radar\": {").Append(string.Join(", ", variables)).Append("}}}}%%\n");
        }

        builder.Append("radar-beta\n");
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        if (diagram.Axes.Count > 0)
        {
            builder.Append("    axis ");
            for (var i = 0; i < diagram.Axes.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                var axis = diagram.Axes[i];
                AppendName(axis.Name, axis.Label);
            }

            builder.Append('\n');
        }

        foreach (var curve in diagram.Curves)
        {
            if (curve.Values.Count is 0)
                throw new ArgumentException($"The curve '{curve.Name}' cannot be written as Mermaid: a curve needs a value.", nameof(diagram));

            builder.Append("    curve ");
            AppendName(curve.Name, curve.Label);
            builder.Append('{');
            for (var i = 0; i < curve.Values.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(FormatValue(curve.Values[i]));
            }

            builder.Append("}\n");
        }

        if (!diagram.ShowLegend)
        {
            builder.Append("    showLegend false\n");
        }

        if (diagram.Ticks != 5)
        {
            builder.Append("    ticks ").Append(diagram.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        if (diagram.Maximum is { } maximum)
        {
            builder.Append("    max ").Append(FormatValue(maximum)).Append('\n');
        }

        if (diagram.Minimum != 0)
        {
            builder.Append("    min ").Append(FormatValue(diagram.Minimum)).Append('\n');
        }

        if (diagram.Graticule is RadarGraticule.Polygon)
        {
            builder.Append("    graticule polygon\n");
        }

        void Add(string name, double value, double defaultValue)
        {
            if (value != defaultValue)
            {
                configuration.Add(name + ": " + MermaidStyle.Number(value));
            }
        }

        void AddVariable(string name, string? value)
        {
            if (value is not null)
            {
                variables.Add("\"" + name + "\": \"" + value + "\"");
            }
        }

        void AppendName(string name, string? label)
        {
            if (!RadarIdentifier.IsValid(name))
                throw new ArgumentException($"The name '{name}' cannot be written as Mermaid: it must start with an ASCII letter or an underscore, and contain only ASCII letters, digits, underscores and dashes.", nameof(diagram));

            builder.Append(name);
            if (label is not null)
            {
                builder.Append('[').Append(GitGraphLangium.Quote(label)).Append(']');
            }
        }

        static string? Number(double? value) => value is { } number ? MermaidStyle.Number(number) : null;

        string FormatValue(double value)
        {
            // The grammar of Mermaid has no sign: the numbers are positive
            if (!(value >= 0) || !double.IsFinite(value))
                throw new ArgumentException($"The value {value.ToString(CultureInfo.InvariantCulture)} cannot be written as Mermaid: the values of a radar chart are positive or zero.", nameof(diagram));

            return PieNumber.Format(value);
        }
    }
}
