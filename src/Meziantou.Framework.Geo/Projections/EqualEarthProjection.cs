namespace Meziantou.Framework.Geo;

/// <summary>
/// The Equal Earth projection (Šavrič, Patterson and Jenny, 2018): an equal-area pseudocylindrical projection for world
/// maps, so the colored areas of a choropleth are in proportion to the areas of the regions.
/// </summary>
public sealed class EqualEarthProjection : GeoProjection
{
    private const double A1 = 1.340264;
    private const double A2 = -0.081106;
    private const double A3 = 0.000893;
    private const double A4 = 0.003796;

    // √3 / 2
    private const double M = 0.8660254037844386;

    /// <inheritdoc />
    public override bool HasStraightMeridians => false;

    /// <inheritdoc />
    protected override (double X, double Y) ProjectRadians(double lambda, double phi)
    {
        var l = Math.Asin(M * Math.Sin(phi));
        var l2 = l * l;
        var l6 = l2 * l2 * l2;
        var x = lambda * Math.Cos(l) / (M * (A1 + (3 * A2 * l2) + (l6 * ((7 * A3) + (9 * A4 * l2)))));
        var y = l * (A1 + (A2 * l2) + (l6 * (A3 + (A4 * l2))));
        return (x, y);
    }

    /// <inheritdoc />
    protected override (double Lambda, double Phi)? InvertRadians(double x, double y)
    {
        // Newton's method on the parametric latitude
        var l = y;
        var l2 = l * l;
        var l6 = l2 * l2 * l2;
        for (var i = 0; i < 12; i++)
        {
            var fy = (l * (A1 + (A2 * l2) + (l6 * (A3 + (A4 * l2))))) - y;
            var fpy = A1 + (3 * A2 * l2) + (l6 * ((7 * A3) + (9 * A4 * l2)));
            var delta = fy / fpy;
            l -= delta;
            l2 = l * l;
            l6 = l2 * l2 * l2;
            if (Math.Abs(delta) < 1e-12)
                break;
        }

        var sinPhi = Math.Sin(l) / M;
        if (Math.Abs(sinPhi) > 1 + 1e-12)
            return null;

        var lambda = M * x * (A1 + (3 * A2 * l2) + (l6 * ((7 * A3) + (9 * A4 * l2)))) / Math.Cos(l);
        if (Math.Abs(lambda) > Math.PI + 1e-9)
            return null;

        return (lambda, Math.Asin(Math.Clamp(sinPhi, -1, 1)));
    }
}
