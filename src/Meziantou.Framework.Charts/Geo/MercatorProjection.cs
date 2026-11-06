namespace Meziantou.Framework.Charts;

/// <summary>
/// The Mercator projection: conformal (it keeps the angles and the shapes of small areas), it enlarges the areas far from
/// the equator. The poles are at infinity: the latitudes are limited to ±85.05°, where the world map is a square.
/// </summary>
public sealed class MercatorProjection : GeoProjection
{
    // atan(sinh(π)): the latitude at which y equals π, as x does at 180°
    private const double LatitudeLimit = 85.05112877980659;

    internal override double MaxLatitude => LatitudeLimit;

    private protected override (double X, double Y) ProjectRadians(double lambda, double phi) => (lambda, Math.Log(Math.Tan((Math.PI / 4) + (phi / 2))));
}
