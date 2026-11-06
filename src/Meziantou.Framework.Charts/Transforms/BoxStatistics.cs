namespace Meziantou.Framework.Charts;

/// <summary>The statistics of box plots: quantiles with linear interpolation (R's type 7, the default of R, NumPy and Excel) and Tukey's whiskers.</summary>
public static class BoxStatistics
{
    /// <summary>Computes the summary of a sample. NaN values are ignored.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The summary, or <see langword="null"/> when there is no value.</returns>
    public static BoxSummary? Summarize(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(value => !double.IsNaN(value)).Order().ToArray();
        if (sorted.Length is 0)
            return null;

        var q1 = Quantile(sorted, 0.25);
        var median = Quantile(sorted, 0.5);
        var q3 = Quantile(sorted, 0.75);
        var fence = 1.5 * (q3 - q1);
        var lower = sorted.First(value => value >= q1 - fence);
        var upper = sorted.Last(value => value <= q3 + fence);
        var outliers = sorted.Where(value => value < lower || value > upper).ToArray();
        return new BoxSummary(sorted.Length, q1, median, q3, lower, upper, outliers);
    }

    /// <summary>Computes a quantile of sorted values, by linear interpolation between the closest ranks (R's type 7).</summary>
    /// <param name="sortedValues">The values, in increasing order.</param>
    /// <param name="probability">The probability, from 0 to 1.</param>
    /// <returns>The quantile, or NaN when there is no value.</returns>
    public static double Quantile(IReadOnlyList<double> sortedValues, double probability)
    {
        ArgumentNullException.ThrowIfNull(sortedValues);
        if (sortedValues.Count is 0)
            return double.NaN;

        probability = Math.Clamp(probability, 0, 1);
        var h = (sortedValues.Count - 1) * probability;
        var low = (int)Math.Floor(h);
        if (low >= sortedValues.Count - 1)
            return sortedValues[^1];

        return sortedValues[low] + ((h - low) * (sortedValues[low + 1] - sortedValues[low]));
    }
}
