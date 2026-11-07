using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The symbols of the list markers (see MarkerSymbols), painted as Blink paints them (TextFragmentPainter::PaintSymbol): a
// filled ellipse for disc, an ellipse stroked with 1px for circle, a filled square for square, a triangle for the disclosure
// markers, in the text fill color of the marker
internal sealed partial class Painter
{
    /// <summary>The points of the disclosure triangles of Blink in their unit square, by the direction they point to.</summary>
    private static readonly (double X, double Y)[] DisclosureLeft = [(1, 0), (0.14, 0.5), (1, 1)];
    private static readonly (double X, double Y)[] DisclosureRight = [(0, 0), (0.86, 0.5), (0, 1)];
    private static readonly (double X, double Y)[] DisclosureUp = [(0, 0.93), (0.5, 0.07), (1, 0.93)];
    private static readonly (double X, double Y)[] DisclosureDown = [(0, 0.07), (0.5, 0.93), (1, 0.07)];

    private void PaintMarkerSymbol(BoxFragment fragment, PhysicalOffset offset, MarkerSymbol symbol)
    {
        var style = fragment.Box.Style;
        var color = GetPrintedTextColor(style, GetColor(style, "-webkit-text-fill-color"));
        if (color.IsTransparent)
            return;

        // The rectangle of the shape is line-relative: from the line-left and line-over edges of the box
        var box = fragment.ContentBoxRect.Translate(offset);
        var (lineLeft, lineOver, inlineSize, blockSize) = MarkerSymbols.GetShapeRect(style, symbol);
        var writingDirection = fragment.WritingDirection;
        var rect = writingDirection.IsHorizontal
            ? new PhysicalRect(box.X + lineLeft, box.Y + lineOver, inlineSize, blockSize)
            : writingDirection.LineOver == PhysicalSide.Right
                ? new PhysicalRect(box.Right - lineOver - blockSize, box.Y + lineLeft, blockSize, inlineSize)
                : new PhysicalRect(box.X + lineOver, box.Bottom - lineLeft - inlineSize, blockSize, inlineSize);
        var paint = new DisplayPaint(color);
        var source = fragment.Box.Element;
        switch (symbol)
        {
            case MarkerSymbol.Disc:
                rect = SnapToDevicePixels(rect);
                _builder.Add(new DrawRoundedRectItem(GetEllipse(rect), paint) { Source = source });
                break;

            case MarkerSymbol.Circle:
                // A stroke of 1px centered on the edge of the ellipse
                rect = SnapToDevicePixels(rect);
                var outer = new PhysicalRect(rect.X - 0.5, rect.Y - 0.5, rect.Width + 1, rect.Height + 1);
                var inner = new PhysicalRect(rect.X + 0.5, rect.Y + 0.5, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));
                _builder.Add(new DrawRingItem(GetEllipse(outer), GetEllipse(inner), paint) { Source = source });
                break;

            case MarkerSymbol.Square:
                _builder.Add(new DrawRectItem(SnapToDevicePixels(rect), paint) { Source = source });
                break;

            default:
            {
                // An open triangle points to the block-end side, a closed one to the inline-end side
                var side = WritingDirection.Opposite(symbol == MarkerSymbol.DisclosureOpen ? writingDirection.BlockStart : writingDirection.InlineStart);
                var points = side switch
                {
                    PhysicalSide.Left => DisclosureLeft,
                    PhysicalSide.Right => DisclosureRight,
                    PhysicalSide.Top => DisclosureUp,
                    _ => DisclosureDown,
                };
                var path = new DisplayPath();
                path.MoveTo(rect.X + (points[0].X * rect.Width), rect.Y + (points[0].Y * rect.Height));
                for (var i = 1; i < points.Length; i++)
                {
                    path.LineTo(rect.X + (points[i].X * rect.Width), rect.Y + (points[i].Y * rect.Height));
                }

                path.Close();
                _builder.Add(new DrawPathItem(path, paint) { Source = source });
                break;
            }
        }

        static RoundedRect GetEllipse(PhysicalRect rect)
        {
            var (rx, ry) = (rect.Width / 2, rect.Height / 2);
            return new RoundedRect(rect, new BorderRadii((rx, ry), (rx, ry), (rx, ry), (rx, ry)));
        }
    }
}
