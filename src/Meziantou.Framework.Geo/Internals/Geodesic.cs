namespace Meziantou.Framework.Geo.Internals;

/// <summary>The geodesic problems on the WGS 84 ellipsoid, solved with the formulas of Vincenty (1975).</summary>
internal static class Geodesic
{
    /// <summary>The semi-major axis of the WGS 84 ellipsoid, in meters.</summary>
    private const double A = 6_378_137;

    /// <summary>The flattening of the WGS 84 ellipsoid.</summary>
    private const double F = 1 / 298.257223563;

    /// <summary>The semi-minor axis of the WGS 84 ellipsoid, in meters.</summary>
    private const double B = A * (1 - F);

    private const double DegreesToRadians = Math.PI / 180;

    /// <summary>Gets the length of the shortest path between two positions on the ellipsoid, in meters.</summary>
    /// <remarks>
    /// The iteration does not converge for nearly antipodal positions: the distance is then the great-circle distance on
    /// a sphere of radius <see cref="GeoPosition.EarthRadius"/>, which is within 0.5% of the distance on the ellipsoid.
    /// </remarks>
    public static double GetDistance(GeoPosition from, GeoPosition to)
    {
        if (from.Longitude == to.Longitude && from.Latitude == to.Latitude)
            return 0;

        var l = NormalizeRadians((to.Longitude - from.Longitude) * DegreesToRadians);
        var u1 = Math.Atan((1 - F) * Math.Tan(from.Latitude * DegreesToRadians));
        var u2 = Math.Atan((1 - F) * Math.Tan(to.Latitude * DegreesToRadians));
        var (sinU1, cosU1) = Math.SinCos(u1);
        var (sinU2, cosU2) = Math.SinCos(u2);

        var lambda = l;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var (sinLambda, cosLambda) = Math.SinCos(lambda);
            var x = cosU2 * sinLambda;
            var y = (cosU1 * sinU2) - (sinU1 * cosU2 * cosLambda);
            var sinSigma = Math.Sqrt((x * x) + (y * y));
            if (sinSigma == 0)
                return 0;

            var cosSigma = (sinU1 * sinU2) + (cosU1 * cosU2 * cosLambda);
            var sigma = Math.Atan2(sinSigma, cosSigma);
            var sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
            var cosSquaredAlpha = 1 - (sinAlpha * sinAlpha);

            // On the equator, cos²α is 0 and the term does not matter
            var cos2SigmaM = cosSquaredAlpha == 0 ? 0 : cosSigma - (2 * sinU1 * sinU2 / cosSquaredAlpha);
            var c = F / 16 * cosSquaredAlpha * (4 + (F * (4 - (3 * cosSquaredAlpha))));
            var previous = lambda;
            lambda = l + ((1 - c) * F * sinAlpha * (sigma + (c * sinSigma * (cos2SigmaM + (c * cosSigma * (-1 + (2 * cos2SigmaM * cos2SigmaM)))))));
            if (Math.Abs(lambda) > Math.PI)
                break;

            if (Math.Abs(lambda - previous) < 1e-12)
            {
                var uSquared = cosSquaredAlpha * ((A * A) - (B * B)) / (B * B);
                var (a, b) = GetSeriesCoefficients(uSquared);
                var deltaSigma = GetDeltaSigma(b, sinSigma, cosSigma, cos2SigmaM);
                return B * a * (sigma - deltaSigma);
            }
        }

        return from.GetDistance(to);
    }

    /// <summary>Gets the position at a distance from a position, in a direction.</summary>
    /// <param name="from">The start position.</param>
    /// <param name="azimuth">The direction, in degrees clockwise from the north.</param>
    /// <param name="distance">The distance, in meters.</param>
    /// <returns>The position. Its longitude is the longitude of <paramref name="from"/> plus the difference of longitude, so that a shape around a position near the 180th meridian stays in one piece.</returns>
    public static GeoPosition GetDestination(GeoPosition from, double azimuth, double distance)
    {
        var (sinAlpha1, cosAlpha1) = Math.SinCos(azimuth * DegreesToRadians);
        var tanU1 = (1 - F) * Math.Tan(from.Latitude * DegreesToRadians);
        var cosU1 = 1 / Math.Sqrt(1 + (tanU1 * tanU1));
        var sinU1 = tanU1 * cosU1;
        var sigma1 = Math.Atan2(tanU1, cosAlpha1);
        var sinAlpha = cosU1 * sinAlpha1;
        var cosSquaredAlpha = 1 - (sinAlpha * sinAlpha);
        var uSquared = cosSquaredAlpha * ((A * A) - (B * B)) / (B * B);
        var (a, b) = GetSeriesCoefficients(uSquared);

        var sigma = distance / (B * a);
        double sinSigma, cosSigma, cos2SigmaM;
        var iteration = 0;
        while (true)
        {
            cos2SigmaM = Math.Cos((2 * sigma1) + sigma);
            (sinSigma, cosSigma) = Math.SinCos(sigma);
            var previous = sigma;
            sigma = (distance / (B * a)) + GetDeltaSigma(b, sinSigma, cosSigma, cos2SigmaM);
            if (Math.Abs(sigma - previous) < 1e-12 || ++iteration >= 100)
                break;
        }

        (sinSigma, cosSigma) = Math.SinCos(sigma);
        cos2SigmaM = Math.Cos((2 * sigma1) + sigma);
        var x = (sinU1 * sinSigma) - (cosU1 * cosSigma * cosAlpha1);
        var latitude = Math.Atan2((sinU1 * cosSigma) + (cosU1 * sinSigma * cosAlpha1), (1 - F) * Math.Sqrt((sinAlpha * sinAlpha) + (x * x)));
        var lambda = Math.Atan2(sinSigma * sinAlpha1, (cosU1 * cosSigma) - (sinU1 * sinSigma * cosAlpha1));
        var c = F / 16 * cosSquaredAlpha * (4 + (F * (4 - (3 * cosSquaredAlpha))));
        var l = lambda - ((1 - c) * F * sinAlpha * (sigma + (c * sinSigma * (cos2SigmaM + (c * cosSigma * (-1 + (2 * cos2SigmaM * cos2SigmaM)))))));
        return new GeoPosition(from.Longitude + (l / DegreesToRadians), latitude / DegreesToRadians);
    }

    private static (double A, double B) GetSeriesCoefficients(double uSquared)
    {
        var a = 1 + (uSquared / 16384 * (4096 + (uSquared * (-768 + (uSquared * (320 - (175 * uSquared)))))));
        var b = uSquared / 1024 * (256 + (uSquared * (-128 + (uSquared * (74 - (47 * uSquared))))));
        return (a, b);
    }

    private static double GetDeltaSigma(double b, double sinSigma, double cosSigma, double cos2SigmaM)
    {
        var cos2SigmaMSquared = cos2SigmaM * cos2SigmaM;
        return b * sinSigma * (cos2SigmaM + (b / 4 * ((cosSigma * (-1 + (2 * cos2SigmaMSquared))) - (b / 6 * cos2SigmaM * (-3 + (4 * sinSigma * sinSigma)) * (-3 + (4 * cos2SigmaMSquared))))));
    }

    private static double NormalizeRadians(double value) => Math.IEEERemainder(value, 2 * Math.PI);
}
