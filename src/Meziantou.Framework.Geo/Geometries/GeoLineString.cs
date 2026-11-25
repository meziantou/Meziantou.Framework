namespace Meziantou.Framework.Geo;

/// <summary>A line through positions, such as a river, a road or a route.</summary>
public sealed class GeoLineString : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoLineString"/> class.</summary>
    /// <param name="positions">The positions, in order.</param>
    public GeoLineString(IEnumerable<GeoPosition> positions)
    {
        PositionArray = ToArray(positions, nameof(positions));
    }

    private GeoLineString(GeoPosition[] positions, bool _)
    {
        PositionArray = positions;
    }

    /// <summary>Gets the positions, in order.</summary>
    public IReadOnlyList<GeoPosition> Positions => PositionArray;

    /// <summary>Gets the array that <see cref="Positions"/> returns, so that the operations read it without interface calls.</summary>
    internal GeoPosition[] PositionArray { get; }

    /// <summary>Creates a line that keeps an array the caller no longer uses, instead of a copy of it.</summary>
    internal static GeoLineString FromOwnedArray(GeoPosition[] positions) => new(positions, _: true);

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
