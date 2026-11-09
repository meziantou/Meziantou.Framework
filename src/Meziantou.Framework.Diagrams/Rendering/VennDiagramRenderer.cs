using System.Text;
using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws a <see cref="VennDiagram"/> as Mermaid does: the circles of the sets with a translucent fill of their color, the
/// labels at the points of the areas farthest from the outlines, and the texts in a grid inside their area. The sizes are
/// those of Mermaid at a width of 1600 pixels, scaled to the width of the diagram.
/// </summary>
internal static class VennDiagramRenderer
{
    private const double ReferenceWidth = 1600;

    /// <summary>The padding of the circles in the drawing, the default of venn.js.</summary>
    private const double CirclePadding = 15;

    public static SceneBounds Render(VennDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var width = diagram.Width;
        var height = diagram.Height;
        var scale = width / ReferenceWidth;
        var colors = new VennColors(diagram, context.Theme);
        var styles = GetStyles(diagram);

        // The overlaps of three sets or more need the overlaps of their pairs to be laid out
        var areas = EnsurePairwiseOverlaps(diagram.Areas);

        // A set of size 0 is removed with its overlaps, as an overlap of a set that does not exist
        var sets = new HashSet<string>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in areas)
        {
            if (area.Sets.Count is 1)
            {
                sets.Add(area.Sets[0]);
                if (area.Size == 0)
                {
                    removed.Add(area.Sets[0]);
                }
            }
        }

        var data = areas.Where(area => area.Sets.All(set => sets.Contains(set) && !removed.Contains(set))).ToList();
        var (circles, textCentres) = VennLayout.Compute([.. data.Select(area => new VennLayoutArea(area.Sets, area.Size))], width, height, CirclePadding);

        // The label of an area is the last non-empty label of the areas with the same sets, as venn.js indexes them
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var area in data)
        {
            if (!string.IsNullOrEmpty(area.Label))
            {
                labels[VennLayout.GetAreaKey(area.Sets)] = area.Label;
            }
        }

        var fontSize = 48 * scale;
        var setIndex = 0;
        foreach (var area in data)
        {
            var isSet = area.Sets.Count is 1;
            var areaGroup = group.Add(new SceneGroup { CssClass = isSet ? "venn-area venn-circle" : "venn-area venn-intersection" });
            areaGroup.SetDataAttribute("venn-sets", string.Join('_', area.Sets));
            var style = styles.GetValueOrDefault(VennLayout.GetAreaKey(area.Sets));
            var areaCircles = area.Sets.Select(set => circles[set]).ToArray();

            SceneColor textColor;
            if (isSet)
            {
                var circle = areaCircles[0];
                var baseColor = style?.Fill ?? colors.GetSetColor(setIndex);
                setIndex++;
                if (circle.Radius > 0 && double.IsFinite(circle.Radius))
                {
                    var fillOpacity = Math.Clamp(style?.FillOpacity ?? 0.1, 0, 1);
                    var strokeWidth = style?.StrokeWidth ?? (5 * scale);
                    var stroke = style?.Stroke ?? baseColor;
                    areaGroup.Add(new SceneCircle(circle.X, circle.Y, circle.Radius)
                    {
                        Fill = baseColor.WithOpacity(baseColor.Opacity * fillOpacity),
                        Stroke = strokeWidth > 0 ? new SolidPaint(stroke.WithOpacity(stroke.Opacity * 0.95)) : null,
                        StrokeWidth = Math.Max(0, strokeWidth),
                    });
                }

                textColor = style?.TextColor ?? colors.GetLabelColor(baseColor);
            }
            else
            {
                // An overlap is filled only by its style
                if (style?.Fill is { } fill && CreateOutline(areaCircles) is { } outline)
                {
                    areaGroup.Add(outline).Fill = fill;
                }

                textColor = style?.TextColor ?? colors.TextColor;
            }

            var key = VennLayout.GetAreaKey(area.Sets);
            var label = labels.TryGetValue(key, out var value) ? value : isSet ? area.Sets[0] : null;
            label = label is null ? null : CollapseWhiteSpace(label);
            if (!string.IsNullOrEmpty(label) && textCentres.TryGetValue(key, out var centre) && !centre.Disjoint && double.IsFinite(centre.X) && double.IsFinite(centre.Y))
            {
                // venn.js rounds the center down, and moves the text down by 0.35em to center it vertically
                var font = new SceneFont(fontSize);
                var x = Math.Floor(centre.X);
                var y = Math.Floor(centre.Y) + (0.35 * fontSize);
                areaGroup.Add(new SceneText(x, y, label, font, textColor) { Anchor = SceneTextAnchor.Middle, MeasuredWidth = context.Measurer.Measure(label, font).Width });
            }
        }

        DrawTextNodes(diagram, context, group, data, circles, textCentres, styles, colors, scale);
        return new SceneBounds(0, 0, width, height);
    }

    /// <summary>Draws the texts of each area in a grid, inside the largest circle that fits in the area around its label.</summary>
    private static void DrawTextNodes(VennDiagram diagram, DiagramRenderContext context, SceneGroup group, List<VennRenderArea> data, VennKeyedMap<VennCircle> circles, Dictionary<string, VennTextCentre> textCentres, Dictionary<string, VennStyle> styles, VennColors colors, double scale)
    {
        if (diagram.TextNodes.Count is 0)
            return;

        // The last area of each key, as Mermaid indexes them
        var areasByKey = new Dictionary<string, VennRenderArea>(StringComparer.Ordinal);
        foreach (var area in data)
        {
            areasByKey[VennLayout.GetAreaKey(area.Sets)] = area;
        }

        var nodesByKey = new List<(string Key, List<VennTextNode> Nodes)>();
        var nodeIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in diagram.TextNodes)
        {
            var key = VennLayout.GetAreaKey(node.Sets);
            if (!nodeIndices.TryGetValue(key, out var index))
            {
                index = nodesByKey.Count;
                nodeIndices[key] = index;
                nodesByKey.Add((key, []));
            }

            nodesByKey[index].Nodes.Add(node);
        }

        var textGroup = group.Add(new SceneGroup { CssClass = "venn-text-nodes" });
        var fontSize = 40 * scale;
        foreach (var (key, nodes) in nodesByKey)
        {
            if (!areasByKey.TryGetValue(key, out var area) || !textCentres.TryGetValue(VennLayout.GetAreaKey(area.Sets), out var centre) || centre.Disjoint)
                continue;

            // The radius of the largest circle around the center that stays inside the circles of the area
            var centerX = centre.X;
            var centerY = centre.Y;
            var areaCircles = area.Sets.Select(set => circles[set]).ToArray();
            var minCircleRadius = areaCircles.Min(circle => circle.Radius);
            var innerRadiusRaw = areaCircles.Min(circle => circle.Radius - VennGeometry.Distance(centerX, centerY, circle.X, circle.Y));
            var innerRadius = double.IsFinite(innerRadiusRaw) ? Math.Max(0, innerRadiusRaw) : 0;
            if (innerRadius == 0 && double.IsFinite(minCircleRadius))
            {
                innerRadius = minCircleRadius * 0.6;
            }

            var innerWidth = Math.Max(80 * scale, innerRadius * 2 * 0.95);
            var innerHeight = Math.Max(60 * scale, innerRadius * 2 * 0.95);
            var labelOffsetBase = !string.IsNullOrEmpty(area.Label) ? Math.Min(32 * scale, innerRadius * 0.25) : 0;
            var labelOffset = labelOffsetBase + (nodes.Count <= 2 ? 30 * scale : 0);
            var startX = centerX - (innerWidth / 2);
            var startY = centerY - (innerHeight / 2) + labelOffset;
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(nodes.Count)));
            var rows = Math.Max(1, (int)Math.Ceiling(nodes.Count / (double)columns));
            var cellWidth = innerWidth / columns;
            var cellHeight = innerHeight / rows;
            if (!double.IsFinite(startX) || !double.IsFinite(startY) || !double.IsFinite(cellWidth) || !double.IsFinite(cellHeight))
                continue;

            var areaGroup = textGroup.Add(new SceneGroup { CssClass = "venn-text-area" });
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var x = startX + (cellWidth * ((i % columns) + 0.5));
                var y = startY + (cellHeight * ((i / columns) + 0.5));
                var text = CollapseWhiteSpace(node.Label ?? node.Id);
                if (text.Length is 0)
                    continue;

                var color = styles.TryGetValue(VennLayout.GetAreaKey([node.Id]), out var style) && style.TextColor is { } textColor ? textColor : colors.TextColor;
                var block = TextBlock.Create(new DiagramText(text), context.Measurer, fontSize, maxWidth: cellWidth * 0.9);
                var nodeGroup = areaGroup.Add(new SceneGroup { CssClass = "venn-text-node" });
                nodeGroup.SetDataAttribute("id", node.Id);
                block.Draw(nodeGroup, x, y, color);
            }
        }
    }

    /// <summary>Returns the outline of the overlap of circles, as the arcs of venn.js.</summary>
    private static SceneShape? CreateOutline(VennCircle[] circles)
    {
        VennGeometry.IntersectionArea(circles, out var arcs);
        if (arcs.Count is 0)
            return null;

        if (arcs.Count is 1)
        {
            var circle = arcs[0].Circle;
            return circle.Radius > 0 ? new SceneCircle(circle.X, circle.Y, circle.Radius) : null;
        }

        var builder = new PathBuilder().MoveTo(arcs[0].X2, arcs[0].Y2);
        foreach (var arc in arcs)
        {
            builder.ArcTo(arc.Circle.Radius, arc.Circle.Radius, 0, arc.Large, sweep: true, arc.X1, arc.Y1);
        }

        return new ScenePath(builder.Close().Build());
    }

    /// <summary>Merges the styles of the same targets, in order, by the key Mermaid gives them: the targets joined by <c>|</c>.</summary>
    private static Dictionary<string, VennStyle> GetStyles(VennDiagram diagram)
    {
        var styles = new Dictionary<string, VennStyle>(StringComparer.Ordinal);
        foreach (var rule in diagram.Styles)
        {
            var key = VennLayout.GetAreaKey(rule.Targets);
            styles[key] = styles.TryGetValue(key, out var existing) ? existing.Merge(rule.Style) : rule.Style;
        }

        return styles;
    }

    /// <summary>
    /// Adds the overlaps of the pairs of sets of the overlaps of three sets or more, which Mermaid adds to lay them out,
    /// with a quarter of the size of the smaller set.
    /// </summary>
    private static List<VennRenderArea> EnsurePairwiseOverlaps(IList<VennArea> areas)
    {
        var result = areas.Select(area => new VennRenderArea(area.Sets, area.EffectiveSize, area.Label)).ToList();
        var existingKeys = new HashSet<string>(result.Select(area => VennLayout.GetAreaKey(area.Sets)), StringComparer.Ordinal);
        var setSizes = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var area in result)
        {
            if (area.Sets.Count is 1)
            {
                setSizes[area.Sets[0]] = area.Size;
            }
        }

        var synthetic = new List<VennRenderArea>();
        foreach (var area in result)
        {
            if (area.Sets.Count < 3)
                continue;

            for (var i = 0; i < area.Sets.Count - 1; i++)
            {
                for (var j = i + 1; j < area.Sets.Count; j++)
                {
                    string[] pair = [area.Sets[i], area.Sets[j]];
                    if (existingKeys.Add(VennLayout.GetAreaKey(pair)))
                    {
                        var size = setSizes.TryGetValue(pair[0], out var sizeA) && setSizes.TryGetValue(pair[1], out var sizeB) ? Math.Min(sizeA, sizeB) / 4 : 2.5;
                        synthetic.Add(new VennRenderArea(pair, size, Label: ""));
                    }
                }
            }
        }

        result.AddRange(synthetic);
        return result;
    }

    /// <summary>Collapses the white space as SVG and HTML show it: runs of white space are one space, and the ends are trimmed.</summary>
    private static string CollapseWhiteSpace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
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

    private sealed record VennRenderArea(IReadOnlyList<string> Sets, double Size, string? Label);
}
