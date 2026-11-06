namespace Meziantou.Framework.Charts;

/// <summary>The rows of a mark once its channels are evaluated and its transform applied: one column per channel.</summary>
internal sealed class MarkData
{
    public MarkData(int count)
    {
        Count = count;
    }

    public int Count { get; }

    public ChannelValues? X { get; set; }

    public ChannelValues? X1 { get; set; }

    public ChannelValues? X2 { get; set; }

    public ChannelValues? Y { get; set; }

    public ChannelValues? Y1 { get; set; }

    public ChannelValues? Y2 { get; set; }

    public ChannelValues? Fill { get; set; }

    public ChannelValues? Stroke { get; set; }

    public ChannelValues? Size { get; set; }

    /// <summary>The grouping of the rows into series (lines, stacks), when it differs from the color.</summary>
    public ChannelValues? Series { get; set; }

    /// <summary>The subgroups of the bands of grouped bars.</summary>
    public ChannelValues? Group { get; set; }

    public string?[]? Text { get; set; }

    /// <summary>Columns specific to a mark, which no scale reads (the open and close prices of candlesticks...).</summary>
    public Dictionary<string, ChannelValues> Extra { get; } = new(StringComparer.Ordinal);

    /// <summary>State specific to a mark, computed when its rows are prepared.</summary>
    public object? State { get; set; }

    public string?[]? Titles { get; set; }

    /// <summary>Values the x scale must include, besides the rows (box plot outliers...).</summary>
    public List<ChannelValues> ExtraX { get; } = [];

    /// <summary>Values the y scale must include, besides the rows.</summary>
    public List<ChannelValues> ExtraY { get; } = [];

    /// <summary>Categories or numbers the color scale must include, besides the rows.</summary>
    public List<ChannelValues> ExtraColor { get; } = [];

    /// <summary>The key of the series of a row: <see cref="Series"/>, then the colors.</summary>
    public ChannelValues? SeriesKey => Series ?? Fill ?? Stroke;

    public MarkData Select(IReadOnlyList<int> indices)
    {
        var result = new MarkData(indices.Count)
        {
            X = X?.Select(indices),
            X1 = X1?.Select(indices),
            X2 = X2?.Select(indices),
            Y = Y?.Select(indices),
            Y1 = Y1?.Select(indices),
            Y2 = Y2?.Select(indices),
            Fill = Fill?.Select(indices),
            Stroke = Stroke?.Select(indices),
            Size = Size?.Select(indices),
            Series = Series?.Select(indices),
            Group = Group?.Select(indices),
            Text = Select(Text, indices),
            Titles = Select(Titles, indices),
        };
        foreach (var (name, values) in Extra)
        {
            result.Extra[name] = values.Select(indices);
        }

        result.ExtraX.AddRange(ExtraX);
        result.ExtraY.AddRange(ExtraY);
        result.ExtraColor.AddRange(ExtraColor);
        return result;
    }

    /// <summary>Returns the rows grouped by a key, each group in the order of its rows, the groups in the order their first row appears.</summary>
    public static List<List<int>> GroupBy(int count, ChannelValues? key)
    {
        var groups = new List<List<int>>();
        if (key is null)
        {
            groups.Add([.. Enumerable.Range(0, count)]);
            return groups;
        }

        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var name = key.GetCategory(i) ?? string.Empty;
            if (!indexes.TryGetValue(name, out var group))
            {
                group = groups.Count;
                indexes.Add(name, group);
                groups.Add([]);
            }

            groups[group].Add(i);
        }

        return groups;
    }

    private static string?[]? Select(string?[]? values, IReadOnlyList<int> indices)
    {
        if (values is null)
            return null;

        var result = new string?[indices.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = values[indices[i]];
        }

        return result;
    }
}
