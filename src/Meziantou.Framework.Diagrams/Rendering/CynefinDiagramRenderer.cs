namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the Cynefin diagrams as Mermaid does: the four quadrants with their names and descriptions, the wavy boundaries
/// between them, the cliff between the clear and the chaotic domains, the confusion ellipse in the center, the items as
/// badges below the names of their domains, and the transitions as curved arrows between the centers of the domains.
/// </summary>
internal static class CynefinDiagramRenderer
{
    private const double ItemHeight = 26;
    private const double ItemGap = 4;
    private const double ItemPaddingX = 10;
    private const int MaxConfusionItems = 3;

    private static readonly CynefinDomain[] QuadrantDomains = [CynefinDomain.Complex, CynefinDomain.Complicated, CynefinDomain.Chaotic, CynefinDomain.Clear];

    private static readonly CynefinDomain[] AllDomains = [CynefinDomain.Complex, CynefinDomain.Complicated, CynefinDomain.Chaotic, CynefinDomain.Clear, CynefinDomain.Confusion];

    public static SceneBounds Render(CynefinDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var overrides = diagram.Colors;
        var palette = GetPalette(theme);
        var width = diagram.Width;
        var height = diagram.Height;
        var showDescriptions = diagram.ShowDomainDescriptions;
        var boundaryColor = overrides.BoundaryColor ?? theme.LineColor;
        var boundaryWidth = overrides.BoundaryWidth ?? 2;
        var cliffColor = overrides.CliffColor ?? palette.Cliff;
        var cliffWidth = overrides.CliffWidth ?? 4;
        var arrowColor = overrides.ArrowColor ?? theme.LineColor;
        var arrowWidth = overrides.ArrowWidth ?? 2;
        var labelColor = overrides.LabelColor ?? theme.PrimaryTextColor;
        var textColor = overrides.TextColor ?? theme.TextColor;
        var domainFontSize = overrides.DomainFontSize ?? 16;
        var itemFontSize = overrides.ItemFontSize ?? 12;
        var backgrounds = new Dictionary<CynefinDomain, SceneColor>
        {
            [CynefinDomain.Complex] = overrides.ComplexBackground ?? palette.Complex,
            [CynefinDomain.Complicated] = overrides.ComplicatedBackground ?? palette.Complicated,
            [CynefinDomain.Clear] = overrides.ClearBackground ?? palette.Clear,
            [CynefinDomain.Chaotic] = overrides.ChaoticBackground ?? palette.Chaotic,
            [CynefinDomain.Confusion] = overrides.ConfusionBackground ?? palette.Confusion,
        };

        // 1. The backgrounds of the quadrants
        var backgroundGroup = group.Add(new SceneGroup { CssClass = "cynefin-backgrounds" });
        foreach (var domain in QuadrantDomains)
        {
            var area = GetArea(domain, width, height);
            backgroundGroup.Add(new SceneRectangle(area.X, area.Y, area.Width, area.Height) { Fill = Fade(backgrounds[domain], 0.4), Stroke = null, StrokeWidth = 0, CssClass = "cynefinDomain" });
        }

        // 2. The wavy boundaries, and 3. the cliff between the clear and the chaotic domains
        var seed = diagram.Seed is 0 ? HashString("cynefin") : diagram.Seed;
        var boundaryGroup = group.Add(new SceneGroup { CssClass = "cynefin-boundaries" });
        if (boundaryWidth > 0)
        {
            boundaryGroup.Add(new ScenePath(CreateFoldPath(width, height, seed, diagram.BoundaryAmplitude)) { Stroke = boundaryColor, StrokeWidth = boundaryWidth, StrokeDashArray = [6, 3], CssClass = "cynefinBoundary" });
            boundaryGroup.Add(new ScenePath(CreateHorizontalBoundary(width, height, seed + 100L, diagram.BoundaryAmplitude)) { Stroke = boundaryColor, StrokeWidth = boundaryWidth, StrokeDashArray = [6, 3], CssClass = "cynefinBoundary" });
        }

        if (cliffWidth > 0)
        {
            boundaryGroup.Add(new ScenePath(CreateCliffPath(width, height)) { Stroke = cliffColor, StrokeWidth = cliffWidth, CssClass = "cynefinCliff" });
        }

        // 4. The confusion ellipse in the center
        var confusion = new PathBuilder()
            .MoveTo((width / 2) - (width * 0.15), height / 2)
            .ArcTo(width * 0.15, height * 0.15, 0, largeArc: true, sweep: true, (width / 2) + (width * 0.15), height / 2)
            .ArcTo(width * 0.15, height * 0.15, 0, largeArc: true, sweep: true, (width / 2) - (width * 0.15), height / 2)
            .Close()
            .Build();
        group.Add(new ScenePath(confusion) { Fill = Fade(backgrounds[CynefinDomain.Confusion], 0.5), Stroke = boundaryColor, StrokeWidth = 1.5, StrokeDashArray = [4, 2], CssClass = "cynefinConfusion" });

        // 5. The names of the domains, and 6. their descriptions
        var labelGroup = group.Add(new SceneGroup { CssClass = "cynefin-labels" });
        var subtitleFontSize = Math.Max(1, itemFontSize - 1);
        foreach (var domain in QuadrantDomains)
        {
            var area = GetArea(domain, width, height);
            context.Text(GetName(domain), domainFontSize, bold: true).Draw(labelGroup, area.CenterX, showDescriptions ? area.CenterY - 30 : area.CenterY, labelColor);
            if (showDescriptions)
            {
                var (model, practice) = GetDescription(domain);
                context.Text(model, subtitleFontSize, italic: true).Draw(labelGroup, area.CenterX, area.CenterY - 10, textColor);
                context.Text(practice, subtitleFontSize, italic: true).Draw(labelGroup, area.CenterX, area.CenterY + 5, textColor);
            }
        }

        context.Text("Confusion", domainFontSize, bold: true).Draw(labelGroup, width / 2, showDescriptions ? (height / 2) - 10 : height / 2, labelColor);
        if (showDescriptions)
        {
            context.Text("Disorder", subtitleFontSize, italic: true).Draw(labelGroup, width / 2, (height / 2) + 8, textColor);
        }

        // 7. The items, as badges below the names of their domains
        var itemGroup = group.Add(new SceneGroup { CssClass = "cynefin-items" });
        foreach (var domain in AllDomains)
        {
            var items = diagram.Items.Where(item => item.Domain == domain).ToList();
            if (items.Count is 0)
                continue;

            var area = GetArea(domain, width, height);
            var isConfusion = domain is CynefinDomain.Confusion;
            var overflow = isConfusion && items.Count > MaxConfusionItems ? items.Count - MaxConfusionItems : 0;
            var y = area.CenterY + (isConfusion ? (showDescriptions ? 22 : 14) : (showDescriptions ? 25 : 15));
            var background = backgrounds[domain];
            foreach (var item in overflow > 0 ? items.Take(MaxConfusionItems) : items)
            {
                y += DrawBadge(itemGroup, context.Text(item.Label, itemFontSize), area.CenterX, y, Fade(background, 0.95), boundaryColor, textColor, dashed: false) + ItemGap;
            }

            if (overflow > 0)
            {
                var text = context.Text("+" + overflow.ToString(System.Globalization.CultureInfo.InvariantCulture) + " more", itemFontSize);
                DrawBadge(itemGroup, text, area.CenterX, y, Fade(background, 0.6), boundaryColor, textColor, dashed: true);
            }
        }

        // 8. The transitions, as quadratic curves between the centers of the domains, bent to their left
        var arrowGroup = group.Add(new SceneGroup { CssClass = "cynefin-arrows" });
        foreach (var transition in diagram.Transitions)
        {
            if (transition.From == transition.To)
                continue;

            var from = GetArea(transition.From, width, height);
            var to = GetArea(transition.To, width, height);
            var dx = to.CenterX - from.CenterX;
            var dy = to.CenterY - from.CenterY;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (!(length > 0))
                continue;

            var offset = length * 0.15;
            var controlX = ((from.CenterX + to.CenterX) / 2) + (-dy / length * offset);
            var controlY = ((from.CenterY + to.CenterY) / 2) + (dx / length * offset);
            var path = new PathBuilder().MoveTo(from.CenterX, from.CenterY).QuadraticTo(controlX, controlY, to.CenterX, to.CenterY).Build();
            arrowGroup.Add(new ScenePath(path) { Stroke = arrowColor, StrokeWidth = arrowWidth, CssClass = "cynefinArrowLine" });
            DrawArrowHead(arrowGroup, to.CenterX, to.CenterY, to.CenterX - controlX, to.CenterY - controlY, arrowWidth, arrowColor);
            if (transition.Label is { IsEmpty: false } label)
            {
                // The baseline of the text is 6 pixels above the control point of the curve
                var text = context.Text(label, subtitleFontSize);
                text.Draw(arrowGroup, controlX, controlY - 6 - (text.Height / 2), textColor);
            }
        }

        return new SceneBounds(0, 0, width, height);
    }

    /// <summary>Draws a badge: a rounded rectangle around a text, centered on <paramref name="centerX"/>, below <paramref name="top"/>.</summary>
    /// <returns>The height of the badge.</returns>
    private static double DrawBadge(SceneGroup group, TextBlock text, double centerX, double top, SceneColor fill, SceneColor stroke, SceneColor textColor, bool dashed)
    {
        var width = text.Width + (ItemPaddingX * 2);
        var height = Math.Max(ItemHeight, text.Height + 8);
        var badge = group.Add(new SceneGroup { CssClass = dashed ? "cynefin-overflow" : "cynefin-item" });
        badge.Add(new SceneRectangle(centerX - (width / 2), top, width, height)
        {
            CornerRadius = 4,
            Fill = fill,
            Stroke = stroke,
            StrokeWidth = 1,
            StrokeDashArray = dashed ? [3, 2] : null,
            CssClass = dashed ? "cynefinItemOverflow" : "cynefinItem",
        });
        text.Draw(badge, centerX, top + (height / 2), textColor);
        return height;
    }

    /// <summary>Draws the arrow head of Mermaid: a triangle of 10 by 10 units, scaled by 0.6 times the width of the line, whose tip is one unit past the end of the line.</summary>
    private static void DrawArrowHead(SceneGroup group, double x, double y, double directionX, double directionY, double lineWidth, SceneColor color)
    {
        var length = Math.Sqrt((directionX * directionX) + (directionY * directionY));
        if (!(length > 0) || !(lineWidth > 0))
            return;

        var scale = 0.6 * lineWidth;
        var ux = directionX / length;
        var uy = directionY / length;
        var baseX = x - (ux * 9 * scale);
        var baseY = y - (uy * 9 * scale);
        var head = new PathBuilder()
            .MoveTo(x + (ux * scale), y + (uy * scale))
            .LineTo(baseX - (uy * 5 * scale), baseY + (ux * 5 * scale))
            .LineTo(baseX + (uy * 5 * scale), baseY - (ux * 5 * scale))
            .Close()
            .Build();
        group.Add(new ScenePath(head) { Fill = color, Stroke = null, StrokeWidth = 0, CssClass = "cynefinArrowHead" });
    }

    /// <summary>The vertical wavy line between the left and the right domains: 7 cubic segments around the center.</summary>
    internal static PathData CreateFoldPath(double width, double height, long seed, double amplitude)
    {
        const int Segments = 7;
        var centerX = width / 2;
        var segmentHeight = height / Segments;
        var points = new (double X, double Y)[Segments + 1];
        for (var i = 0; i <= Segments; i++)
        {
            var jitter = (SeededRandom(seed + (i * 17L)) * amplitude * 2) - amplitude;
            points[i] = (centerX + jitter, i * segmentHeight);
        }

        var builder = new PathBuilder().MoveTo(points[0].X, points[0].Y);
        for (var i = 0; i < Segments; i++)
        {
            var (x0, y0) = points[i];
            var (x1, y1) = points[i + 1];
            var middleY = (y0 + y1) / 2;
            var direction = i % 2 == 0 ? 1 : -1;
            var offset = amplitude * 1.5 * direction * SeededRandom(seed + (i * 31L) + 7);
            builder.CubicTo(x0 + offset, middleY, x1 - offset, middleY, x1, y1);
        }

        return builder.Build();
    }

    /// <summary>The horizontal wavy line between the top and the bottom domains.</summary>
    internal static PathData CreateHorizontalBoundary(double width, double height, long seed, double amplitude)
    {
        const int Segments = 7;
        var centerY = height / 2;
        var segmentWidth = width / Segments;
        var points = new (double X, double Y)[Segments + 1];
        for (var i = 0; i <= Segments; i++)
        {
            var jitter = (SeededRandom(seed + (i * 23L)) * amplitude * 2) - amplitude;
            points[i] = (i * segmentWidth, centerY + jitter);
        }

        var builder = new PathBuilder().MoveTo(points[0].X, points[0].Y);
        for (var i = 0; i < Segments; i++)
        {
            var (x0, y0) = points[i];
            var (x1, y1) = points[i + 1];
            var middleX = (x0 + x1) / 2;
            var direction = i % 2 == 0 ? 1 : -1;
            var offset = amplitude * 1.5 * direction * SeededRandom(seed + (i * 37L) + 11);
            builder.CubicTo(middleX, y0 + offset, middleX, y1 - offset, x1, y1);
        }

        return builder.Build();
    }

    /// <summary>The cliff: a steep S-curve from the center of the diagram down to the middle of its bottom.</summary>
    internal static PathData CreateCliffPath(double width, double height)
    {
        var centerX = width / 2;
        var top = height * 0.5;
        var span = height - top;
        var amplitude = width * 0.03;
        return new PathBuilder()
            .MoveTo(centerX, top)
            .CubicTo(centerX + amplitude, top + (span * 0.2), centerX - (amplitude * 1.5), top + (span * 0.55), centerX + (amplitude * 0.5), top + (span * 0.75))
            .CubicTo(centerX - amplitude, top + (span * 0.85), centerX + (amplitude * 0.3), top + (span * 0.95), centerX, height)
            .Build();
    }

    /// <summary>The pseudo-random generator of Mermaid (mulberry32), with the 32-bit arithmetic of JavaScript: a value in [0, 1).</summary>
    internal static double SeededRandom(long seed)
    {
        var t = unchecked((int)(seed + 0x6d2b79f5L));
        t = unchecked((t ^ (int)((uint)t >> 15)) * (t | 1));
        t ^= unchecked(t + ((t ^ (int)((uint)t >> 7)) * (t | 61)));
        return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
    }

    /// <summary>The string hash of Mermaid (<c>hash * 31 + c</c> on 32 bits), which seeds the waves.</summary>
    internal static int HashString(string value)
    {
        var hash = 0;
        foreach (var c in value)
        {
            hash = unchecked((hash << 5) - hash + c);
        }

        return hash;
    }

    private static string GetName(CynefinDomain domain) => domain switch
    {
        CynefinDomain.Complex => "Complex",
        CynefinDomain.Complicated => "Complicated",
        CynefinDomain.Clear => "Clear",
        CynefinDomain.Chaotic => "Chaotic",
        _ => "Confusion",
    };

    private static (string Model, string Practice) GetDescription(CynefinDomain domain) => domain switch
    {
        CynefinDomain.Complex => ("Probe \u2192 Sense \u2192 Respond", "Emergent Practices"),
        CynefinDomain.Complicated => ("Sense \u2192 Analyse \u2192 Respond", "Good Practices"),
        CynefinDomain.Clear => ("Sense \u2192 Categorise \u2192 Respond", "Best Practices"),
        CynefinDomain.Chaotic => ("Act \u2192 Sense \u2192 Respond", "Novel Practices"),
        _ => (string.Empty, "Disorder"),
    };

    /// <summary>The area of a domain: a quarter of the diagram, or the middle 60 % of each axis for the confusion domain.</summary>
    private static (double X, double Y, double Width, double Height, double CenterX, double CenterY) GetArea(CynefinDomain domain, double width, double height)
    {
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        return domain switch
        {
            CynefinDomain.Complex => (0, 0, halfWidth, halfHeight, halfWidth / 2, halfHeight / 2),
            CynefinDomain.Complicated => (halfWidth, 0, halfWidth, halfHeight, halfWidth + (halfWidth / 2), halfHeight / 2),
            CynefinDomain.Chaotic => (0, halfHeight, halfWidth, halfHeight, halfWidth / 2, halfHeight + (halfHeight / 2)),
            CynefinDomain.Clear => (halfWidth, halfHeight, halfWidth, halfHeight, halfWidth + (halfWidth / 2), halfHeight + (halfHeight / 2)),
            _ => (halfWidth * 0.7, halfHeight * 0.7, halfWidth * 0.6, halfHeight * 0.6, halfWidth, halfHeight),
        };
    }

    private static SceneColor Fade(SceneColor color, double opacity) => color.WithOpacity(color.Opacity * opacity);

    /// <summary>The backgrounds of the domains and the color of the cliff in the themes of Mermaid.</summary>
    private static (SceneColor Complex, SceneColor Complicated, SceneColor Clear, SceneColor Chaotic, SceneColor Confusion, SceneColor Cliff) GetPalette(DiagramTheme theme)
    {
        if (ReferenceEquals(theme, DiagramTheme.Forest))
            return (SceneColor.Parse("#C8E6C9"), SceneColor.Parse("#DCEDC8"), SceneColor.Parse("#FFF9C4"), SceneColor.Parse("#FFE0B2"), SceneColor.Parse("#D7CCC8"), SceneColor.Parse("#8B4513"));

        if (theme.IsDark)
            return (SceneColor.Parse("#1B5E20"), SceneColor.Parse("#0D47A1"), SceneColor.Parse("#F57F17"), SceneColor.Parse("#BF360C"), SceneColor.Parse("#4A148C"), SceneColor.Parse("#FF6B6B"));

        return (SceneColor.Parse("#E8F5E9"), SceneColor.Parse("#E3F2FD"), SceneColor.Parse("#FFF8E1"), SceneColor.Parse("#FBE9E7"), SceneColor.Parse("#F3E5F5"), SceneColor.Parse("#8B0000"));
    }
}
