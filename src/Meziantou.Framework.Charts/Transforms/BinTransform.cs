namespace Meziantou.Framework.Charts;

/// <summary>
/// Groups the x values of the rows into bins and counts them: a histogram when used with a <see cref="RectMark{T}"/>.
/// Each bin becomes a row with <c>X1</c> and <c>X2</c> its edges, <c>Y1</c> zero and <c>Y2</c> its count. The rows are
/// binned separately for each value of the <c>Fill</c> channel.
/// </summary>
/// <remarks>
/// The edges are round numbers, chosen with the tick algorithm for the number of bins, and the domain is extended to
/// round values. A bin includes its lower edge; the last bin also includes its upper edge.
/// </remarks>
public sealed class BinTransform : Transform
{
    /// <summary>Gets or sets the rule that chooses the number of bins. The default is <see cref="BinThresholds.Sturges"/>.</summary>
    public BinThresholds Thresholds { get; set; }

    /// <summary>Gets or sets the approximate number of bins, or <see langword="null"/> to use <see cref="Thresholds"/>.</summary>
    public int? Count { get; set; }

    /// <summary>Gets or sets the edges of the bins, in increasing order, or <see langword="null"/> to compute them. Values outside of them are not counted.</summary>
    public IReadOnlyList<double>? Edges { get; set; }

    internal override MarkData Apply(MarkData data)
    {
        var x = data.X ?? data.X1;
        if (x is not { IsContinuous: true })
            return data;

        var edges = Edges is { Count: >= 2 } explicitEdges ? [.. explicitEdges] : ComputeEdges(x.Numbers);
        var groups = MarkData.GroupBy(data.Count, data.Fill);
        var binCount = Math.Max(0, edges.Length - 1);
        var x1 = new List<double>();
        var x2 = new List<double>();
        var counts = new List<double>();
        var fills = new List<string?>();
        var titles = new List<string?>();
        foreach (var group in groups)
        {
            var histogram = new double[binCount];
            foreach (var row in group)
            {
                var index = FindBin(edges, x.Numbers[row]);
                if (index >= 0)
                {
                    histogram[index]++;
                }
            }

            for (var i = 0; i < binCount; i++)
            {
                x1.Add(edges[i]);
                x2.Add(edges[i + 1]);
                counts.Add(histogram[i]);
                fills.Add(data.Fill?.GetCategory(group[0]));
                titles.Add("[" + NumberFormats.Default(edges[i]) + ", " + NumberFormats.Default(edges[i + 1]) + (i == binCount - 1 ? "]" : ")") + ": " + NumberFormats.Default(histogram[i]));
            }
        }

        var result = new MarkData(counts.Count)
        {
            X1 = ChannelValues.FromNumbers([.. x1], x.Kind),
            X2 = ChannelValues.FromNumbers([.. x2], x.Kind),
            Y1 = ChannelValues.FromNumbers(new double[counts.Count]),
            Y2 = ChannelValues.FromNumbers([.. counts]),
            Fill = data.Fill is null ? null : ChannelValues.FromCategories([.. fills]),
            Titles = [.. titles],
        };
        return result;
    }

    /// <summary>Returns the index of the bin of a value, or -1 when it is outside of the edges.</summary>
    internal static int FindBin(double[] edges, double value)
    {
        if (double.IsNaN(value) || edges.Length < 2 || value < edges[0] || value > edges[^1])
            return -1;

        var index = Array.BinarySearch(edges, value);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return Math.Min(index, edges.Length - 2);
    }

    internal double[] ComputeEdges(double[] values)
    {
        var sorted = values.Where(value => !double.IsNaN(value)).Order().ToArray();
        if (sorted.Length is 0)
            return [0, 1];

        var min = sorted[0];
        var max = sorted[^1];
        if (min == max)
            return [min - 0.5, min + 0.5];

        var count = Count ?? Thresholds switch
        {
            BinThresholds.Scott => GetCount(min, max, 3.49 * StandardDeviation(sorted) * Math.Pow(sorted.Length, -1.0 / 3)),
            BinThresholds.FreedmanDiaconis => GetCount(min, max, 2 * (BoxStatistics.Quantile(sorted, 0.75) - BoxStatistics.Quantile(sorted, 0.25)) * Math.Pow(sorted.Length, -1.0 / 3)),
            _ => (int)Math.Ceiling(Math.Log2(sorted.Length)) + 1,
        };

        count = Math.Max(1, count);
        var (start, stop) = TickGenerator.Nice(min, max, count);
        var edges = TickGenerator.Ticks(start, stop, count);
        return edges.Length >= 2 ? edges : [start, stop];

        static int GetCount(double min, double max, double width) => width > 0 ? (int)Math.Ceiling((max - min) / width) : 1;
    }

    private static double StandardDeviation(double[] values)
    {
        if (values.Length < 2)
            return 0;

        var mean = values.Average();
        var sum = values.Sum(value => (value - mean) * (value - mean));
        return Math.Sqrt(sum / (values.Length - 1));
    }
}
