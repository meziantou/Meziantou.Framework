namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws an annotation per row: a text placed at an offset from a point of the chart, and an arrow from the text to the
/// point. The text is anchored on the side of the offset, so it stays clear of the point; a line break starts a new line.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class AnnotationMark<T> : Mark<T>
{
    private const double ArrowGap = 4;

    /// <summary>Initializes a new instance of the <see cref="AnnotationMark{T}"/> class.</summary>
    /// <param name="data">The rows: the annotations.</param>
    /// <param name="x">The channel of the x position of the point.</param>
    /// <param name="y">The channel of the y position of the point.</param>
    /// <param name="text">The channel of the text.</param>
    public AnnotationMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y, Func<T, string?> text)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(text);
        X = x;
        Y = y;
        Text = text;
    }

    /// <summary>Gets the channel of the x position of the point.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the y position of the point.</summary>
    public Func<T, object?> Y { get; }

    /// <summary>Gets the channel of the text.</summary>
    public Func<T, string?> Text { get; }

    /// <summary>Gets or sets the horizontal offset of the text from the point, in pixels. The default is 40.</summary>
    public double DX { get; set; } = 40;

    /// <summary>Gets or sets the vertical offset of the text from the point, in pixels: negative values are above it. The default is -30.</summary>
    public double DY { get; set; } = -30;

    /// <summary>Gets or sets the angle of the curve of the arrow, in degrees: 0 for a straight arrow, positive values bend it clockwise. The default is 0.</summary>
    public double Bend { get; set; }

    /// <summary>Gets or sets the size of the head of the arrow, in pixels, or 0 for a line without a head. The default is 6.</summary>
    public double HeadSize { get; set; } = 6;

    /// <summary>Gets or sets the font size of the text, or <see langword="null"/> for the font size of the theme.</summary>
    public double? FontSize { get; set; }

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
        data.Text = Evaluate(rows, Text);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var font = new ChartFont(FontSize ?? context.Theme.FontSize);
        var metrics = context.Measurer.Measure("Xg", font);
        for (var i = 0; i < data.Count; i++)
        {
            var x = PointSeries.Center(context.X, context.MapX(data.X, i));
            var y = PointSeries.Center(context.Y, context.MapY(data.Y, i));
            if (data.Text?[i] is not { Length: > 0 } text || double.IsNaN(x) || double.IsNaN(y))
                continue;

            var (px, py) = context.Coordinates.Project(x, y);
            var lines = text.Split('\n');
            var widths = lines.Select(line => context.Measurer.Measure(line, font).Width).ToArray();
            var width = widths.Max();
            var height = lines.Length * metrics.Height;

            // The box of the text: its anchor is at the offset, on the side away from the point
            var anchor = DX > 0 ? SceneTextAnchor.Start : DX < 0 ? SceneTextAnchor.End : SceneTextAnchor.Middle;
            var tx = px + DX;
            var boxLeft = anchor switch { SceneTextAnchor.Start => tx, SceneTextAnchor.End => tx - width, _ => tx - (width / 2) };
            var boxTop = DY < 0 ? py + DY - height : DY > 0 ? py + DY : py - (height / 2);
            if (DY == 0 && DX == 0)
            {
                boxTop = py - height - ArrowGap;
            }

            var annotation = new SceneGroup();
            var stroke = data.Stroke is null && StrokeColor is null ? context.Theme.MutedForeground : GetStroke(context, data, i);
            var start = ClipToBox(px, py, boxLeft - ArrowGap, boxTop - ArrowGap, width + (2 * ArrowGap), height + (2 * ArrowGap));
            if (start is { } from && Math.Abs(from.X - px) + Math.Abs(from.Y - py) > 2 * ArrowGap)
            {
                AddArrow(annotation, from.X, from.Y, px, py, stroke);
            }

            var color = data.Fill is null && FillColor is null ? context.Theme.Foreground : GetFill(context, data, i);
            for (var line = 0; line < lines.Length; line++)
            {
                annotation.Add(new SceneText(tx, boxTop + (line * metrics.Height) + metrics.Ascent, lines[line], font, color)
                {
                    Anchor = anchor,
                    MeasuredWidth = widths[line],
                });
            }

            group.Add(Decorate(annotation, data, i));
        }
    }

    private void AddArrow(SceneGroup group, double x1, double y1, double x2, double y2, ChartColor stroke)
    {
        // The head points at the point; the line stops a little before it
        var length = Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));
        var bend = Bend * Math.PI / 180;
        var path = new PathBuilder().MoveTo(x1, y1);
        double angle;
        if (bend == 0)
        {
            path.LineTo(x2, y2);
            angle = Math.Atan2(y2 - y1, x2 - x1);
        }
        else
        {
            // A quadratic curve whose tangents at the ends make the angle of the bend with the chord
            var (mx, my) = ((x1 + x2) / 2, (y1 + y2) / 2);
            var offset = length / 2 * Math.Tan(bend / 2);
            var (nx, ny) = (-(y2 - y1) / length, (x2 - x1) / length);
            var (cx, cy) = (mx - (nx * offset), my - (ny * offset));
            path.QuadraticTo(cx, cy, x2, y2);
            angle = Math.Atan2(y2 - cy, x2 - cx);
        }

        group.Add(new ScenePath(path.Build()) { Stroke = stroke, StrokeWidth = StrokeWidth ?? 1, LineCap = SceneLineCap.Round });
        if (HeadSize > 0)
        {
            var spread = Math.PI / 7;
            var head = new PathBuilder()
                .MoveTo(x2 - (HeadSize * Math.Cos(angle - spread)), y2 - (HeadSize * Math.Sin(angle - spread)))
                .LineTo(x2, y2)
                .LineTo(x2 - (HeadSize * Math.Cos(angle + spread)), y2 - (HeadSize * Math.Sin(angle + spread)))
                .Build();
            group.Add(new ScenePath(head) { Stroke = stroke, StrokeWidth = StrokeWidth ?? 1, LineCap = SceneLineCap.Round, LineJoin = SceneLineJoin.Round });
        }
    }

    /// <summary>The point where the segment from the center of a box to a point leaves the box, or <see langword="null"/> when the point is in the box.</summary>
    internal static (double X, double Y)? ClipToBox(double px, double py, double left, double top, double width, double height)
    {
        var cx = left + (width / 2);
        var cy = top + (height / 2);
        var dx = px - cx;
        var dy = py - cy;
        if (Math.Abs(dx) <= width / 2 && Math.Abs(dy) <= height / 2)
            return null;

        var scale = Math.Min(dx == 0 ? double.PositiveInfinity : width / 2 / Math.Abs(dx), dy == 0 ? double.PositiveInfinity : height / 2 / Math.Abs(dy));
        return (cx + (dx * scale), cy + (dy * scale));
    }
}
