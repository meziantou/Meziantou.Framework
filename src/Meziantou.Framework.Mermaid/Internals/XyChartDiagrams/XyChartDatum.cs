namespace Meziantou.Framework.Mermaid.Internals.XyChartDiagrams;

/// <summary>A value of a series of an <see cref="XyChart"/>, as a row of a mark of the plot that draws it.</summary>
/// <param name="X">The position on the x axis: a category, or a number.</param>
/// <param name="Y">The value, or <see cref="double.NaN"/> when it is not drawn.</param>
/// <param name="Label">The text written next to the value, or <see langword="null"/>.</param>
internal sealed record XyChartDatum(object X, double Y, string? Label);
