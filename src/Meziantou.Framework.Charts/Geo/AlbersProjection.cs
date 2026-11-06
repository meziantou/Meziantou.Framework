namespace Meziantou.Framework.Charts;

/// <summary>
/// The Albers projection: an equal-area conic projection, for the maps of a country or a continent. The distortion is
/// smallest between its two standard parallels. The defaults are those of maps of the contiguous United States.
/// </summary>
/// <remarks>
/// The longitudes are measured from <see cref="CentralMeridian"/>: a region that spans the opposite meridian is cut in
/// two, so use it for regional maps, not for world maps.
/// </remarks>
public sealed class AlbersProjection : GeoProjection
{
    /// <summary>Gets or sets the first standard parallel, in degrees. The default is 29.5.</summary>
    public double Parallel1 { get; set; } = 29.5;

    /// <summary>Gets or sets the second standard parallel, in degrees. The default is 45.5.</summary>
    public double Parallel2 { get; set; } = 45.5;

    /// <summary>Gets or sets the longitude at the center of the map, in degrees. The default is -96.</summary>
    public double CentralMeridian { get; set; } = -96;

    internal override double CenterLongitude => CentralMeridian;

    internal override bool StraightParallels => false;

    private protected override (double X, double Y) ProjectRadians(double lambda, double phi)
    {
        // The longitude from the central meridian, from -π to π
        lambda -= CentralMeridian * Math.PI / 180;
        if (lambda > Math.PI)
        {
            lambda -= 2 * Math.PI;
        }
        else if (lambda < -Math.PI)
        {
            lambda += 2 * Math.PI;
        }

        var phi0 = Parallel1 * Math.PI / 180;
        var phi1 = Parallel2 * Math.PI / 180;
        var sin0 = Math.Sin(phi0);
        var n = (sin0 + Math.Sin(phi1)) / 2;
        if (Math.Abs(n) < 1e-6)
        {
            // Parallels symmetric around the equator: the cylindrical equal-area projection
            var cos0 = Math.Cos(phi0);
            return (lambda * cos0, Math.Sin(phi) / cos0);
        }

        var c = 1 + (sin0 * ((2 * n) - sin0));
        var r0 = Math.Sqrt(c) / n;
        var r = Math.Sqrt(Math.Max(0, c - (2 * n * Math.Sin(phi)))) / n;
        var angle = lambda * n;
        return (r * Math.Sin(angle), r0 - (r * Math.Cos(angle)));
    }
}
