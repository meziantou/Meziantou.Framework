using System.Globalization;
using System.Text;
using Meziantou.Framework.Diagrams.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.WardleyDiagrams;

/// <summary>
/// Writes a <see cref="WardleyDiagram"/> as Mermaid: the statements grouped by kind, as Mermaid reads them (the anchors,
/// then the components, the pipelines, the links...), the names without quotes when they read back as names.
/// </summary>
internal static class WardleyDiagramWriter
{
    public static void Write(StringBuilder builder, WardleyDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 0)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        // The size statement holds integers; the configuration holds the other sizes
        var sizeStatement = diagram.Width == Math.Floor(diagram.Width) && diagram.Height == Math.Floor(diagram.Height);
        if (!sizeStatement)
        {
            configuration.Add("width: " + MermaidStyle.Number(diagram.Width));
            configuration.Add("height: " + MermaidStyle.Number(diagram.Height));
        }

        AddSetting(configuration, "padding", diagram.MapPadding, 48);
        AddSetting(configuration, "nodeRadius", diagram.NodeRadius, 6);
        AddSetting(configuration, "nodeLabelOffset", diagram.NodeLabelOffset, 8);
        AddSetting(configuration, "axisFontSize", diagram.AxisFontSize, 12);
        AddSetting(configuration, "labelFontSize", diagram.LabelFontSize, 10);
        if (diagram.ShowGrid)
        {
            configuration.Add("showGrid: true");
        }

        var titleStatement = FormatTitle(diagram.Title);
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "wardley-beta", configuration, includeTitle: titleStatement is null);
        builder.Append("wardley-beta\n");
        if (titleStatement is not null)
        {
            builder.Append("title ").Append(titleStatement).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 0);
        if (sizeStatement && (diagram.Width != 900 || diagram.Height != 600))
        {
            builder.Append("size [").Append(FormatInteger(diagram.Width)).Append(", ").Append(FormatInteger(diagram.Height)).Append("]\n");
        }

        WriteStages(builder, diagram);
        foreach (var anchor in diagram.Anchors)
        {
            builder.Append("anchor ").Append(FormatName(anchor.Name)).Append(' ').Append(FormatCoordinates(anchor.Visibility, anchor.Evolution)).Append('\n');
        }

        foreach (var component in diagram.Components)
        {
            builder.Append("component ").Append(FormatName(component.Name)).Append(' ').Append(FormatCoordinates(component.Visibility, component.Evolution));
            AppendLabelOffset(builder, component.LabelOffset);
            if (component.Sourcing is not WardleySourcing.None)
            {
                builder.Append(component.Sourcing switch
                {
                    WardleySourcing.Build => " (build)",
                    WardleySourcing.Buy => " (buy)",
                    WardleySourcing.Outsource => " (outsource)",
                    WardleySourcing.Market => " (market)",
                    _ => throw new ArgumentException($"The sourcing '{component.Sourcing}' is not valid.", nameof(diagram)),
                });
            }

            if (component.Inertia)
            {
                builder.Append(" (inertia)");
            }

            builder.Append('\n');
        }

        foreach (var pipeline in diagram.Pipelines)
        {
            if (pipeline.Components.Count is 0)
                throw new ArgumentException($"The pipeline of '{pipeline.Parent}' has no component: Mermaid needs at least one.", nameof(diagram));

            builder.Append("pipeline ").Append(FormatName(pipeline.Parent)).Append(" {\n");
            foreach (var component in pipeline.Components)
            {
                builder.Append("    component ").Append(FormatName(component.Name)).Append(" [").Append(FormatDecimal(component.Evolution)).Append(']');
                AppendLabelOffset(builder, component.LabelOffset);
                builder.Append('\n');
            }

            builder.Append("}\n");
        }

        foreach (var link in diagram.Links)
        {
            WriteLink(builder, link);
        }

        foreach (var evolve in diagram.Evolves)
        {
            builder.Append("evolve ").Append(FormatName(evolve.Component)).Append(' ').Append(FormatDecimal(evolve.Evolution)).Append('\n');
        }

        foreach (var note in diagram.Notes)
        {
            builder.Append("note ").Append(FormatQuoted(note.Text)).Append(' ').Append(FormatCoordinates(note.Visibility, note.Evolution)).Append('\n');
        }

        if (diagram.AnnotationsPosition is { } position)
        {
            builder.Append("annotations ").Append(FormatCoordinates(position.Visibility, position.Evolution)).Append('\n');
        }

        foreach (var annotation in diagram.Annotations)
        {
            builder.Append("annotation ").Append(annotation.Number.ToString(CultureInfo.InvariantCulture)).Append(',').Append(FormatCoordinates(annotation.Visibility, annotation.Evolution)).Append(' ').Append(FormatQuoted(annotation.Text)).Append('\n');
        }

        foreach (var accelerator in diagram.Accelerators)
        {
            builder.Append("accelerator ").Append(FormatName(accelerator.Name)).Append(' ').Append(FormatCoordinates(accelerator.Visibility, accelerator.Evolution)).Append('\n');
        }

        foreach (var deaccelerator in diagram.Deaccelerators)
        {
            builder.Append("deaccelerator ").Append(FormatName(deaccelerator.Name)).Append(' ').Append(FormatCoordinates(deaccelerator.Visibility, deaccelerator.Evolution)).Append('\n');
        }
    }

    private static void AddSetting(List<string> configuration, string name, double value, double defaultValue)
    {
        if (value != defaultValue)
        {
            configuration.Add(name + ": " + MermaidStyle.Number(value));
        }
    }

    private static void WriteStages(StringBuilder builder, WardleyDiagram diagram)
    {
        if (diagram.Stages.Count is 0)
            return;

        if (diagram.Stages.Count is 1)
            throw new ArgumentException("The evolution axis needs at least two stages in Mermaid.", nameof(diagram));

        builder.Append("evolution ");
        for (var i = 0; i < diagram.Stages.Count; i++)
        {
            var stage = diagram.Stages[i];
            if (i > 0)
            {
                builder.Append(" -> ");
            }

            builder.Append(FormatName(stage.Name));
            if (stage.Boundary is { } boundary)
            {
                builder.Append('@').Append(FormatDecimal(boundary));
            }

            if (stage.SecondName is { } secondName)
            {
                builder.Append(" / ").Append(FormatName(secondName));
            }
        }

        builder.Append('\n');
    }

    private static void WriteLink(StringBuilder builder, WardleyLink link)
    {
        var port = link.Flow switch
        {
            WardleyFlow.None => null,
            WardleyFlow.Forward => "+>",
            WardleyFlow.Backward => "+<",
            WardleyFlow.Bidirectional => "+<>",
            _ => throw new ArgumentException($"The flow '{link.Flow}' is not valid.", nameof(link)),
        };

        builder.Append(FormatName(link.Source));
        if (link.IsDashed)
        {
            // A dashed flow has its port after the target
            builder.Append(" -.-> ").Append(FormatName(link.Target));
            if (port is not null)
            {
                builder.Append(' ').Append(port);
            }
        }
        else
        {
            builder.Append(' ').Append(port ?? "->").Append(' ').Append(FormatName(link.Target));
        }

        if (!string.IsNullOrEmpty(link.Label))
        {
            builder.Append("; ").Append(WardleyEntities.EncodeLabel(link.Label));
        }

        builder.Append('\n');
    }

    private static void AppendLabelOffset(StringBuilder builder, WardleyLabelOffset? offset)
    {
        if (offset is { } value)
        {
            builder.Append(" label [").Append(value.X.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(value.Y.ToString(CultureInfo.InvariantCulture)).Append(']');
        }
    }

    /// <summary>Returns the text of a title statement, which the parser reads back as the title, or <see langword="null"/> to write the title in the front matter.</summary>
    private static string? FormatTitle(string? title)
    {
        if (string.IsNullOrEmpty(title) || !string.Equals(title.Trim(), title, StringComparison.Ordinal) || title.Contains("%%", StringComparison.Ordinal) || title.Contains("  ", StringComparison.Ordinal))
            return null;

        if (title.AsSpan().IndexOfAny("\t\r\n") >= 0 || !string.Equals(GitGraphLangium.ReplaceLoneSurrogates(title), title, StringComparison.Ordinal))
            return null;

        return title;
    }

    /// <summary>Writes a name without quotes when it reads back as a name, quoted otherwise.</summary>
    private static string FormatName(string name) => WardleyLexer.IsBareName(name) ? name : FormatQuoted(name);

    private static string FormatQuoted(string text) => GitGraphLangium.Quote(WardleyEntities.Encode(text));

    private static string FormatCoordinates(double visibility, double evolution) => "[" + FormatDecimal(visibility) + ", " + FormatDecimal(evolution) + "]";

    private static string FormatInteger(double value) => ((long)value).ToString(CultureInfo.InvariantCulture);

    /// <summary>Writes a number with a decimal point and no exponent, as the grammar reads the coordinates: <c>0.5</c>, <c>1.0</c>.</summary>
    private static string FormatDecimal(double value)
    {
        if (value == 0)
            return "0.0";

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex >= 0)
        {
            var exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var mantissa = text[..exponentIndex];
            var point = mantissa.IndexOf('.', StringComparison.Ordinal);
            var digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
            var pointPosition = (point < 0 ? mantissa.Length : point) + exponent;
            if (pointPosition <= 0)
            {
                text = "0." + new string('0', -pointPosition) + digits;
            }
            else if (pointPosition >= digits.Length)
            {
                text = digits + new string('0', pointPosition - digits.Length);
            }
            else
            {
                text = digits[..pointPosition] + "." + digits[pointPosition..];
            }
        }

        return text.Contains('.', StringComparison.Ordinal) ? text : text + ".0";
    }
}
