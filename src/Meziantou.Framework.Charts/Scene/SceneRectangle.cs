namespace Meziantou.Framework.Charts;

/// <summary>An axis-aligned rectangle.</summary>
/// <param name="x">The left edge.</param>
/// <param name="y">The top edge.</param>
/// <param name="width">The width.</param>
/// <param name="height">The height.</param>
public sealed class SceneRectangle(double x, double y, double width, double height) : SceneShape
{
    /// <summary>Gets the left edge.</summary>
    public double X { get; } = x;

    /// <summary>Gets the top edge.</summary>
    public double Y { get; } = y;

    /// <summary>Gets the width.</summary>
    public double Width { get; } = width;

    /// <summary>Gets the height.</summary>
    public double Height { get; } = height;

    /// <summary>Gets or sets the radius of the rounded corners.</summary>
    public double CornerRadius { get; set; }
}
