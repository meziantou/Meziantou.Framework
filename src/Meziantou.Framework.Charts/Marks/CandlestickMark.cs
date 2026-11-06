namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a candlestick per row (OHLC prices): a line from the low to the high price, and a box from the open to the
/// close price, in the positive color of the theme when the price rises and in its negative color when it falls.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class CandlestickMark<T> : Mark<T>
{
    private const string OpenColumn = "open";
    private const string CloseColumn = "close";

    /// <summary>Initializes a new instance of the <see cref="CandlestickMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="x">The channel of the time or the category of the candle.</param>
    /// <param name="open">The channel of the open price.</param>
    /// <param name="high">The channel of the high price.</param>
    /// <param name="low">The channel of the low price.</param>
    /// <param name="close">The channel of the close price.</param>
    public CandlestickMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> open, Func<T, object?> high, Func<T, object?> low, Func<T, object?> close)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(high);
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(close);
        X = x;
        Open = open;
        High = high;
        Low = low;
        Close = close;
    }

    /// <summary>Gets the channel of the time or the category of the candle.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the open price.</summary>
    public Func<T, object?> Open { get; }

    /// <summary>Gets the channel of the high price.</summary>
    public Func<T, object?> High { get; }

    /// <summary>Gets the channel of the low price.</summary>
    public Func<T, object?> Low { get; }

    /// <summary>Gets the channel of the close price.</summary>
    public Func<T, object?> Close { get; }

    /// <summary>Gets or sets the width of a candle, as a fraction of the space between two candles. The default is 0.6.</summary>
    public double CandleWidth { get; set; } = 0.6;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y1 = Evaluate(rows, Low);
        data.Y2 = Evaluate(rows, High);
        data.Extra[OpenColumn] = Evaluate(rows, Open)!;
        data.Extra[CloseColumn] = Evaluate(rows, Close)!;

        // On a continuous axis, half a step of room before the first candle and after the last one
        if (data.X is { IsContinuous: true } x)
        {
            var values = x.Numbers.Where(value => !double.IsNaN(value)).Order().ToArray();
            var spacing = double.PositiveInfinity;
            for (var i = 1; i < values.Length; i++)
            {
                if (values[i] > values[i - 1])
                {
                    spacing = Math.Min(spacing, values[i] - values[i - 1]);
                }
            }

            if (double.IsFinite(spacing))
            {
                data.ExtraX.Add(ChannelValues.FromNumbers([values[0] - (spacing / 2), values[^1] + (spacing / 2)], x.Kind));
            }
        }
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (context.X is null || context.Y is null || data.X is null)
            return;

        var open = data.Extra[OpenColumn];
        var close = data.Extra[CloseColumn];
        var spacing = BarMark<T>.GetBandwidth(context.X, data) / 0.6;
        var width = spacing * CandleWidth;
        for (var i = 0; i < data.Count; i++)
        {
            var x = PointSeries.Center(context.X, context.MapX(data.X, i));
            var low = context.MapY(data.Y1, i);
            var high = context.MapY(data.Y2, i);
            var openPosition = context.MapY(open, i);
            var closePosition = context.MapY(close, i);
            if (double.IsNaN(x) || double.IsNaN(openPosition) || double.IsNaN(closePosition))
                continue;

            var rising = close.GetNumber(i) >= open.GetNumber(i);
            var color = rising ? context.Theme.PositiveColor : context.Theme.NegativeColor;
            var candle = new SceneGroup();
            if (!double.IsNaN(low) && !double.IsNaN(high))
            {
                var wick = context.Segment(x, low, x, high);
                wick.Stroke = color;
                wick.StrokeWidth = StrokeWidth ?? 1;
                candle.Add(wick);
            }

            var body = context.Coordinates.Rectangle(x - (width / 2), x + (width / 2), openPosition, closePosition);
            body.Fill = color;

            // A candle that opens and closes at the same price is a line
            body.Stroke = color;
            candle.Add(body);
            group.Add(Decorate(candle, data, i));
        }
    }
}
