namespace Meziantou.Framework.Charts;

/// <summary>
/// Replaces the y value of each row with a reduction (a mean, by default) of the y values of the rows around it, in the
/// order of the rows, for each series: a moving average. At the ends of a series, the window has fewer rows.
/// </summary>
public sealed class WindowTransform : Transform
{
    private int _size = 3;

    /// <summary>Gets or sets the number of rows of the window. The default is 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int Size
    {
        get => _size;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _size = value;
        }
    }

    /// <summary>Gets or sets how the y values of the window are combined. The default is <see cref="GroupReducer.Mean"/>.</summary>
    public GroupReducer Reducer { get; set; } = GroupReducer.Mean;

    /// <summary>Gets or sets where the window is, relative to its row. The default is <see cref="WindowAnchor.Middle"/>.</summary>
    public WindowAnchor Anchor { get; set; }

    internal override MarkData Apply(MarkData data)
    {
        if (data.Y is not { IsContinuous: true } y)
            return data;

        var reduced = new double[data.Count];
        var (before, after) = Anchor switch
        {
            WindowAnchor.End => (Size - 1, 0),
            WindowAnchor.Start => (0, Size - 1),
            _ => ((Size - 1) / 2, Size / 2),
        };

        foreach (var series in MarkData.GroupBy(data.Count, data.SeriesKey))
        {
            for (var i = 0; i < series.Count; i++)
            {
                var start = Math.Max(0, i - before);
                var end = Math.Min(series.Count - 1, i + after);
                var values = new List<double>(end - start + 1);
                for (var j = start; j <= end; j++)
                {
                    var value = y.Numbers[series[j]];
                    if (!double.IsNaN(value))
                    {
                        values.Add(value);
                    }
                }

                reduced[series[i]] = GroupTransform.Reduce(Reducer, end - start + 1, [.. values]);
            }
        }

        var result = data.Select([.. Enumerable.Range(0, data.Count)]);
        result.Y = ChannelValues.FromNumbers(reduced);
        return result;
    }
}
