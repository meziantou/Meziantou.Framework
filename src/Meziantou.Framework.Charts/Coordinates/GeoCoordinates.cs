namespace Meziantou.Framework.Charts;

/// <summary>
/// Geographic coordinates: the x scale gives the longitude and the y scale the latitude, in degrees, and a
/// <see cref="GeoProjection"/> places them on the map, scaled and centered to fit the plot area. Every mark can draw on a
/// map: a <see cref="DotMark{T}"/> places its dots at their longitudes and latitudes.
/// </summary>
/// <remarks>
/// The plot uses geographic coordinates by default when it has a <see cref="GeoMark{T}"/>. The map has no axes: the x
/// and y scales of the plot and <see cref="Coordinates.Transposed"/> are not used.
/// </remarks>
public sealed class GeoCoordinates : Coordinates
{
    private GeoProjection _projection = new EquirectangularProjection();
    private double? _graticuleStep;

    /// <summary>Gets or sets the projection. The default is an <see cref="EquirectangularProjection"/>.</summary>
    public GeoProjection Projection
    {
        get => _projection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _projection = value;
        }
    }

    /// <summary>
    /// Gets or sets the region the map shows, or <see langword="null"/> to fit the shapes of the <see cref="GeoMark{T}"/>
    /// marks and the positions of the other marks. Use <see cref="Plot.Clip"/> to hide what is outside of it.
    /// </summary>
    public GeoBounds? Extent { get; set; }

    /// <summary>Gets or sets the step of the graticule (the meridians and the parallels drawn under the marks), in degrees, or <see langword="null"/> for none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive number.</exception>
    public double? GraticuleStep
    {
        get => _graticuleStep;
        set
        {
            if (value is { } step && !(step > 0 && double.IsFinite(step)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The step must be a positive number.");

            _graticuleStep = value;
        }
    }

    /// <summary>Gets or sets a value that indicates whether the outline of the globe (the edge of the map of the whole world) is drawn.</summary>
    public bool Outline { get; set; }
}
