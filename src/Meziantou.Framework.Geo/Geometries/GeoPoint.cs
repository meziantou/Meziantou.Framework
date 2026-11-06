namespace Meziantou.Framework.Geo;

/// <summary>A point, such as a city.</summary>
public sealed class GeoPoint : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoPoint"/> class.</summary>
    /// <param name="position">The position.</param>
    public GeoPoint(GeoPosition position)
    {
        Position = position;
    }

    /// <summary>Gets the position.</summary>
    public GeoPosition Position { get; }

    /// <inheritdoc />
    public override GeoGeometryType Type => GeoGeometryType.Point;

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => [Position];
}
