using System.Text;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the sankey diagrams as Mermaid does: the nodes as rectangles colored from the Tableau 10 scheme, their labels
/// beside them, and the links as curves as wide as their value, half transparent, over the nodes.
/// </summary>
internal static class SankeyDiagramRenderer
{
    private const double LabelFontSize = 14;
    private const double LabelOffset = 6;

    // d3.schemeTableau10
    private static readonly SceneColor[] Tableau10 =
    [
        SceneColor.Parse("#4e79a7"), SceneColor.Parse("#f28e2c"), SceneColor.Parse("#e15759"), SceneColor.Parse("#76b7b2"), SceneColor.Parse("#59a14f"),
        SceneColor.Parse("#edc949"), SceneColor.Parse("#af7aa1"), SceneColor.Parse("#ff9da7"), SceneColor.Parse("#9c755f"), SceneColor.Parse("#bab0ab"),
    ];

    public static SceneBounds Render(SankeyDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var layout = SankeyLayout.Compute(diagram);
        var bounds = new BoundsBuilder();
        bounds.Add(new SceneBounds(0, 0, diagram.Width, diagram.Height));
        var colors = GetNodeColors(diagram, layout);

        var nodesGroup = group.Add(new SceneGroup { CssClass = "nodes" });
        foreach (var node in layout.Nodes)
        {
            var rectangle = new SceneRectangle(node.X0, node.Y0, Math.Max(0, node.X1 - node.X0), Math.Max(0, node.Y1 - node.Y0)) { Fill = colors[node.Index], CssClass = "node" };
            nodesGroup.Add(rectangle);
            bounds.Add(new SceneBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
        }

        DrawLabels(diagram, context, group, layout, bounds);

        var linksGroup = group.Add(new SceneGroup { CssClass = "links" });
        foreach (var link in layout.Links)
        {
            var x0 = link.Source.X1;
            var x1 = link.Target.X0;
            var middle = (x0 + x1) / 2;
            var path = new PathBuilder().MoveTo(x0, link.Y0).CubicTo(middle, link.Y0, middle, link.Y1, x1, link.Y1).Build();
            var width = Math.Max(1, link.Width);
            ScenePaint stroke = diagram.LinkColor is { } color
                ? color
                : diagram.LinkColoring switch
                {
                    SankeyLinkColoring.Source => colors[link.Source.Index],
                    SankeyLinkColoring.Target => colors[link.Target.Index],
                    _ => new LinearGradientPaint(x0, 0, x1, 0, [new SceneGradientStop(0, colors[link.Source.Index]), new SceneGradientStop(1, colors[link.Target.Index])]),
                };

            linksGroup.Add(new ScenePath(path) { Stroke = stroke, StrokeWidth = width, Opacity = 0.5, CssClass = "link" });
            bounds.Add(new SceneBounds(Math.Min(x0, x1), Math.Min(link.Y0, link.Y1) - (width / 2), Math.Abs(x1 - x0), Math.Abs(link.Y1 - link.Y0) + width));
        }

        return bounds.ToBounds();
    }

    /// <summary>Gives each node its color: the color of <see cref="SankeyDiagram.NodeColors"/>, or the next color of the scheme, as the ordinal scale of Mermaid.</summary>
    private static SceneColor[] GetNodeColors(SankeyDiagram diagram, SankeyLayout layout)
    {
        var colors = new SceneColor[layout.Nodes.Count];
        var next = 0;
        foreach (var node in layout.Nodes)
        {
            colors[node.Index] = diagram.NodeColors.TryGetValue(node.Name, out var color) ? color : Tableau10[next++ % Tableau10.Length];
        }

        return colors;
    }

    private static void DrawLabels(SankeyDiagram diagram, DiagramRenderContext context, SceneGroup group, SankeyLayout layout, BoundsBuilder bounds)
    {
        var labelsGroup = group.Add(new SceneGroup { CssClass = "node-labels" });
        var font = new SceneFont(LabelFontSize);
        var metrics = context.Measurer.Measure("x", font);
        var outlined = diagram.LabelStyle is SankeyLabelStyle.Outlined;

        // The outlined labels are placed relative to the column of the largest node
        var centralLayer = 0;
        var largest = 0.0;
        foreach (var node in layout.Nodes)
        {
            if (node.DisplayValue > largest)
            {
                largest = node.DisplayValue;
                centralLayer = node.Layer;
            }
        }

        foreach (var node in layout.Nodes)
        {
            var text = CollapseWhiteSpace(diagram.ShowValues ? node.Name + "\n" + diagram.ValuePrefix + SankeyJavaScriptNumber.ToString(Math.Round(node.DisplayValue * 100, MidpointRounding.AwayFromZero) / 100) + diagram.ValueSuffix : node.Name);
            if (text.Length is 0)
                continue;

            var before = outlined ? node.Layer < centralLayer : !(node.X0 < diagram.Width / 2);
            var x = before ? node.X0 - LabelOffset : node.X1 + LabelOffset;
            var anchor = before ? SceneTextAnchor.End : SceneTextAnchor.Start;
            var baseline = ((node.Y0 + node.Y1) / 2) + (diagram.ShowValues ? 0 : 0.35 * LabelFontSize);
            var width = context.Measurer.Measure(text, font).Width;
            if (outlined)
            {
                // The outline of Mermaid is a stroke of 4 pixels behind the text: drawn here as copies of the text around it
                var halo = context.Theme.NodeBackground;
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4;
                    labelsGroup.Add(new SceneText(x + (2 * Math.Cos(angle)), baseline + (2 * Math.Sin(angle)), text, font, halo) { Anchor = anchor, MeasuredWidth = width, CssClass = "sankey-label-bg" });
                }
            }

            labelsGroup.Add(new SceneText(x, baseline, text, font, context.Theme.TextColor) { Anchor = anchor, MeasuredWidth = width, CssClass = outlined ? "sankey-label-fg" : null });
            var left = anchor is SceneTextAnchor.End ? x - width : x;
            bounds.Add(new SceneBounds(left - 2, baseline - metrics.Ascent - 2, width + 4, metrics.Height + 4));
        }
    }

    /// <summary>Collapses the white space of a text as SVG does: the line feeds and the runs of spaces become a single space.</summary>
    internal static string CollapseWhiteSpace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r')
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
