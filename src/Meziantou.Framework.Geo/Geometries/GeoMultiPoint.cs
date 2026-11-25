namespace Meziantou.Framework.Geo;

/// <summary>A set of points.</summary>
public sealed class GeoMultiPoint : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoMultiPoint"/> class.</summary>
    /// <param name="positions">The positions of the points.</param>
    public GeoMultiPoint(IEnumerable<GeoPosition> positions)
    {
        PositionArray = ToArray(positions, nameof(positions));
    }

    /// <summary>Gets the positions of the points.</summary>
    public IReadOnlyList<GeoPosition> Positions => PositionArray;

    /// <summary>Gets the array that <see cref="Positions"/> returns, so that the operations read it without interface calls.</summary>
    internal GeoPosition[] PositionArray { get; }

    /// <inheritdoc />
    public override GeoGeometryType Type => GeoGeometryType.MultiPoint;

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => Positions;
}
