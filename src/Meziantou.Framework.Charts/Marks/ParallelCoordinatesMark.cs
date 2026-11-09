using System.Globalization;

namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a parallel coordinates plot: a vertical axis per dimension, evenly spaced across the plot area, each with its own
/// scale, and a line per row through its values on the axes. The <see cref="Mark{T}.Stroke"/> channel colors the lines.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
/// <remarks>A line breaks around a missing value.</remarks>
public sealed class ParallelCoordinatesMark<T> : Mark<T>
{
    private const string Prefix = "dimension";
    private const double TickSize = 4;

    /// <summary>Initializes a new instance of the <see cref="ParallelCoordinatesMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="dimensions">The axes, from left to right.</param>
    /// <exception cref="ArgumentException"><paramref name="dimensions"/> is empty.</exception>
    public ParallelCoordinatesMark(IEnumerable<T> data, IEnumerable<ParallelDimension<T>> dimensions)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        Dimensions = [.. dimensions];
        if (Dimensions.Count is 0)
            throw new ArgumentException("A parallel coordinates plot needs at least one dimension.", nameof(dimensions));
    }

    /// <summary>Gets the axes, from left to right.</summary>
    public IReadOnlyList<ParallelDimension<T>> Dimensions { get; }

    /// <summary>Gets or sets the opacity of the lines, from 0 to 1. The default is 0.7.</summary>
    public double LineOpacity { get; set; } = 0.7;

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.Frame | MarkTraits.HideXAxis | MarkTraits.HideYAxis;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        for (var i = 0; i < Dimensions.Count; i++)
        {
            data.Extra[Prefix + i.ToString(CultureInfo.InvariantCulture)] = Evaluate(rows, Dimensions[i].Value)!;
        }
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var font = context.LabelFont;
        var nameFont = new SceneFont(context.Theme.FontSize, Bold: true);
        var frame = context.Frame;
        var nameMetrics = Dimensions.Select(dimension => context.Measurer.Measure(dimension.Name, nameFont)).ToArray();
        var top = frame.Y + nameMetrics.Max(metrics => metrics.Height) + 8;
        var bottom = frame.Bottom - (context.Measurer.Measure("0", font).Height / 2);

        // One scale per axis, laid out on the height of the axes
        var channels = new ChannelValues[Dimensions.Count];
        var scales = new ResolvedScale[Dimensions.Count];
        for (var i = 0; i < Dimensions.Count; i++)
        {
            channels[i] = data.Extra[Prefix + i.ToString(CultureInfo.InvariantCulture)];
            scales[i] = ScaleResolver.ResolvePosition(Dimensions[i].Scale, [channels[i]], band: false, zero: false, exact: false)!;
            var (start, end) = scales[i].IsOrdinal ? (top, bottom) : (bottom, top);
            scales[i].Configure(start, end, Math.Max(2, (int)Math.Round((bottom - top) / 40, MidpointRounding.AwayFromZero)));
        }

        // The tick labels are on the left of the axes: the first axis keeps room for them, the last one for half of its name
        var ticks = scales.Select(scale => scale.GetTicks()).ToArray();
        var firstLabels = ticks[0].Count is 0 ? 0 : ticks[0].Max(tick => context.Measurer.Measure(tick.Label, font).Width) + TickSize + 3;
        var left = frame.X + Math.Max(firstLabels, nameMetrics[0].Width / 2);
        var right = frame.Right - (nameMetrics[^1].Width / 2);
        var positions = new double[Dimensions.Count];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = Dimensions.Count is 1 ? (left + right) / 2 : left + ((right - left) * i / (Dimensions.Count - 1));
        }

        var lines = group.Add(new SceneGroup { CssClass = "lines" });
        for (var row = 0; row < data.Count; row++)
        {
            var builder = new PathBuilder();
            var drawing = false;
            for (var i = 0; i < Dimensions.Count; i++)
            {
                var y = scales[i].Map(channels[i], row);
                if (double.IsNaN(y))
                {
                    drawing = false;
                    continue;
                }

                y += scales[i].Bandwidth / 2;
                if (drawing)
                {
                    builder.LineTo(positions[i], y);
                }
                else
                {
                    builder.MoveTo(positions[i], y);
                    drawing = true;
                }
            }

            if (builder.IsEmpty)
                continue;

            var color = GetStroke(context, data, row);
            lines.Add(Decorate(new ScenePath(builder.Build())
            {
                Stroke = color.WithOpacity(color.Opacity * LineOpacity),
                StrokeWidth = StrokeWidth ?? 1.5,
                LineJoin = SceneLineJoin.Round,
            }, data, row));
        }

        // The axes are drawn over the lines
        var axes = group.Add(new SceneGroup { CssClass = "axes" });
        var tickMetrics = context.Measurer.Measure("0", font);
        for (var i = 0; i < Dimensions.Count; i++)
        {
            var x = positions[i];
            axes.Add(new SceneLine(x, top, x, bottom) { Stroke = context.Theme.AxisColor });
            foreach (var tick in ticks[i])
            {
                axes.Add(new SceneLine(x - TickSize, tick.Position, x, tick.Position) { Stroke = context.Theme.AxisColor });
                axes.Add(new SceneText(x - TickSize - 3, tick.Position + ((tickMetrics.Ascent - tickMetrics.Descent) / 2), tick.Label, font, context.Theme.MutedForeground)
                {
                    Anchor = SceneTextAnchor.End,
                    MeasuredWidth = context.Measurer.Measure(tick.Label, font).Width,
                });
            }

            axes.Add(new SceneText(x, top - 8 - nameMetrics[i].Descent, Dimensions[i].Name, nameFont, context.Theme.Foreground)
            {
                Anchor = SceneTextAnchor.Middle,
                MeasuredWidth = nameMetrics[i].Width,
            });
        }
    }
}
