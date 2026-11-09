using System.Globalization;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the packet diagrams as Mermaid does: a box per field in rows of bits, the field cut where it crosses the end of
/// a row, its name in the middle, and the numbers of its first and last bits above it.
/// </summary>
internal static class PacketDiagramRenderer
{
    private const double PaddingX = 5;
    private const double BasePaddingY = 5;

    // Mermaid stops at 10000 rows
    private const int MaxRows = 10_000;

    public static SceneBounds Render(PacketDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var bitsPerRow = diagram.BitsPerRow;
        var bitWidth = diagram.BitWidth;
        var rowHeight = diagram.RowHeight;
        var paddingY = BasePaddingY + (diagram.ShowBits ? 10 : 0);
        var fill = theme.IsDark ? theme.NodeBackground : SceneColor.Parse("#efefef");
        var stroke = theme.IsDark ? theme.NodeBorderColor : SceneColor.Black;
        var textColor = theme.IsDark ? theme.TextColor : SceneColor.Black;
        var labelFontSize = context.FontSize * 0.75;
        var bitFont = new SceneFont(context.FontSize * 0.625);
        var bounds = new BoundsBuilder();
        var rows = 0;
        foreach (var field in diagram.Fields)
        {
            var label = context.Text(field.Label, labelFontSize);
            var fieldGroup = group.Add(new SceneGroup { CssClass = "packet-field" });

            // A field that crosses the end of a row continues on the next one
            var start = field.Start;
            while (start <= field.End)
            {
                var row = start / bitsPerRow;
                if (row >= MaxRows)
                    break;

                rows = Math.Max(rows, row + 1);
                var end = Math.Min(field.End, ((row + 1) * bitsPerRow) - 1);
                var bits = end - start + 1;
                var firstColumn = start % bitsPerRow;
                var column = diagram.DescendingBitOrder ? bitsPerRow - firstColumn - bits : firstColumn;
                var x = (column * bitWidth) + 1;
                var y = (row * (rowHeight + paddingY)) + paddingY;
                var width = (bits * bitWidth) - PaddingX;
                fieldGroup.Add(new SceneRectangle(x, y, width, rowHeight) { Fill = fill, Stroke = stroke, StrokeWidth = 1, CssClass = "packet-block" });
                label.Draw(fieldGroup, x + (width / 2), y + (rowHeight / 2), textColor);
                bounds.Add(new SceneBounds(x, y, width, rowHeight));
                if (diagram.ShowBits)
                {
                    var (leading, trailing) = diagram.DescendingBitOrder ? (end, start) : (start, end);
                    var bitY = y - 2;
                    if (bits == 1)
                    {
                        fieldGroup.Add(new SceneText(x + (width / 2), bitY, leading.ToString(CultureInfo.InvariantCulture), bitFont, textColor) { Anchor = SceneTextAnchor.Middle, CssClass = "packet-bit" });
                    }
                    else
                    {
                        fieldGroup.Add(new SceneText(x, bitY, leading.ToString(CultureInfo.InvariantCulture), bitFont, textColor) { CssClass = "packet-bit start" });
                        fieldGroup.Add(new SceneText(x + width, bitY, trailing.ToString(CultureInfo.InvariantCulture), bitFont, textColor) { Anchor = SceneTextAnchor.End, CssClass = "packet-bit end" });
                    }

                    bounds.Add(new SceneBounds(x, bitY - bitFont.Size, width, bitFont.Size));
                }

                start = end + 1;
            }
        }

        // The diagram is as wide as a row, and has the padding below the last row as Mermaid
        bounds.Add(new SceneBounds(0, 0, (bitWidth * bitsPerRow) + 2, (rows * (rowHeight + paddingY)) + paddingY));
        return bounds.ToBounds();
    }
}
