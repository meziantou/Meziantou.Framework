namespace Meziantou.Framework.Geo;

/// <summary>The options of <see cref="GeoGeometry.Buffer(double, GeoBufferOptions?)"/>.</summary>
public sealed class GeoBufferOptions
{
    private int _segmentsPerQuarterCircle = 8;
    private double _miterLimit = 5;

    /// <summary>Gets or sets the number of straight segments that make a quarter of a circle, for the circles around points and the round joins and ends. The default is 8.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 1,000.</exception>
    public int SegmentsPerQuarterCircle
    {
        get => _segmentsPerQuarterCircle;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _segmentsPerQuarterCircle = value;
        }
    }

    /// <summary>Gets or sets the shape of the buffer around the vertices where a line or a ring turns. The default is <see cref="GeoBufferJoinStyle.Round"/>.</summary>
    public GeoBufferJoinStyle JoinStyle { get; set; }

    /// <summary>Gets or sets the shape of the buffer at the ends of the lines. The default is <see cref="GeoBufferEndCapStyle.Round"/>. The buffer of a point is a circle whatever the style.</summary>
    public GeoBufferEndCapStyle EndCapStyle { get; set; }

    /// <summary>Gets or sets the largest distance between a vertex and the corner of a <see cref="GeoBufferJoinStyle.Miter"/> join, as a multiple of the distance of the buffer. The default is 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1, or not a finite number.</exception>
    public double MiterLimit
    {
        get => _miterLimit;
        set
        {
            if (!(value >= 1) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The miter limit must be a finite number that is at least 1.");

            _miterLimit = value;
        }
    }
}
