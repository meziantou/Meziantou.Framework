namespace Meziantou.Framework.Charts;

/// <summary>The equirectangular (plate carrée) projection: the longitude is x and the latitude is y.</summary>
public sealed class EquirectangularProjection : GeoProjection
{
    private protected override (double X, double Y) ProjectRadians(double lambda, double phi) => (lambda, phi);
}
