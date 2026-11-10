namespace Meziantou.Framework.Charts;

/// <summary>Measures and draws the legend of the size scale: nested circles of a few values, with their labels on the right.</summary>
internal sealed class SizeLegendRenderer : ILegendBlock
{
    private const double LabelGap = 4;
    private const double LeaderLength = 6;

    private readonly ResolvedSizeScale _scale;
    private readonly IReadOnlyList<double>? _values;
    private readonly string? _title;
    private readonly ChartTheme _theme;
    private readonly ITextMeasurer _measurer;
    private readonly List<(double Radius, double LabelY, string Label, double LabelWidth)> _items = [];
    private double _titleHeight;
    private double _maxRadius;
    private double _textHeight;

    public SizeLegendRenderer(ResolvedSizeScale scale, IReadOnlyList<double>? values, string? title, ChartTheme theme, ITextMeasurer measurer)
    {
        _scale = scale;
        _values = values;
        _title = title;
        _theme = theme;
        _measurer = measurer;
    }

    public double Width { get; private set; }

    public double Height { get; private set; }

    private SceneFont Font => new(_theme.FontSize);

    private SceneFont TickFont => new(_theme.TickFontSize);

    /// <summary>The values of the legend: the round values of the domain of the scale, at most three, the largest first.</summary>
    public static double[] GetValues(double maximum, IReadOnlyList<double>? values)
    {
        if (values is not null)
            return [.. values.Where(value => value > 0 && double.IsFinite(value)).Distinct().OrderDescending()];

        if (!(maximum > 0) || !double.IsFinite(maximum))
            return [];

        var ticks = TickGenerator.Ticks(0, maximum, 3).Where(value => value > 0).ToArray();
        return ticks.Length is 0 ? [maximum] : [.. ticks.Reverse().Take(3)];
    }

    public void Measure(double maxWidth, bool vertical)
    {
        _items.Clear();
        var titleMetrics = _title is null ? default : _measurer.Measure(_title, Font with { Bold = true });
        _titleHeight = _title is null ? 0 : titleMetrics.Height + 4;
        _textHeight = _measurer.Measure("0", TickFont).Height;
        var values = GetValues(_scale.Maximum, _values);
        var radii = values.Select(_scale.Map).ToArray();
        _maxRadius = radii.Length is 0 ? 0 : radii.Max();

        // The labels are next to the tops of their circles, pushed down when they would overlap the previous one
        var previous = double.NegativeInfinity;
        var bottom = (_textHeight / 2) + (2 * _maxRadius);
        var height = bottom;
        for (var i = 0; i < values.Length; i++)
        {
            if (!(radii[i] > 0))
                continue;

            var labelY = Math.Max(bottom - (2 * radii[i]), previous + _textHeight + 1);
            previous = labelY;
            var label = NumberFormats.Default(values[i]);
            _items.Add((radii[i], labelY, label, _measurer.Measure(label, TickFont).Width));
            height = Math.Max(height, labelY + (_textHeight / 2));
        }

        var labelsWidth = _items.Count is 0 ? 0 : _items.Max(item => item.LabelWidth);
        Width = Math.Max(titleMetrics.Width, _items.Count is 0 ? 0 : (2 * _maxRadius) + LeaderLength + LabelGap + labelsWidth);
        Height = _titleHeight + (_items.Count is 0 ? 0 : height);
    }

    public void Render(SceneGroup target, double left, double top)
    {
        var group = target.Add(new SceneGroup { CssClass = "legend-size" });
        if (_title is not null)
        {
            var titleMetrics = _measurer.Measure(_title, Font with { Bold = true });
            group.Add(new SceneText(left, top + titleMetrics.Ascent, _title, Font with { Bold = true }, _theme.Foreground) { MeasuredWidth = titleMetrics.Width });
        }

        // The circles stand on the same bottom point; a short line leads from the top of each one to its label
        var metrics = _measurer.Measure("0", TickFont);
        var origin = top + _titleHeight;
        var centerX = left + _maxRadius;
        var bottom = origin + (_textHeight / 2) + (2 * _maxRadius);
        var lineEnd = left + (2 * _maxRadius) + LeaderLength;
        foreach (var (radius, labelY, label, labelWidth) in _items)
        {
            var y = bottom - (2 * radius);
            group.Add(new SceneCircle(centerX, bottom - radius, radius) { Stroke = _theme.AxisColor });
            var path = new PathBuilder().MoveTo(centerX, y).LineTo(lineEnd - 2, y).LineTo(lineEnd, origin + labelY).Build();
            group.Add(new ScenePath(path) { Stroke = _theme.AxisColor, StrokeDashArray = [2, 2] });
            group.Add(new SceneText(lineEnd + LabelGap, origin + labelY + ((metrics.Ascent - metrics.Descent) / 2), label, TickFont, _theme.MutedForeground) { MeasuredWidth = labelWidth });
        }
    }
}
