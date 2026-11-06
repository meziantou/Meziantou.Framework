namespace Meziantou.Framework.Charts;

/// <summary>A scale of categories that divides the axis into bands of equal width, such as the bars of a bar chart.</summary>
public sealed class BandScale : Scale
{
    /// <summary>Gets or sets the categories, in order, or <see langword="null"/> to use the values of the data in the order they first appear.</summary>
    public IReadOnlyList<string>? Domain { get; set; }

    /// <summary>Gets or sets the space between two bands, as a fraction of the step from a band to the next, from 0 to 1. The default is 0.1.</summary>
    public double PaddingInner { get; set; } = 0.1;

    /// <summary>Gets or sets the space before the first band and after the last band, as a fraction of the step. The default is 0.1.</summary>
    public double PaddingOuter { get; set; } = 0.1;

    /// <summary>Gets or sets how the outer space is distributed, from 0 (all of it at the end) to 1 (all of it at the start). The default is 0.5.</summary>
    public double Align { get; set; } = 0.5;
}
