namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a slice per row: a pie or a donut (with <see cref="PolarCoordinates.InnerRadius"/>), whose slices have angles
/// proportional to the values. With <see cref="Radius"/>, the slices have equal angles and radii from the values: a
/// polar area (rose) chart. The plot uses polar coordinates, unless it sets other coordinates.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
/// <remarks>
/// <para>Without a <see cref="Mark{T}.Fill"/> channel, each slice gets the next color of the palette.</para>
/// <para>On the x scale, the slices go from 0 to 1: a slice of a quarter of the total goes from 0 to 0.25, so every pie
/// is a full circle, even when facets share the scale. Draw a marker at 0.72 to point at 72% of a gauge.</para>
/// </remarks>
public sealed class ArcMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="ArcMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="value">The channel of the value of the slice. Negative values count as zero.</param>
    public ArcMark(IEnumerable<T> data, Func<T, object?> value)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Gets the channel of the value of the slice.</summary>
    public Func<T, object?> Value { get; }

    /// <summary>Gets or sets the channel of the radius of the slice (a rose chart), or <see langword="null"/> for slices of the full radius.</summary>
    public Func<T, object?>? Radius { get; set; }

    /// <summary>Gets or sets the channel of a label drawn in the slice when it fits.</summary>
    public Func<T, string?>? Label { get; set; }

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.PreferPolar | MarkTraits.ExactX | MarkTraits.ZeroY | MarkTraits.HideXAxis | (Radius is null ? MarkTraits.HideYAxis : MarkTraits.None);

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        var values = Evaluate(rows, Value)!;
        var x1 = new double[rows.Count];
        var x2 = new double[rows.Count];
        var y2 = new double[rows.Count];
        var radius = Evaluate(rows, Radius);
        var total = 0.0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (radius is null)
            {
                var value = values.GetNumber(i);
                value = double.IsNaN(value) ? 0 : Math.Max(0, value);
                x1[i] = total;
                total += value;
                x2[i] = total;
                y2[i] = 1;
            }
            else
            {
                x1[i] = i;
                x2[i] = i + 1;
                y2[i] = radius.GetNumber(i);
            }
        }

        // The angles are fractions of the whole, so every pie is a full circle, even when facets share the scale
        var whole = radius is null ? total : rows.Count;
        if (whole > 0)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                x1[i] /= whole;
                x2[i] /= whole;
            }
        }

        data.X1 = ChannelValues.FromNumbers(x1);
        data.X2 = ChannelValues.FromNumbers(x2);
        data.Y1 = ChannelValues.FromNumbers(new double[rows.Count]);
        data.Y2 = ChannelValues.FromNumbers(y2);
        data.Text = Evaluate(rows, Label);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (context.X is null || context.Y is null)
            return;

        for (var i = 0; i < data.Count; i++)
        {
            var x1 = context.MapX(data.X1, i);
            var x2 = context.MapX(data.X2, i);
            var y1 = context.MapY(data.Y1, i);
            var y2 = context.MapY(data.Y2, i);
            if (double.IsNaN(x1) || double.IsNaN(x2) || double.IsNaN(y2) || x1 == x2)
                continue;

            // The slices start at 0, which has no position on a log scale: they start at the baseline of the scale instead
            if (double.IsNaN(y1))
            {
                y1 = context.Y.Baseline;
                if (double.IsNaN(y1))
                    continue;
            }

            var shape = context.Coordinates.Rectangle(x1, x2, y1, y2);
            var fill = data.Fill is null && FillColor is null ? context.Theme.GetPaletteColor(i) : GetFill(context, data, i);
            shape.Fill = fill;
            shape.Stroke = data.Stroke is null && StrokeColor is null ? context.Theme.MarkStrokeColor : GetStroke(context, data, i);
            shape.StrokeWidth = StrokeWidth ?? 1;
            shape.LineJoin = SceneLineJoin.Round;
            group.Add(Decorate(shape, data, i));

            if (data.Text?[i] is { Length: > 0 } label)
            {
                AddLabel(context, group, label, fill, x1, x2, y1, y2);
            }
        }
    }

    private static void AddLabel(RenderContext context, SceneGroup group, string label, SceneColor fill, double x1, double x2, double y1, double y2)
    {
        var (px, py) = context.Coordinates.Project((x1 + x2) / 2, (y1 + y2) / 2);
        if (context.Coordinates is PolarSystem polar)
        {
            // The label must fit the arc at its middle radius and the thickness of the ring
            var (angle1, angle2, radius1, radius2) = polar.Transposed ? (y1, y2, x1, x2) : (x1, x2, y1, y2);
            var middleRadius = (radius1 + radius2) / 2;
            var innerRadius = Math.Min(radius1, radius2);
            if (innerRadius < 1)
            {
                // A pie slice: the label goes at 60% of the radius, where the slice is wider
                middleRadius = Math.Max(radius1, radius2) * 0.6;
                (px, py) = PolarGeometry.Point(polar.CenterX, polar.CenterY, middleRadius, (angle1 + angle2) / 2);
            }

            var arcLength = Math.Min(Math.Abs(angle2 - angle1) * middleRadius, 2 * middleRadius);
            context.AddSectorLabel(group, label, px, py, fill, (angle1 + angle2) / 2, Math.Abs(radius2 - radius1), arcLength);
            return;
        }

        var (ax, ay) = context.Coordinates.Project(x1, y1);
        var (bx, by) = context.Coordinates.Project(x2, y2);
        context.AddCenteredLabel(group, label, px, py, fill, Math.Abs(bx - ax), Math.Abs(by - ay));
    }
}
