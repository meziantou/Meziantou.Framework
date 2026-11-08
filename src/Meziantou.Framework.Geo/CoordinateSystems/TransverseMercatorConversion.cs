namespace Meziantou.Framework.Geo;

/// <summary>
/// The transverse Mercator projection (EPSG method 9807), also known as Gauss–Krüger: the projection of UTM and of many
/// national grids, such as the British National Grid. It is conformal and true to scale (times <see cref="ScaleFactor"/>)
/// along its central meridian.
/// </summary>
/// <remarks>
/// The formulas are the series of Krüger to the sixth order in the third flattening, as given by Karney (2011) and used by
/// PROJ: they are accurate to a few nanometers within 3,000 km of the central meridian.
/// </remarks>
public sealed class TransverseMercatorConversion : CoordinateConversion
{
    private Constants? _constants;

    /// <summary>Gets the scale factor on the central meridian, such as 0.9996 for UTM. The default is 1.</summary>
    public double ScaleFactor { get; init; } = 1;

    /// <inheritdoc />
    public override (double Easting, double Northing) Project(double lambda, double phi, Ellipsoid ellipsoid)
    {
        ArgumentNullException.ThrowIfNull(ellipsoid);
        var constants = GetConstants(ellipsoid);
        lambda = GetLongitudeFromCentralMeridian(lambda);

        // The conformal latitude, then the Gauss–Schreiber transverse Mercator (ξ', η')
        var tau = Math.Sinh(ellipsoid.GetIsometricLatitude(phi));
        var cosLambda = Math.Cos(lambda);
        var xiPrime = Math.Atan2(tau, cosLambda);
        var etaPrime = Math.Asinh(Math.Sin(lambda) / Math.Sqrt((tau * tau) + (cosLambda * cosLambda)));

        var xi = xiPrime;
        var eta = etaPrime;
        for (var j = 1; j <= 6; j++)
        {
            xi += constants.Alpha[j - 1] * Math.Sin(2 * j * xiPrime) * Math.Cosh(2 * j * etaPrime);
            eta += constants.Alpha[j - 1] * Math.Cos(2 * j * xiPrime) * Math.Sinh(2 * j * etaPrime);
        }

        var k0A = ScaleFactor * constants.RectifyingRadius;
        return (FalseEasting + (k0A * eta), FalseNorthing + (k0A * xi) - constants.OriginNorthing);
    }

    /// <inheritdoc />
    public override (double Lambda, double Phi) Unproject(double easting, double northing, Ellipsoid ellipsoid)
    {
        ArgumentNullException.ThrowIfNull(ellipsoid);
        var constants = GetConstants(ellipsoid);
        var k0A = ScaleFactor * constants.RectifyingRadius;
        var xi = (northing - FalseNorthing + constants.OriginNorthing) / k0A;
        var eta = (easting - FalseEasting) / k0A;

        var xiPrime = xi;
        var etaPrime = eta;
        for (var j = 1; j <= 6; j++)
        {
            xiPrime -= constants.Beta[j - 1] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            etaPrime -= constants.Beta[j - 1] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }

        var sinhEta = Math.Sinh(etaPrime);
        var cosXi = Math.Cos(xiPrime);
        var lambda = Math.Atan2(sinhEta, cosXi);
        var tauPrime = Math.Sin(xiPrime) / Math.Sqrt((sinhEta * sinhEta) + (cosXi * cosXi));
        var phi = ellipsoid.GetLatitudeFromIsometric(Math.Asinh(tauPrime));
        return (lambda + (CentralMeridian * Math.PI / 180), phi);
    }

    private Constants GetConstants(Ellipsoid ellipsoid)
    {
        var constants = _constants;
        if (constants is null || !ReferenceEquals(constants.Ellipsoid, ellipsoid))
        {
            _constants = constants = new Constants(ellipsoid, LatitudeOfOrigin * Math.PI / 180, ScaleFactor);
        }

        return constants;
    }

    private sealed class Constants
    {
        public Constants(Ellipsoid ellipsoid, double latitudeOfOrigin, double scaleFactor)
        {
            Ellipsoid = ellipsoid;
            var n = ellipsoid.Flattening / (2 - ellipsoid.Flattening);
            var n2 = n * n;
            var n3 = n2 * n;
            var n4 = n3 * n;
            var n5 = n4 * n;
            var n6 = n5 * n;
            RectifyingRadius = ellipsoid.SemiMajorAxis / (1 + n) * (1 + (n2 / 4) + (n4 / 64) + (n6 / 256));
            Alpha =
            [
                (n / 2) - (2 * n2 / 3) + (5 * n3 / 16) + (41 * n4 / 180) - (127 * n5 / 288) + (7891 * n6 / 37800),
                (13 * n2 / 48) - (3 * n3 / 5) + (557 * n4 / 1440) + (281 * n5 / 630) - (1983433 * n6 / 1935360),
                (61 * n3 / 240) - (103 * n4 / 140) + (15061 * n5 / 26880) + (167603 * n6 / 181440),
                (49561 * n4 / 161280) - (179 * n5 / 168) + (6601661 * n6 / 7257600),
                (34729 * n5 / 80640) - (3418889 * n6 / 1995840),
                212378941 * n6 / 319334400,
            ];
            Beta =
            [
                (n / 2) - (2 * n2 / 3) + (37 * n3 / 96) - (n4 / 360) - (81 * n5 / 512) + (96199 * n6 / 604800),
                (n2 / 48) + (n3 / 15) - (437 * n4 / 1440) + (46 * n5 / 105) - (1118711 * n6 / 3870720),
                (17 * n3 / 480) - (37 * n4 / 840) - (209 * n5 / 4480) + (5569 * n6 / 90720),
                (4397 * n4 / 161280) - (11 * n5 / 504) - (830251 * n6 / 7257600),
                (4583 * n5 / 161280) - (108847 * n6 / 3991680),
                20648693 * n6 / 638668800,
            ];

            // The northing of the latitude of origin on the central meridian: the rectifying latitude, from the conformal one
            var chi = Math.Atan(Math.Sinh(ellipsoid.GetIsometricLatitude(latitudeOfOrigin)));
            var mu = chi;
            for (var j = 1; j <= 6; j++)
            {
                mu += Alpha[j - 1] * Math.Sin(2 * j * chi);
            }

            OriginNorthing = scaleFactor * RectifyingRadius * mu;
        }

        public Ellipsoid Ellipsoid { get; }

        public double RectifyingRadius { get; }

        public double[] Alpha { get; }

        public double[] Beta { get; }

        public double OriginNorthing { get; }
    }
}
