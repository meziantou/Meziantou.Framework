namespace Meziantou.Framework.Geo;

/// <summary>
/// The azimuthal equidistant projection, in its polar aspect: the map of a polar region, such as Antarctica or the
/// Arctic, centered on the North Pole, or on the South Pole with <see cref="SouthPole"/>. The distances from the pole
/// are true, the meridians are straight lines from the pole and the parallels are circles around it.
/// </summary>
/// <remarks>
/// The whole world is a disc of radius π, whose edge is the opposite pole: the regions far from the pole are stretched
/// along the parallels. On the map of the North Pole, the <see cref="CentralMeridian"/> points down from the pole; on the
/// map of the South Pole, it points up.
/// </remarks>
public sealed class AzimuthalEquidistantProjection : GeoProjection
{
    /// <summary>Gets or sets a value that indicates whether the map is centered on the South Pole instead of the North Pole. The default is <see langword="false"/>.</summary>
    public bool SouthPole { get; set; }

    /// <summary>Gets or sets the meridian that points along the vertical axis from the pole, in degrees. The default is 0.</summary>
    public double CentralMeridian { get; set; }

    /// <inheritdoc />
    public override double CenterLongitude => CentralMeridian;

    /// <inheritdoc />
    public override bool HasStraightParallels => false;

    /// <inheritdoc />
    protected override (double X, double Y) ProjectRadians(double lambda, double phi)
    {
        // The distance from the pole along the meridian, and the angle from the central meridian
        var theta = lambda - (CentralMeridian * Math.PI / 180);
        if (SouthPole)
        {
            var distance = (Math.PI / 2) + phi;
            return (distance * Math.Sin(theta), distance * Math.Cos(theta));
        }

        var rho = (Math.PI / 2) - phi;
        return (rho * Math.Sin(theta), -rho * Math.Cos(theta));
    }

    /// <inheritdoc />
    protected override (double Lambda, double Phi)? InvertRadians(double x, double y)
    {
        var rho = Math.Sqrt((x * x) + (y * y));
        if (rho > Math.PI + 1e-9)
            return null;

        // At the pole, every longitude is the same position: the central meridian is the one returned
        var centralMeridian = CentralMeridian * Math.PI / 180;
        if (rho == 0)
            return (centralMeridian, SouthPole ? -Math.PI / 2 : Math.PI / 2);

        return SouthPole
            ? (Math.Atan2(x, y) + centralMeridian, rho - (Math.PI / 2))
            : (Math.Atan2(x, -y) + centralMeridian, (Math.PI / 2) - rho);
    }
}
