namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the kanban boards as Mermaid does: a column per section, in the colors of the palette of the theme, and the
/// cards stacked in it, with the ticket on the bottom left, the assignee on the bottom right and a stripe for the priority.
/// </summary>
internal static class KanbanDiagramRenderer
{
    private const double Padding = 10;
    private const double MinTitleHeight = 25;

    public static SceneBounds Render(KanbanDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var width = diagram.ColumnWidth;
        var cardWidth = width - (1.5 * Padding);
        var titles = diagram.Columns.Select(column => context.Text(column.Label ?? column.Id, maxWidth: width - (2 * Padding))).ToList();
        var titleHeight = Math.Max(MinTitleHeight, titles.Count is 0 ? 0 : titles.Max(title => title.Height + Padding));
        var bounds = new BoundsBuilder();
        var sections = group.Add(new SceneGroup { CssClass = "sections" });
        var items = group.Add(new SceneGroup { CssClass = "items" });
        var smallFontSize = context.FontSize * 0.875;
        for (var i = 0; i < diagram.Columns.Count; i++)
        {
            var column = diagram.Columns[i];

            // As in Mermaid, the columns are 5 pixels apart, and the first one takes the second color of the palette
            var centerX = (width * (i + 1)) + (i * Padding / 2);
            var left = centerX - (width / 2);
            var top = 0.0;
            var y = top + titleHeight;
            var cardsGroup = items.Add(new SceneGroup { CssClass = "column-items" });
            foreach (var card in column.Cards)
            {
                var label = context.Text(card.Label ?? card.Id, maxWidth: cardWidth - (2 * Padding));
                var ticket = string.IsNullOrEmpty(card.Ticket) ? null : context.Text(card.Ticket, smallFontSize);
                var assigned = string.IsNullOrEmpty(card.Assigned) ? null : context.Text(card.Assigned, smallFontSize);
                var infoHeight = Math.Max(ticket?.Height ?? 0, assigned?.Height ?? 0);
                var height = label.Height + (2 * Padding) + (infoHeight > 0 ? infoHeight + (Padding / 2) : 0);
                var cardLeft = centerX - (cardWidth / 2);
                var cardGroup = cardsGroup.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(card.CssClasses, "kanban-item") });
                cardGroup.SetDataAttribute("id", card.Id);
                cardGroup.Add(new SceneRectangle(cardLeft, y, cardWidth, height) { Fill = context.Background, Stroke = theme.NodeBorderColor, CornerRadius = 5 });
                if (card.Priority is { } priority && GetPriorityColor(priority) is { } stripe)
                {
                    cardGroup.Add(new SceneLine(cardLeft + 2, y + 2, cardLeft + 2, y + height - 2) { Stroke = stripe, StrokeWidth = 4, CssClass = "priority" });
                }

                label.Draw(cardGroup, cardLeft + Padding + (label.Width / 2), y + Padding + (label.Height / 2), theme.TextColor, SceneTextAnchor.Start);
                var infoCenterY = y + Padding + label.Height + (Padding / 2) + (infoHeight / 2);
                if (ticket is not null)
                {
                    var url = GetTicketLink(diagram.TicketLinkFormat, card.Ticket);
                    var ticketGroup = cardGroup.Add(new SceneGroup { CssClass = "kanban-ticket", Hyperlink = url });
                    ticket.Draw(ticketGroup, cardLeft + Padding + (ticket.Width / 2), infoCenterY, theme.TextColor);
                    if (url is not null)
                    {
                        var underline = infoCenterY + (ticket.Height / 2) - 1;
                        ticketGroup.Add(new SceneLine(cardLeft + Padding, underline, cardLeft + Padding + ticket.Width, underline) { Stroke = theme.TextColor, StrokeWidth = 1 });
                    }
                }

                assigned?.Draw(cardGroup, cardLeft + cardWidth - Padding - (assigned.Width / 2), infoCenterY, theme.TextColor);
                y += height + (Padding / 2);
            }

            var columnHeight = Math.Max(y - top - titleHeight + (3 * Padding), 50) + (titleHeight - MinTitleHeight);
            var fill = Adjust(theme.Palette.Count > 0 ? theme.GetPaletteColor(i + 2) : theme.ClusterBackground, theme.IsDark);
            var section = sections.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(column.CssClasses, "section section-" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) });
            section.SetDataAttribute("id", column.Id);
            section.Add(new SceneRectangle(left, top, width, Math.Max(columnHeight, titleHeight + Padding)) { Fill = fill, Stroke = fill, CornerRadius = 5 });
            titles[i].Draw(section, centerX, top + (titleHeight / 2), GitGraphColors.IsLight(fill) ? SceneColor.Black : SceneColor.White);
            bounds.Add(new SceneBounds(left, top, width, Math.Max(columnHeight, titleHeight + Padding)));
        }

        return bounds.ToBounds();
    }

    internal static SceneColor? GetPriorityColor(KanbanPriority priority) => priority switch
    {
        KanbanPriority.VeryHigh => (SceneColor?)SceneColor.Parse("red"),
        KanbanPriority.High => SceneColor.Parse("orange"),
        KanbanPriority.Low => SceneColor.Parse("blue"),
        KanbanPriority.VeryLow => SceneColor.Parse("lightblue"),
        _ => null,
    };

    /// <summary>Lightens a color by 10% of lightness, or darkens it in a dark theme, as the sections of Mermaid.</summary>
    private static SceneColor Adjust(SceneColor color, bool dark)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        double hue = 0;
        double saturation = 0;
        if (max - min > 1e-9)
        {
            var delta = max - min;
            saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
            hue = max == r ? ((g - b) / delta) + (g < b ? 6 : 0) : max == g ? ((b - r) / delta) + 2 : ((r - g) / delta) + 4;
            hue *= 60;
        }

        lightness = Math.Clamp(lightness + (dark ? -0.1 : 0.1), 0, 1);
        return SceneColor.FromHsl(hue, saturation, lightness);
    }

    /// <summary>
    /// Returns the address of a ticket, or <see langword="null"/> when it is not an http, https, mailto or relative address:
    /// the ticket completes the address, so a safe format such as <c>#TICKET#</c> can become a <c>javascript:</c> address.
    /// </summary>
    internal static string? GetTicketLink(string? format, string? ticket)
    {
        if (string.IsNullOrEmpty(format))
            return null;

        var url = format.Replace("#TICKET#", ticket, StringComparison.Ordinal);
        var trimmed = url.Trim();
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var slash = trimmed.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (slash >= 0 && slash < colon))
            return url;

        return trimmed[..colon].ToLowerInvariant() is "http" or "https" or "mailto" ? url : null;
    }
}
