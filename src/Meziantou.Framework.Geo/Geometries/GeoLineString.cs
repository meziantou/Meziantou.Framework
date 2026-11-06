namespace Meziantou.Framework.Geo;

/// <summary>A line through positions, such as a river, a road or a route.</summary>
public sealed class GeoLineString : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoLineString"/> class.</summary>
    /// <param name="positions">The positions, in order.</param>
    public GeoLineString(IEnumerable<GeoPosition> positions)
    {
        Positions = ToArray(positions, nameof(positions));
    }

    /// <summary>Gets the positions, in order.</summary>
    public IReadOnlyList<GeoPosition> Positions { get; }

    /// <inheritdoc />
    public override GeoGeometryType Type => GeoGeometryType.LineString;

    /// <summary>Gets the length of the line, in meters: the sum of the great-circle distances between its positions.</summary>
    /// <returns>The length, in meters.</returns>
    public double GetLength()
    {
        var length = 0.0;
        for (var i = 1; i < Positions.Count; i++)
        {
            length += Positions[i - 1].GetDistance(Positions[i]);
        }

        return length;
    }

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => Positions;
}
