namespace Meziantou.Framework.Geo;

/// <summary>An ellipsoid of revolution that models the shape of the Earth, such as the one of WGS 84 or GRS 80.</summary>
public sealed class Ellipsoid
{
    /// <summary>Initializes a new instance of the <see cref="Ellipsoid"/> class.</summary>
    /// <param name="name">The name of the ellipsoid.</param>
    /// <param name="semiMajorAxis">The radius at the equator, in meters.</param>
    /// <param name="inverseFlattening">The inverse of the flattening, <c>a / (a - b)</c>, or 0 for a sphere.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="semiMajorAxis"/> is not a positive number, or <paramref name="inverseFlattening"/> is not 0 or a number greater than 1.</exception>
    public Ellipsoid(string name, double semiMajorAxis, double inverseFlattening)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!double.IsFinite(semiMajorAxis) || semiMajorAxis <= 0)
            throw new ArgumentOutOfRangeException(nameof(semiMajorAxis), semiMajorAxis, "The semi-major axis must be a positive number.");

        if (!double.IsFinite(inverseFlattening) || (inverseFlattening is not 0 && inverseFlattening <= 1))
            throw new ArgumentOutOfRangeException(nameof(inverseFlattening), inverseFlattening, "The inverse flattening must be 0 (a sphere) or a number greater than 1.");

        Name = name;
        SemiMajorAxis = semiMajorAxis;
        InverseFlattening = inverseFlattening;
        Flattening = inverseFlattening is 0 ? 0 : 1 / inverseFlattening;
        EccentricitySquared = Flattening * (2 - Flattening);
        Eccentricity = Math.Sqrt(EccentricitySquared);
    }

    /// <summary>Gets the ellipsoid of WGS 84, used by GPS devices, GeoJSON and web maps.</summary>
    public static Ellipsoid Wgs84 { get; } = new("WGS 84", 6_378_137, 298.257223563);

    /// <summary>Gets the ellipsoid GRS 1980, used by ETRS89, RGF93 and NAD83.</summary>
    public static Ellipsoid Grs80 { get; } = new("GRS 1980", 6_378_137, 298.257222101);

    /// <summary>Gets the ellipsoid Clarke 1866, used by NAD27.</summary>
    public static Ellipsoid Clarke1866 { get; } = new("Clarke 1866", 6_378_206.4, 294.978698213898);

    /// <summary>Gets the ellipsoid Clarke 1880 (IGN), used by the NTF datum of France.</summary>
    public static Ellipsoid Clarke1880Ign { get; } = new("Clarke 1880 (IGN)", 6_378_249.2, 293.466021293627);

    /// <summary>Gets the ellipsoid Airy 1830, used by the OSGB36 datum of Great Britain.</summary>
    public static Ellipsoid Airy1830 { get; } = new("Airy 1830", 6_377_563.396, 299.3249646);

    /// <summary>Gets the International 1924 (Hayford) ellipsoid, used by ED50.</summary>
    public static Ellipsoid International1924 { get; } = new("International 1924", 6_378_388, 297);

    /// <summary>Gets the ellipsoid Bessel 1841, used by the DHDN datum of Germany.</summary>
    public static Ellipsoid Bessel1841 { get; } = new("Bessel 1841", 6_377_397.155, 299.1528128);

    /// <summary>Gets the name of the ellipsoid.</summary>
    public string Name { get; }

    /// <summary>Gets the radius at the equator, in meters.</summary>
    public double SemiMajorAxis { get; }

    /// <summary>Gets the inverse of the flattening, or 0 for a sphere.</summary>
    public double InverseFlattening { get; }

    /// <summary>Gets the flattening, <c>(a - b) / a</c>.</summary>
    public double Flattening { get; }

    /// <summary>Gets the radius at the poles, in meters.</summary>
    public double SemiMinorAxis => SemiMajorAxis * (1 - Flattening);

    /// <summary>Gets the first eccentricity.</summary>
    public double Eccentricity { get; }

    /// <summary>Gets the square of the first eccentricity.</summary>
    public double EccentricitySquared { get; }

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <summary>The isometric latitude ψ, whose hyperbolic sine is the tangent of the conformal latitude.</summary>
    internal double GetIsometricLatitude(double phi) => Math.Asinh(Math.Tan(phi)) - (Eccentricity * Math.Atanh(Eccentricity * Math.Sin(phi)));

    /// <summary>The latitude of an isometric latitude ψ: the inverse of <see cref="GetIsometricLatitude"/>.</summary>
    internal double GetLatitudeFromIsometric(double psi)
    {
        var phi = Math.Atan(Math.Sinh(psi));
        if (Eccentricity is 0)
            return phi;

        // Converges by a factor of about e² per iteration
        for (var i = 0; i < 30; i++)
        {
            var next = Math.Atan(Math.Sinh(psi + (Eccentricity * Math.Atanh(Eccentricity * Math.Sin(phi)))));
            if (Math.Abs(next - phi) < 1e-15)
                return next;

            phi = next;
        }

        return phi;
    }

    internal (double X, double Y, double Z) ToGeocentric(double lambda, double phi, double height)
    {
        var sinPhi = Math.Sin(phi);
        var cosPhi = Math.Cos(phi);
        var n = SemiMajorAxis / Math.Sqrt(1 - (EccentricitySquared * sinPhi * sinPhi));
        return ((n + height) * cosPhi * Math.Cos(lambda), (n + height) * cosPhi * Math.Sin(lambda), ((n * (1 - EccentricitySquared)) + height) * sinPhi);
    }

    internal (double Lambda, double Phi, double Height) FromGeocentric(double x, double y, double z)
    {
        var lambda = Math.Atan2(y, x);
        var p = Math.Sqrt((x * x) + (y * y));
        var phi = Math.Atan2(z, p * (1 - EccentricitySquared));
        double height = 0;
        for (var i = 0; i < 30; i++)
        {
            var sinPhi = Math.Sin(phi);
            var w = Math.Sqrt(1 - (EccentricitySquared * sinPhi * sinPhi));
            var n = SemiMajorAxis / w;

            // Valid at every latitude, the poles included
            height = (p * Math.Cos(phi)) + (z * sinPhi) - (SemiMajorAxis * w);
            var next = Math.Atan2(z, p * (1 - (EccentricitySquared * n / (n + height))));
            if (Math.Abs(next - phi) < 1e-15)
            {
                phi = next;
                break;
            }

            phi = next;
        }

        return (lambda, phi, height);
    }
}
