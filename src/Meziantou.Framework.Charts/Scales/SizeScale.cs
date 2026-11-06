namespace Meziantou.Framework.Charts;

/// <summary>Maps the values of the <c>Size</c> channel to radii, so the area of a dot is proportional to its value (a square-root scale from 0).</summary>
public sealed class SizeScale
{
    /// <summary>Gets or sets the radius of the largest value. The default is 12.</summary>
    public double MaxRadius { get; set; } = 12;

    /// <summary>Gets or sets the value drawn with <see cref="MaxRadius"/>, or <see langword="null"/> for the largest value of the data.</summary>
    public double? Maximum { get; set; }
}
