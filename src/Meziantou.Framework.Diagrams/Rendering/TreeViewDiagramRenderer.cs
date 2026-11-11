namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the tree views as Mermaid: a row per node, indented by its depth, a horizontal connector to its label, a
/// vertical connector from a directory down to its last child, and the descriptions aligned in a column.
/// </summary>
internal static class TreeViewDiagramRenderer
{
    private const double IconSize = 14;
    private const double IconGap = 4;
    private const double DescriptionGap = 16;
    private const int MaxDepth = 256;

    private static readonly SceneColor DefaultIconColor = (SceneColor)"#546e7a";
    private static readonly SceneColor DefaultDescriptionColor = (SceneColor)"#6a9955";
    private static readonly SceneColor DefaultHighlightBackground = (SceneColor)"rgba(255, 193, 7, 0.15)";
    private static readonly SceneColor DefaultHighlightBorderColor = (SceneColor)"#ffc107";

    public static SceneBounds Render(TreeViewDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var fontSize = diagram.FontSize is > 0 and < 1000 and var size ? size : theme.FontSize;
        var labelColor = diagram.LabelColor ?? theme.TextColor;
        var lineColor = diagram.LineColor ?? theme.LineColor;
        var iconColor = diagram.IconColor ?? DefaultIconColor;
        var thickness = diagram.LineThickness;
        var rows = new List<Row>();
        var lines = group.Add(new SceneGroup { CssClass = "lines" });
        var highlights = group.Add(new SceneGroup { CssClass = "highlights" });
        var labels = group.Add(new SceneGroup { CssClass = "nodes" });
        var top = 0.0;
        var width = 0.0;
        var visited = new HashSet<TreeViewNode>(ReferenceEqualityComparer.Instance);

        // As in Mermaid, the tree starts with a root directory named /
        var root = new TreeViewNode("/") { IsDirectory = true };
        foreach (var node in diagram.Nodes)
        {
            root.Children.Add(node);
        }

        Draw(root, depth: 0, isRoot: true);

        // The descriptions are aligned in a column after the widest label
        var describedRows = rows.Where(row => !string.IsNullOrWhiteSpace(row.Node.Description)).ToList();
        if (describedRows.Count > 0)
        {
            var column = rows.Max(row => row.LabelRight) + DescriptionGap;
            foreach (var row in describedRows)
            {
                var description = context.Text(Display(row.Node.Description!.Trim()), fontSize, italic: true);
                description.Draw(row.Group, column + (description.Width / 2), row.CenterY, diagram.DescriptionColor ?? DefaultDescriptionColor, SceneTextAnchor.Start);
                width = Math.Max(width, column + description.Width + diagram.PaddingX);
            }
        }

        // The highlighted rows span the whole width of the tree
        foreach (var row in rows)
        {
            if (IsHighlighted(row.Node))
            {
                var rectangleWidth = width - row.X + 8;
                highlights.Add(new SceneRectangle(row.X, row.Y + 1, rectangleWidth, Math.Max(0, row.Height - 2))
                {
                    CornerRadius = 3,
                    Fill = diagram.HighlightBackground ?? DefaultHighlightBackground,
                    Stroke = diagram.HighlightBorderColor ?? DefaultHighlightBorderColor,
                    StrokeWidth = 1,
                    CssClass = "highlight",
                });
                width = Math.Max(width, row.X + rectangleWidth + 2);
            }
        }

        return new SceneBounds(-thickness / 2, 0, width, top);

        void Draw(TreeViewNode node, int depth, bool isRoot)
        {
            var x = depth * (diagram.RowIndent + diagram.PaddingX);
            var label = context.Text(Display(node.Name), fontSize, bold: node.IsDirectory);
            var height = label.Height + (2 * diagram.PaddingY);
            var icon = ResolveIcon(diagram, node);
            var labelX = x + diagram.PaddingX + (icon is null ? 0 : IconSize + IconGap);
            var nodeGroup = labels.Add(new SceneGroup { CssClass = ToCssClass(node) });
            if (icon is not null)
            {
                DrawIcon(nodeGroup, icon, x + diagram.PaddingX, top + diagram.PaddingY, iconColor, context);
            }

            var centerY = top + (height / 2);
            label.Draw(nodeGroup, labelX + (label.Width / 2), centerY, labelColor, SceneTextAnchor.Start);
            var row = new Row(node, nodeGroup, x, top, height, labelX + label.Width, centerY);
            rows.Add(row);

            // The root has no parent to link: Mermaid draws its connector out of the view
            if (!isRoot)
            {
                lines.Add(new SceneLine(x - diagram.RowIndent, centerY, x, centerY) { Stroke = lineColor, StrokeWidth = thickness });
            }

            width = Math.Max(width, x + (labelX - x) + label.Width + diagram.PaddingX);
            top += height;
            if (depth >= MaxDepth)
                return;

            Row? last = null;
            foreach (var child in node.Children)
            {
                // A node that appears twice is drawn once, which also stops the cycles
                if (!visited.Add(child))
                    continue;

                var childRow = rows.Count;
                Draw(child, depth + 1, isRoot: false);
                last = rows[childRow];
            }

            if (last is not null)
            {
                var lineX = x + diagram.PaddingX;
                lines.Add(new SceneLine(lineX, row.Y + height, lineX, last.CenterY + (thickness / 2)) { Stroke = lineColor, StrokeWidth = thickness });
            }
        }
    }

    private static bool IsHighlighted(TreeViewNode node) => node.CssClass is { } cssClass && cssClass.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Contains("highlight", StringComparer.Ordinal);

    private static string? ToCssClass(TreeViewNode node)
    {
        var result = node.IsDirectory ? "node directory" : "node";
        return node.CssClass is { Length: > 0 } cssClass && cssClass.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? result + " " + cssClass : result;
    }

    /// <summary>Keeps the spaces of a label as written: SVG collapses consecutive spaces, so they become no-break spaces.</summary>
    private static string Display(string text) => text.Contains("  ", StringComparison.Ordinal) || text.StartsWith(' ', StringComparison.Ordinal) || text.EndsWith(' ', StringComparison.Ordinal) ? text.Replace(' ', '\u00A0') : text;

    /// <summary>
    /// Returns the icon of a node, as Mermaid resolves it: an explicit icon wins (<c>none</c> hides it); otherwise, when
    /// the icons are shown, the icon of the name or of the extension of a file, else the built-in file or folder icon.
    /// </summary>
    internal static string? ResolveIcon(TreeViewDiagram diagram, TreeViewNode node)
    {
        if (node.Icon is { } icon)
            return icon is "" or "none" ? null : Qualify(icon, diagram.DefaultIconPack);

        if (!diagram.ShowIcons)
            return null;

        if (!node.IsDirectory)
        {
            var detected = DetectIcon(diagram, node.Name);
            if (detected is "none")
                return null;

            if (detected is { Length: > 0 })
                return Qualify(detected, diagram.DefaultIconPack);
        }

        return node.IsDirectory ? "folder" : "file";
    }

    private static string? DetectIcon(TreeViewDiagram diagram, string name)
    {
        if (diagram.FileNameIcons.TryGetValue(name, out var icon) && icon.Length > 0)
            return icon;

        var dot = name.LastIndexOf(".", StringComparison.Ordinal);
        if (dot > 0)
        {
            var extension = name[dot..].ToLowerInvariant();
            if (diagram.ExtensionIcons.TryGetValue(extension, out icon) || diagram.ExtensionIcons.TryGetValue(extension[1..], out icon))
                return icon;
        }

        return null;
    }

    /// <summary>Returns <c>file</c> and <c>folder</c> for the built-in icons, else the name of the icon in its pack.</summary>
    private static string Qualify(string icon, string? defaultIconPack)
    {
        if (icon.Contains(':', StringComparison.Ordinal) || icon is "file" or "folder" || string.IsNullOrEmpty(defaultIconPack))
            return icon;

        return defaultIconPack + ":" + icon;
    }

    private static void DrawIcon(SceneGroup target, string icon, double x, double y, SceneColor color, DiagramRenderContext context)
    {
        // The built-in icons of Mermaid are drawn in a square of 24 units
        var scale = IconSize / 24;
        var iconGroup = target.Add(new SceneGroup { Transform = SceneTransform.Scale(scale, scale).Then(SceneTransform.Translate(x, y)), CssClass = "icon" });
        switch (icon)
        {
            case "folder":
                iconGroup.Add(new ScenePath(new PathBuilder()
                    .MoveTo(10.59, 4.59).ArcTo(2, 2, 0, largeArc: false, sweep: false, 9.17, 4).LineTo(4, 4)
                    .ArcTo(2, 2, 0, largeArc: false, sweep: false, 2, 6).LineTo(2, 18)
                    .ArcTo(2, 2, 0, largeArc: false, sweep: false, 4, 20).LineTo(20, 20)
                    .ArcTo(2, 2, 0, largeArc: false, sweep: false, 22, 18).LineTo(22, 8)
                    .ArcTo(2, 2, 0, largeArc: false, sweep: false, 20, 6).LineTo(12.83, 6).Close().Build()) { Fill = color });
                break;
            case "file":
                iconGroup.Add(new ScenePath(new PathBuilder()
                    .MoveTo(6, 2).ArcTo(2, 2, 0, largeArc: false, sweep: false, 4, 4).LineTo(4, 20)
                    .ArcTo(2, 2, 0, largeArc: false, sweep: false, 6, 22).LineTo(18, 22)
                    .ArcTo(2, 2, 0, largeArc: false, sweep: false, 20, 20).LineTo(20, 8.83)
                    .ArcTo(2, 2, 0, largeArc: false, sweep: false, 19.41, 7.41).LineTo(14.59, 2.59)
                    .ArcTo(2, 2, 0, largeArc: false, sweep: false, 13.17, 2).LineTo(6, 2).Close()
                    .MoveTo(13.5, 3.9).LineTo(18.1, 8.5).LineTo(14.5, 8.5)
                    .ArcTo(1, 1, 0, largeArc: false, sweep: true, 13.5, 7.5).LineTo(13.5, 3.9).Close().Build()) { Fill = color, FillRule = SceneFillRule.EvenOdd });
                break;
            default:
                // An icon of an icon pack, which is not available here: a question mark, as Mermaid draws an unknown icon
                var font = new SceneFont(20, Weight: SceneFont.BoldWeight);
                var ascent = context.Measurer.Measure("?", font).Ascent;
                iconGroup.Add(new SceneText(12, 12 + (ascent / 2) - 1, "?", font, color) { Anchor = SceneTextAnchor.Middle });
                break;
        }
    }

    private sealed record Row(TreeViewNode Node, SceneGroup Group, double X, double Y, double Height, double LabelRight, double CenterY);
}
