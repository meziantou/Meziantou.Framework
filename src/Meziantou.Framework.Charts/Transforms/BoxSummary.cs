namespace Meziantou.Framework.Charts;

/// <summary>The five-number summary of a sample, with its outliers, as a box plot draws it.</summary>
/// <param name="Count">The number of values.</param>
/// <param name="LowerQuartile">The first quartile (25%).</param>
/// <param name="Median">The median (50%).</param>
/// <param name="UpperQuartile">The third quartile (75%).</param>
/// <param name="LowerWhisker">The smallest value that is not below <see cref="LowerQuartile"/> minus 1.5 times the interquartile range.</param>
/// <param name="UpperWhisker">The largest value that is not above <see cref="UpperQuartile"/> plus 1.5 times the interquartile range.</param>
/// <param name="Outliers">The values outside of the whiskers, in increasing order.</param>
public sealed record BoxSummary(int Count, double LowerQuartile, double Median, double UpperQuartile, double LowerWhisker, double UpperWhisker, IReadOnlyList<double> Outliers);
