namespace Meziantou.Framework.Charts;

/// <summary>
/// Replaces the y values with running totals, in the order of the rows, for each series: each row goes from the total
/// before it (<c>Y1</c>) to the total after it (<c>Y2</c>). It is the transform of waterfall charts.
/// </summary>
public sealed class CumulativeTransform : Transform
{
    internal override MarkData Apply(MarkData data)
    {
        if (data.Y is not { IsContinuous: true } y)
            return data;

        var (y1, y2) = Accumulate(y.Numbers, MarkData.GroupBy(data.Count, data.Series), isTotal: null);
        var result = data.Select([.. Enumerable.Range(0, data.Count)]);
        result.Y = null;
        result.Y1 = ChannelValues.FromNumbers(y1);
        result.Y2 = ChannelValues.FromNumbers(y2);
        return result;
    }

    /// <summary>Computes the running totals. A total row goes from zero to the running total, and does not change it.</summary>
    internal static (double[] Y1, double[] Y2) Accumulate(double[] values, List<List<int>> series, bool[]? isTotal)
    {
        var y1 = new double[values.Length];
        var y2 = new double[values.Length];
        foreach (var rows in series)
        {
            var running = 0.0;
            foreach (var row in rows)
            {
                if (isTotal is not null && isTotal[row])
                {
                    y1[row] = 0;
                    y2[row] = running;
                    continue;
                }

                var value = double.IsNaN(values[row]) ? 0 : values[row];
                y1[row] = running;
                running += value;
                y2[row] = running;
            }
        }

        return (y1, y2);
    }
}
