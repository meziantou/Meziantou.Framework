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

    /// <summary>The facet of each row on the horizontal facet axis (the columns).</summary>
    public ChannelValues? FacetX { get; set; }

    /// <summary>The facet of each row on the vertical facet axis (the rows of the grid).</summary>
    public ChannelValues? FacetY { get; set; }

    /// <summary>A list of numbers per row, for the marks that summarize their data (the outliers of a box plot...).</summary>
    public IReadOnlyList<double>[]? Sets { get; set; }

    /// <summary>Columns specific to a mark, which no scale reads (the open and close prices of candlesticks...).</summary>
    public Dictionary<string, ChannelValues> Extra { get; } = new(StringComparer.Ordinal);

    /// <summary>State specific to a mark, computed when its rows are prepared.</summary>
    public object? State { get; set; }

    public string?[]? Titles { get; set; }

    /// <summary>The <c>data-*</c> attributes of each row, validated and formatted.</summary>
    public KeyValuePair<string, string>[]?[]? DataAttributes { get; set; }

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
            DataAttributes = Select(DataAttributes, indices),
            FacetX = FacetX?.Select(indices),
            FacetY = FacetY?.Select(indices),
            Sets = Sets is null ? null : [.. indices.Select(index => Sets[index])],
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

    /// <summary>The rows of a cell of the facet grid. A mark without facet channels is in every cell.</summary>
    public MarkData ForFacet(string? xKey, string? yKey)
    {
        if ((FacetX is null || xKey is null) && (FacetY is null || yKey is null))
            return this;

        var rows = new List<int>();
        for (var i = 0; i < Count; i++)
        {
            if ((FacetX is null || xKey is null || string.Equals(FacetX.GetCategory(i), xKey, StringComparison.Ordinal))
                && (FacetY is null || yKey is null || string.Equals(FacetY.GetCategory(i), yKey, StringComparison.Ordinal)))
            {
                rows.Add(i);
            }
        }

        return Select(rows);
    }

    /// <summary>Joins the rows of several parts of a mark, prepared separately (one per facet).</summary>
    public static MarkData Concat(IReadOnlyList<MarkData> parts)
    {
        var result = new MarkData(parts.Sum(part => part.Count))
        {
            X = ConcatChannel(parts, part => part.X),
            X1 = ConcatChannel(parts, part => part.X1),
            X2 = ConcatChannel(parts, part => part.X2),
            Y = ConcatChannel(parts, part => part.Y),
            Y1 = ConcatChannel(parts, part => part.Y1),
            Y2 = ConcatChannel(parts, part => part.Y2),
            Fill = ConcatChannel(parts, part => part.Fill),
            Stroke = ConcatChannel(parts, part => part.Stroke),
            Size = ConcatChannel(parts, part => part.Size),
            Series = ConcatChannel(parts, part => part.Series),
            Group = ConcatChannel(parts, part => part.Group),
            FacetX = ConcatChannel(parts, part => part.FacetX),
            FacetY = ConcatChannel(parts, part => part.FacetY),
            Text = ConcatArrays(parts, part => part.Text),
            Titles = ConcatArrays(parts, part => part.Titles),
            DataAttributes = ConcatArrays(parts, part => part.DataAttributes),
            Sets = parts.Any(part => part.Sets is not null) ? [.. parts.SelectMany(part => part.Sets ?? new IReadOnlyList<double>[part.Count])] : null,
        };

        foreach (var name in parts.SelectMany(part => part.Extra.Keys).Distinct(StringComparer.Ordinal))
        {
            result.Extra[name] = ConcatChannel(parts, part => part.Extra.GetValueOrDefault(name))!;
        }

        foreach (var part in parts)
        {
            result.ExtraX.AddRange(part.ExtraX);
            result.ExtraY.AddRange(part.ExtraY);
            result.ExtraColor.AddRange(part.ExtraColor);
        }

        return result;
    }

    private static ChannelValues? ConcatChannel(IReadOnlyList<MarkData> parts, Func<MarkData, ChannelValues?> selector)
    {
        var channels = parts.Select(selector).ToList();
        if (channels.TrueForAll(channel => channel is null))
            return null;

        // The parts come from the same channel, so they share a kind; a part without the channel has missing values
        var kinds = channels.Where(channel => channel is not null).Select(channel => channel!.Kind).Distinct().ToList();
        if (kinds.Count is 1 && kinds[0] is not ChannelKind.Category)
        {
            var numbers = new List<double>();
            for (var i = 0; i < parts.Count; i++)
            {
                numbers.AddRange(channels[i]?.Numbers ?? Enumerable.Repeat(double.NaN, parts[i].Count));
            }

            return ChannelValues.FromNumbers([.. numbers], kinds[0]);
        }

        var categories = new List<string?>();
        for (var i = 0; i < parts.Count; i++)
        {
            categories.AddRange(channels[i]?.ToCategorical().Categories ?? new string?[parts[i].Count]);
        }

        return ChannelValues.FromCategories([.. categories]);
    }

    private static TValue?[]? ConcatArrays<TValue>(IReadOnlyList<MarkData> parts, Func<MarkData, TValue?[]?> selector)
        where TValue : class
    {
        if (parts.All(part => selector(part) is null))
            return null;

        return [.. parts.SelectMany(part => selector(part) ?? new TValue?[part.Count])];
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

    private static TValue?[]? Select<TValue>(TValue?[]? values, IReadOnlyList<int> indices)
        where TValue : class
    {
        if (values is null)
            return null;

        var result = new TValue?[indices.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = values[indices[i]];
        }

        return result;
    }
}
