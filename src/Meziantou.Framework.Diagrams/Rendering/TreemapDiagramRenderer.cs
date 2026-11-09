using System.Text;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the treemaps as Mermaid does: the sections as translucent rectangles with their name and total in a header, and
/// the leaves as lighter rectangles with their name and value in the middle, in a font that shrinks to fit them.
/// </summary>
internal static class TreemapDiagramRenderer
{
    private const double SectionLabelFontSize = 12;
    private const double SectionValueFontSize = 10;

    public static SceneBounds Render(TreemapDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var root = TreemapLayout.Compute(diagram);
        var colors = new TreemapColors(context.Theme);
        var formatter = TreemapValueFormatter.Create(diagram.ValueFormat);
        var bounds = new BoundsBuilder();

        // The sections, breadth first, then the leaves, depth first, as Mermaid asks the colors in this order
        var sections = TreemapLayout.BreadthFirst(root).Where(node => node.HasChildren).ToList();
        foreach (var section in sections)
        {
            DrawSection(diagram, context, group, section, colors, formatter, bounds);
        }

        var leaves = TreemapLayout.PreOrder(root).Where(node => !node.HasChildren).ToList();
        var complex = leaves.Count > 20;
        foreach (var leaf in leaves)
        {
            DrawLeaf(diagram, context, group, leaf, colors, formatter, complex, bounds);
        }

        if (bounds.ToBounds() is { Width: 0, Height: 0 })
        {
            bounds.Add(new SceneBounds(0, 0, diagram.NodeWidth * 10, diagram.NodeHeight * 10));
        }

        return bounds.ToBounds();
    }

    private static void DrawSection(TreemapDiagram diagram, DiagramRenderContext context, SceneGroup group, TreemapLayoutNode section, TreemapColors colors, TreemapValueFormatter formatter, BoundsBuilder bounds)
    {
        // The root is not drawn, but takes the first color of the scale
        var fill = colors.GetFill(section.Name);
        var stroke = colors.GetPeer(section.Name);
        if (section.Depth is 0)
            return;

        var style = GetStyle(diagram, section);
        var sectionGroup = group.Add(new SceneGroup { CssClass = "treemapSection" });
        var rectangle = new SceneRectangle(section.X0, section.Y0, Math.Max(0, section.Width), Math.Max(0, section.Height))
        {
            Fill = Translucent(style?.Fill ?? fill, 0.6),
            Stroke = Translucent(style?.Stroke ?? stroke, 0.4),
            StrokeWidth = style?.StrokeWidth ?? 2,
            StrokeDashArray = style?.StrokeDashArray,
            CssClass = "treemapSection",
        };
        if (style?.Opacity is { } opacity)
        {
            rectangle.Opacity = opacity;
        }

        sectionGroup.Add(rectangle);
        bounds.Add(new SceneBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));

        var labelColor = style?.TextColor ?? colors.GetLabel(section.Name);
        var font = new SceneFont(style?.FontSize ?? SectionLabelFontSize, style?.Bold ?? true, style?.Italic ?? false);
        var showValue = diagram.ShowValues && section.Value != 0;

        // The name is cut with an ellipsis to leave room for the value, and clipped to the header
        var available = showValue ? section.Width - 10 - 30 - 10 - 6 : section.Width - 6 - 6;
        var label = Truncate(context.Measurer, CollapseWhiteSpace(section.Name), font, Math.Max(15, available));
        var headerCenter = section.Y0 + (TreemapLayout.SectionHeaderHeight / 2);
        if (label.Length > 0)
        {
            var labelGroup = sectionGroup.Add(new SceneGroup { Clip = new SceneBounds(section.X0, section.Y0, Math.Max(0, section.Width - 12), TreemapLayout.SectionHeaderHeight), CssClass = "treemapSectionLabel" });
            labelGroup.Add(new SceneText(section.X0 + 6, Middle(context.Measurer, font, headerCenter), label, font, labelColor) { MeasuredWidth = context.Measurer.Measure(label, font).Width });
        }

        if (diagram.ShowValues)
        {
            var value = section.Value != 0 ? formatter.Format(section.Value) : "";
            if (value.Length > 0)
            {
                var valueFont = new SceneFont(SectionValueFontSize, style?.Bold ?? false, Italic: true);
                sectionGroup.Add(new SceneText(section.X1 - 10, Middle(context.Measurer, valueFont, headerCenter), value, valueFont, labelColor) { Anchor = SceneTextAnchor.End, MeasuredWidth = context.Measurer.Measure(value, valueFont).Width, CssClass = "treemapSectionValue" });
            }
        }
    }

    private static void DrawLeaf(TreemapDiagram diagram, DiagramRenderContext context, SceneGroup group, TreemapLayoutNode leaf, TreemapColors colors, TreemapValueFormatter formatter, bool complex, BoundsBuilder bounds)
    {
        var style = GetStyle(diagram, leaf);
        var color = colors.GetFill(leaf.Parent?.Name ?? leaf.Name);
        var leafGroup = group.Add(new SceneGroup { CssClass = leaf.Data?.CssClass is { Length: > 0 } cssClass ? "treemapNode treemapLeafGroup " + cssClass : "treemapNode treemapLeafGroup" });
        var rectangle = new SceneRectangle(leaf.X0, leaf.Y0, Math.Max(0, leaf.Width), Math.Max(0, leaf.Height))
        {
            Fill = Translucent(style?.Fill ?? color, 0.3),
            Stroke = style?.Stroke ?? color,
            StrokeWidth = style?.StrokeWidth ?? 3,
            StrokeDashArray = style?.StrokeDashArray,
            CssClass = "treemapLeaf",
        };
        if (style?.Opacity is { } opacity)
        {
            rectangle.Opacity = opacity;
        }

        leafGroup.Add(rectangle);
        bounds.Add(new SceneBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));

        var labelColor = style?.TextColor ?? colors.GetLabel(leaf.Name);
        var name = CollapseWhiteSpace(leaf.Name);
        var bold = style?.Bold ?? false;
        var italic = style?.Italic ?? false;
        var baseLabelFontSize = style?.FontSize is { } fontSize ? Math.Floor(fontSize) : complex ? 16 : 38;
        var baseValueFontSize = complex ? 14 : 28;
        var minLabelFontSize = complex ? 4 : 8;
        var minValueFontSize = complex ? 4 : 6;
        var labelPadding = complex ? 2 : 4;
        var minDisplayThreshold = complex ? 8 : 10;
        var spacing = complex ? 1 : 2;
        const double ValueScaleFactor = 0.6;

        var availableWidth = leaf.Width - (2 * labelPadding);
        var availableHeight = leaf.Height - (2 * labelPadding);
        if (availableWidth < minDisplayThreshold || availableHeight < minDisplayThreshold || name.Length is 0)
            return;

        double TextWidth(string text, double size, bool isItalic) => context.Measurer.Measure(text, new SceneFont(size, bold, isItalic)).Width;
        double ValueFontSize(double labelSize) => Math.Max(minValueFontSize, Math.Min(baseValueFontSize, Math.Round(labelSize * ValueScaleFactor, MidpointRounding.AwayFromZero)));

        // 1. The label shrinks to fit the width, 2. the label and the value shrink to fit the height
        var labelFontSize = baseLabelFontSize;
        while (TextWidth(name, labelFontSize, italic) > availableWidth && labelFontSize > minLabelFontSize)
        {
            labelFontSize--;
        }

        var combinedHeight = labelFontSize + spacing + ValueFontSize(labelFontSize);
        while (combinedHeight > availableHeight && labelFontSize > minLabelFontSize)
        {
            labelFontSize--;
            combinedHeight = labelFontSize + spacing + ValueFontSize(labelFontSize);
        }

        // 3. The label is hidden when it still does not fit
        var hidden = complex
            ? labelFontSize < minLabelFontSize || availableHeight < minLabelFontSize
            : TextWidth(name, labelFontSize, italic) > availableWidth || labelFontSize < minLabelFontSize || availableHeight < labelFontSize;
        if (hidden || labelFontSize <= 0)
            return;

        var clip = new SceneBounds(leaf.X0, leaf.Y0, Math.Max(0, leaf.Width - 4), Math.Max(0, leaf.Height - 4));
        var textGroup = leafGroup.Add(new SceneGroup { Clip = clip });
        var centerX = leaf.X0 + (leaf.Width / 2);
        var centerY = leaf.Y0 + (leaf.Height / 2);
        var labelFont = new SceneFont(labelFontSize, bold, italic);
        textGroup.Add(new SceneText(centerX, Middle(context.Measurer, labelFont, centerY), name, labelFont, labelColor) { Anchor = SceneTextAnchor.Middle, MeasuredWidth = TextWidth(name, labelFontSize, italic), CssClass = "treemapLabel" });

        if (!diagram.ShowValues || leaf.Value == 0)
            return;

        var value = formatter.Format(leaf.Value);
        var valueFontSize = ValueFontSize(labelFontSize);
        var valueTop = centerY + (labelFontSize / 2) + spacing;
        var valueFont = new SceneFont(valueFontSize, bold, italic);
        var valueWidth = context.Measurer.Measure(value, valueFont).Width;
        if (valueWidth > availableWidth || valueTop + valueFontSize > leaf.Y1 - 4 || valueFontSize < minValueFontSize)
            return;

        textGroup.Add(new SceneText(centerX, valueTop + context.Measurer.Measure("x", valueFont).Ascent, value, valueFont, labelColor) { Anchor = SceneTextAnchor.Middle, MeasuredWidth = valueWidth, CssClass = "treemapValue" });
    }

    private static DiagramStyle? GetStyle(TreemapDiagram diagram, TreemapLayoutNode node)
        => node.Data?.CssClass is { } cssClass && diagram.ClassDefinitions.TryGetValue(cssClass, out var style) ? style : null;

    private static SceneColor Translucent(SceneColor color, double opacity) => color.WithOpacity(color.Opacity * opacity);

    /// <summary>Returns the baseline of a line of text centered vertically on a point (SVG <c>dominant-baseline: middle</c>).</summary>
    private static double Middle(ITextMeasurer measurer, SceneFont font, double center)
    {
        var metrics = measurer.Measure("x", font);
        return center + ((metrics.Ascent - metrics.Descent) / 2);
    }

    /// <summary>Cuts a text, with an ellipsis, until it fits a width; the ellipsis alone, or nothing, when even it is too wide.</summary>
    private static string Truncate(ITextMeasurer measurer, string text, SceneFont font, double width)
    {
        if (measurer.Measure(text, font).Width <= width)
            return text;

        // Mermaid removes a character at a time; the width grows with the length, so the longest prefix that fits is found by bisection
        const string Ellipsis = "...";
        var low = 0;
        var high = text.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (measurer.Measure(text[..middle] + Ellipsis, font).Width <= width)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        // The cut does not split a surrogate pair
        if (low > 0 && char.IsHighSurrogate(text[low - 1]))
        {
            low--;
        }

        if (low > 0)
            return text[..low] + Ellipsis;

        return measurer.Measure(Ellipsis, font).Width <= width ? Ellipsis : "";
    }

    /// <summary>Collapses the white space of a text as SVG does: the line feeds and the runs of spaces become a single space.</summary>
    private static string CollapseWhiteSpace(string text)
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
