namespace Meziantou.Framework.Charts;

/// <summary>
/// Stacks the y values of the rows that share an x value, one series above the other. The series of a row is given by
/// the <c>Series</c> channel of the mark, else its <c>Fill</c> or <c>Stroke</c> channel. Bars become stacked bars and
/// areas become stacked areas.
/// </summary>
public sealed class StackTransform : Transform
{
    /// <summary>Gets or sets where the stacks start. The default is <see cref="StackOffset.Diverging"/>.</summary>
    public StackOffset Offset { get; set; } = StackOffset.Diverging;

    /// <summary>Gets or sets the order of the series. The default is <see cref="StackOrder.Given"/>.</summary>
    public StackOrder Order { get; set; }

    internal override MarkData Apply(MarkData data)
    {
        if (data.Y is not { IsContinuous: true } y || data.X is null)
            return data;

        var x = data.X;
        var seriesKey = data.SeriesKey;
        var series = MarkData.GroupBy(data.Count, seriesKey);
        var values = y.Numbers;
        var seriesOrder = GetSeriesOrder(series, values, x);
        var rank = new int[data.Count];
        for (var s = 0; s < seriesOrder.Count; s++)
        {
            foreach (var row in series[seriesOrder[s]])
            {
                rank[row] = s;
            }
        }

        var y1 = new double[data.Count];
        var y2 = new double[data.Count];
        foreach (var stack in GroupByX(x, data.Count))
        {
            stack.Sort((a, b) => rank[a].CompareTo(rank[b]) is var result and not 0 ? result : a.CompareTo(b));
            double positive = 0;
            double negative = 0;
            double total = 0;
            foreach (var row in stack)
            {
                var value = values[row];
                if (double.IsNaN(value))
                {
                    y1[row] = double.NaN;
                    y2[row] = double.NaN;
                    continue;
                }

                if (Offset is StackOffset.Diverging && value < 0)
                {
                    y1[row] = negative;
                    negative += value;
                    y2[row] = negative;
                }
                else
                {
                    y1[row] = positive;
                    positive += value;
                    y2[row] = positive;
                }

                total += Math.Abs(value);
            }

            if (Offset is StackOffset.Expand && total > 0)
            {
                foreach (var row in stack)
                {
                    y1[row] /= total;
                    y2[row] /= total;
                }
            }
            else if (Offset is StackOffset.Silhouette)
            {
                var shift = positive / 2;
                foreach (var row in stack)
                {
                    y1[row] -= shift;
                    y2[row] -= shift;
                }
            }
        }

        var result = data.Select([.. Enumerable.Range(0, data.Count)]);
        result.Y = null;
        result.Y1 = ChannelValues.FromNumbers(y1);
        result.Y2 = ChannelValues.FromNumbers(y2);
        return result;
    }

    internal static List<List<int>> GroupByX(ChannelValues x, int count)
    {
        var groups = new List<List<int>>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            // Continuous values are compared exactly, by their bits
            var key = x.IsContinuous ? BitConverter.DoubleToInt64Bits(x.Numbers[i]).ToString(System.Globalization.CultureInfo.InvariantCulture) : x.Categories[i] ?? string.Empty;
            if (!indexes.TryGetValue(key, out var group))
            {
                group = groups.Count;
                indexes.Add(key, group);
                groups.Add([]);
            }

            groups[group].Add(i);
        }

        return groups;
    }

    private List<int> GetSeriesOrder(List<List<int>> series, double[] values, ChannelValues x)
    {
        var order = Enumerable.Range(0, series.Count).ToList();
        var sums = series.Select(rows => rows.Sum(row => double.IsNaN(values[row]) ? 0 : values[row])).ToArray();
        switch (Order)
        {
            case StackOrder.Ascending:
                return [.. order.OrderBy(index => sums[index])];
            case StackOrder.Descending:
                return [.. order.OrderByDescending(index => sums[index])];
            case StackOrder.InsideOut:
                {
                    // The position of the peak of each series, in the order of the x values
                    var xOrder = GroupByX(x, values.Length).Select((rows, index) => (rows, index)).SelectMany(item => item.rows.Select(row => (row, item.index))).ToDictionary(item => item.row, item => item.index);
                    var peaks = series.Select(rows => rows.Count is 0 ? 0 : xOrder[rows.MaxBy(row => double.IsNaN(values[row]) ? double.NegativeInfinity : values[row])]).ToArray();
                    var byPeak = order.OrderBy(index => peaks[index]).ToList();
                    var top = new List<int>();
                    var bottom = new List<int>();
                    double topSum = 0;
                    double bottomSum = 0;
                    foreach (var index in byPeak)
                    {
                        if (topSum < bottomSum)
                        {
                            top.Add(index);
                            topSum += sums[index];
                        }
                        else
                        {
                            bottom.Add(index);
                            bottomSum += sums[index];
                        }
                    }

                    bottom.Reverse();
                    return [.. bottom, .. top];
                }

            default:
                return order;
        }
    }
}
