namespace Meziantou.Framework.Charts;

/// <summary>The tick labels of a horizontal axis: rotated by -45 degrees when they overlap, and thinned when they still do.</summary>
internal sealed class HorizontalAxisLayout
{
    private const double LabelGap = 6;
    private const double Sin45 = 0.7071067811865476;

    private HorizontalAxisLayout(List<AxisLabel> labels, double rotation, int stride, double labelBlockHeight)
    {
        Labels = labels;
        Rotation = rotation;
        Stride = stride;
        LabelBlockHeight = labelBlockHeight;
    }

    public List<AxisLabel> Labels { get; }

    public double Rotation { get; }

    /// <summary>One label out of <see cref="Stride"/> is drawn.</summary>
    public int Stride { get; }

    /// <summary>The height the labels take under the ticks.</summary>
    public double LabelBlockHeight { get; }

    public static HorizontalAxisLayout Create(IReadOnlyList<Tick> ticks, ITextMeasurer measurer, ChartFont font, double? rotation, double maxRotatedWidth)
    {
        var labels = ticks.Select(tick => new AxisLabel(tick.Position, tick.Label, measurer.Measure(tick.Label, font))).ToList();
        if (labels.Count is 0)
            return new HorizontalAxisLayout(labels, 0, 1, 0);

        var textHeight = labels.Max(label => label.Metrics.Height);
        var spacing = GetMinimumSpacing(labels);
        var maxWidth = labels.Max(label => label.Metrics.Width);
        var angle = rotation ?? (maxWidth + LabelGap > spacing ? -45 : 0);
        if (angle == 0)
        {
            // Thin the labels until they do not overlap
            var stride = 1;
            while (stride < labels.Count && (spacing * stride) < maxWidth + LabelGap)
            {
                stride++;
            }

            return new HorizontalAxisLayout(labels, 0, rotation is null ? 1 : stride, textHeight);
        }

        // Rotated labels are shortened, so a very long label does not take the whole chart
        for (var i = 0; i < labels.Count; i++)
        {
            if (labels[i].Metrics.Width > maxRotatedWidth)
            {
                var text = TextFitting.Truncate(measurer, labels[i].Text, font, maxRotatedWidth) ?? TextFitting.Ellipsis;
                labels[i] = labels[i] with { Text = text, Metrics = measurer.Measure(text, font) };
            }
        }

        // Two parallel labels rotated by 45 degrees are spacing * sin(45) apart
        var rotatedStride = 1;
        var radians = Math.Abs(angle) * Math.PI / 180;
        var perpendicular = Math.Max(Math.Sin(radians), 0.1);
        while (rotatedStride < labels.Count && spacing * rotatedStride * perpendicular < textHeight + 2)
        {
            rotatedStride++;
        }

        var rotatedWidth = labels.Max(label => label.Metrics.Width);
        var height = (rotatedWidth * Math.Sin(radians)) + (textHeight * Math.Cos(radians));
        return new HorizontalAxisLayout(labels, angle, rotatedStride, Math.Max(height, textHeight * Sin45));
    }

    private static double GetMinimumSpacing(List<AxisLabel> labels)
    {
        if (labels.Count < 2)
            return double.PositiveInfinity;

        var spacing = double.PositiveInfinity;
        for (var i = 1; i < labels.Count; i++)
        {
            spacing = Math.Min(spacing, Math.Abs(labels[i].Position - labels[i - 1].Position));
        }

        return spacing;
    }
}
