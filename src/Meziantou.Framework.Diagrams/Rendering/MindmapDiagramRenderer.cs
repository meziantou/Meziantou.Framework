using System.Globalization;
using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the mindmaps as a tidy tree on both sides of the root: the first half of the children of the root on its right,
/// the others on its left, each subtree stacked vertically and its parent centered on it. The shapes, the colors of the
/// sections and the widths of the edges follow Mermaid.
/// </summary>
internal static class MindmapDiagramRenderer
{
    private const double HorizontalGap = 50;
    private const double VerticalGap = 15;

    public static SceneBounds Render(MindmapDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var bounds = new BoundsBuilder();
        if (diagram.Root is null)
            return bounds.ToBounds();

        var state = new MindmapLayoutState(diagram, context);
        var root = state.Build(diagram.Root, depth: 0, section: -1);
        root.X = 0;
        root.Y = 0;

        // The first half of the children go on the right, the others on the left
        var rightCount = (root.Children.Count + 1) / 2;
        PlaceSide(root, root.Children.Take(rightCount).ToList(), direction: 1);
        PlaceSide(root, root.Children.Skip(rightCount).ToList(), direction: -1);

        var edges = group.Add(new SceneGroup { CssClass = "edges" });
        var nodes = group.Add(new SceneGroup { CssClass = "nodes" });
        DrawEdges(state, root, edges, bounds);
        DrawNodes(state, root, nodes, bounds);
        return bounds.ToBounds();
    }

    /// <summary>Places subtrees on one side of their parent: stacked vertically, centered on the parent.</summary>
    private static void PlaceSide(MindmapLayoutNode parent, List<MindmapLayoutNode> children, int direction)
    {
        if (children.Count is 0)
            return;

        var total = children.Sum(child => child.SubtreeHeight) + (VerticalGap * (children.Count - 1));
        var top = parent.Y - (total / 2);
        foreach (var child in children)
        {
            child.X = parent.X + (direction * ((parent.Width / 2) + HorizontalGap + (child.Width / 2)));
            child.Y = top + (child.SubtreeHeight / 2);
            PlaceSide(child, child.Children, direction);
            top += child.SubtreeHeight + VerticalGap;
        }
    }

    private static void DrawEdges(MindmapLayoutState state, MindmapLayoutNode node, SceneGroup target, BoundsBuilder bounds)
    {
        foreach (var child in node.Children)
        {
            // Mermaid makes the edges thinner with the depth: 11, 8, 5, 2, then 3
            var depth = node.Depth + 1;
            var width = 14 - (3 * depth) is > 0 and var value ? value : 3;
            var middle = (node.X + child.X) / 2;
            var path = new PathBuilder().MoveTo(node.X, node.Y).CubicTo(middle, node.Y, middle, child.Y, child.X, child.Y).Build();
            var color = state.GetColor(child.Section);
            target.Add(new ScenePath(path)
            {
                Stroke = color,
                StrokeWidth = width,
                CssClass = "edge edge-depth-" + depth.ToString(CultureInfo.InvariantCulture) + " section-edge-" + child.Section.ToString(CultureInfo.InvariantCulture),
            });
            bounds.Add(new LayoutPoint(node.X, node.Y));
            bounds.Add(new LayoutPoint(child.X, child.Y));
            DrawEdges(state, child, target, bounds);
        }
    }

    private static void DrawNodes(MindmapLayoutState state, MindmapLayoutNode node, SceneGroup target, BoundsBuilder bounds)
    {
        var theme = state.Context.Theme;
        var fill = state.GetColor(node.Section);
        var textColor = MindmapColors.Contrast(fill);
        var sectionClass = node.Section < 0 ? "section-root section--1" : "section-" + node.Section.ToString(CultureInfo.InvariantCulture);
        var nodeGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(node.Source.CssClasses, "mindmap-node " + sectionClass) });
        if (node.Source.Id is { } id)
        {
            nodeGroup.SetDataAttribute("id", id);
        }

        var left = node.X - (node.Width / 2);
        var top = node.Y - (node.Height / 2);
        var width = node.Width;
        var height = node.Height;
        var stroke = theme.IsDark ? MindmapColors.Lighten(fill) : MindmapColors.Darken(fill);
        switch (node.Source.Shape)
        {
            case MindmapNodeShape.Square:
                nodeGroup.Add(new SceneRectangle(left, top, width, height) { Fill = fill });
                break;
            case MindmapNodeShape.RoundedSquare:
                nodeGroup.Add(new SceneRectangle(left, top, width, height) { Fill = fill, CornerRadius = node.Padding });
                break;
            case MindmapNodeShape.Circle:
                nodeGroup.Add(new SceneCircle(node.X, node.Y, width / 2) { Fill = fill });
                break;
            case MindmapNodeShape.Hexagon:
                {
                    var m = height / 4;
                    nodeGroup.Add(new ScenePath(Geometry.Polygon((left + m, top), (left + width - m, top), (left + width, node.Y), (left + width - m, top + height), (left + m, top + height), (left, node.Y))) { Fill = fill });
                    break;
                }

            case MindmapNodeShape.Cloud:
                nodeGroup.Add(new ScenePath(Cloud(left, top, width, height)) { Fill = fill });
                break;
            case MindmapNodeShape.Bang:
                nodeGroup.Add(new ScenePath(Bang(left, top, width, height)) { Fill = fill });
                break;
            default:
                {
                    // A box with rounded top corners, and a line below it
                    const double Radius = 5;
                    var path = new PathBuilder()
                        .MoveTo(left, top + height - Radius)
                        .LineTo(left, top + Radius)
                        .QuadraticTo(left, top, left + Radius, top)
                        .LineTo(left + width - Radius, top)
                        .QuadraticTo(left + width, top, left + width, top + Radius)
                        .LineTo(left + width, top + height)
                        .LineTo(left, top + height)
                        .Close()
                        .Build();
                    nodeGroup.Add(new ScenePath(path) { Fill = fill });
                    nodeGroup.Add(new SceneLine(left, top + height, left + width, top + height) { Stroke = stroke, StrokeWidth = 3 });
                    break;
                }
        }

        node.Text.Draw(nodeGroup, node.X, node.Y, textColor);

        // The cloud and the bang go a little beyond their box
        var overflow = node.Source.Shape is MindmapNodeShape.Cloud or MindmapNodeShape.Bang ? width * 0.15 : 0;
        bounds.Add(new SceneBounds(left - overflow, top - overflow, width + (2 * overflow), height + (2 * overflow) + 1.5));
        foreach (var child in node.Children)
        {
            DrawNodes(state, child, target, bounds);
        }
    }

    /// <summary>Returns the cloud of Mermaid in a box: arcs around its border.</summary>
    private static PathData Cloud(double x, double y, double w, double h)
    {
        var r1 = 0.15 * w;
        var r2 = 0.25 * w;
        var r3 = 0.35 * w;
        var r4 = 0.2 * w;
        var builder = new PathBuilder().MoveTo(x, y);
        var cx = x;
        var cy = y;
        Arc(r1, r1, w * 0.25, -w * 0.1);
        Arc(r3, r3, w * 0.4, -w * 0.1);
        Arc(r2, r2, w * 0.35, w * 0.2);
        Arc(r1, r1, w * 0.15, h * 0.35);
        Arc(r4, r4, -w * 0.15, h * 0.65);
        Arc(r2, r1, -w * 0.25, w * 0.15);
        Arc(r3, r3, -w * 0.5, 0);
        Arc(r1, r1, -w * 0.25, -w * 0.15);
        Arc(r1, r1, -w * 0.1, -h * 0.35);
        Arc(r4, r4, w * 0.1, -h * 0.65);
        return builder.LineTo(x, cy).LineTo(x, y).Close().Build();

        void Arc(double rx, double ry, double dx, double dy)
        {
            cx += dx;
            cy += dy;
            builder.ArcTo(rx, ry, 1, largeArc: false, sweep: true, cx, cy);
        }
    }

    /// <summary>Returns the bang of Mermaid in a box: small arcs inward around its border.</summary>
    private static PathData Bang(double x, double y, double w, double h)
    {
        var r = 0.15 * w;
        var builder = new PathBuilder().MoveTo(x, y);
        var cx = x;
        var cy = y;
        Arc(r, w * 0.25, -h * 0.1);
        Arc(r, w * 0.25, 0);
        Arc(r, w * 0.25, 0);
        Arc(r, w * 0.25, h * 0.1);
        Arc(r, w * 0.15, h * 0.33);
        Arc(r * 0.8, 0, h * 0.34);
        Arc(r, -w * 0.15, h * 0.33);
        Arc(r, -w * 0.25, h * 0.15);
        Arc(r, -w * 0.25, 0);
        Arc(r, -w * 0.25, 0);
        Arc(r, -w * 0.25, -h * 0.15);
        Arc(r, -w * 0.1, -h * 0.33);
        Arc(r * 0.8, 0, -h * 0.34);
        Arc(r, w * 0.1, -h * 0.33);
        return builder.LineTo(x, cy).LineTo(x, y).Close().Build();

        void Arc(double radius, double dx, double dy)
        {
            cx += dx;
            cy += dy;
            builder.ArcTo(radius, radius, 1, largeArc: false, sweep: false, cx, cy);
        }
    }
}
