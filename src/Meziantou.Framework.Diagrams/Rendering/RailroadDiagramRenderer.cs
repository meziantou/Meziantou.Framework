using System.Globalization;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the railroad diagrams as Mermaid does: each rule on its own track, from a start marker after its name to an end
/// marker; a sequence goes left to right, a choice branches with arcs, an optional part has a bypass above it, and a
/// repetition a loop below it.
/// </summary>
internal static class RailroadDiagramRenderer
{
    private const double Padding = 10;
    private const double VerticalSeparation = 8;
    private const double HorizontalSeparation = 10;
    private const double ArcRadius = 10;
    private const double MarkerRadius = 5;
    private const double StrokeWidth = 2;
    private const int MaxDepth = 256;

    public static SceneBounds Render(RailroadDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var bounds = new BoundsBuilder();
        var y = Padding;
        foreach (var rule in diagram.Rules)
        {
            var ruleGroup = group.Add(new SceneGroup { CssClass = "railroad-rule" });
            ruleGroup.SetDataAttribute("name", rule.Name);
            var (width, height) = DrawRule(rule, context, ruleGroup, y);
            bounds.Add(new SceneBounds(0, y, width, height));
            y += height + VerticalSeparation;
        }

        if (diagram.Rules.Count is 0)
            return new SceneBounds(0, 0, 0, 0);

        var result = bounds.ToBounds();
        return new SceneBounds(result.X - Padding, result.Y - Padding, result.Width + (2 * Padding), result.Height + (2 * Padding));
    }

    private static (double Width, double Height) DrawRule(RailroadRule rule, DiagramRenderContext context, SceneGroup target, double top)
    {
        var theme = context.Theme;
        var name = context.Text(rule.Name + " =", bold: true);
        var nameWidth = name.Width + 20;
        var definitionX = nameWidth + 20;
        var box = Measure(rule.Definition, context, depth: 0);
        var baseline = top + Math.Max(20, box.Up);
        name.Draw(target, name.Width / 2, baseline, theme.TitleColor);
        Line(target, theme, nameWidth + MarkerRadius, baseline, definitionX, baseline);
        box.Draw(target, definitionX, baseline - box.Up);
        var end = definitionX + box.Width + 10;
        Line(target, theme, definitionX + box.Width, baseline, end - MarkerRadius, baseline);
        target.Add(new SceneCircle(nameWidth, baseline, MarkerRadius) { Fill = theme.LineColor, CssClass = "railroad-start" });
        target.Add(new SceneCircle(end, baseline, MarkerRadius) { Fill = theme.LineColor, CssClass = "railroad-end" });
        var definitionTop = baseline - box.Up;
        var height = Math.Max(40, definitionTop - top + box.Height + (Padding * 2));
        return (end + MarkerRadius, height);
    }

    /// <summary>The size of an expression around its track, which runs at <see cref="Up"/> from its top, and its drawing at a position.</summary>
    private sealed record Box(double Width, double Up, double Down, Action<SceneGroup, double, double> Draw)
    {
        public double Height => Up + Down;
    }

    private static Box Measure(RailroadExpression expression, DiagramRenderContext context, int depth)
    {
        if (depth > MaxDepth)
            return Empty();

        return expression switch
        {
            RailroadTerminal terminal => Text(context, terminal.Value, rounded: true, context.Theme.SecondaryColor, context.Theme.SecondaryBorderColor, context.Theme.SecondaryTextColor, "railroad-terminal"),
            RailroadNonterminal nonterminal => Text(context, nonterminal.Name, rounded: false, context.Theme.NodeBackground, context.Theme.PrimaryBorderColor, context.Theme.PrimaryTextColor, "railroad-nonterminal"),
            RailroadSpecial special => Text(context, "? " + special.Text + " ?", rounded: false, context.Theme.TertiaryColor, context.Theme.TertiaryBorderColor, context.Theme.TertiaryTextColor, "railroad-special"),
            RailroadSequence sequence => MeasureSequence(sequence.Items.Select(item => Measure(item, context, depth + 1)).ToList(), context),
            RailroadChoice choice => MeasureChoice(choice.Alternatives.Select(item => Measure(item, context, depth + 1)).ToList(), context),
            RailroadOptional optional => MeasureOptional(Measure(optional.Expression, context, depth + 1), context),
            RailroadRepetition repetition => MeasureRepetition(repetition, Measure(repetition.Expression, context, depth + 1), context),
            _ => Empty(),
        };
    }

    private static Box Empty() => new(0, 0, 0, (_, _, _) => { });

    private static Box Text(DiagramRenderContext context, string text, bool rounded, SceneColor fill, SceneColor stroke, SceneColor color, string cssClass)
    {
        var block = context.Text(text);
        var width = block.Width + (Padding * 2);
        var height = Math.Max(block.Height, context.FontSize) + (Padding * 2);
        return new Box(width, height / 2, height / 2, (target, x, y) =>
        {
            var group = target.Add(new SceneGroup { CssClass = cssClass });
            group.Add(new SceneRectangle(x, y, width, height) { Fill = fill, Stroke = stroke, StrokeWidth = StrokeWidth, CornerRadius = rounded ? 10 : 0 });

            block.Draw(group, x + (width / 2), y + (height / 2), color);
        });
    }

    private static Box MeasureSequence(List<Box> items, DiagramRenderContext context)
    {
        if (items.Count is 0)
            return Empty();

        var up = items.Max(item => item.Up);
        var down = items.Max(item => item.Down);
        var width = items.Sum(item => item.Width) + ((items.Count - 1) * HorizontalSeparation);
        return new Box(width, up, down, (target, x, y) =>
        {
            var group = target.Add(new SceneGroup { CssClass = "railroad-sequence" });
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.Draw(group, x, y + up - item.Up);
                if (i < items.Count - 1)
                {
                    Line(group, context.Theme, x + item.Width, y + up, x + item.Width + HorizontalSeparation, y + up);
                }

                x += item.Width + HorizontalSeparation;
            }
        });
    }

    private static Box MeasureChoice(List<Box> alternatives, DiagramRenderContext context)
    {
        if (alternatives.Count is 0)
            return Empty();

        var maxWidth = alternatives.Max(item => item.Width);
        var height = alternatives.Sum(item => item.Height) + ((alternatives.Count - 1) * VerticalSeparation);
        var width = maxWidth + (ArcRadius * 4);
        var center = height / 2;
        return new Box(width, center, height - center, (target, x, y) =>
        {
            var group = target.Add(new SceneGroup { CssClass = "railroad-choice" });
            var top = 0.0;
            foreach (var alternative in alternatives)
            {
                var itemX = (ArcRadius * 2) + ((maxWidth - alternative.Width) / 2);
                var itemCenter = top + alternative.Up;
                alternative.Draw(group, x + itemX, y + top);
                var left = new PathBuilder().MoveTo(x, y + center);
                var right = new PathBuilder().MoveTo(x + itemX + alternative.Width, y + itemCenter);
                if (Math.Abs(itemCenter - center) < 1e-9)
                {
                    left.LineTo(x + itemX, y + itemCenter);
                    right.LineTo(x + width, y + center);
                }
                else
                {
                    // The branch turns down to an alternative below the track, up to one above it
                    var below = itemCenter > center;
                    var sign = below ? 1 : -1;
                    left.ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: below, x + ArcRadius, y + center + (sign * ArcRadius))
                        .LineTo(x + ArcRadius, y + itemCenter - (sign * ArcRadius))
                        .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: !below, x + (ArcRadius * 2), y + itemCenter)
                        .LineTo(x + itemX, y + itemCenter);
                    right.LineTo(x + width - (ArcRadius * 2), y + itemCenter)
                        .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: !below, x + width - ArcRadius, y + itemCenter - (sign * ArcRadius))
                        .LineTo(x + width - ArcRadius, y + center + (sign * ArcRadius))
                        .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: below, x + width, y + center);
                }

                Path(group, context.Theme, left);
                Path(group, context.Theme, right);
                top += alternative.Height + VerticalSeparation;
            }
        });
    }

    private static Box MeasureOptional(Box inner, DiagramRenderContext context)
    {
        var width = inner.Width + (ArcRadius * 4);
        var itemY = ArcRadius * 2;
        var center = itemY + inner.Up;
        return new Box(width, center, inner.Down, (target, x, y) =>
        {
            var group = target.Add(new SceneGroup { CssClass = "railroad-optional" });
            inner.Draw(group, x + (ArcRadius * 2), y + itemY);
            Line(group, context.Theme, x, y + center, x + (ArcRadius * 2), y + center);
            Line(group, context.Theme, x + (ArcRadius * 2) + inner.Width, y + center, x + width, y + center);
            Path(group, context.Theme, Bypass(x, y, width, center));
        });
    }

    private static Box MeasureRepetition(RailroadRepetition repetition, Box inner, DiagramRenderContext context)
    {
        var canSkip = repetition.Minimum is 0;
        var canLoop = repetition.Maximum is not (0 or 1);
        var bounds = FormatBounds(repetition);
        var label = bounds is null ? null : context.Text(bounds, context.FontSize * 0.75);
        var width = Math.Max(inner.Width + (ArcRadius * 4), (label?.Width ?? 0) + (ArcRadius * 4));
        var itemX = (width - inner.Width) / 2;
        var itemY = canSkip ? ArcRadius * 2 : 0;
        var center = itemY + inner.Up;
        var loopY = itemY + inner.Height + ArcRadius;
        var down = inner.Down + (canLoop ? ArcRadius * 2 : 0) + (label is null ? 0 : label.Height + 2);
        return new Box(width, center, down, (target, x, y) =>
        {
            var group = target.Add(new SceneGroup { CssClass = "railroad-repetition" });
            inner.Draw(group, x + itemX, y + itemY);
            Line(group, context.Theme, x, y + center, x + itemX, y + center);
            Line(group, context.Theme, x + itemX + inner.Width, y + center, x + width, y + center);
            if (canLoop)
            {
                // The loop goes back below the expression, from its end to its start
                var right = x + itemX + inner.Width;
                var left = x + itemX;
                Path(group, context.Theme, new PathBuilder()
                    .MoveTo(right, y + center)
                    .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: true, right + ArcRadius, y + center + ArcRadius)
                    .LineTo(right + ArcRadius, y + loopY)
                    .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: true, right, y + loopY + ArcRadius)
                    .LineTo(left, y + loopY + ArcRadius)
                    .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: true, left - ArcRadius, y + loopY)
                    .LineTo(left - ArcRadius, y + center + ArcRadius)
                    .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: true, left, y + center));
            }

            if (canSkip)
            {
                Path(group, context.Theme, Bypass(x, y, width, center));
            }

            label?.Draw(group, x + (width / 2), y + center + down - (label.Height / 2), context.Theme.TextColor);
        });
    }

    /// <summary>Returns the bounds of a repetition when they are not 0 or 1 to unbounded: <c>2+</c>, <c>3</c>, <c>2..4</c>.</summary>
    internal static string? FormatBounds(RailroadRepetition repetition) => (repetition.Minimum, repetition.Maximum) switch
    {
        (0 or 1, null) => null,
        (0, 1) => null,
        (var minimum, null) => minimum.ToString(CultureInfo.InvariantCulture) + "+",
        (var minimum, var maximum) when minimum == maximum => minimum.ToString(CultureInfo.InvariantCulture),
        (var minimum, var maximum) => minimum.ToString(CultureInfo.InvariantCulture) + ".." + maximum.Value.ToString(CultureInfo.InvariantCulture),
    };

    // The bypass above an expression, from the start of the track to its end
    private static PathBuilder Bypass(double x, double y, double width, double center) => new PathBuilder()
        .MoveTo(x, y + center)
        .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: false, x + ArcRadius, y + center - ArcRadius)
        .LineTo(x + ArcRadius, y + ArcRadius)
        .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: true, x + (ArcRadius * 2), y)
        .LineTo(x + width - (ArcRadius * 2), y)
        .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: true, x + width - ArcRadius, y + ArcRadius)
        .LineTo(x + width - ArcRadius, y + center - ArcRadius)
        .ArcTo(ArcRadius, ArcRadius, 0, largeArc: false, sweep: false, x + width, y + center);

    private static void Line(SceneGroup target, DiagramTheme theme, double x1, double y1, double x2, double y2) => Path(target, theme, new PathBuilder().MoveTo(x1, y1).LineTo(x2, y2));

    private static void Path(SceneGroup target, DiagramTheme theme, PathBuilder path) => target.Add(new ScenePath(path.Build()) { Stroke = theme.LineColor, StrokeWidth = StrokeWidth, CssClass = "railroad-line" });
}
