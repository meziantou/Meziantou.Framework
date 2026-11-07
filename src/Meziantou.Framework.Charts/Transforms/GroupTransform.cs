namespace Meziantou.Framework.Charts;

/// <summary>
/// Combines the rows that share an x value and a series into one row, whose y value is reduced from theirs (sum, mean,
/// count...). The other channels come from the first row of the group.
/// </summary>
public sealed class GroupTransform : Transform
{
    /// <summary>Gets or sets how the y values are combined. The default is <see cref="GroupReducer.Sum"/>.</summary>
    public GroupReducer Reducer { get; set; }

    internal override MarkData Apply(MarkData data)
    {
        if (data.X is null)
            return data;

        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new List<List<int>>();
        var series = data.SeriesKey;
        for (var i = 0; i < data.Count; i++)
        {
            var key = data.X.GetCategory(i) + "\u001F" + series?.GetCategory(i);
            if (!keys.TryGetValue(key, out var group))
            {
                group = groups.Count;
                keys.Add(key, group);
                groups.Add([]);
            }

            groups[group].Add(i);
        }

        var result = data.Select([.. groups.Select(group => group[0])]);
        var reduced = new double[groups.Count];
        for (var g = 0; g < groups.Count; g++)
        {
            var values = data.Y is { IsContinuous: true } y ? groups[g].Select(row => y.Numbers[row]).Where(value => !double.IsNaN(value)).ToArray() : [];
            reduced[g] = Reduce(Reducer, groups[g].Count, values);
        }

        result.Y = ChannelValues.FromNumbers(reduced);
        return result;
    }

    internal static double Reduce(GroupReducer reducer, int count, double[] values)
    {
        if (reducer is GroupReducer.Count)
            return count;

        if (values.Length is 0)
            return reducer is GroupReducer.Sum ? 0 : double.NaN;

        return reducer switch
        {
            GroupReducer.Mean => values.Average(),
            GroupReducer.Min => values.Min(),
            GroupReducer.Max => values.Max(),
            GroupReducer.Median => BoxStatistics.Quantile([.. values.Order()], 0.5),
            _ => values.Sum(),
        };
    }
}
