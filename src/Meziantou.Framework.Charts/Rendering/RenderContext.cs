namespace Meziantou.Framework.Charts;

/// <summary>What the marks need to draw their rows: the resolved scales, the coordinates and the theme.</summary>
internal sealed class RenderContext
{
    public RenderContext(ChartTheme theme, ITextMeasurer measurer, ResolvedScale? x, ResolvedScale? y, ResolvedColorScale? color, ResolvedSizeScale? size, CoordinateSystem coordinates, SceneBounds frame)
    {
        Theme = theme;
        Measurer = measurer;
        X = x;
        Y = y;
        Color = color;
        Size = size;
        Coordinates = coordinates;
        Frame = frame;
    }

    public ChartTheme Theme { get; }

    public ITextMeasurer Measurer { get; }

    public ResolvedScale? X { get; }

    public ResolvedScale? Y { get; }

    public ResolvedColorScale? Color { get; }

    public ResolvedSizeScale? Size { get; }

    public CoordinateSystem Coordinates { get; }

    /// <summary>The plot area.</summary>
    public SceneBounds Frame { get; }

    public SceneFont LabelFont => new(Theme.TickFontSize);

    public SceneColor DefaultColor => Theme.GetPaletteColor(0);

    /// <summary>The color of a row: the channel mapped by the color scale, else the constant, else the first color of the theme.</summary>
    public SceneColor GetColor(ChannelValues? channel, int index, SceneColor? constant)
    {
        if (channel is not null && Color is not null)
            return Color.Map(channel, index);

        return constant ?? DefaultColor;
    }

    public double MapX(ChannelValues? values, int index) => values is null || X is null ? double.NaN : X.Map(values, index);

    public double MapY(ChannelValues? values, int index) => values is null || Y is null ? double.NaN : Y.Map(values, index);

    /// <summary>Maps a number with a continuous y scale, or NaN.</summary>
    public double MapYNumber(double value) => Y is ResolvedContinuousScale scale ? scale.MapNumber(value) : double.NaN;

    /// <summary>The full extent of the x scale, from the start of its range to its end.</summary>
    public (double Start, double End) XExtent => X is null ? GetFrameExtent(isX: true) : (X.RangeStart, X.RangeEnd);

    /// <summary>The full extent of the y scale.</summary>
    public (double Start, double End) YExtent => Y is null ? GetFrameExtent(isX: false) : (Y.RangeStart, Y.RangeEnd);

    /// <summary>A straight segment between two positions, or an arc when the coordinates are polar and the radius does not change.</summary>
    public SceneShape Segment(double x1, double y1, double x2, double y2)
    {
        if (Coordinates is PolarSystem polar)
        {
            var (a1, r1, a2, r2) = polar.Transposed ? (y1, x1, y2, x2) : (x1, y1, x2, y2);
            if (r1 == r2 && a1 != a2)
                return new ScenePath(PolarGeometry.Arc(polar.CenterX, polar.CenterY, r1, Math.Min(a1, a2), Math.Max(a1, a2)));
        }

        var (px1, py1) = Coordinates.Project(x1, y1);
        var (px2, py2) = Coordinates.Project(x2, y2);
        return new SceneLine(px1, py1, px2, py2);
    }

    /// <summary>Adds a centered label, in a color that contrasts with the shape under it, when it fits a width and a height.</summary>
    public void AddCenteredLabel(SceneGroup group, string? text, double x, double y, SceneColor background, double maxWidth, double maxHeight)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var metrics = Measurer.Measure(text, LabelFont);
        if (metrics.Width > maxWidth || metrics.Height > maxHeight)
            return;

        group.Add(new SceneText(x, y + ((metrics.Ascent - metrics.Descent) / 2), text, LabelFont, GetContrastingColor(background))
        {
            Anchor = SceneTextAnchor.Middle,
            MeasuredWidth = metrics.Width,
        });
    }

    /// <summary>Adds a horizontal label centered in an annular sector, when it fits the thickness of the ring and the length of the arc at the angle of the label.</summary>
    public void AddSectorLabel(SceneGroup group, string? text, double x, double y, SceneColor background, double angle, double thickness, double arcLength)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var metrics = Measurer.Measure(text, LabelFont);
        var sin = Math.Abs(Math.Sin(angle));
        var cos = Math.Abs(Math.Cos(angle));

        // The extent of the box of the text along the radius and along the arc
        var radial = (metrics.Width * sin) + (metrics.Height * cos);
        var tangential = (metrics.Width * cos) + (metrics.Height * sin);
        if (radial > thickness - 2 || tangential > arcLength - 2)
            return;

        AddCenteredLabel(group, text, x, y, background, double.PositiveInfinity, double.PositiveInfinity);
    }

    /// <summary>The color of a label drawn over a shape of a color: dark on light colors, white on dark colors.</summary>
    public static SceneColor GetContrastingColor(SceneColor background)
    {
        var (lightness, _, _) = OkLab.FromColor(background);
        return lightness > 0.65 ? (SceneColor)"#1f2328" : SceneColor.White;
    }

    private (double Start, double End) GetFrameExtent(bool isX)
    {
        if (Coordinates is PolarSystem polar)
        {
            var isAngle = isX != polar.Transposed;
            return isAngle ? (polar.StartAngle, polar.EndAngle) : (polar.InnerRadius, polar.OuterRadius);
        }

        var horizontal = isX != Coordinates.Transposed;
        return horizontal ? (Frame.X, Frame.Right) : (Frame.Bottom, Frame.Y);
    }
}
