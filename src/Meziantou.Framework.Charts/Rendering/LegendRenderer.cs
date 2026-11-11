namespace Meziantou.Framework.Charts;

/// <summary>Measures and draws the legend of a color scale: swatches for categories, a ramp for numbers.</summary>
internal sealed class LegendRenderer : ILegendBlock
{
    private const double SwatchSize = 10;
    private const double SwatchGap = 5;
    private const double ItemGap = 14;
    private const double RampHeight = 10;
    private const double MaxLabelWidth = 160;

    private readonly ResolvedColorScale _scale;
    private readonly string? _title;
    private readonly ChartTheme _theme;
    private readonly ITextMeasurer _measurer;
    private readonly List<(double X, double Y, string Text, SceneColor Color)> _items = [];
    private double _rampWidth;
    private double _titleHeight;

    public LegendRenderer(ResolvedColorScale scale, string? title, ChartTheme theme, ITextMeasurer measurer)
    {
        _scale = scale;
        _title = title;
        _theme = theme;
        _measurer = measurer;
    }

    public double Width { get; private set; }

    public double Height { get; private set; }

    private SceneFont Font => new(_theme.FontSize);

    private SceneFont TickFont => new(_theme.TickFontSize);

    public static bool IsVisibleByDefault(ResolvedColorScale scale) => scale is not ResolvedOrdinalColorScale ordinal || ordinal.Domain.Count > 1;

    /// <summary>Lays the legend out in a width: in rows that wrap, or one item per row when <paramref name="vertical"/>.</summary>
    public void Measure(double maxWidth, bool vertical)
    {
        _items.Clear();
        var titleMetrics = _title is null ? default : _measurer.Measure(_title, Font with { Weight = SceneFont.BoldWeight });
        _titleHeight = _title is null ? 0 : titleMetrics.Height + 4;
        var width = titleMetrics.Width;
        var lineHeight = Math.Max(SwatchSize, _measurer.Measure("Xg", Font).Height) + 4;

        if (_scale is ResolvedContinuousColorScale)
        {
            _rampWidth = Math.Max(40, Math.Min(240, maxWidth));
            Width = Math.Max(width, _rampWidth);
            Height = _titleHeight + RampHeight + 3 + _measurer.Measure("0", TickFont).Height;
            return;
        }

        var ordinal = (ResolvedOrdinalColorScale)_scale;
        var x = 0.0;
        var y = _titleHeight;
        foreach (var category in ordinal.Domain)
        {
            var text = TextFitting.Truncate(_measurer, category, Font, MaxLabelWidth) ?? TextFitting.Ellipsis;
            var itemWidth = SwatchSize + SwatchGap + _measurer.Measure(text, Font).Width;
            if (x > 0 && (vertical || x + itemWidth > maxWidth))
            {
                x = 0;
                y += lineHeight;
            }

            _items.Add((x, y, text, ordinal.MapCategory(category)));
            width = Math.Max(width, x + itemWidth);
            x += itemWidth + ItemGap;
        }

        Width = width;
        Height = _items.Count is 0 ? _titleHeight : y + lineHeight - 4;
    }

    public void Render(SceneGroup target, double left, double top)
    {
        var group = target.Add(new SceneGroup { CssClass = "legend" });
        if (_title is not null)
        {
            var metrics = _measurer.Measure(_title, Font with { Weight = SceneFont.BoldWeight });
            group.Add(new SceneText(left, top + metrics.Ascent, _title, Font with { Weight = SceneFont.BoldWeight }, _theme.Foreground) { MeasuredWidth = metrics.Width });
        }

        if (_scale is ResolvedContinuousColorScale continuous)
        {
            RenderRamp(group, continuous, left, top + _titleHeight);
            return;
        }

        var textMetrics = _measurer.Measure("Xg", Font);
        var lineHeight = Math.Max(SwatchSize, textMetrics.Height);
        foreach (var (x, y, text, color) in _items)
        {
            var middle = top + y + (lineHeight / 2);
            group.Add(new SceneRectangle(left + x, middle - (SwatchSize / 2), SwatchSize, SwatchSize) { Fill = color, CornerRadius = 2 });
            group.Add(new SceneText(left + x + SwatchSize + SwatchGap, middle + ((textMetrics.Ascent - textMetrics.Descent) / 2), text, Font, _theme.Foreground) { MeasuredWidth = _measurer.Measure(text, Font).Width });
        }
    }

    private void RenderRamp(SceneGroup group, ResolvedContinuousColorScale scale, double left, double top)
    {
        const int Samples = 16;
        var stops = new List<SceneGradientStop>(Samples);
        for (var i = 0; i < Samples; i++)
        {
            var t = i / (double)(Samples - 1);
            stops.Add(new SceneGradientStop(t, scale.MapNumber(scale.Minimum + (t * (scale.Maximum - scale.Minimum)))));
        }

        group.Add(new SceneRectangle(left, top, _rampWidth, RampHeight)
        {
            Fill = new LinearGradientPaint(left, 0, left + _rampWidth, 0, stops),
        });

        if (!double.IsFinite(scale.Minimum) || !double.IsFinite(scale.Maximum))
            return;

        var ticks = scale.Minimum == scale.Maximum ? [scale.Minimum] : TickGenerator.Ticks(scale.Minimum, scale.Maximum, Math.Max(3, _rampWidth / 40));
        var step = ticks.Length > 1 ? ticks[1] - ticks[0] : 1;
        var metrics = _measurer.Measure("0", TickFont);
        var previousEnd = double.NegativeInfinity;
        foreach (var value in ticks)
        {
            var x = left + (scale.Maximum == scale.Minimum ? _rampWidth / 2 : (value - scale.Minimum) / (scale.Maximum - scale.Minimum) * _rampWidth);
            group.Add(new SceneLine(x, top + RampHeight, x, top + RampHeight + 3) { Stroke = _theme.AxisColor });
            var label = scale.Format(value, step);
            var width = _measurer.Measure(label, TickFont).Width;

            // The labels at the ends of the ramp stay inside it, and a label that would overlap the previous one is skipped
            var anchor = x - (width / 2) < left ? SceneTextAnchor.Start : x + (width / 2) > left + _rampWidth ? SceneTextAnchor.End : SceneTextAnchor.Middle;
            var labelX = anchor switch { SceneTextAnchor.Start => left, SceneTextAnchor.End => left + _rampWidth, _ => x };
            var start = anchor switch { SceneTextAnchor.Start => labelX, SceneTextAnchor.End => labelX - width, _ => labelX - (width / 2) };
            if (start < previousEnd + 4)
                continue;

            previousEnd = start + width;
            group.Add(new SceneText(labelX, top + RampHeight + 3 + metrics.Ascent, label, TickFont, _theme.MutedForeground)
            {
                Anchor = anchor,
                MeasuredWidth = width,
            });
        }
    }
}
