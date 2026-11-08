namespace Meziantou.Framework.Geo;

/// <summary>
/// The Lambert conformal conic projection: the projection of Lambert 93 and of the Lambert zones of France, and of many
/// state plane systems. With two standard parallels, it is the EPSG method 9802 (2SP), whose scale is true along them;
/// without, it is the EPSG method 9801 (1SP), whose scale is <see cref="ScaleFactor"/> along <see cref="CoordinateConversion.LatitudeOfOrigin"/>.
/// </summary>
/// <remarks>
/// With <see cref="StandardParallel1"/> and <see cref="StandardParallel2"/>, <see cref="CoordinateConversion.LatitudeOfOrigin"/>
/// is the latitude of the false origin; with neither, the cone is tangent along <see cref="CoordinateConversion.LatitudeOfOrigin"/>.
/// The scale factor applies in both cases, as in the definitions of ESRI.
/// </remarks>
public sealed class LambertConformalConicConversion : CoordinateConversion
{
    private Constants? _constants;

    /// <summary>Gets the first standard parallel, in degrees, or <see langword="null"/> to use the latitude of origin. The default is <see langword="null"/>.</summary>
    public double? StandardParallel1 { get; init; }

    /// <summary>Gets the second standard parallel, in degrees, or <see langword="null"/> to use the first one. The default is <see langword="null"/>.</summary>
    public double? StandardParallel2 { get; init; }

    /// <summary>Gets the scale factor. The default is 1.</summary>
    public double ScaleFactor { get; init; } = 1;

    /// <inheritdoc />
    public override (double Easting, double Northing) Project(double lambda, double phi, Ellipsoid ellipsoid)
    {
        ArgumentNullException.ThrowIfNull(ellipsoid);
        var constants = GetConstants(ellipsoid);
        var r = GetRadius(constants, ellipsoid, phi);
        var theta = constants.N * GetLongitudeFromCentralMeridian(lambda);
        return (FalseEasting + (r * Math.Sin(theta)), FalseNorthing + constants.OriginRadius - (r * Math.Cos(theta)));
    }

    /// <inheritdoc />
    public override (double Lambda, double Phi) Unproject(double easting, double northing, Ellipsoid ellipsoid)
    {
        ArgumentNullException.ThrowIfNull(ellipsoid);
        var constants = GetConstants(ellipsoid);
        var sign = Math.Sign(constants.N);
        var dx = sign * (easting - FalseEasting);
        var dy = sign * (constants.OriginRadius - (northing - FalseNorthing));
        var r = Math.Sqrt((dx * dx) + (dy * dy));
        var lambda = (Math.Atan2(dx, dy) / constants.N) + (CentralMeridian * Math.PI / 180);
        if (r is 0)
            return (lambda, sign * Math.PI / 2);

        // r = a k0 F exp(-n ψ)
        var psi = -Math.Log(r / constants.Scale) / constants.N;
        return (lambda, ellipsoid.GetLatitudeFromIsometric(psi));
    }

    private static double GetRadius(Constants constants, Ellipsoid ellipsoid, double phi)
    {
        if (Math.Abs(Math.Abs(phi) - (Math.PI / 2)) < 1e-12)
            return phi * constants.N > 0 ? 0 : double.PositiveInfinity;

        return constants.Scale * Math.Exp(-constants.N * ellipsoid.GetIsometricLatitude(phi));
    }

    private Constants GetConstants(Ellipsoid ellipsoid)
    {
        var constants = _constants;
        if (constants is null || !ReferenceEquals(constants.Ellipsoid, ellipsoid))
        {
            _constants = constants = new Constants(this, ellipsoid);
        }

        return constants;
    }

    private sealed class Constants
    {
        public Constants(LambertConformalConicConversion conversion, Ellipsoid ellipsoid)
        {
            Ellipsoid = ellipsoid;
            var phi1 = (conversion.StandardParallel1 ?? conversion.LatitudeOfOrigin) * Math.PI / 180;
            var phi2 = (conversion.StandardParallel2 ?? conversion.StandardParallel1 ?? conversion.LatitudeOfOrigin) * Math.PI / 180;
            var psi1 = ellipsoid.GetIsometricLatitude(phi1);
            if (Math.Abs(phi1 - phi2) < 1e-12)
            {
                N = Math.Sin(phi1);
            }
            else
            {
                N = (Math.Log(GetM(ellipsoid, phi1)) - Math.Log(GetM(ellipsoid, phi2))) / (ellipsoid.GetIsometricLatitude(phi2) - psi1);
            }

            if (N is 0 || !double.IsFinite(N))
                throw new InvalidOperationException("The standard parallels of a Lambert conformal conic projection must not be symmetric around the equator.");

            // F = m1 / (n t1^n), with t = exp(-ψ)
            var f = GetM(ellipsoid, phi1) * Math.Exp(N * psi1) / N;
            Scale = ellipsoid.SemiMajorAxis * conversion.ScaleFactor * f;
            OriginRadius = GetRadius(this, ellipsoid, conversion.LatitudeOfOrigin * Math.PI / 180);
        }

        public Ellipsoid Ellipsoid { get; }

        public double N { get; }

        /// <summary>a k0 F.</summary>
        public double Scale { get; }

        public double OriginRadius { get; }

        private static double GetM(Ellipsoid ellipsoid, double phi)
        {
            var sinPhi = Math.Sin(phi);
            return Math.Cos(phi) / Math.Sqrt(1 - (ellipsoid.EccentricitySquared * sinPhi * sinPhi));
        }
    }
}
