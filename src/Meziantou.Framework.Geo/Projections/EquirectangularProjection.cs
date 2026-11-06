namespace Meziantou.Framework.Geo;

/// <summary>The equirectangular (plate carrée) projection: the longitude is x and the latitude is y.</summary>
public sealed class EquirectangularProjection : GeoProjection
{
    /// <inheritdoc />
    protected override (double X, double Y) ProjectRadians(double lambda, double phi) => (lambda, phi);

    /// <inheritdoc />
    protected override (double Lambda, double Phi)? InvertRadians(double x, double y) => (x, y);
}
