namespace Meziantou.Framework.Charts;

/// <summary>
/// Polar coordinates: the x scale gives the angle and the y scale gives the radius (swapped when
/// <see cref="Coordinates.Transposed"/>). Angles are in degrees, clockwise, from 12 o'clock.
/// </summary>
public sealed class PolarCoordinates : Coordinates
{
    /// <summary>Gets or sets the angle of the start of the angular scale. The default is 0, 12 o'clock.</summary>
    public double StartAngle { get; set; }

    /// <summary>Gets or sets the angle of the end of the angular scale. The default is 360, a full circle. Use -90 and 90 for a half-circle gauge.</summary>
    public double EndAngle { get; set; } = 360;

    /// <summary>Gets or sets the radius where the radial scale starts, as a fraction of the outer radius, from 0 to 1. Use it for donuts. The default is 0.</summary>
    public double InnerRadius { get; set; }

    /// <summary>Gets or sets the shape of the grid lines of the radial scale. The default is <see cref="PolarGridShape.Auto"/>.</summary>
    public PolarGridShape GridShape { get; set; }
}
