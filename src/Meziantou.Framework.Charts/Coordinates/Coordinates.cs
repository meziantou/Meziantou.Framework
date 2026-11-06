namespace Meziantou.Framework.Charts;

/// <summary>How the positions of the x and y scales become points of the chart.</summary>
public abstract class Coordinates
{
    private protected Coordinates()
    {
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the roles of the scales are swapped. In Cartesian coordinates, the x
    /// scale is vertical and the y scale is horizontal (horizontal bars). In polar coordinates, the x scale is the radius
    /// and the y scale is the angle (radial bars).
    /// </summary>
    public bool Transposed { get; set; }
}
