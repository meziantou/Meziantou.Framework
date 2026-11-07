namespace Meziantou.Framework.Geo;

/// <summary>The options of the TopoJSON writer, <see cref="TopoJson"/>.</summary>
public sealed class TopoJsonWriterOptions
{
    /// <summary>Gets or sets a value that indicates whether the JSON is indented. The default is <see langword="false"/>.</summary>
    public bool Indented { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct values of each coordinate, such as <c>10000</c> or <c>100000</c>, or
    /// <see langword="null"/> to write the positions as they are. The default is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// When it is set, the topology gets a <c>transform</c>: the longitudes and latitudes are rounded to a grid of this many
    /// values over the bounds of the features, and the arcs are delta-encoded, which makes the file much smaller. The
    /// positions read back are then on that grid, and the consecutive positions that round to the same value are merged.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 2.</exception>
    public int? Quantization
    {
        get;
        set
        {
            if (value is { } quantization)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(quantization, 2, nameof(value));
            }

            field = value;
        }
    }
}
