namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the timelines as Mermaid: boxes with rounded top corners, in the colors of the palette of the theme, one color
/// per section (or per period without sections), and the time axis between the periods and their events.
/// </summary>
internal static class TimelineDiagramRenderer
{
    private const double Margin = 50;

    public static SceneBounds Render(TimelineDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        // The periods in no section come first, as a section without title
        var blocks = new List<(DiagramText? Title, IList<TimelinePeriod> Periods)>();
        if (diagram.Periods.Count > 0 || diagram.Sections.Count is 0)
        {
            blocks.Add((null, diagram.Periods));
        }

        foreach (var section in diagram.Sections)
        {
            blocks.Add((section.Title, section.Periods));
        }

        return diagram.Direction is TimelineDirection.TopToBottom
            ? RenderVertical(diagram, context, group, blocks)
            : RenderHorizontal(diagram, context, group, blocks);
    }

    private static SceneBounds RenderHorizontal(TimelineDiagram diagram, DiagramRenderContext context, SceneGroup group, List<(DiagramText? Title, IList<TimelinePeriod> Periods)> blocks)
    {
        const double TextWidth = 150;
        const double Padding = 20;
        const double NodeWidth = TextWidth + (2 * Padding);
        const double Step = 200;
        var theme = context.Theme;
        var hasSections = blocks.Exists(block => block.Title is not null);
        var bounds = new BoundsBuilder();

        var maxSectionHeight = 0.0;
        foreach (var (title, _) in blocks)
        {
            if (title is not null)
            {
                maxSectionHeight = Math.Max(maxSectionHeight, NodeHeight(context, title, TextWidth, Padding, 0) + 20);
            }
        }

        var maxPeriodHeight = 0.0;
        var maxEventsHeight = 0.0;
        foreach (var period in blocks.SelectMany(block => block.Periods))
        {
            maxPeriodHeight = Math.Max(maxPeriodHeight, NodeHeight(context, period.Label, TextWidth, Padding, 0) + 20);
            var eventsHeight = period.Events.Sum(item => NodeHeight(context, item, TextWidth, Padding, 50)) + (Math.Max(0, period.Events.Count - 1) * 10);
            maxEventsHeight = Math.Max(maxEventsHeight, eventsHeight);
        }

        var x = Margin + Margin;
        var periodY = hasSections ? Margin + maxSectionHeight + Margin : Margin;
        var colorIndex = 0;
        var lines = group.Add(new SceneGroup { CssClass = "lines" });
        var nodes = group.Add(new SceneGroup { CssClass = "nodes" });
        foreach (var (title, periods) in blocks)
        {
            if (title is not null)
            {
                var width = (Step * Math.Max(periods.Count, 1)) - Margin + (2 * Padding);
                DrawNode(nodes, context, title, x, Margin, width, maxSectionHeight, TextWidth + (Step * (Math.Max(periods.Count, 1) - 1)), Padding, colorIndex, "section", bounds);
            }

            var periodColor = colorIndex;
            foreach (var period in periods)
            {
                DrawNode(nodes, context, period.Label, x, periodY, NodeWidth, maxPeriodHeight, TextWidth, Padding, periodColor, "period", bounds);
                var eventY = periodY + 200;
                foreach (var item in period.Events)
                {
                    var height = NodeHeight(context, item, TextWidth, Padding, 50);
                    DrawNode(nodes, context, item, x, eventY, NodeWidth, height, TextWidth, Padding, periodColor, "event", bounds);
                    eventY += height + 10;
                }

                if (period.Events.Count > 0)
                {
                    var lineX = x + (NodeWidth / 2);
                    var lineEnd = periodY + maxPeriodHeight + 200 + maxEventsHeight;
                    lines.Add(new SceneLine(lineX, periodY + maxPeriodHeight, lineX, lineEnd - 8) { Stroke = theme.LineColor, StrokeWidth = 2, StrokeDashArray = [5, 5] });
                    EdgeMarkers.Draw(lines, EdgeMarker.Arrow, new Layout.LayoutPoint(lineX, lineEnd), new Layout.LayoutPoint(lineX, lineEnd - 10), theme.LineColor, context.Background, 2);
                    bounds.Add(new Layout.LayoutPoint(lineX, lineEnd));
                }

                x += Step;
                if (title is null && !diagram.DisableMulticolor)
                {
                    periodColor++;
                }
            }

            if (periods.Count is 0)
            {
                x += Step;
            }

            if (title is not null || !diagram.DisableMulticolor)
            {
                colorIndex = title is null ? periodColor : colorIndex + 1;
            }
        }

        // The time axis, between the periods and their events
        var contentBounds = bounds.ToBounds();
        var axisY = periodY + maxPeriodHeight + Margin;
        var axisEnd = contentBounds.Right + Margin;
        lines.Add(new SceneLine(Margin, axisY, axisEnd - 10, axisY) { Stroke = theme.LineColor, StrokeWidth = 4 });
        EdgeMarkers.Draw(lines, EdgeMarker.Arrow, new Layout.LayoutPoint(axisEnd, axisY), new Layout.LayoutPoint(axisEnd - 10, axisY), theme.LineColor, context.Background, 4);
        bounds.Add(new Layout.LayoutPoint(Margin, axisY));
        bounds.Add(new Layout.LayoutPoint(axisEnd, axisY));
        return bounds.ToBounds();
    }

    private static SceneBounds RenderVertical(TimelineDiagram diagram, DiagramRenderContext context, SceneGroup group, List<(DiagramText? Title, IList<TimelinePeriod> Periods)> blocks)
    {
        const double NodeTextWidth = 200;
        const double Padding = 5;
        const double NodeWidth = NodeTextWidth + (2 * Padding);
        const double EventTextWidth = NodeTextWidth + 100;
        const double EventWidth = EventTextWidth + (2 * Padding);
        const double EventSpacing = 10;
        const double SectionTaskGap = 20;
        const double TaskAxisGap = 20;
        const double TaskVerticalGap = 30;
        const double EventAxisGap = 50;
        var theme = context.Theme;
        var bounds = new BoundsBuilder();
        var leftWidth = NodeWidth + TaskAxisGap;
        var rightWidth = EventWidth + EventAxisGap;
        var axisX = Margin + Margin + leftWidth;
        var sectionTextWidth = Math.Max(50, leftWidth + rightWidth - (Padding * 2));

        var maxPeriodHeight = 0.0;
        var maxEventsHeight = 0.0;
        foreach (var period in blocks.SelectMany(block => block.Periods))
        {
            maxPeriodHeight = Math.Max(maxPeriodHeight, NodeHeight(context, period.Label, NodeTextWidth, Padding, 0));
            var eventsHeight = period.Events.Sum(item => NodeHeight(context, item, EventTextWidth, Padding, 0)) + (Math.Max(0, period.Events.Count - 1) * EventSpacing);
            maxEventsHeight = Math.Max(maxEventsHeight, eventsHeight);
        }

        var maxSectionHeight = 0.0;
        foreach (var (title, _) in blocks)
        {
            if (title is not null)
            {
                maxSectionHeight = Math.Max(maxSectionHeight, NodeHeight(context, title, sectionTextWidth, Padding, 0));
            }
        }

        var spacing = Math.Max(maxPeriodHeight, maxEventsHeight) + TaskVerticalGap;
        var y = Margin;
        var colorIndex = 0;
        var lines = group.Add(new SceneGroup { CssClass = "lines" });
        var nodes = group.Add(new SceneGroup { CssClass = "nodes" });
        foreach (var (title, periods) in blocks)
        {
            var periodY = y;
            if (title is not null)
            {
                var height = Math.Max(NodeHeight(context, title, sectionTextWidth, Padding, 0), maxSectionHeight);
                DrawNode(nodes, context, title, axisX - leftWidth, y, sectionTextWidth + (2 * Padding), height, sectionTextWidth, Padding, colorIndex, "section", bounds);
                periodY = y + height + SectionTaskGap;
            }

            var periodColor = colorIndex;
            foreach (var period in periods)
            {
                var periodHeight = Math.Max(NodeHeight(context, period.Label, NodeTextWidth, Padding, 0), maxPeriodHeight);
                DrawNode(nodes, context, period.Label, axisX - TaskAxisGap - NodeWidth, periodY, NodeWidth, periodHeight, NodeTextWidth, Padding, periodColor, "period", bounds);
                var eventY = periodY;
                foreach (var item in period.Events)
                {
                    var height = NodeHeight(context, item, EventTextWidth, Padding, 0);
                    DrawNode(nodes, context, item, axisX + EventAxisGap, eventY, EventWidth, height, EventTextWidth, Padding, periodColor, "event", bounds);
                    lines.Add(new SceneLine(axisX, eventY + (height / 2), axisX + EventAxisGap, eventY + (height / 2)) { Stroke = theme.LineColor, StrokeWidth = 2, StrokeDashArray = [5, 5] });
                    eventY += height + EventSpacing;
                }

                periodY += spacing;
                if (title is null && !diagram.DisableMulticolor)
                {
                    periodColor++;
                }
            }

            // The next block starts below the periods and the events of this one
            y = periodY;
            if (title is not null || !diagram.DisableMulticolor)
            {
                colorIndex = title is null ? periodColor : colorIndex + 1;
            }
        }

        // The time axis, on the left of the events
        var contentBounds = bounds.ToBounds();
        var fontSize = theme.FontSize;
        var axisTop = Margin - (fontSize * 2);
        var axisBottom = contentBounds.Bottom + (fontSize * 0.5) + 20;
        lines.Add(new SceneLine(axisX, axisTop, axisX, axisBottom - 10) { Stroke = theme.LineColor, StrokeWidth = 4 });
        EdgeMarkers.Draw(lines, EdgeMarker.Arrow, new Layout.LayoutPoint(axisX, axisBottom), new Layout.LayoutPoint(axisX, axisBottom - 10), theme.LineColor, context.Background, 4);
        bounds.Add(new Layout.LayoutPoint(axisX, axisTop));
        bounds.Add(new Layout.LayoutPoint(axisX, axisBottom));
        return bounds.ToBounds();
    }

    /// <summary>Returns the height of a node as Mermaid: the text wrapped at the width, half a line, and the padding.</summary>
    private static double NodeHeight(DiagramRenderContext context, DiagramText text, double textWidth, double padding, double minimum)
    {
        var block = context.Text(text, maxWidth: textWidth);
        return Math.Max(block.Height + (context.FontSize * 1.1 * 0.5) + padding, minimum);
    }

    /// <summary>Draws a node: a box with rounded top corners in a color of the palette, a line of the inverse color at the bottom, and the text.</summary>
    private static void DrawNode(SceneGroup target, DiagramRenderContext context, DiagramText text, double x, double y, double width, double height, double textWidth, double padding, int colorIndex, string cssClass, BoundsBuilder bounds)
    {
        var fill = context.Theme.GetPaletteColor(colorIndex);
        var nodeGroup = target.Add(new SceneGroup { CssClass = "timeline-node " + cssClass + " section-" + colorIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        const double Radius = 5;
        var path = new PathBuilder()
            .MoveTo(x, y + height - Radius)
            .LineTo(x, y + Radius)
            .QuadraticTo(x, y, x + Radius, y)
            .LineTo(x + width - Radius, y)
            .QuadraticTo(x + width, y, x + width, y + Radius)
            .LineTo(x + width, y + height)
            .LineTo(x, y + height)
            .Close()
            .Build();
        nodeGroup.Add(new ScenePath(path) { Fill = fill });
        nodeGroup.Add(new SceneLine(x, y + height, x + width, y + height) { Stroke = Inverse(fill), StrokeWidth = 3 });
        var block = context.Text(text, maxWidth: textWidth);
        block.Draw(nodeGroup, x + (width / 2), y + (padding / 2) + (block.Height / 2) + (context.FontSize * 0.3), Contrast(fill));
        bounds.Add(new SceneBounds(x, y, width, height + 1.5));
    }

    /// <summary>Returns the inverse of a color, as the <c>cScaleInv</c> colors of Mermaid.</summary>
    internal static SceneColor Inverse(SceneColor color) => new((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B), color.A);

    /// <summary>Returns black or white, whichever reads better on a color.</summary>
    internal static SceneColor Contrast(SceneColor color)
    {
        var luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;
        return luminance > 0.6 ? SceneColor.Black : SceneColor.White;
    }
}
