namespace Meziantou.Framework.Charts;

/// <summary>
/// The options of the legend of the size scale: nested circles of a few values, next to the legend of the color scale
/// and at its <see cref="Legend.Position"/>.
/// </summary>
public sealed class SizeLegend
{
    /// <summary>Gets or sets a value that indicates whether the legend is drawn, or <see langword="null"/> for the default: drawn when a mark has a size channel.</summary>
    public bool? Visible { get; set; }

    /// <summary>Gets or sets the title of the legend, or <see langword="null"/> for none.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the values of the circles, or <see langword="null"/> for up to three round values of the domain of the scale.</summary>
    public IReadOnlyList<double>? Values { get; set; }
}
