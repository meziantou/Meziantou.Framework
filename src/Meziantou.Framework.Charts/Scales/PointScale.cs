namespace Meziantou.Framework.Charts;

/// <summary>A scale of categories that places them at evenly spaced points, such as the axes of a radar chart.</summary>
public sealed class PointScale : Scale
{
    /// <summary>Gets or sets the categories, in order, or <see langword="null"/> to use the values of the data in the order they first appear.</summary>
    public IReadOnlyList<string>? Domain { get; set; }

    /// <summary>Gets or sets the space before the first point and after the last point, as a fraction of the step. The default is 0.5.</summary>
    public double Padding { get; set; } = 0.5;

    /// <summary>Gets or sets how the outer space is distributed, from 0 (all of it at the end) to 1 (all of it at the start). The default is 0.5.</summary>
    public double Align { get; set; } = 0.5;
}
